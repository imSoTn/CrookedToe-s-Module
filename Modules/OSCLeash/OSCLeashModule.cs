using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Parameters;
using VRCOSC.App.SDK.VRChat;
using Valve.VR;
using System;
using System.Threading.Tasks;
using CrookedToe.Modules.OSCLeash;

namespace CrookedToe.Modules.OSCLeash;

[ModuleTitle("OSC Leash")]
[ModuleDescription("Allows for controlling avatar movement with parameters, including vertical movement via OpenVR")]
[ModuleType(ModuleType.Generic)]
[ModulePrefab("OSCLeash", "https://github.com/CrookedToe/OSCLeash/tree/main/Unity")]
[ModuleInfo("https://github.com/CrookedToe/CrookedToe-s-Modules")]
public class OSCLeashModule : Module
{
    private const float UPDATE_THRESHOLD = 0.025f;
    private const int FRAME_SKIP = 1;
    private const float DELTA_TIME = 1f / 30f;
    
    private readonly MovementState _state = new();
    private readonly OpenVRService _ovrService;
    private int _frameCounter;
    private Vector3 _lastMovement;
    private bool _needsMovementUpdate;
    private bool _hasShownLeashWarning;

    public OSCLeashModule() => _ovrService = new OpenVRService(Log);

    protected override void OnPreLoad()
    {
        CreateMovementSettings();
        CreateTurningSettings();
        CreateVerticalSettings();
        CreateParameterRegistrations();
    }

    private void CreateMovementSettings()
    {
        CreateSlider(OSCLeashSetting.WalkDeadzone, "Walk Deadzone", 
            "Minimum stretch (0-1) required to start walking.", 0.15f, 0.0f, 1.0f);
        CreateSlider(OSCLeashSetting.RunDeadzone, "Run Deadzone", 
            "Stretch threshold (0-1) for running.", 0.70f, 0.0f, 1.0f);
        CreateSlider(OSCLeashSetting.StrengthMultiplier, "Movement Strength", 
            "Overall speed multiplier (0.1-5.0).", 1.2f, 0.1f, 5.0f);
        CreateSlider(OSCLeashSetting.UpDownDeadzone, "Up/Down Deadzone",
            "Vertical movement threshold (0-1).", 0.5f, 0.0f, 1.0f);
        CreateSlider(OSCLeashSetting.UpDownCompensation, "Up/Down Compensation",
            "Vertical movement effect on horizontal speed (0-1).", 0.5f, 0.0f, 1.0f);
        CreateDropdown(OSCLeashSetting.LeashDirection, "Leash Direction", 
            "Direction the leash faces relative to avatar.", LeashDirection.North);
    }

    private void CreateTurningSettings()
    {
        CreateToggle(OSCLeashSetting.TurningEnabled, "Enable Turning", 
            "Enables avatar rotation control.", false);
        CreateSlider(OSCLeashSetting.TurningMultiplier, "Turn Speed", 
            "Rotation speed multiplier (0.1-2.0).", 0.80f, 0.1f, 2.0f);
        CreateSlider(OSCLeashSetting.TurningDeadzone, "Turn Deadzone", 
            "Minimum stretch for turning (0-1).", 0.15f, 0.0f, 1.0f);
        CreateSlider(OSCLeashSetting.TurningGoal, "Maximum Turn Angle", 
            "Maximum rotation angle (0-180).", 90f, 0.0f, 180.0f);
    }

    private void CreateVerticalSettings()
    {
        CreateToggle(OSCLeashSetting.VerticalMovementEnabled, "Enable Vertical Movement", 
            "Enables OpenVR height control.", false);
        CreateToggle(OSCLeashSetting.GrabBasedGravity, "Grab-Based Gravity",
            "Only apply gravity during/after grab until rest.", false);
        CreateSlider(OSCLeashSetting.VerticalMovementMultiplier, "Vertical Speed", 
            "Vertical movement speed multiplier (0.1-5.0).", 1.0f, 0.1f, 5.0f);
        CreateSlider(OSCLeashSetting.VerticalMovementDeadzone, "Vertical Deadzone", 
            "Minimum vertical pull needed (0-1).", 0.15f, 0.0f, 1.0f, 0.05f);
        CreateSlider(OSCLeashSetting.VerticalMovementSmoothing, "Vertical Smoothing", 
            "Smoothing factor for height changes (0-1).", 0.8f, 0.0f, 1.0f);
        CreateSlider(OSCLeashSetting.VerticalHorizontalCompensation, "Vertical Angle", 
            "Required angle from horizontal (15-75°).", 45f, 15f, 75f);
    }

