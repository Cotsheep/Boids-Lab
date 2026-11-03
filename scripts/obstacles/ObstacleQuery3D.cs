using System;
using System.Collections.Generic;
using Godot;

namespace BoidsLab;

public readonly record struct ObstacleHit3D(bool Blocked, float Fraction, Vector3 Normal, ulong ObstacleId = 0, int Part = -1);
public readonly record struct QueryContact3D(Vector3 Normal, float Depth);

/// <summary>
/// 不激活的静态 PhysicsServer 空间。同步只改障碍几何，不执行 Step、不推进 Boid。
/// 同一模拟步只 Synchronize 一次；独立编辑/新增/拾取事务开始时另行同步。
/// </summary>
public sealed class ObstacleQuery3D : IDisposable
{
    private sealed class Body
    {
        public readonly List<(Rid Body, Rid Shape)> Parts = new();
    }
    private readonly Rid _space = PhysicsServer3D.SpaceCreate();
    private readonly PhysicsDirectSpaceState3D _state;
    private readonly SphereShape3D _probeShape = new();
    private readonly PhysicsShapeQueryParameters3D _parameters = new() { CollisionMask = ObstacleTuning.CollisionLayer, Margin = 0 };
    private readonly PhysicsRayQueryParameters3D _ray = new() { CollisionMask = ObstacleTuning.CollisionLayer, HitFromInside = true };
    private readonly Dictionary<ulong, Body> _bodies = new();
    private readonly Dictionary<Rid, (ulong Id, int Part)> _identities = new();
    private readonly Godot.Collections.Array<Rid> _excluded = new();
    private readonly HashSet<ulong> _seen = new();
    private readonly List<ulong> _removed = new();
    public readonly List<QueryContact3D> Contacts = new(ObstacleTuning.ContactLimit);
    public long QueryCount { get; private set; }
    public int BodyCount => _bodies.Count;
    public bool ContactsSaturated { get; private set; }
    private bool _disposed;

    public ObstacleQuery3D()
    {
        _state = PhysicsServer3D.SpaceGetDirectState(_space);
        _parameters.Shape = _probeShape;
    }

    public void Synchronize(IReadOnlyList<Obstacle3D> obstacles, Transform3D? coordinates = null)
    {
        QueryCount = 0;
        ContactsSaturated = false;
        _seen.Clear();
        Transform3D inverse = (coordinates ?? Transform3D.Identity).AffineInverse();
        foreach (Obstacle3D obstacle in obstacles)
        {
            if (!GodotObject.IsInstanceValid(obstacle) || obstacle.IsQueuedForDeletion() || obstacle.IsPreview) continue;
            var parts = obstacle.Parts;
            if (parts.Count == 0) continue;
            if (obstacle.IsCustom && obstacle.IsInsideTree()) obstacle.RefreshCollisionVisuals();
            Transform3D transform = inverse * (obstacle.IsInsideTree() ? obstacle.GlobalTransform : obstacle.Transform);
            ulong id = obstacle.GetInstanceId();
            _seen.Add(id);
            if (!_bodies.TryGetValue(id, out Body? body))
            {
                body = new Body();
                _bodies.Add(id, body);
            }
            bool rebuild = body.Parts.Count != parts.Count;
            if (!rebuild)
                for (int i = 0; i < parts.Count; i++)
                    if (body.Parts[i].Shape != parts[i].Shape.GetRid()) { rebuild = true; break; }
            if (rebuild)
            {
                ReleaseParts(body);
                for (int i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    if (part.Shape is null) throw new InvalidOperationException("障碍碰撞部件缺少 Shape 资源。");
                    Rid rid = PhysicsServer3D.BodyCreate();
                    PhysicsServer3D.BodySetMode(rid, PhysicsServer3D.BodyMode.Static);
                    PhysicsServer3D.BodySetCollisionLayer(rid, ObstacleTuning.CollisionLayer);
                    PhysicsServer3D.BodySetCollisionMask(rid, 0);
                    PhysicsServer3D.BodyAddShape(rid, part.Shape.GetRid());
                    PhysicsServer3D.BodySetSpace(rid, _space);
                    body.Parts.Add((rid, part.Shape.GetRid()));
                    _identities.Add(rid, (id, i));
                }
            }
            // 每个凸部件一个轻量静态 body，逻辑身份保持统一；可独立排除并稳定选择接触法线。
            for (int i = 0; i < parts.Count; i++)
                PhysicsServer3D.BodySetState(body.Parts[i].Body, PhysicsServer3D.BodyState.Transform, transform * parts[i].Transform);
        }
        _removed.Clear();
        foreach (ulong id in _bodies.Keys) if (!_seen.Contains(id)) _removed.Add(id);
        foreach (ulong id in _removed) Remove(id);
    }

