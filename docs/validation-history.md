# 历次验证记录

本文件归档 README 中的阶段性记录，保留当时的措辞、命令和结果。其中的“当前”“本次”“尚未实现”、测试组数和三维周期边界描述只适用于对应历史阶段，不代表现有代码。当前版本已取消三维环绕。

当前结果见[自动验证与实机验收状态](3d-implementation-validation.md)，使用说明见[项目 README](../README.md)。2026-10-31 整理归档时未重新执行这些历史检查。

## 近期阶段记录（归档）

2026-10-30：C# 构建零警告、零错误；23 组二维/三维检查全部通过。原生窗口已检查观察运行、身份高亮、透视切换、群体跟随、滚轮视距及返回观察模式。新增近距离淡化，通过 Compatibility 渲染检查和 40/200 个跟随者、两种视距、四种运动状态的 16 组画面对比。实际键盘飞行手感仍待人工复核。完整结果及限制见 [三维实施验收记录](3d-implementation-validation.md)与[相机淡化验收](camera-fade-3d.md)。

二维与三维单步控制已实现，复用原有运动更新，保留个体、身份、选择和障碍布局。完整 24 组回归通过，新增 137 项单步检查覆盖 60/120 Hz、两种搜索、按钮/焦点/帮助隔离、领队制动及反复世界切换。详见 [单步控制](single-step.md)。

2026-10-31：数量即时增减已实现，完整 25 组无图形回归通过，包含新增的数量提交、稳定增删、障碍保护和随机复现检查。阶段状态见 [开发计划](next-development-plan.md)，真实键盘操控仍待实测。

## 各阶段验证记录

以下为功能逐次引入时的记录；早期三维周期环绕描述已由当前直接坐标、软边界和领队检查替代。

先构建 C# 项目，再使用 Godot .NET 可执行文件运行：

```text
godot --headless --path . res://tests/world_switch_checks.tscn --quit-after 600
godot --headless --path . res://tests/spatial_3d_checks.tscn --quit-after 600
godot --headless --path . res://tests/steering_3d_checks.tscn --quit-after 600
godot --headless --path . res://tests/observation_3d_checks.tscn --quit-after 600
godot --headless --path . res://tests/motion_3d_checks.tscn --quit-after 600
godot --headless --path . res://tests/motion_checks.tscn --quit-after 120
godot --headless --path . res://tests/neighbor_checks.tscn --quit-after 120
godot --headless --path . res://tests/steering_checks.tscn --quit-after 120
godot --headless --path . res://tests/debug_checks.tscn --quit-after 600
godot --headless --path . res://tests/pause_shortcut_checks.tscn --quit-after 120
godot --headless --path . res://tests/help_overlay_checks.tscn --quit-after 600
godot --headless --path . res://tests/obstacle_3d_checks.tscn --quit-after 600
godot --headless --path . res://tests/obstacle_interaction_3d_checks.tscn --quit-after 600
godot --headless --path . res://tests/obstacle_avoidance_checks.tscn --quit-after 120
godot --headless --path . res://tests/obstacle_interaction_checks.tscn --quit-after 600
godot --headless --path . res://tests/leader_checks.tscn --quit-after 120
godot --headless --path . res://tests/leader_interaction_checks.tscn --quit-after 600
godot --headless --path . res://tests/leader_spacing_checks.tscn --quit-after 600
godot --headless --path . res://tests/boundary_mode_checks.tscn --quit-after 600
```

检查真实节点的匀速移动、加速度积分、60/120 步时间一致性、线性/角速度上限、角加速度上限、朝向收敛与跨 ±180° 转向、零速处理、暂停/重置、种子复现、无限世界运动、相机缩放/拖动后的可见范围、统计和新旧配置兼容。测试使用 `.godot/validation/` 中的临时配置，不覆盖用户设置。退出码 0 表示通过。本次 C# 构建与运动检查均通过；未进行图形窗口测试或 Boids 算法性能测试。

邻居检查以独立的欧氏距离和九幅周期图像的最短距离为参考，验证两种搜索的结果、无重复/无自身、负坐标、百万级坐标、格线与半径边缘、重合位置、随机与密集分布、空群体。还检查步前快照、非连续 ID、两种地图模式下的相机与搜索联动、控件绑定、统计和配置兼容。稀疏场景验证候选次数减少，不把无窗口耗时当作桌面性能基准。

三条规则的无窗口检查覆盖单规则方向、分离距离权重、开关与权重即时生效、视口两侧不误判为近邻、完全重合与对称抵消、无邻居、加速度/速度限制、外部输入叠加、快照更新顺序、配置兼容和两种搜索模式的一致性。每种模式还通过 300 个物理步的群集运行检查，验证状态有限、速度与加速度限幅、轨迹确实改变。这些是逻辑验证，不代表已人工评估图形效果。

调试检查通过真实视口输入验证暂停/运行时单选、再次点击取消、拖动区分、UI 隔离、原色恢复、缩放命中和删除/重置清理；同时验证选中对象的感知范围/邻居线、全体方向、切换/取消选择后的范围清理、视口边缘裁剪与大半径几何，以及绘制不改写模拟统计。采用无窗口验证，未进行原生窗口截图或图形效果验收。

操作说明检查在真实主场景中验证默认隐藏、按钮/F2/Esc/点击外部开关、隐藏 HUD 后打开、焦点隔离、空格暂停/继续、保持模拟运行、背景输入阻断、取消待定手势、说明内部滚动和无窗口视口尺寸适配。弹窗检查及现有空格、选择调试检查均通过，未进行图形窗口视觉验收。

