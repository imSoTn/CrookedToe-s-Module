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
/// A simple 3D vector implementation for movement calculations
/// </summary>
public readonly struct Vector3
{
    /// <summary>X component of the vector</summary>
    public readonly float X;
    /// <summary>Y component of the vector</summary>
    public readonly float Y;
    /// <summary>Z component of the vector</summary>
    public readonly float Z;
    
    public Vector3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }
    
    public static Vector3 Zero => new(0, 0, 0);
    public float Magnitude => MathF.Sqrt(X * X + Y * Y + Z * Z);
    public Vector3 Normalized => Magnitude > 0 ? this / Magnitude : Zero;
    
    public float DistanceTo(Vector3 other)
    {
        float dx = X - other.X;
        float dy = Y - other.Y;
        float dz = Z - other.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
    
    public float Dot(Vector3 other) => X * other.X + Y * other.Y + Z * other.Z;
    
    public float AngleTo(Vector3 other)
    {
        float dot = Dot(other);
        float mags = Magnitude * other.Magnitude;
        return MathF.Acos(dot / mags) * (180f / MathF.PI);
    }
    
    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3 operator *(Vector3 v, float s) => new(v.X * s, v.Y * s, v.Z * s);
    public static Vector3 operator /(Vector3 v, float s) => new(v.X / s, v.Y / s, v.Z / s);
    
    public static bool operator ==(Vector3 a, Vector3 b) => 
        a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    
    public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
    
    public override bool Equals(object? obj) =>
        obj is Vector3 other && this == other;
    
    public override int GetHashCode() =>
        HashCode.Combine(X, Y, Z);
}

/// <summary>
/// Handles smoothing of movement values to reduce jitter
/// </summary>
public class MovementSmoothingSystem
{
    private readonly Queue<float> buffer;
    private readonly int maxSize;
    private float lastValue;
    
    private const float MIN_MOVEMENT_DELTA = 0.001f;
    private const float SMOOTHING_WEIGHT_DECAY = 0.8f;
    
    public MovementSmoothingSystem(int size)
    {
        maxSize = size;
        buffer = new Queue<float>(size);
    }
    
    /// <summary>
    /// Smooths a new value using weighted averaging
    /// </summary>
    public float Smooth(float newValue)
    {
        // Skip if change is too small
        if (Math.Abs(newValue - lastValue) < MIN_MOVEMENT_DELTA)
            return lastValue;
            
        // Add new value to buffer
        buffer.Enqueue(newValue);
        if (buffer.Count > maxSize)
            buffer.Dequeue();
        
        // Calculate weighted average
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
    private const int SMOOTHING_BUFFER_SIZE = 8;
    private MovementStateType currentState = MovementStateType.Idle;
    
    public MovementState()
    {
        horizontalSmoother = new MovementSmoothingSystem(SMOOTHING_BUFFER_SIZE);
        verticalSmoother = new MovementSmoothingSystem(SMOOTHING_BUFFER_SIZE);
    }
    
    // Movement parameters
    public bool IsGrabbed { get; set; }
    public float Stretch { get; set; }
    public float ZPositive { get; set; }
    public float ZNegative { get; set; }
    public float XPositive { get; set; }
    public float XNegative { get; set; }
    public float YPositive { get; set; }
    public float YNegative { get; set; }
    
    public MovementStateType CurrentState => currentState;
    
    /// <summary>
    /// Gets the raw movement vector without any modifiers or smoothing
    /// </summary>
    public Vector3 GetMovementVector()
    {
        return new Vector3(
            XPositive - XNegative,
            YNegative - YPositive,  // Inverted for proper up/down
            ZPositive - ZNegative
        );
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
        
        // Use stretch against the deadzone settings
        if (Stretch > runDeadzone)
        {
            currentState = MovementStateType.Running;
        }
        else if (Stretch > walkDeadzone)
        {
            currentState = MovementStateType.Walking;
        }
        else
        {
            currentState = MovementStateType.Idle;
        }
    }
    
    /// <summary>
    /// Calculates the movement vector with all modifiers applied
    /// </summary>
    public Vector3 CalculateMovement(float strengthMultiplier, float upDownDeadzone, float upDownCompensation)
    {
        if (!IsGrabbed)
            return Vector3.Zero;
            
        // Calculate base movement values
        float outputMultiplier = Stretch * strengthMultiplier;
        
        // Calculate raw movement values with proper vertical handling
        float vertical = verticalSmoother.Smooth((ZPositive - ZNegative) * outputMultiplier);
        float horizontal = horizontalSmoother.Smooth((XPositive - XNegative) * outputMultiplier);
        float verticalStretch = GetVerticalStretch();
        
        // If significant vertical movement, stop horizontal movement
        if (verticalStretch >= upDownDeadzone)
        {
            return Vector3.Zero;
        }
        
        // Apply vertical compensation to horizontal movement
        float yModifier = upDownCompensation != 0 ? 
            Math.Max(-1.0f, Math.Min(1.0f - (verticalStretch * upDownCompensation), 1.0f)) : 1.0f;
            
        vertical = yModifier != 0 ? vertical / yModifier : vertical;
        horizontal = yModifier != 0 ? horizontal / yModifier : horizontal;
        
        return new Vector3(horizontal, verticalStretch, vertical);
    }
    
    /// <summary>
    /// Resets all movement smoothing
    /// </summary>
    public void Reset()
    {
        horizontalSmoother.Clear();
        verticalSmoother.Clear();
        currentState = MovementStateType.Idle;
    }
    
    public float GetVerticalStretch() => Math.Abs(YPositive - YNegative);
    public float GetHorizontalStretch() => Math.Max(Math.Abs(XPositive - XNegative), Math.Abs(ZPositive - ZNegative));
} 