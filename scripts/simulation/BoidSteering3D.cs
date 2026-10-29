using System;
using Godot;

namespace BoidsLab;

/// <summary>只读共同的步前快照计算三维分离、对齐、聚集；不移动节点，不重新搜索。</summary>
public static class BoidSteering3D
{
    private const float ResponseSeconds = 1f;
    private const float MinimumSeparationDistance = 0.001f;
    private const float DirectionEpsilonSquared = 1e-12f;

    public static Vector3 Calculate(NeighborSearch3D search, int index, BoidConfig config)
    {
        if (!search.HasResults) throw new InvalidOperationException("Search neighbors before calculating steering.");
        bool separate = config.SeparationEnabled && config.SeparationWeight > 0;
        bool align = config.AlignmentEnabled && config.AlignmentWeight > 0;
        bool cohere = config.CohesionEnabled && config.CohesionWeight > 0;
        var neighbors = search.GetNeighborIndices(index);
        if (config.MaxSpeed <= 0 || config.MaxForce <= 0 || neighbors.Count == 0 || (!separate && !align && !cohere))
            return Vector3.Zero;

        BoidSnapshot3D self = search.Snapshots[index];
        Vector3 separation = Vector3.Zero;
        Vector3 alignment = Vector3.Zero;
        Vector3 cohesion = Vector3.Zero;
        float separationRadius = MathF.Min(config.SeparationRadius, search.Radius);
        foreach (int neighborIndex in neighbors)
        {
            BoidSnapshot3D neighbor = search.Snapshots[neighborIndex];
            Vector3 offset = search.GetOffset(index, neighborIndex);
            if (separate && WorldGeometry3D.WithinRadius(offset, separationRadius))
            {
                float distance = offset.Length();
                if (distance == 0)
                {
                    // 完全重合时，用唯一 ID 给这一对相反方向，避免除零和随机抖动。
                    separation += (self.Id < neighbor.Id ? Vector3.Left : Vector3.Right) / MinimumSeparationDistance;
                }
                else
                {
                    // 单位远离方向 / 距离：越近的邻居对合成方向影响越大。
                    separation -= offset / distance / MathF.Max(distance, MinimumSeparationDistance);
                }
            }
            if (align) alignment += neighbor.Velocity;
            // 使用最短环绕相对位移，避免跨盒边界的邻居中心落到盒子中央。
            if (cohere) cohesion += offset;
        }

        Vector3 acceleration = Vector3.Zero;
        if (separate) acceleration += SteerTowards(separation, self.Velocity, config) * config.SeparationWeight;
        if (align) acceleration += SteerTowards(alignment / neighbors.Count, self.Velocity, config) * config.AlignmentWeight;
        if (cohere) acceleration += SteerTowards(cohesion / neighbors.Count, self.Velocity, config) * config.CohesionWeight;
        return acceleration.LimitLength(config.MaxForce);
    }

    private static Vector3 SteerTowards(Vector3 direction, Vector3 velocity, BoidConfig config)
    {
        // 对称邻居互相抵消时没有目标方向，不凭空产生制动。
        if (direction.LengthSquared() <= DirectionEpsilonSquared) return Vector3.Zero;
        Vector3 desiredVelocity = direction.Normalized() * config.MaxSpeed;
        return ((desiredVelocity - velocity) / ResponseSeconds).LimitLength(config.MaxForce);
    }
}
