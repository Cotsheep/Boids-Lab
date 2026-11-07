using System;
using Godot;

namespace BoidsLab.Tests;

public partial class LeaderFollowRegionChecks : Node
{
    private int _checks;

    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless") { GetTree().Quit(2); return; }
        try
        {
            CheckPreferencesAndUi();
            CheckSelectionMarkers();
            CheckTwoDimensions();
            CheckThreeDimensions();
            CheckMotionIsolation();
            GD.Print($"PASS: {_checks} leader follow region/configuration/presentation assertions (headless).");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void CheckPreferencesAndUi()
    {
        const string path2D = "res://.godot/validation/follow-region-2d.cfg";
        const string path3D = "res://.godot/validation/follow-region-3d.cfg";
        Check(!new BoidConfig().ShowLeaderFollowRegion, "New preference defaults off");
        foreach (bool threeDimensional in new[] { false, true })
        {
            var config = new BoidConfig();
            var panel = new ControlPanel(); AddChild(panel);
            panel.Bind(config, threeDimensional ? "世界单位" : "像素", threeDimensional: threeDimensional);
            panel.SelectCategory("Leader");
            var toggle = (CheckBox)panel.FindChild("ShowLeaderFollowRegion", true, false);
            int changes = 0; panel.ConfigurationChanged += () => changes++;
            Check(toggle.IsVisibleInTree() && !toggle.ButtonPressed && !toggle.Disabled,
                "Follow range is available on the leader page before assigning a leader");
            toggle.ButtonPressed = true;
            Check(config.ShowLeaderFollowRegion && changes == 1, "Toggle updates shared config once");
            if (threeDimensional) toggle.ButtonPressed = false;
            Check(config.Save(threeDimensional ? path3D : path2D) == Error.Ok, "Save isolated display preference");
            panel.Free();
        }
        var two = new BoidConfig(); var three = new BoidConfig();
        Check(two.Load(path2D) == Error.Ok && three.Load(path3D) == Error.Ok
            && two.ShowLeaderFollowRegion && !three.ShowLeaderFollowRegion, "2D and 3D keep independent preferences");
        using var legacy = new ConfigFile();
        legacy.SetValue("simulation", "boid_count", 0); legacy.Save(path2D);
        two = new BoidConfig(); two.Load(path2D);
        Check(!two.ShowLeaderFollowRegion, "Older configurations retain the off default");
        legacy.SetValue("visualization", "show_leader_follow_region", "invalid"); legacy.Save(path2D);
        two.Load(path2D);
        Check(!two.ShowLeaderFollowRegion, "Invalid preference type retains the existing value");
    }

    private BoidManager Create2D(bool shown = true)
    {
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_2d.tscn").Instantiate<BoidManager>();
        world.Initialize(new BoidConfig { BoidCount = 0, ShowLeaderFollowRegion = shown, AvoidanceEnabled = false, MaxForce = 0 });
        AddChild(world); ObstacleHarness2D.ClearLayout(world);
        var leader = world.SpawnBoid(new(500, 300), Vector2.Right * 100);
        for (int i = 0; i < 8; i++) world.SpawnBoid(new(300 + i * 10, 300 + i * 4), Vector2.Right * 50);
        world.SetLeader(leader);
        return world;
    }

    private BoidManager3D Create3D(bool shown = true)
    {
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_3d.tscn").Instantiate<BoidManager3D>();
        world.Initialize(new BoidConfig { BoidCount = 0, ShowLeaderFollowRegion = shown, AvoidanceEnabled = false, MaxForce = 0 });
        AddChild(world);
        var leader = world.SpawnBoid(new(100, 0, 0), Vector3.Right * 100);
        for (int i = 0; i < 8; i++) world.SpawnBoid(new(-100 + i * 10, i * 4, i * 3), Vector3.Right * 50);
        world.SetLeader(leader);
        return world;
    }

    private void CheckTwoDimensions()
    {
        var world = Create2D(false);
        var view = world.GetNode<SimulationView>("SimulationView");
        Check(view.FollowRegion is null, "2D region starts hidden even with a leader");
        world.Config.ShowLeaderFollowRegion = true; world.ConfigurationChanged();
        var region = view.FollowRegion!.Value;
        Check(Mathf.Abs(region.Radius - 45.96194f) < 0.001f
            && region.Center.DistanceTo(new(500 - 80 - region.Radius, 300)) < 0.001f,
            "2D disk uses follower area and the configured gap to its front edge");
        world.Leader!.Rotation = Mathf.Pi / 2;
        world.Step(0.01);
        Check(Mathf.Abs(view.FollowRegion!.Value.Center.Y - world.Leader.Position.Y) < 0.001f,
            "2D range follows the steering heading rather than the delayed visual rotation");
        world.Leader.ApplyObstacleContact(world.Leader.Position, Vector2.Zero);
        world.Step(0.01);
        Check(Mathf.Abs(view.FollowRegion!.Value.Center.Y - world.Leader.Position.Y) < 0.001f,
            "Stopped 2D leader retains the actual follow heading");
        Vector2 position = world.Leader.Position;
        world.Config.LeaderFollowDistance = 140; world.Config.SeparationRadius = 40; world.ConfigurationChanged();
        region = view.FollowRegion!.Value;
        Check(region.Radius > 70 && Mathf.Abs(position.X - region.Center.X - region.Radius - 140) < 0.001f
            && world.Leader.Position == position && !world.IsRunning, "Paused parameter edits update range without moving the leader");
        world.SetLeader(world.Boids[1]);
        Check(view.FollowRegion!.Value.Center != region.Center, "Identity transfer immediately changes the displayed range");
        world.Config.LeaderFollowWeight = 0; world.ConfigurationChanged();
        Check(view.FollowRegion is null, "Zero follow weight hides an inactive range");
        world.Config.LeaderFollowWeight = 1.5f; world.SetLeader(null);
        Check(view.FollowRegion is null, "Clearing identity leaves no stale circle");
        world.SetLeader(world.Boids[0]); world.ResizePopulation(2);
        Check(view.FollowRegion is null && world.Config.ShowLeaderFollowRegion, "A point-sized follow target creates no false-sized circle");
        world.Reset();
        Check(view.FollowRegion is null, "Reset removes the old range");
        world.Free();
    }

    private void CheckSelectionMarkers()
    {
        var world = Create2D();
        var view = world.GetNode<SimulationView>("SimulationView");
        var leader = world.Leader!;
        float normalWidth = view.LeaderMarker!.Value.Width;
        GetViewport().NotifyMouseEntered();
        Select(world.Boids[1]);
        Check(world.SelectedBoid == world.Boids[1] && view.SelectionMarker is not null
            && world.Boids[1].GetNode<Polygon2D>("Body").Color == view.Palette.Selection,
            "Ordinary selection uses its own color and ring while a leader exists");
        Select(leader);
        Check(world.SelectedBoid == leader && view.SelectionMarker is null && view.LeaderMarker is not null,
            "Selected leader has exactly one identity ring instead of stacked selection and identity shapes");
        Check(view.LeaderMarker!.Value.Color == view.Palette.SelectedLeader && view.LeaderMarker.Value.Width > normalWidth
            && leader.GetNode<Polygon2D>("Body").Color == view.Palette.SelectedLeader,
            "Selecting a leader strengthens the same ring and its body highlight");
        world.SetLeaderTarget(leader.Position + Vector2.One);
        Check(!view.HasSeparateLeaderTarget, "A nearby target cannot overlap the leader ring");
        world.SetLeaderTarget(leader.Position + Vector2.Right * view.LeaderMarker!.Value.Radius * 10);
        Check(view.HasSeparateLeaderTarget, "A distant target retains its visible aiming marker");
        world.ClearBoidSelection();
        Check(view.SelectionMarker is null && view.LeaderMarker!.Value.Color == view.Palette.Leader,
            "Deselecting keeps a single persistent leader identity marker");
        world.SetLeader(null);
        Check(view.LeaderMarker is null && !view.HasSeparateLeaderTarget, "Clearing identity removes all leader markers");
        world.Free();

        void Select(Boid2D boid)
        {
            Vector2 point = boid.GetGlobalTransformWithCanvas() * Vector2.Zero;
            foreach (bool pressed in new[] { true, false })
                GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point,
                    ButtonIndex = MouseButton.Left, Pressed = pressed, ButtonMask = pressed ? MouseButtonMask.Left : 0 }, true);
        }
    }

    private void CheckThreeDimensions()
    {
        var world = Create3D();
        var view = world.GetNode<SimulationView3D>("SimulationView");
        var display = view.GetNode<Node3D>("LeaderFollowRegion");
        view.RefreshDebug();
        var region = view.FollowRegion!.Value;
        Check(display.Visible && Math.Abs(region.Radius - 40) < 0.001f
            && region.Center.DistanceTo(new(-20, 0, 0)) < 0.001f, "3D sphere uses actual follow volume and heading");
        foreach (LabPalette palette in new[] { LabPalette.Dark, LabPalette.Light })
        {
            view.ApplyPalette(palette);
            foreach (string part in new[] { "Fill", "Outline" })
            {
                var material = (StandardMaterial3D)view.GetNode<MeshInstance3D>($"LeaderFollowRegion/{part}").MaterialOverride;
                Check(material.Transparency == BaseMaterial3D.TransparencyEnum.Alpha
                    && material.DepthDrawMode == BaseMaterial3D.DepthDrawModeEnum.Disabled
                    && material.AlbedoColor.A < 0.5f && material.AlbedoColor.R == palette.Leader.R,
                    "Both themes use translucent leader colors without writing depth");
            }
        }
        var camera = view.GetNode<SimulationCamera3D>("Camera3D");
        camera.SetFollowFrozen(true);
        camera.Position = region.Center; view.RefreshDebug();
        var fill = (StandardMaterial3D)view.GetNode<MeshInstance3D>("LeaderFollowRegion/Fill").MaterialOverride;
        Check(fill.AlbedoColor.A == 0, "Camera inside the sphere sees no full-screen color wash");
        camera.Position = region.Center + Vector3.Back * region.Radius * 3; view.RefreshDebug();
        Check(fill.AlbedoColor.A > 0 && fill.AlbedoColor.A < 0.1f, "Fill returns subtly when the camera is outside");
        world.SetRunning(true); world.Step(0.1);
        view.PreparePresentation(0.25f);
        region = view.FollowRegion!.Value;
        Check(Math.Abs(world.Leader!.DisplayPosition.X - 102.5f) < 0.001f
            && region.Center.DistanceTo(new(-17.5f, 0, 0)) < 0.001f
            && display.Position.DistanceTo(region.Center) < 0.001f,
            "3D range and its mesh follow the same interpolated pose as the displayed leader");
        world.SetRunning(false);
        world.Config.ShowLeaderFollowRegion = false; world.ConfigurationChanged();
        Check(!display.Visible && view.FollowRegion is null, "3D toggle hides geometry immediately while paused");
        world.Config.ShowLeaderFollowRegion = true; world.ConfigurationChanged();
        Check(display.Visible, "3D toggle restores geometry without a simulation step");
        world.SetLeader(null);
        Check(!display.Visible, "3D identity cancellation clears the display");
        world.SetLeader(world.Boids[1]); world.ResizePopulation(0);
        Check(!display.Visible, "Deleting all individuals clears the range");
        world.Free();
    }

    private void CheckMotionIsolation()
    {
        var a = Create2D(false); var b = Create2D(true);
        a.Config.MaxForce = b.Config.MaxForce = 200;
        for (int step = 0; step < 60; step++)
        {
            a.Step(1.0 / 60); b.Step(1.0 / 60);
            for (int i = 0; i < a.Boids.Count; i++)
                Check(a.Boids[i].Position == b.Boids[i].Position && a.Boids[i].Velocity == b.Boids[i].Velocity,
                    "2D display preference cannot change trajectories");
        }
        a.Free(); b.Free();
        var c = Create3D(false); var d = Create3D(true);
        c.Config.MaxForce = d.Config.MaxForce = 200;
        for (int step = 0; step < 60; step++)
        {
            c.Step(1.0 / 60); d.Step(1.0 / 60);
            d.GetNode<SimulationView3D>("SimulationView").RefreshDebug();
            for (int i = 0; i < c.Boids.Count; i++)
                Check(c.Boids[i].Position == d.Boids[i].Position && c.Boids[i].Velocity == d.Boids[i].Velocity,
                    "3D display preference cannot change trajectories");
        }
        c.Free(); d.Free();
    }

    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