    private void CreateParameterRegistrations()
    {
        RegisterParameter<bool>(OSCLeashParameter.IsGrabbed, "Leash_IsGrabbed", 
            ParameterMode.Read, "Leash Grabbed", "Whether the leash is being held");
        RegisterParameter<float>(OSCLeashParameter.Stretch, "Leash_Stretch", 
            ParameterMode.Read, "Leash Stretch", "How far the leash is stretched");
        RegisterParameter<float>(OSCLeashParameter.ZPositive, "Leash_ZPositive", 
            ParameterMode.Read, "Forward Pull", "Forward movement value", false);
        RegisterParameter<float>(OSCLeashParameter.ZNegative, "Leash_ZNegative", 
            ParameterMode.Read, "Backward Pull", "Backward movement value", false);
        RegisterParameter<float>(OSCLeashParameter.XPositive, "Leash_XPositive", 
            ParameterMode.Read, "Right Pull", "Rightward movement value", false);
        RegisterParameter<float>(OSCLeashParameter.XNegative, "Leash_XNegative", 
            ParameterMode.Read, "Left Pull", "Leftward movement value", false);
        RegisterParameter<float>(OSCLeashParameter.YPositive, "Leash_YPositive", 
            ParameterMode.Read, "Upward Pull", "Upward movement value", false);
        RegisterParameter<float>(OSCLeashParameter.YNegative, "Leash_YNegative", 
            ParameterMode.Read, "Downward Pull", "Downward movement value", false);
    }

    protected override Task<bool> OnModuleStart()
    {
        var verticalEnabled = GetSettingValue<bool>(OSCLeashSetting.VerticalMovementEnabled);
        if (verticalEnabled)
        {
            _ovrService.Enable();
            _ovrService.SetGrabBasedGravity(GetSettingValue<bool>(OSCLeashSetting.GrabBasedGravity));
            _ovrService.Initialize(GetOVRClient());
        }
        return Task.FromResult(true);
    }

    protected override Task OnModuleStop()
    {
        _ovrService.Disable();
        return Task.CompletedTask;
    }

    [ModuleUpdate(ModuleUpdateMode.Custom, true, 33)]
    private void UpdateMovement()
    {
        if (_frameCounter++ % (FRAME_SKIP + 1) != 0)
            return;

        var ovrClient = GetOVRClient();
        if (ovrClient == null)
            return;

        UpdateVerticalMovement();
        UpdateHorizontalMovement();
    }

    private void UpdateVerticalMovement()
    {
        bool isEnabled = GetSettingValue<bool>(OSCLeashSetting.VerticalMovementEnabled);
        if (!isEnabled)
        {
            _ovrService.Disable();
            return;
        }

        if (!_ovrService.IsInitialized)
        {
            _ovrService.Enable();
            _ovrService.Initialize(GetOVRClient());
            if (!_ovrService.IsInitialized)
                return;
        }

        try
        {
            _ovrService.IsGrabbed = _state.IsGrabbed;
            _ovrService.SetGrabBasedGravity(GetSettingValue<bool>(OSCLeashSetting.GrabBasedGravity));

            float newOffset = _ovrService.UpdateVerticalMovement(
                DELTA_TIME,
                _state,
                GetSettingValue<float>(OSCLeashSetting.VerticalMovementDeadzone),
                GetSettingValue<float>(OSCLeashSetting.VerticalHorizontalCompensation),
                GetSettingValue<float>(OSCLeashSetting.VerticalMovementMultiplier),
                GetSettingValue<float>(OSCLeashSetting.VerticalMovementSmoothing)
            );

            _ovrService.ApplyOffset(newOffset);
        }
        catch (Exception) { }
    }

    private void UpdateHorizontalMovement()
    {
        var player = GetPlayer();
        if (player == null)
            return;

        var newMovement = CalculateMovement();
        if (_state.IsReturningToOrigin)
        {
            var returnVector = _state.GetReturnToOriginVector();
            if (returnVector != Vector3.Zero)
            {
                newMovement = returnVector * (GetSettingValue<float>(OSCLeashSetting.WalkDeadzone) * 0.5f);
            }
        }

        if (!_needsMovementUpdate && 
            !_state.IsReturningToOrigin && 
            _lastMovement.DistanceTo(newMovement) < UPDATE_THRESHOLD)
            return;

        ApplyMovement(player, newMovement);
        _lastMovement = newMovement;
        _needsMovementUpdate = false;
    }

