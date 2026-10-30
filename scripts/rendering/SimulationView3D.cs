using Godot;

namespace BoidsLab;

/// <summary>三维观察环境：空间边框、底面参考线与独立相机，不参与运动计算。</summary>
public partial class SimulationView3D : Node3D
{
    public DebugOverlayGeometry3D DebugGeometry { get; } = new();
    private BoidConfig? _config;
    private BoidManager3D? _manager;
    private BaseMaterial3D? _nearCameraMaterial;
    private readonly ImmediateMesh _debugMesh = new();
    private readonly StandardMaterial3D _debugMaterial = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        VertexColorUseAsAlbedo = true,
    };

    public void Bind(BoidConfig config, BoidManager3D manager)
    {
        _config = config;
        _manager = manager;
        GetNode<MeshInstance3D>("DebugOverlay").Mesh = _debugMesh;
    }

    public void Configure(Aabb bounds, bool resetCamera = true)
    {
        var mesh = new ImmediateMesh();
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
        };
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);
        mesh.SurfaceSetColor(new Color("36536c"));
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
        grid.SurfaceSetColor(new Color("233a4f"));
        for (int i = 1; i < 10; i++)
        {
            float x = bounds.Position.X + bounds.Size.X * i / 10f;
            float z = bounds.Position.Z + bounds.Size.Z * i / 10f;
            AddLine(grid, new Vector3(x, bounds.Position.Y, bounds.Position.Z), new Vector3(x, bounds.Position.Y, bounds.End.Z));
            AddLine(grid, new Vector3(bounds.Position.X, bounds.Position.Y, z), new Vector3(bounds.End.X, bounds.Position.Y, z));
        }
        grid.SurfaceEnd();
        GetNode<MeshInstance3D>("Grid").Mesh = grid;

        var camera = GetNode<SimulationCamera3D>("Camera3D");
        if (resetCamera) camera.Configure(bounds);
        else camera.UpdateHomeBounds(bounds);
    }

    public void RefreshDebug()
    {
        if (_config is null || _manager is null) return;
        RefreshNearCameraFade(_config, _manager);
        GetNode<MeshInstance3D>("Grid").Visible = _config.ShowGrid && _manager.Leader is null;
        GetNode<MeshInstance3D>("ReferenceFrame").Visible = _config.ShowBoundary && _manager.Leader is null;
        var camera = GetNode<SimulationCamera3D>("Camera3D");
        DebugGeometry.Build(_manager.Boids, _manager.SelectedBoid, _config, camera.UnitsPerPixelAt);
        _debugMesh.ClearSurfaces();
        if (DebugGeometry.RangePoints.Count + DebugGeometry.SeparationRangePoints.Count
            + DebugGeometry.NeighborPoints.Count + DebugGeometry.VelocityPoints.Count == 0) return;
        _debugMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _debugMaterial);
        AddPoints(DebugGeometry.RangePoints, new Color("78dcca"));
        AddPoints(DebugGeometry.SeparationRangePoints, new Color("ff9866"));
        AddPoints(DebugGeometry.NeighborPoints, new Color("ffc75a"));
        AddPoints(DebugGeometry.VelocityPoints, new Color("6baeff"));
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
