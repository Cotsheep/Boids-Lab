using System;
using Godot;

namespace BoidsLab;

public partial class ControlPanel : VBoxContainer
{
    public event Action? ToggleRunningRequested;
    public event Action? ResetRequested;
    public event Action? StepRequested;
    public event Action? ConfigurationChanged;

    private Button _startPause = null!;
    private Button _step = null!;

    public void Bind(BoidConfig config, string lengthUnit)
    {
        // 主控制器在 _Ready 中调用一次；控件的值、范围均来自共享配置。
        AddHeading("SIMULATION");
        var buttons = new HBoxContainer();
        AddChild(buttons);
        _startPause = AddButton(buttons, "StartPause", "Start", () => ToggleRunningRequested?.Invoke());
        AddButton(buttons, "Reset", "Reset", () => ResetRequested?.Invoke())
            .TooltipText = "Clear simulation state and pause. Keep current configuration.";
        _step = AddButton(buttons, "Step", "Step", () => StepRequested?.Invoke());
        _step.Disabled = true;
        _step.TooltipText = "Unavailable: simulation core is not implemented.";
        AddNote("Lifecycle controls only. Simulation core is not implemented.");

        AddHeading("POPULATION");
        AddNumber("BoidCount", "Boid Count", BoidConfig.CountRange, config.BoidCount,
            value => config.BoidCount = (int)value, "Target count; no Boids are spawned yet.");
        AddNumber("RandomSeed", "Random Seed", BoidConfig.SeedRange, config.RandomSeed,
            value => config.RandomSeed = (int)value, "Reserved for reproducible initialization on Reset.");

        AddHeading("MOTION PARAMETERS");
        AddNumber("PerceptionRadius", "Perception Radius", BoidConfig.RadiusRange, config.PerceptionRadius,
            value => config.PerceptionRadius = (float)value, $"{lengthUnit} · configuration only");
        AddNumber("MaxSpeed", "Max Speed", BoidConfig.SpeedRange, config.MaxSpeed,
            value => config.MaxSpeed = (float)value, $"{lengthUnit}/s · configuration only");
        AddNumber("MaxForce", "Max Force", BoidConfig.ForceRange, config.MaxForce,
            value => config.MaxForce = (float)value, $"Acceleration limit, {lengthUnit}/s² · configuration only");

        AddHeading("STEERING RULES");
        AddToggle("SeparationEnabled", "Separation", config.SeparationEnabled, value => config.SeparationEnabled = value);
        AddNumber("SeparationWeight", "Separation Weight", BoidConfig.SeparationRange, config.SeparationWeight,
            value => config.SeparationWeight = (float)value);
        AddToggle("AlignmentEnabled", "Alignment", config.AlignmentEnabled, value => config.AlignmentEnabled = value);
        AddNumber("AlignmentWeight", "Alignment Weight", BoidConfig.AlignmentRange, config.AlignmentWeight,
            value => config.AlignmentWeight = (float)value);
        AddToggle("CohesionEnabled", "Cohesion", config.CohesionEnabled, value => config.CohesionEnabled = value);
        AddNumber("CohesionWeight", "Cohesion Weight", BoidConfig.CohesionRange, config.CohesionWeight,
            value => config.CohesionWeight = (float)value);
        AddNote("Rules and weights are saved; steering behavior is TODO.");

        AddHeading("DEBUG VISUALIZATION");
        AddToggle("ShowGrid", "Show Grid", config.ShowGrid, value => config.ShowGrid = value);
        AddToggle("ShowNeighbors", "Show Neighbors  [TODO]", config.ShowNeighbors, value => config.ShowNeighbors = value);
        AddToggle("ShowPerceptionRadius", "Show Perception Radius  [TODO]", config.ShowPerceptionRadius, value => config.ShowPerceptionRadius = value);
        AddToggle("ShowVelocity", "Show Velocity  [TODO]", config.ShowVelocity, value => config.ShowVelocity = value);
        AddNote("Grid is available. TODO overlays only save their switch state.");
    }

    public void RefreshRunState(bool running, bool canStep)
    {
        _startPause.Text = running ? "Pause" : "Start";
        _step.Disabled = !canStep;
    }

    private static Button AddButton(HBoxContainer row, string name, string text, Action action)
    {
        var button = new Button { Name = name, Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        button.Pressed += action;
        row.AddChild(button);
        return button;
    }

    private void AddHeading(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", new Color("78dcca"));
        label.AddThemeFontSizeOverride("font_size", 13);
        AddChild(label);
    }

    private void AddNote(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", new Color("91a6bb"));
        label.AddThemeFontSizeOverride("font_size", 13);
        AddChild(label);
    }

    private void AddNumber(string name, string title, BoidConfig.NumberRange range, double current,
        Action<double> setter, string tooltip = "Configuration only; algorithm is TODO.")
    {
        var row = new HBoxContainer { TooltipText = tooltip };
        row.AddChild(new Label { Text = title, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var input = new SpinBox
        {
            Name = name, MinValue = range.Min, MaxValue = range.Max, Step = range.Step,
            Value = current, CustomMinimumSize = new Vector2(128, 34), TooltipText = tooltip,
        };
        input.ValueChanged += value =>
        {
            setter(range.Constrain(value));
            ConfigurationChanged?.Invoke();
        };
        row.AddChild(input);
        AddChild(row);
    }

    private void AddToggle(string name, string title, bool current, Action<bool> setter)
    {
        var input = new CheckBox { Name = name, Text = title, ButtonPressed = current };
        input.Toggled += value =>
        {
            setter(value);
            ConfigurationChanged?.Invoke();
        };
        AddChild(input);
    }
}
