namespace CrookedToe.Modules.OSCLeash;

/// <summary>
/// Defines the direction the leash faces relative to the avatar
/// </summary>
public enum LeashDirection
{
    /// <summary>Front-facing leash (default). Pull back to turn left/right.</summary>
    North,
    /// <summary>Back-facing leash. Pull forward to turn left/right.</summary>
    South,
    /// <summary>Right-facing leash. Pull left to turn forward/back.</summary>
    East,
    /// <summary>Left-facing leash. Pull right to turn forward/back.</summary>
    West
}

/// <summary>
/// OSC parameters used by the leash module
/// </summary>
public enum OSCLeashParameter
{
    /// <summary>Forward movement value (0-1). Higher values move avatar forward faster.</summary>
    ZPositive,
    /// <summary>Backward movement value (0-1). Higher values move avatar backward faster.</summary>
    ZNegative,
    /// <summary>Right movement value (0-1). Higher values move avatar right faster.</summary>
    XPositive,
    /// <summary>Left movement value (0-1). Higher values move avatar left faster.</summary>
    XNegative,
    /// <summary>Upward movement value (0-1). Higher values move avatar up faster when vertical movement enabled.</summary>
    YPositive,
    /// <summary>Downward movement value (0-1). Higher values move avatar down faster when vertical movement enabled.</summary>
    YNegative,
    /// <summary>Whether the leash is being held (true/false). When false, all movement stops.</summary>
    IsGrabbed,
    /// <summary>How far the leash is stretched (0-1). Controls walking/running state and movement speed.</summary>
    Stretch
}

/// <summary>
/// Configuration settings for the leash module
/// </summary>
public enum OSCLeashSetting
{
    /// <summary>
    /// Direction the leash faces relative to avatar. Affects how turning works:
    /// - North (default): Pull back + left/right to turn
    /// - South: Pull forward + left/right to turn
    /// - East: Pull left + forward/back to turn
    /// - West: Pull right + forward/back to turn
    /// </summary>
    LeashDirection,
    
    /// <summary>
    /// Minimum stretch required to start running (0-1).
    /// - Below WalkDeadzone: No movement
    /// - Between WalkDeadzone and RunDeadzone: Walking speed
    /// - Above RunDeadzone: Running speed
    /// Higher values require more stretch before running starts.
    /// </summary>
    RunDeadzone,
    
    /// <summary>
    /// Minimum stretch required to start walking (0-1).
    /// - Below this: No movement
    /// - Above this: Start walking
    /// Higher values require more stretch before any movement starts.
    /// </summary>
    WalkDeadzone,
    
    /// <summary>
    /// Overall movement speed multiplier (0.1-5.0).
    /// Directly multiplies movement speed in all directions.
    /// Higher values = faster movement overall.
    /// Affects both walking and running speeds.
    /// </summary>
    StrengthMultiplier,
    
    /// <summary>
    /// How much vertical pulling reduces horizontal movement (0-1).
    /// - 0: Vertical pulling doesn't affect horizontal speed
    /// - 1: Strong vertical pulling completely stops horizontal movement
    /// Higher values make it harder to move horizontally while pulling up/down.
    /// </summary>
    UpDownCompensation,
    
    /// <summary>
    /// Minimum vertical movement before canceling horizontal movement (0-1).
    /// When vertical movement exceeds this, horizontal movement stops.
    /// Higher values allow more vertical movement before stopping horizontal movement.
    /// </summary>
    UpDownDeadzone,
    
    /// <summary>
    /// Enables/disables turning control.
    /// When enabled, pulling in certain directions will rotate the avatar.
    /// Direction of pull depends on LeashDirection setting.
    /// </summary>
    TurningEnabled,
    
    /// <summary>
    /// How quickly the avatar rotates when turning (0.1-2.0).
    /// Directly multiplies turning speed.
    /// Higher values make the avatar turn faster when pulling sideways.
    /// </summary>
    TurningMultiplier,
    
    /// <summary>
    /// Minimum stretch needed before turning starts (0-1).
    /// Must exceed this threshold before any turning occurs.
    /// Higher values require more stretch before turning begins.
    /// </summary>
    TurningDeadzone,
    
    /// <summary>
    /// Maximum angle avatar can turn in degrees (0-180).
    /// Limits how far the avatar can rotate when pulling.
    /// Higher values allow more rotation before stopping.
    /// </summary>
    TurningGoal,
    
    /// <summary>
    /// Enables/disables OpenVR vertical movement.
    /// When enabled, pulling up/down changes your real VR height.
    /// Requires SteamVR to be running.
    /// </summary>
    VerticalMovementEnabled,
    
    /// <summary>
    /// How quickly you move up/down in VR (0.1-5.0).
    /// Directly multiplies vertical movement speed.
    /// Higher values = faster height changes when pulling up/down.
    /// </summary>
    VerticalMovementMultiplier,
    
    /// <summary>
    /// Minimum vertical pull needed for height change (0-1).
    /// Must exceed this before height changes occur.
    /// Higher values require stronger vertical pulling.
    /// </summary>
    VerticalMovementDeadzone,
    
    /// <summary>
    /// Smoothing factor for vertical movement (0-1).
    /// - 0: Immediate height changes
    /// - 1: Very smooth but delayed movement
    /// Higher values reduce jitter but increase latency.
    /// </summary>
    VerticalMovementSmoothing,
    
    /// <summary>
    /// Required angle from horizontal for vertical movement (15-75 degrees).
    /// - Lower angles (15°): Easier to trigger vertical movement
    /// - Higher angles (75°): Must pull more vertically
    /// Controls how "vertical" the pull must be to change height.
    /// </summary>
    VerticalHorizontalCompensation,
    
    /// <summary>
    /// When enabled, gravity is only active after the leash has been grabbed and released.
    /// Gravity remains disabled until the next grab-release cycle.
    /// This creates a more controlled vertical movement experience.
    /// </summary>
    GrabBasedGravity
} 