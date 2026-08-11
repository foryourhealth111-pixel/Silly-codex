# Windows 免费代码签名方案调研

更新日期：2026-08-11

## 结论

对于 GitHub Release 中的原始 Windows EXE，当前核验到的零成本公众信任 Authenticode 路线是申请 SignPath Foundation。它需要项目先公开发布、使用 OSI 批准许可证、具备公开文档和可验证的项目声誉，并通过人工审核。

SignPath 的 GitHub 工作流要求使用 GitHub-hosted runner 构建未签名产物，再通过 SignPath Action 提交签名请求。签名完成后，仓库应运行 `signtool verify /pa /all /v`，再对最终签名文件生成 GitHub Artifact Attestation。

## 方案比较

| 方案 | 成本 | Windows 发布体验 | 适合 Silly-codex 的用途 |
|---|---:|---|---|
| SignPath Foundation | 免费，需审核 | 公众信任 Authenticode | 首选，待项目首次公开发布后申请 |
| Azure Artifact Signing | 收费，Basic 起价 9.99 美元/月 | 公众信任 Authenticode | 项目稳定后再考虑 |
| GitHub Artifact Attestation | 公开仓库可用 | 提供来源证明，SmartScreen 不读取它 | 作为签名之外的供应链证明 |
| 自签名证书 | 免费 | 公众用户仍会看到未知发布者 | 本地测试或企业内部分发 |
| Microsoft Store MSIX | 开发者账户可免费注册 | Store 安装由微软处理签名 | 未来做商店版桌面应用时评估 |

## 对项目的影响

1. 先公开 `Silly-codex`，补齐 Apache-2.0 许可证、代码签名政策、作者信息和发布说明。
2. 在仓库首页或发布页加入 `Code signing policy` 链接，并说明构建、审批和签名流程。
3. GitHub Actions 使用 Windows runner 构建伴随窗，上传未签名 EXE，提交 SignPath 签名请求。
4. 只有签名成功并完成验证后，才把 EXE 放入 `v0.1.0` Release。
5. Authenticode 会改变文件哈希；校验和和 Artifact Attestation 必须针对最终签名文件生成。

## 官方来源

- [Microsoft：Code signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
- [Microsoft：SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)
- [SignPath Foundation](https://signpath.org/)
- [SignPath 条款](https://signpath.org/terms)
- [SignPath：GitHub trusted build system](https://docs.signpath.io/trusted-build-systems/github)
- [Azure Artifact Signing pricing](https://azure.microsoft.com/en-us/pricing/details/artifact-signing/)
- [GitHub Artifact Attestations](https://docs.github.com/en/actions/concepts/security/artifact-attestations)
