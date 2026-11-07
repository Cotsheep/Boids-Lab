# 开发与构建说明

本文收录运行环境、工程目录、构建方法及开发机环境记录。快速使用见[项目 README](../README.md)，模块职责见[架构说明](architecture.md)，测试结果见[验证记录](3d-implementation-validation.md)。文中路径与命令均相对包含 `project.godot` 的项目根目录 `boids/`；引擎绝对路径是本机示例，其他机器需替换。

## 运行环境与构建

项目使用 Godot **4.7.2 .NET**、C# 和 `net8.0`，Compatibility 渲染器，无第三方插件或素材依赖。应用和程序集名称为 `BoidsLab`。

常规构建会自动还原依赖：

```powershell
dotnet build BoidsLab.csproj
```

构建后在 Godot 编辑器按 F5 运行二维入口；也可打开 `scenes/main_3d.tscn` 后按 F6 单独运行三维入口。两者均支持运行时切换世界。

## 目录

```text
boids/
├── project.godot, BoidsLab.csproj, BoidsLab.sln
├── CONTEXT.md                       # 领域术语表
├── scenes/
│   ├── main.tscn, main_3d.tscn         # 二维默认入口、三维默认入口
│   ├── boids/                        # 个体外观
│   ├── ui/                           # 帮助弹层
│   └── worlds/                       # 世界场景与维度定义资源
├── scripts/
│   ├── app/                          # 主控制器与世界切换
│   ├── simulation/                   # 配置、运动、搜索、群集、边界及领队
│   ├── rendering/                    # 相机、参考几何与调试绘制
│   ├── obstacles/                    # 障碍、编辑和避障
│   ├── worlds/                       # 世界接口、能力与资源定义
│   └── ui/                           # 控件绑定、帮助与统计
├── tests/
│   ├── run-headless.ps1               # 完整回归入口
│   ├── *_checks.tscn, *Checks.cs      # 二维 / 三维自动检查，当前清单见验证记录
│   └── camera_fade_visual.tscn        # 固定快照图形对比
└── docs/
    ├── README.md                     # 文档导航
    ├── architecture.md               # 架构与职责
    └── manual-acceptance.md           # 维护者实机验收清单
```

参数控件在 `ControlPanel.Bind()` 中生成；HUD 布局在 `main.tscn` 的 `UI/UIRoot` 分支中编辑，操作说明正文在两份 `scenes/worlds/world_*.tres` 中编辑，弹层布局在 `scenes/ui/help_overlay.tscn` 中编辑，世界与相机分别在 `boids_world_2d.tscn` / `boids_world_3d.tscn` 中编辑。`BoidManager` 是世界根节点，负责生成、统一更新、障碍物接触和计时；`Boid2D` 保存自身运动状态并完成积分，`boid_2d.tscn` 中的 Polygon2D 跟随节点变换显示三角形。`SimulationView` 负责背景、网格和调试绘制入口；`SimulationCamera2D` 处理拖动、缩放、全景复位和视口适配。主控制器通过 `ISimulationWorld` 操作 2D 或 3D 世界；`SimulationFeatures` 声明已实现能力，控制面板据此创建可用控件。主控制器按 `SimulationWorldDefinition` 资源切换世界、独立配置和说明。详见 [架构说明](architecture.md)。

## 本机构建说明

本机的 `dotnet --info` 遇到了 workload 安装器异常。首次还原使用以下方式，仅在当前进程中关闭 workload 解析，没有修改系统设置：

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
dotnet build BoidsLab.csproj --source D:/app/Godot4.7.2/GodotSharp/Tools/nupkgs
```

`--source` 指向本机 Godot 自带的官方包；其他机器应改成自己的安装路径，或使用常规包还原。项目本身没有写入机器专用绝对路径。

还原完成后，可以使用 `dotnet build BoidsLab.sln --no-restore --verbosity minimal` 构建；遇到上述 workload 异常时沿用进程内环境变量设置，或使用[回归运行器](3d-implementation-validation.md#构建与无图形回归)。2026-11-06 使用 SDK 8.0.400 命令行构建通过，零警告、零错误。2026-10-29 编辑器曾报告需要 SDK `10.0.11` 而无法加载 `Microsoft.Build`；本次未重新验证编辑器内构建，未改动系统 SDK。

## 回归验证

首次执行前先完成包还原，运行器使用 `--no-restore` 构建：

```powershell
./tests/run-headless.ps1 -GodotExecutable '<Godot .NET 控制台程序路径>'
```

运行器会备份并恢复二维、三维配置与主题偏好，日志保存到 `.godot/validation/full-regression/`。当前检查清单、已记录结果及实机验收边界统一见[验证记录](3d-implementation-validation.md)。
