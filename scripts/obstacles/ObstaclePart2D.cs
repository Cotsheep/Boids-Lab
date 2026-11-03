using Godot;

namespace BoidsLab;

/// <summary>逻辑障碍的一个实心凸部件；局部变换随障碍和父节点一起变换。</summary>
[GlobalClass]
public partial class ObstaclePart2D : Resource
{
    [Export] public Shape2D Shape { get; set; } = null!;
    [Export] public Transform2D Transform { get; set; } = Transform2D.Identity;
}
