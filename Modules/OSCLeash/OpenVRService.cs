using System;
using Valve.VR;
using VRCOSC.App.SDK.OVR;
using VRCOSC.App.SDK.Modules;

namespace CrookedToe.Modules.OSCLeash;

/// <summary>
/// Configuration constants for OpenVR service
/// </summary>
internal static class OpenVRConfig
{
    public const float GRAVITY = 9.81f;
    public const float TERMINAL_VELOCITY = 15.0f;
    public const float VERTICAL_SMOOTHING = 0.95f;
    public const float GRAB_DELAY = 0f;
    public const float MAX_VERTICAL_OFFSET = 2.0f;
    public const float MOVEMENT_DEADZONE = 0.05f;
    public const float STOP_THRESHOLD = 0.01f;
    public const float VELOCITY_STOP_THRESHOLD = 0.1f;
}

/// <summary>
/// Manages OpenVR integration and vertical movement calculations
/// </summary>
public class OpenVRService : IDisposable
{
    private readonly Action<string> logCallback;
    private bool isInitialized;
    private bool isEnabled;
    private float currentVerticalOffset;
    private float verticalVelocity;
    private ETrackingUniverseOrigin originalTrackingOrigin;
    private HmdMatrix34_t standingZeroPose;
    private bool isDisposed;
    private OVRClient? ovrClient;
    private bool useGrabBasedGravity;
    public bool IsGrabbed { get; set; }

    public OpenVRService(Action<string> logCallback)
    {
        this.logCallback = logCallback ?? throw new ArgumentNullException(nameof(logCallback));
        standingZeroPose = new HmdMatrix34_t();
    }

    public bool Initialize(OVRClient? client)
    {
        ThrowIfDisposed();
        
        if (!isEnabled || client?.HasInitialised != true)
        {
            Reset();
            return false;
        }

        if (isInitialized)
            return true;

        try
        {
            var compositor = OpenVR.Compositor;
            var chaperoneSetup = OpenVR.ChaperoneSetup;
            
            if (compositor == null || chaperoneSetup == null)
            {
                Reset();
                return false;
            }

            ovrClient = client;
            originalTrackingOrigin = compositor.GetTrackingSpace();
            chaperoneSetup.GetWorkingStandingZeroPoseToRawTrackingPose(ref standingZeroPose);
            currentVerticalOffset = standingZeroPose.m7;
            isInitialized = true;
            return true;
        }
        catch (Exception ex)
        {
            logCallback($"Failed to initialize OpenVR: {ex.Message}");
            Reset();
            return false;
        }
    }

    public void Enable() => isEnabled = true;
    public void Disable()
    {
        isEnabled = false;
        Reset();
    }

    private void Reset()
    {
        if (isInitialized && ovrClient?.HasInitialised == true)
        {
            try
            {
                var compositor = OpenVR.Compositor;
                var chaperoneSetup = OpenVR.ChaperoneSetup;
                
                if (compositor != null)
                    compositor.SetTrackingSpace(originalTrackingOrigin);
                
                if (chaperoneSetup != null)
                {
                    standingZeroPose.m7 = 0;
                    chaperoneSetup.SetWorkingStandingZeroPoseToRawTrackingPose(ref standingZeroPose);
                    chaperoneSetup.CommitWorkingCopy(EChaperoneConfigFile.Live);
                }
            }
            catch (Exception ex)
            {
                logCallback($"Error resetting OpenVR state: {ex.Message}");
            }
        }

        isInitialized = false;
        verticalVelocity = 0;
        currentVerticalOffset = 0;
        ovrClient = null;
    }

    private bool CanUpdatePlayspace()
    {
        if (!isEnabled || !isInitialized || ovrClient?.HasInitialised != true)
            return false;

        return !useGrabBasedGravity || IsGrabbed || 
               (Math.Abs(currentVerticalOffset) > OpenVRConfig.STOP_THRESHOLD || 
                Math.Abs(verticalVelocity) > OpenVRConfig.VELOCITY_STOP_THRESHOLD);
    }

    public void ApplyOffset(float newOffset)
    {
        ThrowIfDisposed();
        
        if (!CanUpdatePlayspace())
            return;

        try
        {
            var chaperoneSetup = OpenVR.ChaperoneSetup;
            if (chaperoneSetup == null)
                return;

            currentVerticalOffset = newOffset;
            standingZeroPose.m7 = newOffset;
            chaperoneSetup.SetWorkingStandingZeroPoseToRawTrackingPose(ref standingZeroPose);
            chaperoneSetup.CommitWorkingCopy(EChaperoneConfigFile.Live);
        }
        catch (Exception ex)
        {
            logCallback($"Error applying offset: {ex.Message}");
            Reset();
        }
    }

