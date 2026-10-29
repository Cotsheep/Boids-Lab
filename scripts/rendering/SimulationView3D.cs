using Godot;

namespace BoidsLab;

/// <summary>三维观察环境：空间边框、底面参考线与独立相机，不参与运动计算。</summary>
public partial class SimulationView3D : Node3D
{
    public DebugOverlayGeometry3D DebugGeometry { get; } = new();
    private BoidConfig? _config;
    private BoidManager3D? _manager;
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

    public void Configure(Aabb bounds)
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

        GetNode<SimulationCamera3D>("Camera3D").Configure(bounds);
    }

    public void RefreshDebug()
    {
        if (_config is null || _manager is null) return;
        GetNode<MeshInstance3D>("Grid").Visible = _config.ShowGrid;
        var camera = GetNode<SimulationCamera3D>("Camera3D");
        float unitsPerPixel = camera.Size / Mathf.Max(1, GetViewport().GetVisibleRect().Size.Y);
        DebugGeometry.Build(_manager.Boids, _manager.SelectedBoid, _config, _manager.WorldBounds, unitsPerPixel);
        _debugMesh.ClearSurfaces();
        if (DebugGeometry.RangePoints.Count + DebugGeometry.NeighborPoints.Count + DebugGeometry.VelocityPoints.Count == 0) return;
        _debugMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _debugMaterial);
        AddPoints(DebugGeometry.RangePoints, new Color("78dcca"));
        AddPoints(DebugGeometry.NeighborPoints, new Color("ffc75a"));
        AddPoints(DebugGeometry.VelocityPoints, new Color("6baeff"));
        _debugMesh.SurfaceEnd();
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
