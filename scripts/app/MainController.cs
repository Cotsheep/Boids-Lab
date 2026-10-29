using System;
using Godot;

namespace BoidsLab;

public partial class MainController : Node
{
    // 2D / 3D 入口复用同一套 HUD，通过世界接口控制不同的模拟场景。
    [Export] public PackedScene SimulationScene { get; set; } = null!;
    [Export] public string ConfigurationPath { get; set; } = BoidConfig.SavePath;
    [Export(PropertyHint.MultilineText)] public string HelpTextOverride { get; set; } = "";

    private readonly BoidConfig _config = new();
    private ISimulationWorld _world = null!;
    private ControlPanel _controls = null!;
    private StatsPanel _stats = null!;
    private Label _status = null!;
    private Label _debug = null!;
    private Control _placeholder = null!;
    private Control _uiRoot = null!;
    private Button _hideUiButton = null!;
    private Button _helpButton = null!;
    private HelpOverlay _help = null!;
    private Control? _focusBeforeHelp;
    private double _statsElapsed;
    private string? _storageWarning;

    public override void _Ready()
    {
        GetWindow().MinSize = new Vector2I(960, 640);
        Error loadError = _config.Load(ConfigurationPath);
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
        _helpButton = GetNode<Button>("%HelpButton");
        _help = GetNode<HelpOverlay>("%HelpOverlay");
        if (!string.IsNullOrWhiteSpace(HelpTextOverride)) _help.SetInstructions(HelpTextOverride);
        _helpButton.Pressed += _help.Toggle;

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

        _controls.Bind(_config, _world.LengthUnit, _world.Features);
        _controls.ToggleRunningRequested += ToggleRunning;
        _controls.ResetRequested += ResetSimulation;
        _controls.StepRequested += StepSimulation;
        _controls.ConfigurationChanged += ApplyConfiguration;
        _controls.LeaderAssignmentRequested += _world.SetSelectedBoidLeader;
        _world.BoidControlStateChanged += RefreshLeaderControls;
        RefreshLeaderControls();
        if (_controls.Obstacles is { } obstacles)
        {
            obstacles.CreateRequested += _world.BeginObstaclePlacement;
            obstacles.CancelPlacementRequested += _world.CancelObstaclePlacement;
            obstacles.DeleteRequested += _world.DeleteSelectedObstacle;
            obstacles.ResizeRequested += _world.ResizeSelectedObstacle;
        }
        _world.ObstacleSelectionChanged += RefreshObstacleControls;
        RefreshObstacleControls();
        _help.OpenChanged += OnHelpOpenChanged;
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
        // 空格优先于 GUI：按下切换一次，重复事件与松开也消费，防止触发 ui_accept。
        if (@event.IsAction("toggle_pause"))
        {
            if (@event.IsActionPressed("toggle_pause")) ToggleRunning();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event.IsAction("toggle_help"))
        {
            if (@event.IsActionPressed("toggle_help")) _help.Toggle();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event.IsAction("close_help"))
        {
            if (@event.IsActionPressed("close_help"))
            {
                if (_help.Visible) _help.Close();
                else _world.CancelObstaclePlacement();
            }
            GetViewport().SetInputAsHandled();
            return;
        }
        // 全局快捷键：即使 HUD 隐藏或输入框有焦点，也能恢复界面。
        if (!@event.IsActionPressed("toggle_ui")) return;
        ToggleUi();
        GetViewport().SetInputAsHandled();
    }

    private void OnHelpOpenChanged(bool open)
    {
        if (open)
        {
            _focusBeforeHelp = GetViewport().GuiGetFocusOwner();
            _focusBeforeHelp?.ReleaseFocus();
        }
        // 屏蔽鼠标由弹层负责；同时隔离后台键盘焦点与世界输入，不暂停模拟。
        _uiRoot.FocusBehaviorRecursive = open
            ? Control.FocusBehaviorRecursiveEnum.Disabled
            : Control.FocusBehaviorRecursiveEnum.Inherited;
        _world.SetInteractionEnabled(!open);
        if (!open)
        {
            if (GodotObject.IsInstanceValid(_focusBeforeHelp) && _focusBeforeHelp!.IsVisibleInTree())
                _focusBeforeHelp.GrabFocus();
            _focusBeforeHelp = null;
        }
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

    private void RefreshObstacleControls() =>
        _controls.Obstacles?.Refresh(_world.SelectedObstacleInfo, _world.ObstaclePlacement);

    private void RefreshLeaderControls() =>
        _controls.RefreshLeaderState(_world.SelectedBoidId, _world.LeaderId, _world.HasLeaderTarget);

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
        Error error = _config.Save(ConfigurationPath);
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
        _debug.Text = _storageWarning ?? $"目标数量：{_config.BoidCount}  |  随机种子：{_config.RandomSeed}  |  场景：{_world.DisplayName}";
        _stats.Refresh(_world.Stats, Engine.GetFramesPerSecond());
    }

    public override void _ExitTree()
    {
        if (_hideUiButton is not null) _hideUiButton.Pressed -= ToggleUi;
        if (_help is not null)
        {
            _help.OpenChanged -= OnHelpOpenChanged;
            if (_helpButton is not null) _helpButton.Pressed -= _help.Toggle;
        }
        if (_controls is null) return;
        _controls.LeaderAssignmentRequested -= _world.SetSelectedBoidLeader;
        _world.BoidControlStateChanged -= RefreshLeaderControls;
        if (_controls.Obstacles is { } obstacles)
        {
            obstacles.CreateRequested -= _world.BeginObstaclePlacement;
            obstacles.CancelPlacementRequested -= _world.CancelObstaclePlacement;
            obstacles.DeleteRequested -= _world.DeleteSelectedObstacle;
            obstacles.ResizeRequested -= _world.ResizeSelectedObstacle;
        }
        _world.ObstacleSelectionChanged -= RefreshObstacleControls;
        _controls.ToggleRunningRequested -= ToggleRunning;
        _controls.ResetRequested -= ResetSimulation;
        _controls.StepRequested -= StepSimulation;
        _controls.ConfigurationChanged -= ApplyConfiguration;
    }
}
