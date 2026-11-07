# 自动验证与实机验收状态

更新日期：2026-11-07。跟随范围显示、身份标记修复及方案 C 配色应用后，C# 构建零警告、零错误，完整 **33 组**无图形回归通过。2026-11-06 在代码基线 `ff920a7` 上的文档核查通过了当时全部 32 组，并以 SHA-256 核对二维、三维和主题三份配置在测试前后完全一致。图形效果及真实键盘操控仍由维护者按[实机验收清单](manual-acceptance.md)确认，尚未记录最终通过结论。功能与职责见[架构](architecture.md)，本文件集中保存验证方法、结果和性能测量。

## 构建与无图形回归

本次环境为 Windows、.NET SDK 8.0.400、Godot `4.7.2.stable.mono.official.ed1daf0bf`、C# net8.0，二维 GodotPhysics、三维 Jolt，Compatibility 渲染器。先在项目根目录（包含 `project.godot` 的 `boids/`）完成包还原，再执行运行器；引擎路径替换为本机 Godot .NET 控制台程序。常规构建与本机还原方式见[开发与构建说明](development.md#本机构建说明)。运行入口：

```powershell
./tests/run-headless.ps1 -GodotExecutable 'D:/app/Godot4.7.2/Godot_v4.7.2-stable_mono_win64_console.exe'
```

运行器以 `--no-restore` 构建，再按退出码、PASS 标记和 ERROR 输出判定；`finally` 恢复测试前两套模拟配置和独立外观偏好的原始字节，原先不存在的文件会删除。日志保存在 `.godot/validation/full-regression/`。workload resolver 绕过参数只在运行脚本的 PowerShell 进程中临时设置，结束后恢复，未修改系统 SDK。2026-10-29 的编辑器 SDK 检测异常见[历史记录](validation-history.md)；本次未重新验证编辑器内构建。

通过 `-Scenes boundary_3d_checks,leader_3d_checks,leader_interaction_3d_checks` 可运行指定组（上述数组写法适用于 PowerShell 内调用）。默认发现 `tests/` 直属目录的全部 `*_checks.tscn`；以下为可直接传给 `-Scenes` 的完整名称，不含扩展名：

- 三维（11 组）：`boundary_3d_checks`、`camera_fade_3d_checks`、`leader_3d_checks`、`leader_interaction_3d_checks`、`motion_3d_checks`、`observation_3d_checks`、`obstacle_3d_checks`、`obstacle_interaction_3d_checks`、`presentation_3d_checks`、`spatial_3d_checks`、`steering_3d_checks`。
- 二维及共享（22 组）：`angular_limits_checks`、`boundary_mode_checks`、`debug_checks`、`help_overlay_checks`、`leader_checks`、`leader_follow_region_checks`、`leader_gap_checks`、`leader_interaction_checks`、`leader_spacing_checks`、`motion_checks`、`neighbor_checks`、`obstacle_avoidance_checks`、`obstacle_interaction_checks`、`pause_shortcut_checks`、`physics_obstacle_checks`、`population_checks`、`single_step_checks`、`steering_checks`、`theme_checks`、`ui_layout_checks`、`viewport_boundary_2d_checks`、`world_switch_checks`。

诊断、性能及图形场景没有 `_checks` 后缀，须按各文档单独运行，不计入这 33 组。部分旧集成检查直接读写默认用户配置，复测时使用运行器统一备份与恢复；仅退出码为 0 不足以证明检查完成，还须出现 PASS 且没有 ERROR。

2026-11-06 完整回归后同步校订了两套 F2 帮助，再次运行 `help_overlay_checks`、`ui_layout_checks`、`world_switch_checks`、`angular_limits_checks`、`theme_checks` 五组，全部通过。

| 范围 | 主要覆盖 |
| --- | --- |
| 运动、邻居与群集 | 独立距离参照、负数和百万坐标、朴素/网格逐步轨迹一致性、共同快照、运动限幅 |
| 世界与领队 | 二维环绕/无限模式、三维软边界、身份生命周期、速度朝向、40/200 跟随者间距及跟随距离 |
| 交互与配置 | 真实视口事件、捕获/释放意图、文本焦点、帮助、失焦、编辑、世界切换及配置隔离 |
| 单步与数量 | 单步 137 项、数量调整 84 项断言；暂停、稳定增删、障碍保护、失败与重试 |
| 界面与主题 | 分类界面 270 项、主题 169 项断言；三个视口尺寸、草稿提交、焦点、明暗偏好及新生个体 |
| 显示时序 | 三维显示与相机插值 1746 项断言；物理/显示隔离、拾取及调试锚点一致 |

`physics_obstacle_checks` 使用真实静态查询，含 14600 项断言，覆盖资源变化、父变换、预览排除、立即删除、凹口/通道、深重叠与接缝、多面滑动、长位移、零间距、百万坐标、封闭区域、预算耗尽、暂停编辑后单步，以及带不规则障碍的两种搜索轨迹一致性。原高速、开关、领队、环绕、数量、输入、主题与插值检查保留。

2026-11-04 新增 `leader_gap_checks` 的真实管理器回归：原参数速度 500、加速度 1000、前瞻 1.5 秒、间距 2；宽 48 的通道和目标后方障碍在修复前造成约 329.77 单位横向绕行、10 秒后距目标约 292.20。目标感知修复后正面及旋转 90° 的同一场景横向偏移为 0，10 秒后停在到达容差内（约 1.155 单位）；同时覆盖斜向接近、零间距、28 宽通道内静止起步/保持停止、近目标的高速制动、真正过窄通道的阻挡，以及两种邻居搜索。全程用独立矩形距离参照检查间距与不穿障，未缩小实际接触半径。

