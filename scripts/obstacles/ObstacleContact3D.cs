using Godot;

namespace BoidsLab;

/// <summary>完整位移约束；与主动转向和最大加速度无关。</summary>
public static class ObstacleContact3D
{
    public static ObstacleMotionResult Resolve(Vector3 from, ref Vector3 position, ref Vector3 velocity,
        ObstacleQuery3D query, float clearance, ObstacleDebug3D? debug = null)
    {
        Vector3 remaining = position - from;
        position = from;
        if (!Recover(ref position, ref velocity, ref remaining, query, clearance, debug))
            return new(false, false, query.ContactsSaturated);
        int iteration = 0;
        for (; iteration < ObstacleTuning.MotionSegments && remaining.LengthSquared() > ObstacleTuning.Epsilon * ObstacleTuning.Epsilon; iteration++)
        {
            float edge = 1;
            Vector3 segment = remaining * edge;
            ObstacleHit3D hit = query.Sweep(position, segment, clearance);
            if (hit.Blocked)
            {
                position += segment * hit.Fraction;
                if (hit.Normal == Vector3.Zero) { velocity = Vector3.Zero; return new(true, true, query.ContactsSaturated); }
                position += hit.Normal * ObstacleTuning.Skin;
                remaining *= 1 - edge * hit.Fraction;
                RemoveInward(ref remaining, hit.Normal);
                RemoveInward(ref velocity, hit.Normal);
                debug?.Normals.Add(hit.Normal);

                if (!Recover(ref position, ref velocity, ref remaining, query, clearance, debug))
                    return new(false, false, query.ContactsSaturated);
                continue;
            }
            position += segment;
            remaining = Vector3.Zero;
        }

        bool recovered = Recover(ref position, ref velocity, ref remaining, query, clearance, debug);
        return new(recovered, iteration >= ObstacleTuning.MotionSegments && remaining.LengthSquared() > ObstacleTuning.Epsilon * ObstacleTuning.Epsilon, query.ContactsSaturated);
    }

    private static bool Recover(ref Vector3 point, ref Vector3 velocity, ref Vector3 remaining,
        ObstacleQuery3D query, float clearance, ObstacleDebug3D? debug)
    {
        Vector3 original = point;
        for (int pass = 0; pass < ObstacleTuning.RecoveryPasses; pass++)
        {
            if (!query.GatherContacts(point, clearance)) return !query.Occupied(point, clearance);
            Vector3 correction = Vector3.Zero;
            foreach (QueryContact3D contact in query.Contacts)
            {
                float missing = contact.Depth + ObstacleTuning.Skin - correction.Dot(contact.Normal);
                if (missing > 0) correction += contact.Normal * missing;
                RemoveInward(ref velocity, contact.Normal);
                RemoveInward(ref remaining, contact.Normal);
                debug?.Normals.Add(contact.Normal);
            }
            point = point + correction;
        }
        if (!query.Occupied(point, clearance)) return true;
        // 无法找到自由空间时不伪造成功；保留原位置，避免下一步沿内部接缝往复跳动。
        point = original;
        velocity = remaining = Vector3.Zero;
        return false;
    }

    private static void RemoveInward(ref Vector3 vector, Vector3 normal)
    {
        float inward = vector.Dot(normal);
        if (inward < 0) vector -= normal * inward;
    }
}
