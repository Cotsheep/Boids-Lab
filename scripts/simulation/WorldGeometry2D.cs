using Godot;

namespace BoidsLab;

/// <summary>共用的世界几何：null 使用实际非周期坐标，有矩形时表示环绕区域；软边界转向由管理器独立施加。</summary>
public static class WorldGeometry2D
{
    public static Vector2 Wrap(Vector2 point, Rect2? bounds)
    {
        if (bounds is not { } rect || rect.HasPoint(point)) return point;
        return new Vector2(Mathf.Wrap(point.X, rect.Position.X, rect.End.X),
            Mathf.Wrap(point.Y, rect.Position.Y, rect.End.Y));
    }

    /// <summary>环绕模式下，输入点应已规范化到 bounds 内。</summary>
    public static Vector2 Offset(Vector2 from, Vector2 to, Rect2? bounds)
    {
        Vector2 offset = to - from;
        if (bounds is not { } rect) return offset;
        if (offset.X > rect.Size.X * 0.5f) offset.X -= rect.Size.X;
        else if (offset.X < -rect.Size.X * 0.5f) offset.X += rect.Size.X;
        if (offset.Y > rect.Size.Y * 0.5f) offset.Y -= rect.Size.Y;
        else if (offset.Y < -rect.Size.Y * 0.5f) offset.Y += rect.Size.Y;
        return offset;
    }
}
