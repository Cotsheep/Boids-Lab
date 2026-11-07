using Godot;

namespace BoidsLab;

/// <summary>三维观察环境：空间边框、底面参考线与独立相机，不参与运动计算。</summary>
public partial class SimulationView3D : Node3D
{
    public DebugOverlayGeometry3D DebugGeometry { get; } = new();
    public LabPalette Palette { get; private set; } = LabPalette.Dark;
    private readonly BoidThemeMaterials _themeMaterials = new();
    private bool _privateEnvironment;
    internal void ApplyBoidPalette(Boid3D boid) => boid.ApplyPalette(_themeMaterials);

    public void ApplyPalette(LabPalette palette)
    {
        Palette = palette;
        _themeMaterials.Update(palette);
        if (_manager is null) return;
        var environment = GetNode<WorldEnvironment>("WorldEnvironment");
        if (!_privateEnvironment)
        {
            environment.Environment = (Environment)environment.Environment.Duplicate();
            _privateEnvironment = true;
        }
        environment.Environment.BackgroundColor = palette.Canvas;
        environment.Environment.AmbientLightColor = new Color("eee4d8");
        _nearCameraMaterial = null;
        foreach (Boid3D boid in _manager.Boids) ApplyBoidPalette(boid);
        _manager.GetNode<ObstacleEditor3D>("ObstacleEditor").ApplyPalette(palette);
        RebuildReferenceGeometry(_manager.WorldBounds);
        RefreshFollowRegionPalette();
        RefreshDebug();
    }
    private BoidConfig? _config;
    private BoidManager3D? _manager;
    private BaseMaterial3D? _nearCameraMaterial;
    private readonly ImmediateMesh _debugMesh = new();
    private readonly StandardMaterial3D _debugMaterial = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
    };
    private Node3D? _followRegion;
    private readonly StandardMaterial3D _followFillMaterial = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
    };
    private readonly StandardMaterial3D _followOutlineMaterial = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
    };

    public override void _Ready() => ProcessPriority = -10; // 先更新模型显示，再让默认优先级的相机跟随。

    public override void _Process(double delta)
    {
        if (_manager is null || !_manager.IsRunning) return;
        PreparePresentation((float)Engine.GetPhysicsInterpolationFraction());
        var camera = GetNode<SimulationCamera3D>("Camera3D");
        // 活动跟随相机稍后通过 ViewChanged 刷新；自由观察与冻结跟随也须更新调试锚点。
        if (!camera.IsFollowing || camera.IsFollowFrozen) RefreshDebug();
    }

    internal void PreparePresentation(float fraction)
    {
        if (_manager is null) return;
        foreach (Boid3D boid in _manager.Boids) boid.PreparePresentation(_manager.IsRunning ? fraction : 1);
        RefreshFollowRegion();
    }

    public void Bind(BoidConfig config, BoidManager3D manager)
    {
        _config = config;
        _manager = manager;
        GetNode<MeshInstance3D>("DebugOverlay").Mesh = _debugMesh;
        CreateFollowRegion();
    }

    internal (Vector3 Center, float Radius)? FollowRegion
    {
        get
        {
            if (_config?.ShowLeaderFollowRegion != true || _manager?.Leader is not { } leader
                || _config.LeaderFollowWeight <= 0 || _manager.Boids.Count <= 2) return null;
            float radius = LeaderSteering3D.FollowRadius(_manager.Boids.Count - 1, _config);
            Transform3D pose = leader.DisplayTransform;
            return (LeaderSteering3D.FollowPoint(pose.Origin, pose.Basis * Vector3.Forward,
                _config.LeaderFollowDistance + radius), radius);
        }
    }

    private void CreateFollowRegion()
    {
        if (_followRegion is not null) return;
        _followRegion = new Node3D { Name = "LeaderFollowRegion", Visible = false };
        AddChild(_followRegion);
        _followRegion.AddChild(new MeshInstance3D
        {
            Name = "Fill", Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 48, Rings = 24 },
            MaterialOverride = _followFillMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        var rings = new ImmediateMesh();
        rings.SurfaceBegin(Mesh.PrimitiveType.Lines);
        const int segments = 96;
        for (int plane = 0; plane < 3; plane++)
            for (int i = 0; i < segments; i++)
            {
                Vector2 a = Vector2.FromAngle(Mathf.Tau * i / segments);
                Vector2 b = Vector2.FromAngle(Mathf.Tau * (i + 1) / segments);
                AddLine(rings, plane == 0 ? new(a.X, a.Y, 0) : plane == 1 ? new(a.X, 0, a.Y) : new(0, a.X, a.Y),
                    plane == 0 ? new(b.X, b.Y, 0) : plane == 1 ? new(b.X, 0, b.Y) : new(0, b.X, b.Y));
            }
        rings.SurfaceEnd();
        _followRegion.AddChild(new MeshInstance3D
        {
            Name = "Outline", Mesh = rings, MaterialOverride = _followOutlineMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        RefreshFollowRegionPalette();
    }

    private void RefreshFollowRegionPalette()
    {
        _followOutlineMaterial.AlbedoColor = new Color(Palette.Leader, Palette.IsLight ? 0.46f : 0.45f);
    }

    private void RefreshFollowRegion()
    {
        if (_followRegion is null) return;
        if (FollowRegion is not { } region) { _followRegion.Visible = false; return; }
        _followRegion.Visible = true;
        Transform3D coordinates = GlobalTransform.AffineInverse() * _manager!.GetNode<Node3D>("Boids").GlobalTransform;
        _followRegion.Transform = coordinates * new Transform3D(Basis.Identity.Scaled(Vector3.One * region.Radius), region.Center);
        // 镜头进入跟随球时淡出填充，避免整幅画面蒙上一层颜色；轮廓仍保留深度遮挡。
        Vector3 scale = _followRegion.GlobalBasis.Scale.Abs();
        float worldRadius = Mathf.Max(scale.X, Mathf.Max(scale.Y, scale.Z));
        float distance = GetNode<SimulationCamera3D>("Camera3D").GlobalPosition.DistanceTo(_followRegion.GlobalPosition);
        float opacity = Mathf.SmoothStep(1, 1.4f, distance / Mathf.Max(worldRadius, 0.001f));
        _followFillMaterial.AlbedoColor = new Color(Palette.Leader, (Palette.IsLight ? 0.065f : 0.06f) * opacity);
    }

    public void Configure(Aabb bounds, bool resetCamera = true)
    {
        RebuildReferenceGeometry(bounds);
        var camera = GetNode<SimulationCamera3D>("Camera3D");
        if (resetCamera) camera.Configure(bounds);
        else camera.UpdateHomeBounds(bounds);
    }

    private void RebuildReferenceGeometry(Aabb bounds)
    {
        var mesh = new ImmediateMesh();
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
        };
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
        mesh.SurfaceSetColor(Palette.Frame);
        var corners = new Vector3[8];
        for (int i = 0; i < corners.Length; i++)
            corners[i] = bounds.Position + new Vector3(
                (i & 1) == 0 ? 0 : bounds.Size.X,
                (i & 2) == 0 ? 0 : bounds.Size.Y,
                (i & 4) == 0 ? 0 : bounds.Size.Z);
        for (int i = 0; i < corners.Length; i++)
            foreach (int axisBit in new[] { 1, 2, 4 })
                if ((i & axisBit) == 0) AddLine(mesh, corners[i], corners[i | axisBit]);

        mesh.SurfaceEnd();
        GetNode<MeshInstance3D>("ReferenceFrame").Mesh = mesh;

        var grid = new ImmediateMesh();
        grid.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
        grid.SurfaceSetColor(Palette.Grid);
        for (int i = 1; i < 10; i++)
        {
            float x = bounds.Position.X + bounds.Size.X * i / 10f;
            float z = bounds.Position.Z + bounds.Size.Z * i / 10f;
            AddLine(grid, new Vector3(x, bounds.Position.Y, bounds.Position.Z), new Vector3(x, bounds.Position.Y, bounds.End.Z));
            AddLine(grid, new Vector3(bounds.Position.X, bounds.Position.Y, z), new Vector3(bounds.End.X, bounds.Position.Y, z));
        }
        grid.SurfaceEnd();
        GetNode<MeshInstance3D>("Grid").Mesh = grid;
    }

    public void RefreshDebug()
    {
        if (_config is null || _manager is null) return;
        RefreshFollowRegion();
        RefreshNearCameraFade(_config, _manager);
        GetNode<MeshInstance3D>("Grid").Visible = _config.ShowGrid && _manager.Leader is null;
        GetNode<MeshInstance3D>("ReferenceFrame").Visible = _config.ShowBoundary && _manager.Leader is null;
        var camera = GetNode<SimulationCamera3D>("Camera3D");
        DebugGeometry.Build(_manager.Boids, _manager.SelectedBoid, _config, camera.UnitsPerPixelAt);
        _debugMesh.ClearSurfaces();
        bool avoidance = _config.ShowVelocity && _manager.SelectedBoid is { } selected
            && _manager.AvoidanceDebug.BoidId == selected.Id;
        if (DebugGeometry.RangePoints.Count + DebugGeometry.SeparationRangePoints.Count
            + DebugGeometry.NeighborPoints.Count + DebugGeometry.VelocityPoints.Count == 0 && !avoidance) return;
        _debugMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _debugMaterial);
        AddPoints(DebugGeometry.RangePoints, Palette.Perception);
        AddPoints(DebugGeometry.SeparationRangePoints, Palette.Separation);
        AddPoints(DebugGeometry.NeighborPoints, Palette.Selection);
        AddPoints(DebugGeometry.VelocityPoints, Palette.Velocity);
        if (avoidance)
        {
            var debug = _manager.AvoidanceDebug;
            Vector3 origin = _manager.SelectedBoid!.DisplayPosition;
            foreach (var candidate in debug.Candidates)
            {
                _debugMesh.SurfaceSetColor(candidate.Blocked ? Palette.ObstacleOutline : Palette.Perception);
                AddLine(_debugMesh, origin, origin + candidate.Motion);
            }
            _debugMesh.SurfaceSetColor(Palette.Velocity);
            AddLine(_debugMesh, origin, origin + debug.Probe);
            _debugMesh.SurfaceSetColor(Palette.Selection);
            AddLine(_debugMesh, origin, origin + debug.Choice);
            _debugMesh.SurfaceSetColor(Palette.Separation);
            foreach (Vector3 normal in debug.Normals) AddLine(_debugMesh, origin, origin + normal * 30);
        }
        _debugMesh.SurfaceEnd();
    }

    private void RefreshNearCameraFade(BoidConfig config, BoidManager3D manager)
    {
        bool enabled = config.FadeNearbyBoids && manager.Leader is not null;
        if (enabled && _nearCameraMaterial is null && manager.Boids.Count > 0
            && manager.Boids[0].NormalMaterial is BaseMaterial3D source)
        {
            // 同一世界复用一份材质副本；原始场景材质和身份高亮材质保持不变。
            // PixelAlpha 距离淡化可用于当前 Compatibility 渲染器。
            _nearCameraMaterial = (BaseMaterial3D)source.Duplicate();
            _nearCameraMaterial.DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha;
            _nearCameraMaterial.DistanceFadeMinDistance = 120;
            _nearCameraMaterial.DistanceFadeMaxDistance = 240;
        }
        foreach (Boid3D boid in manager.Boids)
            boid.SetViewMaterial(enabled ? _nearCameraMaterial : null);
    }

    private void AddPoints(System.Collections.Generic.List<Vector3> points, Color color)
    {
        _debugMesh.SurfaceSetColor(color);
        foreach (Vector3 point in points) _debugMesh.SurfaceAddVertex(point);
    }

    private static void AddLine(ImmediateMesh mesh, Vector3 a, Vector3 b)
    {
        mesh.SurfaceAddVertex(a);
        mesh.SurfaceAddVertex(b);
    }
}
