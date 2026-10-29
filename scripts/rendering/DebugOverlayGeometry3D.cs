using System;
using System.Collections.Generic;
using Godot;

namespace BoidsLab;

/// <summary>当前画面的调试线段；只查询选中个体，不改写物理步快照与统计。</summary>
public sealed class DebugOverlayGeometry3D
{
    public List<Vector3> RangePoints { get; } = new();
    public List<Vector3> SeparationRangePoints { get; } = new();
    public List<Vector3> NeighborPoints { get; } = new();
    public List<Vector3> VelocityPoints { get; } = new();
    public int PerceptionCount { get; private set; }
    public int SeparationCount { get; private set; }
    public int NeighborCount { get; private set; }
    public int VelocityCount { get; private set; }
    private static readonly Vector3[] UnitSphereLines = BuildUnitSphereLines();

    public void Build(IReadOnlyList<Boid3D> boids, Boid3D? selected, BoidConfig config, Aabb bounds, float unitsPerPixel)
    {
        RangePoints.Clear();
        SeparationRangePoints.Clear();
        NeighborPoints.Clear();
        VelocityPoints.Clear();
        PerceptionCount = SeparationCount = NeighborCount = VelocityCount = 0;
        if (selected is not null)
        {
            Vector3 center = WorldGeometry3D.Wrap(selected.Position, bounds);
            if (config.ShowPerceptionRadius)
            {
                PerceptionCount = 1;
                AddRange(RangePoints, center, config.PerceptionRadius, bounds);
            }
            if (config.ShowSeparationRadius)
            {
                SeparationCount = 1;
                AddRange(SeparationRangePoints, center, MathF.Min(config.SeparationRadius, config.PerceptionRadius), bounds);
            }
            if (config.ShowNeighbors)
                foreach (Boid3D other in boids)
                {
                    if (other == selected) continue;
                    Vector3 offset = WorldGeometry3D.Offset(center, WorldGeometry3D.Wrap(other.Position, bounds), bounds);
                    if (!WorldGeometry3D.WithinRadius(offset, config.PerceptionRadius)) continue;
                    NeighborCount++;
                    AddWrappedLine(NeighborPoints, center, center + offset, bounds);
                }
        }
        if (!config.ShowVelocity) return;
        foreach (Boid3D boid in boids)
        {
            if (boid.Velocity.LengthSquared() <= 0.0001f) continue;
            VelocityCount++;
            Vector3 origin = WorldGeometry3D.Wrap(boid.Position, bounds);
            Vector3 direction = boid.Velocity.Normalized();
            float length = Mathf.Clamp(boid.Velocity.Length() * 0.25f, 8 * unitsPerPixel, 80 * unitsPerPixel);
            // 小盒中避免一个箭头绕行多圈，仍保留方向信息。
            length = Mathf.Min(length, 0.4f * Mathf.Min(bounds.Size.X, Mathf.Min(bounds.Size.Y, bounds.Size.Z)));
            Vector3 tip = origin + direction * length;
            Vector3 up = Mathf.Abs(direction.Dot(Vector3.Up)) < 0.99f ? Vector3.Up : Vector3.Right;
            Vector3 side = direction.Cross(up).Normalized();
            float head = Mathf.Min(5 * unitsPerPixel, length * 0.4f);
            AddWrappedLine(VelocityPoints, origin, tip, bounds);
            AddWrappedLine(VelocityPoints, tip, tip - direction * head + side * head * 0.6f, bounds);
            AddWrappedLine(VelocityPoints, tip, tip - direction * head - side * head * 0.6f, bounds);
        }
    }

    private static void AddRange(List<Vector3> output, Vector3 center, float radius, Aabb bounds)
    {
        if (radius >= bounds.Size.Length() * 0.5f)
        {
            // 环绕距离下感知已覆盖整个世界；以盒框表示全域，不画虚假的内部球面。
            for (int i = 0; i < 8; i++)
                foreach (int bit in new[] { 1, 2, 4 })
                    if ((i & bit) == 0)
                    {
                        output.Add(Corner(bounds, i));
                        output.Add(Corner(bounds, i | bit));
                    }
            return;
        }
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    Vector3 imageCenter = center + new Vector3(x, y, z) * bounds.Size;
                    // 只绘制离该周期球心最近的区域，避免大半径下出现重叠球面的内部边线。
                    Vector3 min = bounds.Position.Max(imageCenter - bounds.Size * 0.5f);
                    Vector3 max = bounds.End.Min(imageCenter + bounds.Size * 0.5f);
                    if (min.X >= max.X || min.Y >= max.Y || min.Z >= max.Z) continue;
                    var clip = new Aabb(min, max - min);
                    for (int i = 0; i < UnitSphereLines.Length; i += 2)
                        AddClippedLine(output, imageCenter + UnitSphereLines[i] * radius,
                            imageCenter + UnitSphereLines[i + 1] * radius, clip);
                }
    }

    private static Vector3 Corner(Aabb bounds, int index) => bounds.Position + new Vector3(
        (index & 1) == 0 ? 0 : bounds.Size.X, (index & 2) == 0 ? 0 : bounds.Size.Y, (index & 4) == 0 ? 0 : bounds.Size.Z);

    private static Vector3[] BuildUnitSphereLines()
    {
        const int segments = 64;
        var points = new List<Vector3>();
        // 六条经线环、赤道与两条纬线环；线框显示三维范围，不创建实体球或碰撞节点。
        for (int ring = 0; ring < 9; ring++)
            for (int segment = 0; segment < segments; segment++)
            {
                points.Add(Point(Mathf.Tau * segment / segments));
                points.Add(Point(Mathf.Tau * (segment + 1) / segments));
                Vector3 Point(float angle)
                {
                    if (ring < 6)
                    {
                        float longitude = Mathf.Pi * ring / 6;
                        return new Vector3(Mathf.Cos(longitude) * Mathf.Cos(angle), Mathf.Sin(angle), Mathf.Sin(longitude) * Mathf.Cos(angle));
                    }
                    float latitude = (ring - 7) * Mathf.Pi / 4;
                    return new Vector3(Mathf.Cos(latitude) * Mathf.Cos(angle), Mathf.Sin(latitude), Mathf.Cos(latitude) * Mathf.Sin(angle));
                }
            }
        return points.ToArray();
    }

    private static void AddWrappedLine(List<Vector3> output, Vector3 a, Vector3 b, Aabb bounds)
    {
        // 最短邻居位移与受限长度箭头，每轴最多跨一次边界；周期副本裁剪后衔接到对侧。
        for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    Vector3 shift = new Vector3(x, y, z) * bounds.Size;
                    AddClippedLine(output, a + shift, b + shift, bounds);
                }
    }

    private static void AddClippedLine(List<Vector3> output, Vector3 a, Vector3 b, Aabb bounds)
    {
        float first = 0, last = 1;
        Vector3 direction = b - a;
        for (int axis = 0; axis < 3; axis++)
        {
            if (direction[axis] == 0)
            {
                if (a[axis] < bounds.Position[axis] || a[axis] > bounds.End[axis]) return;
                continue;
            }
            float start = (bounds.Position[axis] - a[axis]) / direction[axis];
            float end = (bounds.End[axis] - a[axis]) / direction[axis];
            first = MathF.Max(first, MathF.Min(start, end));
            last = MathF.Min(last, MathF.Max(start, end));
            if (last <= first) return;
        }
        Vector3 from = a + direction * first;
        Vector3 to = a + direction * last;
        if (from.DistanceSquaredTo(to) < 0.000001f) return;
        output.Add(from);
        output.Add(to);
    }
}
