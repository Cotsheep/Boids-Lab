using Godot;

namespace BoidsLab;

/// <summary>只绘制背景、网格及调试辅助图形；个体外观由各自场景中的节点呈现。</summary>
public partial class SimulationView : Node2D
{
    public const float GridSpacing = 40;
    internal readonly record struct Marker(Vector2 Center, float Radius, Color Color, float Width);
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
        DrawLeaderFollowRegion();
        DrawDebugOverlays();
        DrawAvoidance();
        DrawLeaderMarkers();
    }

    internal (Vector2 Center, float Radius)? FollowRegion
    {
        get
        {
            if (_config?.ShowLeaderFollowRegion != true || _manager?.Leader is not { } leader
                || _config.LeaderFollowWeight <= 0 || _manager.Boids.Count <= 2) return null;
            float radius = LeaderSteering.FollowRadius(_manager.Boids.Count - 1, _config);
            return (LeaderSteering.FollowPoint(leader.Position, _manager.LeaderFollowHeading,
                _config.LeaderFollowDistance + radius), radius);
        }
    }

    private void DrawLeaderFollowRegion()
    {
        if (FollowRegion is not { } region) return;
        Transform2D coordinates = GlobalTransform.AffineInverse() * _manager!.GetNode<Node2D>("Boids").GlobalTransform;
        float scale = (GetGlobalTransformWithCanvas() * coordinates).X.Length();
        float unitsPerPixel = 1 / Mathf.Max(scale, 0.001f);
        DrawSetTransformMatrix(coordinates);
        DrawCircle(region.Center, region.Radius, new Color(Palette.Leader, Palette.IsLight ? 0.11f : 0.085f));
        DrawArc(region.Center, region.Radius, 0, Mathf.Tau, 128,
            new Color(Palette.Leader, Palette.IsLight ? 0.1f : 0.14f), 5 * unitsPerPixel, true);
        DrawArc(region.Center, region.Radius, 0, Mathf.Tau, 128,
            new Color(Palette.Leader, Palette.IsLight ? 0.9f : 0.82f), 1.5f * unitsPerPixel, true);
        DrawCircle(region.Center, 2 * unitsPerPixel, new Color(Palette.Leader, 0.95f), true, -1, true);
        DrawSetTransformMatrix(Transform2D.Identity);
    }

    private void DrawAvoidance()
    {
        if (_manager?.SelectedBoid is not { } boid || _config?.ShowVelocity != true) return;
        var debug = _manager.AvoidanceDebug;
        if (debug.BoidId != boid.Id) return;
        Vector2 origin = boid.Position;
        foreach (var candidate in debug.Candidates)
            DrawLine(origin, origin + candidate.Motion, candidate.Blocked ? Palette.ObstacleOutline : Palette.Perception, 1, true);
        if (debug.Probe.LengthSquared() > 0) DrawLine(origin, origin + debug.Probe, Palette.Velocity, 2, true);
        if (debug.Choice.LengthSquared() > 0) DrawLine(origin, origin + debug.Choice, Palette.Selection, 3, true);
        foreach (Vector2 normal in debug.Normals) DrawLine(origin, origin + normal * 30, Palette.Separation, 2, true);
    }

    private void DrawLeaderMarkers()
    {
        DrawMarker(LeaderMarker);
        if (!HasSeparateLeaderTarget || _manager?.LeaderTarget is not { } target) return;
        float unitsPerPixel = UnitsPerPixel;
        float arm = 10 * unitsPerPixel;
        DrawArc(target, 6 * unitsPerPixel, 0, Mathf.Tau, 32, Palette.Leader, 2 * unitsPerPixel, true);
        DrawLine(target - Vector2.Right * arm, target + Vector2.Right * arm, Palette.Leader, unitsPerPixel);
        DrawLine(target - Vector2.Down * arm, target + Vector2.Down * arm, Palette.Leader, unitsPerPixel);
    }

    private float UnitsPerPixel => 1 / Mathf.Max(0.001f, GetGlobalTransformWithCanvas().X.Length());

    internal Marker? SelectionMarker => _manager?.SelectedBoid is { } selected && selected != _manager.Leader
        ? new Marker(selected.Position, Mathf.Max(14, 11 * UnitsPerPixel), Palette.Selection, 2 * UnitsPerPixel) : null;

    internal Marker? LeaderMarker => _manager?.Leader is { } leader
        ? new Marker(leader.Position, Mathf.Max(18, 18 * UnitsPerPixel),
            leader.IsSelected ? Palette.SelectedLeader : Palette.Leader, (leader.IsSelected ? 2.5f : 1.5f) * UnitsPerPixel) : null;

    internal bool HasSeparateLeaderTarget => LeaderMarker is { } marker && _manager?.LeaderTarget is { } target
        && target.DistanceSquaredTo(marker.Center) > Mathf.Pow(marker.Radius + 12 * UnitsPerPixel, 2);

    private void DrawMarker(Marker? marker)
    {
        if (marker is { } value)
            DrawArc(value.Center, value.Radius, 0, Mathf.Tau, 96, value.Color, value.Width, true);
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
        DrawMarker(SelectionMarker);
    }
}
