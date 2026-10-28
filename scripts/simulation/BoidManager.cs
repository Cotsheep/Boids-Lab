using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace BoidsLab;

/// <summary>2D 世界管理器：统一调度个体更新、管理实例和相机，不引用 UI。</summary>
public partial class BoidManager : Node2D, ISimulationWorld
{
    // 初始化及 Home 复位的参考尺寸；实际 WorldBounds 来自相机可见区域。
    [Export] public Vector2 WorldSize { get; set; } = new(1280, 720);
    [Export] public PackedScene BoidScene { get; set; } = null!;

    public string DisplayName => "二维世界";
    public string LengthUnit => "像素";
    public BoidConfig Config => _config
        ?? throw new InvalidOperationException("Initialize the world with a shared configuration first.");
    public Rect2 WorldBounds { get; private set; }
    public IReadOnlyList<Boid2D> Boids { get; }
    public NeighborSearch2D NeighborSearch { get; } = new();
    public Boid2D? SelectedBoid { get; private set; }
    public SimulationStats Stats { get; private set; }
    public bool IsRunning { get; private set; }
    // 基础运动、邻居搜索与分离/对齐/聚集规则均已可运行。
    public bool CoreImplemented => true;
    public bool CanStep => false; // 按当前需求，暂不开放 UI 单步功能。

    private readonly List<Boid2D> _boids = new();
    private BoidConfig? _config;
    private Node2D _boidsRoot = null!;
    private SimulationView _view = null!;
    private SimulationCamera2D _camera = null!;
    private int _nextBoidId;
    private Vector2[] _steeringAccelerations = Array.Empty<Vector2>();
    private Boid2D? _pressedBoid;

    public BoidManager()
    {
        Boids = _boids.AsReadOnly();
    }

    // MainController 在将世界加入场景树前传入同一份共享配置。
    public void Initialize(BoidConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!float.IsFinite(WorldSize.X) || !float.IsFinite(WorldSize.Y)
            || WorldSize.X <= 0 || WorldSize.Y <= 0)
            throw new InvalidOperationException("WorldSize must contain positive finite dimensions.");

