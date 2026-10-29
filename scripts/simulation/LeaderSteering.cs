using Godot;

namespace BoidsLab;

/// <summary>领队的目标到达与全局跟随；只计算转向，不保存或修改节点状态。</summary>
public static class LeaderSteering
{
    public const float ArrivalTolerance = 2f;
    private const float PositionResponseSeconds = 1f;
    private const float VelocityResponseSeconds = 0.25f;
    private const float StopSpeed = 0.5f;

    // 给群体留出面积，跟随人数越多，领队身后的到达区域越大；不分配固定编队位置。
    public static float FollowRadius(int followerCount, BoidConfig config) =>
        followerCount <= 1 ? 0 : config.SeparationRadius * Mathf.Sqrt(followerCount) * 0.65f;

    public static Vector2 FollowPoint(Vector2 leaderPosition, Vector2 heading, float distance) =>
        leaderPosition - heading.Normalized() * distance;

    public static Vector2 Follow(BoidSnapshot self, Vector2 point, Vector2 leaderVelocity,
        BoidConfig config, float deltaSeconds, float radius = 0)
    {
        float weight = config.LeaderFollowWeight;
        if (weight <= 0) return Vector2.Zero;
        Vector2 offset = point - self.Position;
        float distance = offset.Length();
        // 进入跟随区域后只匹配领队速度，不再被吸向所有个体共用的中心点。
        point = self.Position + (distance > radius ? offset * ((distance - radius) / distance) : Vector2.Zero);
        // 加权加速度最终仍乘实际 dt；用 weight * dt 限制响应，防止到点制动被放大后反向。
        return Arrive(self, point, leaderVelocity, config, deltaSeconds * weight) * weight;
    }

    public static Vector2 Arrive(BoidSnapshot self, Vector2 target, Vector2 targetVelocity,
        BoidConfig config, float deltaSeconds)
    {
        if (config.MaxSpeed <= 0 || config.MaxForce <= 0) return Vector2.Zero;
        Vector2 offset = target - self.Position;
        bool nearby = offset.LengthSquared() <= ArrivalTolerance * ArrivalTolerance;
        // 越接近目标，相对目标所需的追赶速度越低；移动领队的速度用于前馈补偿。
        Vector2 relativeVelocity = nearby ? Vector2.Zero : offset / PositionResponseSeconds;
        Vector2 desiredVelocity = (targetVelocity + relativeVelocity).LimitLength(config.MaxSpeed);
        float response = nearby && targetVelocity == Vector2.Zero && self.Velocity.Length() <= StopSpeed
            ? deltaSeconds : Mathf.Max(deltaSeconds, VelocityResponseSeconds);
        return ((desiredVelocity - self.Velocity) / response).LimitLength(config.MaxForce);
    }
}
