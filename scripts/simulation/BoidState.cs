using Godot;

namespace BoidsLab;

/// <summary>2D 世界状态：位置单位为世界 px，速度为 px/s；ID 在一次模拟生命周期内唯一。</summary>
public struct BoidState
{
    public int Id;
    public Vector2 Position;
    public Vector2 Velocity;
}
