using System;
using Valve.VR;
using VRCOSC.App.SDK.OVR;

namespace CrookedToe.Modules.OSCLeash;

public class OpenVRService
{
    private readonly Action<string> logger;
    private bool isInitialized;
    private float currentVerticalOffset;
    private float targetVerticalOffset;
    private float verticalVelocity;
    private ETrackingUniverseOrigin originalTrackingOrigin;
    private CVRCompositor? compositor;
    
    private const float GRAVITY = 9.81f;
    private const float TERMINAL_VELOCITY = -15.0f;
    private const float VERTICAL_SMOOTHING = 0.95f;
    
    public OpenVRService(Action<string> logger)
    {
        this.logger = logger;
    }
    
    public bool Initialize(OVRClient? ovrClient)
    {
        if (ovrClient == null || !ovrClient.HasInitialised)
        {
            logger("OpenVR is not initialized. Vertical movement will be disabled until OpenVR is available.");
            Reset();
            return false;
        }
        
        compositor = OpenVR.Compositor;
        if (compositor == null)
        {
            Reset();
            return false;
        }
        
        originalTrackingOrigin = compositor.GetTrackingSpace();
        isInitialized = true;
        
        UpdateOffset();
        return true;
    }
    
    public void Reset()
    {
        isInitialized = false;
        currentVerticalOffset = 0;
        targetVerticalOffset = 0;
        verticalVelocity = 0;
        
        // Force position to exactly 0 when resetting
        ApplyOffset(0f);
        
        compositor = null;
    }
    
    public void UpdateOffset()
    {
        var chaperoneSetup = OpenVR.ChaperoneSetup;
        if (chaperoneSetup != null)
        {
            var standingZeroPose = new HmdMatrix34_t();
            chaperoneSetup.GetWorkingStandingZeroPoseToRawTrackingPose(ref standingZeroPose);
            targetVerticalOffset = standingZeroPose.m7;
        }
    }
    
    public void ApplyOffset(float newOffset)
    {
        var chaperoneSetup = OpenVR.ChaperoneSetup;
        if (chaperoneSetup == null)
            return;
            
        var standingZeroPose = new HmdMatrix34_t();
        chaperoneSetup.GetWorkingStandingZeroPoseToRawTrackingPose(ref standingZeroPose);
        
        currentVerticalOffset = newOffset;
        standingZeroPose.m7 = newOffset;
        
        chaperoneSetup.SetWorkingStandingZeroPoseToRawTrackingPose(ref standingZeroPose);
        chaperoneSetup.CommitWorkingCopy(EChaperoneConfigFile.Live);
    }
    
    public float UpdateVerticalMovement(float deltaTime, MovementState state, float verticalDeadzone, 
        float angleThreshold, float verticalMultiplier, float smoothing)
    {
        if (!isInitialized)
        {
            return 0f;
        }
        
        // If not grabbed, start falling from rest
        if (!state.IsGrabbed)
        {
            if (IsGrabbed)  // Transition from grabbed to not grabbed
            {
                verticalVelocity = 0f;  // Reset velocity when releasing
                IsGrabbed = false;
            }
            return ApplyGravity(deltaTime);
        }
        
        IsGrabbed = true;  // Track grab state
        
        // When grabbed, check for movement
        float stepSize = CalculateStepSize(state, verticalDeadzone, angleThreshold);
        
        // Small deadzone for stable holding
        if (Math.Abs(stepSize) < 0.05f)
        {
            verticalVelocity = 0f;  // Stop any movement in deadzone
            return currentVerticalOffset;
        }
        
        return ApplyMovement(deltaTime, stepSize, verticalMultiplier, smoothing);
    }
    
    private float CalculateStepSize(MovementState state, float verticalDeadzone, float angleThreshold)
    {
        Vector3 movement = state.GetMovementVector();
        float horizontalX = movement.X;
        float horizontalZ = movement.Z;
        float horizontalMagnitude = MathF.Sqrt(horizontalX * horizontalX + horizontalZ * horizontalZ);
        
        if (Math.Abs(movement.Y) < verticalDeadzone)
            return 0f;
        
        float pullAngle = MathF.Atan2(MathF.Abs(movement.Y), horizontalMagnitude) * (180f / MathF.PI);
        if (pullAngle < angleThreshold)
            return 0f;
            
        return movement.Y;
    }
    
    private float ApplyGravity(float deltaTime)
    {
        // If we're very close to the original position (0) and moving slowly, stop
        if (Math.Abs(currentVerticalOffset) < 0.01f && Math.Abs(verticalVelocity) < 0.1f)
        {
            verticalVelocity = 0f;
            return 0f;
        }

        // Apply gravity towards 0 (original position)
        float gravityDirection = -Math.Sign(currentVerticalOffset);  // Always points towards 0
        verticalVelocity += GRAVITY * gravityDirection * deltaTime;
        verticalVelocity = Math.Clamp(verticalVelocity, TERMINAL_VELOCITY, -TERMINAL_VELOCITY);
            
        float newOffset = currentVerticalOffset + verticalVelocity * deltaTime;
        
        // If we've crossed the original position (0), stop at 0
        if ((currentVerticalOffset > 0f && newOffset < 0f) || 
            (currentVerticalOffset < 0f && newOffset > 0f))
        {
            verticalVelocity = 0f;
            return 0f;
        }
        
        return newOffset;
    }
    
    private float ApplyMovement(float deltaTime, float stepSize, float verticalMultiplier, float smoothing)
    {
        float maxOffset = 2.0f; // Maximum 2 meters up/down
        
        // Calculate the target velocity based on input
        float targetVelocity = stepSize * verticalMultiplier;
        
        // Smoothly interpolate to the target velocity
        verticalVelocity = verticalVelocity * smoothing + targetVelocity * (1f - smoothing);
        
        // Update position with velocity
        float newOffset = currentVerticalOffset + verticalVelocity * deltaTime;
        
        // Clamp the position within bounds
        return Math.Clamp(newOffset, -maxOffset, maxOffset);
    }
    
    public void Cleanup()
    {
        if (isInitialized && compositor != null)
        {
            compositor.SetTrackingSpace(originalTrackingOrigin);
            compositor = null;
        }
    }
    
    public bool IsInitialized => isInitialized;
    public float CurrentVerticalOffset => currentVerticalOffset;
    public bool IsGrabbed { get; set; }
} 