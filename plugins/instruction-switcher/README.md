# Instruction Switcher

一个零 npm 依赖的个人 Codex 插件。它通过 `UserPromptSubmit` Hook 为每个任务保存独立的指令选择，并提供自动出现的 Windows 桌面伴随窗。

发布仓库：[foryourhealth111-pixel/Silly-codex](https://github.com/foryourhealth111-pixel/Silly-codex)。插件版本以 `.codex-plugin/plugin.json` 为准，当前版本为 `0.1.0`。

## 安装

在安装前确认 Windows 主机可以调用 Node.js 22 或兼容的 Node.js LTS。伴随窗面向 Windows；Hook 由 Codex 调用。

```powershell
codex plugin marketplace add foryourhealth111-pixel/Silly-codex --ref v0.1.0
codex plugin add instruction-switcher@silly-codex
```

安装后，在 Codex 的插件设置中审核并信任 `SessionStart` 与 `UserPromptSubmit` 两个 Hook。重新启动 Codex 或新建任务后，伴随窗会自动出现。

升级时先退出正在运行的 Codex 实例，再执行：

```powershell
codex plugin marketplace upgrade silly-codex
codex plugin add instruction-switcher@silly-codex
```

卸载插件：

```powershell
codex plugin remove instruction-switcher@silly-codex
```

运行时用户数据保存在 `%CODEX_HOME%\instruction-switcher`，卸载与升级流程会保留这些数据。删除该目录会移除本地指令库、预设和任务状态。

## 桌面伴随窗

打开或恢复 Codex 任务时，`SessionStart` Hook 会自动启动伴随窗。桌面端恢复已有任务后，首次提交消息时 `UserPromptSubmit` Hook 也会启动伴随窗。窗口置顶停靠在屏幕右下角，可以拖动，也可以通过系统托盘显示、隐藏或退出。Hook 启动流程不会创建或改写 Windows 登录启动项。

伴随窗提供白天与黑夜两套主题。白天模式使用白色和浅灰，黑夜模式使用灰黑色；绿色、琥珀色和红色仅用于启用、等待和错误状态。主题选择会随窗口偏好保存。

右上角的折叠按钮会把完整面板收束为约 `58 × 58` 的悬浮球。单击悬浮球可恢复面板，拖动悬浮球可调整位置，按 `Esc` 也可从展开面板折叠。展开面板和悬浮球分别保存位置、显示器、停靠边和边距；显示器、DPI 或任务栏工作区变化后，窗口会自动限制在当前可用区域内。

窗口提供以下操作：

- 自动跟随 Codex 侧栏当前选中的任务；
- 从最近任务中显式选择写入目标；
- 直接应用配置预设，并在列表中微调单个指令项；
- 在“启用指令”列表中拖动已启用项，直接调整当前任务的注入顺序；
- 保存当前组合为新预设，或明确更新已有预设；
- 打开指令库管理面板，编辑正文、排序、导入和导出；
- 显示状态已保存、等待读取或 Hook 已读取；
- 打开用户配置目录。

伴随窗通过 Codex 本机调试端口读取当前侧栏选择，并校验前台会话 ID 对应的 SHA-256 状态键。连续两次采样一致后，目标会自动切换，开关可以立即修改。新建任务尚未生成 Hook descriptor 时，伴随窗会从本地配置构造临时目标；首次消息提交时，Hook 会直接读取同一个会话键下的状态。

Codex 公开事件中缺少任务聚焦事件，因此自动跟随使用桌面端的本机 CDP DOM 属性。这些属性属于 Codex 内部界面。探测端口不可用、映射冲突或心跳过期时，窗口会回退到最近 Hook 活动的只读预览；用户也可以从下拉框显式选择目标，选择后立即切换。

开关写入该任务的独立状态文件；下一条普通消息提交时，Hook 会读取并应用已启用的指令，同时写入读取回执。两个同时运行的任务使用不同状态文件。旧版 v1 descriptor 继续显示，首次 Hook 活动会将其刷新为当前 descriptor 格式。

“隐藏到托盘”属于用户显式隐藏，窗口会保持隐藏，直到从托盘菜单选择“显示面板”或“显示悬浮球”。Codex 失去前台或最小化时，伴随窗会进入前台抑制状态；Codex 回到前台后会恢复用户上次选择的完整面板或悬浮球。两种状态分别管理，用户显式隐藏不会被自动恢复覆盖。

Codex 主窗口关闭约 15 秒后，伴随窗会自行退出；残留的 Electron 后台进程不会延长驻留时间，Codex 窗口最小化期间伴随窗进程保持运行。

## 控制命令

也可以直接发送控制命令：

```text
/choose status
/choose list
/choose set review,tdd
/choose on concise
/choose off review
/choose clear
```

控制命令由 Hook 处理，不会进入模型上下文。普通消息会自动附加当前任务已启用的指令正文。

## 指令库与配置预设

首次运行会在以下目录生成可编辑配置：

```text
%CODEX_HOME%\instruction-switcher\config.json
%CODEX_HOME%\instruction-switcher\instructions\
%CODEX_HOME%\instruction-switcher\sessions\
```

未设置 `CODEX_HOME` 时使用 `%USERPROFILE%\.codex\instruction-switcher`。`config.json` 保存指令项元数据、预设组合和可选的 `defaultPresetId`；每条指令项的 Markdown 正文保存于 `instructions\<id>.md`。用户可以在管理面板中编辑名称和正文，预设保存有序且不重复的指令项 ID。

应用预设会整体替换当前任务配置。临时勾选或取消指令项后，当前配置显示为“自定义配置”。删除指令项时，Hook 和管理面板会从预设及任务状态中清理对应引用；空预设继续保留。删除默认预设后，`defaultPresetId` 自动清空。默认预设只影响新发现的任务，已有任务继续使用自己的状态。

启用指令按任务状态中的有序 `enabled` 数组注入。伴随窗里已启用的行会显示左侧拖动手柄，拖动后立即保存当前任务顺序；未启用项排列在启用区之后。手动调整顺序后，配置预设选择器会显示“自定义配置”。

初始指令项和示例预设属于用户库内容。升级过程保留用户正文和已有配置，用户可以编辑或删除初始内容。

管理面板使用统一的“导入包…”入口，并自动识别指令包、配置预设包和旧版整库文件。指令包中的条目默认显示在自定义列表；配置预设包会携带完整依赖指令，这些随包指令默认隐藏，管理面板始终可以检索，当前任务启用后也会出现在主面板。指令页和配置预设页分别提供上下文导出；更多菜单提供整库备份与恢复。

修改 `config.json` 的 `command` 可以更换控制命令。Hook 会在下一条消息读取更新，桌面伴随窗也会同步刷新指令列表。伴随窗会启动 `companion/focus-tracker.mjs`，该进程仅连接 Codex 暴露在回环地址上的调试端口，并将当前任务映射写入本地 `runtime/focus.json`。

可以通过 `INSTRUCTION_SWITCHER_HOME` 指定独立的数据目录。新版状态固定保存在该目录的 `sessions` 子目录；旧 `PLUGIN_DATA` 状态会在对应任务的 SessionStart 或下一条消息 Hook 中迁移。状态写入使用跨进程锁保护 revision 检查。所有配置和任务状态均保存在本机。桌面伴随窗源码位于 `companion/InstructionSwitcherCompanion.cs`，使用 `scripts/build-companion.ps1` 可重新编译。

## 验证

```text
powershell -NoProfile -File scripts/build-companion.ps1
node --test tests/*.test.mjs
powershell -NoProfile -File tests/companion-lifecycle.test.ps1
powershell -NoProfile -File tests/library-package.test.ps1
pwsh -NoProfile -File tests/focus-regression.ps1
```

焦点回归会向原生输入框发送合成键盘消息，需要在可交互的 Windows 桌面会话中运行。限制输入注入的远程或无头环境只能核验窗口与焦点状态。

构建后可使用隔离运行目录检查浅色、深色、展开面板、悬浮球和管理窗口，避免改动日常使用的任务状态：

```powershell
$visualRoot = Join-Path $env:TEMP "instruction-switcher-visual-check"
New-Item -ItemType Directory -Force -Path (Join-Path $visualRoot "runtime") | Out-Null
& .\companion\InstructionSwitcherCompanion.exe (Join-Path $visualRoot "runtime")
```

## 许可证与作者

本插件目录中的源代码、测试、文档、构建脚本和随包默认指令内容按 [Apache License 2.0](LICENSE) 发布。运行时目录由用户控制，用户原创内容保留用户自己的权利；从随包种子复制出的部分继续按 Apache-2.0 使用。运行时文件不作为插件包的发布内容。

作者：`foryourhealth111-pixel`。第三方组件和宿主运行时的许可证由各自项目负责；本插件没有随包携带 npm 依赖。运行时数据处理见仓库根目录的 [`PRIVACY.md`](../../PRIVACY.md)。

## 签名状态

Windows 伴随窗的公开分发签名通过 SignPath Foundation 的受控流程接入。签名前的构建产物只用于 CI 内部验证，正式 Release 只接受已验证的 Authenticode 文件。签名策略见仓库根目录的 [`CODE_SIGNING_POLICY.md`](../../CODE_SIGNING_POLICY.md)。
