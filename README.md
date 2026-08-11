# Silly-codex

Silly-codex 是一个面向 Codex 的开源插件仓库。当前仓库提供 `instruction-switcher`，用生命周期 Hook 管理每个任务的指令选择，并在 Windows 上提供桌面伴随窗。

## 快速安装

先安装 Node.js LTS，并确认 `node` 命令位于 PATH。然后在 PowerShell 中执行：

```powershell
codex plugin marketplace add foryourhealth111-pixel/Silly-codex --ref v0.1.0
codex plugin add instruction-switcher@silly-codex
```

安装后，在 Codex 的插件设置中审核并信任 `SessionStart` 与 `UserPromptSubmit` Hook。重新启动 Codex 或新建任务后，Windows 伴随窗会自动启动。

升级：

```powershell
codex plugin marketplace upgrade silly-codex
codex plugin add instruction-switcher@silly-codex
```

卸载：

```powershell
codex plugin remove instruction-switcher@silly-codex
```

运行时数据位于 `%CODEX_HOME%\instruction-switcher`。插件升级和卸载会保留用户指令库、预设与任务状态；删除该目录会清除这些本地数据。

## 功能范围

- `SessionStart` Hook 注册任务并启动 Windows 伴随窗。
- `UserPromptSubmit` Hook 读取当前任务选择并注入指令正文。
- 伴随窗支持自动跟随任务、右下角停靠、悬浮球、托盘菜单、主题切换、预设和指令库管理。
- `/choose` 命令用于无窗口场景下的任务级控制。
- 首版发布运行链使用 Hook 与伴随窗。MCP 内嵌面板不属于首版安装内容。

## 开发

仓库不依赖 npm 包。Node 测试使用内置 `node:test`，Windows 伴随窗使用系统 .NET Framework C# 编译器：

```powershell
node --test plugins/instruction-switcher/tests/*.test.mjs
powershell -NoProfile -File plugins/instruction-switcher/scripts/build-companion.ps1
powershell -NoProfile -File plugins/instruction-switcher/tests/companion-lifecycle.test.ps1
powershell -NoProfile -File plugins/instruction-switcher/tests/library-package.test.ps1
powershell -NoProfile -File plugins/instruction-switcher/tests/theme-transition-layer.test.ps1
```

`focus-regression.ps1` 需要可交互的 Windows 桌面，会移动鼠标并发送合成输入；它适合手工验收。

## 发布与签名

GitHub Actions 负责 Node 测试、Windows 构建、插件校验和敏感信息扫描。签名流程使用 SignPath Foundation 的托管 Authenticode 服务：Windows runner 先构建并上传未签名 EXE，SignPath 完成签名后再下载并验证。签名策略和所需 GitHub Secrets 见 [Code signing policy](CODE_SIGNING_POLICY.md)，本地数据处理方式见 [`PRIVACY.md`](PRIVACY.md)。

Release 使用 Git 标签和 GitHub 默认源码归档，不生成额外的自定义插件 ZIP。正式 Release 同时附加 EXE、`LICENSE.txt` 和 `NOTICE.txt`；EXE 必须通过：

```powershell
signtool verify /pa /all /v InstructionSwitcherCompanion.exe
```

## 仓库结构

```text
Silly-codex/
├─ .agents/plugins/marketplace.json
├─ .github/workflows/
├─ plugins/instruction-switcher/
├─ scripts/
├─ docs/
├─ LICENSE
└─ NOTICE
```

## 许可证与商标

本仓库中的源代码、测试、文档、构建脚本和随包默认指令内容按 [Apache License 2.0](LICENSE) 发布。运行时目录由用户控制，用户原创内容保留用户自己的权利；从随包种子复制出的部分继续按 Apache-2.0 使用。运行时文件不作为本仓库的发布内容。

作者：`foryourhealth111-pixel`。Codex 与 OpenAI 是其各自所有者的商标；本项目保持独立运行和发布。

安全问题请参阅 [`SECURITY.md`](SECURITY.md)，贡献流程请参阅 [`CONTRIBUTING.md`](CONTRIBUTING.md)。CI 工具的上游许可见 [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)。
