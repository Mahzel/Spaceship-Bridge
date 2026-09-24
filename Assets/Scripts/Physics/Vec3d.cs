using System;
using UnityEngine;

/// <summary>
/// Minimal double-precision 3-vector for orbit propagation. Vector3 is single precision: at 15 AU (1500 game
/// units) one float step is ~1e-4 units, about 180 km, which is far too coarse to difference positions into
/// velocities or to carry a ship's state frame after frame. Everything orbital that feeds back into itself
/// uses this instead; convert to Vector3 only at the boundary (rendering, UI).
/// </summary>
public struct Vec3d
{
    public double x, y, z;

    public Vec3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
    public Vec3d(Vector3 v) { x = v.x; y = v.y; z = v.z; }

    public static readonly Vec3d zero = new Vec3d(0, 0, 0);

    public static Vec3d operator +(Vec3d a, Vec3d b) => new Vec3d(a.x + b.x, a.y + b.y, a.z + b.z);
    public static Vec3d operator -(Vec3d a, Vec3d b) => new Vec3d(a.x - b.x, a.y - b.y, a.z - b.z);
    public static Vec3d operator -(Vec3d a) => new Vec3d(-a.x, -a.y, -a.z);
    public static Vec3d operator *(Vec3d a, double k) => new Vec3d(a.x * k, a.y * k, a.z * k);
    public static Vec3d operator *(double k, Vec3d a) => new Vec3d(a.x * k, a.y * k, a.z * k);
    public static Vec3d operator /(Vec3d a, double k) => new Vec3d(a.x / k, a.y / k, a.z / k);

    public static double Dot(Vec3d a, Vec3d b) => a.x * b.x + a.y * b.y + a.z * b.z;
    public static Vec3d Cross(Vec3d a, Vec3d b) =>
        new Vec3d(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);

    public double Magnitude => Math.Sqrt(x * x + y * y + z * z);
    public double SqrMagnitude => x * x + y * y + z * z;
    public Vec3d Normalized { get { double m = Magnitude; return m > 0 ? this / m : zero; } }

    public Vector3 ToVector3() => new Vector3((float)x, (float)y, (float)z);
    public override string ToString() => $"({x:G6}, {y:G6}, {z:G6})";
}
