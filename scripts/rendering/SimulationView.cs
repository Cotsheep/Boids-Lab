using Godot;

namespace BoidsLab;

/// <summary>只绘制背景、网格及调试辅助图形；个体外观由各自场景中的节点呈现。</summary>
public partial class SimulationView : Node2D
{
    public const float GridSpacing = 40;
    public DebugOverlayGeometry DebugGeometry { get; } = new();
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
        DrawRect(_manager.WorldBounds, new Color("0c1420"));
        if (_config?.ShowGrid == true) DrawGrid();
        DrawDebugOverlays();
    }

    private void DrawGrid()
    {
        var color = new Color("1c2b3b");
        Rect2 bounds = _manager!.WorldBounds;
        // 可见区域移动时，网格仍固定在世界坐标的整倍数上。
        float firstX = Mathf.Ceil(bounds.Position.X / GridSpacing) * GridSpacing;
        float firstY = Mathf.Ceil(bounds.Position.Y / GridSpacing) * GridSpacing;
        for (float x = firstX; x < bounds.End.X; x += GridSpacing)
            DrawLine(new Vector2(x, bounds.Position.Y), new Vector2(x, bounds.End.Y), color);
        for (float y = firstY; y < bounds.End.Y; y += GridSpacing)
            DrawLine(new Vector2(bounds.Position.X, y), new Vector2(bounds.End.X, y), color);
    }

    private void DrawDebugOverlays()
    {
        if (_config is null || _manager is null) return;
        float scale = GetGlobalTransformWithCanvas().X.Length();
        if (scale <= 0) return;
        float unitsPerPixel = 1 / scale;
        DebugGeometry.Build(_manager.Boids, _manager.SelectedBoid, _config, _manager.WorldBounds, unitsPerPixel);
        if (DebugGeometry.RangePoints.Count > 0)
            DrawMultiline(DebugGeometry.RangePoints.ToArray(), new Color(0.3f, 0.8f, 0.7f, 0.75f), 3 * unitsPerPixel, true);
        if (DebugGeometry.SeparationRangePoints.Count > 0)
            DrawMultiline(DebugGeometry.SeparationRangePoints.ToArray(), new Color(1f, 0.48f, 0.25f, 0.9f), 2 * unitsPerPixel, true);
        if (DebugGeometry.NeighborPoints.Count > 0)
            DrawMultiline(DebugGeometry.NeighborPoints.ToArray(), new Color(1f, 0.78f, 0.3f, 0.85f), 1.5f * unitsPerPixel, true);
        if (DebugGeometry.VelocityPoints.Count > 0)
            DrawMultiline(DebugGeometry.VelocityPoints.ToArray(), new Color(0.4f, 0.7f, 1f, 0.8f), unitsPerPixel, true);
        if (_manager.SelectedBoid is { } selected)
            DrawArc(selected.Position, Mathf.Max(14, 11 * unitsPerPixel), 0, Mathf.Tau, 64,
                new Color("ffe394"), 2 * unitsPerPixel, true);
    }
}
