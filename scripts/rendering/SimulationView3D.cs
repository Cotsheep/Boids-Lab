using Godot;

namespace BoidsLab;

/// <summary>三维观察环境：空间边框、底面参考线与独立相机，不参与运动计算。</summary>
public partial class SimulationView3D : Node3D
{
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

        mesh.SurfaceSetColor(new Color("233a4f"));
        for (int i = 1; i < 10; i++)
        {
            float x = bounds.Position.X + bounds.Size.X * i / 10f;
            float z = bounds.Position.Z + bounds.Size.Z * i / 10f;
            AddLine(mesh, new Vector3(x, bounds.Position.Y, bounds.Position.Z), new Vector3(x, bounds.Position.Y, bounds.End.Z));
            AddLine(mesh, new Vector3(bounds.Position.X, bounds.Position.Y, z), new Vector3(bounds.End.X, bounds.Position.Y, z));
        }
        mesh.SurfaceEnd();
        GetNode<MeshInstance3D>("ReferenceFrame").Mesh = mesh;

        GetNode<SimulationCamera3D>("Camera3D").Configure(bounds);
    }

    private static void AddLine(ImmediateMesh mesh, Vector3 a, Vector3 b)
    {
        mesh.SurfaceAddVertex(a);
        mesh.SurfaceAddVertex(b);
    }
}
