using System;
using System.Collections.Generic;
using Godot;

namespace BoidsLab;

/// <summary>解析球体/轴对齐盒体的前瞻转向与接触约束。沿实际坐标的完整运动线段检测。</summary>
public static class ObstacleAvoidance3D
{
    private const float Skin = 0.01f;
    private const float Epsilon = 0.000001f;
    private const int MaxSegments = 64;
    private const int MaxRecoveryPasses = 8;

    public static Vector3 Calculate(BoidSnapshot3D self, IReadOnlyList<Obstacle3D> obstacles,
        BoidConfig config)
    {
        if (!config.AvoidanceEnabled || config.MaxSpeed <= 0 || config.MaxForce <= 0 || obstacles.Count == 0)
            return Vector3.Zero;
        Vector3 position = self.Position;
        float speed = self.Velocity.Length();
        float nearDistance = Mathf.Max(2, config.ObstacleClearance * 0.5f);
        float nearest = float.PositiveInfinity;
        Vector3 normal = Vector3.Zero;
        foreach (Obstacle3D obstacle in obstacles)
        {
            Surface(position, obstacle, config.ObstacleClearance, out float distance, out Vector3 outward);
            if (distance < nearDistance && distance < nearest && self.Velocity.Dot(outward) <= speed * 0.1f)
            {
                nearest = distance;
                normal = outward;
            }
        }
        if (normal == Vector3.Zero && speed > Epsilon)
        {
            float lookDistance = speed * config.AvoidanceLookAhead + speed * speed / (2 * config.MaxForce);
            FirstHit(position, self.Velocity / speed * lookDistance, obstacles,
                config.ObstacleClearance, out _, out normal);
        }
        if (normal == Vector3.Zero) return Vector3.Zero;

        Vector3 tangent = self.Velocity - normal * self.Velocity.Dot(normal);
        if (tangent.LengthSquared() <= Epsilon * Epsilon)
        {
            // 正面撞来时，从接触面的切平面选一个稳定方向；不同 ID 可走向两侧。
            Vector3 axis = Mathf.Abs(normal.Y) < 0.9f ? Vector3.Up : Vector3.Right;
            tangent = normal.Cross(axis);
            if ((self.Id & 1) != 0) tangent = -tangent;
        }
        tangent = tangent.Normalized();
        Vector3 direction = nearest < -Skin ? normal : (tangent + normal * 0.35f).Normalized();
        return ((direction * config.MaxSpeed - self.Velocity) / 0.25f).LimitLength(config.MaxForce);
    }

    /// <summary>扫描完整的本步位移，接触后沿面滑动；新增/放大障碍物的重叠也在此恢复。</summary>
    public static void ResolveMotion(Vector3 from, ref Vector3 position, ref Vector3 velocity,
        IReadOnlyList<Obstacle3D> obstacles, float clearance)
    {
        Vector3 remaining = position - from;
        position = from;
        RecoverOverlaps(ref position, ref velocity, obstacles, clearance);
        for (int i = 0; i < MaxSegments && remaining.LengthSquared() > Epsilon * Epsilon; i++)
        {
            if (FirstHit(position, remaining, obstacles, clearance, out float fraction, out Vector3 normal))
            {
                position += remaining * fraction + normal * Skin;
                remaining *= 1 - fraction;
                RemoveInward(ref remaining, normal);
                RemoveInward(ref velocity, normal);
                RecoverOverlaps(ref position, ref velocity, obstacles, clearance);
                continue;
            }
            position += remaining;
            break;
        }
        // 极端步长有界处理；剩余位移不会绕过接触检测。
        RecoverOverlaps(ref position, ref velocity, obstacles, clearance);
    }

    private static bool FirstHit(Vector3 origin, Vector3 movement, IReadOnlyList<Obstacle3D> obstacles,
        float clearance, out float fraction, out Vector3 normal)
    {
        fraction = float.PositiveInfinity;
        normal = Vector3.Zero;
        foreach (Obstacle3D obstacle in obstacles)
            if (Sweep(origin, movement, obstacle, clearance, out float hit, out Vector3 outward) && hit < fraction)
            {
                fraction = hit;
                normal = outward;
            }
        return normal != Vector3.Zero;
    }

    private static void RemoveInward(ref Vector3 vector, Vector3 normal)
    {
        float inward = vector.Dot(normal);
        if (inward < 0) vector -= normal * inward;
    }

