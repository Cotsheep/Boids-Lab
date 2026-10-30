using System;
using Godot;

namespace BoidsLab;

/// <summary>只读取步前快照计算转向；不移动节点、不搜索邻居，也不保存跨步状态。</summary>
public static class BoidSteering3D
{
    // 将目标速度差转换为加速度，单位为秒；避免把加速度误当作每帧速度增量。
    private const float ResponseSeconds = 1f;
    private const float MinimumSeparationDistance = 0.001f;
    private const float DirectionEpsilonSquared = 1e-12f;

    public static Vector3 Calculate(NeighborSearch3D search, int index, BoidConfig config,
        Vector3 guidance = default, bool protectSeparation = false, Vector3 referenceVelocity = default)
    {
        if (!search.HasResults) throw new InvalidOperationException("Search neighbors before calculating steering.");
        bool separate = config.SeparationEnabled && config.SeparationWeight > 0;
        bool align = config.AlignmentEnabled && config.AlignmentWeight > 0;
        bool cohere = config.CohesionEnabled && config.CohesionWeight > 0;
        if (config.MaxSpeed <= 0 || config.MaxForce <= 0)
            return Vector3.Zero;

        var neighbors = search.GetNeighborIndices(index);
        if (neighbors.Count == 0 || (!separate && !align && !cohere))
            return guidance.LimitLength(config.MaxForce);
        BoidSnapshot3D self = search.Snapshots[index];
        Vector3 separation = Vector3.Zero;
        Vector3 alignment = Vector3.Zero;
        Vector3 cohesion = Vector3.Zero;
        float separationRadius = MathF.Min(config.SeparationRadius, search.Radius);
        float separationRadiusSquared = separationRadius * separationRadius;
        float nearestSeparation = float.PositiveInfinity;

        foreach (int neighborIndex in neighbors)
        {
            BoidSnapshot3D neighbor = search.Snapshots[neighborIndex];
            Vector3 offset = search.GetOffset(index, neighborIndex);
            float distanceSquared = offset.LengthSquared();
            if (separate && distanceSquared <= separationRadiusSquared)
            {
                nearestSeparation = MathF.Min(nearestSeparation, MathF.Sqrt(distanceSquared));
                if (distanceSquared == 0)
                {
                    // 完全重合时没有几何方向。用唯一 ID 让这一对确定地朝相反方向分开。
                    separation += (self.Id < neighbor.Id ? Vector3.Left : Vector3.Right)
                        / MinimumSeparationDistance;
                }
                else
                {
                    float distance = MathF.Sqrt(distanceSquared);
                    // -offset / distance²：越近的邻居对分离方向的影响越大。
                    separation -= offset / distance / MathF.Max(distance, MinimumSeparationDistance);
                }
            }
            if (align) alignment += neighbor.Velocity - referenceVelocity;
            // 平均相对位移，得到邻居中心相对于自身的方向。
            if (cohere) cohesion += offset;
        }

        // 领队模式使用共同运动参考系，避免每条局部规则反复制动共同前进速度。
        Vector3 acceleration = guidance;
        if (align) acceleration += SteerTowards(alignment / neighbors.Count, self.Velocity - referenceVelocity, config) * config.AlignmentWeight;
        if (cohere) acceleration += SteerTowards(cohesion / neighbors.Count, self.Velocity - referenceVelocity, config) * config.CohesionWeight;
        if (protectSeparation && separate && separation.LengthSquared() > DirectionEpsilonSquared)
        {
            // 跟随时，越拥挤越不允许其他引导抵消分离；半个分离半径内完全保护。
            // 只削减向拥挤方向的分量，保留切向运动，避免整群因分离而停走抖动。
            Vector3 outward = separation.Normalized();
            float inward = acceleration.Dot(outward);
            float protection = Mathf.Clamp(2 * (1 - nearestSeparation / separationRadius), 0, 1);
            if (inward < 0) acceleration -= outward * inward * protection;
        }
        if (separate) acceleration += SteerTowards(separation, self.Velocity - referenceVelocity, config) * config.SeparationWeight;
        return acceleration.LimitLength(config.MaxForce);
    }

    private static Vector3 SteerTowards(Vector3 direction, Vector3 velocity, BoidConfig config)
    {
        // 没有目标方向（例如对称邻居抵消）时不凭空制动。
        if (direction.LengthSquared() <= DirectionEpsilonSquared) return Vector3.Zero;
        Vector3 desiredVelocity = direction.Normalized() * config.MaxSpeed;
        return ((desiredVelocity - velocity) / ResponseSeconds).LimitLength(config.MaxForce);
    }
}
