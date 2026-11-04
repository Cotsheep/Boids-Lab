using System;
using System.Collections.Generic;
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
    private VBoxContainer _content = null!;
    private HBoxContainer _categories = null!;
    private readonly Dictionary<string, (Button Button, ScrollContainer Scroll)> _pages = new();
    public string CurrentCategory { get; private set; } = "Basics";

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
        _pages.Clear();
        CurrentCategory = "Basics";
        MouseFilter = MouseFilterEnum.Stop;
        MouseForcePassScrollEvents = false;
        AddThemeConstantOverride("separation", 12);

        var buttons = new HBoxContainer { Name = "SimulationActions" };
        AddChild(buttons);
        _startPause = AddButton(buttons, "StartPause", "开始", () => ToggleRunningRequested?.Invoke());
        _startPause.ThemeTypeVariation = "PrimaryButton";
        _startPause.TooltipText = "空格：暂停 / 继续；输入框有焦点时也有效。";
        _step = AddButton(buttons, "Step", "单步", () => StepRequested?.Invoke());
        _step.Disabled = true;
        _step.TooltipText = "暂停时推进一个物理时间步，完成后仍保持暂停。"
            + (threeDimensional ? "操控领队时先按 Esc 释放鼠标。" : "");
        AddButton(buttons, "Reset", "重置", () => ResetRequested?.Invoke())
            .TooltipText = "按目标数量、随机种子和当前范围重新生成个体并暂停；清除选择和领队，保留障碍物。";
        _selectionStatus = new Label
        {
            Name = "SelectedBoidStatus", Text = "未选中个体 · 点击场景选择",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore,
        };
        _selectionStatus.AddThemeFontSizeOverride("font_size", 13);
        _selectionStatus.ThemeTypeVariation = "MutedLabel";
        AddChild(_selectionStatus);
        _categories = new HBoxContainer { Name = "Categories" };
        _categories.AddThemeConstantOverride("separation", 4);
        AddChild(_categories);

        BeginCategory("Basics", "基础", "数量、生成与运动上限");
        AddHeading("群体生成");
        AddPopulationCount(config.BoidCount);
        AddNumber("RandomSeed", "随机种子", BoidConfig.SeedRange, config.RandomSeed,
            value => config.RandomSeed = (int)value, "点击重置后使用新种子生成群体；相同生成条件下可复现初始状态。");
        AddHint("数量在按 Enter 或离开输入框后调整；随机种子在重置后生效。");
        AddHeading("平移运动");
        AddNumber("MaxSpeed", "最大速度", BoidConfig.SpeedRange, config.MaxSpeed,
            value => config.MaxSpeed = (float)value, $"单位：{lengthUnit}/秒；限制移动速度，也用于生成时的初始速度及转向时的目标速度。");
        AddNumber("MaxForce", "最大加速度", BoidConfig.ForceRange, config.MaxForce,
            value => config.MaxForce = (float)value, $"单位：{lengthUnit}/秒²；限制总加速度；设为 0 时不能主动加速、转向或制动。");
        AddHeading("朝向转动");
        AddNumber("MaxAngularSpeed", "最大角速度", BoidConfig.AngularSpeedRange, config.MaxAngularSpeedDegrees,
            value => config.MaxAngularSpeedDegrees = (float)value, "单位：度/秒；朝向旋转速度上限。");
        AddNumber("MaxAngularAcceleration", "最大角加速度", BoidConfig.AngularAccelerationRange, config.MaxAngularAccelerationDegrees,
            value => config.MaxAngularAccelerationDegrees = (float)value, "单位：度/秒²；限制转动速度每秒的变化，朝向随实际运动方向调整。");

        if (features.HasFlag(SimulationFeatures.NeighborSearch) || features.HasFlag(SimulationFeatures.Flocking))
        {
            BeginCategory("Flocking", "群集", "感知范围、搜索方式与三条规则");
            AddHeading("邻居感知");
            if (features.HasFlag(SimulationFeatures.NeighborSearch))
            {
                AddSearchMode(config, features.HasFlag(SimulationFeatures.UniformGridSearch));
                AddNumber("PerceptionRadius", "感知半径", BoidConfig.RadiusRange, config.PerceptionRadius,
                    value => { config.PerceptionRadius = (float)value; RefreshRadiusWarning(config); },
                    threeDimensional ? "单位：世界单位；按直线距离查找此范围内的邻居，不包含自身。" : "单位：世界像素；查找此范围内的邻居，不包含自身。环绕模式考虑对侧邻居；视口软边界和领队模式使用直线距离。");
            }
            if (features.HasFlag(SimulationFeatures.Flocking))
            {
                AddHeading("分离 · 保持间距");
                AddToggle("SeparationEnabled", "启用分离", config.SeparationEnabled, value => config.SeparationEnabled = value)
                    .TooltipText = "避开分离范围内的近邻，帮助个体保持间距；实际范围不超过感知半径。";
                AddNumber("SeparationRadius", "分离半径", BoidConfig.SeparationRadiusRange, config.SeparationRadius,
                    value => { config.SeparationRadius = (float)value; RefreshRadiusWarning(config); }, $"单位：{lengthUnit}；实际分离范围不超过感知半径。");
                _separationRadiusWarning = AddHint("提醒：分离半径大于感知半径，实际只在感知范围内执行分离。");
                _separationRadiusWarning.Name = "SeparationRadiusWarning";
                _separationRadiusWarning.ThemeTypeVariation = "WarningLabel";
                RefreshRadiusWarning(config);
                AddNumber("SeparationWeight", "分离权重", BoidConfig.SeparationRange, config.SeparationWeight,
                    value => config.SeparationWeight = (float)value, "避开近邻；权重为零时不贡献加速度。");
                AddHeading("对齐 · 协调方向");
                AddToggle("AlignmentEnabled", "启用对齐", config.AlignmentEnabled, value => config.AlignmentEnabled = value)
                    .TooltipText = "朝邻居的平均速度方向转向，帮助群体协调移动。";
                AddNumber("AlignmentWeight", "对齐权重", BoidConfig.AlignmentRange, config.AlignmentWeight,
                    value => config.AlignmentWeight = (float)value, "朝邻居的平均速度方向转向。");
                AddHeading("聚集 · 保持群体");
                AddToggle("CohesionEnabled", "启用聚集", config.CohesionEnabled, value => config.CohesionEnabled = value)
                    .TooltipText = "朝邻居中心靠拢，帮助个体留在群体附近。";
                AddNumber("CohesionWeight", "聚集权重", BoidConfig.CohesionRange, config.CohesionWeight,
                    value => config.CohesionWeight = (float)value, "朝邻居中心靠拢。");
                AddHint("规则可独立关闭；权重越大，相应转向贡献越强。三条规则的权重范围为 0–50。");
            }
        }

        if (features.HasFlag(SimulationFeatures.Leader))
        {
            BeginCategory("Leader", "领队", "身份、跟随队形与操控");
            AddHeading("领队身份");
            _leaderStatus = AddHint("未指定领队");
            _leaderStatus.Name = "LeaderStatus";
            _leaderStatus.ThemeTypeVariation = "LeaderLabel";
            _leaderToggle = new CheckBox
            {
                Name = "IsLeader", Text = "设为领队", Disabled = true,
                TooltipText = threeDimensional ? "先选中个体再勾选，自动进入领队操控；按 Esc 恢复鼠标指针。选中领队后取消勾选，可返回观察模式。" : "将选中个体设为唯一领队，领队跟随场景中的鼠标并进入无限世界。取消勾选后按设置恢复视口软边界或环绕。",
            };
            _leaderToggle.Toggled += enabled => LeaderAssignmentRequested?.Invoke(enabled);
            _content.AddChild(_leaderToggle);
            AddHint(threeDimensional ? "先在场景中选中个体，再勾选设为领队；指定后自动操控，Esc 释放鼠标。" : "先在场景中选中个体，再勾选设为领队；领队将持续跟随鼠标目标。");
            if (threeDimensional && features.HasFlag(SimulationFeatures.DirectLeaderControl))
            {
                _leaderControlButton = new Button { Name = "ControlLeader", Text = "操控领队（F3）", Disabled = true,
                    TooltipText = "鼠标调整相机视角；WASD 以该视角移动，领队朝向跟随运动方向。空格暂停，Esc 释放。" };
                _leaderControlButton.Pressed += () => LeaderControlRequested?.Invoke();
                _content.AddChild(_leaderControlButton);
            }
            if (features.HasFlag(SimulationFeatures.Flocking))
            {
                AddHeading("跟随队形");
                AddNumber("LeaderFollowDistance", "领队后方距离", BoidConfig.LeaderFollowDistanceRange, config.LeaderFollowDistance,
                    value => config.LeaderFollowDistance = (float)value, $"单位：{lengthUnit}；领队与身后跟随区域前缘的间隔；区域随跟随人数扩大。");
                AddNumber("LeaderFollowWeight", "领队跟随权重", BoidConfig.LeaderFollowWeightRange, config.LeaderFollowWeight,
                    value => config.LeaderFollowWeight = (float)value, "与群集规则共同作用；设为零关闭跟随贡献，避障仍优先。");
            }
            if (threeDimensional)
            {
                AddHeading("跟随相机");
                AddNumber("FollowCameraDistance", "跟随视距", BoidConfig.FollowCameraDistanceRange, config.FollowCameraDistance,
                    value => config.FollowCameraDistance = (float)value, "单位：世界单位；滚轮也可调整，Home 恢复默认视距 300。释放鼠标后可右键转动视角，暂停时也有效。");
                AddToggle("FadeNearbyBoids", "淡化近处个体", config.FadeNearbyBoids, value => config.FadeNearbyBoids = value)
                    .TooltipText = "减轻近镜头普通个体遮挡；领队和选中个体保持高亮，不改变运动或拾取。";
            }
        }

        if (features.HasFlag(SimulationFeatures.Obstacles))
        {
            BeginCategory("Obstacles", "障碍", "创建、编辑与避障行为");
            AddHeading("障碍编辑");
            Obstacles = new ObstaclePanel { Name = "ObstaclePanel", LengthUnit = lengthUnit, ThreeDimensional = threeDimensional };
            _content.AddChild(Obstacles);
            AddHeading("避障行为");
            AddToggle("AvoidanceEnabled", "启用避障", config.AvoidanceEnabled, value => config.AvoidanceEnabled = value)
                .TooltipText = threeDimensional ? "控制普通个体的主动避障与接触阻挡；领队始终保留接触阻挡，由你操控绕行。" : "控制所有个体的主动避障与接触阻挡；关闭后可穿过障碍物。";
            AddNumber("AvoidanceLookAhead", "前瞻时间", BoidConfig.AvoidanceLookAheadRange, config.AvoidanceLookAhead,
                value => config.AvoidanceLookAhead = (float)value, "单位：秒；数值越大，越早开始避让。");
            AddNumber("ObstacleClearance", "避障间距", BoidConfig.ObstacleClearanceRange, config.ObstacleClearance,
                value => config.ObstacleClearance = (float)value, $"单位：{lengthUnit}；个体中心与障碍物之间预留的距离。");
            AddHint(threeDimensional ? "普通个体开启避障后主动绕开障碍；领队由你操控绕行，接触时会阻挡并沿表面滑动。" : "开启避障后提前转向，接触时阻挡并沿表面滑动；关闭后所有个体均可穿过障碍。");
        }

        if (features.HasFlag(SimulationFeatures.DebugDrawing) || threeDimensional)
        {
            BeginCategory("Display", "显示", "观察范围、参考线与调试可视化");
            if (!threeDimensional)
            {
                AddHeading("二维观察边界");
                AddToggle("ViewportBoundary", "使用视口软边界", config.ViewportBoundaryEnabled, value => config.ViewportBoundaryEnabled = value)
                    .TooltipText = "软边界在相机视口四周各外扩 10%，靠近边缘向内转向，越界后逐步返回；平移和缩放不搬移个体。关闭后恢复原有环绕。领队模式下不生效。";
            }
            if (threeDimensional)
            {
                AddHeading("三维观察范围");
                AddNumber("WorldWidth", "宽度 X", BoidConfig.WorldWidthRange, config.WorldWidth, value => config.WorldWidth = (float)value, "单位：世界单位；立即更新观察范围，保留个体位置与相机视角。Home 重新取景。");
                AddNumber("WorldHeight", "高度 Y", BoidConfig.WorldHeightRange, config.WorldHeight, value => config.WorldHeight = (float)value, "单位：世界单位；立即更新观察范围，保留个体位置与相机视角。Home 重新取景。");
                AddNumber("WorldDepth", "深度 Z", BoidConfig.WorldDepthRange, config.WorldDepth, value => config.WorldDepth = (float)value, "单位：世界单位；立即更新观察范围，保留个体位置与相机视角。Home 重新取景。");
                AddHint("范围独立于相机；修改后保留个体位置，Home 按新范围重新取景。领队模式不受边界约束。");
            }
            if (features.HasFlag(SimulationFeatures.DebugDrawing))
            {
                AddHeading("场景参考");
                AddToggle("ShowGrid", "显示网格", config.ShowGrid, value => config.ShowGrid = value)
                    .TooltipText = threeDimensional ? "显示观察范围底面的参考网格；领队模式下隐藏，与邻居搜索方式无关。" : "显示背景参考网格，与邻居搜索方式无关。";
                if (threeDimensional)
                    AddToggle("ShowBoundary", "显示边界", config.ShowBoundary, value => config.ShowBoundary = value)
                        .TooltipText = "显示观察范围的边框；取消勾选后软边界仍生效。领队模式下边框和网格均隐藏。";
                AddHeading("选中个体的调试");
                AddHint("先在场景中选中一个个体；范围和邻居连线跟随该个体显示。");
                AddToggle("ShowNeighbors", "显示邻居连线", config.ShowNeighbors, value => config.ShowNeighbors = value)
                    .TooltipText = "连接选中个体与感知范围内的邻居；未选中个体时不显示。";
                AddToggle("ShowPerceptionRadius", "显示感知范围", config.ShowPerceptionRadius, value => config.ShowPerceptionRadius = value)
                    .TooltipText = "显示选中个体的邻居感知范围；未选中个体时不显示。";
                if (features.HasFlag(SimulationFeatures.Flocking))
                    AddToggle("ShowSeparationRadius", "显示分离半径", config.ShowSeparationRadius, value => config.ShowSeparationRadius = value)
                        .TooltipText = "显示选中个体的实际分离范围，取分离半径与感知半径的较小值。";
                AddHeading("全体个体的调试");
                AddToggle("ShowVelocity", "显示运动方向", config.ShowVelocity, value => config.ShowVelocity = value)
                    .TooltipText = "用箭头显示全体个体的移动方向；箭头长度随速率变化，静止个体不显示。";
            }
        }
        SelectCategory("Basics");
    }

    private void BeginCategory(string id, string title, string tooltip)
    {
        var button = new Button
        {
            Name = "Category" + id, Text = title, TooltipText = tooltip, ToggleMode = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, ThemeTypeVariation = "CategoryButton",
        };
        _categories.AddChild(button);
        var scroll = new ScrollContainer
        {
            Name = id + "Page", SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true, MouseFilter = MouseFilterEnum.Stop, MouseForcePassScrollEvents = false,
            Visible = false,
        };
        AddChild(scroll);
        _content = new VBoxContainer { Name = "Content", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _content.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_content);
        _pages.Add(id, (button, scroll));
        button.Pressed += () => SelectCategory(id);
    }

    public void SelectCategory(string id)
    {
        if (!_pages.ContainsKey(id)) return;
        // 隐藏前提交草稿并释放焦点，避免跨页后 WASD 仍被不可见输入框截获。
        CommitPendingEdits();
        if (GetViewport().GuiGetFocusOwner() is { } focus)
            foreach (var page in _pages.Values)
                if (page.Scroll.IsAncestorOf(focus)) { focus.ReleaseFocus(); break; }
        CurrentCategory = id;
        foreach (var (key, page) in _pages)
        {
            page.Button.SetPressedNoSignal(key == id);
            page.Scroll.Visible = key == id;
        }
    }

    public async void RevealControl(Control control)
    {
        foreach (var (id, page) in _pages)
        {
            if (!page.Scroll.IsAncestorOf(control)) continue;
            SelectCategory(id);
            // 切页的嵌套容器先完成一轮布局，再在下一帧末滚动。
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Callable.From(() =>
            {
                if (IsInstanceValid(page.Scroll) && IsInstanceValid(control) && control.IsVisibleInTree())
                    page.Scroll.EnsureControlVisible(control);
            }).CallDeferred();
            return;
        }
    }

    private Label AddHint(string text)
    {
        var hint = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
        hint.ThemeTypeVariation = "MutedLabel";
        hint.AddThemeFontSizeOverride("font_size", 13);
        _content.AddChild(hint);
        return hint;
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
            ? $"目标 {requested}，实际 {actual}。未找到足够的生成位置；调整范围或障碍后按 Enter 重试。"
            : $"实际数量：{actual}";
        _populationStatus.ThemeTypeVariation = limited ? "WarningLabel" : "MutedLabel";
    }

    private void AddPopulationCount(int current)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "目标个体数量", SizeFlagsHorizontal = SizeFlags.ExpandFill });
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
        _content.AddChild(row);
        _populationStatus = new Label
        {
            Name = "PopulationStatus", AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _populationStatus.AddThemeFontSizeOverride("font_size", 13);
        _content.AddChild(_populationStatus);
    }

    private void SubmitPopulationCount(bool explicitSubmit)
    {
        _populationInput.Apply();
        int requested = (int)BoidConfig.CountRange.Constrain(_populationInput.Value);
        if (!explicitSubmit && requested == _lastPopulationSubmission) return;
        _lastPopulationSubmission = requested; // Enter 后的失焦不重复增删；再次 Enter 可重试未完成的新增。
        PopulationCountRequested?.Invoke(requested);
    }

    public void RefreshLeaderState(int? selectedId, int? leaderId, bool hasTarget, bool controlActive = false, bool running = false, bool obstacleSelected = false)
    {
        if (_selectionStatus is not null) _selectionStatus.Text = selectedId.HasValue
            ? selectedId == leaderId ? $"已选领队 #{selectedId} · 在领队页取消身份"
                : $"已选个体 #{selectedId}" + (_leaderToggle is null ? "" : " · 在领队页设为领队")
            : obstacleSelected ? "已选障碍物 · 在障碍页编辑"
                : leaderId.HasValue ? $"领队 #{leaderId} · {(_threeDimensional ? controlActive ? "操控中 · Esc 释放" : "跟随视角 · F3 操控" : "跟随鼠标")}" : "未选中个体 · 点击场景选择";
        if (_leaderToggle is null || _leaderStatus is null) return;
        if (_leaderControlButton is not null)
        {
            _leaderControlButton.Disabled = !leaderId.HasValue || controlActive;
            _leaderControlButton.Text = controlActive ? "操控中 · Esc 释放鼠标" : "操控领队（F3）";
        }
        _leaderToggle.Disabled = !selectedId.HasValue;
        _leaderToggle.SetPressedNoSignal(selectedId.HasValue && selectedId == leaderId);
        _leaderStatus.Text = leaderId.HasValue
            ? $"当前领队：#{leaderId} · {(_threeDimensional ? controlActive ? "操控中" : "跟随视角 · F3 操控" : hasTarget ? "跟随鼠标" : "鼠标移入场景后跟随")}{(running ? "" : " · 已暂停")}"
            : "未指定领队 · 先在场景中选择一个个体";
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
                : "逐一检查其他个体是否位于感知范围内。",
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
        _content.AddChild(row);
    }

    private void AddHeading(string text)
    {
        var label = new Label { Text = text, CustomMinimumSize = new Vector2(0, 30), VerticalAlignment = VerticalAlignment.Bottom };
        label.ThemeTypeVariation = "AccentLabel";
        label.AddThemeFontSizeOverride("font_size", 13);
        _content.AddChild(label);
    }

    private void AddNumber(string name, string title, BoidConfig.NumberRange range, double current,
        Action<double> setter, string tooltip = "修改后自动保存，在下一次模拟更新时生效；暂停时可用单步查看效果。")
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
        _content.AddChild(row);
    }

    private CheckBox AddToggle(string name, string title, bool current, Action<bool> setter)
    {
        var input = new CheckBox { Name = name, Text = title, ButtonPressed = current };
        input.Toggled += value =>
        {
            setter(value);
            ConfigurationChanged?.Invoke();
        };
        _content.AddChild(input);
        return input;
    }
}
