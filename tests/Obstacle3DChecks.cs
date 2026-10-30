using System;
using Godot;

namespace BoidsLab.Tests;

public partial class Obstacle3DChecks : Node
{
    private int _checks;
    private static readonly Aabb Bounds = new(Vector3.One * -500, Vector3.One * 1000);
    private static readonly ObstacleShape[] Shapes = { ObstacleShape.Box, ObstacleShape.Sphere };

    public override void _Ready()
    {
        if (DisplayServer.GetName() != "headless") { GetTree().Quit(2); return; }
        try
        {
            CheckGeometryAndPrediction();
            CheckContinuousMotion();
            CheckManager();
            GD.Print($"PASS: {_checks} 3D obstacle geometry/avoidance/contact/integration assertions (headless).");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private Obstacle3D Shape(ObstacleShape kind, Vector3 position, Vector3 dimensions)
    {
        var obstacle = new Obstacle3D { Position = position };
        obstacle.Initialize(kind, dimensions);
        AddChild(obstacle);
        return obstacle;
    }

    private void CheckGeometryAndPrediction()
    {
        var config = new BoidConfig { MaxSpeed = 200, MaxForce = 200, ObstacleClearance = 12, AvoidanceLookAhead = 0.75f };
        foreach (ObstacleShape kind in Shapes)
        {
            Obstacle3D shape = Shape(kind, Vector3.Zero, Vector3.One * 100);
            Obstacle3D[] list = { shape };
            foreach (Vector3 axis in new[] { Vector3.Right, Vector3.Up, Vector3.Back })
            {
                Vector3 point = -axis * 200, velocity = axis * 200;
                Vector3 acceleration = ObstacleAvoidance3D.Calculate(new(0, point, velocity), list, config);
                Check(acceleration.Dot(axis) < 0 && acceleration.Cross(axis).Length() > 0, "Head-on approach brakes and chooses a 3D tangent");
                Check(acceleration.IsFinite() && acceleration.Length() <= config.MaxForce + 0.001, "Avoidance obeys acceleration cap");
                Vector3 opposite = ObstacleAvoidance3D.Calculate(new(1, point, velocity), list, config);
                Near(acceleration - axis * acceleration.Dot(axis), -(opposite - axis * opposite.Dot(axis)), "IDs choose opposite head-on tangents");
                Near(ObstacleAvoidance3D.Calculate(new(0, point, -velocity), list, config), Vector3.Zero, "Departing boid is not turned back");
                Near(ObstacleAvoidance3D.Calculate(new(0, point, Vector3.Zero), list, config), Vector3.Zero, "Distant stationary boid remains still");
                Near(ObstacleAvoidance3D.Calculate(new(0, -axis * 62, -velocity), list, config), Vector3.Zero, "Departing near contact keeps moving away");
                Check(ObstacleAvoidance3D.Calculate(new(0, -axis * 62, Vector3.Zero), list, config).Length() > 0, "Stopped surface contact receives escape steering");
                ObstacleAvoidance3D.Surface(-axis * 70, shape, 12, out float distance, out Vector3 normal);
                Check(Mathf.IsEqualApprox(distance, 8), "Clearance is measured from boid center to inflated surface");
                Near(normal, -axis, "Surface normal points outward");
            }
            config.AvoidanceEnabled = false;
            Near(ObstacleAvoidance3D.Calculate(new(0, Vector3.Left * 200, Vector3.Right * 200), list, config), Vector3.Zero, "Disabled avoidance has no force");
            config.AvoidanceEnabled = true;
            config.MaxForce = 0;
            Near(ObstacleAvoidance3D.Calculate(new(0, Vector3.Left * 200, Vector3.Right * 200), list, config), Vector3.Zero, "Zero force does not divide by zero");
            config.MaxForce = 200;
            shape.QueueFree();
        }
        var sphere = Shape(ObstacleShape.Sphere, Vector3.Zero, new Vector3(100, 200, 300));
        Near(sphere.Dimensions, Vector3.One * 100, "Sphere always uses one diameter");
        sphere.SetDimensions(new Vector3(1, 4000, -2));
        Near(sphere.Dimensions, Vector3.One * ObstacleSize.Min, "Sphere diameter has a lower bound");
        sphere.SetDimensions(Vector3.One * 5000);
        Near(sphere.Dimensions, Vector3.One * ObstacleSize.Max, "Sphere diameter has an upper bound");
        var box = Shape(ObstacleShape.Box, Vector3.Zero, new Vector3(1, 80, 4000));
        Near(box.Dimensions, new Vector3(16, 80, 2000), "Box dimensions clamp independently");
        sphere.QueueFree();
        box.QueueFree();
    }

    private void CheckContinuousMotion()
    {
        foreach (ObstacleShape kind in Shapes)
        {
            Obstacle3D obstacle = Shape(kind, Vector3.Zero, Vector3.One * 16);
            Obstacle3D[] list = { obstacle };
            foreach (Vector3 axis in new[] { Vector3.Right, Vector3.Up, Vector3.Back })
            {
                Vector3 from = -axis * 400, position = axis * 400, velocity = axis * 800;
                ObstacleAvoidance3D.ResolveMotion(from, ref position, ref velocity, list, 12);
                Near(position, -axis * 20.01f, "High-speed motion cannot cross a thin obstacle");
                Near(velocity, Vector3.Zero, "Contact removes inward velocity");
                position = Vector3.Zero;
                velocity = axis * 100;
                ObstacleAvoidance3D.ResolveMotion(Vector3.Zero, ref position, ref velocity, list, 12);
                ObstacleAvoidance3D.Surface(position, obstacle, 12, out float distance, out _);
                Check(distance >= 0 && position.IsFinite() && velocity.IsFinite(), "Initial overlap is recovered with a finite normal");
                foreach (int sign in new[] { -1, 1 })
                {
                    Vector3 direction = axis * sign;
                    obstacle.Position = -direction * 470;
                    from = direction * 490;
                    position = from + direction * 100;
                    velocity = direction * 100;
                    ObstacleAvoidance3D.ResolveMotion(from, ref position, ref velocity, list, 5);
                    Near(position, from + direction * 100, "Opposite-face obstacle is not on the motion path");
                    Near(velocity, direction * 100, "Crossing the former boundary preserves velocity");
                    var config = new BoidConfig { MaxSpeed = 60, MaxForce = 200, AvoidanceLookAhead = 0.75f, ObstacleClearance = 5 };
                    Vector3 acceleration = ObstacleAvoidance3D.Calculate(new(0, from, direction * 60), list, config);
                    Near(acceleration, Vector3.Zero, "Prediction has no periodic obstacle copy");
                    obstacle.Position = direction * 530;
                    position = from + direction * 100;
                    ObstacleAvoidance3D.ResolveMotion(from, ref position, ref velocity, list, 5);
                    Near(position, direction * 516.99f, "An obstacle outside the former box still blocks contact");
                    Check(ObstacleAvoidance3D.Calculate(new(0, from, direction * 60), list, config).Length() > 0,
                        "Prediction sees obstacles outside the observation box");
                }
                obstacle.Position = Vector3.Zero;
            }
            obstacle.QueueFree();
        }
        var wall = Shape(ObstacleShape.Box, Vector3.Zero, new Vector3(16, 200, 200));
        Vector3 slide = new(100, 40, 50), slideVelocity = new(200, 40, 50);
        ObstacleAvoidance3D.ResolveMotion(new Vector3(-100, 0, 0), ref slide, ref slideVelocity, new[] { wall }, 0);
        Near(slide, new Vector3(-8.01f, 40, 50), "Contact preserves both tangential motion components");
        Near(slideVelocity, new Vector3(0, 40, 50), "Contact projects velocity onto the surface plane");
        wall.Position = new Vector3(0, 300, 0);
        Vector3 multiple = new(2490, -2490, 1490), speed = new(1, 2, 3);
        Vector3 expected = multiple;
        ObstacleAvoidance3D.ResolveMotion(Vector3.Zero, ref multiple, ref speed, Array.Empty<Obstacle3D>(), 0);
        Near(multiple, expected, "Unobstructed multi-axis multi-width movement is continuous", 0.002f);
        Near(speed, new Vector3(1, 2, 3), "Unobstructed motion preserves velocity");
        wall.QueueFree();
    }

    private void CheckManager()
    {
        foreach (ObstacleShape kind in Shapes)
        {
            var config = new BoidConfig { BoidCount = 0, MaxSpeed = 120, MaxForce = 200,
                SeparationEnabled = false, AlignmentEnabled = false, CohesionEnabled = false };
            var world = GD.Load<PackedScene>("res://scenes/worlds/boids_world_3d.tscn").Instantiate<BoidManager3D>();
            world.Initialize(config);
            AddChild(world);
            var editor = world.GetNode<ObstacleEditor3D>("ObstacleEditor");
            var obstacle = editor.AddObstacle(kind, Vector3.Zero);
            obstacle.SetDimensions(Vector3.One * 100);
            Boid3D boid = world.SpawnBoid(Vector3.Left * 240, Vector3.Right * 120);
            bool approached = false, passedSide = false;
            for (int i = 0; i < 300; i++)
            {
                world.Step(1.0 / 60);
                ObstacleAvoidance3D.Surface(boid.Position, obstacle, config.ObstacleClearance, out float distance, out _);
                Check(distance >= -0.001f && boid.Position.IsFinite() && boid.Velocity.IsFinite(), "Continuous avoidance stays outside both obstacle types");
                Check(boid.Velocity.Length() <= config.MaxSpeed + 0.001f && boid.Acceleration.Length() <= config.MaxForce + 0.001f,
                    "Continuous avoidance respects physical limits");
                approached |= boid.Position.X > -230;
                // 没有导航目标，不要求绕回原直线；离开障碍侧面并保持运动即可。
                passedSide |= (MathF.Abs(boid.Position.Z) > 82 || MathF.Abs(boid.Position.Y) > 82) && boid.Velocity.Length() > 30;
            }
            Check(approached && passedSide, $"Boid passes an obstacle side without parking: {kind}, position={boid.Position}, velocity={boid.Velocity}");
            Check(world.Stats.UpdateMilliseconds.HasValue, "Avoidance runs inside real measured update");
            world.Reset();
            Check(editor.Obstacles.Count == 1 && !world.IsRunning, "Reset retains layout and pauses");
            config.AvoidanceEnabled = false;
            boid = world.SpawnBoid(Vector3.Left * 100, Vector3.Right * 120);
            world.Step(0.8);
            Near(boid.Position, Vector3.Left * 4, "Disabled avoidance disables both steering and contact constraints");
            config.AvoidanceEnabled = true;
            world.Step(1.0 / 60);
            ObstacleAvoidance3D.Surface(boid.Position, obstacle, config.ObstacleClearance, out float recovered, out _);
            Check(recovered >= -0.001f, "Enabling avoidance recovers an existing overlap");
            editor.ResizeSelected(200, 200, 200);
            Check(world.Stats.UpdateMilliseconds is null, "Geometry edits invalidate old measurements");
            world.Step(1.0 / 60);
            ObstacleAvoidance3D.Surface(boid.Position, obstacle, config.ObstacleClearance, out recovered, out _);
            Check(recovered >= -0.001f, "Resized obstacle recovers penetration on next physical step");
            editor.DeleteSelected();
            Check(editor.Obstacles.Count == 0 && world.Stats.UpdateMilliseconds is null, "Deleting removes physical obstacle and invalidates timing");
            world.QueueFree();
        }
    }

    private void Near(Vector3 actual, Vector3 expected, string message, float tolerance = 0.001f) =>
        Check(actual.DistanceTo(expected) <= tolerance, $"{message}: {actual} != {expected}");
    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
