using Godot;

namespace BoidsLab;

/// <summary>组合实心障碍的一个凸部件。面壳不能提供可靠内部恢复。</summary>
[GlobalClass]
public partial class ObstaclePart3D : Resource
{
    [Export] public Shape3D Shape { get; set; } = null!;
    [Export] public Transform3D Transform { get; set; } = Transform3D.Identity;
}
