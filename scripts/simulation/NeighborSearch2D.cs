using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace BoidsLab;

public enum NeighborSearchMode
{
    Naive = 0,
    UniformGrid = 1,
}

/// <summary>一个物理步开始时的状态副本；索引是本次快照下标，不是 Boid.Id。</summary>
public readonly record struct BoidSnapshot(int Id, Vector2 Position, Vector2 Velocity);

/// <summary>
/// 两种搜索共用快照、当前边界的距离判定和结果格式。结果属于最近一次物理步的起点，
/// 不随节点随后移动而变化；在下次 Search / Invalidate 时失效，不应跨步持有列表。
/// </summary>
public sealed class NeighborSearch2D
{
    public bool HasResults { get; private set; }
    public IReadOnlyList<BoidSnapshot> Snapshots => HasResults ? _snapshotView : Array.Empty<BoidSnapshot>();
    public float Radius { get; private set; }
    public Rect2? WrapBounds { get; private set; }
    public NeighborSearchMode Mode { get; private set; }
    public long CandidateChecks { get; private set; }
    public double AverageNeighbors { get; private set; }
    public double SearchMilliseconds { get; private set; }

    private BoidSnapshot[] _snapshots = Array.Empty<BoidSnapshot>();
    private IReadOnlyList<BoidSnapshot> _snapshotView = Array.Empty<BoidSnapshot>();
    private List<int>[] _neighbors = Array.Empty<List<int>>();
    private IReadOnlyList<int>[] _neighborViews = Array.Empty<IReadOnlyList<int>>();
    // 按需建立的无限网格；double 整数键避免远距离坐标转 int 溢出。
    private readonly Dictionary<(double X, double Y), List<int>> _cells = new();
    private int _columns;
    private int _rows;
    private double _cellWidth;
    private double _cellHeight;

    public IReadOnlyList<int> GetNeighborIndices(int index)
    {
        if (!HasResults) throw new InvalidOperationException("Run a neighbor search before reading results.");
        return _neighborViews[index];
    }

    /// <summary>无限世界取直接位移，环绕世界取最短环绕位移。</summary>
    public Vector2 GetOffset(int fromIndex, int toIndex)
    {
        if (!HasResults) throw new InvalidOperationException("Run a neighbor search before reading results.");
        return WorldGeometry2D.Offset(_snapshots[fromIndex].Position, _snapshots[toIndex].Position, WrapBounds);
    }

    public void Invalidate()
    {
        HasResults = false;
    }

    public void Search(IReadOnlyList<Boid2D> boids, float radius, NeighborSearchMode mode, Rect2? wrapBounds = null)
    {
        ArgumentNullException.ThrowIfNull(boids);
        if (wrapBounds is { } bounds && (!bounds.Position.IsFinite() || !bounds.End.IsFinite()
            || !bounds.Size.IsFinite() || bounds.Size.X <= 0 || bounds.Size.Y <= 0))
            throw new ArgumentOutOfRangeException(nameof(wrapBounds));
        if (!float.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));

        long started = Stopwatch.GetTimestamp();
        Invalidate();
        Radius = radius;
        WrapBounds = wrapBounds;
        Mode = mode;
        CandidateChecks = 0;
        PrepareStorage(boids.Count);
        // 只读取一次原生节点状态，之后两种算法都只访问这些值类型副本。
        for (int i = 0; i < boids.Count; i++)
        {
            Boid2D boid = boids[i];
            Vector2 position = boid.Position;
            if (!position.IsFinite()) throw new ArgumentException("Boid positions must be finite.", nameof(boids));
            _snapshots[i] = new BoidSnapshot(boid.Id, WorldGeometry2D.Wrap(position, WrapBounds), boid.Velocity);
            _neighbors[i].Clear();
        }

        _cells.Clear();
        if (mode == NeighborSearchMode.Naive) SearchNaive();
        else SearchGrid();

