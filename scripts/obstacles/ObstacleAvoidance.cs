using System;
using System.Collections.Generic;
using Godot;

namespace BoidsLab;

/// <summary>
/// 解析形状上的前瞻转向和接触兜底。矩形膨胀为 AABB，角部的留距较保守。
/// 无限模式检测全部障碍物；环绕模式在边界处分段，只检测与区域相交的障碍物。
/// </summary>
public static class ObstacleAvoidance
{
    private const float Skin = 0.01f;
    private const float Epsilon = 0.000001f;
    private const int MaxMotionSegments = 64;
    private const int MaxRecoveryPasses = 8;

    public static Vector2 Calculate(BoidSnapshot self, IReadOnlyList<Obstacle2D> obstacles,
        BoidConfig config, Rect2? bounds = null)
    {
        if (!config.AvoidanceEnabled || config.MaxSpeed <= 0 || config.MaxForce <= 0)
            return Vector2.Zero;

        float speed = self.Velocity.Length();
        float lookDistance = speed * config.AvoidanceLookAhead + speed * speed / (2 * config.MaxForce);
        Vector2 forward = speed > Epsilon ? self.Velocity / speed : Vector2.Zero;
        Vector2 probe = forward * lookDistance;
        BoundaryFraction(self.Position, probe, bounds, out float edge, out _, out _);
        probe *= edge;
        Vector2 normal = Vector2.Zero;
        float earliest = float.PositiveInfinity;
        float nearestSurface = float.PositiveInfinity;
        float nearDistance = Mathf.Max(2, config.ObstacleClearance * 0.5f);
        foreach (Obstacle2D obstacle in obstacles)
        {
            if (!Participates(obstacle, bounds)) continue;
            Surface(self.Position, obstacle, config.ObstacleClearance, out float distance, out Vector2 outward);
            // 靠近接触面且没有离开时继续引导，静止接触也能恢复运动。
            if (distance < nearDistance && self.Velocity.Dot(outward) <= speed * 0.1f
                && distance < nearestSurface)
            {
                normal = outward;
                nearestSurface = distance;
                earliest = -1;
            }
            if (earliest >= 0 && Sweep(self.Position, probe, obstacle, config.ObstacleClearance,
                    out float fraction, out Vector2 hitNormal) && fraction < earliest)
            {
                earliest = fraction;
                normal = hitNormal;
            }
        }
        if (normal == Vector2.Zero) return Vector2.Zero;

        Vector2 tangent = new(-normal.Y, normal.X);
        float alignment = tangent.Dot(self.Velocity);
        if (alignment < -Epsilon || (Mathf.Abs(alignment) <= Epsilon && (self.Id & 1) != 0))
            tangent = -tangent;
        // 正面迎上时明确选择一侧，避免只刹停在障碍物前。深陷时优先向外退出。
        Vector2 desiredDirection = nearestSurface < -Skin ? normal : (tangent + normal * 0.35f).Normalized();
        Vector2 desiredVelocity = desiredDirection * config.MaxSpeed;
        return ((desiredVelocity - self.Velocity) / 0.25f).LimitLength(config.MaxForce);
    }

    /// <summary>
    /// 从 from 到 position 扫描运动轨迹；环绕模式在出口和对侧入口分别检测。
    /// 接触只消除向内分量；加速度与朝向仍交给个体更新。最多处理 64 段，
    /// 超限时舍弃未完成位移；重叠恢复最多 8 轮，无法解决填满世界的障碍布局。
    /// </summary>
    public static void ResolveMotion(Vector2 from, ref Vector2 position, ref Vector2 velocity,
        IReadOnlyList<Obstacle2D> obstacles, float clearance, Rect2? bounds = null)
    {
        Vector2 remaining = position - from;
        position = WorldGeometry2D.Wrap(from, bounds);
        RecoverOverlaps(ref position, ref velocity, obstacles, clearance, bounds);

        for (int iteration = 0; iteration < MaxMotionSegments && remaining.LengthSquared() > Epsilon * Epsilon; iteration++)
        {
            BoundaryFraction(position, remaining, bounds, out float edge, out bool crossX, out bool crossY);
            Vector2 segment = remaining * edge;
            float first = float.PositiveInfinity;
            Vector2 normal = Vector2.Zero;
            foreach (Obstacle2D obstacle in obstacles)
            {
                if (!Participates(obstacle, bounds)) continue;
                if (Sweep(position, segment, obstacle, clearance, out float fraction, out Vector2 hitNormal)
                    && fraction < first)
                {
                    first = fraction;
                    normal = hitNormal;
                }
            }

            if (normal != Vector2.Zero)
            {
                position += segment * first + normal * Skin;
                remaining *= 1 - edge * first;
                RemoveInward(ref remaining, normal);
                RemoveInward(ref velocity, normal);
                position = WorldGeometry2D.Wrap(position, bounds);
                RecoverOverlaps(ref position, ref velocity, obstacles, clearance, bounds);
                continue;
            }

            position += segment;
            bool towardRight = remaining.X > 0;
            bool towardBottom = remaining.Y > 0;
            remaining *= 1 - edge;
            if (bounds is { } rect)
            {
                if (crossX) position.X = towardRight ? rect.Position.X : rect.End.X;
                if (crossY) position.Y = towardBottom ? rect.Position.Y : rect.End.Y;
            }
            if (crossX || crossY)
                RecoverOverlaps(ref position, ref velocity, obstacles, clearance, bounds);
        }
        position = WorldGeometry2D.Wrap(position, bounds);
        RecoverOverlaps(ref position, ref velocity, obstacles, clearance, bounds);
    }

