using System.Collections.Generic;
using Godot;

namespace BoidsLab;

/// <summary>只为选中且显示方向的个体收集；缓存跨步复用。</summary>
public sealed class ObstacleDebug3D
{
    public int BoidId { get; private set; } = -1;
    public Vector3 Probe, Choice;
    public readonly List<(Vector3 Motion, bool Blocked)> Candidates = new();
    public readonly List<Vector3> Normals = new();
    public void Clear(int id = -1) { BoidId = id; Probe = Choice = Vector3.Zero; Candidates.Clear(); Normals.Clear(); }
}
