using System;
using Godot;

namespace BoidsLab;

/// <summary>固定在世界坐标中的轴对齐障碍物；尺寸与节点缩放分开管理。</summary>
public partial class Obstacle2D : Node2D
{
    public const float MinSize = 16f;
    public const float MaxSize = 2000f;
    public static readonly Vector2 DefaultRectangleSize = new(160, 100);
    public static readonly Vector2 DefaultCircleSize = new(100, 100);

    public ObstacleShape Shape { get; private set; }
    public Vector2 Dimensions { get; private set; } = DefaultRectangleSize;
    public bool IsSelected { get; private set; }
    public Vector2 HandlePosition => Shape == ObstacleShape.Circle
        ? new Vector2(Dimensions.X * 0.5f, 0) : Dimensions * 0.5f;
    public Rect2 WorldRect => new(Position - Dimensions * 0.5f, Dimensions);

    public void Initialize(ObstacleShape shape, Vector2 dimensions)
    {
        if (!Enum.IsDefined(shape)) throw new ArgumentOutOfRangeException(nameof(shape));
        Shape = shape;
        SetDimensions(dimensions);
    }

    public void SetDimensions(Vector2 dimensions)
    {
        if (!dimensions.IsFinite()) throw new ArgumentOutOfRangeException(nameof(dimensions));
        float width = Mathf.Clamp(dimensions.X, MinSize, MaxSize);
        float height = Shape == ObstacleShape.Circle ? width : Mathf.Clamp(dimensions.Y, MinSize, MaxSize);
        Dimensions = new Vector2(width, height);
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
        return Shape == ObstacleShape.Circle
            ? local.LengthSquared() <= Dimensions.X * Dimensions.X * 0.25f
            : new Rect2(-Dimensions * 0.5f, Dimensions).HasPoint(local);
    }

    public bool HitResizeHandle(Vector2 viewportPosition) => IsSelected
        && (GetGlobalTransformWithCanvas() * HandlePosition).DistanceSquaredTo(viewportPosition) <= 100;

    public void RefreshVisual() => QueueRedraw();

    public override void _Draw()
    {
        float scale = GetGlobalTransformWithCanvas().X.Length();
        float unitsPerPixel = scale > 0 ? 1 / scale : 1;
        Color fill = IsSelected ? new Color("75603a") : new Color("3b4659");
        Color border = IsSelected ? new Color("ffe394") : new Color("91a6bb");
        float width = (IsSelected ? 3 : 2) * unitsPerPixel;
        if (Shape == ObstacleShape.Circle)
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
        if (IsSelected)
        {
            Vector2 handleSize = Vector2.One * 10 * unitsPerPixel;
            DrawRect(new Rect2(HandlePosition - handleSize * 0.5f, handleSize), new Color("fff4d2"));
        }
    }
}
