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
/// 两种搜索共用快照、环绕距离判定和结果格式。结果属于最近一次物理步的起点，
/// 不随节点随后移动而变化；在下次 Search / Invalidate 时失效，不应跨步持有列表。
/// </summary>
public sealed class NeighborSearch2D
{
    public bool HasResults { get; private set; }
    public IReadOnlyList<BoidSnapshot> Snapshots => HasResults ? _snapshotView : Array.Empty<BoidSnapshot>();
    public Rect2 Bounds { get; private set; }
    public float Radius { get; private set; }
    public NeighborSearchMode Mode { get; private set; }
    public long CandidateChecks { get; private set; }
    public double AverageNeighbors { get; private set; }
    public double SearchMilliseconds { get; private set; }

    private BoidSnapshot[] _snapshots = Array.Empty<BoidSnapshot>();
    private IReadOnlyList<BoidSnapshot> _snapshotView = Array.Empty<BoidSnapshot>();
    private List<int>[] _neighbors = Array.Empty<List<int>>();
    private IReadOnlyList<int>[] _neighborViews = Array.Empty<IReadOnlyList<int>>();
    private readonly Dictionary<Vector2I, List<int>> _cells = new();
    private int _columns;
    private int _rows;
    private double _cellWidth;
    private double _cellHeight;

    public IReadOnlyList<int> GetNeighborIndices(int index)
    {
        if (!HasResults) throw new InvalidOperationException("Run a neighbor search before reading results.");
        return _neighborViews[index];
    }

    /// <summary>从自身到邻居的最短环绕位移；供后续分离、聚集使用，不能直接相减节点坐标。</summary>
    public Vector2 GetOffset(int fromIndex, int toIndex)
    {
        if (!HasResults) throw new InvalidOperationException("Run a neighbor search before reading results.");
        return WrappedOffset(_snapshots[fromIndex].Position, _snapshots[toIndex].Position, Bounds.Size);
    }

    public void Invalidate()
    {
        HasResults = false;
    }

    public void Search(IReadOnlyList<Boid2D> boids, Rect2 bounds, float radius, NeighborSearchMode mode)
    {
        ArgumentNullException.ThrowIfNull(boids);
        if (!bounds.Position.IsFinite() || !bounds.Size.IsFinite() || !bounds.End.IsFinite()
            || bounds.Size.X <= 0 || bounds.Size.Y <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds));
        if (!float.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));

        long started = Stopwatch.GetTimestamp();
        Invalidate();
        Bounds = bounds;
        Radius = radius;
        Mode = mode;
        CandidateChecks = 0;
        PrepareStorage(boids.Count);
        // 只读取一次原生节点状态，之后两种算法都只访问这些值类型副本。
        for (int i = 0; i < boids.Count; i++)
        {
            Boid2D boid = boids[i];
            Vector2 position = boid.Position;
            if (!position.IsFinite()) throw new ArgumentException("Boid positions must be finite.", nameof(boids));
            // SpawnBoid 或调试代码可以传入边界外坐标；只规范化副本，不修改节点。
            if (!bounds.HasPoint(position))
                position = new Vector2(Mathf.Wrap(position.X, bounds.Position.X, bounds.End.X),
                    Mathf.Wrap(position.Y, bounds.Position.Y, bounds.End.Y));
            _snapshots[i] = new BoidSnapshot(boid.Id, position, boid.Velocity);
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
        // 均匀分割整个环绕区域，避免最后一列/行残缺而漏掉跨边界邻居。
        // 每个单元不小于半径（某轴只有一格时例外），因此只需自身和相邻格。
        _columns = CellCount(Bounds.Size.X);
        _rows = CellCount(Bounds.Size.Y);
        _cellWidth = Bounds.Size.X / (double)_columns;
        _cellHeight = Bounds.Size.Y / (double)_rows;
        for (int i = 0; i < _snapshots.Length; i++)
        {
            Vector2I cell = CellAt(_snapshots[i].Position);
            if (!_cells.TryGetValue(cell, out List<int>? members))
                _cells.Add(cell, members = new List<int>());
            members.Add(i);
        }

        for (int i = 0; i < _snapshots.Length; i++)
        {
            Vector2I center = CellAt(_snapshots[i].Position);
            // 只有 1 或 2 列/行时，三个偏移会访问重复格；改为每格只访问一次。
            int xStart = _columns <= 2 ? 0 : center.X - 1;
            int yStart = _rows <= 2 ? 0 : center.Y - 1;
            for (int y = 0; y < Math.Min(3, _rows); y++)
                for (int x = 0; x < Math.Min(3, _columns); x++)
                {
                    var cell = new Vector2I(WrapCell(xStart + x, _columns), WrapCell(yStart + y, _rows));
                    if (!_cells.TryGetValue(cell, out List<int>? members)) continue;
                    foreach (int j in members) CheckCandidate(i, j);
                }
        }
    }

    private int CellCount(float length) => (int)Math.Clamp(Math.Floor(length / (double)Radius), 1, int.MaxValue / 2);

    private Vector2I CellAt(Vector2 position) => new(
        Math.Clamp((int)Math.Floor(((double)position.X - Bounds.Position.X) / _cellWidth), 0, _columns - 1),
        Math.Clamp((int)Math.Floor(((double)position.Y - Bounds.Position.Y) / _cellHeight), 0, _rows - 1));

    private static int WrapCell(int coordinate, int count) => (coordinate % count + count) % count;

    private void CheckCandidate(int i, int j)
    {
        if (i == j) return;
        // 有向距离检查：A 检查 B 与 B 检查 A 分别计一次；不统计自身与格子查找。
        CandidateChecks++;
        Vector2 offset = WrappedOffset(_snapshots[i].Position, _snapshots[j].Position, Bounds.Size);
        if ((double)offset.X * offset.X + (double)offset.Y * offset.Y <= (double)Radius * Radius)
            _neighbors[i].Add(j); // 包含恰在半径上的邻居，也包含位置重合的其他个体。
    }

    /// <summary>两个已位于同一环绕区域内的点之间的最短位移，模拟与当前画面调试共用。</summary>
    public static Vector2 WrappedOffset(Vector2 from, Vector2 to, Vector2 size)
    {
        Vector2 offset = to - from;
        if (offset.X > size.X * 0.5f) offset.X -= size.X;
        else if (offset.X < -size.X * 0.5f) offset.X += size.X;
        if (offset.Y > size.Y * 0.5f) offset.Y -= size.Y;
        else if (offset.Y < -size.Y * 0.5f) offset.Y += size.Y;
        return offset;
    }
}
