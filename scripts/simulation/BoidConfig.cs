using System;
using Godot;

namespace BoidsLab;

/// <summary>UI、模拟与画布共享此实例；数值范围和默认值只在此处定义。</summary>
public sealed class BoidConfig
{
    public const string SavePath = "user://boids_lab.cfg";

    public readonly record struct NumberRange(double Min, double Max, double Step, double Default)
    {
        public double Constrain(double value) => double.IsFinite(value)
            ? Math.Clamp(Math.Round(value / Step) * Step, Min, Max)
            : Default;
    }

    public static readonly NumberRange CountRange = new(0, 5000, 1, 200);
    public static readonly NumberRange SeedRange = new(0, int.MaxValue, 1, 42);
    public static readonly NumberRange RadiusRange = new(1, 500, 1, 80);
    public static readonly NumberRange SeparationRadiusRange = new(1, 500, 1, 25);
    public static readonly NumberRange SpeedRange = new(0, 1000, 1, 120);
    public static readonly NumberRange ForceRange = new(0, 2000, 1, 200);
    public static readonly NumberRange AngularSpeedRange = new(0, 1440, 1, 360);
    public static readonly NumberRange AngularAccelerationRange = new(0, 7200, 1, 720);
    public static readonly NumberRange SeparationRange = new(0, 5, 0.1, 1.5);
    public static readonly NumberRange AlignmentRange = new(0, 5, 0.1, 1);
    public static readonly NumberRange CohesionRange = new(0, 5, 0.1, 1);
    public static readonly NumberRange AvoidanceLookAheadRange = new(0.1, 3, 0.05, 0.75);
    public static readonly NumberRange ObstacleClearanceRange = new(0, 100, 1, 12);
    public static readonly NumberRange LeaderFollowDistanceRange = new(16, 500, 1, 80);
    public static readonly NumberRange LeaderFollowWeightRange = new(0, 5, 0.1, 1.5);
    public static readonly NumberRange WorldWidthRange = new(100, 100000, 10, 2560);
    public static readonly NumberRange WorldHeightRange = new(100, 100000, 10, 1440);
    public static readonly NumberRange WorldDepthRange = new(100, 100000, 10, 1440);
    public static readonly NumberRange FollowCameraDistanceRange = new(80, 2000, 1, 300);

    public int BoidCount { get; set; } = (int)CountRange.Default;
    public int RandomSeed { get; set; } = (int)SeedRange.Default;
    public float PerceptionRadius { get; set; } = (float)RadiusRange.Default;
    public float SeparationRadius { get; set; } = (float)SeparationRadiusRange.Default;
    public NeighborSearchMode NeighborSearchMode { get; set; } = NeighborSearchMode.Naive;
    public float MaxSpeed { get; set; } = (float)SpeedRange.Default;
    public float MaxForce { get; set; } = (float)ForceRange.Default;
    public float MaxAngularSpeedDegrees { get; set; } = (float)AngularSpeedRange.Default;
    public float MaxAngularAccelerationDegrees { get; set; } = (float)AngularAccelerationRange.Default;
    public float SeparationWeight { get; set; } = (float)SeparationRange.Default;
    public float AlignmentWeight { get; set; } = (float)AlignmentRange.Default;
    public float CohesionWeight { get; set; } = (float)CohesionRange.Default;
    public bool SeparationEnabled { get; set; } = true;
    public bool AlignmentEnabled { get; set; } = true;
    public bool CohesionEnabled { get; set; } = true;
    public bool AvoidanceEnabled { get; set; } = true;
    public float AvoidanceLookAhead { get; set; } = (float)AvoidanceLookAheadRange.Default;
    public float ObstacleClearance { get; set; } = (float)ObstacleClearanceRange.Default;
    public float LeaderFollowDistance { get; set; } = (float)LeaderFollowDistanceRange.Default;
    public float LeaderFollowWeight { get; set; } = (float)LeaderFollowWeightRange.Default;
    public float WorldWidth { get; set; } = (float)WorldWidthRange.Default;
    public float WorldHeight { get; set; } = (float)WorldHeightRange.Default;
    public float WorldDepth { get; set; } = (float)WorldDepthRange.Default;
    public float FollowCameraDistance { get; set; } = (float)FollowCameraDistanceRange.Default;
    public bool ShowGrid { get; set; } = true;
    public bool ShowBoundary { get; set; }
    public bool ShowNeighbors { get; set; }
    public bool ShowPerceptionRadius { get; set; }
    public bool ShowSeparationRadius { get; set; }
    public bool ShowVelocity { get; set; }

