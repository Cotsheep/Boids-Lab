# BoidsLab 架构与扩展入口

## 场景结构

主场景根节点协调游戏世界和 HUD，两个分支直接使用同一个根视口：

```text
main.tscn / Game (Node，MainController)
├── World (Node，世界场景容器)
│   └── boids_world_2d.tscn / SimulationWorld2D (Node2D，运行时实例化)
│       ├── SimulationView (Node2D，绘制游戏世界)
│       └── Camera2D
└── UI (CanvasLayer，layer = 1)
	└── UIRoot (Control，全窗口布局)
		├── 顶部标题与运行状态
		├── 左侧参数面板 / ControlPanel
		├── 中央提示文字
		└── 底部统计面板 / StatsPanel
```

世界场景由 `MainController.SimulationScene` 导出的 PackedScene 插槽指定。目前主场景已指定 `scenes/worlds/boids_world_2d.tscn`，主控制器在运行时将它实例化到 `World` 节点。背景、网格及未来的 Boid 都由世界中的 Node2D 直接绘制到根视口，不经过 SubViewport 或 UI 纹理容器。

2D 世界默认大小为 1280 × 720 **世界像素**，在世界场景根节点的 `WorldSize` 中设置。`BoidSimulation.WorldBounds` 接收该边界，面板大小不会反过来改变模拟边界。Camera2D 自动等比例适配整个根视口；网格间距 40 世界像素，经过相机缩放后的屏幕间距可能不同。HUD 覆盖区域仍属于世界，没有扣除顶部、左侧或底部面板所占空间。没有新增相机拖拽、缩放输入等交互。

HUD 使用独立的 CanvasLayer，不跟随 Camera2D 的世界变换。UIRoot、纯布局节点和提示文字的空白区域不拦截鼠标，有交互的参数面板、滚动区域和按钮正常处理鼠标事件。这里只明确 UI 输入边界，没有提前实现世界的鼠标或键盘操作。

“全窗口”指世界和 HUD 的显示组织方式，不表示切换到独占全屏；本次没有修改操作系统窗口模式。

世界场景不查找 UI 节点，也不保存用户配置。游戏控制器可以调用同样的 `Initialize(config)`，将它挂在普通 2D 游戏场景树中；它不强制依赖实验室界面。根节点需要先由宿主初始化，当前未增加独立运行时的默认配置或自动开跑行为。

## 数据流

```text
ControlPanel ──修改──> BoidConfig（唯一共享实例）
	 │ 配置变化事件           │
	 v                       └──> 世界场景与核心共享该引用
MainController
	 ├── 控制请求 ──> ISimulationWorld ──> SimulationWorld2D
	 │                                     ├──> BoidSimulation
	 │                                     └──> SimulationView
	 ├── 配置保存 ──> ConfigFile
	 └── 世界统计 + 引擎 FPS ──> StatsPanel
```

`MainController` 创建并加载一个 `BoidConfig`，将相同引用交给控件与世界场景，世界再交给核心和绘制节点。数值输入的范围与默认值全部来自配置类；`Normalize()` 对读取和修改后的数据进行约束。原生 `SpinBox` 显示当前数值。

配置在用户数据目录保存，不写回工程。持久化不包含生成状态或运行请求。TODO 开关也保存，加载它们不会假装产生对应绘制。

## 模块职责

| 文件 | 职责与扩展位置 |
| --- | --- |
| `MainController.cs` | 主场景的 Node 脚本；创建共享配置、将世界场景实例化到 World 容器、接收 UI 事件；通过 ISimulationWorld 发送控制请求，按 0.25 s 间隔刷新统计显示 |
| `ISimulationWorld.cs` | UI 与 2D/3D 世界之间的小接口：初始化、运行/暂停、重置、单步、配置变化、统计及单位信息 |
| `SimulationWorld2D.cs` | Node2D 场景根；拥有 2D 核心，在原生 `_PhysicsProcess` 中驱动 Step，管理相机与绘制刷新 |
| `BoidConfig.cs` | 参数默认值、范围、规则/可视化开关、输入约束和 ConfigFile 保存/加载 |
| `BoidState.cs` | 简单结构体：Id、Position、Velocity；位置单位 px，速度单位 px/s |
| `BoidSimulation.cs` | 普通 C# 的 2D 核心；固定世界边界、模拟控制方法、只读状态视图与算法 TODO |
| `SimulationStats.cs` | 与向量维度无关的公共统计数据；null 表示未测量 |
| `SimulationView.cs` | Node2D 的 `_Draw()`；按 WorldBounds 绘制背景和网格，保留 Boid/调试绘制 TODO，不引用 Control |
| `ControlPanel.cs` | 生成原生参数控件，更新共享配置，通过 C# 事件向主控制器发送操作请求；单位文字来自世界场景 |
| `StatsPanel.cs` | 展示测量数据，不运行或推算算法；null 统一显示 `--` |
| `main.tscn` | Game 根、World 容器、UI CanvasLayer；世界直接使用根视口，原生 Control 控件构成覆盖式 HUD |
| `boids_world_2d.tscn` | 独立 2D 世界：Node2D 根、Node2D 绘制节点、Camera2D |
| `lab_theme.tres` | 原生 Theme / StyleBoxFlat，统一界面颜色、间距与基础样式 |

