using System;
using System.Collections.Generic;
using Godot;

namespace BoidsLab;

public readonly record struct ObstacleHit2D(bool Blocked, float Fraction, Vector2 Normal, ulong ObstacleId = 0, int Part = -1);
public readonly record struct QueryContact2D(Vector2 Normal, float Depth);

/// <summary>
/// 不激活的静态 PhysicsServer 空间。同步只改障碍几何，不执行 Step、不推进 Boid。
/// 同一模拟步只 Synchronize 一次；独立编辑/新增/拾取事务开始时另行同步。
/// </summary>
public sealed class ObstacleQuery2D : IDisposable
{
    private sealed class Body
    {
        public readonly List<(Rid Body, Rid Shape)> Parts = new();
    }
    private readonly Rid _space = PhysicsServer2D.SpaceCreate();
    private readonly PhysicsDirectSpaceState2D _state;
    private readonly CircleShape2D _probeShape = new();
    private readonly PhysicsShapeQueryParameters2D _parameters = new() { CollisionMask = ObstacleTuning.CollisionLayer, Margin = 0 };
    private readonly PhysicsPointQueryParameters2D _point = new() { CollisionMask = ObstacleTuning.CollisionLayer };
    private readonly Dictionary<ulong, Body> _bodies = new();
    private readonly Dictionary<Rid, (ulong Id, int Part)> _identities = new();
    private readonly Godot.Collections.Array<Rid> _excluded = new();
    private readonly HashSet<ulong> _seen = new();
    private readonly List<ulong> _removed = new();
    public readonly List<QueryContact2D> Contacts = new(ObstacleTuning.ContactLimit);
    public long QueryCount { get; private set; }
    public int BodyCount => _bodies.Count;
    public bool ContactsSaturated { get; private set; }
    public Rect2? Bounds { get; private set; }
    private bool _disposed;

    public ObstacleQuery2D()
    {
        _state = PhysicsServer2D.SpaceGetDirectState(_space);
        _parameters.Shape = _probeShape;
    }

    public void Synchronize(IReadOnlyList<Obstacle2D> obstacles, Rect2? bounds = null, Transform2D? coordinates = null)
    {
        Bounds = bounds;
        QueryCount = 0;
        ContactsSaturated = false;
        _seen.Clear();
        Transform2D inverse = (coordinates ?? Transform2D.Identity).AffineInverse();
        foreach (Obstacle2D obstacle in obstacles)
        {
            if (!GodotObject.IsInstanceValid(obstacle) || obstacle.IsQueuedForDeletion()) continue;
            var parts = obstacle.Parts;
            if (parts.Count == 0) continue;
            if (obstacle.IsCustom && obstacle.IsInsideTree()) obstacle.QueueRedraw();
            Transform2D transform = inverse * (obstacle.IsInsideTree() ? obstacle.GlobalTransform : obstacle.Transform);
            if (bounds is { } area)
            {
                bool intersects = false;
                foreach (var part in parts)
                    if (part.Shape is not null && ((transform * part.Transform) * part.Shape.GetRect()).Intersects(area, true)) { intersects = true; break; }
                if (!intersects) continue;
            }
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
                    Rid rid = PhysicsServer2D.BodyCreate();
                    PhysicsServer2D.BodySetMode(rid, PhysicsServer2D.BodyMode.Static);
                    PhysicsServer2D.BodySetCollisionLayer(rid, ObstacleTuning.CollisionLayer);
                    PhysicsServer2D.BodySetCollisionMask(rid, 0);
                    PhysicsServer2D.BodyAddShape(rid, part.Shape.GetRid());
                    PhysicsServer2D.BodySetSpace(rid, _space);
                    body.Parts.Add((rid, part.Shape.GetRid()));
                    _identities.Add(rid, (id, i));
                }
            }
            // 每个凸部件一个轻量静态 body，逻辑身份保持统一；可独立排除并稳定选择接触法线。
            for (int i = 0; i < parts.Count; i++)
                PhysicsServer2D.BodySetState(body.Parts[i].Body, PhysicsServer2D.BodyState.Transform, transform * parts[i].Transform);
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
            PhysicsServer2D.FreeRid(part.Body);
        }
        body.Parts.Clear();
    }

    private void Prepare(Vector2 point, float radius)
    {
        float actual = Mathf.Max(ObstacleTuning.MinimumRadius, radius);
        if (_probeShape.Radius != actual) _probeShape.Radius = actual;
        _parameters.Transform = new Transform2D(0, point);
        _parameters.Motion = Vector2.Zero;
    }

    public bool Occupied(Vector2 point, float radius)
    {
        if (BodyCount == 0) return false;
        Prepare(point, radius);
        QueryCount++;
        var hits = _state.IntersectShape(_parameters, 1);
        bool occupied = hits.Count > 0;
        ((Godot.Collections.Array)hits).Dispose();
        return occupied;
    }

    public bool ContainsPoint(Vector2 point, ulong obstacleId)
    {
        _point.Position = point;
        QueryCount++;
        var hits = _state.IntersectPoint(_point, Mathf.Max(1, BodyCount * ObstacleTuning.ContactLimit));
        bool found = false;
        foreach (var hit in hits)
        {
            found |= _identities.TryGetValue(hit["rid"].AsRid(), out var identity) && identity.Id == obstacleId;
            hit.Dispose();
        }
        ((Godot.Collections.Array)hits).Dispose();
        return found;
    }

    public bool GatherContacts(Vector2 point, float radius)
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
                Vector2 normal = rest["normal"].AsVector2();
                // 对圆/球而言，表面点沿原生外法线投影给出恢复深度。
                // 不用两个几乎重合的接触点相减归一化，避免浅接触的切向噪声。
                float depth = (rest["point"].AsVector2() - point).Dot(normal) + _probeShape.Radius;
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
            return order != 0 ? order : a.Normal.Y.CompareTo(b.Normal.Y);
        });
        return Contacts.Count > 0;
    }

    /// <summary>调用者先处理初始重叠；CastMotion 本身会忽略已重叠形状。</summary>
    public ObstacleHit2D Sweep(Vector2 origin, Vector2 motion, float radius)
    {
        float length = motion.Length();
        if (BodyCount == 0 || length <= ObstacleTuning.Epsilon) return new(false, 1, Vector2.Zero);
        Prepare(origin, radius);
        _parameters.Motion = motion;
        QueryCount++;
        float[] cast = _state.CastMotion(_parameters);
        if (cast.Length < 2) return new(true, 0, Vector2.Zero);
        if (cast[0] >= 1) return new(false, 1, Vector2.Zero);
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
        Vector2 normal = Vector2.Zero;
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
            Vector2 candidate = rest["normal"].AsVector2();
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

    private static int CompareNormal(Vector2 a, Vector2 b)
    {
        int order = a.X.CompareTo(b.X);
        if (order != 0) return order;
        return a.Y.CompareTo(b.Y);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (Body body in _bodies.Values) ReleaseParts(body);
        _bodies.Clear();
        _identities.Clear();
        PhysicsServer2D.FreeRid(_space);
        ((Godot.Collections.Array)_excluded).Dispose();
        _parameters.Dispose(); _point.Dispose(); _probeShape.Dispose();
    }
}