`viewport_boundary_2d_checks` 验证可选二维视口软边界：四边及四角连续回归、运动限幅与零加速度、普通群集/主动避障优先级、跨边不再成为周期邻居、两种搜索及调试、暂停/运行中切换、领队无限模式和重置、相机平移缩放/Home/窗口变化不搬移、视口外障碍，以及真实 UI 保存、旧配置默认值和二维/三维切换隔离。原环绕模式专项继续通过。

`angular_limits_checks` 验证两种模式的统一角运动限制开关：直接朝向、低速滞回、零速度、垂直及掉头、切换时清理角运动状态、恢复两项上限、真实接触后的最终速度与网格尖端对齐、三维旋转立即刷新且保留位置插值，以及暂停 UI、原数值保留、旧配置默认开启和世界切换隔离。

`leader_follow_region_checks` 含 1119 项断言，覆盖显示开关与配置隔离、真实跟随区域几何、二维单一领队圆环与目标重叠抑制、暂停修改与身份生命周期、三维显示插值及镜头进入球体后的填充淡出，以及开关前后逐步运动轨迹一致性。方案 C 另通过 Compatibility 原生渲染检查了 10 张二维 / 三维跟随范围对照图，以及默认资源的明暗主题、普通选中 / 选中领队对照图；复现方法见[跟随范围显示](leader-follow-region.md)。

额外的 40/200 跟随者绕障检查保留穿障、有限状态、速度、最终间距与跟随距离断言。二维汇合期存在短暂拥挤：重构测量中每秒采样的最近距离不足 10 单位峰值为 5/40、20/200，最终为 0/40、2/200，平均最近距离约 20.42、16.97。二维新增压力场景从绕障后阶段检查原拥挤标准（不超过 10%），原无障碍检查未放宽；三维继续在移动采样点检查。避障优先会临时压过分离力，不能保证全过程硬间距。

封闭场景 `PhysicsObstacleChecks.Enclosed` 使用各轴 ±50、厚度 16 的六面墙和间距 12，验证全部方向受阻时制动并保持在自由空间内；不保证逃出 U 形、封闭或过窄布局。接入方式与几何限制见[避障架构](obstacle-query-refactor.md)。

## 避障性能记录

2026-11-03 迁移基线为 `0681de343b39b0fdd8a43f887474ca01752c7a6f`。基线通过临时 Git 导出测量，正式工作区未切换；导出副本保存在本地忽略目录 `.godot/validation/obstacle-baseline/`。环境：Intel Core Ultra 7 155H，16 核/22 线程，Windows build 26200，Godot 4.7.2 .NET Debug，headless。

两版使用种子 42、默认运动参数、网格搜索、6 个相同布局的菜单障碍；预热 10 步，200/1000 个体各采样 40 步，5000 个体采样 5 步。表中为管理器 UpdateMilliseconds，含邻居、策略、积分和接触，不含渲染。两版轨迹随算法变化，属于同布局比较。

| 维度 / 数量 | 基线均值 ms | 重构均值 ms | 基线 P95 ms | 重构 P95 ms | 重构原生查询 / 步（均值） |
| --- | ---: | ---: | ---: | ---: | ---: |
| 2D / 200 | 1.7850 | 2.4561 | 1.9589 | 3.3940 | 1547.9 |
| 3D / 200 | 1.6882 | 2.2002 | 1.8550 | 2.5234 | 1400.0 |
| 2D / 1000 | 11.2823 | 14.8664 | 12.7726 | 23.0523 | 7639.2 |
| 3D / 1000 | 8.7566 | 11.7057 | 10.0616 | 15.3928 | 7004.8 |
| 2D / 5000 | 93.7214 | 108.3262 | 103.3364 | 115.7242 | 40578.2 |
| 3D / 5000 | 48.7936 | 62.5022 | 53.8548 | 71.8576 | 35270.2 |

该布局中 200/1000 个体平均更新时间增加约 30%～38%；5000 个体仅验证短样本可完成性。基线原生查询数为 0，因为旧算法使用手写几何，不能将查询数当成工作量比值。这是单机短样本记录，不转换为桌面 FPS。

当前版本可用保留的性能场景复测：

```powershell
& 'D:/app/Godot4.7.2/Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path . res://tests/diagnostics/obstacle_benchmark.tscn
```

迁移测量原始日志保留在 `.godot/validation/obstacle-benchmark-before-final.log` 和 `obstacle-benchmark-after-final.log`，不随源码推送。

## 已有窗口检查与待验收范围

此前通过 Godot MCP 在 Intel Arc / OpenGL Compatibility 原生窗口检查过观察模式运行/暂停、领队透视及身份颜色、停止与跟随、操控按钮、视距保存、取消身份返回观察模式；两次错误列表为空，检查后恢复视距 300。

自动键盘曾发送逻辑键 Space、物理键 Pause，不能据此确认绑定物理键的实际键盘行为。F3、Esc、WASD、暂停、焦点、帮助和失焦已通过视口事件检查，捕获/释放、鼠标转向及连续飞行手感仍待实机确认。

近距离淡化已有 4694 项交付时逻辑检查及 16 组同状态图形对比，见[相机淡化](camera-fade-3d.md)。选中对象保持不透明；未加入相机碰撞，任意队形仍可能遮挡。界面及主题的局部窗口结果见[界面](ui-layout.md)和[主题](themes.md)，相机显示插值的时序对照见[抖动诊断](follow-camera-jitter.md)。

2026-11-06 文档核查只重新执行构建与无图形回归，未新增图形或真实键盘验收结果。不规则轮廓、候选/法线可读性、群体绕行观感和旋转父节点下的编辑手感仍待检查。无限世界仍使用单精度坐标；短时窗口检查不代表驱动长期稳定性。较早结果保留在[历次验证记录](validation-history.md)。
