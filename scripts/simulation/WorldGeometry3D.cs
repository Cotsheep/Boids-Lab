using Godot;

namespace BoidsLab;

/// <summary>固定三维环绕盒的公共几何，供运动、搜索与调试显示共用。</summary>
public static class WorldGeometry3D
{
    public static Vector3 Wrap(Vector3 point, Aabb bounds)
    {
        if (point.X >= bounds.Position.X && point.X < bounds.End.X
            && point.Y >= bounds.Position.Y && point.Y < bounds.End.Y
            && point.Z >= bounds.Position.Z && point.Z < bounds.End.Z) return point;
        return new Vector3(Mathf.Wrap(point.X, bounds.Position.X, bounds.End.X),
            Mathf.Wrap(point.Y, bounds.Position.Y, bounds.End.Y), Mathf.Wrap(point.Z, bounds.Position.Z, bounds.End.Z));
    }

    /// <summary>输入点均已规范化到盒内。恰好相隔半边长时保留原位移符号。</summary>
    public static Vector3 Offset(Vector3 from, Vector3 to, Aabb bounds)
    {
        Vector3 delta = to - from;
        for (int axis = 0; axis < 3; axis++)
        {
            if (delta[axis] > bounds.Size[axis] * 0.5f) delta[axis] -= bounds.Size[axis];
            else if (delta[axis] < -bounds.Size[axis] * 0.5f) delta[axis] += bounds.Size[axis];
        }
        return delta;
    }

    public static bool WithinRadius(Vector3 offset, float radius) =>
        (double)offset.X * offset.X + (double)offset.Y * offset.Y + (double)offset.Z * offset.Z <= (double)radius * radius;
}
