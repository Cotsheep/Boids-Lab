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

    public void Build(IReadOnlyList<Boid3D> boids, Boid3D? selected, BoidConfig config, Func<Vector3, float> pixelScale)
    {
        RangePoints.Clear();
        SeparationRangePoints.Clear();
        NeighborPoints.Clear();
        VelocityPoints.Clear();
        PerceptionCount = SeparationCount = NeighborCount = VelocityCount = 0;
        if (selected is not null)
        {
            Vector3 center = selected.Position;
            if (config.ShowPerceptionRadius)
            {
                PerceptionCount = 1;
                AddRange(RangePoints, center, config.PerceptionRadius);
            }
            if (config.ShowSeparationRadius)
            {
                SeparationCount = 1;
                AddRange(SeparationRangePoints, center, MathF.Min(config.SeparationRadius, config.PerceptionRadius));
            }
            if (config.ShowNeighbors)
                foreach (Boid3D other in boids)
                {
                    if (other == selected) continue;
                    Vector3 offset = other.Position - center;
                    if (!WorldGeometry3D.WithinRadius(offset, config.PerceptionRadius)) continue;
                    NeighborCount++;
                    AddLine(NeighborPoints, center, center + offset);
                }
        }
        if (!config.ShowVelocity) return;
        foreach (Boid3D boid in boids)
        {
            if (boid.Velocity.LengthSquared() <= 0.0001f) continue;
            VelocityCount++;
            Vector3 origin = boid.Position;
            float unitsPerPixel = pixelScale(boid.Position);
            Vector3 direction = boid.Velocity.Normalized();
            float length = Mathf.Clamp(boid.Velocity.Length() * 0.25f, 8 * unitsPerPixel, 80 * unitsPerPixel);
            Vector3 tip = origin + direction * length;
            Vector3 up = Mathf.Abs(direction.Dot(Vector3.Up)) < 0.99f ? Vector3.Up : Vector3.Right;
            Vector3 side = direction.Cross(up).Normalized();
            float head = Mathf.Min(5 * unitsPerPixel, length * 0.4f);
            AddLine(VelocityPoints, origin, tip);
            AddLine(VelocityPoints, tip, tip - direction * head + side * head * 0.6f);
            AddLine(VelocityPoints, tip, tip - direction * head - side * head * 0.6f);
        }
    }

    private static void AddRange(List<Vector3> output, Vector3 center, float radius)
    {
        foreach (Vector3 point in UnitSphereLines) output.Add(center + point * radius);
    }

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

    private static void AddLine(List<Vector3> output, Vector3 a, Vector3 b)
    {
        output.Add(a);
        output.Add(b);
    }
}
