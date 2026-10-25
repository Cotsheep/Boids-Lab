# BoidsLab

> 开发机曾报告 Godot 4.7.2 图形操作触发系统蓝屏，尚未完成修复。此前的界面检查不代表驱动稳定性，当前验证范围仅限编译与静态检查。详细诊断记录保存在本地，不随源码上传。

用于团队技术分享和个人学习的 2D Boids 实验框架。运行环境为 **Godot 4.7.2 .NET + .NET 8 SDK**，语言为 C#。

当前阶段只完成 UI、配置、生命周期接口与绘制入口。**没有实现 Boid 初始化、运动、三条转向规则或邻居搜索。**

项目采用普通全窗口游戏的结构：`Game (Node)` 下分别放置 `World (Node)` 与 `UI (CanvasLayer)`。Node2D 世界直接显示在根视口中，Control 面板作为 HUD 覆盖在世界画面上；未来可以在同一个 World 容器中接入 Node3D 场景，复用 UI。当前尚无 3D 场景或算法。

## 打开和运行

1. 使用 Godot **4.7.2 .NET 版**导入本目录的 `project.godot`。已有项目目录仍为 `boids`，应用和程序集名称为 `BoidsLab`。
2. 在编辑器中构建 C# 项目，然后按 F6 运行 `scenes/main.tscn`，或按 F5 运行主场景。
3. 也可以先在本目录运行 `dotnet build BoidsLab.csproj`，再由编辑器启动。

初始窗口为 1360 × 860，最小为 960 × 640。这里的“全窗口”指游戏世界使用整个根视口，不切换操作系统的独占全屏模式，也没有修改原生窗口模式。HUD 顶部显示标题和状态，左侧参数面板可滚动，底部统计项可换行。空白 UI 和提示文字不拦截鼠标，有交互的面板与控件正常接收输入；当前没有新增游戏操作。使用 Compatibility 渲染器，无第三方插件或素材依赖；原有 Godot 图标保留。

## 当前可用操作

| 操作 | 当前行为 |
| --- | --- |
| Start / Pause | 切换运行请求；没有真实模拟更新，顶部始终提示核心未实现 |
| Reset | 暂停、清空状态及模拟统计；保留参数和可视化开关 |
| Step | 禁用，等待模拟核心实现 |
| 参数与规则开关 | 修改同一个 `BoidConfig` 实例并保存配置；尚不产生群体行为 |
| Show Grid | 显示或隐藏间距 40 世界像素的辅助网格，与邻居搜索无关 |
| 其他调试开关 | 保存状态，界面标为 TODO；尚无对应绘制 |
| 性能统计 | FPS 来自引擎实测；实际 Boid 数量及全部算法指标显示 `--` |

`Boid Count` 表示**目标数量**，并非已经生成的数量。配置变化后底部显示当前目标数量和种子。

2D 世界默认大小为 1280 × 720 世界像素，由 `SimulationWorld2D.WorldSize` 管理。Camera2D 等比例适配整个根视口，窗口显示区域变化不会改变模拟边界。HUD 遮住的区域仍属于世界，没有通过扣除面板宽高改变相机显示范围或世界边界。参数里的 px 指世界像素，经过相机缩放后不一定对应一个屏幕像素。

## 配置

范围与默认值集中定义在 `scripts/simulation/BoidConfig.cs`，UI 从这里获取，不再复制定义。

| 参数 | 默认值 | 范围 | 步长 / 单位 |
| --- | --- | --- | --- |
| Boid Count | 200 | 0–5000 | 1 |
| Random Seed | 42 | 0–2147483647 | 1 |
| Perception Radius | 80 | 1–500 | 1 px |
| Max Speed | 120 | 0–1000 | 1 px/s |
| Max Force | 200 | 0–2000 | 1 px/s²，加速度限制 |
| Separation Weight | 1.5 | 0–5 | 0.1 |
| Alignment Weight | 1 | 0–5 | 0.1 |
| Cohesion Weight | 1 | 0–5 | 0.1 |

三条规则默认启用；网格默认显示，其余可视化默认关闭。当前范围只是实验用的输入约束，不代表经过算法效果或性能验证。

数值输入按 Enter 或失去焦点后提交。配置修改时保存为 `user://boids_lab.cfg`，下次启动恢复；不会自动保存运行状态。Windows 默认位置为 `%APPDATA%/Godot/app_userdata/BoidsLab/boids_lab.cfg`。首次启动找不到配置时使用默认值。无效类型回退到默认值，越界数值被限制到合法范围。加载或保存失败会显示提示；Reset 不删除配置文件。

## 目录

```text
boids/
├── project.godot
├── BoidsLab.csproj
├── BoidsLab.sln
├── scenes/
│   ├── main.tscn
│   ├── lab_theme.tres
│   └── worlds/boids_world_2d.tscn
├── scripts/
│   ├── app/MainController.cs
│   ├── simulation/BoidState.cs
│   ├── simulation/BoidConfig.cs
│   ├── simulation/BoidSimulation.cs
│   ├── simulation/SimulationStats.cs
│   ├── rendering/SimulationView.cs
│   ├── worlds/ISimulationWorld.cs, SimulationWorld2D.cs
│   └── ui/ControlPanel.cs, StatsPanel.cs
└── docs/architecture.md
```

参数控件在 `ControlPanel.Bind()` 中生成；HUD 布局和提示文字在 `main.tscn` 的 `UI/UIRoot` 分支中编辑，世界与相机在 `boids_world_2d.tscn` 中编辑。`SimulationView` 继承 Node2D，负责世界绘制。主控制器把 `SimulationScene` 指定的世界场景实例化到 `World` 容器，未来可替换为实现 ISimulationWorld 的 Node3D 场景。详见 [架构说明](docs/architecture.md)。

## 本机构建说明

本机的 `dotnet --info` 遇到了 workload 安装器异常。首次还原使用以下方式，仅在当前进程中关闭 workload 解析，没有修改系统设置：

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
dotnet build BoidsLab.csproj --source D:/app/Godot4.7.2/GodotSharp/Tools/nupkgs
```

`--source` 指向本机 Godot 自带的官方包；其他机器应改成自己的安装路径，或使用常规包还原。项目本身没有写入机器专用绝对路径。

还原完成后，未设置上述环境变量的常规 `dotnet build BoidsLab.csproj --no-restore` 也已通过，零警告、零错误。

## 初版框架的历史验证

- Godot MCP：确认 4.7.2 Mono，创建/保存场景、启动编辑器和项目；应用运行日志无错误。
- 运行内检查：70 项全部通过，覆盖按钮状态、所有参数及规则开关、配置保存/恢复、网格实际绘制、未实现统计保持 `--`。
- 窗口布局：960 × 640、1360 × 860、1920 × 1080 均已运行并检查截图。

验证脚本与截图位于忽略目录 `.godot/validation/`，属于可删除的本地检查产物，不是项目运行依赖。FPS 是界面运行帧率，本次没有进行 Boids 算法性能测试。

上述运行内检查和截图属于旧版 Control 画布结构，不能代表当前架构已完成运行验证。此次普通游戏层次与 HUD 的改动已通过 C# 编译（零警告、零错误）和静态场景结构检查；未启动 Godot 或执行任何图形测试，实际布局与输入交互尚未运行验证，旧运行检查脚本不再适用。
