using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Parameters;
using VRCOSC.App.SDK.VRChat;
using System.Diagnostics;
using Valve.VR;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace VRCOSC.Modules.OSCLeash;

[ModuleTitle("OSC Leash")]
[ModuleDescription("Allows for controlling avatar movement with parameters, including vertical movement via OpenVR")]
[ModuleType(ModuleType.Generic)]
[ModulePrefab("OSCLeash", "https://github.com/CrookedToe/OSCLeash/tree/main/Unity")]
[ModuleInfo("https://github.com/CrookedToe/CrookedToe-s-Modules")]
public class OSCLeashModule : Module
{
    private bool isGrabbed;
    private float stretch;
    private float zPositive;
    private float zNegative;
    private float xPositive;
    private float xNegative;
    private float yPositive;
    private float yNegative;
    
    // OpenVR variables
    private CVRSystem vrSystem;
    private float currentVerticalOffset;
    private float targetVerticalOffset;
    private float verticalVelocity;
    private readonly Queue<float> verticalOffsetSmoothingBuffer = new(8);
    private bool isVRInitialized;
    private ETrackingUniverseOrigin originalTrackingOrigin;
    
    // Input smoothing with larger buffer for more stability
    private readonly Queue<float> verticalSmoothingBuffer = new(8);
    private readonly Queue<float> horizontalSmoothingBuffer = new(8);
    private const int SmoothingBufferSize = 8;

    // Frame skipping for performance
    private int frameCounter;
    private const int FrameSkip = 1; // Process every other frame
    
    // Batch update threshold
    private const float UpdateThreshold = 0.025f; // Increased threshold for less frequent updates

    // Physics constants
    private static class Constants
    {
        public const float GRAVITY = 9.81f;
        public const float TERMINAL_VELOCITY = -15.0f;
        public const float VERTICAL_SMOOTHING = 0.95f;
        public const float SMOOTHING_WEIGHT_DECAY = 0.8f;
        public const int SMOOTHING_BUFFER_SIZE = 8;
        public const int FRAME_SKIP = 1;
        public const float UPDATE_THRESHOLD = 0.025f;
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

        // Register physbone state parameters
        RegisterParameter<bool>(OSCLeashParameter.IsGrabbed, "Leash_IsGrabbed", ParameterMode.Read, "Leash Grabbed", "Physbone grab state");
        RegisterParameter<float>(OSCLeashParameter.Stretch, "Leash_Stretch", ParameterMode.Read, "Leash Stretch", "Physbone stretch value");
        
        // Direction parameters
        RegisterParameter<float>(OSCLeashParameter.ZPositive, "Leash_ZPositive", ParameterMode.Read, "Forward Direction", "Forward movement value", false);
        RegisterParameter<float>(OSCLeashParameter.ZNegative, "Leash_ZNegative", ParameterMode.Read, "Backward Direction", "Backward movement value", false);
        RegisterParameter<float>(OSCLeashParameter.XPositive, "Leash_XPositive", ParameterMode.Read, "Right Direction", "Right movement value", false);
        RegisterParameter<float>(OSCLeashParameter.XNegative, "Leash_XNegative", ParameterMode.Read, "Left Direction", "Left movement value", false);
        RegisterParameter<float>(OSCLeashParameter.YPositive, "Leash_YPositive", ParameterMode.Read, "Up Direction", "Upward movement value", false);
        RegisterParameter<float>(OSCLeashParameter.YNegative, "Leash_YNegative", ParameterMode.Read, "Down Direction", "Downward movement value", false);
    }

    protected override async Task<bool> OnModuleStart()
    {
        if (!GetSettingValue<bool>(OSCLeashSetting.VerticalMovementEnabled))
        {
            return true; // Skip OpenVR initialization if vertical movement is disabled
        }

        // Initialize OpenVR
        EVRInitError initError = EVRInitError.None;
        vrSystem = OpenVR.Init(ref initError, EVRApplicationType.VRApplication_Overlay);
        if (initError != EVRInitError.None)
        {
            Log($"Failed to initialize OpenVR: {initError}");
            return false;
        }

        // Store original tracking origin and get initial OVRAS offset
        var compositor = OpenVR.Compositor;
        if (compositor != null)
        {
            originalTrackingOrigin = compositor.GetTrackingSpace();
            UpdateOVRASOffset();
            isVRInitialized = true;
        }

        return true;
    }

