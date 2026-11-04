# 统一避障查询与资源接入

二维和三维共用「障碍表示 → 原生查询 → 主动策略 → 接触约束」的职责划分。模式与管理器关系见[架构说明](architecture.md)，回归结果、性能测量和复现命令统一见[验证记录](3d-implementation-validation.md)。

## 职责与同步

| 入口 | 职责 |
| --- | --- |
| `Obstacle2D/3D`、`ObstaclePart2D/3D` | 菜单形状、实心轮廓/凸体资源、外观和逻辑身份 |
| `ObstacleQuery2D/3D` | 静态空间、资源与变换快照、占用、扫掠、重叠和拾取 |
| `ObstacleAvoidance`、`ObstacleAvoidance3D` | 前向体积探测、缓存候选方向、目标速度与受限加速度 |
| `ObstacleContact2D/3D` | 完整位移阻挡、沿面滑动、初始重叠恢复与失败报告 |
| `ObstacleBoundary2D` | 首次边界前瞻截断、实际运动出口/入口拆段 |
| 两个 `BoidManager` | 同步障碍 → 步前快照 → 全体转向 → 积分 → 接触 → 显示提交 |

查询使用 PhysicsServer 创建的独立空间，**不激活、不执行物理 Step**。每个凸部件对应一个轻量静态 body，RID 映射到逻辑障碍 ID 和部件下标。专用碰撞层为第 8 层（`1u << 7`），只注册确认后的障碍，排除预览、Boid、UI 和调试几何。整体选择、移动和删除仍操作一个逻辑障碍。

编辑器修改几何后立即同步；每次模拟 Step、即时新增和拾取事务开始时也同步，支持暂停编辑后立即查询。同一步内所有个体读取同一快照。`Synchronize` 合成完整父变换与部件局部变换，再转换到指定模拟坐标系；管理器传入 Boids 容器的全局变换，独立查询默认使用世界坐标。

二维观察模式只注册变换后部件包围范围与可见边界相交的障碍；无限模式和即时新增检查全部障碍。前瞻在首次边界截断，实际运动分出口和入口扫掠，不生成障碍周期副本。三维使用实际坐标，不按观察盒或相机过滤。

删除或离开场景树立即注销相关 RID；编辑器退出时释放所有 body、空间与查询资源。Reset 保留布局，切换世界释放旧空间。

## 查询与运动

查询圆/球半径就是 `ObstacleClearance`，不叠加外观半径；零间距以 0.001 单位半径作数值近似。盒角遵循圆/球扫掠的圆角几何。

`CastMotion` 返回安全/不安全比例，并忽略初始重叠。接触层先恢复重叠，再扫掠完整位移；对命中区间作有界细化，在命中处用静态 `GetRestInfo` 读取原生法线。逐个排除部件枚举接触，并按朝内程度、法线坐标、逻辑 ID 和部件下标固定并列选择。浅接触不能将接触点对相减后归一化，否则浮点噪声会造成切向漂移。

初始重叠由 `CollideShape` 检测，恢复深度由原生表面点沿法线的投影加圆/球半径得到。最多处理 64 段运动、8 轮恢复和每次 32 个接触；预算耗尽时舍弃剩余位移，恢复失败时停在本次恢复起点并停止速度。封闭、过窄或复杂重叠布局没有脱困保证，未引入全局寻路或 Boid 间硬碰撞。

主动策略缓存二维 32 个交替偏转方向、三维 64 个按偏角排列的球面方向。前方危险时才展开候选，找到畅通方向即停止。高速以速度方向为前向，低速使用实际朝向；二维 +X、三维 -Z 约定保持。前瞻距离为 `速率 × 前瞻时间 + 速率² / (2 × 最大加速度)`；全部受阻时同时按刹车距离及前瞻/响应期距离限速。目标速度差除以 0.25 秒响应时间，再按 MaxForce 限幅；MaxForce 为零时主动转向为零，接触约束独立执行。

二维鼠标领队额外向策略传入目标，探测长度限制为原前瞻与 `max(目标距离, 制动距离)` 的较小值，避免目标后方障碍迫使领队绕远。发生危险或近表面接触时，先尝试有效前瞻内的精确目标方向；这段方向可通行时继续原到达转向，当前动量会在制动距离内碰撞时先刹车。目标方向确实受阻才按相对目标方向的偏角搜索候选。实际间距的重叠仍优先恢复；较大的近表面缓冲带不再将可通行窄缝当成禁行区，也不会让已停在目标的领队持续横移。普通个体和三维策略保持原有探测方式。