    private Vector3 CalculateMovement()
    {
        if (!_state.IsGrabbed)
            return Vector3.Zero;

        _state.UpdateState(
            GetSettingValue<float>(OSCLeashSetting.WalkDeadzone),
            GetSettingValue<float>(OSCLeashSetting.RunDeadzone)
        );

        return _state.CalculateMovement(
            GetSettingValue<float>(OSCLeashSetting.StrengthMultiplier),
            GetSettingValue<float>(OSCLeashSetting.UpDownDeadzone),
            GetSettingValue<float>(OSCLeashSetting.UpDownCompensation)
        );
    }

    private void ApplyMovement(Player player, Vector3 movement)
    {
        if (!_state.IsGrabbed && !_state.IsReturningToOrigin)
        {
            player.StopRun();
            player.MoveVertical(0);
            player.MoveHorizontal(0);
            player.LookHorizontal(0);
            _state.Reset();
            return;
        }

        if (_state.CurrentState == MovementStateType.Running && !_state.IsReturningToOrigin)
            player.Run();
        else
            player.StopRun();

        player.MoveVertical(movement.Z);
        player.MoveHorizontal(movement.X);

        if (_state.IsGrabbed && _state.CurrentState != MovementStateType.Idle)
            UpdateTurning(player, movement);
        else
            player.LookHorizontal(0);
    }

    private void UpdateTurning(Player player, Vector3 movement)
    {
        if (!GetSettingValue<bool>(OSCLeashSetting.TurningEnabled) || 
            _state.Stretch <= GetSettingValue<float>(OSCLeashSetting.TurningDeadzone))
        {
            player.LookHorizontal(0);
            return;
        }

        player.LookHorizontal(CalculateTurningOutput(movement));
    }

    private float CalculateTurningOutput(Vector3 movement)
    {
        var turningMultiplier = GetSettingValue<float>(OSCLeashSetting.TurningMultiplier);
        var turningGoal = Math.Max(0f, GetSettingValue<float>(OSCLeashSetting.TurningGoal)) / 180f;
        var direction = GetSettingValue<LeashDirection>(OSCLeashSetting.LeashDirection);

        float output = direction switch
        {
            LeashDirection.North => movement.Z < turningGoal ? movement.X * turningMultiplier + (movement.X > 0 ? -movement.X : movement.X) : 0,
            LeashDirection.South => -movement.Z < turningGoal ? -movement.X * turningMultiplier + (movement.X > 0 ? -movement.X : movement.X) : 0,
            LeashDirection.East => movement.X < turningGoal ? movement.Z * turningMultiplier + (movement.Z > 0 ? -movement.Z : movement.Z) : 0,
            LeashDirection.West => -movement.X < turningGoal ? -movement.Z * turningMultiplier + (movement.Z > 0 ? -movement.Z : movement.Z) : 0,
            _ => 0
        };

        return Math.Clamp(output, -1f, 1f);
    }

    protected override void OnRegisteredParameterReceived(RegisteredParameter parameter)
    {
        _needsMovementUpdate = true;
        switch (parameter.Lookup)
        {
            case OSCLeashParameter.IsGrabbed: _state.IsGrabbed = parameter.GetValue<bool>(); break;
            case OSCLeashParameter.Stretch: _state.Stretch = parameter.GetValue<float>(); break;
            case OSCLeashParameter.ZPositive: _state.ZPositive = parameter.GetValue<float>(); break;
            case OSCLeashParameter.ZNegative: _state.ZNegative = parameter.GetValue<float>(); break;
            case OSCLeashParameter.XPositive: _state.XPositive = parameter.GetValue<float>(); break;
            case OSCLeashParameter.XNegative: _state.XNegative = parameter.GetValue<float>(); break;
            case OSCLeashParameter.YPositive: _state.YPositive = parameter.GetValue<float>(); break;
            case OSCLeashParameter.YNegative: _state.YNegative = parameter.GetValue<float>(); break;
        }
    }
} 