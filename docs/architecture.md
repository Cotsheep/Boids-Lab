# BoidsLab 架构与扩展入口

## 更新方式与职责划分

项目参考 [SebLague/Boids 的管理器](https://github.com/SebLague/Boids/blob/fdda4db1368df92fbacd8a644bbe6dc61d705441/Assets/Scripts/BoidManager.cs)与[个体脚本](https://github.com/SebLague/Boids/blob/fdda4db1368df92fbacd8a644bbe6dc61d705441/Assets/Scripts/Boid.cs)，采用“管理器统一调度、个体更新自身、独立场景显示外观”的组织方式。2D 已实现基础运动、朝向跟随、两种 CPU 邻居搜索，以及分离、对齐、聚集规则；3D 已实现创建与基础运动、观察相机、单选调试和朴素邻居搜索。均没有 GPU 计算。

`BoidManager` 是 2D 世界根节点，也是该 2D 世界唯一驱动 Boid 模拟的 Godot 回调入口。`Boid2D.UpdateBoid(deltaSeconds)` 是普通 C# 方法，由管理器显式调用；个体没有自己的 `_Process()` 或 `_PhysicsProcess()`。

每只 Boid 使用原生 `Node2D.Position` 和 `Rotation`，并保存 `Velocity`、`Acceleration`、`AngularVelocity`、`AngularAcceleration`。ID 在一次 Reset 周期内唯一；没有供渲染同步的状态副本。个体场景中的 `Polygon2D` 跟随父节点变换显示外观。

## 场景结构

```text
main.tscn / Game (Node，MainController)
├── World (Node，世界场景容器)
│   └── boids_world_2d.tscn / BoidsWorld2D (Node2D，BoidManager)
│       ├── SimulationView (Node2D，背景、网格与调试绘制)
│       ├── Boids (Node2D，运行时生成的个体容器)
│       ├── Obstacles (Node2D，运行时创建的矩形/圆形障碍物)
│       ├── Camera2D (SimulationCamera2D，拖动、缩放与全景复位)
│       └── ObstacleEditor (Node2D，ObstacleEditor2D，放置预览与编辑输入)
└── UI (CanvasLayer，layer = 1)
	├── UIRoot (Control，全窗口 HUD)
	│   ├── 顶部标题、运行状态与操作说明按钮
	│   ├── 左侧参数面板 / ControlPanel
	│   ├── 世界信息
	│   └── 底部统计面板 / StatsPanel
	└── HelpOverlay (Control，默认隐藏的操作说明弹层)
		├── Shade (ColorRect，背景遮罩)
		└── Card (PanelContainer，标题、关闭按钮与可滚动说明)
```

独立的个体场景由 `BoidManager.BoidScene` 指定，目前指向 `scenes/boids/boid_2d.tscn`：

```text
Boid2D (Node2D，Boid2D 脚本)
└── Body (Polygon2D，朝向本地 +X 的三角形)
```

`SpawnBoid(position, velocity)` 会实例化该场景，初始化配置、ID、速度与朝向，加入 `Boids` 容器并注册到管理器集合。`RemoveBoid(boid)` 从集合和场景树中移除个体，再请求释放节点。创建、移除操作应在管理器的更新遍历之外进行。启动和 Reset 会按配置数量生成随机位置、随机方向的个体，初始速率为 `MaxSpeed`，加速度和角速度为零；初始保持暂停。

世界场景由 `MainController.SimulationScene` 插槽指定，主控制器在运行时将它实例化到 `World` 节点。世界直接使用根视口，HUD 覆盖在上方。这里只指显示组织方式，没有切换操作系统的独占全屏模式。

`BoidManager.WorldSize` 默认 1280 × 720，仅用于初始与 Home 参考视角。`ViewBounds` 保存可见矩形；`WrapBounds` 根据领队引用计算：无领队时为 ViewBounds，有领队时为 null，表示无限世界。共享的 `WorldGeometry2D` 负责位置规范化与两种距离计算。

相机拖动、缩放、Home 或视口尺寸变化后发出 `ViewChanged`。`UpdateViewBounds()` 更新可见矩形：有领队时只刷新绘制；无领队时还立即环绕越界个体并使邻居结果失效。暂停也执行这一步，保留速度、旋转和身份。

参考矩形与 `ViewBounds` 分开保存；可见范围不传回 `Configure()`，缩放上下限保持 0.25–8 倍。平移无地图范围限制；大坐标仍存在单精度精度损失，尚未使用浮动原点。

网格间距为 40 世界像素，网格线始终位于世界坐标的 40 整倍数处，只绘制当前可见部分；经过缩放后不一定等于 40 屏幕像素。这个网格只是视觉辅助，与邻居搜索内部使用的空间格子独立。左键拖动画面平移视角，位移按当前相机倍率换算，画面在不同倍率下都跟随鼠标。滚轮以鼠标位置为中心缩放，倍率限制为参考视角的 0.25–8 倍，每格乘或除以 1.15；Home 结束拖动、恢复参考视角并居中。视口尺寸变化时，相机重新计算适配倍率，保留用户倍率与观察位置。

HUD 使用独立 CanvasLayer，不跟随 Camera2D 的世界变换。UIRoot、纯布局节点和提示文字不拦截鼠标，有交互的参数面板、滚动区域和按钮正常处理输入。

F1 和顶部“隐藏界面”按钮切换整个 `UIRoot.Visible`，包含占位文字、提示和统计。`MainController._Input()` 接收全局 `toggle_ui` 动作，即使输入框有焦点也能隐藏/恢复 HUD，隐藏前释放控件焦点。相机在 `_UnhandledInput()` 中处理滚轮和 `reset_camera`（Home），让 UI 优先处理输入；参数面板关闭 `MouseForcePassScrollEvents`，滚轮不会穿透到相机。快捷键映射位于 `project.godot`。

空格映射到 `toggle_pause`，由 `MainController._Input()` 在 GUI 处理前拦截。仅首次按下调用现有 `ToggleRunning()`；长按重复事件与松开事件也标记为已处理，避免触发有焦点控件的 `ui_accept`、改变复选框或在输入框插入空格。HUD 隐藏时同样有效，暂停按钮和状态文字同步刷新。这只切换模拟的运行状态，不暂停整个 SceneTree，所以相机、选择与 UI 继续响应。

操作指引集中在 `scenes/ui/help_overlay.tscn` 的 RichTextLabel 中。`HelpOverlay` 与 `UIRoot` 并列，所以 F1 隐藏 HUD 后仍可由 F2 打开。F2（`toggle_help`）、Esc（`close_help`）与空格都由主控制器处理；弹层不会自动修改 `IsRunning`。弹层最大为 760 × 680，随可用视口缩小，正文在内部滚动，不创建原生 Window。

`HelpOverlay` 负责显示、关闭、居中及遮罩点击。遮罩消费按下和松开，松开后再关闭，避免关闭点击穿透；点击卡片不会关闭。`MainController.OnHelpOpenChanged()` 暂时禁用 HUD 的递归键盘焦点，并通过 `ISimulationWorld.SetInteractionEnabled(false)` 禁用世界交互。2D 世界把该请求交给相机，取消未结束的点击/拖动并关闭手势处理，仍记录原始光标位置供恢复使用，物理更新照常执行。关闭后恢复世界输入和仍可见的原控件焦点。3D 世界把同一交互开关交给 SimulationCamera3D，取消选择手势和相机拖动；弹层无需依赖具体相机类型。

相机只在 `_UnhandledInput()` 收到左键按下时开始一个待定手势，确保参数控件优先处理点击。通过 `WorldPressStarted(viewportPosition)` 让管理器记录按下时命中的个体；累计位移不足 5 个视口像素时不移动相机，越过阈值后按完整累计位移开始拖动。`_Input()` 接收后续移动与松开，跨入 UI 后也能结束拖动。未拖动且在世界内松开时发出 `WorldPressEnded(true)`，其余情况发出 false。失焦、丢失左键按住状态、Home、滚轮缩放或视口变化会取消待定手势，避免误选择或粘住相机。

隐藏 UI 不会暂停或重置模拟。相机不直接操作个体或 `BoidConfig`，通过 `ViewChanged` 让管理器同步可见范围；UI 可见性与相机视角不写入配置文件。相机自己订阅和解除视口尺寸、窗口失焦事件；管理器负责订阅和解除相机视角事件。

## 初始化与更新顺序

主控制器先创建、加载共享配置，再实例化世界并调用 `Initialize(config)`，最后把世界加入场景树。管理器在 `_Ready()` 中取得个体容器、背景和相机，绑定配置与相机事件，先适配参考视角并同步可见边界，再执行 Reset，确保个体生成在实际视野中。世界需要宿主先初始化；当前没有独立运行时的默认配置或自动开跑行为。

每个固定物理时间步的调用顺序为：

```text
Godot → BoidManager._PhysicsProcess(delta)
		   └── 运行请求开启时 → Step(delta)
								  ├── NeighborSearch.Search：复制状态并搜索邻居
								  ├── 读取领队步前快照，确定本步共用的身后跟随区域
								  ├── 优先避障，否则领队到达目标 / 跟随者的群集与跟随合力
								  ├── 遍历已注册个体
								  │     ├── Boid2D.UpdateBoid(delta, steering)：移动与朝向积分
								  │     └── ResolveMotion：按模式处理接触与边界；无障碍物时仅执行必要的环绕
								  └── 记录数量、搜索/更新耗时、候选次数与平均邻居数
```

`Step()` 在个体循环前按 WrapBounds 规范化位置，然后调用 `NeighborSearch2D.Search(..., wrapBounds)`。两种搜索都读取同一时间步开始时的位置、速度副本，不访问已经更新过的节点状态。随后为全部个体计算优先避障或群集转向，写入复用的 `Vector2[]`；全部算完后再逐个积分、按模式进行障碍物接触修正与环绕。个体增删后按实际数量调整数组，每步覆盖所有条目，关闭规则不会遗留旧转向。

`UpdateBoid()` 验证初始化状态和正的有限时间步长，执行移动与朝向积分，返回运动状态是否变化。管理器在运动改变状态后请求调试图形重绘。原生 `Polygon2D` 的显示由场景节点负责，不需要统一绘制所有个体的函数。这里使用 Godot 的固定物理回调（默认 60 次/秒）；`MainController` 每 0.25 秒只刷新统计标签，不影响运动频率。

## 邻居搜索

`NeighborSearch2D` 是普通 C# 类，没有自己的 Godot 回调。`NeighborSearchMode` 的 `Naive` 与 `UniformGrid` 共用快照、距离过滤和结果容器，UI 通过共享配置选择。默认使用朴素遍历，存储键为 `simulation/neighbor_search_mode`；缺失或非法值回退到默认方式。

`BoidSnapshot` 是只含 ID、Position、Velocity 的只读值类型。`NeighborSearch.Snapshots[i]` 对应本次管理器列表中的第 i 只个体，**i 不是个体 ID**，删除个体造成 ID 不连续也不会影响搜索。`GetNeighborIndices(i)` 返回邻居的快照下标；`GetOffset(i, j)` 返回从 i 到 j 的直接或最短环绕位移。两种算法输出的邻居集合一致，但列表顺序不保证相同。

`BoidSteering.Calculate(search, index, config)` 使用这三个读取入口计算规则；它是无跨步状态的普通静态方法，不修改节点、不再次搜索邻居。

规则读取同一步的快照，位移由 `WorldGeometry2D.Offset` 决定：无限模式直接相减，环绕模式取最短周期位移。初始化、Reset、增删、搜索方式/半径变化和边界模式切换会使结果失效。相机变化只在无领队时影响边界及搜索。

朴素方式执行 `N(N−1)` 次距离检查。无限网格按 `floor(position / radius)` 分格，支持负坐标；环绕网格各轴用 `max(1, floor(边长 / radius))` 格均分边界，以避免末端残格漏查。查询自身与相邻格，环绕索引取模；一行/两行等情况不重复访问。字典在两种模式下都只保存占用的格子。

两种方式最终都排除自身，再按当前地图模式比较距离平方与半径平方，包含等于半径的情况与不同个体的位置重合。快照和邻居列表在数量不变时复用；格子成员在每步重新构建，其开销计入搜索时间。没有排序邻居、GPU、线程或对象池。

网格平均查询成本取决于候选总量 C，含建格通常为 `O(N + C)`（字典操作按平均成本计）；最坏情况下仍是 `O(N²)`。完整邻居下标列表占用 `O(N + K)` 空间，K 为有向邻居总数，密集场景也可能达到二次规模。它不保证所有数量和半径下都比朴素方式快。比较方式见 README。

## 分离、对齐、聚集的计算

三条规则共用同一步的邻居列表。对齐和聚集使用感知半径内的所有邻居；分离只使用 `min(SeparationRadius, PerceptionRadius)` 内的近邻。`SeparationRadius` 默认 25，范围和持久化集中在 `BoidConfig`，保存键为 `simulation/separation_radius`，旧配置缺失时使用默认值。界面显示设置值，超过感知半径时仅实际查询范围受限，不悄悄改写控件数值。

1. **分离**：累加 `-offset / distance²`，近邻对方向的影响更大。距离低于 0.001 像素时限制倒数，避免数值爆炸；完全重合时用唯一 ID 为一对个体指定相反的左右方向，确定且不消耗随机数。
2. **对齐**：求邻居速度的平均向量，采用其方向作为目标方向；目标速率为 `MaxSpeed`，不是严格匹配邻居平均速率。
3. **聚集**：求世界坐标位移的平均向量，朝这个局部中心转向。不能直接平均节点世界坐标，否则格线两边的群体会朝屏幕中间错误转向。

沿用参考项目的目标速度差思路，并显式按固定的 `ResponseSeconds = 1` 秒转换为加速度：

```text
单条规则加速度 = LimitLength((Normalize(目标方向) × MaxSpeed − 当前速度) / ResponseSeconds, MaxForce)
群集加速度 = LimitLength(分离加速度 × 分离权重 + 对齐加速度 × 对齐权重 + 聚集加速度 × 聚集权重, MaxForce)
```

每条规则关闭或权重为零时不贡献加速度；没有邻居或目标方向抵消为零时不凭空制动。`MaxSpeed = 0` 或 `MaxForce = 0` 时群集转向为零。没有最小速率限制，个体之间没有碰撞体或不重叠保证。两种搜索的结果集合相同，但累加顺序可能导致微小浮点误差；长期轨迹不承诺逐位一致。

## 领队、目标与跟随

地图模式仅取决于 `Leader` 是否为 null，与暂停、选择和跟随权重无关。`SetLeader` 只在有/无领队的切换上更新边界模式：清除邻居统计，返回环绕模式时立即规范化全体位置。更换领队不会传送个体。删除领队和 Reset 使用同一入口；恢复环绕不修改速度、朝向或运行开关，Reset 自身仍按原规则暂停。

`BoidManager.Leader` 保存唯一领队引用，`SelectedBoid` 仍只表示普通选择。`SetSelectedBoidLeader(bool)` 是 UI 请求入口，`SetLeader(Boid2D?)` 验证个体属于本世界、转移标记并清空旧目标。移除领队、Reset 或解除身份时清空引用和目标，不能只记录可能在重置后复用的 ID。`Boid2D.IsLeader` 管理持久的紫色本体，`SimulationView` 绘制紫色菱形和目标标记；金色选择圆环可以与身份标记同时出现。

`SimulationCamera2D.ObservePointer()` 保存最近鼠标事件的视口坐标；障碍物编辑器消费手势前也更新这个位置。`BoidManager._Process()` 在 GUI 处理后用最终相机变换换算鼠标世界坐标，调用 `SetLeaderTarget`；静止光标也能在相机移动后得到正确目标。UI 悬停、帮助、障碍物编辑、离开窗口或失焦时保留最后目标，暂停时仍可更新标记。此回调只采样输入，运动依然只由 `_PhysicsProcess` 驱动。UI 通过原有接口和事件同步角色，目标存在状态未变时不反复刷新控件。

`Step()` 先取得所有步前快照，再统一读取领队位置、速度和身后区域。领队速度大于 1 世界像素/秒时更新方向，停止时保留最后方向。区域半径 `r = 0.65 × SeparationRadius × sqrt(跟随数量)`（不超过一名跟随者时为 0）；中心为 `leaderPosition - heading × (LeaderFollowDistance + r)`。后方距离表示到区域前缘的间隔，区域面积随数量增加，不分配固定队形位置。

`LeaderSteering.Arrive()` 以直接世界位移计算到达转向：位置误差按 1 秒转换为追赶速度，加上目标速度，再按 0.25 秒响应计算加速度并限幅。领队追踪鼠标，接近 2 像素的静止目标后制动；没有目标时减速等待，不受三条群集规则牵引。`Follow()` 对区域外个体取最近区域边缘作为目标，区域内只匹配领队速度，消除共用目标点的持续挤压力。避障仍优先。

身份与目标不写入配置，后方距离和跟随权重仍由 BoidConfig 管理。跟随者通过 `BoidSteering.Calculate(..., guidance, protectSeparation)` 合成群集与跟随转向：局部距离从分离半径缩小到半径一半时，逐渐削减其他引导中抵消分离的分量，保留切向分量，然后加分离力并限制总加速度。仅启用领队跟随时保护，关闭分离或权重为零时不保护。没有固定编队或个体间硬碰撞保证。

`LeaderSteering.Follow()` 在加权前用 `weight × dt` 作为到达计算的有效步长，再乘跟随权重；这使高权重时的低速制动也不会跨过目标速度而反向。与群集规则合成之后，管理器仍对总加速度限幅。

## 障碍物编辑与避障

障碍物采用独立 `Obstacle2D` 节点，保存形状与尺寸，直接在世界坐标下绘制矩形或圆形。缩放改变几何尺寸，不通过 Node2D.Scale 放大描边或鼠标容差；选中边框与缩放手柄按相机倍率补偿为稳定的屏幕尺寸。障碍物位置独立于相机，相机变化也不修改障碍物几何。

`ObstaclePanel` 只发送创建、删除、尺寸修改与取消放置请求。`Refresh(ObstacleSelection?, ObstacleShape?)` 接收选中对象的形状、宽高与当前放置状态；用 `SetValueNoSignal()` 回填数值，避免拖动时的状态同步反过来触发编辑。圆形显示直径，矩形显示宽度和高度；无选择时尺寸与删除控件禁用。尺寸范围来自 `Obstacle2D` 的统一常量，避障参数范围与持久化由 `BoidConfig` 管理。

添加按钮进入放置模式，下次世界点击确定位置并选中。障碍物本体拖动、缩放手柄及 Shift + 滚轮缩放优先于相机输入；普通空白拖动和滚轮保留原有相机操作。障碍物选择与 Boid 选择互斥。Delete 删除选中障碍物，但编辑输入框时由 GUI 处理；Esc 或右键取消放置。操作说明打开时取消待定操作并禁用世界交互，模拟运行状态不受影响。

`ObstacleAvoidance` 从当前个体状态和障碍物几何计算避让方向，障碍物检测与两种 Boid 邻居搜索独立。需要避让时优先采用避障转向，其他时间沿用三条群集规则。`AvoidanceEnabled` 默认开启，`AvoidanceLookAhead` 默认为 0.75 秒，`ObstacleClearance` 默认为 12 世界像素，旧配置没有这些键时保留默认值。障碍物布局、选中对象与放置状态不保存；Reset 保留布局，只重建鸟群。

圆形按半径加间距检测，矩形按扩张后的轴对齐包围盒检测，因此矩形角部留距较保守。运动积分后用本步线段扫描障碍物，命中时截断位移、消除朝内的速度分量而保留切向速度；起点重叠时先尝试移到表面之外。接触修正与受 `MaxForce` 限制的转向是不同步骤，不把硬接触引起的速度改变伪装成受限加速度。关闭避障同时关闭这两部分。

`ObstacleAvoidance` 同时接收本步 WrapBounds。无限模式扫描完整世界轨迹和全部障碍物；环绕模式在视口出口处分段，到对侧后继续扫描，且只检测与边界矩形相交的形状。障碍物自身不传送，也不生成周期副本。接触最多处理 64 段，重叠恢复最多 8 轮；重叠封闭布局不保证能退出。

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

界面的“个体数量”表示目标数量。数量与种子修改后在 Reset 生效，当前没有自动同步数量差额。`InitializePopulation()` 每次创建按 `Config.RandomSeed` 初始化的随机数生成器，使用 `Config.BoidCount`、当前可见的 `ViewBounds` 和 `Config.MaxSpeed` 生成位置、方向与速度，再逐个调用 `SpawnBoid()`。同一引擎版本、相同配置和可见边界下可复现初始状态，不在每次参数变化时重新设种子。

管理器对外提供 `IReadOnlyList<Boid2D> Boids`，它包装同一个内部列表，会反映创建和移除。这个接口只限制集合增删入口；其中的 Boid 仍然是实际节点对象。调试绘制可以读取节点状态，正常运动更新由管理器调度个体完成。

配置持久化到 `user://boids_lab.cfg`，包括所有可视化开关，不包含个体生成结果、运行请求或选择状态。

## 单选与调试绘制

`BoidManager.SelectedBoid` 保存唯一选择；按下时命中测试在实际 Polygon2D 的局部坐标中进行，同时提供 10 个视口像素的中心容差。多个命中对象取屏幕距离最近者，完全重叠时优先当前选中个体。松开确认的是按下时记录的实例，避免运行中的个体移走而选不中。再次点击选中对象或点击空白会取消，拖动及 UI 点击保持选择。Reset 和 RemoveBoid 清理选择，已移除的待定点击对象不会再被选中。

`Boid2D.SetSelected()` 只改变 Body 的颜色；取消时恢复进入场景时记录的原色，不改变运动状态。`SimulationView` 额外绘制选择圆环。高亮不依赖任何可视化开关；UI 隐藏后仍属于世界的一部分。

`DebugOverlayGeometry.Build()` 从当前节点状态构造四组线段，再由 `SimulationView._Draw()` 使用原生 `DrawMultiline()` 绘制：

- `RangePoints`：选中个体的感知圆，96 段近似。无限模式只裁剪显示；环绕模式裁剪到最短位移区域后衔接到对侧，覆盖全域时绘制矩形边框。
- `SeparationRangePoints`：仅选中个体的实际分离范围，半径为 `min(SeparationRadius, PerceptionRadius)`，复用同一套圆弧与视口裁剪。独立开关 `ShowSeparationRadius` 默认关闭，保存键为 `visualization/show_separation_radius`；旧配置缺失时使用默认值。感知范围为青绿色 3 像素线，分离范围为橙色 2 像素线，均按相机缩放补偿以保持视口线宽。
- `VelocityPoints`：所有非静止个体的实际速度方向箭头，不使用可能滞后的 Rotation。长度取 `speed × 0.25s`，限制在 8–80 个视口像素；线宽与箭头头部随缩放补偿。
- `NeighborPoints`：扫描选中个体的当前邻居，距离规则与本步地图模式一致；无限模式只裁剪，环绕模式显示两侧短线。

四组数据在每次重绘前清空重建，关闭开关、取消选择、移除或 Reset 不会留下旧图形。暂停时配置、选择与相机变化也会请求重绘；运行时沿用运动更新后的重绘通知。调试数据属于当前画面状态，不覆盖物理步起点的 `NeighborSearch` 快照或 `Stats`。感知与分离范围只为选中对象生成，不遍历全体个体，成本不随数量增长；全体箭头和选中对象的邻居查询仍是 O(N)，与模拟所选搜索模式独立。绘制及调试查询开销反映在 FPS 中，不进入模拟计时。

## 模块职责

| 文件 | 职责与扩展位置 |
| --- | --- |
| `MainController.cs` | 创建并加载共享配置、实例化世界、接收 UI 事件、切换 HUD 与协调弹层输入隔离；通过 ISimulationWorld 发送控制请求，每 0.25 s 刷新统计 |
| `ISimulationWorld.cs` | UI 与世界的公共约定：初始化、运行/暂停、交互开关、重置、单步、配置变化、统计及单位信息 |
| `BoidManager.cs` | Node2D 世界根；按种子生成个体，管理创建、移除和 Reset；同步相机可见边界、统一更新与计时 |
| `Boid2D.cs` | 保存个体线性/角运动状态；SetAcceleration 接收输入，UpdateBoid 积分速度与位置，UpdateFacing 平滑跟随运动方向 |
| `NeighborSearch2D.cs` | 两种邻居搜索、步前快照、直接/环绕距离、邻居下标与实测统计 |
| `WorldGeometry2D.cs` | 共用的环绕位置和直接/最短环绕位移，不保存状态 |
| `BoidSteering.cs` | 只读快照计算分离、对齐、聚集，按开关/权重合成并限制群集加速度 |
| `LeaderSteering.cs` | 目标到达、移动目标速度补偿、身后区域跟随计算；不修改节点或保存身份 |
| `BoidConfig.cs` | 参数默认值、范围、规则/可视化开关、输入约束和配置保存/加载 |
| `SimulationStats.cs` | 维度无关的公共统计数据，null 表示未测量 |
| `SimulationView.cs` | 按动态边界绘制背景、网格、四组调试线段和选择圆环 |
| `DebugOverlayGeometry.cs` | 当前状态的感知/分离范围、速度箭头、选中对象邻居查询及视口线段裁剪 |
| `SimulationCamera2D.cs` | 原生 Camera2D；区分点击/拖动，处理缩放、Home、视口适配，通知手势和视角变化 |
| `ControlPanel.cs` | 创建原生参数控件，修改共享配置，通过 C# 事件发送控制请求；单位来自世界接口 |
| `ObstaclePanel.cs` | 障碍物创建/删除按钮、选中对象尺寸输入与放置状态；不引用世界节点 |
| `ObstacleShape.cs` | 形状枚举与尺寸选择信息，供编辑 UI 与世界共享 |
| `Obstacle2D.cs` | 矩形/圆形的尺寸、命中测试、世界绘制、高亮及缩放手柄 |
| `ObstacleAvoidance.cs` | 基于障碍物几何的前瞻避让与运动约束 |
| `ObstacleEditor2D.cs` | 障碍物列表与选择、放置预览、拖动/缩放/删除输入；不推进 Boid 模拟 |
| `HelpOverlay.cs` / `help_overlay.tscn` | 窗口内操作说明弹层、响应式布局、滚动正文和关闭行为；不修改模拟或配置 |
| `StatsPanel.cs` | 展示实际测量结果，null 统一显示 `--` |
| `main.tscn` | Game、World 容器及 CanvasLayer HUD |
| `boids_world_2d.tscn` | 管理器根节点、个体与障碍物容器、背景/调试节点和 Camera2D |
| `boid_2d.tscn` | 单个 Boid 的场景模板，子 Polygon2D 定义三角形外观 |
| `lab_theme.tres` | 原生 Theme / StyleBoxFlat，统一 UI 样式 |

## 控制与统计语义

- `Initialize(config)`：接收共享配置，验证参考尺寸。已就绪的世界会复位参考视角、同步可见边界并 Reset。
- `SetRunning(running)`：开启或暂停固定物理时间步的运动更新。
- `SetInteractionEnabled(enabled)`：世界就绪后启用/禁用相机和选择输入，取消待定手势；不改变模拟运行状态。
- `Reset()`：暂停、移除旧个体，ID 归零，按配置重新生成；加速度、角速度和计时清零或恢复未测量。
- `SpawnBoid(position, velocity)`：世界就绪后创建并注册一个个体，分配递增 ID，更新真实数量。
- `RemoveBoid(boid)`：移除已注册个体，保留其余个体的 ID 和状态，更新真实数量。
- `Step(deltaSeconds)`：明确推进一个时间步，先搜索邻居并计算全部转向，再调用个体运动积分并修正障碍物接触。内部使用或测试可直接调用，运行回调由 IsRunning 控制。
- `StepOnce()`：保留入口，CanStep 为 false，因此当前不执行 UI 单步。
- `CoreImplemented`：表示基础运动可运行，两个世界均为 true；可选功能是否实现由 `Features` 分别声明，不能据此判断群集规则已实现。

实际数量来自已注册集合，包括空群体的 0。Stopwatch 分别测量搜索耗时与整步耗时：前者包含快照、格子构建、候选距离检查与结果写入；后者还包含三条规则计算、个体运动积分与障碍物接触，不包含渲染。候选检查次数按有向距离检查计数，排除自身，不计格子查找，不把规则中的邻居遍历重复算作搜索检查；平均邻居数为有向邻居总数除以实际数量，空群体测量后为 0。界面显示最近一次物理步的数值，并非 0.25 秒内的累加或平均，FPS 独立来自引擎实测。

首次运行前、Reset、增删个体或邻域结果失效时，计时与邻居指标显示 `--`。暂停保留最近读数；有领队时平移缩放不影响物理状态和统计，无领队时同步新边界并使旧邻域结果失效。

## 三维基础世界与共享 UI

`scenes/main_3d.tscn` 继承 `main.tscn`，只替换 `SimulationScene`、`ConfigurationPath` 和 `HelpTextOverride`。默认项目入口仍为 2D；3D 入口在编辑器中打开后按 F6 运行，当前没有运行时模式切换按钮。主场景实际结构为：

```text
Game3D (MainController)
├── World (Node)
│   └── BoidsWorld3D (Node3D / BoidManager3D)
│       ├── Boids (Node3D)
│       │   └── Boid_0 ... (Boid3D)
│       │       └── Body (MeshInstance3D / CylinderMesh 六棱锥)
│       └── SimulationView (Node3D / SimulationView3D)
│           ├── ReferenceFrame (MeshInstance3D / ImmediateMesh 盒状边框)
│           ├── Grid (MeshInstance3D / 可切换底面网格)
│           ├── DebugOverlay (MeshInstance3D / 合批调试线段)
│           ├── Camera3D (SimulationCamera3D，环绕、平移、缩放与复位)
│           ├── WorldEnvironment
│           └── Sun (DirectionalLight3D)
└── UI (CanvasLayer)
    ├── UIRoot (共用 HUD)
    └── HelpOverlay (共用弹层，三维专用正文)
```

`BoidManager3D` 实现 `ISimulationWorld`，创建、重置和管理 `IReadOnlyList<Boid3D>`，统一负责运行状态、物理更新、环绕和计时。只读集合是管理器列表的实时视图，可通过 `SpawnBoid` / `RemoveBoid` 在更新遍历之外动态增减；UI 的目标数量仍在重置后生效。`RandomNumberGenerator` 按种子复现位置，方向在球面均匀采样，初始速率取最大速度。初始与重置后均暂停。

每个固定物理步的顺序为：

```text
Godot → BoidManager3D._PhysicsProcess(delta)
        └── IsRunning → Step(delta)
                       ├── 规范化位置，NeighborSearch3D 复制快照并朴素搜索
                       ├── 遍历个体
                       │   ├── Boid3D.UpdateBoid(delta)
                       │   │   ├── 合并外部 / 当步转向加速度，限制模长
                       │   │   ├── 更新速度并限速，再按新速度更新 Position
                       │   │   └── 计算方向误差，积分角速度并更新 Quaternion
                       │   └── X、Y、Z 分别环绕到固定 WorldBounds
                       ├── 发布真实搜索与整步统计
                       └── 从更新后的位置重建调试线段（不计入模拟耗时）
```

`Boid3D` 没有自身的物理帧回调，使用原生 Node3D 的 Position / Quaternion，并保存 Vector3 速度、加速度、角速度和角加速度。它负责运动积分，不使用 RigidBody3D、重力或接触求解。未来的三维群集规则应在管理器内先读取全体步前状态、计算每只个体的转向，再统一推进，避免前后遍历顺序改变结果。

本地 -Z 是前向。锥体网格沿 +Y 生成，子节点旋转 -90° 后尖端朝 -Z。朝向误差由当前前向与速度方向的叉积、点积计算最短旋转轴和角度；恰好掉头时沿自身上方轴旋转。阻尼跟随控制受最大角加速度与角速度约束，使用四元数组合避免欧拉角奇异；零速只制动已有旋转，不指定新的方向，也不强制消除滚转。

当前没有自动转向输入，因此默认轨迹是匀速直线。可在管理器外通过个体接口研究加速度，例如在世界就绪后调用：

```csharp
Boid3D boid = world.SpawnBoid(Vector3.Zero, new Vector3(80, 0, 0));
boid.SetAcceleration(new Vector3(0, 20, -10)); // 持续输入，受共享 MaxForce 限制
// 后续停止外部输入：boid.SetAcceleration(Vector3.Zero);
// 未来规则由管理器调用 UpdateBoid(delta, steeringAcceleration)，当步输入不保留。
```

加速度的代码入口已实现，UI 的“最大加速度”只是上限，不会凭空产生加速度。上调最大速度不会给慢速个体主动加速；重置重新生成时才使用新的最大速度作为初始速率。

`WorldSize` 默认 1280 × 720 × 720，以原点为中心。`WorldBounds` 是固定 Aabb，与相机视口独立。三个轴各自取模回到半开区间，保留速度和旋转，能处理一次跨越多个盒子宽度的情况。当前没有三维领队和障碍物，因此不应用二维的领队模式切换或接触处理。`SimulationView3D` 创建空间参考线，并将参考盒传给 SimulationCamera3D。相机右键绕观察中心旋转，中键沿观察平面平移，滚轮以鼠标投影位置为锚点缩放，Home 重置中心、角度及倍率。倍率限制为 0.25～8，俯仰限制为 ±85°，避免翻越极点。相机配置与模拟边界相互独立。

相机仅在 `_UnhandledInput` 开始手势，让 UI 优先处理输入；开始后由 `_Input` 接管移动与松开，因此可以在 UI 上结束世界拖动。失焦、鼠标离窗、丢失按键状态、视口变化、Home、缩放或帮助弹窗会取消待定手势。视口适配保留观察中心和用户倍率，三维相机不修改个体、共享配置或统计。

两个世界共用 BoidConfig 的默认值和范围；三维的长度、速度、加速度使用世界单位，例如默认速度 120 世界单位/秒，不隐式换算成米。MainController 通过导出的 ConfigurationPath 分别读写 `user://boids_lab.cfg` 与 `user://boids_lab_3d.cfg`。三维暂时不使用的配置字段不参与运动计算。

`SimulationFeatures` 声明可选能力：2D 为 All，3D 为 NeighborSearch | DebugDrawing。新增 UniformGridSearch 能力独立控制搜索方式选项，因此三维只显示禁用切换的朴素遍历项，初始化、重置和配置变化时会统一约束为 Naive。三维没有启用 Flocking、Leader 或 Obstacles，相应规则、领队、障碍物与分离范围控件保持隐藏；操作说明明确搜索暂不改变运动。共享 UI 继续复用同一配置与事件接口。

ControlPanel / StatsPanel 不接触 Vector2、Vector3 或相机。三维统计报告实际数量、最近一步更新/搜索耗时、候选次数与平均邻居数。MainController 的 0.25 秒计时仍只刷新统计 UI，模拟每个物理帧持续推进。

`NeighborSearch3D` 在物理步开始时复制 ID、位置与速度。结果中的整数是快照下标，不是可能不连续的 ID；数组与列表在数量不变时复用，结果只在下一次搜索或失效前有效。朴素搜索对每个个体检查全部其他个体，有向候选次数为 N × (N − 1)，包含恰在半径上的邻居和位置重合的其他个体。平均邻居数为有向邻居总数除以个体数；空群体完成搜索后为 0。搜索计时包含快照、距离判定和结果写入，整步计时还包含运动与环绕，不包含调试绘制。

`WorldGeometry3D` 统一位置规范化、最短三轴环绕位移与半径判定。相隔半边长的两个点保留原位移方向，其他跨界位移选取更短一侧。摄像机操作、普通选择和绘制开关不使搜索结果失效；感知半径变化、Reset、增删个体会清除旧结果，统计显示 `--`，直到下一个物理步重新测量。

左键选择沿 Camera3D 的投影射线检查网格包围球，保留至少 10 个屏幕像素的点击容差。取最近命中，完全重合时优先保留已选对象以便再次点击取消；不选相机后方或视锥之外的对象。按下时记录对象，松开且未拖动才提交选择，因此运行中的个体移动不会改变这次点击的身份。右键、中键与超过阈值的左键拖动不改变选择。删除选中对象、重置和输入隔离负责清理对象或手势引用。

`Boid3D.SetSelected()` 仅替换自身 Body 的 MaterialOverride，选中材质来自个体场景中的共享只读资源，不修改普通网格材质。取消时恢复原覆盖材质。ControlPanel 在没有领队控件时显示普通选择的 ID。

`DebugOverlayGeometry3D` 从当前节点位置生成线段，邻居查询只针对选中个体，复杂度 O(N)，不复用旧物理快照，也不改写它。感知范围仅生成选中对象的球面经纬线；线段按周期图像裁剪到模拟盒内，在对侧接续。球半径超过某轴半边长时，还按最近周期球心的区域裁剪，避免显示重叠球面的内部边界；半径覆盖整个环绕世界时用十二条盒边表示全域。邻居线使用相同的最短位移；速度箭头作用于全体非静止个体，在小盒中限制箭头长度，避免一支箭头绕行多圈。

`SimulationView3D.RefreshDebug()` 在物理更新后、选择/参数变化和相机变化时重建一份 ImmediateMesh，将感知范围、邻居线、速度箭头按颜色一起提交。暂停时仍可立即观察新的半径与选择。底面网格单独控制可见性，盒状边框始终保留。三维分离规则未实现，因此分离范围暂不绘制。

## 后续实现顺序

以下内容仍未实现：

1. 3D：接入均匀网格，并与朴素基准比较结果；随后逐条实现分离、对齐、聚集，再推进障碍物与领队。
2. 两个版本：根据后续需求加入数量即时增减与 UI 单步控制。

当前空间划分只使用简单均匀网格；没有引入 Compute Shader、碰撞节点、ECS、多线程或对象池。后续根据测量结果决定是否继续优化。

## 验证边界

`spatial_3d_checks` 使用独立的 27 幅周期图像距离验证三维朴素搜索，覆盖边/面/角落、半径边缘、重合、随机分布、空/单个体、非连续 ID、数组重建、快照冻结和统计生命周期。调试几何检查覆盖跨界线长、盒内裁剪、大半径、全域感知和不改写模拟统计。

`observation_3d_checks` 通过真实三维主场景和视口输入验证相机旋转、平移、缩放上下限与锚点、Home、UI/帮助隔离、视口适配、失焦及手势取消；同时检查暂停/运行单选、按下身份保持、前后遮挡、取消/转移/删除/重置、材质隔离、调试控件保存和即时更新。仅无图形验证，尚未人工验收画面。

`motion_3d_checks.tscn` 通过真实 Godot 三维节点验证线性积分、步长一致性、受限角运动、竖直和掉头方向、三轴正负环绕、多圈越界、种子复现、动态增删和统计。实例化真实三维入口，检查共享 UI、独立设置路径、暂停/继续的实际物理回调、未实现控件隐藏、帮助内容、原生网格朝向及相机视锥覆盖。构建零警告、零错误；三组三维测试和受共享 UI 影响的八组二维回归均通过。仅无图形验证，未进行图形窗口视觉验收。

`boundary_mode_checks.tscn` 覆盖有/无领队反复切换、暂停时立即传送、领队转移不传送、删除和 Reset、视角变化、速度朝向保持，以及朴素/网格搜索与调试显示的一致性。邻居测试以独立的周期图像距离验证小网格、边缘、随机分布和重复候选；避障检查验证双向传送入口的连续扫描。

`leader_checks` 验证全局跟随、停靠、快照时序、避障与角色生命周期；`leader_interaction_checks` 使用真实主场景验证连续光标、暂停、UI/弹层/编辑隔离、相机变换、失焦、隐藏 HUD 和重置。`leader_spacing_checks` 验证 40/200 名跟随者及高权重，在静止和连续转弯时避免整体重叠且保持跟随。

`tests/obstacle_avoidance_checks.tscn` 验证两种形状的预测转向、接触扫描、切向运动、重叠恢复、正负方向出屏运动和视野外障碍检测；实际管理器检查避障开关、零转向上限下的接触兜底，以及每种形状连续 300 步的绕行和间距。`tests/obstacle_interaction_checks.tscn` 验证真实主场景中的放置、选择、拖动、手柄/数值/滚轮缩放、Delete 焦点隔离、弹窗、隐藏 HUD 及重置保留布局。仅使用无窗口模式，未进行图形驱动下的视觉验收。

`tests/help_overlay_checks.tscn` 检查真实主场景的帮助按钮、F2/Esc、遮罩点击、内部滚动、HUD 隐藏时打开、焦点限制、空格控制、后台模拟继续更新、阻断背景鼠标和 Home、取消打开前的手势，以及 960 × 640 与 1920 × 1080 无窗口视口中的居中布局。不会保存用户配置或创建图形窗口。

`tests/pause_shortcut_checks.tscn` 在真实主场景中验证空格在无焦点、按钮、复选框、数字输入框、搜索方式控件有焦点及 HUD 隐藏时均切换运行状态，过滤长按重复与松开，不触发重置、修改参数或打开选择菜单；同时验证 Enter 仍可正常激活按钮。

本次重构的 C# 编译已通过，零警告、零错误；静态结构检查已通过，覆盖资源路径、UID、节点层次、场景插槽、个体三角形场景、统一更新入口及旧类型引用清理。

基础运动通过 `tests/motion_checks.tscn` 在真实 Godot 节点上的无窗口检查，覆盖匀速、加速度积分与限幅、60/120 步对比、角速度/角加速度限制、朝向收敛、最短转向、零速、暂停/重置、种子复现、无限世界运动、真实统计及新旧配置兼容。测试实现为 `tests/MotionChecks.cs`，不依赖第三方测试框架。

`tests/neighbor_checks.tscn` 验证两种搜索均与独立的欧氏距离参考一致，覆盖稀疏/密集/随机分布、格线、重合位置、半径边缘、负坐标网格、空群体、候选次数与平均值；同时检查真实管理器的快照时序、增删、视角独立性与配置失效、搜索方式 UI 绑定和统计显示。测试只验证候选数减少，不断言运行时间的固定加速倍数。

`tests/steering_checks.tscn` 检查单规则方向、分离距离权重、开关与权重变化、视口两侧的非邻居、完全重合、零方向/无邻居/零上限、加速度合成、列表顺序与两种搜索方式的一致性。真实管理器检查全部规则基于步前状态、UI 绑定、新旧配置，并在两种模式下各运行 300 步，验证有限状态、速度/加速度限幅、真实轨迹变化。运动和邻居检查中的独立场景显式关闭群集规则，以隔离各模块责任。

`tests/debug_checks.tscn` 通过真实视口鼠标事件验证暂停/运行单选、取消、颜色恢复、点击与拖动阈值、UI 输入隔离、失焦取消、重叠点击、缩放/旋转后的命中、删除/重置清理。几何检查覆盖选中对象感知范围/连线、全体箭头、切换/取消选择后的范围清理、静止方向、视口边缘与角落裁剪、大半径范围及各开关清理；同时在 `_Draw()` 中生成数据并确认未改变模拟统计。检查使用无窗口模式，不代替图形驱动下的视觉验收。

新增观察控件已在 Godot `--headless` 模式下实例化当前主场景并验证：HUD 隐藏/恢复、焦点释放、按键重复过滤、参数面板输入隔离、全景及 8 倍缩放下的左键拖动、跨入 UI 后松手、失焦与丢失松开事件后的拖动取消、鼠标位置缩放及上下限、Home 复位、视口尺寸适配。检查没有创建原生窗口；失焦检查仅发送信号，视口适配仅修改无窗口视口尺寸。

用户更新显卡驱动后反馈简单测试暂未复现蓝屏。本次没有进行截图、原生窗口操作或驱动稳定性测试。旧版 Control 画布的 70 项运行检查及截图只属于历史记录，不能证明当前布局或图形驱动的稳定性。
