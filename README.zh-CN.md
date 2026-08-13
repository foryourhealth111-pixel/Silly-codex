<div align="center">

# Silly Codex

**让可复用的指令跟着你的工作习惯走。**

**[English](README.md) | [简体中文](README.zh-CN.md)**

[![License](https://img.shields.io/badge/license-Apache--2.0-2f6f9f.svg)](LICENSE)
[![Node tests](https://github.com/foryourhealth111-pixel/Silly-codex/actions/workflows/node-tests.yml/badge.svg)](https://github.com/foryourhealth111-pixel/Silly-codex/actions/workflows/node-tests.yml)
[![Windows build](https://github.com/foryourhealth111-pixel/Silly-codex/actions/workflows/windows.yml/badge.svg)](https://github.com/foryourhealth111-pixel/Silly-codex/actions/workflows/windows.yml)

</div>

记录影响 AI 协作质量的细微规则，按任务自由组合，并在该任务的每条普通消息中持续应用当前组合。

<div align="center">

<a href="docs/assets/instruction-switcher-overview.png"><img src="docs/assets/instruction-switcher-overview.png" alt="Silly Codex 伴随窗显示当前任务、配置预设、指令顺序和 Hook 回执" width="418"></a>

<br>
<sub>截图使用合成任务和演示预设。新安装提供六条可编辑指令，配置预设初始为空。</sub>

</div>

## 什么是 Silly Codex？

Silly Codex 是一个管理可复用指令的 Codex 插件。你可以把“使用中文回复”“遵循这套代码风格”“先运行测试”“用表格输出”等要求分别保存为 Markdown 指令。它们会留在本地指令库中，无需依赖聊天记录，也不用塞进一个越来越长的提示文件。

你可以为当前任务选择一组有序指令。插件会在本地保存这组选择，并由 `UserPromptSubmit` Hook 在每条普通消息中注入已启用的指令正文。Windows 伴随窗会显示当前任务、所用预设、指令顺序以及 Hook 读取状态。

所有任务共用同一套指令库，每个 Codex 任务保存自己的启用组合。你还可以设置默认预设，为新任务建立统一起点。长期偏好有稳定的存放位置，临时要求也能按任务单独调整。

## 为什么使用 Silly Codex？

语言、格式和工作习惯往往藏在很多细小要求里。对话结束后，这些要求很容易散落；每次复制一大段提示词，也很难快速确认当前生效的内容。

| 常见问题 | Silly Codex 的处理方式 | 你得到的结果 |
| --- | --- | --- |
| 语言、代码风格、测试要求和输出格式散落在不同对话里 | 把每项偏好保存成可编辑指令 | 对话结束后，成熟规则仍可继续使用 |
| 审查、开发、研究和文档任务需要不同规范 | 创建有顺序的预设，并针对当前任务微调 | 每个任务只带上需要的组合 |
| 新对话缺少已经磨合好的协作习惯 | 应用已有预设，或为新任务设置默认预设 | 很快恢复熟悉的协作方式 |
| 临时规则影响了其他工作 | 按 Codex 会话键分别保存启用列表 | 再次打开旧任务时恢复原有选择 |
| 成熟规则难以迁移或分享 | 导出单条指令、包含依赖的预设或整库备份 | 通过本地文件迁移到其他设备或分享给他人 |

## 核心功能

| 能力 | 实际用途 |
| --- | --- |
| 指令库 | 创建、编辑、搜索、隐藏和删除具名 Markdown 指令 |
| 有序预设 | 按场景组合指令、控制注入顺序，并将组合保存复用 |
| 任务级状态 | 为并行进行的 Codex 任务保存独立组合，恢复任务时继续沿用 |
| 随时调整 | 在伴随窗应用预设、开关单条指令、拖动排序或撤销预设切换 |
| 可迁移包 | 预览并导入 `.ispkg.json` 文件，按冲突情况选择新建、复用、更新、复制或跳过 |
| 本地存储 | 将指令库、预设和任务状态保存在 Codex 数据目录下 |
| 无窗口控制 | 伴随窗隐藏或不可用时，通过 `/choose` 命令管理当前任务 |

Windows 伴随窗可以跟随 Codex 侧栏中选中的任务，也可以将已发现的最近任务设为明确写入目标。界面支持展开面板、悬浮球和系统托盘三种形态。

## 工作方式

```mermaid
flowchart LR
    A["创建可复用指令"] --> B["组合成配置预设"]
    B --> C["为当前任务选择或微调"]
    C --> D["在本地保存有序任务状态"]
    D --> E["提交普通 Codex 消息"]
    E --> F["Hook 注入已启用的 Markdown"]
    F --> G["伴随窗显示读取回执"]
```

1. 指令和预设保存在同一个本地库中。
2. 每个 Codex 会话记录一份有顺序的启用指令 ID 列表。
3. 应用预设会替换这份列表。手动开关或拖动指令后，当前任务会显示为自定义组合。
4. 提交下一条普通消息时，Hook 读取最新状态并按顺序注入正文。
5. `/choose` 控制消息由 Hook 处理，不会进入模型上下文。

<div align="center">

<a href="docs/assets/instruction-switcher-workflow.gif"><img src="docs/assets/instruction-switcher-workflow.gif" alt="Silly Codex 展示任务跟随、应用预设、微调指令、Hook 回执和悬浮球的完整流程" width="794"></a>

<br>
<sub>动图使用合成任务数据，操作来自真实的 Windows 伴随窗。</sub>

</div>

## 典型使用场景

- 将代码风格、回复语言、测试要求和输出格式分别保存为指令。
- 为一个项目创建严格审查预设，为另一个项目创建简洁开发预设。
- 用默认预设建立新任务的协作基线，再添加只对当前任务生效的临时要求。
- 回到以前的任务时，恢复当时的指令组合和顺序。
- 导出已经成熟的审查或写作预设，连同所需指令一起分享给团队或社区成员。
- 使用整库备份把个人指令库迁移到另一台设备。

## 快速开始

### 环境要求

| 组件 | 要求 |
| --- | --- |
| Codex | 支持插件、`SessionStart` 和 `UserPromptSubmit` Hook 的桌面环境 |
| Codex CLI | 安装时可以从 `PATH` 调用 |
| Node.js | 20 或更高版本 |
| Windows | 完整伴随窗需要 Windows 10/11 和可交互桌面会话 |

Hook 和本地指令库使用 Node.js。可视化伴随窗仅在 Windows 上启动。

### 安装

当前安装器直接从 GitHub 仓库获取。在 PowerShell 中运行：

```powershell
npx --yes github:foryourhealth111-pixel/Silly-codex
```

安装器会登记或升级 `silly-codex` marketplace，然后安装 `instruction-switcher@silly-codex`。npm 包和插件运行时不引入第三方 npm 依赖，安装器也没有 lifecycle 或 `postinstall` 脚本。

安装完成后：

1. 在 Codex 插件设置中审核并信任 `SessionStart` 和 `UserPromptSubmit` Hook。
2. 重启 Codex，或打开一个新任务。
3. 在伴随窗中打开“设置 > 指令库”，编辑一条初始指令，或新建一条经常使用的偏好。
4. 打开“配置预设”，新建预设、勾选所需指令，再拖动调整顺序。
5. 回到伴随窗应用该预设，然后提交一条普通消息。

首次运行会创建六条可编辑的初始指令。预设列表初始为空，第一次有效配置通常只需准备一条符合个人习惯的指令，再把它保存到一个预设中。

<details>
<summary>手动安装 Codex marketplace</summary>

```powershell
codex plugin marketplace add foryourhealth111-pixel/Silly-codex --ref main
codex plugin add instruction-switcher@silly-codex
```

</details>

### 不使用伴随窗时控制任务

在 Codex 消息框中发送以下命令。`/choose list` 会显示其他命令需要使用的 ID。

```text
/choose help
/choose status
/choose list
/choose preset <预设-id>
/choose set <指令-id-1>,<指令-id-2>
/choose on <指令-id>
/choose off <指令-id>
/choose clear
```

Hook 会把这些消息当作控制操作处理，命令文本不会发送给模型。

## 导入、导出与分享

在伴随窗中打开“设置”，即可使用可迁移的包文件：

| 操作 | 入口 | 包含内容 |
| --- | --- | --- |
| 导出指令 | “指令库 > 导出当前指令” | 当前选中的指令及其 Markdown 正文 |
| 导出预设 | “配置预设 > 导出预设” | 当前预设及其引用的全部指令 |
| 导入包 | “导入包” | 预览待导入指令、预设、依赖和冲突 |
| 备份指令库 | “更多 > 备份整个指令库” | 指令、预设、控制命令和默认预设 |
| 恢复备份 | “更多 > 恢复备份” | 确认后替换当前指令库和预设 |

写入前会先显示导入预览。遇到匹配项或冲突时，你可以新建条目、复用相同本地内容、更新已导入条目、保留本地版本并创建副本，或跳过该条目。预设包会带上完整的依赖指令，接收者无需手工重建组合。

生成的 `.ispkg.json` 文件可以通过现有渠道发送。Silly Codex 当前不提供云同步、账户服务或公开分享链接。

> 整库备份不包含各任务的会话状态，也不包含窗口位置、界面语言和主题偏好。恢复备份会替换当前指令库和预设，请在确认前检查预览。

## 与 `AGENTS.md` 和 skills 的区别

Silly Codex、[`AGENTS.md`](https://developers.openai.com/codex/guides/agents-md/) 和 [skills](https://developers.openai.com/codex/skills/) 处理的是指令管理中的不同问题。

| | Silly Codex | `AGENTS.md` | Skills |
| --- | --- | --- | --- |
| 主要用途 | 细小行为规则和个人偏好 | 稳定的全局或仓库级规范 | 可重复执行的任务流程和能力 |
| 内容单元 | 一条具名 Markdown 指令 | 一份按范围生效的 Markdown 文件 | 包含 `SKILL.md`，并可附带脚本和参考资料的目录 |
| 生效方式 | 按任务在界面选择或使用 `/choose`，随普通消息注入 | 每次运行开始时按全局和目录范围发现 | 明确调用，或在描述与任务匹配时选用 |
| 组合方式 | 将有序指令保存为预设，运行中随时调整 | 按目录优先级合并多层文件 | 使用一个或多个自包含 skill 包 |
| 分享方式 | 导出指令包和预设包 | 复制或提交 Markdown 文件 | 分享 skill 目录，或放进插件分发 |
| 适合场景 | 经常按任务变化、需要快速确认和切换的偏好 | 应持续作用于用户、仓库或子目录的规则 | 需要详细步骤、参考资料、工具或脚本的完整流程 |

三者可以共同使用。仓库不变量适合放在 `AGENTS.md` 中，完整流程适合交给 skills，日常输出里经常重新组合的细微偏好可以交给 Silly Codex。

## 常见问题

### 新任务会自动启用全部初始指令吗？

不会。新安装包含六条可编辑指令，预设和启用列表都为空。你可以自行选择。若在“设置 > 配置预设”中指定默认预设，新任务会从该有序组合开始。

### 哪些内容会跨对话保留？

指令库和预设在本机共享。每个已发现的 Codex 会话会保存自己的启用列表和顺序。再次打开已有任务时会恢复原选择；新任务只在设置默认预设后自动获得初始组合。

### 编辑一条指令后会发生什么？

预设和任务状态通过稳定 ID 引用指令。下一次 Hook 读取时，所有启用这条指令的任务都会使用更新后的 Markdown 正文。

### 子代理任务会继承当前组合吗？

不会。每个会话 ID 分别保存状态，子代理任务不会继承父任务的当前组合。设置默认预设后，子代理会话可以用该预设初始化自己的组合。

### 必须使用 Windows 吗？

WinForms 伴随窗、悬浮球和托盘菜单需要 Windows 10/11。其他系统会跳过伴随窗。在 Codex 插件和 Hook 环境受支持的情况下，Node.js Hook 与 `/choose` 控制路径仍可使用。

### 数据保存在哪里？

默认目录为：

```text
%CODEX_HOME%\instruction-switcher
```

没有设置 `CODEX_HOME` 时，实际目录为 `%USERPROFILE%\.codex\instruction-switcher`。需要更换本地目录时，请在 Codex 启动前设置 `INSTRUCTION_SWITCHER_HOME`。设置窗口可以显示并打开当前目录。

### Silly Codex 会上传我的指令吗？

项目没有自动上传、遥测、广告或远程账户服务。任务跟随功能会读取 Codex 的本机调试端点和相关本地状态，并将结果写入插件数据目录。只有你主动分享包文件时，文件才会离开本机。数据边界详见 [PRIVACY.md](PRIVACY.md)。

## 参与贡献

欢迎提交缺陷报告、文档修正和范围清晰的代码改动。本地检查与提交说明见 [CONTRIBUTING.md](CONTRIBUTING.md)，安全问题请按照 [SECURITY.md](SECURITY.md) 中的流程报告。

## 许可证

仓库中的源代码、测试、文档、构建脚本和随包初始指令采用 [Apache License 2.0](LICENSE)。你在本地指令库中创作的内容仍由你自行管理。第三方说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

Silly Codex 是 [@foryourhealth111-pixel](https://github.com/foryourhealth111-pixel) 维护的独立项目。Codex 和 OpenAI 是其各自所有者的商标。

## 致谢

感谢 [Linux.do](https://linux.do/) 社区的支持。