    public float UpdateVerticalMovement(float deltaTime, MovementState state, float verticalDeadzone,
        float angleThreshold, float verticalMultiplier, float smoothing)
    {
        ThrowIfDisposed();
        
        if (!isInitialized || !isEnabled)
            return currentVerticalOffset;

        float newOffset = currentVerticalOffset;
        IsGrabbed = state.IsGrabbed;

        if (IsGrabbed)
        {
            float stepSize = CalculateStepSize(state, verticalDeadzone, angleThreshold);
            if (Math.Abs(stepSize) >= OpenVRConfig.MOVEMENT_DEADZONE)
            {
                newOffset = ApplyMovement(deltaTime, stepSize, verticalMultiplier, smoothing);
            }
        }
        else if (CanUpdatePlayspace())
        {
            newOffset = ApplyGravity(deltaTime);
        }

        return newOffset;
    }

    public void SetGrabBasedGravity(bool enabled)
    {
        useGrabBasedGravity = enabled;
        if (!enabled)
            ApplyOffset(0f);
    }

    private float CalculateStepSize(MovementState state, float verticalDeadzone, float angleThreshold)
    {
        Vector3 movement = state.GetMovementVector();
        
        // Calculate horizontal magnitude for angle check
        float horizontalX = movement.X;
        float horizontalZ = movement.Z;
        float horizontalMagnitude = MathF.Sqrt(horizontalX * horizontalX + horizontalZ * horizontalZ);

        // Convert the angle threshold from degrees to the actual angle check
        float pullAngle = MathF.Atan2(MathF.Abs(movement.Y), horizontalMagnitude) * (180f / MathF.PI);
        if (pullAngle < angleThreshold)
            return 0f;

        return movement.Y;
    }

    private float ApplyGravity(float deltaTime)
    {
        // If we're very close to the original position and moving slowly, stop
        if (Math.Abs(currentVerticalOffset) < OpenVRConfig.STOP_THRESHOLD && 
            Math.Abs(verticalVelocity) < OpenVRConfig.VELOCITY_STOP_THRESHOLD)
        {
            verticalVelocity = 0f;
            ApplyOffset(0f);
            return 0f;
        }

        // Apply standard gravity towards original position
        float gravityDirection = -Math.Sign(currentVerticalOffset);
        verticalVelocity += OpenVRConfig.GRAVITY * gravityDirection * deltaTime;
        
        // Clamp velocity based on direction
        if (gravityDirection > 0)
        {
            verticalVelocity = Math.Min(verticalVelocity, OpenVRConfig.TERMINAL_VELOCITY);
        }
        else
        {
            verticalVelocity = Math.Max(verticalVelocity, -OpenVRConfig.TERMINAL_VELOCITY);
        }

        float newOffset = currentVerticalOffset + verticalVelocity * deltaTime;

        // If we've crossed the original position, stop at 0
        if ((currentVerticalOffset > 0f && newOffset < 0f) ||
            (currentVerticalOffset < 0f && newOffset > 0f))
        {
            verticalVelocity = 0f;
            ApplyOffset(0f);
            return 0f;
        }

        ApplyOffset(newOffset);
        return newOffset;
    }

    private float ApplyMovement(float deltaTime, float stepSize, float verticalMultiplier, float smoothing)
    {
        // Calculate target velocity based on input
        float targetVelocity = stepSize * verticalMultiplier;
        
        // Smoothly interpolate to target velocity using the smoothing parameter
        verticalVelocity = verticalVelocity * smoothing + targetVelocity * (1f - smoothing);
        
        // Update position with velocity
        float newOffset = currentVerticalOffset + verticalVelocity * deltaTime;
        float clampedOffset = Math.Clamp(newOffset, -OpenVRConfig.MAX_VERTICAL_OFFSET, OpenVRConfig.MAX_VERTICAL_OFFSET);
        
        ApplyOffset(clampedOffset);
        return clampedOffset;
    }

    private void ThrowIfDisposed()
    {
        if (isDisposed)
        {
            throw new ObjectDisposedException(nameof(OpenVRService));
        }
    }

    public void Dispose()
    {
        if (!isDisposed)
        {
            try
            {
                if (isInitialized && ovrClient?.HasInitialised == true)
                {
                    var compositor = OpenVR.Compositor;
                    if (compositor != null)
                    {
                        compositor.SetTrackingSpace(originalTrackingOrigin);
                        ApplyOffset(0f);
                    }
                }
            }
            finally
            {
                isInitialized = false;
                ovrClient = null;
                isDisposed = true;
            }
        }
        GC.SuppressFinalize(this);
    }

    public bool IsInitialized => isInitialized;
    public float CurrentVerticalOffset => currentVerticalOffset;
} 