    public void Normalize()
    {
        BoidCount = (int)CountRange.Constrain(BoidCount);
        RandomSeed = (int)SeedRange.Constrain(RandomSeed);
        PerceptionRadius = (float)RadiusRange.Constrain(PerceptionRadius);
        SeparationRadius = (float)SeparationRadiusRange.Constrain(SeparationRadius);
        if (!Enum.IsDefined(NeighborSearchMode)) NeighborSearchMode = NeighborSearchMode.Naive;
        MaxSpeed = (float)SpeedRange.Constrain(MaxSpeed);
        MaxForce = (float)ForceRange.Constrain(MaxForce);
        MaxAngularSpeedDegrees = (float)AngularSpeedRange.Constrain(MaxAngularSpeedDegrees);
        MaxAngularAccelerationDegrees = (float)AngularAccelerationRange.Constrain(MaxAngularAccelerationDegrees);
        SeparationWeight = (float)SeparationRange.Constrain(SeparationWeight);
        AlignmentWeight = (float)AlignmentRange.Constrain(AlignmentWeight);
        CohesionWeight = (float)CohesionRange.Constrain(CohesionWeight);
        AvoidanceLookAhead = (float)AvoidanceLookAheadRange.Constrain(AvoidanceLookAhead);
        ObstacleClearance = (float)ObstacleClearanceRange.Constrain(ObstacleClearance);
        LeaderFollowDistance = (float)LeaderFollowDistanceRange.Constrain(LeaderFollowDistance);
        LeaderFollowWeight = (float)LeaderFollowWeightRange.Constrain(LeaderFollowWeight);
        WorldWidth = (float)WorldWidthRange.Constrain(WorldWidth);
        WorldHeight = (float)WorldHeightRange.Constrain(WorldHeight);
        WorldDepth = (float)WorldDepthRange.Constrain(WorldDepth);
        FollowCameraDistance = (float)FollowCameraDistanceRange.Constrain(FollowCameraDistance);
    }

    public Error Load(string path = SavePath)
    {
        var file = new ConfigFile();
        Error error = file.Load(path);
        if (error != Error.Ok) return error;

        // 类型损坏的条目回退到当前值；有效数字统一经过范围检查。
        double Number(string key, double fallback)
        {
            Variant value = file.GetValue("simulation", key, fallback);
            return value.VariantType is Variant.Type.Int or Variant.Type.Float
                ? value.AsDouble() : fallback;
        }
        bool Flag(string section, string key, bool fallback)
        {
            Variant value = file.GetValue(section, key, fallback);
            return value.VariantType == Variant.Type.Bool ? value.AsBool() : fallback;
        }

        BoidCount = (int)CountRange.Constrain(Number("boid_count", BoidCount));
        RandomSeed = (int)SeedRange.Constrain(Number("random_seed", RandomSeed));
        PerceptionRadius = (float)RadiusRange.Constrain(Number("perception_radius", PerceptionRadius));
        SeparationRadius = (float)SeparationRadiusRange.Constrain(Number("separation_radius", SeparationRadius));
        double searchMode = Number("neighbor_search_mode", (int)NeighborSearchMode.Naive);
        NeighborSearchMode = searchMode == (int)NeighborSearchMode.UniformGrid
            ? NeighborSearchMode.UniformGrid : NeighborSearchMode.Naive;
        MaxSpeed = (float)SpeedRange.Constrain(Number("max_speed", MaxSpeed));
        MaxForce = (float)ForceRange.Constrain(Number("max_force", MaxForce));
        MaxAngularSpeedDegrees = (float)AngularSpeedRange.Constrain(Number("max_angular_speed_degrees", MaxAngularSpeedDegrees));
        MaxAngularAccelerationDegrees = (float)AngularAccelerationRange.Constrain(Number("max_angular_acceleration_degrees", MaxAngularAccelerationDegrees));
        SeparationWeight = (float)SeparationRange.Constrain(Number("separation_weight", SeparationWeight));
        AlignmentWeight = (float)AlignmentRange.Constrain(Number("alignment_weight", AlignmentWeight));
        CohesionWeight = (float)CohesionRange.Constrain(Number("cohesion_weight", CohesionWeight));
        SeparationEnabled = Flag("simulation", "separation_enabled", SeparationEnabled);
        AlignmentEnabled = Flag("simulation", "alignment_enabled", AlignmentEnabled);
        CohesionEnabled = Flag("simulation", "cohesion_enabled", CohesionEnabled);
        AvoidanceEnabled = Flag("simulation", "avoidance_enabled", AvoidanceEnabled);
        AvoidanceLookAhead = (float)AvoidanceLookAheadRange.Constrain(Number("avoidance_look_ahead", AvoidanceLookAhead));
        ObstacleClearance = (float)ObstacleClearanceRange.Constrain(Number("obstacle_clearance", ObstacleClearance));
        LeaderFollowDistance = (float)LeaderFollowDistanceRange.Constrain(Number("leader_follow_distance", LeaderFollowDistance));
        LeaderFollowWeight = (float)LeaderFollowWeightRange.Constrain(Number("leader_follow_weight", LeaderFollowWeight));
        WorldWidth = (float)WorldWidthRange.Constrain(Number("world_width_3d", WorldWidth));
        WorldHeight = (float)WorldHeightRange.Constrain(Number("world_height_3d", WorldHeight));
        WorldDepth = (float)WorldDepthRange.Constrain(Number("world_depth_3d", WorldDepth));
        FollowCameraDistance = (float)FollowCameraDistanceRange.Constrain(Number("follow_camera_distance_3d", FollowCameraDistance));
        ShowGrid = Flag("visualization", "show_grid", ShowGrid);
        ShowBoundary = Flag("visualization", "show_boundary_3d", ShowBoundary);
        ShowNeighbors = Flag("visualization", "show_neighbors", ShowNeighbors);
        ShowPerceptionRadius = Flag("visualization", "show_perception_radius", ShowPerceptionRadius);
        ShowSeparationRadius = Flag("visualization", "show_separation_radius", ShowSeparationRadius);
        ShowVelocity = Flag("visualization", "show_velocity", ShowVelocity);
        return Error.Ok;
    }

