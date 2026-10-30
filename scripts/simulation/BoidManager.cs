using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace BoidsLab;

/// <summary>2D 世界管理器：统一调度个体更新、管理实例和相机，不引用 UI。</summary>
public partial class BoidManager : Node2D, ISimulationWorld
{
    // 初始化及 Home 复位的参考尺寸；无领队时，ViewBounds 也作为环绕边界。
    [Export] public Vector2 WorldSize { get; set; } = new(1280, 720);
    [Export] public PackedScene BoidScene { get; set; } = null!;

    public string DisplayName => "二维世界";
    public string LengthUnit => "像素";
    public SimulationFeatures Features => SimulationFeatures.All & ~SimulationFeatures.DirectLeaderControl;
    public BoidConfig Config => _config
        ?? throw new InvalidOperationException("Initialize the world with a shared configuration first.");
    public Rect2 ViewBounds { get; private set; }
    public Rect2? WrapBounds => Leader is null ? ViewBounds : null;
    public IReadOnlyList<Boid2D> Boids { get; }
    public NeighborSearch2D NeighborSearch { get; } = new();
    public Boid2D? SelectedBoid { get; private set; }
    public Boid2D? Leader { get; private set; }
    public Vector2? LeaderTarget { get; private set; }
    public int? SelectedBoidId => SelectedBoid?.Id;
    public int? LeaderId => Leader?.Id;
    public bool HasLeaderTarget => LeaderTarget.HasValue;
    public bool IsLeaderControlActive => false;
    public void SetLeaderControlActive(bool active) { }
    public event Action? ConfigurationEdited { add { } remove { } }
    public event Action? BoidControlStateChanged;
    public SimulationStats Stats { get; private set; }
    public bool IsRunning { get; private set; }
    // 基础运动、邻居搜索与分离/对齐/聚集规则均已可运行。
    public bool CoreImplemented => true;
    public bool CanStep => !IsRunning;
    public bool PopulationSpawnLimited { get; private set; }
    public ObstacleSelection? SelectedObstacleInfo => _obstacleEditor.SelectionInfo;
    public ObstacleShape? ObstaclePlacement => _obstacleEditor.PlacementShape;
    public event Action? ObstacleSelectionChanged;