避障优先于普通群集、跟随和三维软边界。二维关闭避障同时关闭转向与接触；三维普通个体沿用该开关，玩家领队不主动绕障但始终执行接触阻挡。即时新增始终检查确认障碍，详细规则见[数量调整](population-resizing.md)。

参数与预算集中在 `ObstacleTuning`。管理器的 `UnresolvedContacts`、`ExhaustedContactBudgets` 和查询的 `ContactsSaturated` 保留失败信息；`QueryCount` 统计最近一次同步后的原生查询次数，不混入邻居候选数。查询资源、排除集合和方向缓存复用；Godot 返回的结果数组仍会分配。

策略参考固定版本 [SebLague Boid.cs](https://github.com/SebLague/Boids/blob/fdda4db1368df92fbacd8a644bbe6dc61d705441/Assets/Scripts/Boid.cs) 的体积探测和 [BoidHelper.cs](https://github.com/SebLague/Boids/blob/fdda4db1368df92fbacd8a644bbe6dc61d705441/Assets/Scripts/BoidHelper.cs) 的方向分布；查询 API 见 Godot [二维](https://docs.godotengine.org/en/stable/classes/class_physicsdirectspacestate2d.html) / [三维](https://docs.godotengine.org/en/stable/classes/class_physicsdirectspacestate3d.html) 文档，实际行为由本机集成检查验证。

## 不规则障碍接入

### 二维实心轮廓

示例：`scenes/obstacles/l_shape_2d.tscn`。根节点挂载 `Obstacle2D.cs`，`SolidPolygon` 为不自交的实心轮廓，由 `Geometry2D.DecomposePolygonInConvex` 分解成凸部件。外观绘制同一轮廓，保留 L 形凹口。

可直接保存为世界 `Obstacles` 容器的子节点，初始化时自动注册；运行时接入：

```csharp
var obstacle = GD.Load<PackedScene>("res://scenes/obstacles/l_shape_2d.tscn")
    .Instantiate<Obstacle2D>();
obstacle.Position = new Vector2(500, 350); // Obstacles 容器局部坐标
world.GetNode<ObstacleEditor2D>("ObstacleEditor").RegisterObstacle(obstacle);
```

也可手工设置 `CollisionParts`，每个 `ObstaclePart2D` 包含 Shape2D 和局部 Transform2D；CapsuleShape2D 有集成覆盖。轮廓与手工部件只选择一种来源，不支持未经处理的孔洞。ConcavePolygonShape2D 线段壳不能代替实心体。

### 三维凸体与组合凸体

示例：`scenes/obstacles/compound_3d.tscn`，由两个 ConvexPolygonShape3D 部件组成 L 形，其中一个带局部旋转，凹口开放。`Obstacle3D.CollisionParts` 中每个 `ObstaclePart3D` 包含 Shape3D 和局部 Transform3D；CylinderShape3D 也有查询与拾取覆盖。使用 `ObstacleEditor3D.RegisterObstacle(instance)`，或预先放在世界 Obstacles 容器下。

自定义三维障碍显示 Shape3D 生成的碰撞轮廓，可附加 MeshInstance3D 外观并继承主题及选择颜色。艺术网格与碰撞资源独立，网格修改不会自动生成碰撞。当前保证范围为实心凸体及其组合；[ConcavePolygonShape3D 面壳](https://docs.godotengine.org/en/stable/classes/class_concavepolygonshape3d.html) 不具备相同的深层内部检测与重叠恢复保证。

自定义障碍支持整体拾取、拖动和删除；三维重合时优先已选中障碍，其余并列按逻辑身份稳定选择。菜单形状专用尺寸控件和 Shift 滚轮缩放对自定义障碍禁用。修改部件数组、局部变换或轮廓后调用 `editor.SynchronizeQuery()` 更新查询及轮廓；下一次 Step、新增或拾取也会同步。未提供顶点编辑器、模型导入器或自动模型凸分解工具。

## 调试显示

选中 Boid 并开启「显示运动方向」，显示最近一步的前向探测、受阻/畅通候选、最终选向及接触法线。配色依次使用 Velocity、ObstacleOutline / Perception、Selection、Separation；三维线条锚定显示姿态。

只收集选中且开启开关的个体，缓存跨步复用；选择、配置、重置等失效操作清空旧记录。图形检查项目见[实机验收清单](manual-acceptance.md)。
