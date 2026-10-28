# BoidsLab 架构与扩展入口

## 更新方式与职责划分

项目参考 [SebLague/Boids 的管理器](https://github.com/SebLague/Boids/blob/fdda4db1368df92fbacd8a644bbe6dc61d705441/Assets/Scripts/BoidManager.cs)与[个体脚本](https://github.com/SebLague/Boids/blob/fdda4db1368df92fbacd8a644bbe6dc61d705441/Assets/Scripts/Boid.cs)，采用“管理器统一调度、个体更新自身、独立场景显示外观”的组织方式。当前实现了基础运动、朝向跟随、两种 CPU 邻居搜索，以及分离、对齐、聚集规则，没有 GPU 计算。

`BoidManager` 是 2D 世界根节点，也是唯一驱动 Boid 模拟的 Godot 回调入口。`Boid2D.UpdateBoid(deltaSeconds)` 是普通 C# 方法，由管理器显式调用；个体没有自己的 `_Process()` 或 `_PhysicsProcess()`。

每只 Boid 使用原生 `Node2D.Position` 和 `Rotation`，并保存 `Velocity`、`Acceleration`、`AngularVelocity`、`AngularAcceleration`。ID 在一次 Reset 周期内唯一；没有供渲染同步的状态副本。个体场景中的 `Polygon2D` 跟随父节点变换显示外观。

## 场景结构

```text
main.tscn / Game (Node，MainController)
├── World (Node，世界场景容器)
│   └── boids_world_2d.tscn / BoidsWorld2D (Node2D，BoidManager)
│       ├── SimulationView (Node2D，背景、网格与调试绘制)
│       ├── Boids (Node2D，运行时生成的个体容器)
│       └── Camera2D (SimulationCamera2D，拖动、缩放与全景复位)
└── UI (CanvasLayer，layer = 1)
	└── UIRoot (Control，全窗口布局)
		├── 顶部标题与运行状态
		├── 左侧参数面板 / ControlPanel
		├── 中央提示文字
		└── 底部统计面板 / StatsPanel
```

独立的个体场景由 `BoidManager.BoidScene` 指定，目前指向 `scenes/boids/boid_2d.tscn`：

```text
Boid2D (Node2D，Boid2D 脚本)
└── Body (Polygon2D，朝向本地 +X 的三角形)
```

`SpawnBoid(position, velocity)` 会实例化该场景，初始化配置、ID、速度与朝向，加入 `Boids` 容器并注册到管理器集合。`RemoveBoid(boid)` 从集合和场景树中移除个体，再请求释放节点。创建、移除操作应在管理器的更新遍历之外进行。启动和 Reset 会按配置数量生成随机位置、随机方向的个体，初始速率为 `MaxSpeed`，加速度和角速度为零；初始保持暂停。

世界场景由 `MainController.SimulationScene` 插槽指定，主控制器在运行时将它实例化到 `World` 节点。世界直接使用根视口，HUD 覆盖在上方。这里只指显示组织方式，没有切换操作系统的独占全屏模式。

`BoidManager.WorldSize` 默认 1280 × 720 **世界像素**，仅用来建立初始与 Home 复位的参考矩形。管理器将它传给 `SimulationCamera2D.Configure()`，相机以适配该参考矩形的倍率为基础，叠加用户的缩放倍率。实际 `WorldBounds` 来自相机当前可见区域，背景和个体环绕共用这个动态矩形。HUD 遮挡的区域仍属于世界，UI 面板大小不改变边界。`Boids` 容器和 `SimulationView` 保持默认变换，因此个体的本地位置与背景网格使用同一套坐标。

相机在拖动、鼠标中心缩放、Home 复位或视口尺寸变化完成后发出 `ViewChanged`。管理器的 `UpdateWorldBounds()` 用 `GetGlobalTransformWithCanvas().AffineInverse()` 将视口矩形转换为世界根节点坐标，更新 `WorldBounds`，立即环绕画面外的个体并请求背景重绘。即使暂停也更新边界，不推进速度或旋转，也不重新生成个体。一次鼠标中心缩放只在位置补偿完成后发出通知，避免按中间视角重复环绕。当前相机不旋转，可见区域是与世界坐标轴平行的矩形。

参考矩形与动态 `WorldBounds` 分别保存；动态边界不会再次传回相机的 `Configure()`，从而避免缩放被“适配新世界大小”抵消。

网格间距为 40 世界像素，网格线始终位于世界坐标的 40 整倍数处，只绘制当前可见部分；经过缩放后不一定等于 40 屏幕像素。这个网格只是视觉辅助，与邻居搜索内部使用的空间格子独立。左键拖动画面平移视角，位移按当前相机倍率换算，画面在不同倍率下都跟随鼠标。滚轮以鼠标位置为中心缩放，倍率限制为参考视角的 0.25–8 倍，每格乘或除以 1.15；Home 结束拖动、恢复参考视角并居中。视口尺寸变化时，相机重新计算适配倍率，保留用户倍率与观察位置。

HUD 使用独立 CanvasLayer，不跟随 Camera2D 的世界变换。UIRoot、纯布局节点和提示文字不拦截鼠标，有交互的参数面板、滚动区域和按钮正常处理输入。

F1 和顶部“隐藏界面”按钮切换整个 `UIRoot.Visible`，包含占位文字、提示和统计。`MainController._Input()` 接收全局 `toggle_ui` 动作，即使输入框有焦点也能隐藏/恢复 HUD，隐藏前释放控件焦点。相机在 `_UnhandledInput()` 中处理滚轮和 `reset_camera`（Home），让 UI 优先处理输入；参数面板关闭 `MouseForcePassScrollEvents`，滚轮不会穿透到相机。快捷键映射位于 `project.godot`。

空格映射到 `toggle_pause`，由 `MainController._Input()` 在 GUI 处理前拦截。仅首次按下调用现有 `ToggleRunning()`；长按重复事件与松开事件也标记为已处理，避免触发有焦点控件的 `ui_accept`、改变复选框或在输入框插入空格。HUD 隐藏时同样有效，暂停按钮和状态文字同步刷新。这只切换模拟的运行状态，不暂停整个 SceneTree，所以相机、选择与 UI 继续响应。

相机只在 `_UnhandledInput()` 收到左键按下时开始一个待定手势，确保参数控件优先处理点击。通过 `WorldPressStarted(viewportPosition)` 让管理器记录按下时命中的个体；累计位移不足 5 个视口像素时不移动相机，越过阈值后按完整累计位移开始拖动。`_Input()` 接收后续移动与松开，跨入 UI 后也能结束拖动。未拖动且在世界内松开时发出 `WorldPressEnded(true)`，其余情况发出 false。失焦、丢失左键按住状态、Home、滚轮缩放或视口变化会取消待定手势，避免误选择或粘住相机。

隐藏 UI 不会暂停或重置模拟。相机不直接操作个体或 `BoidConfig`，通过 `ViewChanged` 让管理器同步边界及越界位置；UI 可见性与相机视角不写入配置文件。相机自己订阅和解除视口尺寸、窗口失焦事件；管理器负责订阅和解除相机视角事件。

## 初始化与更新顺序

主控制器先创建、加载共享配置，再实例化世界并调用 `Initialize(config)`，最后把世界加入场景树。管理器在 `_Ready()` 中取得个体容器、背景和相机，绑定配置与相机事件，先适配参考视角并同步可见边界，再执行 Reset，确保个体生成在实际视野中。世界需要宿主先初始化；当前没有独立运行时的默认配置或自动开跑行为。

每个固定物理时间步的调用顺序为：

```text
Godot → BoidManager._PhysicsProcess(delta)
		   └── 运行请求开启时 → Step(delta)
								  ├── NeighborSearch.Search：复制状态并搜索邻居
								  ├── BoidSteering.Calculate：为全部个体计算三条规则的合加速度
								  ├── 遍历已注册个体
								  │     ├── Boid2D.UpdateBoid(delta, steering)：移动与朝向积分
								  │     └── 按 WorldBounds 环绕越界位置
								  └── 记录数量、搜索/更新耗时、候选次数与平均邻居数
```

`Step()` 在个体循环前调用 `NeighborSearch2D.Search()`。两种搜索都读取同一时间步开始时的位置、速度副本，不访问已经更新过的节点状态。随后为全部个体计算三条规则的合加速度，写入复用的 `Vector2[]`；全部算完后再逐个积分。个体增删后按实际数量调整数组，每步覆盖所有条目，关闭规则不会遗留旧转向。

`UpdateBoid()` 验证初始化状态和正的有限时间步长，执行移动与朝向积分，返回运动状态是否变化。管理器在运动或环绕改变状态后请求调试图形重绘。原生 `Polygon2D` 的显示由场景节点负责，不需要统一绘制所有个体的函数。这里使用 Godot 的固定物理回调（默认 60 次/秒）；`MainController` 每 0.25 秒只刷新统计标签，不影响运动频率。

## 邻居搜索

`NeighborSearch2D` 是普通 C# 类，没有自己的 Godot 回调。`NeighborSearchMode` 的 `Naive` 与 `UniformGrid` 共用快照、距离过滤和结果容器，UI 通过共享配置选择。默认使用朴素遍历，存储键为 `simulation/neighbor_search_mode`；缺失或非法值回退到默认方式。

`BoidSnapshot` 是只含 ID、Position、Velocity 的只读值类型。`NeighborSearch.Snapshots[i]` 对应本次管理器列表中的第 i 只个体，**i 不是个体 ID**，删除个体造成 ID 不连续也不会影响搜索。`GetNeighborIndices(i)` 返回邻居的快照下标；`GetOffset(i, j)` 返回从 i 到 j 的最短环绕位移。两种算法输出的邻居集合一致，但列表顺序不保证相同。

`BoidSteering.Calculate(search, index, config)` 使用这三个读取入口计算规则；它是无跨步状态的普通静态方法，不修改节点、不再次搜索邻居。

分离与聚集应使用环绕位移，避免跨边界邻居被误判为远处；对齐读取快照速度。不要在规则中混用已经移动的节点坐标/速度。结果对应最近一次物理步的起点，节点积分后仍保留这个快照供检查，并非积分后的重新搜索。列表存储会复用，不应跨下一次 Search 持有它；需要长期保存时自行复制。读取前检查 `HasResults`；初始化、重置、增删个体、搜索方式/半径或世界边界变化会使结果失效，下一次运行步重算。

朴素方式对每个个体遍历其他所有个体，执行 `N(N−1)` 次距离检查。均匀网格在每个方向取 `max(1, floor(边长 / 半径))` 格，并将整个边长均分，避免末端残余格在环绕接缝处漏查。每轴超过一格时，格子尺寸不小于半径，查询自身及相邻格即可；索引按边界环绕。某轴只有一格或两格时，每格只访问一次，避免重复邻居与虚高计数。字典仅保存有个体的格子，不分配完整二维数组。

两种方式最终都排除自身，再比较最短环绕距离的平方与半径平方，包含等于半径的情况与不同个体的位置重合。快照和邻居列表在数量不变时复用；格子成员在每步重新构建，其开销计入搜索时间。没有排序邻居、GPU、线程或对象池。

网格平均查询成本取决于候选总量 C，含建格通常为 `O(N + C)`（字典操作按平均成本计）；最坏情况下仍是 `O(N²)`。完整邻居下标列表占用 `O(N + K)` 空间，K 为有向邻居总数，密集场景也可能达到二次规模。它不保证所有数量和半径下都比朴素方式快。比较方式见 README。

## 分离、对齐、聚集的计算

三条规则共用同一步的邻居列表。对齐和聚集使用感知半径内的所有邻居；分离只使用 `min(SeparationRadius, PerceptionRadius)` 内的近邻。`SeparationRadius` 默认 25，范围和持久化集中在 `BoidConfig`，保存键为 `simulation/separation_radius`，旧配置缺失时使用默认值。界面显示设置值，超过感知半径时仅实际查询范围受限，不悄悄改写控件数值。

1. **分离**：累加 `-offset / distance²`，近邻对方向的影响更大。距离低于 0.001 像素时限制倒数，避免数值爆炸；完全重合时用唯一 ID 为一对个体指定相反的左右方向，确定且不消耗随机数。
2. **对齐**：求邻居速度的平均向量，采用其方向作为目标方向；目标速率为 `MaxSpeed`，不是严格匹配邻居平均速率。
3. **聚集**：求最短环绕位移的平均向量，朝这个局部中心转向。不能直接平均节点世界坐标，否则接缝两边的群体会朝屏幕中间错误转向。

沿用参考项目的目标速度差思路，并显式按固定的 `ResponseSeconds = 1` 秒转换为加速度：

```text
单条规则加速度 = LimitLength((Normalize(目标方向) × MaxSpeed − 当前速度) / ResponseSeconds, MaxForce)
群集加速度 = LimitLength(分离加速度 × 分离权重 + 对齐加速度 × 对齐权重 + 聚集加速度 × 聚集权重, MaxForce)
```

每条规则关闭或权重为零时不贡献加速度；没有邻居或目标方向抵消为零时不凭空制动。`MaxSpeed = 0` 或 `MaxForce = 0` 时群集转向为零。没有最小速率限制，也没有碰撞体或不重叠保证。两种搜索的结果集合相同，但累加顺序可能导致微小浮点误差；长期轨迹不承诺逐位一致。

## 速度、加速度与朝向

`SetAcceleration(Vector2)` 设置持续的外部加速度输入，直到下一次设置、传入零向量或 Reset。群集加速度通过 `UpdateBoid(delta, steeringAcceleration)` 的第二个参数按步传入，默认值为零，兼容直接调用 `UpdateBoid(delta)` 的运动测试。两者相加后再限幅，群集规则不会覆盖持续输入，也不会残留到下一步。输入采用世界像素/秒²；`MaxForce` 保留原有字段名和存储键，UI 名称为“最大加速度”，没有引入质量或力的物理系统。

每个时间步采用半隐式欧拉积分：

```text
a = LimitLength(accelerationInput + steeringAcceleration, MaxForce)
v = LimitLength(v + a × dt, MaxSpeed)
p = p + v × dt
```

时间单位是秒；没有群集或外部加速度时保持匀速。有规则转向目标时，最大速度也用作目标速率；无目标时提高上限不会自动加速。下调速度上限时会裁剪速度；触及上限后，速度增量可能小于加速度积分的结果。半隐式欧拉是数值近似，减小时间步会减小位置误差，不保证不同步长逐位相同。

`UpdateFacing()` 单独控制朝向，目标为 `Velocity.Angle()`。先用 `Mathf.AngleDifference()` 取得 [-π, π] 的最短夹角，再用带角速度反馈的临界阻尼形式计算角加速度：

```text
error = AngleDifference(Rotation, Velocity.Angle())
requestedAlpha = response² × error - 2 × response × angularVelocity
alpha = Clamp(requestedAlpha, ±maxAngularAcceleration)
angularVelocity = Clamp(angularVelocity + alpha × dt, ±maxAngularSpeed)
Rotation = Wrap(Rotation + angularVelocity × dt, -π, π)
```

`FacingResponse = 6`（1/秒）控制跟随响应。误差促使转动，角速度反馈负责制动。内部角度、角速度、角加速度使用弧度、弧度/秒、弧度/秒²；UI 与配置使用度，更新时转换。`AngularAcceleration` 记录本步角速度实际增量除以 dt；动态下调角速度上限时先硬裁剪旧角速度，再积分。低于 0.01 像素/秒时不选择新方向，只减缓已有旋转，避免零速度导致朝向跳变。朝向变化不会主动旋转速度向量。

调试代码仍可以设置持续的外部加速度，例如：

```csharp
// 已就绪的 BoidManager 内，或持有该管理器引用的调试代码中：
Boid2D boid = SpawnBoid(new Vector2(640, 360), new Vector2(80, 0));
boid.SetAcceleration(new Vector2(0, 40)); // 持续向下加速，轨迹弯曲，朝向逐渐跟随。
// 后续某时刻：boid.SetAcceleration(Vector2.Zero); // 撤销外部输入，群集规则仍正常运行。
```

此示例需要模拟处于运行状态才会积分。若要单独观察外部加速度效果，可关闭三条规则。默认三条规则开启时，个体会根据邻居持续转向，外观朝向再由角加速度机制跟随速度变化。

## 配置与数据流

```text
ControlPanel ──修改──> BoidConfig（唯一共享实例）
	 │ 配置变化事件          ├──> BoidManager
	 v                       ├──> 每只 Boid2D
MainController               └──> SimulationView
	 ├── 控制请求 ──> ISimulationWorld ──> BoidManager
	 ├── 配置保存 ──> ConfigFile
	 └── 世界统计 + 引擎 FPS ──> StatsPanel
```

参数范围与默认值只在 `BoidConfig` 中定义。`MainController`、管理器、个体与背景绘制节点引用相同的配置对象。控件改变时更新并保存配置，调用管理器的 `ConfigurationChanged()` 做范围约束并请求重绘；运动上限在下一次运行更新时读取。

界面的“个体数量”表示目标数量。数量与种子修改后在 Reset 生效，当前没有自动同步数量差额。`InitializePopulation()` 每次创建按 `Config.RandomSeed` 初始化的随机数生成器，使用 `Config.BoidCount`、当前可见的 `WorldBounds` 和 `Config.MaxSpeed` 生成位置、方向与速度，再逐个调用 `SpawnBoid()`。同一引擎版本、相同配置和可见边界下可复现初始状态，不在每次参数变化时重新设种子。

管理器对外提供 `IReadOnlyList<Boid2D> Boids`，它包装同一个内部列表，会反映创建和移除。这个接口只限制集合增删入口；其中的 Boid 仍然是实际节点对象。调试绘制可以读取节点状态，正常运动更新由管理器调度个体完成。

配置持久化到 `user://boids_lab.cfg`，包括所有可视化开关，不包含个体生成结果、运行请求或选择状态。

## 单选与调试绘制

`BoidManager.SelectedBoid` 保存唯一选择；按下时命中测试在实际 Polygon2D 的局部坐标中进行，同时提供 10 个视口像素的中心容差。多个命中对象取屏幕距离最近者，完全重叠时优先当前选中个体。松开确认的是按下时记录的实例，避免运行中的个体移走而选不中。再次点击选中对象或点击空白会取消，拖动及 UI 点击保持选择。Reset 和 RemoveBoid 清理选择，已移除的待定点击对象不会再被选中。

`Boid2D.SetSelected()` 只改变 Body 的颜色；取消时恢复进入场景时记录的原色，不改变运动状态。`SimulationView` 额外绘制选择圆环。高亮不依赖任何可视化开关；UI 隐藏后仍属于世界的一部分。

`DebugOverlayGeometry.Build()` 从当前节点状态构造四组线段，再由 `SimulationView._Draw()` 使用原生 `DrawMultiline()` 绘制：

- `RangePoints`：仅选中个体的感知范围，以 96 段圆弧近似，没有选择时不生成。先裁剪到最短环绕位移区域，再延续到画面另一侧，避免大半径时画出不属于感知边界的重复弧线。半径大于等于世界半对角线时，范围覆盖整个环绕空间，绘制世界边框。
- `SeparationRangePoints`：仅选中个体的实际分离范围，半径为 `min(SeparationRadius, PerceptionRadius)`，复用同一套圆弧与环绕裁剪。独立开关 `ShowSeparationRadius` 默认关闭，保存键为 `visualization/show_separation_radius`；旧配置缺失时使用默认值。感知范围为青绿色 3 像素线，分离范围为橙色 2 像素线，均按相机缩放补偿以保持视口线宽。
- `VelocityPoints`：所有非静止个体的实际速度方向箭头，不使用可能滞后的 Rotation。长度取 `speed × 0.25s`，限制在 8–80 个视口像素；线宽与箭头头部随缩放补偿。
- `NeighborPoints`：仅对选中个体扫描当前群体，用与模拟共用的 `NeighborSearch2D.WrappedOffset()` 判定邻居。跨边界连线裁剪并平移到对侧，显示最短路径。

四组数据在每次重绘前清空重建，关闭开关、取消选择、移除或 Reset 不会留下旧图形。暂停时配置、选择与相机变化也会请求重绘；运行时沿用运动更新后的重绘通知。调试数据属于当前画面状态，不覆盖物理步起点的 `NeighborSearch` 快照或 `Stats`。感知与分离范围只为选中对象生成，不遍历全体个体，成本不随数量增长；全体箭头和选中对象的邻居查询仍是 O(N)，与模拟所选搜索模式独立。绘制及调试查询开销反映在 FPS 中，不进入模拟计时。

## 模块职责

| 文件 | 职责与扩展位置 |
| --- | --- |
| `MainController.cs` | 创建并加载共享配置、实例化世界、接收 UI 事件与切换 HUD；通过 ISimulationWorld 发送控制请求，每 0.25 s 刷新统计 |
| `ISimulationWorld.cs` | UI 与世界的公共约定：初始化、运行/暂停、重置、单步、配置变化、统计及单位信息 |
| `BoidManager.cs` | Node2D 世界根；按种子生成个体，管理创建、移除和 Reset；同步相机可见边界、统一更新、环绕与计时 |
| `Boid2D.cs` | 保存个体线性/角运动状态；SetAcceleration 接收输入，UpdateBoid 积分速度与位置，UpdateFacing 平滑跟随运动方向 |
| `NeighborSearch2D.cs` | 两种邻居搜索、步前状态快照、环绕距离、邻居下标列表及实测搜索统计 |
| `BoidSteering.cs` | 只读快照计算分离、对齐、聚集，按开关/权重合成并限制群集加速度 |
| `BoidConfig.cs` | 参数默认值、范围、规则/可视化开关、输入约束和配置保存/加载 |
| `SimulationStats.cs` | 维度无关的公共统计数据，null 表示未测量 |
| `SimulationView.cs` | 按动态边界绘制背景、网格、四组调试线段和选择圆环 |
| `DebugOverlayGeometry.cs` | 当前状态的感知/分离范围、速度箭头、选中对象邻居查询及环绕线段裁剪 |
| `SimulationCamera2D.cs` | 原生 Camera2D；区分点击/拖动，处理缩放、Home、视口适配，通知手势和视角变化 |
| `ControlPanel.cs` | 创建原生参数控件，修改共享配置，通过 C# 事件发送控制请求；单位来自世界接口 |
| `StatsPanel.cs` | 展示实际测量结果，null 统一显示 `--` |
| `main.tscn` | Game、World 容器及 CanvasLayer HUD |
| `boids_world_2d.tscn` | 管理器根节点、个体容器、背景/调试节点和 Camera2D |
| `boid_2d.tscn` | 单个 Boid 的场景模板，子 Polygon2D 定义三角形外观 |
| `lab_theme.tres` | 原生 Theme / StyleBoxFlat，统一 UI 样式 |

## 控制与统计语义

- `Initialize(config)`：接收共享配置，验证参考尺寸。已就绪的世界会复位参考视角、同步可见边界并 Reset。
- `SetRunning(running)`：开启或暂停固定物理时间步的运动更新。
- `Reset()`：暂停、移除旧个体，ID 归零，按配置重新生成；加速度、角速度和计时清零或恢复未测量。
- `SpawnBoid(position, velocity)`：世界就绪后创建并注册一个个体，分配递增 ID，更新真实数量。
- `RemoveBoid(boid)`：移除已注册个体，保留其余个体的 ID 和状态，更新真实数量。
- `Step(deltaSeconds)`：明确推进一个时间步，先搜索邻居并计算全部转向，再调用个体运动积分并环绕越界位置。内部使用或测试可直接调用，运行回调由 IsRunning 控制。
- `StepOnce()`：保留入口，CanStep 为 false，因此当前不执行 UI 单步。
- `CoreImplemented`：当前为 true，基础运动、邻居搜索与三条群集规则均已实现。

实际数量来自已注册集合，包括空群体的 0。Stopwatch 分别测量搜索耗时与整步耗时：前者包含快照、格子构建、候选距离检查与结果写入；后者还包含三条规则计算、个体运动积分与边界处理，不包含渲染。候选检查次数按有向距离检查计数，排除自身，不计格子查找，不把规则中的邻居遍历重复算作搜索检查；平均邻居数为有向邻居总数除以实际数量，空群体测量后为 0。界面显示最近一次物理步的数值，并非 0.25 秒内的累加或平均，FPS 独立来自引擎实测。

首次运行前、Reset、增删个体或邻域结果失效时，计时与邻居指标显示 `--`；暂停且未改变查询条件时保留最后一次读数。环绕以相机当前可见区域为边界：个体中心超出右侧从左侧进入，其他方向类似，保留越界余量和速度。边界内个体不做重复取模；视角变化也会立即环绕越界个体并使旧邻域结果失效。

## 复用 UI 到 3D

未来新增一个根节点为 Node3D、实现 `ISimulationWorld` 的场景，并在 `MainController.SimulationScene` 中指定。该世界自己拥有 3D 个体、Camera3D 和 3D 更新实现，仍挂入 World 容器；CanvasLayer 下的 Control 控件作为屏幕空间 HUD 复用。

ControlPanel 和 StatsPanel 不接触 Vector2、Vector3、Camera2D 或 Camera3D。长度、速度和加速度的提示单位来自 `ISimulationWorld.LengthUnit`。当前 `BoidManager` 与 `Boid2D` 都专用于 2D，没有实现 3D 场景或模式切换；3D 的单位、参数范围与默认值需要届时明确。

## 后续实现顺序

以下内容仍未实现：

1. 根据后续需求加入数量即时增减与 UI 单步控制。

当前空间划分只使用简单均匀网格；没有引入 Compute Shader、碰撞节点、ECS、多线程或对象池。后续根据测量结果决定是否继续优化。

## 验证边界

`tests/pause_shortcut_checks.tscn` 在真实主场景中验证空格在无焦点、按钮、复选框、数字输入框、搜索方式控件有焦点及 HUD 隐藏时均切换运行状态，过滤长按重复与松开，不触发重置、修改参数或打开选择菜单；同时验证 Enter 仍可正常激活按钮。

本次重构的 C# 编译已通过，零警告、零错误；静态结构检查已通过，覆盖资源路径、UID、节点层次、场景插槽、个体三角形场景、统一更新入口及旧类型引用清理。

基础运动通过 `tests/motion_checks.tscn` 在真实 Godot 节点上的无窗口检查，覆盖匀速、加速度积分与限幅、60/120 步对比、角速度/角加速度限制、朝向收敛、最短转向、零速、暂停/重置、种子复现、边界环绕、真实统计及新旧配置兼容。测试实现为 `tests/MotionChecks.cs`，不依赖第三方测试框架。

`tests/neighbor_checks.tscn` 验证两种搜索均与独立的环绕距离参考一致，覆盖稀疏/密集/随机分布、接缝、重合位置、半径边缘、小网格、空群体、候选次数与平均值；同时检查真实管理器的快照时序、增删、边界与配置失效、搜索方式 UI 绑定和统计显示。测试只验证候选数减少，不断言运行时间的固定加速倍数。

`tests/steering_checks.tscn` 检查单规则方向、分离距离权重、开关与权重变化、环绕接缝、完全重合、零方向/无邻居/零上限、加速度合成、列表顺序与两种搜索方式的一致性。真实管理器检查全部规则基于步前状态、UI 绑定、新旧配置，并在两种模式下各运行 300 步，验证有限状态、速度/加速度限幅、边界与真实轨迹变化。运动和邻居检查中的独立场景显式关闭群集规则，以隔离各模块责任。

`tests/debug_checks.tscn` 通过真实视口鼠标事件验证暂停/运行单选、取消、颜色恢复、点击与拖动阈值、UI 输入隔离、失焦取消、重叠点击、缩放/旋转后的命中、删除/重置清理。几何检查覆盖选中对象感知范围/连线、全体箭头、切换/取消选择后的范围清理、静止方向、边缘与角落环绕、大半径范围及各开关清理；同时在 `_Draw()` 中生成数据并确认未改变模拟统计。检查使用无窗口模式，不代替图形驱动下的视觉验收。

新增观察控件已在 Godot `--headless` 模式下实例化当前主场景并验证：HUD 隐藏/恢复、焦点释放、按键重复过滤、参数面板输入隔离、全景及 8 倍缩放下的左键拖动、跨入 UI 后松手、失焦与丢失松开事件后的拖动取消、鼠标位置缩放及上下限、Home 复位、视口尺寸适配。检查没有创建原生窗口；失焦检查仅发送信号，视口适配仅修改无窗口视口尺寸。

用户更新显卡驱动后反馈简单测试暂未复现蓝屏。本次没有进行截图、原生窗口操作或驱动稳定性测试。旧版 Control 画布的 70 项运行检查及截图只属于历史记录，不能证明当前布局或图形驱动的稳定性。
