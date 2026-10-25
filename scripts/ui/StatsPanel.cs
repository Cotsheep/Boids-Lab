using System.Globalization;
using Godot;

namespace BoidsLab;

public partial class StatsPanel : HFlowContainer
{
    private Label _count = null!;
    private Label _fps = null!;
    private Label _time = null!;
    private Label _checks = null!;
    private Label _neighbors = null!;

    public override void _Ready()
    {
        _count = AddMetric("BoidCount", "BOIDS · ACTUAL");
        _fps = AddMetric("Fps", "FPS · APPLICATION");
        _time = AddMetric("UpdateTime", "SIM UPDATE · ms");
        _checks = AddMetric("NeighborChecks", "CANDIDATE CHECKS");
        _neighbors = AddMetric("AverageNeighbors", "AVG NEIGHBORS");
    }

    public void Refresh(SimulationStats stats, double fps)
    {
        _count.Text = stats.BoidCount?.ToString(CultureInfo.InvariantCulture) ?? "--";
        _fps.Text = fps > 0 ? fps.ToString("0", CultureInfo.InvariantCulture) : "--";
        _time.Text = stats.UpdateMilliseconds?.ToString("0.000", CultureInfo.InvariantCulture) ?? "--";
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