    private static bool Participates(Obstacle2D obstacle, Rect2? bounds) =>
        bounds is not { } rect || obstacle.WorldRect.Intersects(rect);

    private static void RemoveInward(ref Vector2 vector, Vector2 normal)
    {
        float inward = vector.Dot(normal);
        if (inward < 0) vector -= normal * inward;
    }

    private static void RecoverOverlaps(ref Vector2 point, ref Vector2 velocity,
        IReadOnlyList<Obstacle2D> obstacles, float clearance, Rect2? bounds)
    {
        for (int pass = 0; pass < MaxRecoveryPasses; pass++)
        {
            bool moved = false;
            foreach (Obstacle2D obstacle in obstacles)
            {
                if (!Participates(obstacle, bounds)) continue;
                Surface(point, obstacle, clearance, out float distance, out Vector2 normal);
                if (distance >= 0) continue;
                point = WorldGeometry2D.Wrap(point + normal * (-distance + Skin), bounds);
                RemoveInward(ref velocity, normal);
                moved = true;
            }
            if (!moved) return;
        }
    }

    /// <summary>带留距的形状有符号距离及向外单位法线；内部为负。</summary>
    internal static void Surface(Vector2 point, Obstacle2D obstacle, float clearance,
        out float distance, out Vector2 normal)
    {
        Vector2 offset = point - obstacle.Position;
        if (obstacle.Shape == ObstacleShape.Circle)
        {
            float length = offset.Length();
            distance = length - obstacle.Dimensions.X * 0.5f - clearance;
            normal = length > Epsilon ? offset / length : Vector2.Up;
            return;
        }
        Vector2 half = obstacle.Dimensions * 0.5f + Vector2.One * clearance;
        Vector2 outside = new(Mathf.Max(Mathf.Abs(offset.X) - half.X, 0), Mathf.Max(Mathf.Abs(offset.Y) - half.Y, 0));
        if (outside.LengthSquared() > 0)
        {
            distance = outside.Length();
            normal = new Vector2(outside.X * (offset.X < 0 ? -1 : 1), outside.Y * (offset.Y < 0 ? -1 : 1)) / distance;
            return;
        }
        float xDistance = half.X - Mathf.Abs(offset.X);
        float yDistance = half.Y - Mathf.Abs(offset.Y);
        distance = -Mathf.Min(xDistance, yDistance);
        normal = xDistance < yDistance
            ? new Vector2(offset.X < 0 ? -1 : 1, 0)
            : new Vector2(0, offset.Y < 0 ? -1 : 1);
    }

    private static bool Sweep(Vector2 origin, Vector2 movement, Obstacle2D obstacle, float clearance,
        out float fraction, out Vector2 normal)
    {
        fraction = 0;
        Surface(origin, obstacle, clearance, out float distance, out normal);
        if (distance <= 0)
            return movement.Dot(normal) < -Epsilon;
        if (movement.LengthSquared() <= Epsilon * Epsilon) return false;

        Vector2 offset = origin - obstacle.Position;
        if (obstacle.Shape == ObstacleShape.Circle)
        {
            float radius = obstacle.Dimensions.X * 0.5f + clearance;
            float a = movement.LengthSquared();
            float b = offset.Dot(movement);
            float c = offset.LengthSquared() - radius * radius;
            if (b >= 0) return false;
            float discriminant = b * b - a * c;
            if (discriminant < 0) return false;
            fraction = (-b - Mathf.Sqrt(discriminant)) / a;
            if (fraction < 0 || fraction > 1) return false;
            normal = (offset + movement * fraction).Normalized();
            return movement.Dot(normal) < -Epsilon;
        }

        Vector2 half = obstacle.Dimensions * 0.5f + Vector2.One * clearance;
        float entry = 0;
        float exit = 1;
        normal = Vector2.Zero;
        for (int axis = 0; axis < 2; axis++)
        {
            float start = axis == 0 ? offset.X : offset.Y;
            float delta = axis == 0 ? movement.X : movement.Y;
            float extent = axis == 0 ? half.X : half.Y;
            if (Mathf.Abs(delta) <= Epsilon)
            {
                if (Mathf.Abs(start) > extent) return false;
                continue;
            }
            float near = (-extent - start) / delta;
            float far = (extent - start) / delta;
            float sign = -1;
            if (near > far) { (near, far) = (far, near); sign = 1; }
            if (near > entry)
            {
                entry = near;
                normal = axis == 0 ? new Vector2(sign, 0) : new Vector2(0, sign);
            }
            exit = Mathf.Min(exit, far);
            if (entry > exit) return false;
        }
        fraction = entry;
        return normal != Vector2.Zero && fraction <= 1 && movement.Dot(normal) < -Epsilon;
    }

    private static void BoundaryFraction(Vector2 origin, Vector2 movement, Rect2? bounds,
        out float fraction, out bool crossX, out bool crossY)
    {
        fraction = 1;
        crossX = crossY = false;
        if (bounds is not { } rect) return;
        float x = movement.X > 0 ? (rect.End.X - origin.X) / movement.X
            : movement.X < 0 ? (rect.Position.X - origin.X) / movement.X : float.PositiveInfinity;
        float y = movement.Y > 0 ? (rect.End.Y - origin.Y) / movement.Y
            : movement.Y < 0 ? (rect.Position.Y - origin.Y) / movement.Y : float.PositiveInfinity;
        fraction = Mathf.Clamp(Mathf.Min(1, Mathf.Min(x, y)), 0, 1);
        crossX = x <= 1 && x <= y;
        crossY = y <= 1 && y <= x;
    }
}
