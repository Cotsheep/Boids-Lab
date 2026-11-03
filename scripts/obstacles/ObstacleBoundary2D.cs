using Godot;

namespace BoidsLab;

/// <summary>二维观察模式的边界适配；前瞻截断、实际位移拆段。</summary>
public static class ObstacleBoundary2D
{
    public static void BoundaryFraction(Vector2 origin, Vector2 movement, Rect2? bounds,
        out float fraction, out bool crossX, out bool crossY)
    {
        fraction = 1;
        crossX = crossY = false;
        if (bounds is not { } rect) return;
        float x = movement.X > 0 ? (rect.End.X - origin.X) / movement.X
            : movement.X < 0 ? (rect.Position.X - origin.X) / movement.X : float.PositiveInfinity;
        float y = movement.Y > 0 ? (rect.End.Y - origin.Y) / movement.Y
            : movement.Y < 0 ? (rect.Position.Y - origin.Y) / movement.Y : float.PositiveInfinity;
        fraction = Mathf.Clamp(Mathf.Min(1, Mathf.Min(x, y)), 0, 1);
        crossX = x <= 1 && x <= y;
        crossY = y <= 1 && y <= x;
    }
}
