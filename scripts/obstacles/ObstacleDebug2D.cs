using System.Collections.Generic;
using Godot;

namespace BoidsLab;

/// <summary>只为选中且显示方向的个体收集；缓存跨步复用。</summary>
public sealed class ObstacleDebug2D
{
    public int BoidId { get; private set; } = -1;
    public Vector2 Probe, Choice;
    public readonly List<(Vector2 Motion, bool Blocked)> Candidates = new();
    public readonly List<Vector2> Normals = new();
    public void Clear(int id = -1) { BoidId = id; Probe = Choice = Vector2.Zero; Candidates.Clear(); Normals.Clear(); }
}
