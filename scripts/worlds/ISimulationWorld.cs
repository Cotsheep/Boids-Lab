using System;

namespace BoidsLab;

/// <summary>UI 与模拟场景之间的最小约定，不暴露 Vector2/Vector3 或绘制节点。</summary>
public interface ISimulationWorld
{
    string DisplayName { get; }
    string LengthUnit { get; }
    SimulationFeatures Features { get; }
    bool IsRunning { get; }
    bool CoreImplemented { get; }
    bool CanStep { get; }
    SimulationStats Stats { get; }
    ObstacleSelection? SelectedObstacleInfo { get; }
    ObstacleShape? ObstaclePlacement { get; }
    event Action? ObstacleSelectionChanged;
    int? SelectedBoidId { get; }
    int? LeaderId { get; }
    bool HasLeaderTarget { get; }
    event Action? BoidControlStateChanged;

    void Initialize(BoidConfig config);
    void SetRunning(bool running);
    // 仅控制相机/选择等用户操作；关闭时取消待定手势，不改变模拟运行状态。
    void SetInteractionEnabled(bool enabled);
    void Reset();
    void StepOnce();
    void ConfigurationChanged();
    void BeginObstaclePlacement(ObstacleShape shape);
    void CancelObstaclePlacement();
    void DeleteSelectedObstacle();
    void ResizeSelectedObstacle(float width, float height);
    void SetSelectedBoidLeader(bool enabled);
}
