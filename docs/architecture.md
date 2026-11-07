# BoidsLab 架构与扩展入口

本文说明当前实现；领域术语见[术语表](../CONTEXT.md)，验证结果见[验证记录](3d-implementation-validation.md)。除单独说明外，二维运动坐标属于 `Boids` 容器，默认场景中与世界坐标一致；三维同理。障碍查询会把完整父变换转换到该模拟坐标系。

## 更新方式与职责划分

项目参考 [SebLague/Boids 的管理器](https://github.com/SebLague/Boids/blob/fdda4db1368df92fbacd8a644bbe6dc61d705441/Assets/Scripts/BoidManager.cs)与[个体脚本](https://github.com/SebLague/Boids/blob/fdda4db1368df92fbacd8a644bbe6dc61d705441/Assets/Scripts/Boid.cs)，采用“管理器统一调度、个体更新自身、独立场景显示外观”的组织方式。2D 与 3D 均已实现基础运动、朝向跟随、两种 CPU 邻居搜索、分离/对齐/聚集规则、观察相机与单选调试。均没有 GPU 计算。

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

世界场景、配置路径和说明文字由 `MainController.World2D` / `World3D` 引用的 `SimulationWorldDefinition` 资源指定。主控制器将当前世界实例化到 `World` 节点，并支持运行时切换；一次只保留一个世界。世界直接使用根视口，HUD 覆盖在上方。这里只指显示组织方式，没有切换操作系统的独占全屏模式。

`BoidManager.WorldSize` 默认 1280 × 720，仅用于初始与 Home 参考视角。`ViewBounds` 保存可见矩形；`WrapBounds` 只在无领队且未开启 `ViewportBoundaryEnabled` 时返回该矩形，其余使用非周期坐标。无领队且开启开关时，`IsViewportBoundaryActive` 为 true，管理器以 `ViewportBoundaryBounds` 独立施加软边界转向：相对视口四周各外扩 10%，中心相同，宽高为视口的 1.2 倍；存在领队时为无限世界。共享的 `WorldGeometry2D` 负责位置规范化与两种距离计算。

相机拖动、缩放、Home 或视口尺寸变化后发出 `ViewChanged`。`UpdateViewBounds()` 更新可见矩形：领队模式只刷新绘制；观察模式使旧邻居统计失效，环绕模式立即规范化越界位置，软边界模式保留全部位置。暂停也同步新范围，但软边界回归只在下一次模拟更新或单步发生。

参考矩形与 `ViewBounds` 分开保存；可见范围不传回 `Configure()`，缩放上下限保持 0.25–8 倍。平移无地图范围限制；大坐标仍存在单精度精度损失，尚未使用浮动原点。

网格间距为 40 世界像素，网格线始终位于世界坐标的 40 整倍数处，只绘制当前可见部分；经过缩放后不一定等于 40 屏幕像素。这个网格只是视觉辅助，与邻居搜索内部使用的空间格子独立。左键拖动画面平移视角，位移按当前相机倍率换算，画面在不同倍率下都跟随鼠标。滚轮以鼠标位置为中心缩放，倍率限制为参考视角的 0.25–8 倍，每格乘或除以 1.15；Home 结束拖动、恢复参考视角并居中。视口尺寸变化时，相机重新计算适配倍率，保留用户倍率与观察位置。

HUD 使用独立 CanvasLayer，不跟随 Camera2D 的世界变换。UIRoot、纯布局节点和提示文字不拦截鼠标，有交互的参数面板、滚动区域和按钮正常处理输入。

F1 和顶部“隐藏界面”按钮切换整个 `UIRoot.Visible`，包含操作提示、参数和统计。`MainController._Input()` 接收全局 `toggle_ui` 动作，即使输入框有焦点也能隐藏/恢复 HUD，隐藏前释放控件焦点。相机在 `_UnhandledInput()` 中处理滚轮和 `reset_camera`（Home），让 UI 优先处理输入；参数面板关闭 `MouseForcePassScrollEvents`，滚轮不会穿透到相机。快捷键映射位于 `project.godot`。

空格映射到 `toggle_pause`，由 `MainController._Input()` 在 GUI 处理前拦截。仅首次按下调用现有 `ToggleRunning()`；长按重复事件与松开事件也标记为已处理，避免触发有焦点控件的 `ui_accept`、改变复选框或在输入框插入空格。HUD 隐藏时同样有效，暂停按钮和状态文字同步刷新。这只切换模拟的运行状态，不暂停整个 SceneTree，所以相机、选择与 UI 继续响应。

操作指引正文分别保存在 `scenes/worlds/world_2d.tres` 和 `world_3d.tres` 的 `Instructions` 中，由主控制器调用 `HelpOverlay.SetInstructions()` 填入 RichTextLabel；`scenes/ui/help_overlay.tscn` 只定义弹层布局。`HelpOverlay` 与 `UIRoot` 并列，所以 F1 隐藏 HUD 后仍可由 F2 打开。F2（`toggle_help`）、Esc（`close_help`）与空格都由主控制器处理；弹层不会自动修改 `IsRunning`。弹层最大为 760 × 680，随可用视口缩小，正文在内部滚动，不创建原生 Window。

`HelpOverlay` 负责显示、关闭、居中及遮罩点击。遮罩消费按下和松开，松开后再关闭，避免关闭点击穿透；点击卡片不会关闭。`MainController.OnHelpOpenChanged()` 暂时禁用 HUD 的递归键盘焦点，并通过 `ISimulationWorld.SetInteractionEnabled(false)` 禁用世界交互。2D 世界把该请求交给相机，取消未结束的点击/拖动并关闭手势处理，仍记录原始光标位置供恢复使用，物理更新照常执行。关闭后恢复世界输入和仍可见的原控件焦点。3D 世界把同一交互开关交给 SimulationCamera3D 与 ObstacleEditor3D，取消选择、相机拖动、障碍拖动与放置；弹层无需依赖具体相机类型。

相机只在 `_UnhandledInput()` 收到左键按下时开始一个待定手势，确保参数控件优先处理点击。通过 `WorldPressStarted(viewportPosition)` 让管理器记录按下时命中的个体；累计位移不足 5 个视口像素时不移动相机，越过阈值后按完整累计位移开始拖动。`_Input()` 接收后续移动与松开，跨入 UI 后也能结束拖动。未拖动且在世界内松开时发出 `WorldPressEnded(true)`，其余情况发出 false。失焦、丢失左键按住状态、Home、滚轮缩放或视口变化会取消待定手势，避免误选择或粘住相机。

隐藏 UI 不会暂停或重置模拟。相机不直接操作个体或 `BoidConfig`，通过 `ViewChanged` 让管理器同步可见范围；UI 可见性与相机视角不写入配置文件。相机自己订阅和解除视口尺寸、窗口失焦事件；管理器负责订阅和解除相机视角事件。

## 初始化与更新顺序

主控制器先创建、加载共享配置，再实例化世界并调用 `Initialize(config)`，最后把世界加入场景树。管理器在 `_Ready()` 中取得个体容器、背景和相机，绑定配置与相机事件，先适配参考视角并同步可见边界，再执行 Reset，确保个体生成在实际视野中。世界需要宿主先初始化；当前没有独立运行时的默认配置或自动开跑行为。

每个固定物理时间步的调用顺序为：

```text
Godot → BoidManager._PhysicsProcess(delta)
		   └── 运行请求开启时 → Step(delta)
								  ├── 同步障碍查询，NeighborSearch.Search 复制状态并搜索邻居
								  ├── 读取领队步前快照，确定本步共用的身后跟随区域
								  ├── 优先避障，否则领队到达目标 / 普通个体的群集与跟随合力，观察模式再施加软边界
								  ├── 遍历已注册个体
								  │     ├── Boid2D.UpdateBoid(delta, steering)：移动与朝向积分
								  │     └── ResolveMotion：按模式处理接触与边界；无障碍物时仅执行必要的环绕
								  └── 记录数量、搜索/更新耗时、候选次数与平均邻居数
```

`Step()` 在个体循环前按 WrapBounds 规范化位置，同步本步障碍查询，再调用 `NeighborSearch2D.Search(..., wrapBounds)`。两种搜索都读取同一时间步开始时的位置、速度副本，不访问已经更新过的节点状态。随后为全部个体计算优先避障或群集转向，写入复用的 `Vector2[]`；全部算完后再逐个积分、按模式进行障碍物接触修正与环绕。个体增删后按实际数量调整数组，每步覆盖所有条目，关闭规则不会遗留旧转向。

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

观察模式由 `ViewportBoundaryEnabled` 选择软边界或环绕，默认关闭；领队身份优先，存在时停用两类边界。`SetLeader` 在有/无领队的切换上清除邻居统计，返回环绕时立即规范化位置，返回软边界时保留位置。转移身份不经过中间观察模式。关闭软边界立即恢复环绕；切换开关不重置群体、速度、朝向或运行状态。`BoundarySteering2D` 的缓冲带位于扩展边界内侧，各轴靠近两端的 20% 区域逐渐增强回归，越界后保持最大回归请求，速度响应为 0.5 秒；群集同轴向外分量不能抵消回归请求，避障优先，合力限幅。详见[二维视口软边界](viewport-boundary-2d.md)。

`BoidManager.Leader` 保存唯一领队引用，`SelectedBoid` 仍只表示普通选择。`SetSelectedBoidLeader(bool)` 是 UI 请求入口，`SetLeader(Boid2D?)` 验证个体属于本世界、转移标记并清空旧目标。移除领队、Reset 或解除身份时清空引用和目标，不能只记录可能在重置后复用的 ID。`Boid2D.IsLeader` 管理持久的领队身份，`SimulationView` 绘制单一身份圆环；选中领队时本体及同一圆环使用选中领队色并加强轮廓，不叠加普通选择环。目标准星在贴近领队时隐藏，远处仍显示。

`SimulationCamera2D.ObservePointer()` 保存最近鼠标事件的视口坐标；障碍物编辑器消费手势前也更新这个位置。`BoidManager._Process()` 在 GUI 处理后用最终相机变换换算鼠标世界坐标，调用 `SetLeaderTarget`；静止光标也能在相机移动后得到正确目标。UI 悬停、帮助、障碍物编辑、离开窗口或失焦时保留最后目标，暂停时仍可更新标记。此回调只采样输入，运动依然只由 `_PhysicsProcess` 驱动。UI 通过原有接口和事件同步角色，目标存在状态未变时不反复刷新控件。

`Step()` 先取得所有步前快照，再统一读取领队位置、速度和身后区域。领队速度大于 1 世界像素/秒时更新方向，停止时保留最后方向。区域半径 `r = 0.65 × SeparationRadius × sqrt(跟随数量)`（不超过一名跟随者时为 0）；中心为 `leaderPosition - heading × (LeaderFollowDistance + r)`。后方距离表示到区域前缘的间隔，区域面积随数量增加，不分配固定队形位置。

`LeaderSteering.Arrive()` 以直接世界位移计算到达转向：位置误差按 1 秒转换为追赶速度，加上目标速度，再按 0.25 秒响应计算加速度并限幅。领队追踪鼠标，接近 2 像素的静止目标后制动；没有目标时减速等待，不受三条群集规则牵引。`Follow()` 对区域外个体取最近区域边缘作为目标，区域内只匹配领队速度，消除共用目标点的持续挤压力。避障仍优先。

身份与目标不写入配置，后方距离和跟随权重仍由 BoidConfig 管理。跟随者通过 `BoidSteering.Calculate(..., guidance, protectSeparation)` 合成群集与跟随转向：局部距离从分离半径缩小到半径一半时，逐渐削减其他引导中抵消分离的分量，保留切向分量，然后加分离力并限制总加速度。仅启用领队跟随时保护，关闭分离或权重为零时不保护。没有固定编队或个体间硬碰撞保证。

`LeaderSteering.Follow()` 在加权前用 `weight × dt` 作为到达计算的有效步长，再乘跟随权重；这使高权重时的低速制动也不会跨过目标速度而反向。与群集规则合成之后，管理器仍对总加速度限幅。

## 障碍物编辑与避障

障碍物采用独立 `Obstacle2D` 节点，保存形状与尺寸，直接在世界坐标下绘制矩形或圆形。缩放改变几何尺寸，不通过 Node2D.Scale 放大描边或鼠标容差；选中边框与缩放手柄按相机倍率补偿为稳定的屏幕尺寸。障碍物位置独立于相机，相机变化也不修改障碍物几何。

`ObstaclePanel` 只发送创建、删除、尺寸修改与取消放置请求。`Refresh(ObstacleSelection?, ObstacleShape?)` 接收选中对象的形状、宽高深与当前放置状态；用 `SetValueNoSignal()` 回填数值，避免拖动时的状态同步反过来触发编辑。圆形/球体显示直径，矩形显示宽度和高度，长方体增加深度；无选择或正在放置时隐藏尺寸与删除控件。自定义障碍隐藏尺寸控件，但仍可删除。尺寸范围来自 `ObstacleSize` 的统一常量，避障参数范围与持久化由 `BoidConfig` 管理。

添加按钮进入放置模式，下次世界点击确定位置并选中。障碍物本体拖动、缩放手柄及 Shift + 滚轮缩放优先于相机输入；普通空白拖动和滚轮保留原有相机操作。障碍物选择与 Boid 选择互斥。Delete 删除选中障碍物，但编辑输入框时由 GUI 处理；Esc 或右键取消放置。操作说明打开时取消待定操作并禁用世界交互，模拟运行状态不受影响。

`ObstacleAvoidance` / `ObstacleAvoidance3D` 只消费统一圆/球查询结果：前方受阻或近表面需要脱离时才展开按偏角排列的缓存方向；不再识别 Rectangle、Circle、Box、Sphere。障碍物检测与两种 Boid 邻居搜索独立。需要避让时优先采用避障转向，其他时间沿用三条群集规则。`AvoidanceEnabled` 默认开启，`AvoidanceLookAhead` 默认为 0.75 秒，`ObstacleClearance` 默认为 12 长度单位（二维世界像素、三维世界单位），旧配置没有这些键时保留默认值。障碍物布局、选中对象与放置状态不保存；Reset 保留布局，只重建鸟群。

二维管理器只给鼠标领队传入 `LeaderTarget`。策略用目标距离限制远期前瞻，同时保留当前速度的制动检测；危险或近表面时优先检查精确目标方向，畅通则沿用到达转向，制动范围内有障碍则先刹车，目标方向受阻才展开相对目标的候选。近表面缓冲范围不等于实际碰撞半径，可通行窄缝和已到达目标不会因此触发横向驱赶。普通个体与三维策略不受这一目标参数影响。

`ObstacleQuery2D/3D` 持有不激活的静态 PhysicsServer 空间、每个凸部件的静态 body 与局部 Shape 资源（共同映射到逻辑障碍身份）；同步不推进物理。每步开始统一提交障碍变换/资源，暂停新增和拾取也先同步。圆/球查询半径就是 ObstacleClearance，零间距使用 0.001 的数值点近似，未叠加 Boid 网格半径。盒角现为圆/球扫掠的真实圆角。`ObstacleContact2D/3D` 独立扫描完整位移、投影速度和剩余位移以沿面滑动；初始重叠用真实重叠查询及原生接触法线恢复。接触修正不受 MaxForce 限制。二维关闭避障仍同时关闭主动转向与接触。

`ObstacleBoundary2D` 仅在环绕模式截断第一次边界前的预测，并把实际位移按出口/入口拆段。`ObstacleQuery2D.Synchronize` 在环绕模式只注册变换后碰撞部件包围范围与边界相交的障碍；软边界、领队无限模式和即时新增检查全部障碍。三维无视野/观察盒过滤。障碍不传送、不生成周期副本。运动最多 64 段、重叠恢复最多 8 轮；耗尽时丢弃未完成位移或报告未恢复，不能把封闭布局当成成功。预算、方向数与响应时间集中在 `ObstacleTuning`。详细同步和资源接入见[统一避障架构](obstacle-query-refactor.md)。

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

上述角运动积分由 `AngularMotionLimitsEnabled` 统一控制，默认开启。关闭时角速度和角加速度状态清零，外观直接对齐有效速度；`VelocityFacing` 以 0.01/0.02 世界单位/秒停稳/恢复阈值作滞回，近零速度不归一化。管理器在配置变更时调用个体的 `RefreshFacingConfiguration`，暂停也可立即切换朝向但不推进位置；接触修正后再按最终速度对齐。再次开启清空旧角运动历史并恢复原上限。详见[角运动与直接朝向](angular-motion.md)。

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

界面的「目标个体数量」通过 Enter 或失焦提交给 `ISimulationWorld.ResizePopulation()`，两个管理器立即补齐或缩减差额，保持已有状态；数字草稿不会触发增删。种子仍只在 Reset 生效。`InitializePopulation()` 使用局部随机生成器复现重置结果；即时增量使用另一份随机生成器，Reset 时以种子异或固定常量初始化，之后连续使用，不受未重置的种子编辑影响。

缩减从列表末尾移除，优先保留领队，其次是普通选择。新增复用 `SpawnBoid()`，在观察范围或无限世界的群体质心附近采样，用统一障碍查询的 `Occupied(position, ObstacleClearance)` 过滤。每个待新增个体最多尝试 64 次，首次耗尽后结束本次增量并设置 `PopulationSpawnLimited`。UI 保留请求数，显示实际数并允许 Enter 重试。三维批量操作结束后统一刷新调试与材质；两种世界均使旧搜索和统计失效。细节与验收见 [数量即时增减](population-resizing.md)。

管理器对外提供 `IReadOnlyList<Boid2D> Boids`，它包装同一个内部列表，会反映创建和移除。这个接口只限制集合增删入口；其中的 Boid 仍然是实际节点对象。调试绘制可以读取节点状态，正常运动更新由管理器调度个体完成。

二维配置持久化到 `user://boids_lab.cfg`，三维配置到 `user://boids_lab_3d.cfg`，包括对应的可视化开关，不包含个体生成结果、运行请求或选择状态；主题偏好独立保存到 `user://boids_lab_ui.cfg`。

## 单选与调试绘制

`BoidManager.SelectedBoid` 保存唯一选择；按下时命中测试在实际 Polygon2D 的局部坐标中进行，同时提供 10 个视口像素的中心容差。多个命中对象取屏幕距离最近者，完全重叠时优先当前选中个体。松开确认的是按下时记录的实例，避免运行中的个体移走而选不中。再次点击选中对象或点击空白会取消，拖动及 UI 点击保持选择。Reset 和 RemoveBoid 清理选择，已移除的待定点击对象不会再被选中。

`Boid2D.SetSelected()` 只改变选择状态并刷新 Body 颜色，不改变运动状态；颜色取自当前主题，领队颜色优先于选择颜色，取消选择后恢复对应身份颜色。`SimulationView` 额外绘制选择圆环。高亮不依赖任何可视化开关；UI 隐藏后仍属于世界的一部分。

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
| `ObstacleAvoidance.cs` / `ObstacleAvoidance3D.cs` | 前方体积探测、候选方向采样、受限转向；只读取查询结果 |
| `ObstacleQuery2D.cs` / `ObstacleQuery3D.cs` | 静态物理空间、形状同步、占用/扫掠/接触、诊断与 RID 释放 |
| `ObstacleContact2D.cs` / `ObstacleContact3D.cs` | 独立的完整位移阻挡、滑动和有界重叠恢复 |
| `ObstacleEditor2D.cs` | 障碍物列表与选择、放置预览、拖动/缩放/删除输入；不推进 Boid 模拟 |
| `HelpOverlay.cs` / `help_overlay.tscn` | 窗口内操作说明弹层、响应式布局、滚动正文和关闭行为；不修改模拟或配置 |
| `StatsPanel.cs` | 展示实际测量结果，null 统一显示 `--` |
| `main.tscn` | Game、World 容器及 CanvasLayer HUD |
| `boids_world_2d.tscn` | 管理器根节点、个体与障碍物容器、背景/调试节点和 Camera2D |
| `boid_2d.tscn` | 单个 Boid 的场景模板，子 Polygon2D 定义三角形外观 |
| `lab_theme.tres` | 编辑器预览的静态 Theme / StyleBoxFlat；运行时由 LabTheme 按配色资源生成主题 |

## 控制与统计语义

- `Initialize(config)`：接收共享配置，验证参考尺寸。已就绪的世界会复位参考视角、同步可见边界并 Reset。
- `SetRunning(running)`：开启或暂停固定物理时间步的运动更新。
- `SetInteractionEnabled(enabled)`：世界就绪后启用/禁用相机和选择输入，取消待定手势；不改变模拟运行状态。
- `Reset()`：暂停、移除旧个体，ID 归零，按配置重新生成；加速度、角速度和计时清零或恢复未测量。
- `SpawnBoid(position, velocity)`：世界就绪后创建并注册一个个体，分配递增 ID，更新真实数量。
- `RemoveBoid(boid)`：移除已注册个体，保留其余个体的 ID 和状态，更新真实数量。
- `ResizePopulation(requestedCount)`：将请求限制到 0～5000，稳定地增删现有群体，保留运行状态与存活个体；报告新增是否因有限采样未完成。
- `Step(deltaSeconds)`：明确推进一个时间步，先搜索邻居并计算全部转向，再调用个体运动积分并修正障碍物接触。内部使用或测试可直接调用，运行回调由 IsRunning 控制。
- `StepOnce()`：仅暂停时调用原有 `Step(1.0 / Engine.PhysicsTicksPerSecond)`，完成后保持暂停；`CanStep = !IsRunning`，运行时拒绝单步。MainController 刷新统计和状态，帮助、世界切换及三维鼠标捕获期间拒绝 UI 单步请求，不增加额外更新路径。详见 [单步控制](single-step.md)。
- 当前两个世界均提供可运行的模拟；可选功能由 `Features` 声明。已移除早期的 CoreImplemented 开关及未实现占位层，运行状态直接显示为已暂停 / 运行中。

实际数量来自已注册集合，包括空群体的 0。Stopwatch 分别测量搜索耗时与整步耗时：前者包含快照、格子构建、候选距离检查与结果写入；后者还包含三条规则计算、个体运动积分与障碍物接触，不包含渲染。候选检查次数按有向距离检查计数，排除自身，不计格子查找，不把规则中的邻居遍历重复算作搜索检查；平均邻居数为有向邻居总数除以实际数量，空群体测量后为 0。界面显示最近一次物理步的数值，并非 0.25 秒内的累加或平均，FPS 独立来自引擎实测。

首次运行前、Reset、增删个体或邻域结果失效时，计时与邻居指标显示 `--`。暂停保留最近读数。二维相机变化在无领队时同步边界并使邻域结果失效，有领队时只改变观察；三维观察相机的旋转、平移和缩放均不修改模拟范围或统计，三维观察盒尺寸变化才使结果失效。

## 三维基础世界与共享 UI

`scenes/main_3d.tscn` 继承 `main.tscn`，只将 `StartIn3D` 设为 true。默认 F5 进入 2D，3D 入口可用 F6 运行；两者均可从顶部选择器切换世界。切换提交当前数值编辑、保存参数、解除旧事件订阅并释放旧世界，再创建目标世界与控件。两套配置分别缓存与保存；目标世界重新生成并暂停，HUD 显隐保留，帮助关闭，选择/领队/障碍物/相机与运动状态清除。详见 [世界切换说明](world-switching.md)。进入 3D 时结构为：

```text
Game3D (MainController)
├── World (Node)
│   └── BoidsWorld3D (Node3D / BoidManager3D)
│       ├── Boids (Node3D)
│       │   └── Boid_0 ... (Boid3D)
│       │       └── Body (MeshInstance3D / CylinderMesh 六棱锥)
│       ├── SimulationView (Node3D / SimulationView3D)
│       │   ├── ReferenceFrame (MeshInstance3D / ImmediateMesh 盒状边框)
│       │   ├── Grid (MeshInstance3D / 可切换底面网格)
│       │   ├── DebugOverlay (MeshInstance3D / 合批调试线段)
│       │   ├── Camera3D (SimulationCamera3D，环绕、平移、缩放与复位)
│       │   ├── WorldEnvironment
│       │   └── Sun (DirectionalLight3D)
│       ├── Obstacles (Node3D / 障碍物实例)
│       ├── ObstacleEditor (Node3D / ObstacleEditor3D)
│       └── LeaderController (Node / LeaderController3D)
└── UI (CanvasLayer)
	├── UIRoot (共用 HUD)
	└── HelpOverlay (共用弹层，三维专用正文)
```

`BoidManager3D` 实现 `ISimulationWorld`，统一管理实例、领队身份、软边界、固定物理步和计时。个体的 ID 在一次 Reset 内唯一；创建和移除在更新遍历之外执行。初始与重置后暂停，数量提交后立即增减，种子在重置后生效。重置保留障碍布局。

每个固定物理步的顺序为：

```text
Godot → BoidManager3D._PhysicsProcess(delta)
		└── IsRunning → Step(delta)
					   ├── 同步障碍查询，NeighborSearch3D 复制实际坐标快照并执行所选搜索
					   ├── 从共同快照计算领队控制 / 跟随与群集 / 软边界，普通个体避障优先
					   ├── 遍历个体
					   │   ├── Boid3D.UpdateBoid(delta, steeringAcceleration)
					   │   │   ├── 合并外部 / 当步转向加速度，限制模长
					   │   │   ├── 更新速度并限速，再按新速度更新 Position
					   │   │   └── 计算方向误差，积分角速度并更新 Quaternion
					   │   └── 在真实坐标扫描接触并滑动，不环绕或钳制到盒内
					   ├── 发布真实搜索与整步统计
					   └── 从更新后的位置重建调试线段（不计入模拟耗时）
```

`Boid3D` 保存 Node3D 的 Position / Quaternion 以及线性、角运动状态，没有自身物理回调。普通个体和领队均按实际速度方向转向；线性速度/加速度始终限幅，角速度/角加速度由共享开关控制。关闭角运动限制后用最短旋转直接对齐速度，显示只插值位置、旋转使用当前朝向；切换时刷新旋转历史并保留显示位置。相机目标视角只提供 WASD 的移动参考，不直接驱动领队朝向。网格尖端朝本地 -Z，不使用刚体或重力。

### 实际坐标与软边界

`BoidConfig` 新增 WorldWidth、WorldHeight、WorldDepth，默认 2560×1440×1440，范围 100～100000，步长 10。管理器的 WorldBounds 保存观察盒尺寸与运行时中心；WorldSize 从配置读取。初始化中心为原点，改变尺寸保持中心和所有位置，不重置相机。视角从不回写模拟范围。底面网格与边框立即重建，Home 根据最新盒重新取景。

`BoundarySteering3D.Apply` 在观察盒内侧、各轴靠近两端的 20% 缓冲带中平滑增强向内目标速度，响应时间 0.5 秒；外沿与越界时覆盖同轴向外群集输入，合力再限幅。普通个体优先避障；边界只改变转向，不直接修正位置。最大加速度为零时无法主动返回。

`ShowBoundary` 默认关闭，保存于三维配置的 `visualization/show_boundary_3d`。三维「显示边界」控件只控制 ReferenceFrame 的可见性，暂停时也立即生效；ShowGrid 独立控制底面网格。存在领队时两者始终隐藏，退出后按各自开关恢复，不影响边界转向计算。

`NeighborSearch3D.Search(boids, radius, mode)` 不再接收边界，直接复制真实位置。朴素搜索检查全部有向配对；稀疏网格按 floor(position/radius) 分格并查询相邻 27 格，不取模。两者按快照下标排序，保证规则累加顺序一致。详见 [三维搜索说明](neighbor-search-3d.md)。

`WorldGeometry3D` 仅保留精确半径判断。调试球、邻居线和速度箭头都在实际坐标生成，不作周期复制或盒内裁剪；独立查询当前选中个体，不覆盖物理快照及计时。相机按对象深度换算透视模式的拾取容差与箭头屏幕尺寸。

### 领队、无限世界与群集

`SetLeader` 验证对象属于本世界并管理唯一身份；普通选择与领队相互独立。四种外观分别为普通橙、选中金、领队紫、选中领队粉，使用独立 MaterialOverride，不修改共享材质。

存在领队即停用软边界，隐藏边框和底面网格。全体继续使用直接距离和无界网格。取消或删除领队后，观察盒中心取现有群体质心；空群体取原领队位置。所有个体和障碍保持原位置。重置从领队模式退出后在该盒内按种子重新生成并暂停。

`LeaderSteering3D` 使用共同步前快照和领队实际朝向计算身后球形区域：多人半径为 0.8×分离半径×人数立方根，单人半径为零，前缘与领队相距 LeaderFollowDistance。区域外到达、区域内匹配领队速度，不吸向共用球心。

`BoidSteering3D` 保留全部可调群集规则。跟随时以领队步前速度为参考计算局部速度差，避免反复抵消共同前进速度；拥挤时削减抵消分离方向的跟随、对齐与聚集分量，保留切向运动。跟随权重为零时恢复普通群集计算。领队不执行普通群集或主动避障，只执行控制加速度和接触约束。

### 相机、输入和 UI 接口

`LeaderController3D` 保存有效 WASD 和鼠标目标偏航/俯仰，俯仰限制 ±85°。它不积分个体，管理器每个固定步读取归一化的移动方向，以最大速度为目标速率，0.25 秒响应并限制加速度。无输入或释放后目标速度归零，低速时停止残余漂移。

`Boid3D` 根节点继续保存真实物理位姿；每步记录前一位姿与接触修正后的当前位姿。`SimulationView3D` 以优先级 -10 在显示帧读取 Engine.GetPhysicsInterpolationFraction，只给 Body 子节点补偿插值位姿，不把插值写回模拟。相机随后读取 DisplayGlobalTransform；拾取和调试端点使用相同显示位姿，邻居成员仍按物理坐标判断。暂停、单步、新生、初始化和领队切换清理历史；仅三维生效。细节及 30～144 FPS 回归见[显示与相机插值](follow-camera-jitter.md)。

`SimulationCamera3D` 保留自由正交观察；领队模式为 70° 透视跟随，距离默认 300、范围 80～2000，可滚轮或控件保存。切换立即定位，此后用时间相关的指数平滑跟随。Home 在领队模式只恢复默认距离；返回观察时保留观察角度与缩放，并移至当前群体。

`ISimulationWorld` 新增 IsLeaderControlActive、SetLeaderControlActive 和 ConfigurationEdited 事件。后者让世界中的滚轮视距修改经宿主保存并同步控件。SimulationFeatures.DirectLeaderControl 只由三维声明；二维的接口实现不可操控，不改变原有边界和领队规则。共享 UI 不接触 Vector3 或具体相机。

界面指定或转移三维领队后，MainController 自动调用操控入口；直接调用管理器的 SetLeader 只改变身份与视角，不主动捕获系统鼠标。进入操控前提交编辑内容并释放控件焦点、取消编辑手势。捕获时鼠标输入不穿透 GUI，WASD 不响应文本编辑。Esc、F1/F2、失焦、领队移除、重置及切换世界均释放并清空输入，不自动恢复；界面转移身份时清空旧输入再操控新领队。按钮 / F3 用于释放后重新进入操控。空格仍全局优先，暂停时保留捕获与目标视角调整，不推进个体状态。

释放后的透视跟随允许右键拖动，通过 LeaderController3D.RotateView 调整同一目标偏航 / 俯仰；即使模拟暂停，相机仍平滑更新。普通鼠标悬停不转向，GUI 与帮助继续隔离背景手势。重新操控时沿用调整后的视角作为 WASD 参考，不改变领队的速度朝向规则。正交观察的右键旋转和中键平移保持原样。

相机区分 ViewChanged 和 ManualViewChanged：自动跟随只更新画面，手动操作取消未完成编辑手势。障碍拖动和放置固定编辑平面并冻结跟随，结束后恢复；坐标不限制在观察盒内。普通个体开启避障时优先前瞻转向并约束完整运动线段；领队始终接触阻挡、滑动，不受主动避障开关影响。详见 [三维避障说明](obstacles-3d.md)。

两套 BoidConfig 独立实例与文件保存。三维尺寸和跟随视距持久化，中心、身份、捕获和相机角度不持久化。统计仍报告真实数量、最近物理步的搜索/更新耗时和候选数；调试与相机刷新不产生虚假测量。

`SimulationView3D` 管理跟随视角的近距离淡化：启用时复制并复用一份普通材质，使用 PixelAlpha 在距相机 120～240 世界单位之间渐变。`Boid3D` 的领队和选择外观优先于视图材质；退出跟随或关闭开关后恢复原材质。`FadeNearbyBoids` 默认开启，保存到 `visualization/fade_nearby_boids_3d`，只在三维界面提供开关。显示不改变运动、邻居搜索、拾取或相机距离；新建、转移、取消身份与重置时均刷新。实现选择与图形复现见 [相机淡化说明](camera-fade-3d.md)。

## 参数面板分类

`ControlPanel` 保留统一事件与配置绑定，内部使用固定操作栏、分类按钮和五个独立 `ScrollContainer`；隐藏页不参与布局或键盘遍历。每个世界重新 Bind 时回到基础页，日常切换分类保留各页滚动位置。`SelectCategory()` 在隐藏旧页前提交草稿并释放旧页焦点，`RevealControl()` 切换到控件所属页，等待布局后滚动到目标。外部不再依赖面板父节点是 ScrollContainer。

`ObstaclePanel` 按选择 / 放置状态显示尺寸和操作按钮，不变更障碍编辑事件或碰撞逻辑。界面分组与验证见[界面说明](ui-layout.md)。

## 明暗主题与外观偏好

`LabPalette : Resource` 导出分组颜色属性；明 / 暗资源为 `themes/bingyan_light.tres` 和 `bingyan_dark.tres`，由主场景的 LightPalette / DarkPalette 槽引用。`LabTheme` 生成原生控件样式。`MainController` 缓存两套 Theme，向 HUD、帮助及当前 SimulationView 应用同一配色；动态控件继承语义角色。`AppearancePreferences` 独立保存到 `user://boids_lab_ui.cfg`，不混入两套 BoidConfig。

资源颜色 setter 发出 Changed；MainController 监听并重建主题缓存，支持 Remote Inspector 即时调色。二维更新绘制颜色；三维两种主题均使用世界内共享的四种无光照身份材质，保持源场景资源不变。明色障碍使用无光照杏橙填色与轮廓，避免灯光导致灰褐色。切换主题或修改资源时重建参考几何与淡化材质缓存，不重新配置相机或修改运动状态；新生个体继承当前主题。障碍、预览、调试颜色同步更新。详见[主题说明](themes.md)。

## 开发状态与范围

本轮约定的三维观察与领队、近距离淡化、二维/三维单步控制及数量即时增减均已实现。剩余图形效果与真实键盘操控由维护者验收，见[实机验收清单](manual-acceptance.md)。

邻居搜索使用朴素遍历或均匀网格；障碍检测统一使用 Godot 静态查询空间。未引入 Compute Shader、逐个体物理节点、ECS、多线程或对象池，后续优化以测量结果为依据。

## 验证边界

构建与无图形回归、测试覆盖、性能记录和图形验收边界统一见[验证记录](3d-implementation-validation.md)。待确认的键鼠与视觉项目见[实机验收清单](manual-acceptance.md)，较早环境证据见[历次验证记录](validation-history.md)。
