using System;
using Godot;

namespace BoidsLab.Tests;

/// <summary>真实 PhysicsServer/GodotPhysics2D/Jolt 集成，不等待物理步；已知自由空间独立验证。</summary>
public partial class PhysicsObstacleChecks : Node
{
    private int _checks;
    private void Check(bool value, string message) { _checks++; if (!value) throw new InvalidOperationException(message); }
    public override void _Ready()
    {
        try
        {
            Lifecycle2D(); Lifecycle3D(); Irregular(); Contacts(); Boundaries(); Sampling(); Enclosed(); ManagerSync(); SearchEquivalence(); Ties();
            GD.Print($"PASS: {_checks} native obstacle query/irregular/contact/synchronization assertions.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Lifecycle2D()
    {
        using var query = new ObstacleQuery2D();
        var parent = new Node2D { Position = new(300, -200), Rotation = 0.4f, Scale = Vector2.One * 1.3f };
        AddChild(parent);
        var obstacle = new Obstacle2D { Position = new(40, 30) };
        obstacle.Initialize(ObstacleShape.Rectangle, new(100, 40));
        parent.AddChild(obstacle);
        var list = new[] { obstacle };
        query.Synchronize(list);
        Check(query.Occupied(obstacle.GlobalPosition, 0), "2D immediate creation and parent transform");
        Vector2 old = obstacle.GlobalPosition;
        obstacle.Position += new Vector2(300, 0);
        query.Synchronize(list);
        Check(!query.Occupied(old, 0) && query.Occupied(obstacle.GlobalPosition, 0), "2D immediate paused move");
        obstacle.SetDimensions(new(200, 60));
        query.Synchronize(list);
        Check(query.Occupied(obstacle.ToGlobal(new Vector2(90, 0)), 0), "2D immediate resize");
        obstacle.CollisionParts.Add(new ObstaclePart2D { Shape = new CapsuleShape2D { Radius = 10, Height = 60 }, Transform = new(0.3f, new Vector2(20, 0)) });
        query.Synchronize(list);
        Check(query.Occupied(obstacle.ToGlobal(new Vector2(20, 0)), 2), "New Capsule2D shape reuses query");
        Check(query.ContainsPoint(obstacle.ToGlobal(new Vector2(20, 0)), obstacle.GetInstanceId()), "Custom shape participates in generic picking");
        ((CapsuleShape2D)obstacle.CollisionParts[0].Shape).Radius = 20;
        query.Synchronize(list);
        Check(query.Occupied(obstacle.ToGlobal(new Vector2(30, 0)), 0), "2D mutable resource synchronized");
        query.Remove(obstacle.GetInstanceId());
        Check(!query.Occupied(obstacle.GlobalPosition, 0), "2D immediate deletion without a physics frame");
        parent.Free();
    }

    private void Lifecycle3D()
    {
        using var query = new ObstacleQuery3D();
        var parent = new Node3D { Position = new(300, -200, 100), Rotation = new(0.2f, 0.4f, 0.3f) };
        AddChild(parent);
        var obstacle = new Obstacle3D { Position = new(40, 30, 10) };
        obstacle.Initialize(ObstacleShape.Box, new(100, 40, 60));
        parent.AddChild(obstacle);
        var list = new[] { obstacle };
        query.Synchronize(list);
        Check(query.Occupied(obstacle.GlobalPosition, 0), "3D immediate creation and parent transform");
        Vector3 old = obstacle.GlobalPosition;
        obstacle.Position += new Vector3(300, 0, 0);
        query.Synchronize(list);
        Check(!query.Occupied(old, 0) && query.Occupied(obstacle.GlobalPosition, 0), "3D immediate paused move");
        obstacle.SetDimensions(new(200, 60, 60));
        query.Synchronize(list);
        Check(query.Occupied(obstacle.ToGlobal(new Vector3(90, 0, 0)), 0), "3D immediate resize");
        obstacle.CollisionParts.Add(new ObstaclePart3D { Shape = new CylinderShape3D { Radius = 20, Height = 80 }, Transform = new(Basis.FromEuler(new(0.2f, 0, 0)), new(20, 0, 0)) });
        query.Synchronize(list);
        Check(query.Occupied(obstacle.ToGlobal(new Vector3(20, 0, 0)), 2), "New Cylinder3D shape reuses query");
        Vector3 origin = obstacle.ToGlobal(new Vector3(-100, 0, 0));
        Vector3 direction = (obstacle.ToGlobal(new Vector3(20, 0, 0)) - origin).Normalized();
        Check(query.HitDistance(origin, direction, 200, out ulong id) is not null && id == obstacle.GetInstanceId(), "Custom 3D ray picking retains logical identity");
        obstacle.SetPreview(true);
        query.Synchronize(list);
        Check(query.BodyCount == 0 && !query.Occupied(obstacle.GlobalPosition, 0), "Preview never participates");
        obstacle.SetPreview(false);
        query.Synchronize(list);
        query.Remove(obstacle.GetInstanceId());
        Check(!query.Occupied(obstacle.GlobalPosition, 0), "3D immediate deletion");
        parent.Free();
    }

    private void Irregular()
    {
        using var two = new ObstacleQuery2D();
        using var three = new ObstacleQuery3D();
        var l = GD.Load<PackedScene>("res://scenes/obstacles/l_shape_2d.tscn").Instantiate<Obstacle2D>();
        var compound = GD.Load<PackedScene>("res://scenes/obstacles/compound_3d.tscn").Instantiate<Obstacle3D>();
        AddChild(l); AddChild(compound);
        two.Synchronize(new[] { l }); three.Synchronize(new[] { compound });
        Check(l.Parts.Count >= 2, "Concave solid polygon decomposed into convex parts");
        Check(!two.Occupied(Vector2.Zero, 12) && two.Occupied(new(-80, 0), 0), "L notch remains open and arm solid");
        Check(!two.Sweep(new(80, 80), new(-100, -100), 12).Blocked, "2D free diagonal inside enclosing box remains passable");
        Check(two.Sweep(new(0, 0), new(-200, 0), 12).Blocked, "L arm blocks circle cast");
        Check(!three.Occupied(Vector3.Zero, 12) && three.Occupied(new(-80, 0, 0), 0), "Compound notch open and convex arm solid");
        Check(!three.Sweep(new(80, -80, 0), new(-100, 100, 0), 12).Blocked, "3D free diagonal inside enclosing box remains passable");
        Check(three.Sweep(Vector3.Zero, new(-200, 0, 0), 12).Blocked, "Compound arm blocks sphere cast");
        var cfg = new BoidConfig { MaxSpeed = 100, MaxForce = 200 };
        Check(ObstacleAvoidance.Calculate(new(0, Vector2.Zero, Vector2.Left * 100), two, cfg) != Vector2.Zero, "Concave active avoidance");
        Check(ObstacleAvoidance3D.Calculate(new(0, Vector3.Zero, Vector3.Left * 100), three, cfg) != Vector3.Zero, "Compound active avoidance");
        for (int i = 0; i < 3; i++)
        {
            Vector2 from = i == 0 ? new(-80, -80) : i == 1 ? new(-80, 0) : new(0, -80);
            Vector2 point = from, velocity = Vector2.Zero;
            var result = ObstacleContact2D.Resolve(from, ref point, ref velocity, two, 12);
            Check(result.Recovered && !two.Occupied(point, 11.95f), $"2D deep seam recovery {i}: {point}");
            Vector3 start = i == 0 ? new(-80, 80, 0) : i == 1 ? new(-80, 0, 0) : new(0, 80, 0);
            Vector3 p = start, v = Vector3.Zero;
            result = ObstacleContact3D.Resolve(start, ref p, ref v, three, 12);
            Check(result.Recovered && !three.Occupied(p, 11.95f), $"3D deep seam recovery {i}: {p}");
            Vector3 previous = p;
            for (int step = 0; step < 20; step++) ObstacleContact3D.Resolve(p, ref p, ref v, three, 12);
            Check(p.DistanceTo(previous) < 0.1f, "Compound recovery does not oscillate on seams");
        }
        l.Rotation = 0.6f; compound.Rotation = new(0.3f, 0.5f, 0.6f);
        two.Synchronize(new[] { l }); three.Synchronize(new[] { compound });
        Check(!two.Occupied(l.ToGlobal(Vector2.Zero), 12) && two.Occupied(l.ToGlobal(new(-80, 0)), 0), "Rotated L keeps notch");
        Check(!three.Occupied(compound.GlobalPosition, 12) && three.Occupied(compound.ToGlobal(new(-80, 0, 0)), 0), "Rotated compound keeps notch");
        l.Free(); compound.Free();
    }

    private void Contacts()
    {
        using var query = new ObstacleQuery3D();
        var wall = new Obstacle3D(); wall.Initialize(ObstacleShape.Box, new(16, 200, 200)); AddChild(wall);
        foreach (float clearance in new[] { 0f, 12f, 100f })
        {
            query.Synchronize(new[] { wall });
            Vector3 p = new(10000, 0, 0), v = new(20000, 0, 0);
            var result = ObstacleContact3D.Resolve(new(-10000, 0, 0), ref p, ref v, query, clearance);
            Check(result.Recovered && p.X <= -8 - clearance + 0.03f && p.X > -8 - clearance - 0.2f, $"Long thin-wall cast radius {clearance}: {p}");
            Check(v.Length() < 0.03f, "Head-on no bounce");
        }
        wall.Position = new(1000000, -1000000, 1000000);
        query.Synchronize(new[] { wall });
        Vector3 far = wall.Position + Vector3.Right * 200, velocity = Vector3.Right * 400;
        ObstacleContact3D.Resolve(wall.Position - Vector3.Right * 200, ref far, ref velocity, query, 12);
        Check(far.X <= wall.Position.X - 19.8f && far.X >= wall.Position.X - 20.3f, "Million-unit coordinates retain finite conservative contact");
        wall.Free();

        var xWall = new Obstacle3D(); xWall.Initialize(ObstacleShape.Box, new(16, 400, 400)); AddChild(xWall);
        var yWall = new Obstacle3D(); yWall.Initialize(ObstacleShape.Box, new(400, 16, 400)); AddChild(yWall);
        query.Synchronize(new[] { xWall, yWall });
        Vector3 corner = new(100, 100, 50), cornerVelocity = new(200, 200, 50);
        var contactDebug = new ObstacleDebug3D();
        var cornerResult = ObstacleContact3D.Resolve(new(-100, -100, 0), ref corner, ref cornerVelocity, query, 12, contactDebug);
        Check(cornerResult.Recovered && corner.X <= -19.95f && corner.Y <= -19.95f && Math.Abs(corner.Z - 50) < 0.1f,
            $"Multi-plane corner preserves unconstrained slide: {corner}");
        xWall.Free(); yWall.Free();
    }

    private void Boundaries()
    {
        using var query = new ObstacleQuery2D();
        var bounds = new Rect2(0, 0, 100, 100);
        query.Synchronize(Array.Empty<Obstacle2D>(), bounds);
        Vector2 p = new(340, 350), v = new(310, 310);
        var result = ObstacleContact2D.Resolve(new(30, 40), ref p, ref v, query, 0);
        Check(p.DistanceTo(new(40, 50)) < 0.01f && !result.MotionBudgetExhausted, "Multiple corner wraps split motion");
        p = new(100000, 100000); v = Vector2.One * 100000;
        result = ObstacleContact2D.Resolve(new(30, 30), ref p, ref v, query, 0);
        Check(result.MotionBudgetExhausted && bounds.HasPoint(p), "Budget exhaustion discards remaining displacement");
        var huge = new Obstacle2D { Position = new(50, 50) }; huge.Initialize(ObstacleShape.Rectangle, new(1000, 1000));
        query.Synchronize(new[] { huge }, bounds);
        p = new(50, 50); v = Vector2.One;
        result = ObstacleContact2D.Resolve(p, ref p, ref v, query, 12);
        Check(!result.Recovered && v == Vector2.Zero && p == new Vector2(50, 50), "No free wrapped space reports recovery failure without oscillation");
        huge.Free();
    }

    private void Sampling()
    {
        using var query = new ObstacleQuery3D();
        var wall = new Obstacle3D { Position = new(500, 0, 0) }; wall.Initialize(ObstacleShape.Box, Vector3.One * 100); AddChild(wall);
        query.Synchronize(new[] { wall });
        var config = new BoidConfig { MaxSpeed = 120, MaxForce = 200 };
        var debug = new ObstacleDebug3D();
        Vector3 a = ObstacleAvoidance3D.Calculate(new(0, Vector3.Zero, Vector3.Forward * 120), query, config, debug: debug);
        Check(a == Vector3.Zero && debug.Candidates.Count == 1 && query.QueryCount <= 2, "Clear forward path avoids candidate fan");
        wall.Position = Vector3.Forward * 100; query.Synchronize(new[] { wall }); debug.Clear();
        a = ObstacleAvoidance3D.Calculate(new(0, Vector3.Zero, Vector3.Forward * 120), query, config, debug: debug);
        Check(a != Vector3.Zero && a.IsFinite() && debug.Candidates.Count <= ObstacleTuning.Directions3D + 1, "-Z basis and bounded candidate fan");
        for (int step = 0; step < 20; step++)
            Check(ObstacleAvoidance3D.Calculate(new(0, Vector3.Zero, Vector3.Forward * 120), query, config).DistanceTo(a) < 0.0001f, "Stable repeated selection");
        config.MaxForce = 0;
        Check(ObstacleAvoidance3D.Calculate(new(0, Vector3.Zero, Vector3.Zero), query, config) == Vector3.Zero, "Zero force no infinite lookahead");
        wall.Free();
    }

    private void ManagerSync()
    {
        foreach (int dimension in new[] { 2, 3 })
        {
            Node node = GD.Load<PackedScene>($"res://scenes/worlds/boids_world_{dimension}d.tscn").Instantiate();
            var world = (ISimulationWorld)node;
            world.Initialize(new BoidConfig { BoidCount = 0, MaxForce = 0 }); AddChild(node);
            if (node is BoidManager two)
            {
                ObstacleHarness2D.ClearLayout(two);
                var editor = two.GetNode<ObstacleEditor2D>("ObstacleEditor");
                Vector2 center = two.ViewBounds.GetCenter();
                var boid = two.SpawnBoid(center, Vector2.Zero);
                var obstacle = editor.AddObstacle(ObstacleShape.Rectangle, center + Vector2.Right * 200);
                obstacle.Position = center;
                Check(boid.Position == center, "2D paused edits never advance boid");
                two.StepOnce();
                Check(!two.IsRunning && boid.Position.DistanceTo(center) > 50 && two.UnresolvedContacts == 0, "2D one step sees just-edited geometry");
                two.Reset(); Check(editor.Obstacles.Count == 1, "2D reset keeps layout");
            }
            else
            {
                var three = (BoidManager3D)node; var editor = three.GetNode<ObstacleEditor3D>("ObstacleEditor");
                var boid = three.SpawnBoid(Vector3.Zero, Vector3.Zero);
                var obstacle = editor.AddObstacle(ObstacleShape.Sphere, Vector3.Right * 200); obstacle.Position = Vector3.Zero;
                Check(boid.Position == Vector3.Zero, "3D paused edits never advance boid");
                three.StepOnce();
                Check(!three.IsRunning && boid.Position.Length() > 50 && three.UnresolvedContacts == 0, "3D one step sees edited geometry");
                three.Reset(); Check(editor.Obstacles.Count == 1, "3D reset keeps layout");
            }
            RemoveChild(node); node.Free();
        }
    }

    private void Enclosed()
    {
        using var query = new ObstacleQuery3D();
        var walls = new Obstacle3D[6];
        for (int i = 0; i < walls.Length; i++)
        {
            Vector3 position = Vector3.Zero, size = Vector3.One * 200;
            position[i / 2] = i % 2 == 0 ? -50 : 50;
            size[i / 2] = 16;
            walls[i] = new Obstacle3D { Position = position };
            walls[i].Initialize(ObstacleShape.Box, size);
            AddChild(walls[i]);
        }
        query.Synchronize(walls);
        var config = new BoidConfig { MaxSpeed = 120, MaxForce = 200 };
        var debug = new ObstacleDebug3D();
        Vector3 acceleration = ObstacleAvoidance3D.Calculate(new(0, Vector3.Zero, Vector3.Forward * 120), query, config, debug: debug);
        Check(debug.Candidates.Count == ObstacleTuning.Directions3D + 1, "All-blocked case exhausts bounded fan");
        Check(debug.Candidates.TrueForAll(c => c.Blocked) && debug.Choice.Length() < config.MaxSpeed * ObstacleTuning.ResponseSeconds,
            "All blocked directions retain blocked status and reduce target speed");
        Check(acceleration.IsFinite() && acceleration.Length() <= config.MaxForce + 0.001f, "Conservative fallback remains finite and capped");
        Vector3 point = Vector3.Zero, velocity = Vector3.Forward * 120;
        for (int i = 0; i < 120; i++)
        {
            Vector3 start = point;
            velocity = (velocity + ObstacleAvoidance3D.Calculate(new(0, point, velocity), query, config) / 60).LimitLength(config.MaxSpeed);
            point += velocity / 60;
            ObstacleContact3D.Resolve(start, ref point, ref velocity, query, config.ObstacleClearance);
            Check(Mathf.Max(point.Abs().X, Mathf.Max(point.Abs().Y, point.Abs().Z)) <= 30.1f,
                "Closed room remains contained; local avoidance is not global pathfinding");
        }
        foreach (var wall in walls) wall.Free();
    }

    private void SearchEquivalence()
    {
        foreach (int dimension in new[] { 2, 3 })
        {
            Node[] worlds = new Node[2];
            for (int mode = 0; mode < 2; mode++)
            {
                Node node = GD.Load<PackedScene>($"res://scenes/worlds/boids_world_{dimension}d.tscn").Instantiate();
                ((ISimulationWorld)node).Initialize(new BoidConfig { BoidCount = 0, NeighborSearchMode = (NeighborSearchMode)mode });
                AddChild(node); worlds[mode] = node;
                if (node is BoidManager two)
                {
                    ObstacleHarness2D.ClearLayout(two);
                    Vector2 center = two.ViewBounds.GetCenter();
                    var obstacle = GD.Load<PackedScene>("res://scenes/obstacles/l_shape_2d.tscn").Instantiate<Obstacle2D>();
                    obstacle.Position = center;
                    two.GetNode<ObstacleEditor2D>("ObstacleEditor").RegisterObstacle(obstacle);
                    for (int i = 0; i < 40; i++) two.SpawnBoid(center + new Vector2(-250 + i % 5 * 25, (i / 5 - 4) * 25), Vector2.Right * 100);
                }
                else
                {
                    var three = (BoidManager3D)node;
                    var obstacle = GD.Load<PackedScene>("res://scenes/obstacles/compound_3d.tscn").Instantiate<Obstacle3D>();
                    three.GetNode<ObstacleEditor3D>("ObstacleEditor").RegisterObstacle(obstacle);
                    for (int i = 0; i < 40; i++) three.SpawnBoid(new Vector3(-250 + i % 5 * 25, (i / 5 - 4) * 25, 0), Vector3.Right * 100);
                }
            }
            for (int step = 0; step < 180; step++)
            {
                if (dimension == 2)
                {
                    var a = (BoidManager)worlds[0]; var b = (BoidManager)worlds[1]; a.Step(1.0 / 60); b.Step(1.0 / 60);
                    for (int i = 0; i < 40; i++) Check(a.Boids[i].Position.DistanceTo(b.Boids[i].Position) < 0.01f && a.Boids[i].Velocity.DistanceTo(b.Boids[i].Velocity) < 0.01f,
                        "2D snapshot trajectories agree with irregular obstacles in naive/grid modes");
                }
                else
                {
                    var a = (BoidManager3D)worlds[0]; var b = (BoidManager3D)worlds[1]; a.Step(1.0 / 60); b.Step(1.0 / 60);
                    for (int i = 0; i < 40; i++) Check(a.Boids[i].Position.DistanceTo(b.Boids[i].Position) < 0.01f && a.Boids[i].Velocity.DistanceTo(b.Boids[i].Velocity) < 0.01f,
                        "3D snapshot trajectories agree with irregular obstacles in naive/grid modes");
                }
            }
            foreach (Node world in worlds) { RemoveChild(world); world.Free(); }
        }
    }

    private void Ties()
    {
        using var query = new ObstacleQuery3D();
        var first = new Obstacle3D(); first.Initialize(ObstacleShape.Box, Vector3.One * 100); AddChild(first);
        var second = new Obstacle3D(); second.Initialize(ObstacleShape.Box, Vector3.One * 100); AddChild(second);
        query.Synchronize(new[] { second, first }); // 反转注册顺序，不能把偶然的 native 返回顺序当成并列选择。
        var hit = query.Sweep(Vector3.Left * 200, Vector3.Right * 400, 12);
        Check(hit.Blocked && hit.ObstacleId == Math.Min(first.GetInstanceId(), second.GetInstanceId()), "Equal contacts have stable logical identity");
        query.HitDistance(Vector3.Left * 200, Vector3.Right, 400, out ulong picked, first.GetInstanceId());
        Check(picked == first.GetInstanceId(), "Picking exact overlap preserves selected obstacle priority");
        query.HitDistance(Vector3.Left * 200, Vector3.Right, 400, out picked, second.GetInstanceId());
        Check(picked == second.GetInstanceId(), "Picking overlap can preserve either selected identity");
        first.Free(); second.Free();
    }
}
