using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace BoidsLab;

public readonly record struct BoidSnapshot3D(int Id, Vector3 Position, Vector3 Velocity);

/// <summary>朴素三维搜索：只读步前快照，邻居结果保存快照下标，不保存 Boid ID。</summary>
public sealed class NeighborSearch3D
{
    public bool HasResults { get; private set; }
    public IReadOnlyList<BoidSnapshot3D> Snapshots => HasResults ? _snapshotView : Array.Empty<BoidSnapshot3D>();
    public Aabb Bounds { get; private set; }
    public float Radius { get; private set; }
    public long CandidateChecks { get; private set; }
    public double AverageNeighbors { get; private set; }
    public double SearchMilliseconds { get; private set; }

    private BoidSnapshot3D[] _snapshots = Array.Empty<BoidSnapshot3D>();
    private IReadOnlyList<BoidSnapshot3D> _snapshotView = Array.Empty<BoidSnapshot3D>();
    private List<int>[] _neighbors = Array.Empty<List<int>>();
    private IReadOnlyList<int>[] _neighborViews = Array.Empty<IReadOnlyList<int>>();

    public void Invalidate() => HasResults = false;

    public IReadOnlyList<int> GetNeighborIndices(int index)
    {
        if (!HasResults) throw new InvalidOperationException("Run a neighbor search before reading results.");
        return _neighborViews[index];
    }

    public Vector3 GetOffset(int fromIndex, int toIndex)
    {
        if (!HasResults) throw new InvalidOperationException("Run a neighbor search before reading results.");
        return WorldGeometry3D.Offset(_snapshots[fromIndex].Position, _snapshots[toIndex].Position, Bounds);
    }

    public void Search(IReadOnlyList<Boid3D> boids, float radius, Aabb bounds)
    {
        ArgumentNullException.ThrowIfNull(boids);
        if (!float.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!bounds.Position.IsFinite() || !bounds.Size.IsFinite() || !bounds.End.IsFinite()
            || bounds.Size.X <= 0 || bounds.Size.Y <= 0 || bounds.Size.Z <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds));

        long started = Stopwatch.GetTimestamp();
        Invalidate();
        Bounds = bounds;
        Radius = radius;
        CandidateChecks = 0;
        if (_snapshots.Length != boids.Count)
        {
            _snapshots = new BoidSnapshot3D[boids.Count];
            _snapshotView = Array.AsReadOnly(_snapshots);
            _neighbors = new List<int>[boids.Count];
            _neighborViews = new IReadOnlyList<int>[boids.Count];
            for (int i = 0; i < boids.Count; i++)
            {
                _neighbors[i] = new List<int>();
                _neighborViews[i] = _neighbors[i].AsReadOnly();
            }
        }
        for (int i = 0; i < boids.Count; i++)
        {
            Vector3 position = boids[i].Position;
            if (!position.IsFinite()) throw new ArgumentException("Boid positions must be finite.", nameof(boids));
            _snapshots[i] = new BoidSnapshot3D(boids[i].Id, WorldGeometry3D.Wrap(position, bounds), boids[i].Velocity);
            _neighbors[i].Clear();
        }

        // TODO: 后续阶段增加网格搜索，复用本快照、距离判定、结果格式和统计语义。
        long totalNeighbors = 0;
        for (int i = 0; i < _snapshots.Length; i++)
            for (int j = 0; j < _snapshots.Length; j++)
            {
                if (i == j) continue;
                CandidateChecks++; // 有向距离检查：A 查 B 与 B 查 A 各计一次。
                Vector3 offset = WorldGeometry3D.Offset(_snapshots[i].Position, _snapshots[j].Position, bounds);
                if (!WorldGeometry3D.WithinRadius(offset, radius)) continue;
                _neighbors[i].Add(j); // 包含半径边缘和重合的其他个体，不包含自身。
                totalNeighbors++;
            }
        AverageNeighbors = boids.Count == 0 ? 0 : (double)totalNeighbors / boids.Count;
        SearchMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        HasResults = true;
    }
}
