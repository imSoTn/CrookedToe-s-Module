using System;
using System.Collections.Generic;
using System.Linq;

namespace CrookedToe.Modules.OSCLeash;

/// <summary>
/// Represents the current state of movement
/// </summary>
public enum MovementStateType
{
    /// <summary>No movement</summary>
    Idle,
    /// <summary>Walking movement</summary>
    Walking,
    /// <summary>Running movement</summary>
    Running,
    /// <summary>Vertical movement</summary>
    VerticalMovement,
    /// <summary>Turning movement</summary>
    Turning,
    /// <summary>Returning to original position</summary>
    ReturnToOrigin
}

/// <summary>
/// Configuration constants for movement smoothing
/// </summary>
internal static class MovementConfig
{
    public const float HORIZONTAL_MIN_DELTA = 0.0002f;
    public const float VERTICAL_MIN_DELTA = 0.0005f;
    public const float SIGNIFICANT_CHANGE_THRESHOLD = 0.15f;
    public const int DEFAULT_SMOOTHING_BUFFER_SIZE = 5;
}

/// <summary>
/// Handles smoothing of movement values to reduce jitter
/// </summary>
public class MovementSmoothingSystem
{
    private readonly Queue<float> buffer;
    private readonly int maxSize;
    private float lastValue;
    private float[] weights;
    private readonly bool isHorizontal;
    
    public MovementSmoothingSystem(int size, bool isHorizontal = false, float smoothing = 0.5f)
    {
        this.isHorizontal = isHorizontal;
        maxSize = isHorizontal ? 4 : size;
        buffer = new Queue<float>(maxSize);
        
        weights = new float[maxSize];
        float weight = 1f;
        float decay = smoothing;
        for (int i = 0; i < maxSize; i++)
        {
            weights[i] = weight;
            weight *= decay;
        }
    }
    
    /// <summary>
    /// Smooths a new value using weighted averaging
    /// </summary>
    public float Smooth(float newValue)
    {
        float delta = newValue - lastValue;
        float minDelta = isHorizontal ? MovementConfig.HORIZONTAL_MIN_DELTA : MovementConfig.VERTICAL_MIN_DELTA;
        
        if (isHorizontal && Math.Abs(delta) > MovementConfig.SIGNIFICANT_CHANGE_THRESHOLD)
        {
            lastValue = lastValue + delta * 0.6f;
            buffer.Clear();
            buffer.Enqueue(lastValue);
            return lastValue;
        }
        
        if (Math.Abs(delta) < minDelta)
            return lastValue;
            
        buffer.Enqueue(newValue);
        if (buffer.Count > maxSize)
            buffer.Dequeue();
            
        if (isHorizontal && buffer.Count > 1)
        {
            var values = buffer.ToArray();
            float weightedSum = 0;
            float weightSum = 0;
            
            float changeRate = Math.Min(1.0f, Math.Abs(delta) * 2.0f);
            float progressiveWeight = 1.0f;
            
            for (int idx = values.Length - 1; idx >= 0; idx--)
            {
                weightedSum += values[idx] * progressiveWeight;
                weightSum += progressiveWeight;
                progressiveWeight *= (1.0f - changeRate) * 0.5f + 0.5f;
            }
            
            lastValue = weightedSum / weightSum;
            return lastValue;
        }
        
        float sum = 0;
        float totalWeight = 0;
        int i = buffer.Count - 1;
        
        foreach (var value in buffer)
        {
            if (i < 0) break;
            sum += value * weights[i];
            totalWeight += weights[i];
            i--;
        }
        
        lastValue = sum / totalWeight;
        return lastValue;
    }
    
    /// <summary>
    /// Resets the smoothing buffer
    /// </summary>
    public void Clear()
    {
        buffer.Clear();
        lastValue = 0;
    }
}

/// <summary>
/// Manages the movement state and calculations
/// </summary>
public class MovementState
{
    private readonly MovementSmoothingSystem horizontalSmoother;
    private readonly MovementSmoothingSystem verticalSmoother;
    private Vector3 cachedMovementVector;
    private bool movementVectorDirty = true;
    private Vector3 lastMovement = Vector3.Zero;
    private Vector3 originalPosition = Vector3.Zero;
    private bool hasStoredOriginalPosition;
    private const float RETURN_COMPLETION_THRESHOLD = 0.01f;
    private readonly float smoothing;
    private readonly float verticalSmoothing;
    
    public MovementState(float smoothing = 0.5f, float verticalSmoothing = 0.65f)
    {
        this.smoothing = smoothing;
        this.verticalSmoothing = verticalSmoothing;
        horizontalSmoother = new MovementSmoothingSystem(3, true, smoothing);
        verticalSmoother = new MovementSmoothingSystem(MovementConfig.DEFAULT_SMOOTHING_BUFFER_SIZE, false, verticalSmoothing);
    }

    #region Properties with Change Detection
    private bool _isGrabbed;
    private float _stretch;
    private float _zPositive, _zNegative, _xPositive, _xNegative, _yPositive, _yNegative;

    public bool IsGrabbed 
    { 
        get => _isGrabbed;
        set { if (_isGrabbed != value) { _isGrabbed = value; movementVectorDirty = true; } }
    }
    
    public float Stretch 
    { 
        get => _stretch;
        set { if (_stretch != value) { _stretch = value; movementVectorDirty = true; } }
    }
    
