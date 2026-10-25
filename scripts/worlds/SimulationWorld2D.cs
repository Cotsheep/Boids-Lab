using System;
using Godot;

namespace BoidsLab;

/// <summary>普通 2D 世界场景：拥有核心、Camera2D 和绘制节点，不引用 UI。</summary>
public partial class SimulationWorld2D : Node2D, ISimulationWorld
{
    // 世界大小独立于 HUD。相机适配整个主视口，不随 UI 面板布局改变模拟边界。
    [Export] public Vector2 WorldSize { get; set; } = new(1280, 720);

    public string DisplayName => "2D World";
    public string LengthUnit => "px";
    public bool IsRunning => _simulation?.IsRunning ?? false;
    public bool CoreImplemented => _simulation?.CoreImplemented ?? false;
    public bool CanStep => _simulation?.CanStep ?? false;
    public SimulationStats Stats => _simulation?.Stats ?? default;

    private BoidSimulation? _simulation;
    private SimulationView _view = null!;
    private Camera2D _camera = null!;
    private Viewport _viewport = null!;

    private BoidSimulation Simulation => _simulation
        ?? throw new InvalidOperationException("Initialize the world with a shared configuration first.");

    public void Initialize(BoidConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!float.IsFinite(WorldSize.X) || !float.IsFinite(WorldSize.Y)
            || WorldSize.X <= 0 || WorldSize.Y <= 0)
            throw new InvalidOperationException("WorldSize must contain positive finite dimensions.");

        _simulation = new BoidSimulation(config, new Rect2(Vector2.Zero, WorldSize));
        if (IsNodeReady()) BindView();
    }

    public override void _Ready()
    {
        _view = GetNode<SimulationView>("SimulationView");
        _camera = GetNode<Camera2D>("Camera2D");
        _viewport = GetViewport();
        _viewport.SizeChanged += FitCamera;
        FitCamera();
        if (_simulation is not null) BindView();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_simulation is { IsRunning: true } && _simulation.Step(delta))
            _view.QueueRedraw();
    }

    public void SetRunning(bool running)
    {
        if (running) Simulation.Start();
        else Simulation.Pause();
    }

    public void Reset()
    {
        Simulation.Reset();
        _view.QueueRedraw();
    }

    public void StepOnce()
    {
        if (!CanStep) return;
        Simulation.Step(1.0 / Engine.PhysicsTicksPerSecond);
        _view.QueueRedraw();
    }

    public void ConfigurationChanged()
    {
        // 核心与 UI 共享同一个配置实例；无需复制参数。
        _view.QueueRedraw();
        // TODO: 初始化算法实现后，明确数量/种子修改是否需要重建状态。
    }

    private void BindView()
    {
        _view.Bind(Simulation.Config, Simulation);
        FitCamera();
    }

    private void FitCamera()
    {
        Vector2 viewportSize = _viewport.GetVisibleRect().Size;
        if (viewportSize.X <= 0 || viewportSize.Y <= 0) return;
        float zoom = Mathf.Min(viewportSize.X / WorldSize.X, viewportSize.Y / WorldSize.Y);
        _camera.Position = WorldSize * 0.5f;
        _camera.Zoom = Vector2.One * zoom;
    }

    public override void _ExitTree()
    {
        if (_viewport is not null) _viewport.SizeChanged -= FitCamera;
    }
}