    private static void RecoverOverlaps(ref Vector3 point, ref Vector3 velocity,
        IReadOnlyList<Obstacle3D> obstacles, float clearance)
    {
        // 有界恢复；障碍物互相封闭或填满世界时，不承诺一定存在自由位置。
        for (int pass = 0; pass < MaxRecoveryPasses; pass++)
        {
            bool moved = false;
            foreach (Obstacle3D obstacle in obstacles)
            {
                Surface(point, obstacle, clearance, out float distance, out Vector3 normal);
                if (distance >= 0) continue;
                point += normal * (-distance + Skin);
                RemoveInward(ref velocity, normal);
                moved = true;
            }
            if (!moved) return;
        }
    }

    /// <summary>带间距形状的有符号距离与向外法线；盒体按各轴膨胀，角部留距较保守。</summary>
    public static void Surface(Vector3 point, Obstacle3D obstacle, float clearance, out float distance, out Vector3 normal)
    {
        Vector3 offset = point - obstacle.Position;
        if (obstacle.Shape == ObstacleShape.Sphere)
        {
            float length = offset.Length();
            distance = length - obstacle.Dimensions.X * 0.5f - clearance;
            normal = length > Epsilon ? offset / length : Vector3.Up;
            return;
        }
        Vector3 half = obstacle.Dimensions * 0.5f + Vector3.One * clearance;
        Vector3 outside = (offset.Abs() - half).Max(Vector3.Zero);
        if (outside.LengthSquared() > 0)
        {
            distance = outside.Length();
            normal = new Vector3(outside.X * (offset.X < 0 ? -1 : 1),
                outside.Y * (offset.Y < 0 ? -1 : 1), outside.Z * (offset.Z < 0 ? -1 : 1)) / distance;
            return;
        }
        Vector3 inside = half - offset.Abs();
        int nearestAxis = inside.X <= inside.Y && inside.X <= inside.Z ? 0 : inside.Y <= inside.Z ? 1 : 2;
        distance = -inside[nearestAxis];
        normal = Vector3.Zero;
        normal[nearestAxis] = offset[nearestAxis] < 0 ? -1 : 1;
    }

    public static float? HitDistance(Vector3 origin, Vector3 direction, Obstacle3D obstacle, float maxDistance)
    {
        Surface(origin, obstacle, 0, out float distance, out _);
        if (distance <= 0) return 0;
        return Sweep(origin, direction * maxDistance, obstacle, 0, out float hit, out _) ? hit * maxDistance : null;
    }

    internal static bool Sweep(Vector3 origin, Vector3 movement, Obstacle3D obstacle, float clearance,
        out float fraction, out Vector3 normal)
    {
        fraction = 0;
        Surface(origin, obstacle, clearance, out float distance, out normal);
        if (distance <= 0) return movement.Dot(normal) < -Epsilon;
        if (movement.LengthSquared() <= Epsilon * Epsilon) return false;
        Vector3 offset = origin - obstacle.Position;
        if (obstacle.Shape == ObstacleShape.Sphere)
        {
            // 双精度判别式减小长距离射线减法的精度损失。
            double radius = obstacle.Dimensions.X * 0.5 + clearance;
            double a = Dot(movement, movement);
            double b = Dot(offset, movement);
            double c = Dot(offset, offset) - radius * radius;
            if (b >= 0) return false;
            double discriminant = b * b - a * c;
            if (discriminant < 0) return false;
            fraction = (float)((-b - Math.Sqrt(discriminant)) / a);
            if (fraction < 0 || fraction > 1) return false;
            normal = (offset + movement * fraction).Normalized();
            return movement.Dot(normal) < -Epsilon;
        }
        Vector3 half = obstacle.Dimensions * 0.5f + Vector3.One * clearance;
        float entry = 0, exit = 1;
        normal = Vector3.Zero;
        for (int axis = 0; axis < 3; axis++)
        {
            float start = offset[axis], delta = movement[axis], extent = half[axis];
            if (Mathf.Abs(delta) <= Epsilon)
            {
                if (Mathf.Abs(start) > extent) return false;
                continue;
            }
            float near = (-extent - start) / delta, far = (extent - start) / delta, sign = -1;
            if (near > far) { (near, far) = (far, near); sign = 1; }
            if (near > entry)
            {
                entry = near;
                normal = Vector3.Zero;
                normal[axis] = sign;
            }
            exit = Mathf.Min(exit, far);
            if (entry > exit) return false;
        }
        fraction = entry;
        return normal != Vector3.Zero && fraction <= 1 && movement.Dot(normal) < -Epsilon;
    }

    private static double Dot(Vector3 a, Vector3 b) => (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z;

}