    private readonly List<Boid2D> _boids = new();
    private BoidConfig? _config;
    private Node2D _boidsRoot = null!;
    private SimulationView _view = null!;
    private SimulationCamera2D _camera = null!;
    private ObstacleEditor2D _obstacleEditor = null!;
    private int _nextBoidId;
    private readonly RandomNumberGenerator _populationRandom = new();
    private Vector2[] _steeringAccelerations = Array.Empty<Vector2>();
    private Boid2D? _pressedBoid;
    private Vector2 _leaderHeading = Vector2.Right;
    private bool _interactionEnabled = true;

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
        ViewBounds = new Rect2(Vector2.Zero, WorldSize);
        if (IsNodeReady())
        {
            _view.Bind(Config, this);
            _camera.Configure(new Rect2(Vector2.Zero, WorldSize));
            Reset();
        }
    }

    public override void _Ready()
    {
        GetWindow().FocusEntered += OnFocusEntered;
        GetWindow().FocusExited += OnFocusExited;
        _boidsRoot = GetNode<Node2D>("Boids");
        _view = GetNode<SimulationView>("SimulationView");
        _camera = GetNode<SimulationCamera2D>("Camera2D");
        _obstacleEditor = GetNode<ObstacleEditor2D>("ObstacleEditor");
        _obstacleEditor.Initialize(this, _camera, GetNode<Node2D>("Obstacles"));
        _obstacleEditor.Changed += OnObstaclesChanged;
        _obstacleEditor.GeometryChanged += InvalidateNeighborResults;
        _view.Bind(Config, this);
        _camera.ViewChanged += UpdateViewBounds;
        _camera.WorldPressStarted += OnWorldPressStarted;
        _camera.WorldPressEnded += OnWorldPressEnded;
        _camera.Configure(new Rect2(Vector2.Zero, WorldSize));
        Reset();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsRunning) Step(delta);
    }

    // 暂停时也更新目标标记；运动仍只由物理步推进。
    public override void _Process(double delta) => UpdateCursorTarget();

    private void UpdateCursorTarget()
    {
        if (!_interactionEnabled || Leader is null || _obstacleEditor.IsEditing) return;
        Viewport viewport = GetViewport();
        if (_camera.CursorViewportPosition is not { } pointer) return;
        if (!_windowFocused || !viewport.GetVisibleRect().HasPoint(pointer)
            || viewport.GuiGetHoveredControl() is not null) return;
        SetLeaderTarget(GetGlobalTransformWithCanvas().AffineInverse() * pointer);
    }

    private bool _windowFocused = true;
    private void OnFocusEntered() => _windowFocused = true;
    private void OnFocusExited() => _windowFocused = false;

    private void UpdateViewBounds()
    {
        Rect2 viewportRect = GetViewport().GetVisibleRect();
        if (!viewportRect.HasArea()) return;

        // 无领队时，视口变化同时改变环绕世界；有领队时只改变观察范围。
        Rect2 bounds = GetGlobalTransformWithCanvas().AffineInverse() * viewportRect;
        if (!bounds.HasArea() || !bounds.Position.IsFinite() || !bounds.Size.IsFinite()) return;
        if (bounds == ViewBounds) return;

        ViewBounds = bounds;
        if (WrapBounds is not null)
        {
            WrapPopulation();
            InvalidateNeighborResults();
        }
        _view.QueueRedraw();
        _obstacleEditor.RefreshVisuals();
    }

    public void SetRunning(bool running) => IsRunning = running;

    public void SetInteractionEnabled(bool enabled)
    {
        _interactionEnabled = enabled;
        _camera.SetInteractionEnabled(enabled);
        _obstacleEditor.SetInteractionEnabled(enabled);
    }

    public void BeginObstaclePlacement(ObstacleShape shape) => _obstacleEditor.BeginPlacement(shape);
    public void CancelObstaclePlacement() => _obstacleEditor.CancelPlacement();
    public void DeleteSelectedObstacle() => _obstacleEditor.DeleteSelected();
    public void ResizeSelectedObstacle(float width, float height, float depth = 0) => _obstacleEditor.ResizeSelected(width, height);
    internal void ClearBoidSelection() => SelectBoid(null);

    public void SetSelectedBoidLeader(bool enabled)
    {
        if (!_interactionEnabled || SelectedBoid is null) return;
        if (enabled) SetLeader(SelectedBoid);
        else if (Leader == SelectedBoid) SetLeader(null);
    }

    /// <summary>更换唯一领队；身份不随普通选择变化，目标在更换时清空。</summary>
    public void SetLeader(Boid2D? boid)
    {
        if (boid is not null && !_boids.Contains(boid))
            throw new ArgumentException("Leader must belong to this world.", nameof(boid));
        if (Leader == boid) return;
        bool wasWrapping = Leader is null;
        Leader?.SetLeader(false);
        Leader = boid;
        Leader?.SetLeader(true);
        LeaderTarget = null;
        _leaderHeading = boid is null ? Vector2.Right : Vector2.FromAngle(boid.Rotation);
        if (wasWrapping != (Leader is null))
        {
            WrapPopulation();
            InvalidateNeighborResults();
        }
        BoidControlStateChanged?.Invoke();
        _view.QueueRedraw();
    }

    public void SetLeaderTarget(Vector2 position)
    {
        if (!position.IsFinite()) throw new ArgumentOutOfRangeException(nameof(position));
        if (!_interactionEnabled || Leader is null) return;
        if (LeaderTarget == position) return;
        bool firstTarget = !LeaderTarget.HasValue;
        LeaderTarget = position;
        if (firstTarget) BoidControlStateChanged?.Invoke();
        _view.QueueRedraw();
    }

    private void OnObstaclesChanged()
    {
        ObstacleSelectionChanged?.Invoke();
    }

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
        _obstacleEditor.ClearSelection();
        SelectBoid(target == SelectedBoid ? null : target);
    }

    private void SelectBoid(Boid2D? boid)
    {
        if (SelectedBoid == boid) return;
        SelectedBoid?.SetSelected(false);
        SelectedBoid = boid;
        SelectedBoid?.SetSelected(true);
        BoidControlStateChanged?.Invoke();
        _view.QueueRedraw();
    }

    public void Reset()
    {
        Config.Normalize();
        PopulationSpawnLimited = false;
        _populationRandom.Seed = (ulong)Config.RandomSeed ^ 0x9e3779b97f4a7c15UL;
        IsRunning = false;
        _obstacleEditor.CancelGesture();
        _obstacleEditor.CancelPlacement();
        SetLeader(null);
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
        // 数量由独立提交入口调整；种子在 Reset 时生效。
    }

    public void ResizePopulation(int requestedCount)
    {
        Config.BoidCount = (int)BoidConfig.CountRange.Constrain(requestedCount);
        PopulationSpawnLimited = false;
        if (_boids.Count == Config.BoidCount) return;
        // 稳定地从列表末尾缩减，先保留领队，再保留普通选择。
        for (int i = _boids.Count - 1; i >= 0 && _boids.Count > Config.BoidCount; i--)
            if (_boids[i] != Leader && _boids[i] != SelectedBoid) RemoveBoid(_boids[i]);
        if (_boids.Count > Config.BoidCount && SelectedBoid is { } selected && selected != Leader) RemoveBoid(selected);
        if (_boids.Count > Config.BoidCount && Leader is { } leader) RemoveBoid(leader);

        Rect2 region = ViewBounds;
        if (Leader is not null)
        {
            Vector2 center = Vector2.Zero;
            foreach (Boid2D boid in _boids) center += boid.Position / _boids.Count;
            float radius = Mathf.Max(Config.PerceptionRadius, Config.SeparationRadius * Mathf.Sqrt(Config.BoidCount));
            region = new Rect2(center - Vector2.One * radius, Vector2.One * radius * 2);
        }
        while (_boids.Count < Config.BoidCount)
        {
            bool placed = false;
            for (int attempt = 0; attempt < 64; attempt++)
            {
                Vector2 position = region.Position + new Vector2(_populationRandom.Randf(), _populationRandom.Randf()) * region.Size;
                position = WorldGeometry2D.Wrap(position, WrapBounds);
                bool clear = true;
                foreach (Obstacle2D obstacle in _obstacleEditor.Obstacles)
                {
                    ObstacleAvoidance.Surface(position, obstacle, Config.ObstacleClearance, out float distance, out _);
                    if (distance <= 0) { clear = false; break; }
                }
                if (!clear) continue;
                Vector2 velocity = Vector2.FromAngle(_populationRandom.RandfRange(-Mathf.Pi, Mathf.Pi)) * Config.MaxSpeed;
                SpawnBoid(position, velocity);
                placed = true;
                break;
            }
            if (!placed) { PopulationSpawnLimited = true; break; }
        }
        InvalidateNeighborResults();
        _view.QueueRedraw();
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
        boid.Position = WorldGeometry2D.Wrap(position, WrapBounds);
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
        if (_pressedBoid == boid)
        {
            _pressedBoid = null;
            _camera.CancelPointerGesture();
        }
        if (Leader == boid) SetLeader(null);
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
        bool changed = WrapPopulation();
        Rect2? wrapBounds = WrapBounds;
        NeighborSearch.Search(_boids, Config.PerceptionRadius, Config.NeighborSearchMode, wrapBounds);
        if (_steeringAccelerations.Length != _boids.Count)
            _steeringAccelerations = new Vector2[_boids.Count];
        // 一次物理步内统一使用领队的步前状态，避免列表顺序改变跟随结果。
        int leaderIndex = Leader is null ? -1 : _boids.IndexOf(Leader);
        BoidSnapshot leaderSnapshot = leaderIndex < 0 ? default : NeighborSearch.Snapshots[leaderIndex];
        if (leaderIndex >= 0 && leaderSnapshot.Velocity.LengthSquared() > 1f)
            _leaderHeading = leaderSnapshot.Velocity.Normalized();
        float followRadius = LeaderSteering.FollowRadius(_boids.Count - 1, Config);
        Vector2 followPoint = leaderIndex < 0 ? default : LeaderSteering.FollowPoint(
            leaderSnapshot.Position, _leaderHeading, Config.LeaderFollowDistance + followRadius);
        for (int i = 0; i < _boids.Count; i++)
        {
            BoidSnapshot self = NeighborSearch.Snapshots[i];
            Vector2 avoidance = ObstacleAvoidance.Calculate(self,
                _obstacleEditor.Obstacles, Config, wrapBounds);
            // 优先给避障留出完整的转向能力，避免群集规则抵消避障方向。
            if (avoidance != Vector2.Zero) _steeringAccelerations[i] = avoidance;
            else if (i == leaderIndex)
            {
                // 鼠标尚未进入世界时平滑刹停；领队不被跟随者的群集规则拉回。
                _steeringAccelerations[i] = LeaderSteering.Arrive(self, LeaderTarget ?? self.Position,
                    Vector2.Zero, Config, (float)deltaSeconds);
            }
            else
            {
                bool following = leaderIndex >= 0 && Config.LeaderFollowWeight > 0;
                Vector2 guidance = Vector2.Zero;
                if (following)
                    guidance = LeaderSteering.Follow(self, followPoint, leaderSnapshot.Velocity,
                        Config, (float)deltaSeconds, followRadius);
                _steeringAccelerations[i] = BoidSteering.Calculate(NeighborSearch, i, Config, guidance, following);
            }
        }

        // 所有规则均已使用同一份快照计算完毕，之后才开始修改节点状态。
        for (int i = 0; i < _boids.Count; i++)
        {
            Boid2D boid = _boids[i];
            Vector2 before = boid.Position;
            changed |= boid.UpdateBoid(deltaSeconds, _steeringAccelerations[i]);
            if (Config.AvoidanceEnabled && _obstacleEditor.Obstacles.Count > 0)
            {
                Vector2 position = boid.Position;
                Vector2 velocity = boid.Velocity;
                ObstacleAvoidance.ResolveMotion(before, ref position, ref velocity,
                    _obstacleEditor.Obstacles, Config.ObstacleClearance, wrapBounds);
                changed |= boid.ApplyObstacleContact(position, velocity);
            }
            else
            {
                Vector2 position = WorldGeometry2D.Wrap(boid.Position, wrapBounds);
                changed |= position != boid.Position;
                boid.Position = position;
            }
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

    private bool WrapPopulation()
    {
        if (WrapBounds is not { } bounds) return false;
        bool changed = false;
        foreach (Boid2D boid in _boids)
        {
            Vector2 position = WorldGeometry2D.Wrap(boid.Position, bounds);
            changed |= position != boid.Position;
            boid.Position = position;
        }
        return changed;
    }

    private void InitializePopulation()
    {
        using var random = new RandomNumberGenerator { Seed = (ulong)Config.RandomSeed };
        for (int i = 0; i < Config.BoidCount; i++)
        {
            Vector2 position = new(
                random.RandfRange(ViewBounds.Position.X, ViewBounds.End.X),
                random.RandfRange(ViewBounds.Position.Y, ViewBounds.End.Y));
            Vector2 velocity = Vector2.FromAngle(random.RandfRange(-Mathf.Pi, Mathf.Pi)) * Config.MaxSpeed;
            SpawnBoid(position, velocity);
        }
    }

    public override void _ExitTree()
    {
        _populationRandom.Dispose();
        GetWindow().FocusEntered -= OnFocusEntered;
        GetWindow().FocusExited -= OnFocusExited;
        if (_obstacleEditor is not null)
        {
            _obstacleEditor.Changed -= OnObstaclesChanged;
            _obstacleEditor.GeometryChanged -= InvalidateNeighborResults;
        }
        if (_camera is not null)
        {
            _camera.ViewChanged -= UpdateViewBounds;
            _camera.WorldPressStarted -= OnWorldPressStarted;
            _camera.WorldPressEnded -= OnWorldPressEnded;
        }
    }
}
