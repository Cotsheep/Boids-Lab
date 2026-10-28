using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace BoidsLab.Tests;

public partial class DebugChecks : Node
{
    private int _checks;
    private BoidManager _manager = null!;
    private SimulationCamera2D _camera = null!;
    private SimulationView _view = null!;

    public override async void _Ready()
    {
        if (DisplayServer.GetName() != "headless")
        {
            GD.PushError("Run debug_checks.tscn with --headless.");
            GetTree().Quit(2);
            return;
        }
        try
        {
            CheckGeometry();
            CheckDebugOptions();
            await CheckInteraction();
            GD.Print($"PASS: {_checks} selection/debug geometry/input assertions (headless).");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private async Task Frame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Pointer(Vector2 position, Vector2 relative = default, bool held = false) =>
        GetViewport().PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position,
            Relative = relative, ButtonMask = held ? MouseButtonMask.Left : 0 }, true);

    private void Button(Vector2 position, bool pressed) =>
        GetViewport().PushInput(new InputEventMouseButton { Position = position, GlobalPosition = position,
            ButtonIndex = MouseButton.Left, Pressed = pressed, ButtonMask = pressed ? MouseButtonMask.Left : 0 }, true);

    private async Task Click(Vector2 position)
    {
        Pointer(position);
        Button(position, true);
        Button(position, false);
        await Frame();
    }

    private static Vector2 Screen(Boid2D boid) => boid.GetGlobalTransformWithCanvas() * Vector2.Zero;

