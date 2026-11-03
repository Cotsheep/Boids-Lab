using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace BoidsLab.Tests;

public partial class PopulationChecks : Node
{
    private int _checks;
    private MainController _app = null!;
    private ControlPanel Panel => _app.GetNode<ControlPanel>("%ControlPanel");
    private ISimulationWorld World => (ISimulationWorld)_app.GetNode<Node>("%World").GetChild(0);
    private BoidConfig Config => World is BoidManager two ? two.Config : ((BoidManager3D)World).Config;
    private SpinBox CountInput => (SpinBox)Panel.FindChild("BoidCount", true, false);
    private int Count => World.Stats.BoidCount ?? -1;

    public override async void _Ready()
    {
        if (DisplayServer.GetName() != "headless") { GetTree().Quit(2); return; }
        try
        {
            _app = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<MainController>();
            _app.World2D = (SimulationWorldDefinition)_app.World2D.Duplicate();
            _app.World3D = (SimulationWorldDefinition)_app.World3D.Duplicate();
            _app.World2D.ConfigurationPath = "res://.godot/validation/population-2d.cfg";
            _app.World3D.ConfigurationPath = "res://.godot/validation/population-3d.cfg";
            foreach (var definition in new[] { _app.World2D, _app.World3D })
                Check(new BoidConfig { BoidCount = 4, RandomSeed = 42, WorldWidth = 300, WorldHeight = 300, WorldDepth = 300 }
                    .Save(definition.ConfigurationPath) == Error.Ok, "Isolated population preferences");
            AddChild(_app);
            await Frame();
            foreach (int mode in new[] { 0, 1 })
            {
                if (mode != _app.CurrentWorldIndex) { _app.RequestWorldSwitch(mode); await Frame(); }
                if (World is BoidManager two) ObstacleHarness2D.ClearLayout(two);
                await CheckSubmission();
                CheckPreservationAndRandomness();
                CheckSelectionAndLeader();
                CheckObstacles();
            }
            await CheckSwitching();
            GD.Print($"PASS: {_checks} runtime population/input/placement/determinism assertions (headless).");
            _app.Free();
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task CheckSubmission()
    {
        int requests = 0;
        void CountRequest(int count) => requests++;
        Panel.PopulationCountRequested += CountRequest;
        var edit = CountInput.GetLineEdit();
        edit.GrabFocus();
        await Frame();
        object[] before = States();
        edit.Text = "1";
        await Frame();
        edit.Text = "10";
        await Frame();
        Check(Count == 4 && Config.BoidCount == 4 && States().SequenceEqual(before), "Typing intermediate digits does not resize or save the population");
        Tap(Key.Enter);
        await Frame();
        Check(Count == 10 && Config.BoidCount == 10 && requests == 1 && !World.IsRunning,
            "Enter commits the requested population while preserving pause");
        var saved = new BoidConfig();
        Check(saved.Load(_app.ConfigurationPath) == Error.Ok && saved.BoidCount == 10, "Committed request is saved to the active world's file");
        edit.ReleaseFocus();
        await Frame();
        Check(requests == 1, "Focus loss after Enter does not submit twice");
        edit.GrabFocus();
        await Frame();
        edit.Text = "7";
        await Frame();
        edit.ReleaseFocus();
        await Frame();
        Check(Count == 7 && requests == 2, "Focus loss commits the final text once");

        // 验证 SpinBox 的箭头修改也会在编辑焦点离开后提交。
        edit.GrabFocus();
        await Frame();
        Rect2 numberRect = CountInput.GetGlobalRect();
        Click(new Vector2(numberRect.End.X - 8, numberRect.Position.Y + 8));
        await Frame();
        Check(Count == 7, "SpinBox value edits remain pending until submission");
        edit.ReleaseFocus();
        Check(Count == 8 && requests == 3, "SpinBox draft value commits on focus loss");
        await Frame();
        Click(new Vector2(numberRect.End.X - 8, numberRect.Position.Y + 8));
        await Frame();
        Check(edit.HasFocus() && Count == 8, "Clicking an unfocused SpinBox arrow begins a pending edit");
        edit.ReleaseFocus();
        Check(Count == 9 && requests == 4, "Arrow-only edits commit when leaving the input");
        World.SetRunning(true);
        edit.GrabFocus();
        edit.Text = "5";
        Tap(Key.Enter);
        Check(World.IsRunning && Count == 5, "Population submission does not pause a running world");
        World.SetRunning(false);
        edit.ReleaseFocus();
        Panel.PopulationCountRequested -= CountRequest;
        // 结束焦点引起的面板滚动，后续世界点击从可见画面发出。
        Panel.SelectCategory("Basics");
        await Frame();
    }

    private void CheckPreservationAndRandomness()
    {
        Config.RandomSeed = 42;
        Config.BoidCount = 4;
        World.Reset();
        object[] reset = States();
        foreach (NeighborSearchMode search in new[] { NeighborSearchMode.Naive, NeighborSearchMode.UniformGrid })
        {
            Config.NeighborSearchMode = search;
            World.StepOnce();
            object[] moving = States();
            ulong[] instances = Instances();
            World.ResizePopulation(9);
            Check(States().Take(4).SequenceEqual(moving) && Instances().Take(4).SequenceEqual(instances),
                "Growing preserves positions, velocities, angular state and instances");
            Check(Ids().SequenceEqual(Enumerable.Range(0, 9)) && World.Stats.UpdateMilliseconds is null && !HasNeighbors(),
                "New IDs increase monotonically and old search/timing results are invalidated");
            World.ResizePopulation(4);
            Check(States().SequenceEqual(moving), "Stable shrinking removes newer unselected boids first");
            World.ResizePopulation(6);
            Check(Ids().SequenceEqual(new[] { 0, 1, 2, 3, 9, 10 }), "Removed IDs are not reused by subsequent growth");
            World.StepOnce();
            Check(HasNeighbors() && World.Stats.NeighborCandidateChecks is >= 0, "Both search modes refresh safely after resizing");
            Config.BoidCount = 4;
            World.Reset();
            Check(States().SequenceEqual(reset), "Incremental generation never changes seeded reset results");
        }
        World.ResizePopulation(7);
        object[] firstGrowth = States();
        Config.RandomSeed = 999;
        World.ResizePopulation(11);
        object[] changedSeedDraft = States();
        Config.RandomSeed = 42;
        Config.BoidCount = 4;
        World.Reset();
        World.ResizePopulation(7);
        Check(States().SequenceEqual(firstGrowth), "Reset restarts a reproducible independent incremental sequence");
        World.ResizePopulation(11);
        Check(States().SequenceEqual(changedSeedDraft), "Editing seed without resetting does not change incremental randomness");
        if (World is BoidManager two)
            Check(two.Boids.All(b => two.ViewBounds.HasPoint(b.Position)), "Observation growth stays within current 2D bounds");
        else
        {
            var three = (BoidManager3D)World;
            Check(three.Boids.All(b => three.WorldBounds.HasPoint(b.Position)), "Observation growth stays within current 3D bounds");
        }
    }

    private void CheckSelectionAndLeader()
    {
        Config.BoidCount = 0;
        World.Reset();
        World.SetRunning(true);
        if (World is BoidManager two)
        {
            Vector2 center = two.ViewBounds.GetCenter();
            for (int i = 0; i < 5; i++) two.SpawnBoid(center + new Vector2(i * 70, 0), new Vector2(30, 10));
            Click(two.Boids[0].GetGlobalTransformWithCanvas() * Vector2.Zero);
            var selected = two.Boids[0];
            var leader = two.Boids[4];
            two.SetLeader(leader);
            Check(two.SelectedBoid == selected, "2D selection is independent from leader identity");
            foreach (var boid in two.Boids) boid.Position += new Vector2(1000000, -1000000);
            Transform2D camera = two.GetNode<SimulationCamera2D>("Camera2D").Transform;
            object[] states = States();
            two.ResizePopulation(20);
            Check(States().Take(5).SequenceEqual(states) && two.Boids.Skip(5).All(b => b.Position.DistanceTo(leader.Position) < 2000)
                && two.WrapBounds is null && two.GetNode<SimulationCamera2D>("Camera2D").Transform == camera,
                "Infinite 2D growth stays near the distant flock without moving survivors or camera");
            two.ResizePopulation(2);
            Check(two.Boids.SequenceEqual(new[] { selected, leader }) && two.SelectedBoid == selected && two.Leader == leader,
                "Shrink retains selected boid and leader in original order");
            two.ResizePopulation(1);
            Check(two.Boids[0] == leader && two.SelectedBoid is null, "One remaining slot prioritizes the leader over ordinary selection");
            two.ResizePopulation(0);
            Check(two.Leader is null && two.SelectedBoid is null && two.LeaderTarget is null && two.WrapBounds is not null,
                "Zero population clears 2D roles and returns to wrapping mode");
        }
        else
        {
            var three = (BoidManager3D)World;
            Vector3 center = three.WorldBounds.GetCenter();
            for (int i = 0; i < 5; i++) three.SpawnBoid(center + Vector3.Right * i * 70, new Vector3(30, 10, 0));
            var selected = three.Boids[0];
            var leader = three.Boids[4];
            var camera = three.GetNode<SimulationCamera3D>("SimulationView/Camera3D");
            Click(camera.UnprojectPosition(selected.GlobalPosition));
            three.SetLeader(leader);
            Check(three.SelectedBoid == selected, "3D selection is independent from leader identity");
            foreach (var boid in three.Boids) boid.Position += new Vector3(1000000, -1000000, 1000000);
            three.SetLeaderControlActive(true);
            three.LeaderController._Input(new InputEventAction { Action = "leader_forward", Pressed = true });
            Transform3D view = camera.Transform;
            object[] states = States();
            three.ResizePopulation(20);
            Check(States().Take(5).SequenceEqual(states) && three.Boids.Skip(5).All(b => b.Position.DistanceTo(leader.Position) < 2000)
                && three.IsLeaderControlActive && camera.Transform == view, "Infinite 3D growth preserves control/camera and stays near the distant flock");
            three.ResizePopulation(2);
            Check(three.Boids.SequenceEqual(new[] { selected, leader }) && three.SelectedBoid == selected && three.Leader == leader,
                "3D shrinking retains selection and leader");
            three.ResizePopulation(1);
            Check(three.Boids[0] == leader && three.SelectedBoid is null && three.IsLeaderControlActive,
                "Shrinking to the leader preserves control");
            Vector3 last = leader.Position;
            three.ResizePopulation(0);
            Check(three.Leader is null && three.SelectedBoid is null && !three.IsLeaderControlActive && !camera.IsFollowing
                && three.LeaderController.MovementDirection == Vector3.Zero && three.WorldBounds.GetCenter().IsEqualApprox(last),
                "Zero clears 3D capture and input, restoring observation at the last leader position");
            Check(three.GetNode<SimulationView3D>("SimulationView").DebugGeometry.VelocityPoints.Count == 0,
                "Zero population leaves no stale debug arrows");
        }
        Check(World.IsRunning && Count == 0, "Shrinking through leader removal to zero preserves the running state");
        World.SetRunning(false);
    }

    private void CheckObstacles()
    {
        Config.AvoidanceEnabled = false; // 生成保护不依赖运行时主动避障开关。
        Config.BoidCount = 0;
        World.Reset();
        if (World is BoidManager two)
        {
            var editor = two.GetNode<ObstacleEditor2D>("ObstacleEditor");
            Vector2 center = two.ViewBounds.GetCenter();
            var box = editor.AddObstacle(ObstacleShape.Rectangle, center);
            box.SetDimensions(Vector2.One * 2000);
            two.SpawnBoid(center, Vector2.Right * 10);
            object[] existing = States();
            Submit(6);
            Check(Count == 1 && two.PopulationSpawnLimited && States().SequenceEqual(existing),
                "A blocked 2D region stops bounded attempts without relocating existing overlap");
            CheckLimitMessage();
            box.Position += Vector2.One * 10000;
            Submit(6);
            Check(Count == 6 && !two.PopulationSpawnLimited, "Submitting the same request retries after freeing 2D space");
            var circle = editor.AddObstacle(ObstacleShape.Circle, center);
            circle.SetDimensions(Vector2.One * 200);
            two.ResizePopulation(200);
            Check(two.Boids.Skip(6).All(b => b.Position.DistanceTo(circle.Position) > 100 + Config.ObstacleClearance),
                "New 2D boids respect circular obstacles and clearance even when avoidance is disabled");
            Check(editor.Obstacles.Count == 2 && box.Position == center + Vector2.One * 10000, "Resizing preserves obstacle layout");
            two.ResizePopulation(0);
            circle.Position += Vector2.One * 10000;
            box.Position = new Vector2(two.ViewBounds.End.X - two.ViewBounds.Size.X * 0.05f - 1000 - Config.ObstacleClearance, center.Y);
            Submit(5000);
            Check(Count > 0 && Count < 5000 && two.PopulationSpawnLimited, "Partial 2D growth reports the actual count after bounded sampling");
            two.ResizePopulation(0);
            editor.DeleteSelected();
            box.Position += Vector2.One * 10000;
        }
        else
        {
            var three = (BoidManager3D)World;
            var editor = three.GetNode<ObstacleEditor3D>("ObstacleEditor");
            Vector3 center = three.WorldBounds.GetCenter();
            var box = editor.AddObstacle(ObstacleShape.Box, center);
            box.SetDimensions(Vector3.One * 2000);
            three.SpawnBoid(center, Vector3.Right * 10);
            object[] existing = States();
            Submit(6);
            Check(Count == 1 && three.PopulationSpawnLimited && States().SequenceEqual(existing),
                "A blocked 3D region stops bounded attempts without relocating existing overlap");
            CheckLimitMessage();
            box.Position += Vector3.One * 10000;
            Submit(6);
            Check(Count == 6 && !three.PopulationSpawnLimited, "Submitting the same request retries after freeing 3D space");
            var sphere = editor.AddObstacle(ObstacleShape.Sphere, center);
            sphere.SetDimensions(Vector3.One * 100);
            three.ResizePopulation(200);
            Check(three.Boids.Skip(6).All(b => b.Position.DistanceTo(sphere.Position) > 50 + Config.ObstacleClearance),
                "New 3D boids respect spherical obstacles and clearance at distant coordinates");
            Check(editor.Obstacles.Count == 2 && box.Position == center + Vector3.One * 10000, "3D resizing preserves obstacle layout");
            three.ResizePopulation(0);
            sphere.Position += Vector3.One * 10000;
            box.Position = new Vector3(three.WorldBounds.End.X - three.WorldBounds.Size.X * 0.05f - 1000 - Config.ObstacleClearance, center.Y, center.Z);
            Submit(5000);
            Check(Count > 0 && Count < 5000 && three.PopulationSpawnLimited, "Partial 3D growth reports actual count without ignoring contact constraints");
            three.ResizePopulation(0);
            box.Position += Vector3.One * 10000;
        }
        World.ResizePopulation(5000);
        Check(Count == 5000 && !World.PopulationSpawnLimited, "Unblocked growth reaches the configured maximum");
        World.ResizePopulation(0);
        Config.BoidCount = 4;
        World.Reset();
    }

    private async Task CheckSwitching()
    {
        CountInput.GetLineEdit().GrabFocus();
        await Frame();
        CountInput.GetLineEdit().Text = "13";
        _app.RequestWorldSwitch(0);
        await Frame();
        var saved = new BoidConfig();
        Check(saved.Load(_app.World3D.ConfigurationPath) == Error.Ok && saved.BoidCount == 13,
            "World switching saves a pending count without modifying the replacement world");
        Submit(7);
        for (int i = 0; i < 4; i++)
        {
            int mode = (i + 1) % 2;
            _app.RequestWorldSwitch(mode);
            await Frame();
            Check(Count == (mode == 1 ? 13 : 7) && !World.IsRunning, "World switching retains independent requested counts and starts paused");
        }
    }

    private void CheckLimitMessage()
    {
        var status = (Label)Panel.FindChild("PopulationStatus", true, false);
        var saved = new BoidConfig();
        Check(status.Text.Contains("实际 1") && status.Text.Contains("重试")
            && saved.Load(_app.ConfigurationPath) == Error.Ok && saved.BoidCount == 6,
            "UI reports the placement limit and saves requested rather than actual population");
    }
    private void Submit(int count)
    {
        CountInput.GetLineEdit().Text = count.ToString();
        CountInput.GetLineEdit().EmitSignal(LineEdit.SignalName.TextSubmitted, count.ToString());
    }
    private object[] States() => World is BoidManager two
        ? two.Boids.Select(b => (object)(b.Id, b.Position, b.Velocity, b.Acceleration, b.Rotation, b.AngularVelocity, b.AngularAcceleration)).ToArray()
        : ((BoidManager3D)World).Boids.Select(b => (object)(b.Id, b.Position, b.Velocity, b.Acceleration, b.Quaternion, b.AngularVelocity, b.AngularAcceleration)).ToArray();
    private int[] Ids() => World is BoidManager two ? two.Boids.Select(b => b.Id).ToArray() : ((BoidManager3D)World).Boids.Select(b => b.Id).ToArray();
    private ulong[] Instances() => World is BoidManager two ? two.Boids.Select(b => b.GetInstanceId()).ToArray()
        : ((BoidManager3D)World).Boids.Select(b => b.GetInstanceId()).ToArray();
    private bool HasNeighbors() => World is BoidManager two ? two.NeighborSearch.HasResults : ((BoidManager3D)World).NeighborSearch.HasResults;
    private void Tap(Key key)
    {
        foreach (bool down in new[] { true, false })
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = down }, true);
    }
    private void Click(Vector2 point)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
        foreach (bool down in new[] { true, false })
            GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point,
                ButtonIndex = MouseButton.Left, Pressed = down }, true);
    }
    private async Task Frame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
