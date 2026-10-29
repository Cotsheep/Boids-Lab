using System.Globalization;
using Godot;

namespace BoidsLab;

public partial class StatsPanel : HFlowContainer
{
    private Label _count = null!;
    private Label _fps = null!;
    private Label _time = null!;
    private Label _searchTime = null!;
    private Label _checks = null!;
    private Label _neighbors = null!;

    public override void _Ready()
    {
        _count = AddMetric("BoidCount", "实际个体数量");
        _fps = AddMetric("Fps", "帧率（帧/秒）");
        _time = AddMetric("UpdateTime", "模拟更新耗时（毫秒）");
        _time.TooltipText = "最近一次物理步中当前世界已实现的模拟计算总耗时；不含绘制与界面刷新。未实现或尚未测量的指标显示为 --。";
        _searchTime = AddMetric("SearchTime", "邻居搜索耗时（毫秒）");
        _searchTime.TooltipText = "最近一次搜索的实测耗时，包含状态快照、网格构建（网格模式）、距离判定和结果写入。";
        _checks = AddMetric("NeighborChecks", "邻居候选检查次数");
        _checks.TooltipText = "每步实际距离检查次数，不含自身；甲检查乙和乙检查甲各计一次。";
        _neighbors = AddMetric("AverageNeighbors", "平均邻居数量");
        _neighbors.TooltipText = "每个个体在感知半径内的邻居数量的平均值；空群体测量后为零。";
    }

    public void Refresh(SimulationStats stats, double fps)
    {
        _count.Text = stats.BoidCount?.ToString(CultureInfo.InvariantCulture) ?? "--";
        _fps.Text = fps > 0 ? fps.ToString("0", CultureInfo.InvariantCulture) : "--";
        _time.Text = stats.UpdateMilliseconds?.ToString("0.000", CultureInfo.InvariantCulture) ?? "--";
        _searchTime.Text = stats.NeighborSearchMilliseconds?.ToString("0.000", CultureInfo.InvariantCulture) ?? "--";
        _checks.Text = stats.NeighborCandidateChecks?.ToString(CultureInfo.InvariantCulture) ?? "--";
        _neighbors.Text = stats.AverageNeighbors?.ToString("0.00", CultureInfo.InvariantCulture) ?? "--";
    }

    private Label AddMetric(string name, string title)
    {
        var cell = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(165, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var heading = new Label { Text = title };
        heading.AddThemeColorOverride("font_color", new Color("91a6bb"));
        heading.AddThemeFontSizeOverride("font_size", 12);
        var value = new Label { Name = name, Text = "--" };
        value.AddThemeFontSizeOverride("font_size", 23);
        cell.AddChild(heading);
        cell.AddChild(value);
        AddChild(cell);
        return value;
    }
}
