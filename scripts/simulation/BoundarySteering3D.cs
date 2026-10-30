using Godot;

namespace BoidsLab;

/// <summary>只施加回归加速度，不修正位置。缓冲带为各轴外侧 20%。</summary>
public static class BoundarySteering3D
{
    public static Vector3 Apply(BoidSnapshot3D self, Aabb bounds, BoidConfig config, Vector3 flocking)
    {
        if (config.MaxForce <= 0 || config.MaxSpeed <= 0) return Vector3.Zero;
        for (int axis = 0; axis < 3; axis++)
        {
            float band = bounds.Size[axis] * 0.2f;
            float lower = (bounds.Position[axis] + band - self.Position[axis]) / band;
            float upper = (self.Position[axis] - (bounds.End[axis] - band)) / band;
            float amount = Mathf.Clamp(Mathf.Max(lower, upper), 0, 1);
            if (amount <= 0) continue;
            float inward = lower > upper ? 1f : -1f;
            float blend = amount * amount * (3 - 2 * amount);
            float requested = (inward * config.MaxSpeed - self.Velocity[axis]) / 0.5f;
            // 外沿及越界时覆盖同轴群集输入；向外群集力不能抵消向内回归请求。
            flocking[axis] = Mathf.Lerp(flocking[axis], requested, blend);
            if (requested * inward > 0)
                flocking[axis] = inward * Mathf.Max(flocking[axis] * inward, requested * inward * blend);
        }
        return flocking.LimitLength(config.MaxForce);
    }
}