模拟核心不继承 Node，使用 Godot 的 Vector2 表达 2D 向量。原生 Node2D 世界从 Godot 接收更新回调，显式调用普通 C# 核心。没有自建生命周期调度器。绘制节点只读取状态，不修改位置和速度。背景网格只是视觉辅助，不构成空间网格数据结构。

## 复用 UI 到 3D

未来新增一个根节点为 Node3D、实现 `ISimulationWorld` 的场景，由它持有 3D 核心、Camera3D 和 3D 显示节点，然后在 `MainController.SimulationScene` 中指定这个场景。它同样挂入 `World` 容器，使用根视口呈现；`UI (CanvasLayer)` 下的 Control 控件继续作为屏幕空间 HUD。主界面不需要知道世界根节点是 Node2D 还是 Node3D，也不需要为复用 UI 额外包装整幅世界视口。

ControlPanel 和 StatsPanel 不接触 Vector2、Vector3、Camera2D 或 Camera3D。参数开关与统计结构可以复用；长度、速度、加速度的提示单位由 `ISimulationWorld.LengthUnit` 提供。

这次没有实现 3D 场景、3D 算法或模式切换。当前 BoidState 与 BoidSimulation 仍专用于 2D；未来应单独定义 Vector3 状态与 3D 核心，不能把当前 2D 算法当作已经通用。3D 的单位、范围与默认值需届时明确，当前配置数值仍以 2D 世界像素解释。

## 生命周期接口

- `new BoidSimulation(config, worldBounds)`：保留共享引用和固定世界边界，调用核心的 `Initialize()`。
- `Initialize()`：约束配置、暂停、清空状态、将统计置为未测量；状态生成留为 TODO。
- `Start()` / `Pause()`：切换运行请求。当前 `IsRunning` 不表示算法已经实现。
- `Reset()`：重新初始化，保留当前配置。
- `Step(deltaSeconds)`：显式使用秒为单位的正时间步长；当前返回 false，不产生状态更新。
- `States`：只读列表；当前为空。
- `Stats`：当前全部字段为 null；FPS 独立由主控制器从引擎获取。
- `CoreImplemented`：当前固定为 false；UI 单步按钮据此禁用。
- `CanStep`：核心实现且处于暂停状态时才允许手动单步。

Reset 后统计仍是 `--`，而非零。Start 后也不会生成任何演示鸟群。未实现的统计不能用目标数量、空列表长度、帧间隔或随机数补齐。

## 后续实现顺序建议（均不属于本次交付）

1. 在核心 `Initialize()` 中利用数量、种子和 WorldBounds 生成状态；先定义数量/种子变更何时触发重新生成。
2. 在 `DrawBoids()` 中读取状态并绘制静态三角形，检查朝向与坐标单位。
3. 在 `Step()` 中实现单个时间步的位置更新，再逐条加入规则和简单邻居遍历。
4. 算法可运行后再将 `CoreImplemented` 设为 true，启用单步；为测量指标提供真实计时、计数和统计口径。
5. 按需向画布提供真实邻居数据，再实现调试绘制，移除对应 TODO 标记。

暂不规定边界是环绕还是反弹，也不提前实现邻居缓存或空间划分。未来决定这些行为时同步更新本说明。

## 本次验证边界

本次普通游戏层次调整已通过 C# 编译（零警告、零错误）与静态场景结构检查，包括资源路径、父节点路径、唯一名称、世界场景插槽、CanvasLayer HUD 及根视口世界分支。由于已知本机存在图形操作触发蓝屏的情况，没有启动 Godot、导入场景、截图或进行窗口测试；实际布局与输入交互尚未运行验证。之前 70 项运行内检查针对旧的 Control 画布结构，不能视为本次变更的运行验证；旧检查脚本也不适用于新的节点结构。
