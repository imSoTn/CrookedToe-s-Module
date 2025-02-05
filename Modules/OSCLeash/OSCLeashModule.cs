using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Parameters;
using VRCOSC.App.SDK.VRChat;
using Valve.VR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace VRCOSC.Modules.OSCLeash;

[ModuleTitle("OSC Leash")]
[ModuleDescription("Allows for controlling avatar movement with parameters, including vertical movement via OpenVR")]
[ModuleType(ModuleType.Generic)]
[ModulePrefab("OSCLeash", "https://github.com/CrookedToe/OSCLeash/tree/main/Unity")]
[ModuleInfo("https://github.com/CrookedToe/CrookedToe-s-Modules")]
public class OSCLeashModule : Module
{
    // Movement state
    private readonly MovementState state = new();
    
    // OpenVR state
    private float currentVerticalOffset;
    private float targetVerticalOffset;
    private float verticalVelocity;
    private bool wasOpenVRAvailable;
    private ETrackingUniverseOrigin originalTrackingOrigin;
    private CVRCompositor? compositor;
    
    // Movement smoothing
    private readonly MovementSmoother verticalSmoother = new(8);
    private readonly MovementSmoother horizontalSmoother = new(8);
    
    // Performance optimization
    private int frameCounter;
    private const int FrameSkip = 1;
    private const float UpdateThreshold = 0.025f;
    private Vector3 lastMovement;
    private bool needsMovementUpdate;
    
    // Physics constants
    private static class PhysicsConstants
    {
        public const float GRAVITY = 9.81f;
        public const float TERMINAL_VELOCITY = -15.0f;
        public const float VERTICAL_SMOOTHING = 0.95f;
    }
    
    private class MovementState
    {
        public bool IsGrabbed { get; set; }
        public float Stretch { get; set; }
        public float ZPositive { get; set; }
        public float ZNegative { get; set; }
        public float XPositive { get; set; }
        public float XNegative { get; set; }
        public float YPositive { get; set; }
        public float YNegative { get; set; }
        public bool IsVerticalMovementEnabled { get; set; }
        
        public float GetVerticalStretch() => Math.Abs(YPositive - YNegative);
        public float GetHorizontalStretch() => Math.Max(Math.Abs(XPositive - XNegative), Math.Abs(ZPositive - ZNegative));
    }
    
