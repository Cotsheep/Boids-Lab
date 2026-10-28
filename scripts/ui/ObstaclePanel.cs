using System;
using Godot;

namespace BoidsLab;

/// <summary>障碍物编辑控件；只发送请求，不持有世界节点或保存场景状态。</summary>
public partial class ObstaclePanel : VBoxContainer
{
    public event Action<ObstacleShape>? CreateRequested;
    public event Action? DeleteRequested;
    public event Action<float, float>? ResizeRequested;
    public event Action? CancelPlacementRequested;

    public string LengthUnit { get; set; } = "像素";

    private Label _status = null!;
    private Label _widthLabel = null!;
    private SpinBox _width = null!;
    private SpinBox _height = null!;
    private HBoxContainer _heightRow = null!;
    private Button _delete = null!;
    private Button _cancel = null!;
    private ObstacleSelection? _selection;
    private ObstacleShape? _placement;

    public override void _Ready()
    {
        var createRow = new HBoxContainer();
        AddChild(createRow);
        AddButton(createRow, "AddRectangle", "添加矩形", () => CreateRequested?.Invoke(ObstacleShape.Rectangle))
            .TooltipText = "进入放置模式，在场景中点击创建矩形障碍物。";
        AddButton(createRow, "AddCircle", "添加圆形", () => CreateRequested?.Invoke(ObstacleShape.Circle))
            .TooltipText = "进入放置模式，在场景中点击创建圆形障碍物。";

        _status = new Label { Name = "ObstacleStatus", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _status.AddThemeColorOverride("font_color", new Color("91a6bb"));
        _status.AddThemeFontSizeOverride("font_size", 13);
        AddChild(_status);

        var widthRow = new HBoxContainer();
        _widthLabel = new Label { Text = "宽度", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        widthRow.AddChild(_widthLabel);
        _width = AddSizeInput(widthRow, "ObstacleWidth");
        AddChild(widthRow);

        _heightRow = new HBoxContainer();
        _heightRow.AddChild(new Label { Text = "高度", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _height = AddSizeInput(_heightRow, "ObstacleHeight");
        AddChild(_heightRow);
        _width.ValueChanged += OnSizeChanged;
        _height.ValueChanged += OnSizeChanged;

        var editRow = new HBoxContainer();
        AddChild(editRow);
        _delete = AddButton(editRow, "DeleteObstacle", "删除选中", () => DeleteRequested?.Invoke());
        _delete.TooltipText = "删除当前选中的障碍物；也可按 Delete，编辑输入框时不会删除。";
        _cancel = AddButton(editRow, "CancelObstaclePlacement", "取消放置", () => CancelPlacementRequested?.Invoke());
        _cancel.TooltipText = "取消尚未完成的放置；也可按 Esc 或鼠标右键。";
        Refresh(_selection, _placement);
    }

    public void Refresh(ObstacleSelection? selected, ObstacleShape? placement)
    {
        _selection = selected;
        _placement = placement;
        if (_status is null) return;

        bool circle = selected?.Shape == ObstacleShape.Circle;
        bool editable = selected.HasValue && !placement.HasValue;
        _status.Text = placement.HasValue
            ? $"正在放置{(placement == ObstacleShape.Circle ? "圆形" : "矩形")}：点击场景"
            : selected.HasValue ? $"已选中{(circle ? "圆形" : "矩形")}障碍物" : "未选择障碍物";
        _widthLabel.Text = circle ? "直径" : "宽度";
        _heightRow.Visible = !circle;
        _width.Editable = _height.Editable = editable;
        _width.SetValueNoSignal(selected?.Width ?? Obstacle2D.DefaultRectangleSize.X);
        _height.SetValueNoSignal(selected?.Height ?? Obstacle2D.DefaultRectangleSize.Y);
        _delete.Disabled = !editable;
        _cancel.Visible = placement.HasValue;
    }

    private void OnSizeChanged(double value)
    {
        if (!_selection.HasValue || _placement.HasValue) return;
        float width = (float)_width.Value;
        float height = _selection.Value.Shape == ObstacleShape.Circle ? width : (float)_height.Value;
        ResizeRequested?.Invoke(width, height);
    }

    private SpinBox AddSizeInput(HBoxContainer row, string name)
    {
        var input = new SpinBox
        {
            Name = name,
            MinValue = Obstacle2D.MinSize,
            MaxValue = Obstacle2D.MaxSize,
            Step = 0.1,
            CustomMinimumSize = new Vector2(128, 34),
            TooltipText = $"单位：{LengthUnit}；修改后立即调整选中障碍物。",
        };
        row.AddChild(input);
        return input;
    }

    private static Button AddButton(HBoxContainer row, string name, string text, Action action)
    {
        var button = new Button { Name = name, Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        button.Pressed += action;
        row.AddChild(button);
        return button;
    }
}