障碍物检查覆盖两种形状的前瞻转向、加速度限幅、高速线段接触、沿表面运动、初始重叠、正负方向的出屏运动、视野外障碍物及配置兼容；每种形状还在真实管理器中连续运行 300 步，检查保持间距且持续绕到一侧。编辑交互通过真实视口输入验证创建、互斥选择、拖动、手柄与数值缩放、滚轮区分、删除、弹窗拦截及重置保留布局。以上检查和既有回归检查均在无窗口模式下通过，未进行图形窗口视觉验收。

领队检查覆盖无邻居时的全局跟随、身后位置、移动领队的速度补偿、目标减速与停止、世界坐标目标方向、同一步快照与列表顺序独立性、避障优先、删除/重置清理和配置兼容。真实主场景检查勾选、转移、颜色恢复、普通选择独立、暂停目标、鼠标连续跟随与 UI/障碍物/弹窗隔离、相机变换和 F1 隐藏后的操作。只进行无窗口验证，未人工验收图形效果。

`leader_spacing_checks` 使用真实管理器检查 40/200 个追随者、默认和高跟随权重下的静止及持续转弯场景，验证近邻间距，并确认群体仍跟随领队而非散离。

`boundary_mode_checks` 检查有无领队切换、转移/删除/重置、暂停与运行状态保持、立即回收视口外个体、视角变化，以及两种搜索和调试连线在重复切换后的结果一致性。

`motion_3d_checks` 验证三维匀速/加速、60/120 步积分一致性、线性与角运动限幅、竖直和 180° 朝向收敛、三个坐标轴的正负越界、动态创建/移除、种子复现、相机包围整个空间，以及实际物理回调中的暂停/继续。真实 3D 主场景还验证共享 UI、独立配置保存、重置、F1/F2、未实现控件隐藏和原生网格朝向。全部通过，并通过 2D 空格、弹层、障碍物交互、领队交互和边界切换回归。仅进行了无图形验证，尚未人工验收三维画面。

`observation_3d_checks` 通过真实主场景输入验证右键旋转、中键平移、滚轮锚点与倍率上下限、Home、文本焦点、UI 与帮助隔离、失焦和丢失按键状态时终止拖动，以及无窗口视口尺寸变化。相机操作不改变模拟盒、个体或模拟统计。

`spatial_3d_checks` 以独立的 27 幅周期图像距离为参照，对照两种搜索在面/边/角落、半径边缘、单轴少量格子、非整除边长、重合位置、随机分布、空群体下的结果；验证快照冻结、统计失效和稀疏分布的候选减少。还检查跨边界线长、感知/分离范围裁剪、实际分离半径、大半径与全域感知，以及调试查询不改写模拟统计。三维观察检查覆盖运行/暂停选择、前后遮挡、取消/转移/删除/重置、独立高亮材质和帮助输入隔离。

`steering_3d_checks` 验证单条规则方向、权重与开关、环绕位移、完全重合、对称抵消、分离距离权重、速度/加速度限制、共同快照及外部输入。朴素与网格驱动相同初始群体连续运行 240 步，中途增删个体并修改半径，逐步对照位置、速度和加速度一致性。上述检查仅验证逻辑，不代表人工图形验收或桌面性能基准。

2026-10-29 环境检查：本机 SDK 为 8.0.400，命令行构建零警告、零错误，世界切换、四组三维检查及八组相关二维检查通过。额外运行无图形编辑器导入时，Godot 报告 `.NET Sdk not found. The required version is '10.0.11'`，随后无法加载 `Microsoft.Build`。因此不能把本轮结果视为编辑器内构建验证通过；未改动系统 SDK，也未打开图形运行窗口。

`obstacle_3d_checks` 与 `obstacle_interaction_3d_checks` 新增 1371 项无图形检查，覆盖两种三维障碍物的避让、连续接触、环绕入口、重叠恢复与创建/编辑交互；另外 11 组相关二维/三维回归通过。未进行图形画面验收。

## 初版框架的历史验证

- Godot MCP：确认 4.7.2 Mono，创建/保存场景、启动编辑器和项目；应用运行日志无错误。
- 运行内检查：70 项全部通过，覆盖按钮状态、所有参数及规则开关、配置保存/恢复、网格实际绘制、未实现统计保持 `--`。
- 窗口布局：960 × 640、1360 × 860、1920 × 1080 均已运行并检查截图。

验证脚本与截图位于忽略目录 `.godot/validation/`，属于可删除的本地检查产物，不是项目运行依赖。FPS 是界面运行帧率，本次没有进行 Boids 算法性能测试。

当前架构与观察控件已通过 C# 编译和静态结构检查。另以 Godot `--headless` 模式实例化当前主场景，验证了 HUD 隐藏/恢复、输入框焦点释放、按键重复过滤、UI 输入隔离、不同倍率下的左键拖动、在 UI 上松手及失焦时停止拖动、鼠标位置缩放、缩放边界、Home 复位及无窗口视口尺寸适配。检查脚本与日志位于 `.godot/validation/check_observation_controls.gd` 和 `observation-controls.log`。

上述旧版截图不能代表当前布局；本次没有进行原生窗口、截图或图形驱动稳定性测试。无窗口检查验证的是运行逻辑，不代表画面效果或显卡驱动稳定性。
