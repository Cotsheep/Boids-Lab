# 三维观察、领队与操控实施验收

日期：2026-10-30。功能基线：`9fd3062`。本记录随三维观察与领队实施提交保存；实际提交与远程同步状态以 Git 为准。原有 `architecture.md` 图示缩进修改和 `3d-next-steps.md` 的历史交接正文保留。

后续操控调整：领队与普通个体统一按实际速度方向平滑转向，相机目标视角只决定相机和 WASD 的移动参考。侧移、后退时领队转向运动方向；静止时转动相机不会主动改变领队朝向。已同步 F2 与控件说明，并通过构建及 leader_3d、leader_interaction_3d、motion_3d、world_switch 四组回归；其中领队检查为 3549 项，包含静止观察、视角参考下的侧移/后退、角运动限幅及 40/200 个跟随者。

显示选项调整：新增三维「显示边界」，默认关闭；仅在勾选且没有领队时显示观察盒边框。配置自动保存，底面网格仍单独控制，软边界计算不受显示开关影响。构建及 observation_3d、leader_3d、world_switch、boundary_3d 四组回归通过。

## 四阶段交付

| 阶段 | 已实现内容 | 可独立运行的主要检查 |
| --- | --- | --- |
| 空间与观察 | 实际坐标搜索/调试/避障；可保存的观察范围；软边界；缩小不搬移；范围与相机独立 | boundary_3d_checks、spatial_3d_checks、obstacle_3d_checks、observation_3d_checks |
| 领队与无限世界 | 唯一身份和四种外观；共同快照；身后球形跟随区域；拥挤保护；退出恢复观察中心 | leader_3d_checks、steering_3d_checks |
| 第三人称操控 | 鼠标目标视角、WASD、制动、接触滑动、平滑透视跟随、视距保存及透视编辑 | leader_interaction_3d_checks、obstacle_interaction_3d_checks |
| 衔接与回归 | 取消/转移/删除/重置/失焦/帮助/世界切换清理；中文控件与说明；二维回归 | world_switch_checks 及全部既有二维检查 |

跟随时以领队步前速度作为局部群集规则的共同速度参考，避免分离/聚集反复制动共同前进速度。40/200 个跟随者的静止和持续转弯检查同时约束间距与跟随距离，另覆盖高跟随权重。

## 构建与无图形回归

C# 命令行构建通过，零警告、零错误。只在构建进程设置已有 workload resolver 绕过参数，未改系统 SDK；编辑器内 SDK 检测异常仍不属于本轮修复范围。

运行入口：

```powershell
./tests/run-headless.ps1 -GodotExecutable 'D:/app/Godot4.7.2/Godot_v4.7.2-stable_mono_win64_console.exe'
```

可通过 `-Scenes boundary_3d_checks,leader_3d_checks,leader_interaction_3d_checks` 只运行指定组。运行器按退出码、PASS 标记和 ERROR 输出判定，并在 finally 中还原测试前两套用户配置。日志保存到 `.godot/validation/full-regression/`。

已核查全部 22 个场景的最新日志均通过：

- 三维：boundary_3d、leader_3d、leader_interaction_3d、motion_3d、observation_3d、obstacle_3d、obstacle_interaction_3d、spatial_3d、steering_3d。
- 二维及共享：boundary_mode、debug、help_overlay、leader、leader_interaction、leader_spacing、motion、neighbor、obstacle_avoidance、obstacle_interaction、pause_shortcut、steering、world_switch。

以上名称均带 `_checks` 后缀。覆盖独立欧氏距离参照、负数与百万级坐标、两种搜索轨迹一致性、六面/边角边界、范围外障碍和连续接触、身份生命周期、真实视口输入、配置隔离。无图形耗时不作为桌面性能结论。

## 原生窗口检查与剩余视觉验收

通过 Godot MCP 启动原生窗口，使用当前 Intel Arc 的 OpenGL Compatibility 渲染。已观察到：

- 观察模式运行和暂停正常，群体持续转向；领队模式隐藏边框与底面网格。
- 指定领队后立即切入透视；选中领队显示粉色，取消身份恢复普通选择外观。
- 未操控的领队逐渐停止，跟随者持续靠近身后区域。
- 操控按钮进入“操控中”状态；滚轮修改视距并同步数值控件。
- 取消身份后恢复观察框与正交视角，群体位置保持连续。
- 两次运行的 MCP 错误列表均为空，检查结束后已关闭测试窗口；视距恢复为 300。

尚不能视为完整飞行手感验收：自动键盘工具发送空格时，Godot 收到逻辑键 Space、物理键 Pause，无法据此验证绑定物理键的真实键盘操作。临时诊断输出已移除。F3、Esc、WASD、暂停、文本焦点、帮助和失焦的程序逻辑已通过视口输入检查，但仍需用实际键盘确认捕获/释放、鼠标转向与平滑追随的主观效果。

已知取景限制：200 个跟随者聚集后，默认 300 视距可能位于群体附近，近处个体会遮挡领队。实际拉远到约 650 可观察队形。默认值仍遵循本轮方案；没有加入相机碰撞、个体透明化或自动取景策略。转弯队形、近距离遮挡和飞行时相机平滑仍适合单独人工评估。

无限世界继续使用现有单精度坐标，不引入浮动原点。本次短时窗口检查不代表图形驱动长期稳定性测试。
