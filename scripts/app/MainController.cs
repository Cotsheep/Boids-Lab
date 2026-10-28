using System;
using Godot;

namespace BoidsLab;

public partial class MainController : Node
{
    // 同一套 UI 的模拟场景，未来可指定实现 ISimulationWorld 的 Node3D 场景。
    [Export] public PackedScene SimulationScene { get; set; } = null!;

    private readonly BoidConfig _config = new();
    private ISimulationWorld _world = null!;
    private ControlPanel _controls = null!;
    private StatsPanel _stats = null!;
    private Label _status = null!;
    private Label _debug = null!;
    private Control _placeholder = null!;
    private Control _uiRoot = null!;
    private Button _hideUiButton = null!;
    private double _statsElapsed;
    private string? _storageWarning;

    public override void _Ready()
    {
        GetWindow().MinSize = new Vector2I(960, 640);
        Error loadError = _config.Load();
        if (loadError != Error.Ok && loadError != Error.FileNotFound)
        {
            _storageWarning = $"配置加载失败（错误代码：{(int)loadError}）";
            GD.PushWarning(_storageWarning);
        }
        _controls = GetNode<ControlPanel>("%ControlPanel");
        _stats = GetNode<StatsPanel>("%StatsPanel");
        _status = GetNode<Label>("%RunStatus");
        _debug = GetNode<Label>("%DebugInfo");
        _placeholder = GetNode<Control>("%CorePlaceholder");
        _uiRoot = GetNode<Control>("%UIRoot");
        _hideUiButton = GetNode<Button>("%HideUiButton");
        _hideUiButton.Pressed += ToggleUi;

        if (SimulationScene is null)
            throw new InvalidOperationException("Assign a simulation scene in MainController.SimulationScene.");
        Node worldNode = SimulationScene.Instantiate();
        if (worldNode is not ISimulationWorld world)
        {
            worldNode.Free();
            throw new InvalidOperationException("The simulation scene root must implement ISimulationWorld.");
        }
        _world = world;
        _world.Initialize(_config);
        GetNode<Node>("%World").AddChild(worldNode);
        GetNode<Label>("%WorldTitle").Text = _world.DisplayName;
        GetNode<Label>("%WorldCoordinates").Text = $"世界坐标 / {_world.LengthUnit}";

        _controls.Bind(_config, _world.LengthUnit);
        _controls.ToggleRunningRequested += ToggleRunning;
        _controls.ResetRequested += ResetSimulation;
        _controls.StepRequested += StepSimulation;
        _controls.ConfigurationChanged += ApplyConfiguration;
        RefreshInterface();
    }

    public override void _Process(double delta)
    {
        _statsElapsed += delta;
        if (_statsElapsed < 0.25) return;
        _statsElapsed = 0;
        _stats.Refresh(_world.Stats, Engine.GetFramesPerSecond());
    }

    public override void _Input(InputEvent @event)
    {
        // 全局快捷键：即使 HUD 隐藏或输入框有焦点，也能恢复界面。
        if (!@event.IsActionPressed("toggle_ui")) return;
        ToggleUi();
        GetViewport().SetInputAsHandled();
    }

    private void ToggleUi()
    {
        if (_uiRoot.Visible)
        {
            Control? focused = GetViewport().GuiGetFocusOwner();
            if (focused is not null && _uiRoot.IsAncestorOf(focused))
                focused.ReleaseFocus();
        }
        _uiRoot.Visible = !_uiRoot.Visible;
    }

    private void ToggleRunning()
    {
        _world.SetRunning(!_world.IsRunning);
        RefreshInterface();
    }

    private void ResetSimulation()
    {
        _world.Reset();
        RefreshInterface();
    }

    private void StepSimulation()
    {
        if (!_world.CanStep) return;
        _world.StepOnce();
        RefreshInterface();
    }

    private void ApplyConfiguration()
    {
        _config.Normalize();
        Error error = _config.Save();
        _storageWarning = error == Error.Ok ? null : $"配置保存失败（错误代码：{(int)error}）";
        if (_storageWarning is not null) GD.PushWarning(_storageWarning);
        _world.ConfigurationChanged();
        RefreshInterface();
    }

    private void RefreshInterface()
    {
        _controls.RefreshRunState(_world.IsRunning, _world.CanStep);
        string runState = _world.IsRunning
            ? (_world.CoreImplemented ? "运行中" : "已请求运行")
            : "已暂停";
        _status.Text = _world.CoreImplemented ? runState : $"{runState} · 核心尚未实现";
        _placeholder.Visible = !_world.CoreImplemented;
        _debug.Text = _storageWarning ?? $"目标数量：{_config.BoidCount}  |  随机种子：{_config.RandomSeed}  |  场景：{_world.DisplayName}  |  -- 表示尚未测量";
        _stats.Refresh(_world.Stats, Engine.GetFramesPerSecond());
    }

    public override void _ExitTree()
    {
        if (_hideUiButton is not null) _hideUiButton.Pressed -= ToggleUi;
        if (_controls is null) return;
        _controls.ToggleRunningRequested -= ToggleRunning;
        _controls.ResetRequested -= ResetSimulation;
        _controls.StepRequested -= StepSimulation;
        _controls.ConfigurationChanged -= ApplyConfiguration;
    }
}
