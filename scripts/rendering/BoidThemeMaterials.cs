using Godot;

namespace BoidsLab;

/// <summary>一个世界共用四份主题材质；不改写场景资源，也不为每个个体复制材质。</summary>
internal sealed class BoidThemeMaterials
{
    public StandardMaterial3D Normal { get; } = Make();
    public StandardMaterial3D Selection { get; } = Make();
    public StandardMaterial3D Leader { get; } = Make();
    public StandardMaterial3D SelectedLeader { get; } = Make();

    public BoidThemeMaterials() => Update(LabPalette.Dark);

    public void Update(LabPalette palette)
    {
        Normal.AlbedoColor = palette.Boid;
        Selection.AlbedoColor = palette.Selection;
        Leader.AlbedoColor = palette.Leader;
        SelectedLeader.AlbedoColor = palette.SelectedLeader;
    }

    private static StandardMaterial3D Make() => new()
    {
        Roughness = 0.8f,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };
}
