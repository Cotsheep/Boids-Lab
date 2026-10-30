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
    bool IsLeaderControlActive { get; }
    event Action? BoidControlStateChanged;
    event Action? ConfigurationEdited;

    void Initialize(BoidConfig config);
    void SetRunning(bool running);
    // 仅控制相机/选择等用户操作；关闭时取消待定手势，不改变模拟运行状态。
    void SetInteractionEnabled(bool enabled);
    void Reset();
    // 仅暂停时推进一个当前物理频率的时间步，保持暂停；运行时不执行。
    void StepOnce();
    void ConfigurationChanged();
    void BeginObstaclePlacement(ObstacleShape shape);
    void CancelObstaclePlacement();
    void DeleteSelectedObstacle();
    void ResizeSelectedObstacle(float width, float height, float depth = 0);
    void SetSelectedBoidLeader(bool enabled);
    void SetLeaderControlActive(bool active);
}