    private readonly struct Vector3
    {
        public readonly float X, Y, Z;
        
        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
        
        public float DistanceTo(Vector3 other)
        {
            float dx = X - other.X;
            float dy = Y - other.Y;
            float dz = Z - other.Z;
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
    
    private class MovementSmoother
    {
        private readonly Queue<float> buffer;
        private readonly int maxSize;
        private float lastValue;
        public static float SMOOTHING_WEIGHT_DECAY = 0.8f;
        public static float MIN_MOVEMENT_DELTA = 0.001f;
        public static int SMOOTHING_BUFFER_SIZE = 8;
        
        public MovementSmoother(int size)
        {
            maxSize = size;
            buffer = new Queue<float>(size);
        }
        
        public float Smooth(float newValue)
        {
            if (Math.Abs(newValue - lastValue) < MIN_MOVEMENT_DELTA)
                return lastValue;
                
            buffer.Enqueue(newValue);
            if (buffer.Count > maxSize)
                buffer.Dequeue();
            
            float sum = 0;
            float weight = 1;
            float totalWeight = 0;
            
            foreach (var value in buffer.Reverse())
            {
                sum += value * weight;
                totalWeight += weight;
                weight *= SMOOTHING_WEIGHT_DECAY;
            }
            
            lastValue = sum / totalWeight;
            return lastValue;
        }
        
        public void Clear()
        {
            buffer.Clear();
            lastValue = 0;
        }
    }
    
    private enum LeashDirection
    {
        North,
        South,
        East,
        West
    }
    
    private enum OSCLeashParameter
    {
        ZPositive,
        ZNegative,
        XPositive,
        XNegative,
        YPositive,
        YNegative,
        IsGrabbed,
        Stretch
    }
    
    private enum OSCLeashSetting
    {
        LeashDirection,
        RunDeadzone,
        WalkDeadzone,
        StrengthMultiplier,
        UpDownCompensation,
        UpDownDeadzone,
        TurningEnabled,
        TurningMultiplier,
        TurningDeadzone,
        TurningGoal,
        VerticalMovementEnabled,
        VerticalMovementMultiplier,
        VerticalMovementDeadzone,
        VerticalMovementSmoothing,
        VerticalStepMultiplier,
        VerticalHorizontalCompensation
    }
    
    protected override void OnPreLoad()
    {
        // Create settings
        CreateDropdown(OSCLeashSetting.LeashDirection, "Leash Direction", "Direction the leash faces", LeashDirection.North);
        CreateSlider(OSCLeashSetting.RunDeadzone, "Run Deadzone", "Stretch threshold for running", 0.70f, 0.0f, 1.0f);
        CreateSlider(OSCLeashSetting.WalkDeadzone, "Walk Deadzone", "Stretch threshold for walking", 0.15f, 0.0f, 1.0f);
        CreateSlider(OSCLeashSetting.StrengthMultiplier, "Strength Multiplier", "Movement strength multiplier", 1.2f, 0.1f, 5.0f);
        CreateSlider(OSCLeashSetting.UpDownCompensation, "Up/Down Compensation", "Compensation for vertical movement", 1.0f, 0.0f, 2.0f);
        CreateSlider(OSCLeashSetting.UpDownDeadzone, "Up/Down Deadzone", "Vertical angle deadzone", 0.5f, 0.0f, 1.0f);
        
        CreateToggle(OSCLeashSetting.TurningEnabled, "Enable Turning", "Enable turning control with the leash", false);
        CreateSlider(OSCLeashSetting.TurningMultiplier, "Turning Multiplier", "Turning speed multiplier", 0.80f, 0.1f, 2.0f);
        CreateSlider(OSCLeashSetting.TurningDeadzone, "Turning Deadzone", "Minimum stretch required for turning", 0.15f, 0.0f, 1.0f);
        CreateSlider(OSCLeashSetting.TurningGoal, "Turning Goal", "Maximum turning angle in degrees", 90f, 0.0f, 180.0f);
        
        // Vertical movement settings
        CreateToggle(OSCLeashSetting.VerticalMovementEnabled, "Enable Vertical Movement", "Enable vertical movement control via OpenVR", false);
        CreateSlider(OSCLeashSetting.VerticalMovementMultiplier, "Vertical Movement Multiplier", "Vertical movement speed multiplier", 1.0f, 0.1f, 5.0f);
        CreateSlider(OSCLeashSetting.VerticalMovementDeadzone, "Vertical Movement Deadzone", "Minimum stretch for vertical movement", 0.15f, 0.0f, 1.0f, 0.05f);
        CreateSlider(OSCLeashSetting.VerticalMovementSmoothing, "Vertical Movement Smoothing", "Smoothing factor for vertical movement (higher = smoother)", 0.8f, 0.0f, 1.0f);
        CreateSlider(OSCLeashSetting.VerticalStepMultiplier, "Vertical Step Multiplier", "Multiplier for dynamic step size based on vertical stretch (larger = bigger steps)", 0.01f, 0.001f, 0.1f, 0.001f);
        CreateSlider(OSCLeashSetting.VerticalHorizontalCompensation, "Horizontal Movement Compensation", "Reduces vertical movement when moving horizontally (higher = more reduction)", 1.0f, 0.0f, 2.0f);
        
        // Register parameters
        RegisterParameter<bool>(OSCLeashParameter.IsGrabbed, "Leash_IsGrabbed", ParameterMode.Read, "Leash Grabbed", "Physbone grab state");
        RegisterParameter<float>(OSCLeashParameter.Stretch, "Leash_Stretch", ParameterMode.Read, "Leash Stretch", "Physbone stretch value");
        RegisterParameter<float>(OSCLeashParameter.ZPositive, "Leash_ZPositive", ParameterMode.Read, "Forward Direction", "Forward movement value", false);
        RegisterParameter<float>(OSCLeashParameter.ZNegative, "Leash_ZNegative", ParameterMode.Read, "Backward Direction", "Backward movement value", false);
        RegisterParameter<float>(OSCLeashParameter.XPositive, "Leash_XPositive", ParameterMode.Read, "Right Direction", "Right movement value", false);
        RegisterParameter<float>(OSCLeashParameter.XNegative, "Leash_XNegative", ParameterMode.Read, "Left Direction", "Left movement value", false);
        RegisterParameter<float>(OSCLeashParameter.YPositive, "Leash_YPositive", ParameterMode.Read, "Up Direction", "Upward movement value", false);
        RegisterParameter<float>(OSCLeashParameter.YNegative, "Leash_YNegative", ParameterMode.Read, "Down Direction", "Downward movement value", false);
    }
    
    protected override Task<bool> OnModuleStart()
    {
        state.IsVerticalMovementEnabled = GetSettingValue<bool>(OSCLeashSetting.VerticalMovementEnabled);
        if (state.IsVerticalMovementEnabled)
        {
            InitializeOpenVR();
        }
        return Task.FromResult(true);
    }
    
    protected override Task OnModuleStop()
    {
        if (GetOVRClient().HasInitialised && compositor != null)
        {
            compositor.SetTrackingSpace(originalTrackingOrigin);
            compositor = null;
        }
        return Task.CompletedTask;
    }
    
    private void InitializeOpenVR()
    {
        if (!GetOVRClient().HasInitialised)
        {
            Log("OpenVR is not initialized. Vertical movement will be disabled until OpenVR is available.");
            wasOpenVRAvailable = false;
            return;
        }
        
        wasOpenVRAvailable = true;
        compositor = OpenVR.Compositor;
        if (compositor != null)
        {
            originalTrackingOrigin = compositor.GetTrackingSpace();
            UpdateOVRASOffset();
        }
    }
    
    [ModuleUpdate(ModuleUpdateMode.Custom, true, 33)]
    private void UpdateMovement()
    {
        if (frameCounter++ % (FrameSkip + 1) != 0)
            return;
            
        if (GetOVRClient().HasInitialised != wasOpenVRAvailable)
        {
            InitializeOpenVR();
        }
        
        var deltaTime = 1f / 30f;
        UpdateVerticalMovementIfNeeded(deltaTime);
        UpdateHorizontalMovementIfNeeded(deltaTime);
    }
    
    private void UpdateVerticalMovementIfNeeded(float deltaTime)
    {
        if (!state.IsVerticalMovementEnabled || !GetOVRClient().HasInitialised)
            return;
            
        if (!needsMovementUpdate && !state.IsGrabbed)
            return;
            
        UpdateVerticalOffset(deltaTime);
    }
    
    private void UpdateHorizontalMovementIfNeeded(float deltaTime)
    {
        var player = GetPlayer();
        if (player == null)
            return;
            
        var newMovement = CalculateMovement();
        if (!needsMovementUpdate && lastMovement.DistanceTo(newMovement) < UpdateThreshold)
        {
            if (!state.IsGrabbed)
            {
                ResetMovement(player);
            }
            return;
        }
        
        ApplyMovement(player, newMovement);
        lastMovement = newMovement;
        needsMovementUpdate = false;
    }
    
    private Vector3 CalculateMovement()
    {
        if (!state.IsGrabbed)
            return new Vector3(0, 0, 0);
            
        var strengthMultiplier = GetSettingValue<float>(OSCLeashSetting.StrengthMultiplier);
        var outputMultiplier = state.Stretch * strengthMultiplier;
        
        var vertical = verticalSmoother.Smooth((state.ZPositive - state.ZNegative) * outputMultiplier);
        var horizontal = horizontalSmoother.Smooth((state.XPositive - state.XNegative) * outputMultiplier);
        var upDown = state.YPositive + state.YNegative;
        
        return new Vector3(horizontal, upDown, vertical);
    }
    
    private void ResetMovement(Player player)
    {
        player.StopRun();
        player.MoveVertical(0);
        player.MoveHorizontal(0);
        player.LookHorizontal(0);
        verticalSmoother.Clear();
        horizontalSmoother.Clear();
    }
    
    private void ApplyMovement(Player player, Vector3 movement)
    {
        var upDownDeadzone = GetSettingValue<float>(OSCLeashSetting.UpDownDeadzone);
        if (movement.Y >= upDownDeadzone)
        {
            ResetMovement(player);
            return;
        }
        
        var upDownCompensation = GetSettingValue<float>(OSCLeashSetting.UpDownCompensation);
        var yModifier = upDownCompensation != 0 ? 
            Clamp(1.0f - (movement.Y * upDownCompensation)) : 1.0f;
            
        var vertical = yModifier != 0 ? movement.Z / yModifier : movement.Z;
        var horizontal = yModifier != 0 ? movement.X / yModifier : movement.X;
        
        var runDeadzone = GetSettingValue<float>(OSCLeashSetting.RunDeadzone);
        if (state.Stretch > runDeadzone)
            player.Run();
        else
            player.StopRun();
            
        player.MoveVertical(vertical);
        player.MoveHorizontal(horizontal);
        
        UpdateTurning(player, movement);
    }
    
    private void UpdateTurning(Player player, Vector3 movement)
    {
        var turningEnabled = GetSettingValue<bool>(OSCLeashSetting.TurningEnabled);
        if (!turningEnabled || state.Stretch <= GetSettingValue<float>(OSCLeashSetting.TurningDeadzone))
        {
            player.LookHorizontal(0);
            return;
        }
        
        player.LookHorizontal(CalculateTurningOutput(movement));
    }
    
    protected override void OnRegisteredParameterReceived(RegisteredParameter parameter)
    {
        needsMovementUpdate = true;
        switch (parameter.Lookup)
        {
            case OSCLeashParameter.IsGrabbed:
                state.IsGrabbed = parameter.GetValue<bool>();
                break;
            case OSCLeashParameter.Stretch:
                state.Stretch = parameter.GetValue<float>();
                break;
            case OSCLeashParameter.ZPositive:
                state.ZPositive = parameter.GetValue<float>();
                break;
            case OSCLeashParameter.ZNegative:
                state.ZNegative = parameter.GetValue<float>();
                break;
            case OSCLeashParameter.XPositive:
                state.XPositive = parameter.GetValue<float>();
                break;
            case OSCLeashParameter.XNegative:
                state.XNegative = parameter.GetValue<float>();
                break;
            case OSCLeashParameter.YPositive:
                state.YPositive = parameter.GetValue<float>();
                break;
            case OSCLeashParameter.YNegative:
                state.YNegative = parameter.GetValue<float>();
                break;
        }
    }
    
    // Helper Methods
    private float SmoothValue(Queue<float> buffer, float newValue)
    {
        buffer.Enqueue(newValue);
        if (buffer.Count > MovementSmoother.SMOOTHING_BUFFER_SIZE)
            buffer.Dequeue();
        
        float sum = 0;
        float weight = 1;
        float totalWeight = 0;
        
        foreach (var value in buffer.Reverse())
        {
            sum += value * weight;
            totalWeight += weight;
            weight *= MovementSmoother.SMOOTHING_WEIGHT_DECAY;
        }
        return sum / totalWeight;
    }
    
    // OpenVR Update Methods
    private void UpdateOVRASOffset()
    {
        if (!GetSettingValue<bool>(OSCLeashSetting.VerticalMovementEnabled) || !GetOVRClient().HasInitialised)
            return;
        
        try
        {
            var chaperoneSetup = OpenVR.ChaperoneSetup;
            if (chaperoneSetup != null)
            {
                var standingZeroPose = new HmdMatrix34_t();
                chaperoneSetup.GetWorkingStandingZeroPoseToRawTrackingPose(ref standingZeroPose);
                targetVerticalOffset = standingZeroPose.m7;
            }
        }
        catch (Exception ex)
        {
            Log($"Error reading OVRAS offset: {ex.Message}");
            targetVerticalOffset = 0;
        }
    }
    
    private void UpdateVerticalOffset(float deltaTime)
    {
        if (!ShouldUpdateVerticalOffset())
            return;
        
        try
        {
            var chaperoneSetup = OpenVR.ChaperoneSetup;
            if (chaperoneSetup == null)
                return;
            
            var standingZeroPose = GetCurrentStandingPose(chaperoneSetup);
            var stepSize = CalculateStepSize();
            var newOffset = CalculateNewVerticalOffset(deltaTime, stepSize);
            
            if (ShouldApplyVerticalUpdate(newOffset, stepSize))
                ApplyVerticalOffset(chaperoneSetup, standingZeroPose, newOffset);
        }
        catch (Exception ex)
        {
            Log($"Error updating vertical offset: {ex.Message}");
        }
    }
    
    private bool ShouldUpdateVerticalOffset()
    {
        return GetSettingValue<bool>(OSCLeashSetting.VerticalMovementEnabled) && GetOVRClient().HasInitialised;
    }
    
    private HmdMatrix34_t GetCurrentStandingPose(CVRChaperoneSetup chaperoneSetup)
    {
        var standingZeroPose = new HmdMatrix34_t();
        chaperoneSetup.GetWorkingStandingZeroPoseToRawTrackingPose(ref standingZeroPose);
        return standingZeroPose;
    }
    
    private float CalculateStepSize()
    {
        float verticalStretch = Math.Abs(state.YPositive - state.YNegative);
        float baseStepMultiplier = GetSettingValue<float>(OSCLeashSetting.VerticalStepMultiplier);
        float stepSize = baseStepMultiplier * (1.0f + verticalStretch);
        
        float horizontalStretch = Math.Max(Math.Abs(state.XPositive - state.XNegative), Math.Abs(state.ZPositive - state.ZNegative));
        float horizontalCompensation = GetSettingValue<float>(OSCLeashSetting.VerticalHorizontalCompensation);
        return stepSize * (1.0f - (horizontalStretch * horizontalCompensation));
    }
    
    private float CalculateNewVerticalOffset(float deltaTime, float stepSize)
    {
        if (!state.IsGrabbed)
            return CalculateFallingOffset(deltaTime, stepSize);
        
        return CalculateGrabbedOffset(deltaTime, stepSize);
    }
    
    private float CalculateFallingOffset(float deltaTime, float stepSize)
    {
        if (currentVerticalOffset == 0)
        {
            verticalVelocity = 0;
            return 0;
        }
        
        float gravityDirection = currentVerticalOffset > 0 ? -1 : 1;
        verticalVelocity = Math.Clamp(verticalVelocity + PhysicsConstants.GRAVITY * gravityDirection * deltaTime * 2f,
            PhysicsConstants.TERMINAL_VELOCITY, -PhysicsConstants.TERMINAL_VELOCITY);
        float newOffset = currentVerticalOffset + verticalVelocity * deltaTime;
        
        if ((currentVerticalOffset > 0 && newOffset <= 0) || (currentVerticalOffset < 0 && newOffset >= 0))
        {
            verticalVelocity = 0;
            return 0;
        }
        
        return (float)(Math.Floor(newOffset / stepSize) * stepSize);
    }
    
    private float CalculateGrabbedOffset(float deltaTime, float stepSize)
    {
        var verticalDeadzone = GetSettingValue<float>(OSCLeashSetting.VerticalMovementDeadzone);
        if (state.Stretch <= verticalDeadzone)
        {
            verticalVelocity = 0;
            return currentVerticalOffset;
        }
        
        float horizontalCombined = Math.Max(Math.Abs(state.XPositive - state.XNegative), Math.Abs(state.ZPositive - state.ZNegative));
        float horizontalDeadzone = GetSettingValue<float>(OSCLeashSetting.VerticalHorizontalCompensation);
        if (horizontalCombined >= horizontalDeadzone)
        {
            verticalVelocity = 0;
            return currentVerticalOffset;
        }
        
        var verticalDelta = (state.YNegative - state.YPositive) * GetSettingValue<float>(OSCLeashSetting.VerticalMovementMultiplier) * 5f *
                            (1.0f - (horizontalCombined * horizontalDeadzone));
        targetVerticalOffset += verticalDelta * deltaTime;
        targetVerticalOffset = (float)(Math.Floor(targetVerticalOffset / stepSize) * stepSize);
        
        var newOffset = currentVerticalOffset * PhysicsConstants.VERTICAL_SMOOTHING + targetVerticalOffset * (1 - PhysicsConstants.VERTICAL_SMOOTHING);
        verticalVelocity = 0;
        return (float)(Math.Floor(newOffset / stepSize) * stepSize);
    }
    
    private bool ShouldApplyVerticalUpdate(float newOffset, float stepSize)
    {
        return Math.Abs(newOffset - currentVerticalOffset) >= stepSize;
    }
    
    private void ApplyVerticalOffset(CVRChaperoneSetup chaperoneSetup, HmdMatrix34_t standingZeroPose, float newOffset)
    {
        currentVerticalOffset = newOffset;
        standingZeroPose.m7 = currentVerticalOffset;
        chaperoneSetup.SetWorkingStandingZeroPoseToRawTrackingPose(ref standingZeroPose);
        chaperoneSetup.CommitWorkingCopy(EChaperoneConfigFile.Live);
    }
    
    private float CalculateTurningOutput(Vector3 movement)
    {
        var turningMultiplier = GetSettingValue<float>(OSCLeashSetting.TurningMultiplier);
        var turningGoal = Math.Max(0f, GetSettingValue<float>(OSCLeashSetting.TurningGoal)) / 180f;
        var direction = GetSettingValue<LeashDirection>(OSCLeashSetting.LeashDirection);
        float turningOutput = 0f;
        
        switch (direction)
        {
            case LeashDirection.North:
                if (movement.Z < turningGoal)
                {
                    turningOutput = movement.X * turningMultiplier;
                    turningOutput += (movement.X > 0) ? -movement.X : movement.X;
                }
                break;
            case LeashDirection.South:
                if (-movement.Z < turningGoal)
                {
                    turningOutput = -movement.X * turningMultiplier;
                    turningOutput += (movement.X > 0) ? -movement.X : movement.X;
                }
                break;
            case LeashDirection.East:
                if (movement.X < turningGoal)
                {
                    turningOutput = movement.Z * turningMultiplier;
                    turningOutput += (movement.Z > 0) ? -movement.Z : movement.Z;
                }
                break;
            case LeashDirection.West:
                if (-movement.X < turningGoal)
                {
                    turningOutput = -movement.Z * turningMultiplier;
                    turningOutput += (movement.Z > 0) ? -movement.Z : movement.Z;
                }
                break;
        }
        
        return Clamp(turningOutput);
    }
    
    private static float Clamp(float value)
    {
        return Math.Max(-1.0f, Math.Min(value, 1.0f));
    }
} 