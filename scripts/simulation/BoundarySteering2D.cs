using Godot;

namespace BoidsLab;

/// <summary>给定边界的外侧 20% 内平滑向内转向；只施加加速度，不裁剪或传送位置。</summary>
public static class BoundarySteering2D
{
    public static Vector2 Apply(BoidSnapshot self, Rect2 bounds, BoidConfig config, Vector2 flocking)
    {
        if (config.MaxForce <= 0 || config.MaxSpeed <= 0) return Vector2.Zero;
        for (int axis = 0; axis < 2; axis++)
        {
            float band = bounds.Size[axis] * 0.2f;
            float lower = (bounds.Position[axis] + band - self.Position[axis]) / band;
            float upper = (self.Position[axis] - (bounds.End[axis] - band)) / band;
            float amount = Mathf.Clamp(Mathf.Max(lower, upper), 0, 1);
            if (amount <= 0) continue;
            float inward = lower > upper ? 1f : -1f;
            float blend = amount * amount * (3 - 2 * amount);
            float requested = (inward * config.MaxSpeed - self.Velocity[axis]) / 0.5f;
            flocking[axis] = Mathf.Lerp(flocking[axis], requested, blend);
            // 群集朝外的分量不能抵消边界的向内请求，合力仍由最大加速度限幅。
            if (requested * inward > 0)
                flocking[axis] = inward * Mathf.Max(flocking[axis] * inward, requested * inward * blend);
        }
        return flocking.LimitLength(config.MaxForce);
    }
}
