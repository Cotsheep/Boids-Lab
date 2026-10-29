using System;

namespace BoidsLab;

/// <summary>世界已实现的可选功能；共享 UI 只创建对应控件。</summary>
[Flags]
public enum SimulationFeatures
{
    None = 0,
    Leader = 1,
    Obstacles = 2,
    NeighborSearch = 4,
    Flocking = 8,
    DebugDrawing = 16,
    UniformGridSearch = 32,
    All = Leader | Obstacles | NeighborSearch | Flocking | DebugDrawing | UniformGridSearch,
}