    protected override async Task OnModuleStop()
    {
        // Reset vertical offset and cleanup OpenVR
        if (isVRInitialized)
        {
            // Restore original tracking origin
            var compositor = OpenVR.Compositor;
            if (compositor != null)
            {
                compositor.SetTrackingSpace(originalTrackingOrigin);
            }

            OpenVR.Shutdown();
            vrSystem = null;
            isVRInitialized = false;
        }
    }

    protected override void OnRegisteredParameterReceived(RegisteredParameter parameter)
    {
        switch (parameter.Lookup)
        {
            case OSCLeashParameter.IsGrabbed:
                isGrabbed = parameter.GetValue<bool>();
                break;
            case OSCLeashParameter.Stretch:
                stretch = parameter.GetValue<float>();
                break;
            case OSCLeashParameter.ZPositive:
                zPositive = parameter.GetValue<float>();
                break;
            case OSCLeashParameter.ZNegative:
                zNegative = parameter.GetValue<float>();
                break;
            case OSCLeashParameter.XPositive:
                xPositive = parameter.GetValue<float>();
                break;
            case OSCLeashParameter.XNegative:
                xNegative = parameter.GetValue<float>();
                break;
            case OSCLeashParameter.YPositive:
                yPositive = parameter.GetValue<float>();
                break;
            case OSCLeashParameter.YNegative:
                yNegative = parameter.GetValue<float>();
                break;
        }
    }

    private float SmoothValue(Queue<float> buffer, float newValue)
    {
        buffer.Enqueue(newValue);
        if (buffer.Count > SmoothingBufferSize)
            buffer.Dequeue();
        
        float sum = 0;
        float weight = 1;
        float totalWeight = 0;
        
        foreach (var value in buffer.Reverse())
        {
            sum += value * weight;
            totalWeight += weight;
            weight *= Constants.SMOOTHING_WEIGHT_DECAY;
        }
        
        return sum / totalWeight;
    }

