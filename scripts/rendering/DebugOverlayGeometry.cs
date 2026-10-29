using System;
using System.Collections.Generic;
using Godot;

namespace BoidsLab;

/// <summary>构造当前可见状态的调试线段，不修改模拟快照、计时或邻居统计。</summary>
public sealed class DebugOverlayGeometry
{
    // 每两个点构成一条线，供 CanvasItem.DrawMultiline 一次绘制一组。
    public List<Vector2> RangePoints { get; } = new();
    public List<Vector2> SeparationRangePoints { get; } = new();
    public List<Vector2> VelocityPoints { get; } = new();
    public List<Vector2> NeighborPoints { get; } = new();
    public int PerceptionCount { get; private set; }
    public int SeparationCount { get; private set; }
    public int VelocityCount { get; private set; }
    public int NeighborCount { get; private set; }
    private const int CircleSegments = 96;

    public void Build(IReadOnlyList<Boid2D> boids, Boid2D? selected, BoidConfig config,
        Rect2 bounds, float unitsPerPixel, bool wrap = false)
    {
        RangePoints.Clear();
        SeparationRangePoints.Clear();
        VelocityPoints.Clear();
        NeighborPoints.Clear();
        PerceptionCount = SeparationCount = VelocityCount = NeighborCount = 0;
        if (!bounds.HasArea()) return;

        if (config.ShowPerceptionRadius && selected is not null)
        {
            PerceptionCount = 1;
            AddRange(RangePoints, selected.Position, config.PerceptionRadius, bounds, wrap);
        }
        if (config.ShowSeparationRadius && selected is not null)
        {
            SeparationCount = 1;
            // 与分离规则使用相同的有效半径；不会显示感知范围以外的虚假作用区域。
            float radius = MathF.Min(config.SeparationRadius, config.PerceptionRadius);
            AddRange(SeparationRangePoints, selected.Position, radius, bounds, wrap);
        }

        // 仅显示选中对象的范围时，不再遍历全体个体生成圆弧。
        if (config.ShowVelocity)
        {
            foreach (Boid2D boid in boids)
            {
                if (boid.Velocity.LengthSquared() <= 0.0001f) continue;
                VelocityCount++;
                Vector2 direction = boid.Velocity.Normalized();
                float length = Mathf.Clamp(boid.Velocity.Length() * 0.25f, 8 * unitsPerPixel, 80 * unitsPerPixel);
                Vector2 tip = boid.Position + direction * length;
                Vector2 side = new(-direction.Y, direction.X);
                float head = MathF.Min(5 * unitsPerPixel, length * 0.4f);
                AddLine(VelocityPoints, boid.Position, tip, bounds, wrap);
                AddLine(VelocityPoints, tip, tip - direction * head + side * head * 0.6f, bounds, wrap);
                AddLine(VelocityPoints, tip, tip - direction * head - side * head * 0.6f, bounds, wrap);
            }
        }

        if (!config.ShowNeighbors || selected is null) return;
        // 调试只查询被选中的一个个体，O(N)。暂停/首次运行前也可以立即看到当前关系。
        foreach (Boid2D boid in boids)
        {
            if (boid == selected) continue;
            Vector2 offset = WorldGeometry2D.Offset(selected.Position, boid.Position, wrap ? bounds : null);
            if ((double)offset.X * offset.X + (double)offset.Y * offset.Y
                > (double)config.PerceptionRadius * config.PerceptionRadius) continue;
            NeighborCount++;
            AddLine(NeighborPoints, selected.Position, selected.Position + offset, bounds, wrap);
        }
    }

    private static void AddRange(List<Vector2> output, Vector2 center, float radius, Rect2 bounds, bool wrap)
    {
        if (wrap) { AddWrappedRange(output, center, radius, bounds); return; }
        Vector2 previous = center + new Vector2(radius, 0);
        for (int i = 1; i <= CircleSegments; i++)
        {
            Vector2 next = center + Vector2.FromAngle(Mathf.Tau * i / CircleSegments) * radius;
            AddVisibleLine(output, previous, next, bounds);
            previous = next;
        }
    }

