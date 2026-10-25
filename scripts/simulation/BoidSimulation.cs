using System;
using System.Collections.Generic;
using Godot;

namespace BoidsLab;

/// <summary>普通 C# 的 2D 状态与算法入口，由世界场景的 Godot 回调显式驱动。</summary>
public sealed class BoidSimulation
{
    private readonly List<BoidState> _states = new();
    public IReadOnlyList<BoidState> States { get; }
    public BoidConfig Config { get; }
    public Rect2 WorldBounds { get; }
    public SimulationStats Stats { get; private set; }
    public bool IsRunning { get; private set; }
    public bool CoreImplemented => false;
    public bool CanStep => CoreImplemented && !IsRunning;

    public BoidSimulation(BoidConfig config, Rect2 worldBounds)
    {
        Config = config;
        WorldBounds = worldBounds;
        States = _states.AsReadOnly();
        Initialize();
    }

    public void Initialize()
    {
        Config.Normalize();
        IsRunning = false;
        _states.Clear();
        Stats = default;
        // TODO: 根据共享配置中的数量、随机种子及 WorldBounds 初始化状态。
        // 当前保持空集合，不生成伪造 Boid。
    }

    public void Start() => IsRunning = true;
    public void Pause() => IsRunning = false;
    public void Reset() => Initialize();

    /// <returns>是否产生了新的模拟状态；当前恒为 false。</returns>
    public bool Step(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));

        // TODO: 邻居搜索、Separation / Alignment / Cohesion、力与速度限制、积分、边界处理。
        // TODO: 仅在真实算法执行后更新 Stats，并使用实际计时与计数。
        return false;
    }
}
