using System;
using Godot;

namespace BoidsLab;

public partial class ControlPanel : VBoxContainer
{
    public event Action? ToggleRunningRequested;
    public event Action? ResetRequested;
    public event Action? StepRequested;
    public event Action? ConfigurationChanged;

    public ObstaclePanel Obstacles { get; private set; } = null!;

    private Button _startPause = null!;
    private Button _step = null!;

    public void Bind(BoidConfig config, string lengthUnit)
    {
        // 主控制器在 _Ready 中调用一次；控件的值、范围均来自共享配置。
        AddHeading("模拟控制");
        var buttons = new HBoxContainer();
        AddChild(buttons);
        _startPause = AddButton(buttons, "StartPause", "开始", () => ToggleRunningRequested?.Invoke());
        _startPause.TooltipText = "空格：暂停 / 继续；无论焦点在哪个控件上都有效，长按不会重复切换。";
        AddButton(buttons, "Reset", "重置", () => ResetRequested?.Invoke())
            .TooltipText = "暂停并按当前数量、种子和最大速度重新生成个体；清空加速度与旋转状态。";
        _step = AddButton(buttons, "Step", "单步", () => StepRequested?.Invoke());
        _step.Disabled = true;
        _step.TooltipText = "单步功能暂未开放。";

        AddHeading("障碍物");
        Obstacles = new ObstaclePanel { Name = "ObstaclePanel", LengthUnit = lengthUnit };
        AddChild(Obstacles);
        AddToggle("AvoidanceEnabled", "启用避障", config.AvoidanceEnabled, value => config.AvoidanceEnabled = value);
        AddNumber("AvoidanceLookAhead", "前瞻时间", BoidConfig.AvoidanceLookAheadRange, config.AvoidanceLookAhead,
            value => config.AvoidanceLookAhead = (float)value, "单位：秒；沿当前运动方向提前观察障碍物。数值越大，越早开始避让。");
        AddNumber("ObstacleClearance", "避障间距", BoidConfig.ObstacleClearanceRange, config.ObstacleClearance,
            value => config.ObstacleClearance = (float)value, $"单位：{lengthUnit}；个体中心与障碍物之间预留的距离。");

        AddHeading("群体设置");
        AddNumber("BoidCount", "个体数量", BoidConfig.CountRange, config.BoidCount,
            value => config.BoidCount = (int)value, "目标数量；点击重置后重新生成，不会立即增删个体。");
        AddNumber("RandomSeed", "随机种子", BoidConfig.SeedRange, config.RandomSeed,
            value => config.RandomSeed = (int)value, "点击重置后生效；相同种子和参数可复现初始位置与速度。");

        AddHeading("邻居搜索");
        AddSearchMode(config);
        AddNumber("PerceptionRadius", "感知半径", BoidConfig.RadiusRange, config.PerceptionRadius,
            value => config.PerceptionRadius = (float)value, $"单位：{lengthUnit}；按最短环绕距离查找邻居，包含半径边缘，不包含自身。");

        AddHeading("运动参数");
        AddNumber("MaxSpeed", "最大速度", BoidConfig.SpeedRange, config.MaxSpeed,
            value => config.MaxSpeed = (float)value, $"速度上限，单位：{lengthUnit}/秒；也用作重置时的初始速率和有转向目标时的目标速率。");
        AddNumber("MaxForce", "最大加速度", BoidConfig.ForceRange, config.MaxForce,
            value => config.MaxForce = (float)value, $"线性加速度模长上限，单位：{lengthUnit}/秒²；限制各规则及合成后的总加速度。");
        AddNumber("MaxAngularSpeed", "最大角速度", BoidConfig.AngularSpeedRange, config.MaxAngularSpeedDegrees,
            value => config.MaxAngularSpeedDegrees = (float)value, "朝向旋转速度上限，单位：度/秒。");
        AddNumber("MaxAngularAcceleration", "最大角加速度", BoidConfig.AngularAccelerationRange, config.MaxAngularAccelerationDegrees,
            value => config.MaxAngularAccelerationDegrees = (float)value, "角速度变化率上限，单位：度/秒²；控制朝向平滑跟随运动方向。");

        AddHeading("转向规则");
        AddNumber("SeparationRadius", "分离半径", BoidConfig.SeparationRadiusRange, config.SeparationRadius,
            value => config.SeparationRadius = (float)value, $"单位：{lengthUnit}；仅避开此范围内的邻居，实际范围不超过感知半径。");
        AddToggle("SeparationEnabled", "启用分离规则", config.SeparationEnabled, value => config.SeparationEnabled = value);
        AddNumber("SeparationWeight", "分离权重", BoidConfig.SeparationRange, config.SeparationWeight,
            value => config.SeparationWeight = (float)value, "避开近邻；越近的邻居对分离方向影响越大，权重为零时不贡献加速度。");
        AddToggle("AlignmentEnabled", "启用对齐规则", config.AlignmentEnabled, value => config.AlignmentEnabled = value);
        AddNumber("AlignmentWeight", "对齐权重", BoidConfig.AlignmentRange, config.AlignmentWeight,
            value => config.AlignmentWeight = (float)value, "朝邻居的平均速度方向转向，目标速率为最大速度。");
        AddToggle("CohesionEnabled", "启用聚集规则", config.CohesionEnabled, value => config.CohesionEnabled = value);
        AddNumber("CohesionWeight", "聚集权重", BoidConfig.CohesionRange, config.CohesionWeight,
            value => config.CohesionWeight = (float)value, "朝邻居中心靠拢，使用最短环绕位移计算中心方向。");

        AddHeading("调试可视化");
        AddToggle("ShowGrid", "显示网格", config.ShowGrid, value => config.ShowGrid = value);
        AddToggle("ShowNeighbors", "显示选中个体的邻居连线", config.ShowNeighbors, value => config.ShowNeighbors = value);
        AddToggle("ShowPerceptionRadius", "显示选中个体的感知范围", config.ShowPerceptionRadius, value => config.ShowPerceptionRadius = value);
        AddToggle("ShowSeparationRadius", "显示选中个体的分离半径", config.ShowSeparationRadius, value => config.ShowSeparationRadius = value);
        AddToggle("ShowVelocity", "显示全体运动方向", config.ShowVelocity, value => config.ShowVelocity = value);
    }

    public void RefreshRunState(bool running, bool canStep)
    {
        _startPause.Text = running ? "暂停" : "开始";
        _step.Disabled = !canStep;
    }

    private static Button AddButton(HBoxContainer row, string name, string text, Action action)
    {
        var button = new Button { Name = name, Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        button.Pressed += action;
        row.AddChild(button);
        return button;
    }

    private void AddSearchMode(BoidConfig config)
    {
        var row = new VBoxContainer();
        row.AddChild(new Label { Text = "搜索方式" });
        var input = new OptionButton
        {
            Name = "NeighborSearchMode",
            TooltipText = "朴素遍历检查所有其他个体；均匀网格只检查相邻格内的候选。搜索耗时包含快照、建格和结果写入。",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        input.AddItem("朴素遍历", (int)NeighborSearchMode.Naive);
        input.AddItem("均匀网格", (int)NeighborSearchMode.UniformGrid);
        input.Select((int)config.NeighborSearchMode);
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
