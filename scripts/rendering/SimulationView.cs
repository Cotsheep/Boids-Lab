using Godot;

namespace BoidsLab;

/// <summary>只绘制背景、网格及调试辅助图形；个体外观由各自场景中的节点呈现。</summary>
public partial class SimulationView : Node2D
{
    public const float GridSpacing = 40;
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
        // TODO: 读取 _manager.Boids 中个体的真实状态。
        // ShowVelocity -> 速度向量；ShowPerceptionRadius -> 感知圆。
        // ShowNeighbors -> 邻居关系线；可读取 _manager.NeighborSearch 的步前快照与结果。
        // 这些开关目前只保存配置，不能产生虚构可视化。
    }
}
