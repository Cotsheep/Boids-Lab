using System;
using Godot;

namespace BoidsLab;

/// <summary>障碍外观与可组合碰撞资源；物理对象由世界查询服务统一持有。</summary>
public partial class Obstacle2D : Node2D
{
    public const float MinSize = ObstacleSize.Min;
    public const float MaxSize = ObstacleSize.Max;
    private LabPalette _palette = LabPalette.Dark;
    internal void ApplyPalette(LabPalette palette) { _palette = palette; QueueRedraw(); }
    public static readonly Vector2 DefaultRectangleSize = new(160, 100);
    public static readonly Vector2 DefaultCircleSize = new(100, 100);

    public ObstacleShape Shape { get; private set; }
    public Vector2 Dimensions { get; private set; } = DefaultRectangleSize;
    public bool IsSelected { get; private set; }
    [Export] public Godot.Collections.Array<ObstaclePart2D> CollisionParts { get; set; } = new();
    [Export] public Vector2[] SolidPolygon { get; set; } = Array.Empty<Vector2>();
    public bool IsCustom => CollisionParts.Count > 0 || SolidPolygon.Length > 0;
    private readonly Godot.Collections.Array<ObstaclePart2D> _primitive = new();
    private Vector2[] _decomposedPolygon = Array.Empty<Vector2>();
    internal Godot.Collections.Array<ObstaclePart2D> Parts
    {
        get
        {
            if (SolidPolygon.Length > 0 && !System.Linq.Enumerable.SequenceEqual(SolidPolygon, _decomposedPolygon))
            {
                CollisionParts.Clear();
                foreach (Vector2[] convex in Geometry2D.DecomposePolygonInConvex(SolidPolygon))
                    CollisionParts.Add(new ObstaclePart2D { Shape = new ConvexPolygonShape2D { Points = convex } });
                if (CollisionParts.Count == 0) throw new InvalidOperationException("SolidPolygon 必须是至少三个顶点、不自交的实心轮廓。");
                _decomposedPolygon = (Vector2[])SolidPolygon.Clone();
            }
            else if (SolidPolygon.Length == 0 && _decomposedPolygon.Length > 0)
            {
                CollisionParts.Clear();
                _decomposedPolygon = Array.Empty<Vector2>();
            }
            return CollisionParts.Count > 0 ? CollisionParts : _primitive;
        }
    }
    public Vector2 HandlePosition => Shape == ObstacleShape.Circle
        ? new Vector2(Dimensions.X * 0.5f, 0) : Dimensions * 0.5f;

    public void Initialize(ObstacleShape shape, Vector2 dimensions)
    {
        if (shape is not (ObstacleShape.Rectangle or ObstacleShape.Circle)) throw new ArgumentOutOfRangeException(nameof(shape));
        Shape = shape;
        SetDimensions(dimensions);
    }

    public void SetDimensions(Vector2 dimensions)
    {
        if (!dimensions.IsFinite()) throw new ArgumentOutOfRangeException(nameof(dimensions));
        float width = Mathf.Clamp(dimensions.X, MinSize, MaxSize);
        float height = Shape == ObstacleShape.Circle ? width : Mathf.Clamp(dimensions.Y, MinSize, MaxSize);
        Dimensions = new Vector2(width, height);
        _primitive.Clear();
        _primitive.Add(new ObstaclePart2D { Shape = Shape == ObstacleShape.Circle
            ? new CircleShape2D { Radius = width * 0.5f } : new RectangleShape2D { Size = Dimensions } });
        QueueRedraw();
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        QueueRedraw();
    }

    public bool HitTest(Vector2 viewportPosition)
    {
        Vector2 local = GetGlobalTransformWithCanvas().AffineInverse() * viewportPosition;
        if (SolidPolygon.Length > 0) return Geometry2D.IsPointInPolygon(local, SolidPolygon);
        if (IsCustom) return false; // 资源部件拾取通过编辑器的统一点查询。
        return Shape == ObstacleShape.Circle
            ? local.LengthSquared() <= Dimensions.X * Dimensions.X * 0.25f
            : new Rect2(-Dimensions * 0.5f, Dimensions).HasPoint(local);
    }

    public bool HitResizeHandle(Vector2 viewportPosition) => IsSelected && !IsCustom
        && (GetGlobalTransformWithCanvas() * HandlePosition).DistanceSquaredTo(viewportPosition) <= 100;

    public void RefreshVisual() => QueueRedraw();

    public override void _Draw()
    {
        float scale = GetGlobalTransformWithCanvas().X.Length();
        float unitsPerPixel = scale > 0 ? 1 / scale : 1;
        Color fill = IsSelected ? _palette.ObstacleSelected : _palette.Obstacle;
        Color border = IsSelected ? _palette.Selection : _palette.ObstacleOutline;
        float width = (IsSelected ? 3 : 2) * unitsPerPixel;
        if (SolidPolygon.Length > 0)
        {
            DrawColoredPolygon(SolidPolygon, fill);
            var outline = new Vector2[SolidPolygon.Length + 1];
            SolidPolygon.CopyTo(outline, 0);
            outline[^1] = outline[0];
            DrawPolyline(outline, border, width, true);
        }
        else if (IsCustom)
        {
            foreach (var part in Parts)
            {
                DrawSetTransformMatrix(part.Transform);
                part.Shape.Draw(GetCanvasItem(), fill);
            }
            DrawSetTransformMatrix(Transform2D.Identity);
        }
        else if (Shape == ObstacleShape.Circle)
        {
            DrawCircle(Vector2.Zero, Dimensions.X * 0.5f, fill);
            DrawArc(Vector2.Zero, Dimensions.X * 0.5f, 0, Mathf.Tau, 96, border, width, true);
        }
        else
        {
            var rect = new Rect2(-Dimensions * 0.5f, Dimensions);
            DrawRect(rect, fill);
            DrawRect(rect, border, false, width);
        }
        if (IsSelected && !IsCustom)
        {
            Vector2 handleSize = Vector2.One * 10 * unitsPerPixel;
            DrawRect(new Rect2(HandlePosition - handleSize * 0.5f, handleSize), _palette.Selection);
        }
    }
}
