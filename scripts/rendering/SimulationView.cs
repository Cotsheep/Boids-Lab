using Godot;

namespace BoidsLab;

/// <summary>只绘制背景、网格及调试辅助图形；个体外观由各自场景中的节点呈现。</summary>
public partial class SimulationView : Node2D
{
    public const float GridSpacing = 40;
    public DebugOverlayGeometry DebugGeometry { get; } = new();
    public LabPalette Palette { get; private set; } = LabPalette.Dark;
    public void ApplyPalette(LabPalette palette)
    {
        Palette = palette;
        if (_manager is not null)
        {
            foreach (Boid2D boid in _manager.Boids) boid.ApplyPalette(palette);
            _manager.GetNode<ObstacleEditor2D>("ObstacleEditor").ApplyPalette(palette);
        }
        QueueRedraw();
    }
    private BoidConfig? _config;
    private BoidManager? _manager;

    public void Bind(BoidConfig config, BoidManager manager)
    {
        _config = config;
        _manager = manager;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_manager is null) return;
        DrawRect(_manager.ViewBounds, Palette.Canvas);
        if (_config?.ShowGrid == true) DrawGrid();
        DrawDebugOverlays();
        DrawLeaderMarkers();
    }

    private void DrawLeaderMarkers()
    {
        if (_manager?.Leader is not { } leader) return;
        float unitsPerPixel = 1 / Mathf.Max(0.001f, GetGlobalTransformWithCanvas().X.Length());
        float radius = Mathf.Max(18, 18 * unitsPerPixel);
        Vector2 center = leader.Position;
        // 紫色菱形表示持久的领队身份；金色选择圆环仍表示当前普通选择。
        Vector2[] diamond = { center + Vector2.Up * radius, center + Vector2.Right * radius,
            center + Vector2.Down * radius, center + Vector2.Left * radius, center + Vector2.Up * radius };
        DrawPolyline(diamond, Palette.Leader, 2 * unitsPerPixel, true);
        if (_manager.LeaderTarget is not { } target) return;
        float arm = 10 * unitsPerPixel;
        DrawArc(target, 6 * unitsPerPixel, 0, Mathf.Tau, 32, Palette.Leader, 2 * unitsPerPixel, true);
        DrawLine(target - Vector2.Right * arm, target + Vector2.Right * arm, Palette.Leader, unitsPerPixel);
        DrawLine(target - Vector2.Down * arm, target + Vector2.Down * arm, Palette.Leader, unitsPerPixel);
    }

    private void DrawGrid()
    {
        var color = Palette.Grid;
        Rect2 bounds = _manager!.ViewBounds;
        // 可见区域移动时，网格仍固定在世界坐标的整倍数上。
        double firstX = System.Math.Ceiling(bounds.Position.X / (double)GridSpacing) * GridSpacing;
        double firstY = System.Math.Ceiling(bounds.Position.Y / (double)GridSpacing) * GridSpacing;
        // 循环次数只取决于视口大小，避免远处 float 加法不再增长而陷入死循环。
        for (int i = 0; i <= Mathf.CeilToInt(bounds.Size.X / GridSpacing); i++)
        {
            float x = (float)(firstX + i * (double)GridSpacing);
            if (x <= bounds.End.X) DrawLine(new Vector2(x, bounds.Position.Y), new Vector2(x, bounds.End.Y), color);
        }
        for (int i = 0; i <= Mathf.CeilToInt(bounds.Size.Y / GridSpacing); i++)
        {
            float y = (float)(firstY + i * (double)GridSpacing);
            if (y <= bounds.End.Y) DrawLine(new Vector2(bounds.Position.X, y), new Vector2(bounds.End.X, y), color);
        }
    }

    private void DrawDebugOverlays()
    {
        if (_config is null || _manager is null) return;
        float scale = GetGlobalTransformWithCanvas().X.Length();
        if (scale <= 0) return;
        float unitsPerPixel = 1 / scale;
        DebugGeometry.Build(_manager.Boids, _manager.SelectedBoid, _config, _manager.ViewBounds, unitsPerPixel, _manager.WrapBounds is not null);
        if (DebugGeometry.RangePoints.Count > 0)
            DrawMultiline(DebugGeometry.RangePoints.ToArray(), Palette.Perception, 3 * unitsPerPixel, true);
        if (DebugGeometry.SeparationRangePoints.Count > 0)
            DrawMultiline(DebugGeometry.SeparationRangePoints.ToArray(), Palette.Separation, 2 * unitsPerPixel, true);
        if (DebugGeometry.NeighborPoints.Count > 0)
            DrawMultiline(DebugGeometry.NeighborPoints.ToArray(), Palette.Selection, 1.5f * unitsPerPixel, true);
        if (DebugGeometry.VelocityPoints.Count > 0)
            DrawMultiline(DebugGeometry.VelocityPoints.ToArray(), Palette.Velocity, unitsPerPixel, true);
        if (_manager.SelectedBoid is { } selected)
            DrawArc(selected.Position, Mathf.Max(14, 11 * unitsPerPixel), 0, Mathf.Tau, 64,
                Palette.Selection, 2 * unitsPerPixel, true);
    }
}