    private static void AddVisibleLine(List<Vector2> output, Vector2 from, Vector2 to, Rect2 bounds)
    {
        // 只裁剪显示，不生成对侧副本，也不改变真实的感知范围。
        if (!ClipLine(ref from, ref to, bounds) || from.DistanceSquaredTo(to) < 1e-10f) return;
        output.Add(from);
        output.Add(to);
    }

    private static void AddLine(List<Vector2> output, Vector2 from, Vector2 to, Rect2 bounds, bool wrap)
    {
        if (wrap) AddWrappedLine(output, from, to, bounds);
        else AddVisibleLine(output, from, to, bounds);
    }

    private static void AddWrappedRange(List<Vector2> output, Vector2 center, float radius, Rect2 bounds)
    {
        // 环绕空间的最大距离是半对角线。全域范围用世界边框表示。
        if (radius >= bounds.Size.Length() * 0.5f)
        {
            Vector2 topRight = new(bounds.End.X, bounds.Position.Y);
            Vector2 bottomLeft = new(bounds.Position.X, bounds.End.Y);
            output.AddRange(new[] { bounds.Position, topRight, topRight, bounds.End,
                bounds.End, bottomLeft, bottomLeft, bounds.Position });
            return;
        }
        // 只保留最短环绕位移区域内的圆弧，再将越界部分接到另一侧。
        // 半径大于某一轴的半宽时，避免绘制并非实际感知边界的重复圆弧。
        var shortestRegion = new Rect2(-bounds.Size * 0.5f, bounds.Size);
        Vector2 previous = new(radius, 0);
        for (int i = 1; i <= CircleSegments; i++)
        {
            Vector2 next = Vector2.FromAngle(Mathf.Tau * i / CircleSegments) * radius;
            Vector2 from = previous;
            Vector2 to = next;
            if (ClipLine(ref from, ref to, shortestRegion))
                AddWrappedLine(output, center + from, center + to, bounds);
            previous = next;
        }
    }

    private static void AddWrappedLine(List<Vector2> output, Vector2 from, Vector2 to, Rect2 bounds)
    {
        // 仅平移可能与视野相交的副本；跨边界连线显示为两侧短线，不横穿屏幕。
        int minX = (int)MathF.Ceiling((bounds.Position.X - MathF.Max(from.X, to.X)) / bounds.Size.X);
        int maxX = (int)MathF.Floor((bounds.End.X - MathF.Min(from.X, to.X)) / bounds.Size.X);
        int minY = (int)MathF.Ceiling((bounds.Position.Y - MathF.Max(from.Y, to.Y)) / bounds.Size.Y);
        int maxY = (int)MathF.Floor((bounds.End.Y - MathF.Min(from.Y, to.Y)) / bounds.Size.Y);
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 shift = new(x * bounds.Size.X, y * bounds.Size.Y);
                Vector2 a = from + shift;
                Vector2 b = to + shift;
                if (!ClipLine(ref a, ref b, bounds) || a.DistanceSquaredTo(b) < 1e-10f) continue;
                output.Add(a);
                output.Add(b);
            }
    }

    private static bool ClipLine(ref Vector2 from, ref Vector2 to, Rect2 rect)
    {
        Vector2 delta = to - from;
        float start = 0;
        float end = 1;
        if (!ClipAxis(from.X, delta.X, rect.Position.X, rect.End.X, ref start, ref end)
            || !ClipAxis(from.Y, delta.Y, rect.Position.Y, rect.End.Y, ref start, ref end)) return false;
        to = from + delta * end;
        from += delta * start;
        return true;
    }

    private static bool ClipAxis(float origin, float delta, float min, float max, ref float start, ref float end)
    {
        if (MathF.Abs(delta) < 1e-7f) return origin >= min && origin <= max;
        float a = (min - origin) / delta;
        float b = (max - origin) / delta;
        start = MathF.Max(start, MathF.Min(a, b));
        end = MathF.Min(end, MathF.Max(a, b));
        return start <= end;
    }
}
