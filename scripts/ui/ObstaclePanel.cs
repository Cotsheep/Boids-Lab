using System;
using Godot;

namespace BoidsLab;

/// <summary>障碍物编辑控件；只发送请求，不持有世界节点或保存场景状态。</summary>
public partial class ObstaclePanel : VBoxContainer
{
    public event Action<ObstacleShape>? CreateRequested;
    public event Action? DeleteRequested;
    public event Action<float, float, float>? ResizeRequested;
    public event Action? CancelPlacementRequested;

    public string LengthUnit { get; set; } = "世界像素";
    public bool ThreeDimensional { get; set; }

    private Label _status = null!;
    private HBoxContainer _widthRow = null!;
    private HBoxContainer _editRow = null!;
    private Label _widthLabel = null!;
    private SpinBox _width = null!;
    private SpinBox _height = null!;
    private HBoxContainer _heightRow = null!;
    private SpinBox _depth = null!;
    private HBoxContainer _depthRow = null!;
    private Button _delete = null!;
    private Button _cancel = null!;
    private ObstacleSelection? _selection;
    private ObstacleShape? _placement;

    public override void _Ready()
    {
        var createRow = new HBoxContainer();
        AddChild(createRow);
        AddButton(createRow, ThreeDimensional ? "AddBox" : "AddRectangle", ThreeDimensional ? "添加长方体" : "添加矩形",
            () => CreateRequested?.Invoke(ThreeDimensional ? ObstacleShape.Box : ObstacleShape.Rectangle))
            .TooltipText = ThreeDimensional ? "进入放置模式，移动鼠标预览，左键放置长方体；Esc 或右键取消。放置平面固定，可超出观察范围。" : "进入放置模式，移动鼠标预览，左键放置矩形；Esc 或右键取消。";
        AddButton(createRow, ThreeDimensional ? "AddSphere" : "AddCircle", ThreeDimensional ? "添加球体" : "添加圆形",
            () => CreateRequested?.Invoke(ThreeDimensional ? ObstacleShape.Sphere : ObstacleShape.Circle))
            .TooltipText = ThreeDimensional ? "进入放置模式，移动鼠标预览，左键放置球体；Esc 或右键取消。放置平面固定，可超出观察范围。" : "进入放置模式，移动鼠标预览，左键放置圆形；Esc 或右键取消。";

        _status = new Label { Name = "ObstacleStatus", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _status.ThemeTypeVariation = "MutedLabel";
        _status.AddThemeFontSizeOverride("font_size", 13);
        AddChild(_status);

        var widthRow = _widthRow = new HBoxContainer();
        _widthLabel = new Label { Text = "宽度", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        widthRow.AddChild(_widthLabel);
        _width = AddSizeInput(widthRow, "ObstacleWidth");
        AddChild(widthRow);

        _heightRow = new HBoxContainer();
        _heightRow.AddChild(new Label { Text = ThreeDimensional ? "高度 Y" : "高度", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _height = AddSizeInput(_heightRow, "ObstacleHeight");
        AddChild(_heightRow);
        _depthRow = new HBoxContainer();
        _depthRow.AddChild(new Label { Text = "深度 Z", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _depth = AddSizeInput(_depthRow, "ObstacleDepth");
        AddChild(_depthRow);
        _width.ValueChanged += OnSizeChanged;
        _height.ValueChanged += OnSizeChanged;
        _depth.ValueChanged += OnSizeChanged;

        var editRow = _editRow = new HBoxContainer();
        AddChild(editRow);
        _delete = AddButton(editRow, "DeleteObstacle", "删除障碍物", () => DeleteRequested?.Invoke());
        _delete.TooltipText = "删除当前选中的障碍物；也可按 Delete，编辑输入框时不会删除。";
        _cancel = AddButton(editRow, "CancelObstaclePlacement", "取消放置", () => CancelPlacementRequested?.Invoke());
        _cancel.TooltipText = "结束当前放置预览；也可按 Esc 或鼠标右键。";
        Refresh(_selection, _placement);
    }

    public void Refresh(ObstacleSelection? selected, ObstacleShape? placement)
    {
        _selection = selected;
        _placement = placement;
        if (_status is null) return;

        bool circle = selected?.Shape is ObstacleShape.Circle or ObstacleShape.Sphere;
        bool editable = selected.HasValue && !placement.HasValue;
        _status.Text = placement.HasValue
            ? $"正在放置{ShapeName(placement.Value)} · 左键确认，Esc 或右键取消"
            : selected.HasValue ? $"已选中{ShapeName(selected.Value.Shape)}障碍物" : "选择上方形状开始放置；点击已有障碍物可编辑尺寸。";
        _widthLabel.Text = circle ? "直径" : ThreeDimensional ? "宽度 X" : "宽度";
        _widthRow.Visible = editable;
        _heightRow.Visible = editable && !circle;
        _depthRow.Visible = editable && ThreeDimensional && !circle;
        _editRow.Visible = editable || placement.HasValue;
        _delete.Visible = editable;
        _width.Editable = _height.Editable = _depth.Editable = editable;
        _width.SetValueNoSignal(selected?.Width ?? Obstacle2D.DefaultRectangleSize.X);
        _height.SetValueNoSignal(selected?.Height ?? Obstacle2D.DefaultRectangleSize.Y);
        _depth.SetValueNoSignal(selected?.Depth ?? Obstacle3D.DefaultBoxSize.Z);
        _delete.Disabled = !editable;
        _cancel.Visible = placement.HasValue;
    }

    private void OnSizeChanged(double value)
    {
        if (!_selection.HasValue || _placement.HasValue) return;
        float width = (float)_width.Value;
        bool round = _selection.Value.Shape is ObstacleShape.Circle or ObstacleShape.Sphere;
        float height = round ? width : (float)_height.Value;
        float depth = round ? width : (float)_depth.Value;
        ResizeRequested?.Invoke(width, height, ThreeDimensional ? depth : 0);
    }

    private SpinBox AddSizeInput(HBoxContainer row, string name)
    {
        var input = new SpinBox
        {
            Name = name,
            MinValue = ObstacleSize.Min,
            MaxValue = ObstacleSize.Max,
            Step = 0.1,
            CustomMinimumSize = new Vector2(128, 34),
            TooltipText = $"单位：{LengthUnit}；修改后立即调整选中障碍物。",
        };
        row.AddChild(input);
        return input;
    }

    private static string ShapeName(ObstacleShape shape) => shape switch
    {
        ObstacleShape.Box => "长方体",
        ObstacleShape.Sphere => "球体",
        ObstacleShape.Circle => "圆形",
        _ => "矩形",
    };

    private static Button AddButton(HBoxContainer row, string name, string text, Action action)
    {
        var button = new Button { Name = name, Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        button.Pressed += action;
        row.AddChild(button);
        return button;
    }
}
