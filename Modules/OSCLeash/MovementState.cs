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
    Turning
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
    private MovementStateType currentState = MovementStateType.Idle;
    private Vector3 cachedMovementVector;
    private bool movementVectorDirty = true;
    private Vector3 lastMovement = Vector3.Zero;
    private readonly float smoothing;
    private readonly float verticalSmoothing;
    
    public MovementState(float smoothing = 0.5f, float verticalSmoothing = 0.65f)
    {
        this.smoothing = smoothing;
        this.verticalSmoothing = verticalSmoothing;
        horizontalSmoother = new MovementSmoothingSystem(3, true, smoothing);
        verticalSmoother = new MovementSmoothingSystem(MovementConfig.DEFAULT_SMOOTHING_BUFFER_SIZE, false, verticalSmoothing);
    }
    
    #region Movement Parameters
    private bool _isGrabbed;
    public bool IsGrabbed 
    { 
        get => _isGrabbed;
        set 
        {
            if (_isGrabbed != value)
            {
                _isGrabbed = value;
                movementVectorDirty = true;
            }
        }
    }
    
    private float _stretch;
    public float Stretch 
    { 
        get => _stretch;
        set 
        {
            if (_stretch != value)
            {
                _stretch = value;
                movementVectorDirty = true;
            }
        }
    }
    #endregion
    
    #region Movement Values
    private float _zPositive, _zNegative, _xPositive, _xNegative, _yPositive, _yNegative;
    
    public float ZPositive 
    { 
        get => _zPositive;
        set 
        {
            if (_zPositive != value)
            {
                _zPositive = value;
                movementVectorDirty = true;
            }
        }
    }
    
    public float ZNegative 
    { 
        get => _zNegative;
        set 
        {
            if (_zNegative != value)
            {
                _zNegative = value;
                movementVectorDirty = true;
            }
        }
    }
    
    public float XPositive 
    { 
        get => _xPositive;
        set 
        {
            if (_xPositive != value)
            {
                _xPositive = value;
                movementVectorDirty = true;
            }
        }
    }
    
    public float XNegative 
    { 
        get => _xNegative;
        set 
        {
            if (_xNegative != value)
            {
                _xNegative = value;
                movementVectorDirty = true;
            }
        }
    }
    
    public float YPositive 
    { 
        get => _yPositive;
        set 
        {
            if (_yPositive != value)
            {
                _yPositive = value;
                movementVectorDirty = true;
            }
        }
    }
    
    public float YNegative 
    { 
        get => _yNegative;
        set 
        {
            if (_yNegative != value)
            {
                _yNegative = value;
                movementVectorDirty = true;
            }
        }
    }
    #endregion
    
    public MovementStateType CurrentState => currentState;
    
    /// <summary>
    /// Gets the raw movement vector without any modifiers or smoothing
    /// </summary>
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
    
    /// <summary>
    /// Updates the movement state based on current values and thresholds
    /// </summary>
    public void UpdateState(float walkDeadzone, float runDeadzone)
    {
        if (!IsGrabbed)
        {
            currentState = MovementStateType.Idle;
            return;
        }
        
        currentState = Stretch > runDeadzone ? MovementStateType.Running :
                      Stretch > walkDeadzone ? MovementStateType.Walking :
                      MovementStateType.Idle;
    }
    
    /// <summary>
    /// Calculates the movement vector with all modifiers applied
    /// </summary>
    public Vector3 CalculateMovement(float strengthMultiplier, float upDownDeadzone, float upDownCompensation)
    {
        if (!IsGrabbed)
        {
            if (lastMovement != Vector3.Zero)
            {
                lastMovement = new Vector3(
                    lastMovement.X * smoothing,
                    lastMovement.Y * smoothing,
                    lastMovement.Z * smoothing
                );
                
                if (lastMovement.Magnitude < upDownDeadzone)
                    lastMovement = Vector3.Zero;
                    
                return lastMovement;
            }
            return Vector3.Zero;
        }
            
        float outputMultiplier = Stretch * strengthMultiplier;
        float verticalStretch = GetVerticalStretch();
        
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
                    
                return lastMovement;
            }
            return new Vector3(0, verticalStretch, 0);
        }
            
        Vector3 movement = GetMovementVector();
        
        bool isChangingDirection = Math.Sign(movement.X) != Math.Sign(lastMovement.X) ||
                                 Math.Sign(movement.Z) != Math.Sign(lastMovement.Z);
        
        float currentLerpFactor = isChangingDirection ? 
            (1f - smoothing) * 1.5f : 
            (1f - smoothing);
        
        if (upDownCompensation < upDownDeadzone)
        {
            Vector3 rawMovement = movement * outputMultiplier;
            lastMovement = new Vector3(
                lastMovement.X + (rawMovement.X - lastMovement.X) * currentLerpFactor,
                rawMovement.Y,
                lastMovement.Z + (rawMovement.Z - lastMovement.Z) * currentLerpFactor
            );
            return lastMovement;
        }
        
        float yModifier = Math.Max(-1.0f, Math.Min(1.0f - (verticalStretch * upDownCompensation), 1.0f));
        float horizontalMultiplier = outputMultiplier / (yModifier != 0 ? yModifier : 1.0f);
        
        Vector3 compensatedMovement = new Vector3(
            movement.X * horizontalMultiplier,
            verticalStretch,
            movement.Z * horizontalMultiplier
        );
        
        lastMovement = new Vector3(
            lastMovement.X + (compensatedMovement.X - lastMovement.X) * currentLerpFactor,
            compensatedMovement.Y,
            lastMovement.Z + (compensatedMovement.Z - lastMovement.Z) * currentLerpFactor
        );
        
        return lastMovement;
    }
    
    /// <summary>
    /// Resets all movement smoothing
    /// </summary>
    public void Reset()
    {
        horizontalSmoother.Clear();
        verticalSmoother.Clear();
        currentState = MovementStateType.Idle;
        movementVectorDirty = true;
    }
    
    public float GetVerticalStretch()
    {
        return Math.Abs(YPositive - YNegative);
    }
    
    public float GetHorizontalStretch()
    {
        return Math.Max(Math.Abs(XPositive - XNegative), Math.Abs(ZPositive - ZNegative));
    }
} 