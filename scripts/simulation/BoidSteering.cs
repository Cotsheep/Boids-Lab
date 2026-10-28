using System;
using Godot;

namespace BoidsLab;

/// <summary>只读取步前快照计算转向；不移动节点、不搜索邻居，也不保存跨步状态。</summary>
public static class BoidSteering
{
    // 将目标速度差转换为加速度，单位为秒；避免把加速度误当作每帧速度增量。
    private const float ResponseSeconds = 1f;
    private const float MinimumSeparationDistance = 0.001f;
    private const float DirectionEpsilonSquared = 1e-12f;

    public static Vector2 Calculate(NeighborSearch2D search, int index, BoidConfig config)
    {
        if (!search.HasResults) throw new InvalidOperationException("Search neighbors before calculating steering.");
        bool separate = config.SeparationEnabled && config.SeparationWeight > 0;
        bool align = config.AlignmentEnabled && config.AlignmentWeight > 0;
        bool cohere = config.CohesionEnabled && config.CohesionWeight > 0;
        if ((!separate && !align && !cohere) || config.MaxSpeed <= 0 || config.MaxForce <= 0)
            return Vector2.Zero;

        var neighbors = search.GetNeighborIndices(index);
        if (neighbors.Count == 0) return Vector2.Zero;
        BoidSnapshot self = search.Snapshots[index];
        Vector2 separation = Vector2.Zero;
        Vector2 alignment = Vector2.Zero;
        Vector2 cohesion = Vector2.Zero;
        float separationRadius = MathF.Min(config.SeparationRadius, search.Radius);
        float separationRadiusSquared = separationRadius * separationRadius;

        foreach (int neighborIndex in neighbors)
        {
            BoidSnapshot neighbor = search.Snapshots[neighborIndex];
            Vector2 offset = search.GetOffset(index, neighborIndex);
            float distanceSquared = offset.LengthSquared();
            if (separate && distanceSquared <= separationRadiusSquared)
            {
                if (distanceSquared == 0)
                {
                    // 完全重合时没有几何方向。用唯一 ID 让这一对确定地朝相反方向分开。
                    separation += (self.Id < neighbor.Id ? Vector2.Left : Vector2.Right)
                        / MinimumSeparationDistance;
                }
                else
                {
                    float distance = MathF.Sqrt(distanceSquared);
                    // -offset / distance²：越近的邻居对分离方向的影响越大。
                    separation -= offset / distance / MathF.Max(distance, MinimumSeparationDistance);
                }
            }
            if (align) alignment += neighbor.Velocity;
            // 平均环绕位移，而不是直接平均世界坐标，正确处理屏幕边缘的邻居。
            if (cohere) cohesion += offset;
        }

        Vector2 acceleration = Vector2.Zero;
        if (separate) acceleration += SteerTowards(separation, self.Velocity, config) * config.SeparationWeight;
        if (align) acceleration += SteerTowards(alignment / neighbors.Count, self.Velocity, config) * config.AlignmentWeight;
        if (cohere) acceleration += SteerTowards(cohesion / neighbors.Count, self.Velocity, config) * config.CohesionWeight;
        return acceleration.LimitLength(config.MaxForce);
    }

    private static Vector2 SteerTowards(Vector2 direction, Vector2 velocity, BoidConfig config)
    {
        // 没有目标方向（例如对称邻居抵消）时不凭空制动。
        if (direction.LengthSquared() <= DirectionEpsilonSquared) return Vector2.Zero;
        Vector2 desiredVelocity = direction.Normalized() * config.MaxSpeed;
        return ((desiredVelocity - velocity) / ResponseSeconds).LimitLength(config.MaxForce);
    }
}