    public Error Save(string path = SavePath)
    {
        Normalize();
        var file = new ConfigFile();
        file.SetValue("simulation", "boid_count", BoidCount);
        file.SetValue("simulation", "random_seed", RandomSeed);
        file.SetValue("simulation", "perception_radius", PerceptionRadius);
        file.SetValue("simulation", "separation_radius", SeparationRadius);
        file.SetValue("simulation", "neighbor_search_mode", (int)NeighborSearchMode);
        file.SetValue("simulation", "max_speed", MaxSpeed);
        file.SetValue("simulation", "max_force", MaxForce);
        file.SetValue("simulation", "max_angular_speed_degrees", MaxAngularSpeedDegrees);
        file.SetValue("simulation", "max_angular_acceleration_degrees", MaxAngularAccelerationDegrees);
        file.SetValue("simulation", "separation_weight", SeparationWeight);
        file.SetValue("simulation", "alignment_weight", AlignmentWeight);
        file.SetValue("simulation", "cohesion_weight", CohesionWeight);
        file.SetValue("simulation", "separation_enabled", SeparationEnabled);
        file.SetValue("simulation", "alignment_enabled", AlignmentEnabled);
        file.SetValue("simulation", "cohesion_enabled", CohesionEnabled);
        file.SetValue("simulation", "avoidance_enabled", AvoidanceEnabled);
        file.SetValue("simulation", "avoidance_look_ahead", AvoidanceLookAhead);
        file.SetValue("simulation", "obstacle_clearance", ObstacleClearance);
        file.SetValue("simulation", "leader_follow_distance", LeaderFollowDistance);
        file.SetValue("simulation", "leader_follow_weight", LeaderFollowWeight);
        file.SetValue("simulation", "world_width_3d", WorldWidth);
        file.SetValue("simulation", "world_height_3d", WorldHeight);
        file.SetValue("simulation", "world_depth_3d", WorldDepth);
        file.SetValue("simulation", "follow_camera_distance_3d", FollowCameraDistance);
        file.SetValue("visualization", "show_grid", ShowGrid);
        file.SetValue("visualization", "show_boundary_3d", ShowBoundary);
        file.SetValue("visualization", "show_neighbors", ShowNeighbors);
        file.SetValue("visualization", "show_perception_radius", ShowPerceptionRadius);
        file.SetValue("visualization", "show_separation_radius", ShowSeparationRadius);
        file.SetValue("visualization", "show_velocity", ShowVelocity);
        return file.Save(path);
    }
}
