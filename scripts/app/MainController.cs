using System;
using Godot;

namespace BoidsLab;

public partial class MainController : Node
{
    [Export] public SimulationWorldDefinition World2D { get; set; } = null!;
    [Export] public SimulationWorldDefinition World3D { get; set; } = null!;
    [Export] public bool StartIn3D { get; set; }

    public int CurrentWorldIndex => _activeIndex >= 0 ? _activeIndex : StartIn3D ? 1 : 0;
    public string ConfigurationPath => DefinitionAt(CurrentWorldIndex).ConfigurationPath;
    public bool IsSwitchingWorld => _switchPending;
    private readonly BoidConfig?[] _configurations = new BoidConfig?[2];
    private readonly string?[] _storageWarnings = new string?[2];
    private BoidConfig _config => _configurations[CurrentWorldIndex]!;
    private string? _storageWarning
    {
        get => _storageWarnings[CurrentWorldIndex];
        set => _storageWarnings[CurrentWorldIndex] = value;
    }
    private int _activeIndex = -1;
    private bool _switchPending;
    private Node? _worldNode;
    private OptionButton _worldSelector = null!;
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

    public override void _Ready()
    {
        GetWindow().MinSize = new Vector2I(960, 640);
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
        _helpButton.Pressed += _help.Toggle;
        _help.OpenChanged += OnHelpOpenChanged;
        _worldSelector = GetNode<OptionButton>("%WorldSelector");
        _worldSelector.AddItem(World2D.Title, 0);
        _worldSelector.AddItem(World3D.Title, 1);
        _worldSelector.ItemSelected += OnWorldSelected;
        ActivateWorld(CurrentWorldIndex);
    }

    private SimulationWorldDefinition DefinitionAt(int index) => index == 0 ? World2D : World3D;

    private BoidConfig ConfigurationAt(int index)
    {
        if (_configurations[index] is { } existing) return existing;
        var config = new BoidConfig();
        Error error = config.Load(DefinitionAt(index).ConfigurationPath);
        if (error != Error.Ok && error != Error.FileNotFound)
        {
            _storageWarnings[index] = $"配置加载失败（错误代码：{(int)error}）；当前使用默认参数。";
            GD.PushWarning(_storageWarnings[index]);
        }
        _configurations[index] = config;
        return config;
    }

    private void OnWorldSelected(long index) => RequestWorldSwitch((int)index);

    public void RequestWorldSwitch(int index)
    {
        if (index is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(index));
        if (_switchPending || index == CurrentWorldIndex) return;
        _switchPending = true;
        _worldSelector.Disabled = true;
        // 等当前输入/信号派发结束，再拆除旧世界和参数控件。
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(this) && IsInsideTree()) ActivateWorld(index);
        }).CallDeferred();
    }

    private void ActivateWorld(int index)
    {
        Node? candidate = null;
        try
        {
            SimulationWorldDefinition definition = DefinitionAt(index);
            candidate = definition.Scene.Instantiate();
            if (candidate is not ISimulationWorld next)
                throw new InvalidOperationException("世界场景根节点必须实现 ISimulationWorld。");
            next.Initialize(ConfigurationAt(index)); // 验证目标世界后，才拆除旧世界。

            if (_worldNode is not null)
            {
                _controls.CommitPendingEdits();
                SaveConfiguration();
                _focusBeforeHelp = null;
                _help.Close();
                _world.SetRunning(false);
                _world.SetInteractionEnabled(false);
                _world.CancelObstaclePlacement();
                UnbindWorld();
                GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
                _worldNode.ProcessMode = ProcessModeEnum.Disabled;
                _worldNode.GetParent().RemoveChild(_worldNode);
                _worldNode.Free();
            }

            _activeIndex = index;
            _world = next;
            _worldNode = candidate;
            candidate = null;
            GetNode<Node>("%World").AddChild(_worldNode);
            _world.SetRunning(false);
            _controls.Bind(_config, _world.LengthUnit, _world.Features, _worldNode is Node3D);
            BindWorld();
            _controls.GetParent<ScrollContainer>().ScrollVertical = 0;
            _help.SetInstructions(definition.Instructions);
            _worldSelector.Select(index);
            GetNode<Label>("%WorldTitle").Text = _world.DisplayName;
            GetNode<Label>("%WorldCoordinates").Text = $"世界坐标 / {_world.LengthUnit}";
            _statsElapsed = 0;
            RefreshInterface();
        }
        catch (Exception error)
        {
            candidate?.Free();
            _worldSelector.Select(CurrentWorldIndex);
            _debug.Text = $"世界切换失败：{error.Message}";
            GD.PushError(_debug.Text);
        }
        finally
        {
            _switchPending = false;
            _worldSelector.Disabled = false;
        }
    }

    private void BindWorld()
    {
        _controls.ToggleRunningRequested += ToggleRunning;
        _controls.ResetRequested += ResetSimulation;
        _controls.StepRequested += StepSimulation;
        _controls.ConfigurationChanged += ApplyConfiguration;
        _controls.LeaderAssignmentRequested += _world.SetSelectedBoidLeader;
        _world.BoidControlStateChanged += RefreshLeaderControls;
        if (_controls.Obstacles is { } obstacles)
        {
            obstacles.CreateRequested += _world.BeginObstaclePlacement;
            obstacles.CancelPlacementRequested += _world.CancelObstaclePlacement;
            obstacles.DeleteRequested += _world.DeleteSelectedObstacle;
            obstacles.ResizeRequested += _world.ResizeSelectedObstacle;
        }
        _world.ObstacleSelectionChanged += RefreshObstacleControls;
        RefreshLeaderControls();
        RefreshObstacleControls();
    }

    private void UnbindWorld()
    {
        _controls.ToggleRunningRequested -= ToggleRunning;
        _controls.ResetRequested -= ResetSimulation;
        _controls.StepRequested -= StepSimulation;
        _controls.ConfigurationChanged -= ApplyConfiguration;
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
    }

    public override void _Process(double delta)
    {
        if (_worldNode is null || _switchPending) return;
        _statsElapsed += delta;
        if (_statsElapsed < 0.25) return;
        _statsElapsed = 0;
        _stats.Refresh(_world.Stats, Engine.GetFramesPerSecond());
    }

    public override void _Input(InputEvent @event)
    {
        if (_switchPending || _worldNode is null)
        {
            GetViewport().SetInputAsHandled();
            return;
        }
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
        if (_worldNode is not null) _world.SetInteractionEnabled(!open);
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
        _world.ConfigurationChanged();
        SaveConfiguration();
        RefreshInterface();
    }

    private void SaveConfiguration()
    {
        Error error = _config.Save(ConfigurationPath);
        _storageWarning = error == Error.Ok ? null : $"配置保存失败（错误代码：{(int)error}）；本次运行仍保留参数。";
        if (_storageWarning is not null) GD.PushWarning(_storageWarning);
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
        if (_worldSelector is not null) _worldSelector.ItemSelected -= OnWorldSelected;
        if (_hideUiButton is not null) _hideUiButton.Pressed -= ToggleUi;
        if (_help is not null)
        {
            _help.OpenChanged -= OnHelpOpenChanged;
            if (_helpButton is not null) _helpButton.Pressed -= _help.Toggle;
        }
        if (_worldNode is not null) UnbindWorld();
    }
}
