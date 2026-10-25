using Godot;

namespace BoidsLab;

/// <summary>2D 世界中的绘制节点；使用世界坐标，不引用 UI 或面板尺寸。</summary>
public partial class SimulationView : Node2D
{
    public const float GridSpacing = 40;
    private BoidConfig? _config;
    private BoidSimulation? _simulation;

    public void Bind(BoidConfig config, BoidSimulation simulation)
    {
        _config = config;
        _simulation = simulation;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_simulation is null) return;
        DrawRect(_simulation.WorldBounds, new Color("0c1420"));
        if (_config?.ShowGrid == true) DrawGrid();
        DrawBoids();
        DrawDebugOverlays();
    }

    private void DrawGrid()
    {
        var color = new Color("1c2b3b");
        Rect2 bounds = _simulation!.WorldBounds;
        for (float x = bounds.Position.X; x < bounds.End.X; x += GridSpacing)
            DrawLine(new Vector2(x, bounds.Position.Y), new Vector2(x, bounds.End.Y), color);
        for (float y = bounds.Position.Y; y < bounds.End.Y; y += GridSpacing)
            DrawLine(new Vector2(bounds.Position.X, y), new Vector2(bounds.End.X, y), color);
    }

    private void DrawBoids()
    {
        if (_simulation is null) return;
        // TODO: 读取 _simulation.States，按 Position / Velocity 绘制三角形。
    }

    private void DrawDebugOverlays()
    {
        if (_config is null || _simulation is null) return;
        // TODO: ShowVelocity -> 速度向量。
        // TODO: ShowPerceptionRadius -> 感知圆。
        // TODO: ShowNeighbors -> 邻居关系线；等待核心提供真实邻居数据。
        // 这些开关目前只保存配置，不能产生虚构可视化。
    }
}