    private async Task CheckInteraction()
    {
        // 与 MainController 一致；只调整无窗口视口，不创建原生窗口。
        GetWindow().MinSize = new Vector2I(960, 640);
        var config = new BoidConfig { BoidCount = 0, MaxSpeed = 120, PerceptionRadius = 100,
            SeparationEnabled = false, AlignmentEnabled = false, CohesionEnabled = false,
            ShowNeighbors = true, ShowPerceptionRadius = true, ShowSeparationRadius = true, ShowVelocity = true };
        _manager = GD.Load<PackedScene>("res://scenes/worlds/boids_world_2d.tscn").Instantiate<BoidManager>();
        _manager.Initialize(config);
        AddChild(_manager);
        _camera = _manager.GetNode<SimulationCamera2D>("Camera2D");
        _view = _manager.GetNode<SimulationView>("SimulationView");
        await Frame();
        GetViewport().NotifyMouseEntered();
        Vector2 origin = _manager.GetGlobalTransformWithCanvas().AffineInverse()
            * (GetViewport().GetVisibleRect().Size * new Vector2(0.65f, 0.5f));
        Boid2D a = _manager.SpawnBoid(origin, new Vector2(120, 0));
        Boid2D b = _manager.SpawnBoid(origin + new Vector2(45, 0), new Vector2(0, 60));
        Color originalColor = a.GetNode<Polygon2D>("Body").Color;
        SimulationStats unmeasured = _manager.Stats;
        await Click(Screen(a));
        Check(_manager.SelectedBoid == a && a.IsSelected, "Paused click selects one individual before any simulation step");
        Check(a.GetNode<Polygon2D>("Body").Color != originalColor, "Selected body is highlighted");
        Check(_view.DebugGeometry.PerceptionCount == 1 && _view.DebugGeometry.SeparationCount == 1 && _view.DebugGeometry.VelocityCount == 2
            && _view.DebugGeometry.NeighborCount == 1, "Native draw builds selected range/links and all velocity arrows");
        Check(_manager.Stats == unmeasured && !_manager.NeighborSearch.HasResults, "Paused debug drawing leaves simulation metrics unmeasured");
        await Click(Screen(b));
        Check(_manager.SelectedBoid == b && !a.IsSelected && b.IsSelected
            && _manager.Boids.Count(x => x.IsSelected) == 1, "Selecting another individual replaces the old selection");
        Check(a.GetNode<Polygon2D>("Body").Color == originalColor, "Deselection restores original custom body color");
        await Click(Screen(b));
        Check(_manager.SelectedBoid is null && _view.DebugGeometry.NeighborPoints.Count == 0, "Clicking selected individual again clears selection and links");
        Check(_view.DebugGeometry.PerceptionCount == 0 && _view.DebugGeometry.RangePoints.Count == 0
            && _view.DebugGeometry.SeparationCount == 0 && _view.DebugGeometry.SeparationRangePoints.Count == 0
            && _view.DebugGeometry.VelocityCount == 2, "Deselection removes perception range while retaining all directions");

        Vector2 press = Screen(a);
        Pointer(press);
        Button(press, true);
        _manager.SetRunning(true);
        _manager._PhysicsProcess(0.5);
        Check(Screen(a).DistanceTo(press) > 10, "Moving selection probe leaves original pointer position");
        Button(press, false);
        _manager.SetRunning(false);
        await Frame();
        Check(_manager.SelectedBoid == a, "Running click selects the individual hit on press, even after it moved");
        SimulationStats measured = _manager.Stats;
        b.Position = a.Position + new Vector2(300, 0);
        _manager.ConfigurationChanged();
        await Frame();
        Check(_view.DebugGeometry.NeighborCount == 0 && _manager.Stats == measured,
            "Links query current positions without overwriting the last physics snapshot/statistics");
        b.Position = a.Position + new Vector2(45, 0);
        config.ShowNeighbors = false;
        _manager.ConfigurationChanged();
        await Frame();
        Check(_manager.SelectedBoid == a && _view.DebugGeometry.NeighborPoints.Count == 0, "Hiding links preserves selection");
        config.ShowNeighbors = true;
        config.ShowPerceptionRadius = config.ShowSeparationRadius = config.ShowVelocity = false;
        _manager.ConfigurationChanged();
        await Frame();
        Check(_view.DebugGeometry.NeighborCount == 1 && _view.DebugGeometry.RangePoints.Count == 0
            && _view.DebugGeometry.SeparationRangePoints.Count == 0 && _view.DebugGeometry.VelocityPoints.Count == 0,
            "Debug switches act independently while paused");

        Vector2 cameraBefore = _camera.Position;
        press = Screen(b);
        Pointer(press);
        Button(press, true);
        Pointer(press + new Vector2(2, 0), new Vector2(2, 0), true);
        Check(_camera.Position == cameraBefore, "Small pointer jitter does not pan camera");
        Button(press + new Vector2(2, 0), false);
        await Frame();
        Check(_manager.SelectedBoid == b, "Sub-threshold gesture is a click");
        press = Screen(a);
        Pointer(press);
        Button(press, true);
        Vector2 drag = new(30, 15);
        Pointer(press + drag, drag, true);
        Button(press + drag, false);
        await Frame();
        Check(_manager.SelectedBoid == b, "Dragging over a Boid does not change selection");
        Check(_camera.Position.DistanceTo(cameraBefore - drag / _camera.Zoom) < 0.001f, "Drag preserves full accumulated motion");
        press = Screen(a);
        Pointer(press);
        Button(press, true);
        GetWindow().EmitSignal(Window.SignalName.FocusExited);
        Button(press, false);
        await Frame();
        Check(_manager.SelectedBoid == b, "Focus loss cancels pending selection");

        Vector2 originalB = b.Position;
        b.Position = a.Position;
        await Click(Screen(b));
        Check(_manager.SelectedBoid is null, "Clicking an overlapping selected individual can still deselect it");
        b.Position = originalB;
        await Click(Screen(b));
        Boid2D pending = _manager.SpawnBoid(_manager.GetGlobalTransformWithCanvas().AffineInverse()
            * (GetViewport().GetVisibleRect().Size * new Vector2(0.8f, 0.25f)), Vector2.Zero);
        press = Screen(pending);
        Pointer(press);
        Button(press, true);
        _manager.RemoveBoid(pending);
        await Frame();
        Button(press, false);
        await Frame();
        Check(_manager.SelectedBoid == b, "Removing a pending click target does not select a freed instance or clear another selection");

        var layer = new CanvasLayer();
        var ui = new Button { Position = new Vector2(10, 10), Size = new Vector2(180, 60), Text = "输入隔离检查" };
        layer.AddChild(ui);
        AddChild(layer);
        int uiClicks = 0;
        ui.Pressed += () => uiClicks++;
        await Frame();
        await Click(new Vector2(40, 30));
        Check(uiClicks == 1 && _manager.SelectedBoid == b, "UI clicks reach the button without clearing world selection");
        Check(_camera.Position.DistanceTo(cameraBefore - drag / _camera.Zoom) < 0.001f, "UI interaction does not pan camera");
        ui.Visible = false;
        await Click(GetViewport().GetVisibleRect().Size * new Vector2(0.9f, 0.85f));
        Check(_manager.SelectedBoid is null, "Clicking empty world clears selection");

        _manager.RemoveBoid(b);
        _camera.FitWorld();
        Pointer(Screen(a));
        for (int i = 0; i < 30; i++)
            _camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true });
        a.Position = _manager.WorldBounds.GetCenter();
        a.Rotation = Mathf.Pi / 2;
        await Frame();
        Vector2 insideTip = a.GetNode<Polygon2D>("Body").GetGlobalTransformWithCanvas() * new Vector2(8, 0);
        Check(insideTip.DistanceTo(Screen(a)) > 10, "Zoomed polygon probe lies outside click-tolerance circle");
        await Click(insideTip);
        Check(_manager.SelectedBoid == a, "Hit test follows rotated/scaled native polygon");
        _manager.RemoveBoid(a);
        await Frame();
        Check(_manager.SelectedBoid is null && _view.DebugGeometry.NeighborCount == 0, "Removing selected instance clears debug selection");
        Boid2D replacement = _manager.SpawnBoid(_manager.WorldBounds.GetCenter(), Vector2.Zero);
        for (int i = 0; i < 50; i++)
            _camera._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true });
        await Click(Screen(replacement) + new Vector2(7, 0));
        Check(_manager.SelectedBoid == replacement, "Small zoom retains a usable screen-space click target");
        _manager.Reset();
        await Frame();
        Check(_manager.SelectedBoid is null && _manager.Boids.Count == 0, "Reset clears selection and old instances");
        layer.QueueFree();
        _manager.QueueFree();
    }

    private void CheckGeometry()
    {
        var config = new BoidConfig { ShowNeighbors = true, ShowPerceptionRadius = true, ShowSeparationRadius = true,
            ShowVelocity = true, PerceptionRadius = 20, SeparationRadius = 8 };
        var a = new Boid2D { Position = new Vector2(50, 50), Rotation = Mathf.Pi / 2 };
        var b = new Boid2D { Position = new Vector2(60, 50) };
        var c = new Boid2D { Position = new Vector2(150, 150) };
        a.Initialize(config, 0, new Vector2(40, 0));
        a.Rotation = Mathf.Pi / 2;
        b.Initialize(config, 1, new Vector2(0, -20));
        c.Initialize(config, 2, Vector2.Zero);
        Boid2D[] boids = { a, b, c };
        var geometry = new DebugOverlayGeometry();
        var bounds = new Rect2(0, 0, 200, 200);
        try
        {
            geometry.Build(boids, a, config, bounds, 1);
            Check(geometry.PerceptionCount == 1 && geometry.VelocityCount == 2, "Range includes only selected individual; arrows still cover moving Boids");
            Check(geometry.NeighborCount == 1 && geometry.NeighborPoints.Count == 2, "Only selected-to-neighbor connection is emitted");
            Check(geometry.VelocityPoints[1] - geometry.VelocityPoints[0] == new Vector2(10, 0), "Arrow follows actual velocity, not lagging body rotation");
            Check(geometry.RangePoints.Count == 96 * 2, "Only one ordinary perception circle is generated regardless of population");
            Check(geometry.SeparationCount == 1 && geometry.SeparationRangePoints.Count == 96 * 2
                && geometry.SeparationRangePoints[0] == a.Position + new Vector2(8, 0),
                "Separation circle uses selected center and effective separation radius");
            config.ShowPerceptionRadius = false;
            geometry.Build(boids, a, config, bounds, 1);
            Check(geometry.RangePoints.Count == 0 && geometry.SeparationRangePoints.Count == 96 * 2,
                "Separation display is independent of perception display");
            config.ShowPerceptionRadius = true;
            config.SeparationRadius = 40;
            geometry.Build(boids, a, config, bounds, 1);
            Check(geometry.SeparationRangePoints.SequenceEqual(geometry.RangePoints),
                "Separation visualization is capped at perception radius just like steering");
            config.ShowSeparationRadius = false;
            geometry.Build(boids, a, config, bounds, 1);
            Check(geometry.SeparationCount == 0 && geometry.SeparationRangePoints.Count == 0 && geometry.RangePoints.Count > 0,
                "Hiding separation does not hide perception");
            config.ShowSeparationRadius = true;
            config.SeparationRadius = 8;
            geometry.Build(boids, b, config, bounds, 1);
            Check(geometry.PerceptionCount == 1 && geometry.RangePoints.Count == 96 * 2
                && geometry.RangePoints[0] == b.Position + new Vector2(config.PerceptionRadius, 0),
                "Switching selection replaces range geometry with the new selected center");
            Check(geometry.SeparationRangePoints[0] == b.Position + new Vector2(8, 0),
                "Separation range follows the newly selected individual");
            geometry.Build(boids, null, config, bounds, 1);
            Check(geometry.NeighborPoints.Count == 0 && geometry.PerceptionCount == 0
                && geometry.RangePoints.Count == 0 && geometry.SeparationRangePoints.Count == 0 && geometry.VelocityCount == 2,
                "No selection suppresses ranges and links but retains all velocity arrows");
            config.ShowNeighbors = config.ShowPerceptionRadius = config.ShowSeparationRadius = config.ShowVelocity = false;
            geometry.Build(boids, a, config, bounds, 1);
            Check(geometry.RangePoints.Count + geometry.SeparationRangePoints.Count + geometry.NeighborPoints.Count
                + geometry.VelocityPoints.Count == 0, "Disabled flags clear geometry rather than retaining stale lines");

            config.ShowNeighbors = config.ShowPerceptionRadius = config.ShowSeparationRadius = true;
            config.PerceptionRadius = 10;
            bounds = new Rect2(0, 0, 100, 100);
            a.Position = new Vector2(2, 50);
            b.Position = new Vector2(98, 50);
            geometry.Build(new[] { a, b }, a, config, bounds, 1);
            Check(geometry.NeighborCount == 1 && geometry.NeighborPoints.Count == 4, "Wrapped neighbors draw short segments at both edges");
            float length = 0;
            for (int i = 0; i < geometry.NeighborPoints.Count; i += 2)
                length += geometry.NeighborPoints[i].DistanceTo(geometry.NeighborPoints[i + 1]);
            Check(MathF.Abs(length - 4) < 0.001f, "Wrapped connection preserves shortest path length");
            Check(geometry.RangePoints.Any(p => p.X < 1) && geometry.RangePoints.Any(p => p.X > 99), "Perception arcs continue across the seam");
            Check(geometry.SeparationRangePoints.Any(p => p.X < 1) && geometry.SeparationRangePoints.Any(p => p.X > 99),
                "Separation arcs also continue across the seam");
            CheckInside(geometry, bounds);
            a.Position = new Vector2(2, 2);
            b.Position = new Vector2(98, 98);
            geometry.Build(new[] { a, b }, a, config, bounds, 1);
            Check(geometry.NeighborCount == 1, "Corner wrap finds diagonal neighbor");
            CheckInside(geometry, bounds);

            bounds = new Rect2(-100, -50, 100, 40);
            a.Position = bounds.GetCenter();
            config.PerceptionRadius = 30;
            geometry.Build(new[] { a }, a, config, bounds, 1);
            Check(!geometry.PerceptionCoversWorld && geometry.RangePoints.Count > 0, "Radius exceeding one half-axis still draws valid toroidal boundary arcs");
            CheckInside(geometry, bounds);
            config.PerceptionRadius = 80;
            config.SeparationRadius = 100;
            geometry.Build(new[] { a }, a, config, bounds, 1);
            Check(geometry.PerceptionCoversWorld && geometry.RangePoints.Count == 8, "Range covering entire torus is shown by world outline");
            Check(geometry.SeparationCoversWorld && geometry.SeparationRangePoints.Count == 8,
                "Separation covering entire torus also uses world outline");
            CheckInside(geometry, bounds);
            geometry.Build(new[] { a }, null, config, bounds, 1);
            Check(!geometry.PerceptionCoversWorld && geometry.RangePoints.Count == 0
                && !geometry.SeparationCoversWorld && geometry.SeparationRangePoints.Count == 0,
                "Deselection also clears full-world perception outline");
        }
        finally
        {
            foreach (Boid2D boid in boids) boid.Free();
        }
    }

    private void CheckInside(DebugOverlayGeometry geometry, Rect2 bounds)
    {
        Check(geometry.RangePoints.Concat(geometry.SeparationRangePoints).Concat(geometry.NeighborPoints).Concat(geometry.VelocityPoints)
            .All(p => p.IsFinite() && p.X >= bounds.Position.X - 0.001f && p.X <= bounds.End.X + 0.001f
                && p.Y >= bounds.Position.Y - 0.001f && p.Y <= bounds.End.Y + 0.001f), "All debug segments are finite and clipped to the visible world");
    }

    private void CheckDebugOptions()
    {
        const string path = "res://.godot/validation/debug-options-test.cfg";
        var config = new BoidConfig();
        var panel = new ControlPanel();
        AddChild(panel);
        try
        {
            panel.Bind(config, "像素");
            var toggle = (CheckBox)panel.FindChild("ShowSeparationRadius", true, false);
            Check(!toggle.ButtonPressed && !config.ShowSeparationRadius, "Separation visualization defaults to off");
            toggle.ButtonPressed = true;
            Check(config.ShowSeparationRadius && !config.ShowPerceptionRadius, "New UI toggle updates shared configuration independently");
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://.godot/validation"));
            Check(config.Save(path) == Error.Ok, "Save separation visualization setting");
            var loaded = new BoidConfig();
            Check(loaded.Load(path) == Error.Ok && loaded.ShowSeparationRadius, "Restore separation visualization setting");
            using var oldFile = new ConfigFile();
            oldFile.SetValue("visualization", "show_perception_radius", true);
            Check(oldFile.Save(path) == Error.Ok, "Write old visualization settings");
            loaded = new BoidConfig();
            Check(loaded.Load(path) == Error.Ok && loaded.ShowPerceptionRadius && !loaded.ShowSeparationRadius,
                "Old settings keep perception choice and default the new toggle to off");
        }
        finally
        {
            panel.Free();
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
        }
    }
}