    public void Remove(ulong id)
    {
        if (!_bodies.Remove(id, out Body? body)) return;
        ReleaseParts(body);
    }

    private void ReleaseParts(Body body)
    {
        foreach (var part in body.Parts)
        {
            _identities.Remove(part.Body);
            PhysicsServer3D.FreeRid(part.Body);
        }
        body.Parts.Clear();
    }

    private void Prepare(Vector3 point, float radius)
    {
        float actual = Mathf.Max(ObstacleTuning.MinimumRadius, radius);
        if (_probeShape.Radius != actual) _probeShape.Radius = actual;
        _parameters.Transform = new Transform3D(Basis.Identity, point);
        _parameters.Motion = Vector3.Zero;
    }

    public bool Occupied(Vector3 point, float radius)
    {
        if (BodyCount == 0) return false;
        Prepare(point, radius);
        QueryCount++;
        var hits = _state.IntersectShape(_parameters, 1);
        bool occupied = hits.Count > 0;
        ((Godot.Collections.Array)hits).Dispose();
        return occupied;
    }

    public bool GatherContacts(Vector3 point, float radius)
    {
        Contacts.Clear();
        if (BodyCount == 0) return false;
        Prepare(point, radius);
        QueryCount++;
        var pairs = _state.CollideShape(_parameters, ObstacleTuning.ContactLimit);
        ContactsSaturated |= pairs.Count >= ObstacleTuning.ContactLimit * 2;
        bool overlaps = pairs.Count > 0;
        ((Godot.Collections.Array)pairs).Dispose();
        if (overlaps)
        {
            _excluded.Clear();
            for (int i = 0; i < ObstacleTuning.ContactLimit; i++)
            {
                _parameters.Exclude = _excluded;
                QueryCount++;
                using var rest = _state.GetRestInfo(_parameters);
                if (rest.Count == 0) break;
                Vector3 normal = rest["normal"].AsVector3();
                // 对圆/球而言，表面点沿原生外法线投影给出恢复深度。
                // 不用两个几乎重合的接触点相减归一化，避免浅接触的切向噪声。
                float depth = (rest["point"].AsVector3() - point).Dot(normal) + _probeShape.Radius;
                if (depth > ObstacleTuning.Epsilon && normal.LengthSquared() > ObstacleTuning.Epsilon)
                    Contacts.Add(new(normal, depth));
                _excluded.Add(rest["rid"].AsRid());
                if (i == ObstacleTuning.ContactLimit - 1) ContactsSaturated = true;
            }
            _excluded.Clear();
            _parameters.Exclude = _excluded;
        }
        // 接触数组顺序不是契约：深度优先，然后按法线坐标固定并列顺序。
        Contacts.Sort(static (a, b) =>
        {
            int order = b.Depth.CompareTo(a.Depth);
            if (order != 0) return order;
            order = a.Normal.X.CompareTo(b.Normal.X);
            if (order != 0) return order;
            order = a.Normal.Y.CompareTo(b.Normal.Y);
            return order != 0 ? order : a.Normal.Z.CompareTo(b.Normal.Z);
        });
        return Contacts.Count > 0;
    }

