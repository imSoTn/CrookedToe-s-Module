using System;

namespace CrookedToe.Modules.OSCLeash;

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