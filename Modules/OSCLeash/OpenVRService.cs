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
    private float targetVerticalOffset;
    private float verticalVelocity;
    private float grabTimer;
    private ETrackingUniverseOrigin originalTrackingOrigin;
    private HmdMatrix34_t standingZeroPose;
    private float lastAppliedOffset;
    private bool isDisposed;
    private OVRClient? ovrClient;

    public OpenVRService(Action<string> logCallback)
    {
        this.logCallback = logCallback ?? throw new ArgumentNullException(nameof(logCallback));
        standingZeroPose = new HmdMatrix34_t();
    }

    /// <summary>
    /// Initializes the OpenVR service with the provided client
    /// </summary>
    public bool Initialize(OVRClient? client)
    {
        ThrowIfDisposed();
        
        if (!isEnabled)
        {
            Reset();
            return false;
        }

        if (client == null || !client.HasInitialised)
        {
            logCallback("OpenVR is not initialized. Vertical movement will be disabled until OpenVR is available.");
            Reset();
            return false;
        }

        ovrClient = client;

        if (isInitialized)
        {
            return true;
        }

        try
        {
            var compositor = OpenVR.Compositor;
            var chaperoneSetup = OpenVR.ChaperoneSetup;
            
            if (compositor == null || chaperoneSetup == null)
            {
                logCallback("Failed to initialize OpenVR: Required components not available");
                Reset();
                return false;
            }

            originalTrackingOrigin = compositor.GetTrackingSpace();
            chaperoneSetup.GetWorkingStandingZeroPoseToRawTrackingPose(ref standingZeroPose);
            currentVerticalOffset = standingZeroPose.m7;
            isInitialized = true;
            UpdateOffset();
            return true;
        }
        catch (Exception ex)
        {
            logCallback($"Failed to initialize OpenVR: {ex.Message}");
            Reset();
            return false;
        }
    }

    public void Enable()
    {
        isEnabled = true;
        if (!isInitialized)
        {
            Reset();
        }
    }

    public void Disable()
    {
        isEnabled = false;
        Reset();
    }

    /// <summary>
    /// Resets the service state without releasing resources
    /// </summary>
    public void Reset()
    {
        ThrowIfDisposed();
        
        ResetOpenVRState();
        ResetInternalState();
    }

    private void ResetOpenVRState()
    {
        if (isInitialized && ovrClient?.HasInitialised == true)
        {
            try
            {
                var compositor = OpenVR.Compositor;
                var chaperoneSetup = OpenVR.ChaperoneSetup;
                
                if (compositor != null)
                {
                    compositor.SetTrackingSpace(originalTrackingOrigin);
                }
                
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
    }

    private void ResetInternalState()
    {
        isInitialized = false;
        verticalVelocity = 0;
        grabTimer = 0;
        currentVerticalOffset = 0;
        lastAppliedOffset = 0;
    }

    private void UpdateOffset()
    {
        if (ovrClient?.HasInitialised == true)
        {
            var chaperoneSetup = OpenVR.ChaperoneSetup;
            if (chaperoneSetup != null)
            {
                chaperoneSetup.GetWorkingStandingZeroPoseToRawTrackingPose(ref standingZeroPose);
                targetVerticalOffset = standingZeroPose.m7;
            }
        }
    }

    /// <summary>
    /// Applies a vertical offset to the player's position
    /// </summary>
    public void ApplyOffset(float newOffset)
    {
        ThrowIfDisposed();
        
        if (!isEnabled || !isInitialized || ovrClient?.HasInitialised != true)
            return;

        try
        {
            var chaperoneSetup = OpenVR.ChaperoneSetup;
            if (chaperoneSetup == null)
                return;

            currentVerticalOffset = newOffset;
            standingZeroPose.m7 = newOffset;
            lastAppliedOffset = newOffset;

            chaperoneSetup.SetWorkingStandingZeroPoseToRawTrackingPose(ref standingZeroPose);
            chaperoneSetup.CommitWorkingCopy(EChaperoneConfigFile.Live);
        }
        catch (Exception ex)
        {
            logCallback($"Error applying offset: {ex.Message}");
            Reset();
        }
    }

    /// <summary>
    /// Updates vertical movement based on the current state and parameters
    /// </summary>
    public float UpdateVerticalMovement(float deltaTime, MovementState state, float verticalDeadzone,
        float angleThreshold, float verticalMultiplier, float smoothing)
    {
        ThrowIfDisposed();
        
        if (!isInitialized || !isEnabled)
            return currentVerticalOffset;

        // If not grabbed, start falling
        if (!state.IsGrabbed)
        {
            if (IsGrabbed)  // Just released
            {
                verticalVelocity = 0f;
                IsGrabbed = false;
                grabTimer = 0f;
            }
            return ApplyGravity(deltaTime);
        }

        // Handle initial grab
        if (!IsGrabbed)
        {
            // Initial grab setup
            grabTimer = 0f;
            verticalVelocity = 0f;
            UpdateOffset();
            IsGrabbed = true;
            return currentVerticalOffset;
        }

        // Update grab timer
        grabTimer += deltaTime;

        // During initial grab delay, ignore vertical movement completely
        if (grabTimer < OpenVRConfig.GRAB_DELAY)
        {
            verticalVelocity = 0f;
            return currentVerticalOffset;
        }

        // After delay, calculate movement
        float stepSize = CalculateStepSize(state, verticalDeadzone, angleThreshold);
        if (Math.Abs(stepSize) < OpenVRConfig.MOVEMENT_DEADZONE)
        {
            verticalVelocity = 0f;
            return currentVerticalOffset;
        }

        return ApplyMovement(deltaTime, stepSize, verticalMultiplier, smoothing);
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

        // Apply gravity towards original position
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
    public bool IsGrabbed { get; set; }
} 