    /// <summary>调用者先处理初始重叠；CastMotion 本身会忽略已重叠形状。</summary>
    public ObstacleHit3D Sweep(Vector3 origin, Vector3 motion, float radius)
    {
        float length = motion.Length();
        if (BodyCount == 0 || length <= ObstacleTuning.Epsilon) return new(false, 1, Vector3.Zero);
        Prepare(origin, radius);
        _parameters.Motion = motion;
        QueryCount++;
        float[] cast = _state.CastMotion(_parameters);
        if (cast.Length < 2) return new(true, 0, Vector3.Zero);
        if (cast[0] >= 1) return new(false, 1, Vector3.Zero);
        float safe = cast[0], unsafeFraction = cast[1];
        // 缩小引擎的比例区间，使高速长线段不留下与线长成正比的安全退让。
        for (int i = 0; i < ObstacleTuning.SweepRefinements && (unsafeFraction - safe) * length > ObstacleTuning.SweepTolerance; i++)
        {
            float span = unsafeFraction - safe;
            Prepare(origin + motion * safe, radius);
            _parameters.Motion = motion * span;
            QueryCount++;
            float[] refine = _state.CastMotion(_parameters);
            if (refine.Length < 2 || refine[0] >= 1) break;
            unsafeFraction = safe + span * refine[1];
            float next = safe + span * refine[0];
            if (next == safe) break;
            safe = next;
        }
        // RestInfo 只在明确命中位置做静态接触查询，绝不当成运动扫掠。
        // 接触点对相减会在浅接触处放大浮点误差；运动投影使用引擎的原生法线。
        Prepare(origin + motion * unsafeFraction, radius + ObstacleTuning.Skin);
        Vector3 normal = Vector3.Zero;
        ulong id = 0;
        int partIndex = -1;
        float mostInward = float.PositiveInfinity;
        _excluded.Clear();
        for (int i = 0; i < ObstacleTuning.ContactLimit; i++)
        {
            _parameters.Exclude = _excluded;
            QueryCount++;
            using var rest = _state.GetRestInfo(_parameters);
            if (rest.Count == 0) break;
            Rid rid = rest["rid"].AsRid();
            Vector3 candidate = rest["normal"].AsVector3();
            float inward = candidate.Dot(motion);
            var identity = _identities[rid];
            int normalOrder = CompareNormal(candidate, normal);
            bool earlierIdentity = identity.Id < id || (identity.Id == id && identity.Part < partIndex);
            if (inward < mostInward || (inward == mostInward && (normalOrder < 0 || (normalOrder == 0 && earlierIdentity))))
            {
                mostInward = inward;
                normal = candidate;
                id = identity.Id; partIndex = identity.Part;
            }
            _excluded.Add(rid);
            if (i == ObstacleTuning.ContactLimit - 1) ContactsSaturated = true;
        }
        _excluded.Clear();
        _parameters.Exclude = _excluded;
        return new(true, safe, normal, id, partIndex);
    }

    private static int CompareNormal(Vector3 a, Vector3 b)
    {
        int order = a.X.CompareTo(b.X);
        if (order != 0) return order;
        order = a.Y.CompareTo(b.Y);
        return order != 0 ? order : a.Z.CompareTo(b.Z);
    }

    public float? HitDistance(Vector3 origin, Vector3 direction, float maxDistance, out ulong obstacleId, ulong preferredId = 0)
    {
        _ray.From = origin;
        _ray.To = origin + direction * maxDistance;
        _excluded.Clear();
        float nearest = float.PositiveInfinity;
        obstacleId = 0;
        for (int i = 0; i < ObstacleTuning.ContactLimit; i++)
        {
            _ray.Exclude = _excluded;
            QueryCount++;
            using var hit = _state.IntersectRay(_ray);
            if (hit.Count == 0) break;
            float distance = origin.DistanceTo(hit["position"].AsVector3());
            if (distance > nearest + ObstacleTuning.Epsilon) break;
            Rid rid = hit["rid"].AsRid();
            ulong id = _identities[rid].Id;
            if (distance < nearest || id == preferredId || (obstacleId != preferredId && id < obstacleId))
            {
                nearest = distance;
                obstacleId = id;
            }
            _excluded.Add(rid);
            if (i == ObstacleTuning.ContactLimit - 1) ContactsSaturated = true;
        }
        _excluded.Clear();
        _ray.Exclude = _excluded;
        return float.IsPositiveInfinity(nearest) ? null : nearest;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (Body body in _bodies.Values) ReleaseParts(body);
        _bodies.Clear();
        _identities.Clear();
        PhysicsServer3D.FreeRid(_space);
        ((Godot.Collections.Array)_excluded).Dispose();
        _parameters.Dispose(); _ray.Dispose(); _probeShape.Dispose();
    }
}