        _config = config;
        Config.Normalize();
        WorldBounds = new Rect2(Vector2.Zero, WorldSize);
        if (IsNodeReady())
        {
            _view.Bind(Config, this);
            _camera.Configure(new Rect2(Vector2.Zero, WorldSize));
            Reset();
        }
    }

    public override void _Ready()
    {
        _boidsRoot = GetNode<Node2D>("Boids");
        _view = GetNode<SimulationView>("SimulationView");
        _camera = GetNode<SimulationCamera2D>("Camera2D");
        _view.Bind(Config, this);
        _camera.ViewChanged += UpdateWorldBounds;
        _camera.WorldPressStarted += OnWorldPressStarted;
        _camera.WorldPressEnded += OnWorldPressEnded;
        _camera.Configure(new Rect2(Vector2.Zero, WorldSize));
        Reset();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsRunning) Step(delta);
    }

    private void UpdateWorldBounds()
    {
        Rect2 viewportRect = GetViewport().GetVisibleRect();
        if (!viewportRect.HasArea()) return;

        // 从视口坐标反变换到世界根节点坐标，背景和个体使用同一套边界。
        Rect2 bounds = GetGlobalTransformWithCanvas().AffineInverse() * viewportRect;
        if (!bounds.HasArea() || !bounds.Position.IsFinite() || !bounds.Size.IsFinite()) return;
        if (bounds == WorldBounds) return;

        WorldBounds = bounds;
        // 即使暂停也立即同步边界；只环绕位置，不推进运动或重新生成个体。
        foreach (Boid2D boid in _boids) WrapPosition(boid);
        InvalidateNeighborResults();
        _view.QueueRedraw();
    }

    private bool WrapPosition(Boid2D boid)
    {
        Vector2 position = boid.Position;
        // 边界内的个体不做取模，避免无意义的浮点误差影响位置。
        if (WorldBounds.HasPoint(position)) return false;
        boid.Position = new Vector2(
            Mathf.Wrap(position.X, WorldBounds.Position.X, WorldBounds.End.X),
            Mathf.Wrap(position.Y, WorldBounds.Position.Y, WorldBounds.End.Y));
        return true;
    }

    public void SetRunning(bool running) => IsRunning = running;

    private void OnWorldPressStarted(Vector2 viewportPosition)
    {
        _pressedBoid = null;
        float closest = float.PositiveInfinity;
        foreach (Boid2D boid in _boids)
        {
            if (!boid.HitTest(viewportPosition)) continue;
            float distance = (boid.GetGlobalTransformWithCanvas() * Vector2.Zero).DistanceSquaredTo(viewportPosition);
            // 完全重叠时优先命中已经选中的个体，仍可再次点击取消。
            if (distance > closest || (distance == closest && boid != SelectedBoid)) continue;
            closest = distance;
            _pressedBoid = boid;
        }
    }

    private void OnWorldPressEnded(bool clicked)
    {
        Boid2D? target = _pressedBoid;
        _pressedBoid = null;
        if (!clicked) return;
        // 运行时按下命中的个体可能已经移动；仍选择同一个实例，而非松开处的新个体。
        if (target is not null && !_boids.Contains(target)) return;
        SelectBoid(target == SelectedBoid ? null : target);
    }

    private void SelectBoid(Boid2D? boid)
    {
        SelectedBoid?.SetSelected(false);
        SelectedBoid = boid;
        SelectedBoid?.SetSelected(true);
        _view.QueueRedraw();
    }

    public void Reset()
    {
        Config.Normalize();
        IsRunning = false;
        SelectBoid(null);
        _pressedBoid = null;
        foreach (Boid2D boid in _boids)
        {
            _boidsRoot.RemoveChild(boid);
            boid.QueueFree();
        }
        _boids.Clear();
        _nextBoidId = 0;
        InitializePopulation();
        InvalidateNeighborResults();
        _view.QueueRedraw();
    }

    public void StepOnce()
    {
        if (CanStep) Step(1.0 / Engine.PhysicsTicksPerSecond);
    }

    public void ConfigurationChanged()
    {
        Config.Normalize();
        if (NeighborSearch.HasResults && (NeighborSearch.Mode != Config.NeighborSearchMode
            || NeighborSearch.Radius != Config.PerceptionRadius))
            InvalidateNeighborResults();
        // 管理器和个体共享 Config；不复制参数，也不在参数变化时重置个体。
        _view.QueueRedraw();
        // 数量和种子在 Reset 时生效；修改运动上限在下一次运行更新时生效。
    }

    /// <summary>创建并注册一个个体；在世界就绪后、更新遍历之外调用。</summary>
    public Boid2D SpawnBoid(Vector2 position, Vector2 velocity)
    {
        if (!position.IsFinite()) throw new ArgumentOutOfRangeException(nameof(position));
        if (!velocity.IsFinite()) throw new ArgumentOutOfRangeException(nameof(velocity));
        if (_boidsRoot is null)
            throw new InvalidOperationException("Spawn Boids after the world is ready.");
        if (BoidScene is null)
            throw new InvalidOperationException("Assign a Boid scene in BoidManager.BoidScene.");

        Node instance = BoidScene.Instantiate();
        if (instance is not Boid2D boid)
        {
            instance.Free();
            throw new InvalidOperationException("The Boid scene root must use Boid2D.");
        }

        boid.Name = $"Boid_{_nextBoidId}";
        boid.Position = position;
        boid.Initialize(Config, _nextBoidId++, velocity);
        _boidsRoot.AddChild(boid);
        _boids.Add(boid);
        InvalidateNeighborResults();
        _view.QueueRedraw();
        return boid;
    }

    /// <summary>移除已注册的个体；不会更改其余个体的 ID 或运动状态。</summary>
    public void RemoveBoid(Boid2D boid)
    {
        if (!_boids.Remove(boid)) return;
        if (SelectedBoid == boid) SelectBoid(null);
        _boidsRoot.RemoveChild(boid);
        boid.QueueFree();
        InvalidateNeighborResults();
        _view.QueueRedraw();
    }

    /// <returns>是否产生了新的运动状态。</returns>
    public bool Step(double deltaSeconds)
    {
        if (!float.IsFinite((float)deltaSeconds) || (float)deltaSeconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));

        long started = Stopwatch.GetTimestamp();
        NeighborSearch.Search(_boids, WorldBounds, Config.PerceptionRadius, Config.NeighborSearchMode);
        if (_steeringAccelerations.Length != _boids.Count)
            _steeringAccelerations = new Vector2[_boids.Count];
        for (int i = 0; i < _boids.Count; i++)
            _steeringAccelerations[i] = BoidSteering.Calculate(NeighborSearch, i, Config);

        // 所有规则均已使用同一份快照计算完毕，之后才开始修改节点状态。
        bool changed = false;
        for (int i = 0; i < _boids.Count; i++)
        {
            Boid2D boid = _boids[i];
            changed |= boid.UpdateBoid(deltaSeconds, _steeringAccelerations[i]);
            changed |= WrapPosition(boid);
        }

        Stats = new SimulationStats(_boids.Count, Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            NeighborSearch.CandidateChecks, NeighborSearch.AverageNeighbors, NeighborSearch.SearchMilliseconds);
        if (changed) _view.QueueRedraw();
        return changed;
    }

    private void InvalidateNeighborResults()
    {
        NeighborSearch.Invalidate();
        Stats = new SimulationStats(_boids.Count, null, null, null);
    }

    private void InitializePopulation()
    {
        using var random = new RandomNumberGenerator { Seed = (ulong)Config.RandomSeed };
        for (int i = 0; i < Config.BoidCount; i++)
        {
            Vector2 position = new(
                random.RandfRange(WorldBounds.Position.X, WorldBounds.End.X),
                random.RandfRange(WorldBounds.Position.Y, WorldBounds.End.Y));
            Vector2 velocity = Vector2.FromAngle(random.RandfRange(-Mathf.Pi, Mathf.Pi)) * Config.MaxSpeed;
            SpawnBoid(position, velocity);
        }
    }

    public override void _ExitTree()
    {
        if (_camera is not null)
        {
            _camera.ViewChanged -= UpdateWorldBounds;
            _camera.WorldPressStarted -= OnWorldPressStarted;
            _camera.WorldPressEnded -= OnWorldPressEnded;
        }
    }
}
