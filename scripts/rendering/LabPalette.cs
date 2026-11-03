using Godot;

namespace BoidsLab;

/// <summary>可在 Inspector 编辑的语义配色资源；修改会通知运行中的界面和世界刷新。</summary>
[GlobalClass]
public partial class LabPalette : Resource
{
    public const string DarkPath = "res://themes/bingyan_dark.tres";
    public const string LightPath = "res://themes/bingyan_light.tres";
    private static LabPalette? _dark, _light;
    public static LabPalette Dark => _dark ??= GD.Load<LabPalette>(DarkPath);
    public static LabPalette Light => _light ??= GD.Load<LabPalette>(LightPath);
    public static LabPalette For(bool light) => light ? Light : Dark;

    [ExportGroup("模式")]
    [Export] public bool IsLight
    {
        get => _isLight;
        set { if (_isLight == value) return; _isLight = value; EmitChanged(); }
    }
    private bool _isLight;

    [ExportGroup("界面底色")]
    [Export] public Color Canvas { get => _canvas; set => SetColor(ref _canvas, value); }
    private Color _canvas = new("17191c");
    [Export] public Color Panel { get => _panel; set => SetColor(ref _panel, value); }
    private Color _panel = new("222428");
    [Export] public Color Raised { get => _raised; set => SetColor(ref _raised, value); }
    private Color _raised = new("303238");
    [Export] public Color Input { get => _input; set => SetColor(ref _input, value); }
    private Color _input = new("1a1c20");
    [Export] public Color Border { get => _border; set => SetColor(ref _border, value); }
    private Color _border = new("484a50");

    [ExportGroup("文字与状态")]
    [Export] public Color Text { get => _text; set => SetColor(ref _text, value); }
    private Color _text = new("f5efe7");
    [Export] public Color Muted { get => _muted; set => SetColor(ref _muted, value); }
    private Color _muted = new("b9afa4");
    [Export] public Color Disabled { get => _disabled; set => SetColor(ref _disabled, value); }
    private Color _disabled = new("7f7972");
    [Export] public Color Warning { get => _warning; set => SetColor(ref _warning, value); }
    private Color _warning = new("f4c26b");
    [Export] public Color Success { get => _success; set => SetColor(ref _success, value); }
    private Color _success = new("80c8b7");

    [ExportGroup("强调与交互")]
    [Export] public Color Accent { get => _accent; set => SetColor(ref _accent, value); }
    private Color _accent = new("ff8a42");
    [Export] public Color OnAccent { get => _onAccent; set => SetColor(ref _onAccent, value); }
    private Color _onAccent = new("27180e");
    [Export] public Color AccentText { get => _accentText; set => SetColor(ref _accentText, value); }
    private Color _accentText = new("ffac72");
    [Export] public Color AccentWash { get => _accentWash; set => SetColor(ref _accentWash, value); }
    private Color _accentWash = new("493226");

    [ExportGroup("个体身份")]
    [Export] public Color Boid { get => _boid; set => SetColor(ref _boid, value); }
    private Color _boid = new("ffa94d");
    [Export] public Color Selection { get => _selection; set => SetColor(ref _selection, value); }
    private Color _selection = new("ffe394");
    [Export] public Color Leader { get => _leader; set => SetColor(ref _leader, value); }
    private Color _leader = new("d69cff");
    [Export] public Color SelectedLeader { get => _selectedLeader; set => SetColor(ref _selectedLeader, value); }
    private Color _selectedLeader = new("ffa1eb");

    [ExportGroup("场景与障碍")]
    [Export] public Color Grid { get => _grid; set => SetColor(ref _grid, value); }
    private Color _grid = new("2b2e33");
    [Export] public Color Frame { get => _frame; set => SetColor(ref _frame, value); }
    private Color _frame = new("65605a");
    [Export] public Color Obstacle { get => _obstacle; set => SetColor(ref _obstacle, value); }
    private Color _obstacle = new("626671");
    [Export] public Color ObstacleSelected { get => _obstacleSelected; set => SetColor(ref _obstacleSelected, value); }
    private Color _obstacleSelected = new("94703c");
    [Export] public Color ObstacleOutline { get => _obstacleOutline; set => SetColor(ref _obstacleOutline, value); }
    private Color _obstacleOutline = new("9298a6");
    [Export] public Color ObstaclePreview { get => _obstaclePreview; set => SetColor(ref _obstaclePreview, value); }
    private Color _obstaclePreview = new("ff8a42");
    [Export] public Color ObstaclePreviewOutline { get => _obstaclePreviewOutline; set => SetColor(ref _obstaclePreviewOutline, value); }
    private Color _obstaclePreviewOutline = new("ffac72");

    [ExportGroup("调试可视化")]
    [Export] public Color Perception { get => _perception; set => SetColor(ref _perception, value); }
    private Color _perception = new("78dcca");
    [Export] public Color Separation { get => _separation; set => SetColor(ref _separation, value); }
    private Color _separation = new("ff9866");
    [Export] public Color Velocity { get => _velocity; set => SetColor(ref _velocity, value); }
    private Color _velocity = new("6baeff");

    private void SetColor(ref Color field, Color value)
    {
        if (field == value) return;
        field = value;
        EmitChanged();
    }
}
