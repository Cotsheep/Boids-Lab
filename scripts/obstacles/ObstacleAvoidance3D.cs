using Godot;

namespace BoidsLab;

/// <summary>前方球形扫掠 → 按偏角采样 → 受限转向。只消费统一查询，不识别障碍形状。</summary>
public static class ObstacleAvoidance3D
{
    public static Vector3 Calculate(BoidSnapshot3D self, ObstacleQuery3D query, BoidConfig config,
        Vector3 heading = default, ObstacleDebug3D? debug = null)
    {
        if (!config.AvoidanceEnabled || config.MaxForce <= 0 || config.MaxSpeed <= 0 || query.BodyCount == 0) return Vector3.Zero;
        float speed = self.Velocity.Length();
        Vector3 forward = speed > ObstacleTuning.Epsilon ? self.Velocity / speed
            : heading.LengthSquared() > ObstacleTuning.Epsilon ? heading.Normalized() : Vector3.Forward;
        float near = Mathf.Max(ObstacleTuning.NearMinimum, config.ObstacleClearance * ObstacleTuning.NearClearanceRatio);
        float length = speed * config.AvoidanceLookAhead + speed * speed / (2 * config.MaxForce);
        Vector3 normal = Vector3.Zero;
        if (query.GatherContacts(self.Position, config.ObstacleClearance + near))
        {
            QueryContact3D contact = query.Contacts[0];
            if (self.Velocity.Dot(contact.Normal) <= speed * ObstacleTuning.DepartingDot)
            {
                normal = contact.Normal;
                length = Mathf.Max(near, length);
            }
        }
        Vector3 probe = forward * length;
        ObstacleHit3D danger = query.Sweep(self.Position, probe, config.ObstacleClearance);
        if (debug is not null) { debug.Probe = probe; debug.Candidates.Add((probe, danger.Blocked)); }
        if (!danger.Blocked && normal == Vector3.Zero) return Vector3.Zero;
        // 实际重叠由接触层恢复；主动层只提供稳定离开方向。
        if (query.GatherContacts(self.Position, config.ObstacleClearance))
            return Steer(query.Contacts[0].Normal, config.MaxSpeed, self.Velocity, config, debug);
        if (normal == Vector3.Zero) normal = danger.Normal;
        Vector3 best = -forward;
        float bestDistance = -1;
        Vector3 right = forward.Cross(Mathf.Abs(forward.Y) < 0.95f ? Vector3.Up : Vector3.Right).Normalized();
        Vector3 up = right.Cross(forward).Normalized();
        foreach (Vector3 local in ObstacleTuning.Samples3D)
        {
            float side = (self.Id & 1) == 0 ? 1 : -1;
            Vector3 direction = right * local.X * side + up * local.Y * side - forward * local.Z;
            // 靠近表面时只尝试离开/切向方向，避免 CastMotion 忽略起点重叠。
            if (normal != Vector3.Zero && direction.Dot(normal) < -ObstacleTuning.TangentDotTolerance && probe.LengthSquared() < near * near) continue;
            Vector3 motion = direction * length;
            ObstacleHit3D hit = query.Sweep(self.Position, motion, config.ObstacleClearance);
            debug?.Candidates.Add((motion, hit.Blocked));
            if (!hit.Blocked && (normal == Vector3.Zero || speed > ObstacleTuning.StationarySpeed || direction.Dot(normal) > ObstacleTuning.DepartingDot))
                return Steer(direction, config.MaxSpeed, self.Velocity, config, debug);
            float distance = hit.Fraction * motion.Length();
            if (distance > bestDistance) { bestDistance = distance; best = direction; }
        }
        // 全部阻挡不是“原前方安全”：按可用刹车距离限速，空间不足时制动。
        float free = Mathf.Max(0, bestDistance - near);
        float safeSpeed = Mathf.Min(config.MaxSpeed, Mathf.Min(Mathf.Sqrt(2 * config.MaxForce * free),
            free / (config.AvoidanceLookAhead + ObstacleTuning.ResponseSeconds)));
        return Steer(best, safeSpeed, self.Velocity, config, debug);
    }

    private static Vector3 Steer(Vector3 direction, float speed, Vector3 velocity, BoidConfig config, ObstacleDebug3D? debug)
    {
        if (debug is not null) debug.Choice = direction * speed * ObstacleTuning.ResponseSeconds;
        return ((direction * speed - velocity) / ObstacleTuning.ResponseSeconds).LimitLength(config.MaxForce);
    }
}