    private void UpdateOVRASOffset()
    {
        if (!GetSettingValue<bool>(OSCLeashSetting.VerticalMovementEnabled) || !isVRInitialized) return;

        try
        {
            // Try to read OpenVR Advanced Settings offset
            var chaperoneSetup = OpenVR.ChaperoneSetup;
            if (chaperoneSetup != null)
            {
                var standingZeroPose = new HmdMatrix34_t();
                chaperoneSetup.GetWorkingStandingZeroPoseToRawTrackingPose(ref standingZeroPose);
                targetVerticalOffset = standingZeroPose.m7; // Get the vertical component
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
        if (!ShouldUpdateVerticalOffset()) return;

        try
        {
            var chaperoneSetup = OpenVR.ChaperoneSetup;
            if (chaperoneSetup == null) return;

            var standingZeroPose = GetCurrentStandingPose(chaperoneSetup);
            var stepSize = CalculateStepSize();
            var newOffset = CalculateNewVerticalOffset(deltaTime, stepSize);

            // Only update if the change is significant
            if (ShouldApplyVerticalUpdate(newOffset, stepSize))
            {
                ApplyVerticalOffset(chaperoneSetup, standingZeroPose, newOffset);
            }
        }
        catch (Exception ex)
        {
            Log($"Error updating vertical offset: {ex.Message}");
        }
    }

    private bool ShouldUpdateVerticalOffset()
    {
        return GetSettingValue<bool>(OSCLeashSetting.VerticalMovementEnabled) && isVRInitialized;
    }

    private HmdMatrix34_t GetCurrentStandingPose(CVRChaperoneSetup chaperoneSetup)
    {
        var standingZeroPose = new HmdMatrix34_t();
        chaperoneSetup.GetWorkingStandingZeroPoseToRawTrackingPose(ref standingZeroPose);
        return standingZeroPose;
    }

    private float CalculateStepSize()
    {
        float verticalStretch = Math.Abs(yPositive - yNegative);
        float baseStepMultiplier = GetSettingValue<float>(OSCLeashSetting.VerticalStepMultiplier);
        float stepSize = baseStepMultiplier * (1.0f + verticalStretch);

        // Apply horizontal compensation
        float horizontalStretch = Math.Max(Math.Abs(xPositive - xNegative), Math.Abs(zPositive - zNegative));
        float horizontalCompensation = GetSettingValue<float>(OSCLeashSetting.VerticalHorizontalCompensation);
        return stepSize * (1.0f - (horizontalStretch * horizontalCompensation));
    }

    private float CalculateNewVerticalOffset(float deltaTime, float stepSize)
    {
        if (!isGrabbed)
        {
            return CalculateFallingOffset(deltaTime, stepSize);
        }

        return CalculateGrabbedOffset(deltaTime, stepSize);
    }

    private float CalculateFallingOffset(float deltaTime, float stepSize)
    {
        if (currentVerticalOffset == 0)
        {
            verticalVelocity = 0;
            return 0;
        }

        // Apply gravity in the direction towards 0
        float gravityDirection = currentVerticalOffset > 0 ? -1 : 1;
        verticalVelocity = Math.Clamp(
            verticalVelocity + Constants.GRAVITY * gravityDirection * deltaTime * 2f,
            Constants.TERMINAL_VELOCITY,
            -Constants.TERMINAL_VELOCITY
        );

        float newOffset = currentVerticalOffset + verticalVelocity * deltaTime;

        // Check if we've crossed 0
        if ((currentVerticalOffset > 0 && newOffset <= 0) || 
            (currentVerticalOffset < 0 && newOffset >= 0))
        {
            verticalVelocity = 0;
            return 0;
        }

        // Quantize the falling movement
        return (float)(Math.Floor(newOffset / stepSize) * stepSize);
    }

    private float CalculateGrabbedOffset(float deltaTime, float stepSize)
    {
        var verticalDeadzone = GetSettingValue<float>(OSCLeashSetting.VerticalMovementDeadzone);
        if (stretch <= verticalDeadzone)
        {
            verticalVelocity = 0;
            return currentVerticalOffset;
        }

        // Check horizontal deadzone
        float horizontalCombined = Math.Max(Math.Abs(xPositive - xNegative), Math.Abs(zPositive - zNegative));
        float horizontalDeadzone = GetSettingValue<float>(OSCLeashSetting.VerticalHorizontalCompensation);
        
        if (horizontalCombined >= horizontalDeadzone)
        {
            verticalVelocity = 0;
            return currentVerticalOffset;
        }

        // Calculate vertical movement
        var verticalDelta = (yNegative - yPositive) * 
            GetSettingValue<float>(OSCLeashSetting.VerticalMovementMultiplier) * 5f * 
            (1.0f - (horizontalCombined * horizontalDeadzone));

        targetVerticalOffset += verticalDelta * deltaTime;
        targetVerticalOffset = (float)(Math.Floor(targetVerticalOffset / stepSize) * stepSize);

        // Apply smoothing
        var newOffset = currentVerticalOffset * Constants.VERTICAL_SMOOTHING + 
            targetVerticalOffset * (1 - Constants.VERTICAL_SMOOTHING);

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

    [ModuleUpdate(ModuleUpdateMode.Custom, true, 33)]
    private void UpdateMovement()
    {
        // Frame skipping for performance
        if (frameCounter++ % (FrameSkip + 1) != 0)
            return;

        var deltaTime = 1f/30f; // Fixed timestep at 30Hz

        var player = GetPlayer();
        if (player == null)
        {
            if (GetSettingValue<bool>(OSCLeashSetting.VerticalMovementEnabled))
            {
                UpdateVerticalOffset(deltaTime);
            }
            return;
        }

        if (!isGrabbed)
        {
            player.StopRun();
            player.MoveVertical(0);
            player.MoveHorizontal(0);
            player.LookHorizontal(0);
            verticalSmoothingBuffer.Clear();
            horizontalSmoothingBuffer.Clear();
            if (GetSettingValue<bool>(OSCLeashSetting.VerticalMovementEnabled))
            {
                UpdateVerticalOffset(deltaTime);
            }
            return;
        }

        // Calculate movement
        var strengthMultiplier = GetSettingValue<float>(OSCLeashSetting.StrengthMultiplier);
        var outputMultiplier = stretch * strengthMultiplier;
        
        var rawVerticalOutput = Clamp((zPositive - zNegative) * outputMultiplier);
        var rawHorizontalOutput = Clamp((xPositive - xNegative) * outputMultiplier);

        // Handle vertical movement via OpenVR if enabled
        if (GetSettingValue<bool>(OSCLeashSetting.VerticalMovementEnabled))
        {
            UpdateVerticalOffset(deltaTime);
        }

        // Early exit if no significant movement
        if (Math.Abs(rawVerticalOutput) < UpdateThreshold && Math.Abs(rawHorizontalOutput) < UpdateThreshold)
        {
            player.StopRun();
            player.MoveVertical(0);
            player.MoveHorizontal(0);
            player.LookHorizontal(0);
            return;
        }

        var verticalOutput = SmoothValue(verticalSmoothingBuffer, rawVerticalOutput);
        var horizontalOutput = SmoothValue(horizontalSmoothingBuffer, rawHorizontalOutput);

        // Up/Down compensation
        var yCombined = yPositive + yNegative;
        var upDownDeadzone = GetSettingValue<float>(OSCLeashSetting.UpDownDeadzone);
        if (yCombined >= upDownDeadzone)
        {
            player.StopRun();
            player.MoveVertical(0);
            player.MoveHorizontal(0);
            player.LookHorizontal(0);
            return;
        }

        // Up/Down Compensation
        var upDownCompensation = GetSettingValue<float>(OSCLeashSetting.UpDownCompensation);
        if (upDownCompensation != 0)
        {
            var yModifier = Clamp(1.0f - (yCombined * upDownCompensation));
            if (yModifier != 0.0f)
            {
                verticalOutput /= yModifier;
                horizontalOutput /= yModifier;
            }
        }

        // Apply movement
        var runDeadzone = GetSettingValue<float>(OSCLeashSetting.RunDeadzone);
        if (stretch > runDeadzone)
            player.Run();
        else
            player.StopRun();

        player.MoveVertical(verticalOutput);
        player.MoveHorizontal(horizontalOutput);
        
        // Apply turning if enabled
        var turningEnabled = GetSettingValue<bool>(OSCLeashSetting.TurningEnabled);
        if (turningEnabled && stretch > GetSettingValue<float>(OSCLeashSetting.TurningDeadzone))
        {
            var turningOutput = CalculateTurningOutput();
            player.LookHorizontal(turningOutput);
        }
        else
        {
            player.LookHorizontal(0);
        }
    }

    private float CalculateTurningOutput()
    {
        var turningMultiplier = GetSettingValue<float>(OSCLeashSetting.TurningMultiplier);
        var turningGoal = Math.Max(0f, GetSettingValue<float>(OSCLeashSetting.TurningGoal)) / 180f;
        var direction = GetSettingValue<LeashDirection>(OSCLeashSetting.LeashDirection);
        var horizontalOutput = Clamp((xPositive - xNegative) * stretch * GetSettingValue<float>(OSCLeashSetting.StrengthMultiplier));
        var verticalOutput = Clamp((zPositive - zNegative) * stretch * GetSettingValue<float>(OSCLeashSetting.StrengthMultiplier));

        float turningOutput = 0f;
        switch (direction)
        {
            case LeashDirection.North:
                if (zPositive < turningGoal)
                {
                    turningOutput = horizontalOutput * turningMultiplier;
                    if (xPositive > xNegative)
                        turningOutput += zNegative;
                    else
                        turningOutput -= zNegative;
                }
                break;
            case LeashDirection.South:
                if (zNegative < turningGoal)
                {
                    turningOutput = -horizontalOutput * turningMultiplier;
                    if (xPositive > xNegative)
                        turningOutput -= zPositive;
                    else
                        turningOutput += zPositive;
                }
                break;
            case LeashDirection.East:
                if (xPositive < turningGoal)
                {
                    turningOutput = verticalOutput * turningMultiplier;
                    if (zPositive > zNegative)
                        turningOutput += xNegative;
                    else
                        turningOutput -= xNegative;
                }
                break;
            case LeashDirection.West:
                if (xNegative < turningGoal)
                {
                    turningOutput = -verticalOutput * turningMultiplier;
                    if (zPositive > zNegative)
                        turningOutput -= xPositive;
                    else
                        turningOutput += xPositive;
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