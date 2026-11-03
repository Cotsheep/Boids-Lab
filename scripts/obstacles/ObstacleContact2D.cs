using Godot;

namespace BoidsLab;

public readonly record struct ObstacleMotionResult(bool Recovered, bool MotionBudgetExhausted, bool ContactsSaturated);

/// <summary>完整位移约束；与主动转向和最大加速度无关。</summary>
public static class ObstacleContact2D
{
    public static ObstacleMotionResult Resolve(Vector2 from, ref Vector2 position, ref Vector2 velocity,
        ObstacleQuery2D query, float clearance, ObstacleDebug2D? debug = null)
    {
        Vector2 remaining = position - from;
        position = WorldGeometry2D.Wrap(from, query.Bounds);
        if (!Recover(ref position, ref velocity, ref remaining, query, clearance, debug))
            return new(false, false, query.ContactsSaturated);
        int iteration = 0;
        for (; iteration < ObstacleTuning.MotionSegments && remaining.LengthSquared() > ObstacleTuning.Epsilon * ObstacleTuning.Epsilon; iteration++)
        {
            ObstacleBoundary2D.BoundaryFraction(position, remaining, query.Bounds, out float edge, out bool crossX, out bool crossY);
            Vector2 segment = remaining * edge;
            ObstacleHit2D hit = query.Sweep(position, segment, clearance);
            if (hit.Blocked)
            {
                position += segment * hit.Fraction;
                if (hit.Normal == Vector2.Zero) { velocity = Vector2.Zero; return new(true, true, query.ContactsSaturated); }
                position += hit.Normal * ObstacleTuning.Skin;
                remaining *= 1 - edge * hit.Fraction;
                RemoveInward(ref remaining, hit.Normal);
                RemoveInward(ref velocity, hit.Normal);
                debug?.Normals.Add(hit.Normal);
                position = WorldGeometry2D.Wrap(position, query.Bounds);
                if (!Recover(ref position, ref velocity, ref remaining, query, clearance, debug))
                    return new(false, false, query.ContactsSaturated);
                continue;
            }
            position += segment;
            bool right = remaining.X > 0, down = remaining.Y > 0;
            remaining *= 1 - edge;
            if (query.Bounds is { } bounds)
            {
                if (crossX) position.X = right ? bounds.Position.X : bounds.End.X;
                if (crossY) position.Y = down ? bounds.Position.Y : bounds.End.Y;
            }
            if ((crossX || crossY) && !Recover(ref position, ref velocity, ref remaining, query, clearance, debug))
                return new(false, false, query.ContactsSaturated);
        }
        position = WorldGeometry2D.Wrap(position, query.Bounds);
        bool recovered = Recover(ref position, ref velocity, ref remaining, query, clearance, debug);
        return new(recovered, iteration >= ObstacleTuning.MotionSegments && remaining.LengthSquared() > ObstacleTuning.Epsilon * ObstacleTuning.Epsilon, query.ContactsSaturated);
    }

    private static bool Recover(ref Vector2 point, ref Vector2 velocity, ref Vector2 remaining,
        ObstacleQuery2D query, float clearance, ObstacleDebug2D? debug)
    {
        Vector2 original = point;
        for (int pass = 0; pass < ObstacleTuning.RecoveryPasses; pass++)
        {
            if (!query.GatherContacts(point, clearance)) return !query.Occupied(point, clearance);
            Vector2 correction = Vector2.Zero;
            foreach (QueryContact2D contact in query.Contacts)
            {
                float missing = contact.Depth + ObstacleTuning.Skin - correction.Dot(contact.Normal);
                if (missing > 0) correction += contact.Normal * missing;
                RemoveInward(ref velocity, contact.Normal);
                RemoveInward(ref remaining, contact.Normal);
                debug?.Normals.Add(contact.Normal);
            }
            point = WorldGeometry2D.Wrap(point + correction, query.Bounds);
        }
        if (!query.Occupied(point, clearance)) return true;
        // 无法找到自由空间时不伪造成功；保留原位置，避免下一步沿内部接缝往复跳动。
        point = original;
        velocity = remaining = Vector2.Zero;
        return false;
    }

    private static void RemoveInward(ref Vector2 vector, Vector2 normal)
    {
        float inward = vector.Dot(normal);
        if (inward < 0) vector -= normal * inward;
    }
}
