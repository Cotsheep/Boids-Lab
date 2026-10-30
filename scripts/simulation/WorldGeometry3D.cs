using Godot;

namespace BoidsLab;

/// <summary>三维世界统一使用实际坐标及直接欧氏距离。</summary>
public static class WorldGeometry3D
{
    public static bool WithinRadius(Vector3 offset, float radius) =>
        (double)offset.X * offset.X + (double)offset.Y * offset.Y + (double)offset.Z * offset.Z <= (double)radius * radius;
}
