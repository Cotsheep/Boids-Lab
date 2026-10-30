using System;
using Godot;

namespace BoidsLab;

public partial class ControlPanel : VBoxContainer
{
    public event Action? ToggleRunningRequested;
    public event Action? ResetRequested;
    public event Action? StepRequested;
    public event Action? ConfigurationChanged;
    public event Action<int>? PopulationCountRequested;
    public event Action<bool>? LeaderAssignmentRequested;
    public event Action? LeaderControlRequested;
    private Button? _leaderControlButton;

    public ObstaclePanel? Obstacles { get; private set; }

    private Button _startPause = null!;
    private Button _step = null!;
    private CheckBox? _leaderToggle;
    private Label? _leaderStatus;
    private Label? _selectionStatus;
    private Label? _separationRadiusWarning;
    private SpinBox _populationInput = null!;
    private Label _populationStatus = null!;
    private int _lastPopulationSubmission;
    private bool _threeDimensional;

    public void Bind(BoidConfig config, string lengthUnit, SimulationFeatures features = SimulationFeatures.All, bool threeDimensional = false)
    {
        // 切换世界时重新构建控件；主控制器先解除旧世界的事件绑定。
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _threeDimensional = threeDimensional;
        Obstacles = null;
        _leaderControlButton = null;
        _leaderToggle = null;
        _leaderStatus = null;
        _selectionStatus = null;
        _separationRadiusWarning = null;
        AddHeading("模拟控制");
        var buttons = new HBoxContainer();
        AddChild(buttons);
        _startPause = AddButton(buttons, "StartPause", "开始", () => ToggleRunningRequested?.Invoke());
        _startPause.TooltipText = "空格：暂停 / 继续；无论焦点在哪个控件上都有效，长按不会重复切换。";
        AddButton(buttons, "Reset", "重置", () => ResetRequested?.Invoke())
            .TooltipText = "暂停并按当前数量、种子和最大速度重新生成个体；清空加速度与旋转状态。";
        _step = AddButton(buttons, "Step", "单步", () => StepRequested?.Invoke());
        _step.Disabled = true;
        _step.TooltipText = "暂停时推进一个固定物理时间步，完成后仍保持暂停，并刷新调试与统计。"
            + (threeDimensional ? "操控领队时请先按 Esc 释放鼠标。" : "");

        if (features.HasFlag(SimulationFeatures.Leader))
        {
            AddHeading("领队");
            _leaderToggle = new CheckBox
            {
                Name = "IsLeader", Text = "设为领队", Disabled = true,
                TooltipText = threeDimensional ? "先选中个体再勾选，自动操控该领队；Esc 释放鼠标后可右键调整视角。领队模式无边界，取消身份后恢复软边界，保留所有位置。" : "先选中一个个体再勾选；勾选其他个体会转移唯一领队，取消普通选择不会清除领队。有领队时地图无限，解除身份后恢复视口环绕。",
            };
            _leaderToggle.Toggled += enabled => LeaderAssignmentRequested?.Invoke(enabled);
            AddChild(_leaderToggle);
            _leaderStatus = new Label { Name = "LeaderStatus", Text = "未指定领队", AutowrapMode = TextServer.AutowrapMode.WordSmart };
            _leaderStatus.AddThemeColorOverride("font_color", new Color("d69cff"));
            _leaderStatus.AddThemeFontSizeOverride("font_size", 13);
            AddChild(_leaderStatus);
            if (threeDimensional && features.HasFlag(SimulationFeatures.DirectLeaderControl))
            {
                _leaderControlButton = new Button { Name = "ControlLeader", Text = "操控领队（F3）", Disabled = true,
                    TooltipText = "捕获鼠标：鼠标调整视角，WASD 按相机视角移动；领队朝向跟随实际运动方向。松键刹停，Esc 释放鼠标；空格暂停时仍可调整视角。" };
                _leaderControlButton.Pressed += () => LeaderControlRequested?.Invoke();
                AddChild(_leaderControlButton);
                AddNumber("FollowCameraDistance", "跟随视距", BoidConfig.FollowCameraDistanceRange, config.FollowCameraDistance,
                    value => config.FollowCameraDistance = (float)value, "领队模式滚轮也可调整，Home 恢复默认距离。领队只接受障碍接触阻挡，不主动绕障。");
                AddToggle("FadeNearbyBoids", "淡化近处个体", config.FadeNearbyBoids, value => config.FadeNearbyBoids = value);
                GetNode<CheckBox>("FadeNearbyBoids").TooltipText = "仅在领队跟随视角中淡化贴近相机的普通个体，减轻遮挡；领队和选中个体保留高亮。只影响显示，不改变运动或点击选择。";
            }
        }

        if (features.HasFlag(SimulationFeatures.Obstacles))
        {
            AddHeading("障碍物");
            Obstacles = new ObstaclePanel { Name = "ObstaclePanel", LengthUnit = lengthUnit, ThreeDimensional = threeDimensional };
            AddChild(Obstacles);
            AddToggle("AvoidanceEnabled", "启用避障", config.AvoidanceEnabled, value => config.AvoidanceEnabled = value);
            AddNumber("AvoidanceLookAhead", "前瞻时间", BoidConfig.AvoidanceLookAheadRange, config.AvoidanceLookAhead,
                value => config.AvoidanceLookAhead = (float)value, "单位：秒；沿当前运动方向提前观察障碍物。数值越大，越早开始避让。");
            AddNumber("ObstacleClearance", "避障间距", BoidConfig.ObstacleClearanceRange, config.ObstacleClearance,
                value => config.ObstacleClearance = (float)value, $"单位：{lengthUnit}；个体中心与障碍物之间预留的距离。");
        }

        if (threeDimensional)
        {
            AddHeading("三维观察范围");
            AddNumber("WorldWidth", "宽度 X", BoidConfig.WorldWidthRange, config.WorldWidth,
                value => config.WorldWidth = (float)value, "改变软边界范围，保留个体位置；Home 按新范围取景。");
            AddNumber("WorldHeight", "高度 Y", BoidConfig.WorldHeightRange, config.WorldHeight,
                value => config.WorldHeight = (float)value);
            AddNumber("WorldDepth", "深度 Z", BoidConfig.WorldDepthRange, config.WorldDepth,
                value => config.WorldDepth = (float)value);
        }

        AddHeading("群体设置");
        AddPopulationCount(config.BoidCount);
        AddNumber("RandomSeed", "随机种子", BoidConfig.SeedRange, config.RandomSeed,
            value => config.RandomSeed = (int)value, "点击重置后生效；相同种子和参数可复现初始位置与速度。");

        if (features.HasFlag(SimulationFeatures.NeighborSearch))
        {
            AddHeading("邻居搜索");
            AddSearchMode(config, features.HasFlag(SimulationFeatures.UniformGridSearch));
            AddNumber("PerceptionRadius", "感知半径", BoidConfig.RadiusRange, config.PerceptionRadius,
                value => { config.PerceptionRadius = (float)value; RefreshRadiusWarning(config); }, threeDimensional ? "三维始终使用实际坐标和直接距离，包含半径边缘及重合的其他个体。" : $"单位：{lengthUnit}；环绕世界使用最短环绕距离，无限世界使用直接距离。包含半径边缘和重合的其他个体，不包含自身。");
        }

        AddHeading("运动参数");
        AddNumber("MaxSpeed", "最大速度", BoidConfig.SpeedRange, config.MaxSpeed,
            value => config.MaxSpeed = (float)value, $"速度上限，单位：{lengthUnit}/秒；也用作重置时的初始速率和有转向目标时的目标速率。");
        AddNumber("MaxForce", "最大加速度", BoidConfig.ForceRange, config.MaxForce,
            value => config.MaxForce = (float)value, $"线性加速度模长上限，单位：{lengthUnit}/秒²；限制外部输入与转向输入合成后的总加速度；当前没有加速度输入时不会主动改变速度。");
        AddNumber("MaxAngularSpeed", "最大角速度", BoidConfig.AngularSpeedRange, config.MaxAngularSpeedDegrees,
            value => config.MaxAngularSpeedDegrees = (float)value, "朝向旋转速度上限，单位：度/秒。");
        AddNumber("MaxAngularAcceleration", "最大角加速度", BoidConfig.AngularAccelerationRange, config.MaxAngularAccelerationDegrees,
            value => config.MaxAngularAccelerationDegrees = (float)value, "角速度变化率上限，单位：度/秒²；控制朝向平滑跟随运动方向。");

        if (features.HasFlag(SimulationFeatures.Flocking))
        {
            AddHeading("转向规则");
            if (features.HasFlag(SimulationFeatures.Leader))
            {
                AddNumber("LeaderFollowDistance", "领队后方距离", BoidConfig.LeaderFollowDistanceRange, config.LeaderFollowDistance,
                    value => config.LeaderFollowDistance = (float)value, $"单位：{lengthUnit}；领队到身后跟随区域前缘的间隔；区域随跟随数量扩大，不受发现领队的感知半径限制。");
                AddNumber("LeaderFollowWeight", "领队跟随权重", BoidConfig.LeaderFollowWeightRange, config.LeaderFollowWeight,
                    value => config.LeaderFollowWeight = (float)value, "跟随与原有群集规则共同作用；设为零关闭跟随贡献，避障仍优先。");
            }
            AddNumber("SeparationRadius", "分离半径", BoidConfig.SeparationRadiusRange, config.SeparationRadius,
                value => { config.SeparationRadius = (float)value; RefreshRadiusWarning(config); }, $"单位：{lengthUnit}；仅避开此范围内的邻居，实际范围不超过感知半径。");
            _separationRadiusWarning = new Label
            {
                Name = "SeparationRadiusWarning",
                Text = "提醒：分离半径大于感知半径，实际分离范围以感知半径为限。",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _separationRadiusWarning.AddThemeColorOverride("font_color", new Color("ffc75a"));
            _separationRadiusWarning.AddThemeFontSizeOverride("font_size", 13);
            AddChild(_separationRadiusWarning);
            RefreshRadiusWarning(config);
            AddToggle("SeparationEnabled", "启用分离规则", config.SeparationEnabled, value => config.SeparationEnabled = value);
            AddNumber("SeparationWeight", "分离权重", BoidConfig.SeparationRange, config.SeparationWeight,
                value => config.SeparationWeight = (float)value, "避开近邻；越近的邻居对分离方向影响越大，权重为零时不贡献加速度。");
            AddToggle("AlignmentEnabled", "启用对齐规则", config.AlignmentEnabled, value => config.AlignmentEnabled = value);
            AddNumber("AlignmentWeight", "对齐权重", BoidConfig.AlignmentRange, config.AlignmentWeight,
                value => config.AlignmentWeight = (float)value, "朝邻居的平均速度方向转向，目标速率为最大速度。");
            AddToggle("CohesionEnabled", "启用聚集规则", config.CohesionEnabled, value => config.CohesionEnabled = value);
            AddNumber("CohesionWeight", "聚集权重", BoidConfig.CohesionRange, config.CohesionWeight,
                value => config.CohesionWeight = (float)value, "朝邻居中心靠拢，按当前地图模式的位移计算中心方向。");
        }

        if (features.HasFlag(SimulationFeatures.DebugDrawing))
        {
            AddHeading("调试可视化");
            if (!features.HasFlag(SimulationFeatures.Leader) || threeDimensional)
            {
                _selectionStatus = new Label { Name = "SelectedBoidStatus", Text = "未选择个体" };
                AddChild(_selectionStatus);
            }
            AddToggle("ShowGrid", "显示网格", config.ShowGrid, value => config.ShowGrid = value);
            if (threeDimensional)
                AddToggle("ShowBoundary", "显示边界", config.ShowBoundary, value => config.ShowBoundary = value);
            AddToggle("ShowNeighbors", "显示选中个体的邻居连线", config.ShowNeighbors, value => config.ShowNeighbors = value);
            AddToggle("ShowPerceptionRadius", "显示选中个体的感知范围", config.ShowPerceptionRadius, value => config.ShowPerceptionRadius = value);
            if (features.HasFlag(SimulationFeatures.Flocking))
                AddToggle("ShowSeparationRadius", "显示选中个体的分离半径", config.ShowSeparationRadius, value => config.ShowSeparationRadius = value);
            AddToggle("ShowVelocity", "显示全体运动方向", config.ShowVelocity, value => config.ShowVelocity = value);
        }

        if (!features.HasFlag(SimulationFeatures.Flocking))
        {
            AddHeading("当前阶段");
            AddChild(new Label
            {
                Text = features.HasFlag(SimulationFeatures.NeighborSearch)
                    ? features.HasFlag(SimulationFeatures.UniformGridSearch)
                        ? "已实现基础运动、观察以及朴素 / 网格邻居搜索。群体规则、领队和避障尚未实现；邻居关系暂不改变运动。"
                        : "已实现基础运动、观察与朴素邻居搜索。当前世界未开放网格、群体规则、领队和避障；邻居关系暂不改变运动。"
                    : "仅实现个体创建与基础运动。邻居搜索、群体规则、领队、避障及调试绘制尚未实现。",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        }
    }

    public void RefreshRunState(bool running, bool canStep)
    {
        _startPause.Text = running ? "暂停" : "开始";
        _step.Disabled = !canStep;
    }

    private void RefreshRadiusWarning(BoidConfig config)
    {
        if (_separationRadiusWarning is not null)
            _separationRadiusWarning.Visible = config.SeparationRadius > config.PerceptionRadius;
    }

    public void CommitPendingEdits()
    {
        // 只提交正在编辑的文本。未聚焦输入框的文字可能尚未追上本帧更新的 Value。
        if (GetViewport().GuiGetFocusOwner() is LineEdit edit
            && edit.GetParent() is SpinBox spin && IsAncestorOf(spin))
        {
            if (spin == _populationInput) SubmitPopulationCount(false);
            else spin.Apply();
        }
    }

    public void RefreshPopulationState(int requested, int actual, bool limited)
    {
        _populationStatus.Text = limited
            ? $"请求 {requested}，实际 {actual}。未找到足够的无障碍位置；调整范围或障碍后按 Enter 重试。"
            : $"实际数量：{actual}";
        _populationStatus.AddThemeColorOverride("font_color", new Color(limited ? "ffc75a" : "91a6bb"));
    }

    private void AddPopulationCount(int current)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "请求个体数量", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _populationInput = new SpinBox
        {
            Name = "BoidCount", MinValue = BoidConfig.CountRange.Min, MaxValue = BoidConfig.CountRange.Max,
            Step = BoidConfig.CountRange.Step, Value = current, CustomMinimumSize = new Vector2(128, 34),
            UpdateOnTextChanged = false,
            TooltipText = "按 Enter 或离开输入框后调整数量，保留已有个体的运动状态。缩减时优先保留领队和选中个体；新增个体避开障碍。",
        };
        _lastPopulationSubmission = current;
        SpinBox input = _populationInput;
        input.GetLineEdit().TextSubmitted += _ => { if (input == _populationInput) SubmitPopulationCount(true); };
        input.GetLineEdit().FocusExited += () => { if (input == _populationInput) SubmitPopulationCount(false); };
        row.AddChild(input);
        AddChild(row);
        _populationStatus = new Label
        {
            Name = "PopulationStatus", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _populationStatus.AddThemeFontSizeOverride("font_size", 13);
        AddChild(_populationStatus);
    }

    private void SubmitPopulationCount(bool explicitSubmit)
    {
        _populationInput.Apply();
        int requested = (int)BoidConfig.CountRange.Constrain(_populationInput.Value);
        if (!explicitSubmit && requested == _lastPopulationSubmission) return;
        _lastPopulationSubmission = requested; // Enter 后的失焦不重复增删；再次 Enter 可重试未完成的新增。
        PopulationCountRequested?.Invoke(requested);
    }

    public void RefreshLeaderState(int? selectedId, int? leaderId, bool hasTarget, bool controlActive = false, bool running = false)
    {
        if (_selectionStatus is not null) _selectionStatus.Text = selectedId.HasValue ? $"已选择个体：#{selectedId}" : "未选择个体";
        if (_leaderToggle is null || _leaderStatus is null) return;
        if (_leaderControlButton is not null)
        {
            _leaderControlButton.Disabled = !leaderId.HasValue || controlActive;
            _leaderControlButton.Text = controlActive ? "操控中 · Esc 释放鼠标" : "操控领队（F3）";
        }
        _leaderToggle.Disabled = !selectedId.HasValue;
        _leaderToggle.SetPressedNoSignal(selectedId.HasValue && selectedId == leaderId);
        _leaderStatus.Text = leaderId.HasValue
            ? (_threeDimensional ? $"当前领队：#{leaderId} · {(controlActive ? "操控中" : "待操控")}{(running ? "" : " · 已暂停")}" : $"当前领队：#{leaderId} · {(hasTarget ? "跟随鼠标" : "鼠标移入场景后跟随")}") : "未指定领队";
    }

    public void RefreshViewConfiguration(BoidConfig config)
    {
        if (FindChild("FollowCameraDistance", true, false) is SpinBox distance)
            distance.SetValueNoSignal(config.FollowCameraDistance);
    }

    private static Button AddButton(HBoxContainer row, string name, string text, Action action)
    {
        var button = new Button { Name = name, Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        button.Pressed += action;
        row.AddChild(button);
        return button;
    }

    private void AddSearchMode(BoidConfig config, bool supportsGrid)
    {
        var row = new VBoxContainer();
        row.AddChild(new Label { Text = "搜索方式" });
        var input = new OptionButton
        {
            Name = "NeighborSearchMode",
            TooltipText = supportsGrid
                ? "朴素遍历检查所有其他个体；均匀网格只检查相邻格内的候选。搜索耗时包含快照、建格和结果写入。"
                : "当前世界只开放朴素遍历，逐一检查其他个体。",
            Disabled = !supportsGrid,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        input.AddItem("朴素遍历", (int)NeighborSearchMode.Naive);
        if (supportsGrid) input.AddItem("均匀网格", (int)NeighborSearchMode.UniformGrid);
        input.Select(supportsGrid ? (int)config.NeighborSearchMode : 0);
        input.ItemSelected += index =>
        {
            config.NeighborSearchMode = (NeighborSearchMode)input.GetItemId((int)index);
            ConfigurationChanged?.Invoke();
        };
        row.AddChild(input);
        AddChild(row);
    }

    private void AddHeading(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", new Color("78dcca"));
        label.AddThemeFontSizeOverride("font_size", 13);
        AddChild(label);
    }

    private void AddNumber(string name, string title, BoidConfig.NumberRange range, double current,
        Action<double> setter, string tooltip = "修改后在下一次运行更新时生效。")
    {
        var row = new HBoxContainer { TooltipText = tooltip };
        row.AddChild(new Label { Text = title, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var input = new SpinBox
        {
            Name = name, MinValue = range.Min, MaxValue = range.Max, Step = range.Step,
            Value = current, CustomMinimumSize = new Vector2(128, 34), TooltipText = tooltip,
        };
        input.ValueChanged += value =>
        {
            setter(range.Constrain(value));
            ConfigurationChanged?.Invoke();
        };
        row.AddChild(input);
        AddChild(row);
    }

    private void AddToggle(string name, string title, bool current, Action<bool> setter)
    {
        var input = new CheckBox { Name = name, Text = title, ButtonPressed = current };
        input.Toggled += value =>
        {
            setter(value);
            ConfigurationChanged?.Invoke();
        };
        AddChild(input);
    }
}
