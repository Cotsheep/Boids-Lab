using Godot;

namespace BoidsLab;

/// <summary>前方圆形扫掠 → 按偏角采样 → 受限转向。只消费统一查询，不识别障碍形状。</summary>
public static class ObstacleAvoidance
{
    public static Vector2 Calculate(BoidSnapshot self, ObstacleQuery2D query, BoidConfig config,
        Vector2 heading = default, ObstacleDebug2D? debug = null)
    {
        if (!config.AvoidanceEnabled || config.MaxForce <= 0 || config.MaxSpeed <= 0 || query.BodyCount == 0) return Vector2.Zero;
        float speed = self.Velocity.Length();
        Vector2 forward = speed > ObstacleTuning.Epsilon ? self.Velocity / speed
            : heading.LengthSquared() > ObstacleTuning.Epsilon ? heading.Normalized() : Vector2.Right;
        float near = Mathf.Max(ObstacleTuning.NearMinimum, config.ObstacleClearance * ObstacleTuning.NearClearanceRatio);
        float length = speed * config.AvoidanceLookAhead + speed * speed / (2 * config.MaxForce);
        Vector2 normal = Vector2.Zero;
        if (query.GatherContacts(self.Position, config.ObstacleClearance + near))
        {
            QueryContact2D contact = query.Contacts[0];
            if (self.Velocity.Dot(contact.Normal) <= speed * ObstacleTuning.DepartingDot)
            {
                normal = contact.Normal;
                length = Mathf.Max(near, length);
            }
        }
        Vector2 probe = Truncate(self.Position, forward * length, query);
        ObstacleHit2D danger = query.Sweep(self.Position, probe, config.ObstacleClearance);
        if (debug is not null) { debug.Probe = probe; debug.Candidates.Add((probe, danger.Blocked)); }
        if (!danger.Blocked && normal == Vector2.Zero) return Vector2.Zero;
        // 实际重叠由接触层恢复；主动层只提供稳定离开方向。
        if (query.GatherContacts(self.Position, config.ObstacleClearance))
            return Steer(query.Contacts[0].Normal, config.MaxSpeed, self.Velocity, config, debug);
        if (normal == Vector2.Zero) normal = danger.Normal;
        Vector2 best = -forward;
        float bestDistance = -1;
        foreach (Vector2 local in ObstacleTuning.Samples2D)
        {
            Vector2 sample = new(local.X, (self.Id & 1) == 0 ? local.Y : -local.Y);
            Vector2 direction = new(forward.X * sample.X - forward.Y * sample.Y, forward.Y * sample.X + forward.X * sample.Y);
            // 靠近表面时只尝试离开/切向方向，避免 CastMotion 忽略起点重叠。
            if (normal != Vector2.Zero && direction.Dot(normal) < -ObstacleTuning.TangentDotTolerance && probe.LengthSquared() < near * near) continue;
            Vector2 motion = Truncate(self.Position, direction * length, query);
            ObstacleHit2D hit = query.Sweep(self.Position, motion, config.ObstacleClearance);
            debug?.Candidates.Add((motion, hit.Blocked));
            if (!hit.Blocked && (normal == Vector2.Zero || speed > ObstacleTuning.StationarySpeed || direction.Dot(normal) > ObstacleTuning.DepartingDot))
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

    private static Vector2 Truncate(Vector2 origin, Vector2 motion, ObstacleQuery2D query)
    {
        ObstacleBoundary2D.BoundaryFraction(origin, motion, query.Bounds, out float edge, out _, out _);
        return motion * edge;
    }

    private static Vector2 Steer(Vector2 direction, float speed, Vector2 velocity, BoidConfig config, ObstacleDebug2D? debug)
    {
        if (debug is not null) debug.Choice = direction * speed * ObstacleTuning.ResponseSeconds;
        return ((direction * speed - velocity) / ObstacleTuning.ResponseSeconds).LimitLength(config.MaxForce);
    }
}
