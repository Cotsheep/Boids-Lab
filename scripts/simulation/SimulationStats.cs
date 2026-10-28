namespace BoidsLab;

/// <summary>2D/3D 场景都可以提供的统计；null 表示尚未测量。</summary>
public readonly record struct SimulationStats(
    int? BoidCount,
    double? UpdateMilliseconds,
    long? NeighborCandidateChecks,
    double? AverageNeighbors,
    double? NeighborSearchMilliseconds = null);