        long totalNeighbors = 0;
        foreach (List<int> neighbors in _neighbors) totalNeighbors += neighbors.Count;
        AverageNeighbors = boids.Count == 0 ? 0 : (double)totalNeighbors / boids.Count;
        SearchMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        HasResults = true;
    }

    private void PrepareStorage(int count)
    {
        if (_snapshots.Length == count) return;
        _snapshots = new BoidSnapshot[count];
        _snapshotView = Array.AsReadOnly(_snapshots);
        _neighbors = new List<int>[count];
        _neighborViews = new IReadOnlyList<int>[count];
        for (int i = 0; i < count; i++)
        {
            _neighbors[i] = new List<int>();
            _neighborViews[i] = _neighbors[i].AsReadOnly();
        }
    }

    private void SearchNaive()
    {
        for (int i = 0; i < _snapshots.Length; i++)
            for (int j = 0; j < _snapshots.Length; j++)
                CheckCandidate(i, j);
    }

    private void SearchGrid()
    {
        if (WrapBounds is { } bounds)
        {
            // 均分环绕区域，避免末端残格漏查跨边界邻居。
            _columns = (int)Math.Clamp(Math.Floor(bounds.Size.X / (double)Radius), 1, int.MaxValue / 2);
            _rows = (int)Math.Clamp(Math.Floor(bounds.Size.Y / (double)Radius), 1, int.MaxValue / 2);
            _cellWidth = bounds.Size.X / (double)_columns;
            _cellHeight = bounds.Size.Y / (double)_rows;
        }
        // 两种模式都只保存占用的格子。
        for (int i = 0; i < _snapshots.Length; i++)
        {
            var cell = CellAt(_snapshots[i].Position);
            if (!_cells.TryGetValue(cell, out List<int>? members))
                _cells.Add(cell, members = new List<int>());
            members.Add(i);
        }

        for (int i = 0; i < _snapshots.Length; i++)
        {
            var center = CellAt(_snapshots[i].Position);
            if (WrapBounds is not null)
            {
                // 只有一列或两列时，每格只访问一次，避免重复邻居。
                double xStart = _columns <= 2 ? 0 : center.X - 1;
                double yStart = _rows <= 2 ? 0 : center.Y - 1;
                for (int y = 0; y < Math.Min(3, _rows); y++)
                    for (int x = 0; x < Math.Min(3, _columns); x++)
                        CheckCell(i, ((xStart + x + _columns) % _columns, (yStart + y + _rows) % _rows));
                continue;
            }
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    var cell = (center.X + x, center.Y + y);
                    // 极远坐标的浮点整数可能无法表示 ±1；不要重复访问同一格。
                    if ((x != 0 && cell.Item1 == center.X) || (y != 0 && cell.Item2 == center.Y)) continue;
                    CheckCell(i, cell);
                }
        }
    }

    private void CheckCell(int index, (double X, double Y) cell)
    {
        if (!_cells.TryGetValue(cell, out List<int>? members)) return;
        foreach (int j in members) CheckCandidate(index, j);
    }

    private (double X, double Y) CellAt(Vector2 position) => WrapBounds is { } bounds
        ? (Math.Clamp(Math.Floor(((double)position.X - bounds.Position.X) / _cellWidth), 0, _columns - 1),
           Math.Clamp(Math.Floor(((double)position.Y - bounds.Position.Y) / _cellHeight), 0, _rows - 1))
        : (Math.Floor(position.X / (double)Radius), Math.Floor(position.Y / (double)Radius));

    private void CheckCandidate(int i, int j)
    {
        if (i == j) return;
        // 有向距离检查：A 检查 B 与 B 检查 A 分别计一次；不统计自身与格子查找。
        CandidateChecks++;
        Vector2 offset = WorldGeometry2D.Offset(_snapshots[i].Position, _snapshots[j].Position, WrapBounds);
        if ((double)offset.X * offset.X + (double)offset.Y * offset.Y <= (double)Radius * Radius)
            _neighbors[i].Add(j); // 包含恰在半径上的邻居，也包含位置重合的其他个体。
    }

}
