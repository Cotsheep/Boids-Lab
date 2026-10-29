using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace BoidsLab;

public readonly record struct BoidSnapshot3D(int Id, Vector3 Position, Vector3 Velocity);

/// <summary>三维朴素/网格搜索共用步前快照与环绕距离，邻居结果保存快照下标。</summary>
public sealed class NeighborSearch3D
{
    public bool HasResults { get; private set; }
    public IReadOnlyList<BoidSnapshot3D> Snapshots => HasResults ? _snapshotView : Array.Empty<BoidSnapshot3D>();
    public Aabb Bounds { get; private set; }
    public float Radius { get; private set; }
    public NeighborSearchMode Mode { get; private set; }
    public long CandidateChecks { get; private set; }
    public double AverageNeighbors { get; private set; }
    public double SearchMilliseconds { get; private set; }

    private BoidSnapshot3D[] _snapshots = Array.Empty<BoidSnapshot3D>();
    private IReadOnlyList<BoidSnapshot3D> _snapshotView = Array.Empty<BoidSnapshot3D>();
    private List<int>[] _neighbors = Array.Empty<List<int>>();
    private IReadOnlyList<int>[] _neighborViews = Array.Empty<IReadOnlyList<int>>();
    private readonly Dictionary<Vector3I, List<int>> _cells = new();
    private Vector3I _gridSize = Vector3I.One;
    private Vector3 _cellSize = Vector3.One;

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

    public void Search(IReadOnlyList<Boid3D> boids, float radius, Aabb bounds, NeighborSearchMode mode = NeighborSearchMode.Naive)
    {
        ArgumentNullException.ThrowIfNull(boids);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!float.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!bounds.Position.IsFinite() || !bounds.Size.IsFinite() || !bounds.End.IsFinite()
            || bounds.Size.X <= 0 || bounds.Size.Y <= 0 || bounds.Size.Z <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds));

        long started = Stopwatch.GetTimestamp();
        Invalidate();
        Bounds = bounds;
        Radius = radius;
        Mode = mode;
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

        _cells.Clear();
        if (mode == NeighborSearchMode.UniformGrid) SearchGrid();
        else
            for (int i = 0; i < _snapshots.Length; i++)
                for (int j = 0; j < _snapshots.Length; j++) CheckCandidate(i, j);
        long totalNeighbors = 0;
        foreach (List<int> neighbors in _neighbors) totalNeighbors += neighbors.Count;
        AverageNeighbors = boids.Count == 0 ? 0 : (double)totalNeighbors / boids.Count;
        SearchMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        HasResults = true;
    }

    private void SearchGrid()
    {
        for (int axis = 0; axis < 3; axis++)
        {
            _gridSize[axis] = (int)Math.Clamp(Math.Floor(Bounds.Size[axis] / (double)Radius), 1, int.MaxValue / 2);
            _cellSize[axis] = Bounds.Size[axis] / _gridSize[axis];
        }
        // 均分整条边，避免末端残格；多格时格宽至少为感知半径，只需检查 3×3×3 邻域。
        for (int i = 0; i < _snapshots.Length; i++)
        {
            Vector3I key = CellAt(_snapshots[i].Position);
            if (!_cells.TryGetValue(key, out List<int>? members)) _cells.Add(key, members = new List<int>());
            members.Add(i);
        }
        for (int i = 0; i < _snapshots.Length; i++)
        {
            Vector3I center = CellAt(_snapshots[i].Position);
            int xStart = _gridSize.X <= 2 ? 0 : center.X - 1;
            int yStart = _gridSize.Y <= 2 ? 0 : center.Y - 1;
            int zStart = _gridSize.Z <= 2 ? 0 : center.Z - 1;
            for (int x = 0; x < Math.Min(3, _gridSize.X); x++)
                for (int y = 0; y < Math.Min(3, _gridSize.Y); y++)
                    for (int z = 0; z < Math.Min(3, _gridSize.Z); z++)
                    {
                        var cell = new Vector3I((xStart + x + _gridSize.X) % _gridSize.X,
                            (yStart + y + _gridSize.Y) % _gridSize.Y, (zStart + z + _gridSize.Z) % _gridSize.Z);
                        if (!_cells.TryGetValue(cell, out List<int>? members)) continue;
                        foreach (int j in members) CheckCandidate(i, j);
                    }
            // 与朴素算法保持相同累加顺序，避免切换搜索方式改变浮点转向结果。
            _neighbors[i].Sort();
        }
    }

    private Vector3I CellAt(Vector3 position)
    {
        var result = new Vector3I();
        for (int axis = 0; axis < 3; axis++)
            result[axis] = (int)Math.Clamp(Math.Floor(((double)position[axis] - Bounds.Position[axis]) / _cellSize[axis]), 0, _gridSize[axis] - 1);
        return result;
    }

    private void CheckCandidate(int i, int j)
    {
        if (i == j) return;
        CandidateChecks++;
        Vector3 offset = WorldGeometry3D.Offset(_snapshots[i].Position, _snapshots[j].Position, Bounds);
        if (WorldGeometry3D.WithinRadius(offset, Radius)) _neighbors[i].Add(j);
    }
}
