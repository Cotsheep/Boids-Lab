# 文档导航

按下表查阅操作与实现；当前测试结果、性能和待验收范围统一见[验证记录](3d-implementation-validation.md)。

## 当前使用与实现

| 文档 | 内容 |
| --- | --- |
| [项目 README](../README.md) | 打开运行、二维/三维操作、参数范围与构建说明 |
| [领域术语表](../CONTEXT.md) | 个体数量、选择、领队、操控、跟随区域与边界的统一定义 |
| [自动验证与验收状态](3d-implementation-validation.md) | 当前回归、复现命令、性能测量与实机验收边界 |
| [实机验收清单](manual-acceptance.md) | 供维护者检查键鼠操控、视觉效果、单步与数量调整 |
| [架构说明](architecture.md) | 管理器统一更新、规则计算、状态与 UI 分工 |
| [界面分组](ui-layout.md) | 五类参数、固定操作栏、输入提交与布局检查 |
| [冰岩明暗主题](themes.md) | 可编辑配色资源、Remote Inspector 调色、主题与世界显示 |
| [领队跟随抖动诊断](follow-camera-jitter.md) | 复现、显示插值修复、时序与生命周期回归 |
| [世界切换](world-switching.md) | 两套配置隔离、切换顺序与清理语义 |
| [二维视口软边界](viewport-boundary-2d.md) | 可选视口软边界、环绕/领队衔接及相机变化 |
| [角运动与直接朝向](angular-motion.md) | 角速度/角加速度统一开关、极低速度处理及三维显示 |
| [三维邻居搜索](neighbor-search-3d.md) | 直接距离、无界网格、快照和一致性 |
| [统一避障查询与资源接入](obstacle-query-refactor.md) | 不规则实心障碍、同步、策略、接触与调试显示 |
| [三维障碍与避障](obstacles-3d.md) | 放置、选择、拖动、尺寸与模式行为 |
| [近距离淡化](camera-fade-3d.md) | 材质行为、固定快照图形对比与局限 |
| [单步控制](single-step.md) | 固定时间步、暂停、输入隔离与验证 |
| [数量即时增减](population-resizing.md) | 提交时机、保留顺序、障碍采样、随机性与失败反馈 |

早期实现与环境检查保留在[历次验证记录](validation-history.md)。各功能文档中的交付时测试数量按记录日期理解；当前结果以验证记录为准。`.godot/validation/` 中的日志和图片是本地生成产物，不随源码推送。

## 文档维护约定

- `CONTEXT.md` 只记录领域术语、关系和歧义；实现细节放在架构或对应功能文档中。
- 参数默认值、范围和步长以 `scripts/simulation/BoidConfig.cs` 为准；二维长度使用世界像素，三维使用世界单位，屏幕容差另标视口像素。
- 操作说明正文在 `scenes/worlds/world_2d.tres` 与 `world_3d.tres`，弹层布局在 `scenes/ui/help_overlay.tscn`；修改用户可见行为时同步检查 README、功能说明和两套 F2 帮助。
- 当前测试清单与最近一次运行结果集中在[验证记录](3d-implementation-validation.md)；新增检查场景后更新清单，功能文档中的旧组数须注明交付日期。
- 命令从包含 `project.godot` 的项目根目录执行。引擎绝对路径仅为本机示例；首次执行无图形回归前先完成包还原，使用运行器保护用户配置。
- 历史测量保留日期、环境和局限；只有实际执行并取得证据后，才更新构建、回归或实机验收结论。
