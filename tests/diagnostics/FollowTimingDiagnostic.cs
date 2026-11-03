using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace BoidsLab.Tests;

/// <summary>显式诊断场景：重放物理/显示时序，报告相对静止目标的屏幕抖动，不属于通过型回归。</summary>
public partial class FollowTimingDiagnostic : Node
{
    private const string Output = "res://.godot/validation/follow-timing";
    private readonly List<string> _rows = new() { "physics_hz,render_hz,fade,display_interpolation,frame,physics_tick,screen_x,camera_x,world_x" };

    public override void _Ready()
    {
        try
        {
            GetWindow().Size = new Vector2I(1360, 860);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Output));
            GD.Print($"FOLLOW-TIMING: project interpolation={ProjectSettings.GetSetting("physics/common/physics_interpolation", false)}, physics_hz={Engine.PhysicsTicksPerSecond}");
            bool baselineOnly = OS.GetCmdlineUserArgs().Contains("--baseline-only");
            Measure(60, 144, false, false);
            if (!baselineOnly)
            {
                Measure(60, 60, false, false);
                Measure(60, 120, false, false);
                Measure(60, 144, true, false);
                Measure(144, 144, false, false);
                Measure(60, 144, false, true);
            }
            using var file = Godot.FileAccess.Open(Output + "/samples.csv", Godot.FileAccess.ModeFlags.Write);
            file.StoreString(string.Join("\n", _rows) + "\n");
            GD.Print($"FOLLOW-TIMING: samples={ProjectSettings.GlobalizePath(Output)}/samples.csv");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Measure(int physicsHz, int renderHz, bool fade, bool interpolateDisplay)
    {
        var config = new BoidConfig
        {
            BoidCount = 0, MaxSpeed = 120, MaxForce = 0,
            MaxAngularSpeedDegrees = 0, MaxAngularAccelerationDegrees = 0,
            SeparationEnabled = false, AlignmentEnabled = false, CohesionEnabled = false,
            LeaderFollowWeight = 0, AvoidanceEnabled = false, FadeNearbyBoids = fade,
            ShowGrid = false, ShowBoundary = false, ShowVelocity = false,
        };
        var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_3d.tscn").Instantiate<BoidManager3D>();
        world.Initialize(config);
        AddChild(world);
        world.SetPhysicsProcess(false);
        var camera = world.GetNode<SimulationCamera3D>("SimulationView/Camera3D");
        camera.SetProcess(false);
        var leader = world.SpawnBoid(Vector3.Zero, Vector3.Right * 120);
        leader.Quaternion = Quaternion.Identity;
        world.SpawnBoid(new Vector3(-40, 20, 80), Vector3.Right * 120);
        world.SpawnBoid(new Vector3(40, -20, -80), Vector3.Right * 120);
        world.SetLeader(leader);
        world.SetRunning(true);

        var view = world.GetNode<SimulationView3D>("SimulationView");
        double physicsDt = 1.0 / physicsHz, renderDt = 1.0 / renderHz;
        int tick = 0;
        var screen = new List<double>();
        float maxTrajectoryError = 0;
        for (int frame = 1; frame <= renderHz * 4; frame++)
        {
            double time = frame * renderDt;
            while ((tick + 1) * physicsDt <= time + 1e-10)
            {
                world._PhysicsProcess(physicsDt);
                tick++;
                maxTrajectoryError = Mathf.Max(maxTrajectoryError, Mathf.Abs(leader.Position.X - (float)(tick * physicsDt * 120)));
            }
            // fraction=1 仅用于重现旧版跳步显示；正式运行始终使用引擎提供的帧内比例。
            float fraction = (float)((time - tick * physicsDt) / physicsDt);
            view.PreparePresentation(interpolateDisplay ? fraction : 1);
            camera._Process(renderDt);
            float x = camera.UnprojectPosition(leader.GetNode<MeshInstance3D>("Body").GlobalPosition).X;
            if (!float.IsFinite(x)) throw new InvalidOperationException("Non-finite screen coordinate");
            if (time > 2) screen.Add(x); // 舍弃相机起步追赶阶段。
            _rows.Add(FormattableString.Invariant($"{physicsHz},{renderHz},{fade},{interpolateDisplay},{frame},{tick},{x:F6},{camera.Position.X:F6},{leader.Position.X:F6}"));
        }
        var steps = screen.Skip(1).Select((x, i) => x - screen[i]).ToArray();
        double rms = Math.Sqrt(steps.Select(d => d * d).Average());
        int reversals = steps.Zip(steps.Skip(1)).Count(p => p.First * p.Second < -0.01);
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"FOLLOW-TIMING: physics={physicsHz}, render={renderHz}, fade={fade}, interpolation_probe={interpolateDisplay}, range_px={screen.Max() - screen.Min():F4}, step_rms_px={rms:F4}, reversals={reversals}, trajectory_error={maxTrajectoryError:F6}"));
        world.Free();
    }
}
