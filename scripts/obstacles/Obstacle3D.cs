using System;
using Godot;

namespace BoidsLab;

/// <summary>轴对齐长方体或球体；用真实尺寸管理解析几何，不使用节点缩放或刚体。</summary>
public partial class Obstacle3D : Node3D
{
    private LabPalette _palette = LabPalette.Dark;
    internal void ApplyPalette(LabPalette palette) { _palette = palette; RefreshAppearance(); }
    public static readonly Vector3 DefaultBoxSize = new(160, 100, 100);
    public static readonly Vector3 DefaultSphereSize = Vector3.One * 100;
    public ObstacleShape Shape { get; private set; }
    public Vector3 Dimensions { get; private set; } = DefaultBoxSize;
    public bool IsSelected { get; private set; }
    public Aabb WorldBox => new(Position - Dimensions * 0.5f, Dimensions);
    private MeshInstance3D? _body;
    private MeshInstance3D? _outline;
    private bool _preview;
    private readonly StandardMaterial3D _material = new() { Roughness = 0.8f };
    private readonly StandardMaterial3D _lineMaterial = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color("ffe394"),
    };

    public override void _Ready()
    {
        _body = new MeshInstance3D { Name = "Body", MaterialOverride = _material };
        _outline = new MeshInstance3D { Name = "SelectionOutline" };
        AddChild(_body);
        AddChild(_outline);
        RebuildMesh();
        RefreshAppearance();
    }

    public void Initialize(ObstacleShape shape, Vector3 dimensions)
    {
        if (shape is not (ObstacleShape.Box or ObstacleShape.Sphere)) throw new ArgumentOutOfRangeException(nameof(shape));
        Shape = shape;
        SetDimensions(dimensions);
    }

    public void SetDimensions(Vector3 size)
    {
        if (!size.IsFinite()) throw new ArgumentOutOfRangeException(nameof(size));
        size = size.Clamp(Vector3.One * ObstacleSize.Min, Vector3.One * ObstacleSize.Max);
        Dimensions = Shape == ObstacleShape.Sphere ? Vector3.One * size.X : size;
        RebuildMesh();
    }

    public void SetSelected(bool selected) { IsSelected = selected; RefreshAppearance(); }
    public void SetPreview(bool preview) { _preview = preview; RefreshAppearance(); }

    private void RefreshAppearance()
    {
        // 明色使用干净的平面填色与轮廓，避免场景灯光把杏橙压成灰褐色。
        _material.ShadingMode = _palette.IsLight ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel;
        _material.AlbedoColor = _preview ? _palette.ObstaclePreview : IsSelected ? _palette.ObstacleSelected : _palette.Obstacle;
        _lineMaterial.AlbedoColor = _preview ? _palette.ObstaclePreviewOutline : IsSelected ? _palette.Selection : _palette.ObstacleOutline;
        if (_outline is not null) _outline.Visible = IsSelected || _preview || _palette.IsLight;
    }

    private void RebuildMesh()
    {
        if (_body is null || _outline is null) return;
        _body.Mesh = Shape == ObstacleShape.Sphere
            ? new SphereMesh { Radius = Dimensions.X * 0.5f, Height = Dimensions.X, RadialSegments = 32, Rings = 16 }
            : new BoxMesh { Size = Dimensions };
        var lines = new ImmediateMesh();
        lines.SurfaceBegin(Mesh.PrimitiveType.Lines, _lineMaterial);
        if (Shape == ObstacleShape.Box)
        {
            Vector3 half = Dimensions * 0.5f + Vector3.One * 0.1f;
            for (int i = 0; i < 8; i++)
                foreach (int bit in new[] { 1, 2, 4 })
                    if ((i & bit) == 0) { lines.SurfaceAddVertex(Corner(i)); lines.SurfaceAddVertex(Corner(i | bit)); }
            Vector3 Corner(int index) => new Vector3((index & 1) == 0 ? -half.X : half.X,
                (index & 2) == 0 ? -half.Y : half.Y, (index & 4) == 0 ? -half.Z : half.Z);
        }
        else
        {
            float radius = Dimensions.X * 0.5f + 0.1f;
            for (int axis = 0; axis < 3; axis++)
                for (int segment = 0; segment < 64; segment++)
                {
                    lines.SurfaceAddVertex(Point(segment));
                    lines.SurfaceAddVertex(Point(segment + 1));
                    Vector3 Point(int i)
                    {
                        var point = Vector3.Zero;
                        point[(axis + 1) % 3] = Mathf.Cos(Mathf.Tau * i / 64) * radius;
                        point[(axis + 2) % 3] = Mathf.Sin(Mathf.Tau * i / 64) * radius;
                        return point;
                    }
                }
        }
        lines.SurfaceEnd();
        _outline.Mesh = lines;
    }
}
