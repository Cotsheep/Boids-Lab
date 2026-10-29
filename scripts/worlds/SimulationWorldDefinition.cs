using Godot;

namespace BoidsLab;

/// <summary>世界入口的数据资源：场景、独立配置文件和对应操作说明，不保存运行状态。</summary>
[GlobalClass]
public partial class SimulationWorldDefinition : Resource
{
    [Export] public string Title { get; set; } = "";
    [Export] public PackedScene Scene { get; set; } = null!;
    [Export] public string ConfigurationPath { get; set; } = "";
    [Export(PropertyHint.MultilineText)] public string Instructions { get; set; } = "";
}