    public float ZPositive 
    { 
        get => _zPositive;
        set { if (_zPositive != value) { _zPositive = value; movementVectorDirty = true; } }
    }
    
    public float ZNegative 
    { 
        get => _zNegative;
        set { if (_zNegative != value) { _zNegative = value; movementVectorDirty = true; } }
    }
    
    public float XPositive 
    { 
        get => _xPositive;
        set { if (_xPositive != value) { _xPositive = value; movementVectorDirty = true; } }
    }
    
    public float XNegative 
    { 
        get => _xNegative;
        set { if (_xNegative != value) { _xNegative = value; movementVectorDirty = true; } }
    }
    
    public float YPositive 
    { 
        get => _yPositive;
        set { if (_yPositive != value) { _yPositive = value; movementVectorDirty = true; } }
    }
    
    public float YNegative 
    { 
        get => _yNegative;
        set { if (_yNegative != value) { _yNegative = value; movementVectorDirty = true; } }
    }
    #endregion

    public MovementStateType CurrentState { get; private set; } = MovementStateType.Idle;
    public bool IsReturningToOrigin => CurrentState == MovementStateType.ReturnToOrigin;

    public Vector3 GetMovementVector()
    {
        if (!movementVectorDirty)
            return cachedMovementVector;
            
        cachedMovementVector = new Vector3(
            XPositive - XNegative,
            YNegative - YPositive,
            ZPositive - ZNegative
        );
        
        movementVectorDirty = false;
        return cachedMovementVector;
    }

    public void UpdateState(float walkDeadzone, float runDeadzone)
    {
        if (!IsGrabbed)
        {
            if (!hasStoredOriginalPosition)
            {
                originalPosition = lastMovement;
                hasStoredOriginalPosition = true;
            }
            
            CurrentState = lastMovement.DistanceTo(originalPosition) > RETURN_COMPLETION_THRESHOLD
                ? MovementStateType.ReturnToOrigin
                : MovementStateType.Idle;

            if (CurrentState == MovementStateType.Idle)
                hasStoredOriginalPosition = false;

            return;
        }
        
        hasStoredOriginalPosition = false;
        CurrentState = Stretch > runDeadzone ? MovementStateType.Running :
                      Stretch > walkDeadzone ? MovementStateType.Walking :
                      MovementStateType.Idle;
    }

    public Vector3 CalculateMovement(float strengthMultiplier, float upDownDeadzone, float upDownCompensation)
    {
        if (!IsGrabbed)
        {
            if (lastMovement == Vector3.Zero)
                return Vector3.Zero;

            lastMovement *= smoothing;
            if (lastMovement.Magnitude < upDownDeadzone)
                lastMovement = Vector3.Zero;
            return lastMovement;
        }

        float verticalStretch = Math.Abs(YPositive - YNegative);
        if (verticalStretch >= upDownDeadzone)
        {
            if (lastMovement != Vector3.Zero)
            {
                lastMovement = new Vector3(
                    lastMovement.X * verticalSmoothing,
                    verticalStretch,
                    lastMovement.Z * verticalSmoothing
                );

                if (Math.Abs(lastMovement.X) < upDownDeadzone && Math.Abs(lastMovement.Z) < upDownDeadzone)
                    lastMovement = new Vector3(0, verticalStretch, 0);
            }
            else
            {
                lastMovement = new Vector3(0, verticalStretch, 0);
            }
            return lastMovement;
        }

        Vector3 movement = GetMovementVector();
        float outputMultiplier = Stretch * strengthMultiplier;

        if (upDownCompensation < upDownDeadzone)
        {
            Vector3 rawMovement = movement * outputMultiplier;
            lastMovement = InterpolateMovement(rawMovement);
            return lastMovement;
        }

        float yModifier = Math.Clamp(1.0f - (verticalStretch * upDownCompensation), -1.0f, 1.0f);
        float horizontalMultiplier = outputMultiplier / (yModifier != 0 ? yModifier : 1.0f);

        Vector3 compensatedMovement = new Vector3(
            movement.X * horizontalMultiplier,
            verticalStretch,
            movement.Z * horizontalMultiplier
        );

        lastMovement = InterpolateMovement(compensatedMovement);
        return lastMovement;
    }

    private Vector3 InterpolateMovement(Vector3 targetMovement)
    {
        bool isChangingDirection = Math.Sign(targetMovement.X) != Math.Sign(lastMovement.X) ||
                                 Math.Sign(targetMovement.Z) != Math.Sign(lastMovement.Z);

        float lerpFactor = isChangingDirection ? (1f - smoothing) * 1.5f : (1f - smoothing);

        return new Vector3(
            lastMovement.X + (targetMovement.X - lastMovement.X) * lerpFactor,
            targetMovement.Y,
            lastMovement.Z + (targetMovement.Z - lastMovement.Z) * lerpFactor
        );
    }

    public void Reset()
    {
        horizontalSmoother.Clear();
        verticalSmoother.Clear();
        CurrentState = MovementStateType.Idle;
        movementVectorDirty = true;
        lastMovement = Vector3.Zero;
        hasStoredOriginalPosition = false;
    }

    public Vector3 GetReturnToOriginVector()
    {
        if (!IsReturningToOrigin)
            return Vector3.Zero;

        Vector3 direction = originalPosition - lastMovement;
        float magnitude = direction.Magnitude;
        
        return magnitude < float.Epsilon ? Vector3.Zero : direction / magnitude;
    }
} 