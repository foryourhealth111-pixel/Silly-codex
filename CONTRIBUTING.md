# Contributing

感谢参与 Silly-codex。提交改动前请先阅读根目录的 `LICENSE`、`NOTICE` 和 `CODE_SIGNING_POLICY.md`。

## 本地检查

```powershell
node --test plugins/instruction-switcher/tests/*.test.mjs
powershell -NoProfile -File plugins/instruction-switcher/scripts/build-companion.ps1
powershell -NoProfile -File plugins/instruction-switcher/tests/companion-lifecycle.test.ps1
powershell -NoProfile -File plugins/instruction-switcher/tests/library-package.test.ps1
powershell -NoProfile -File plugins/instruction-switcher/tests/theme-transition-layer.test.ps1
node scripts/validate-plugin.mjs
```

焦点回归脚本需要可交互桌面，提交前可以在本机单独运行。

## 提交约定

- 保持插件运行时零 npm 依赖。
- Hook 只读写 `%CODEX_HOME%\\instruction-switcher` 中的用户数据。
- 修改默认指令或预设时，同时说明兼容性影响。
- 新增第三方依赖时，提交来源、版本和许可证说明。
- 提交信息使用简短的英文动词开头，例如 `Add`、`Fix`、`Update`。

贡献内容按 Apache License 2.0 提交。第三方或其他许可证内容需要使用 OSI 批准且与 Apache-2.0 兼容的许可证，并在提交中记录来源、版本、许可证和 NOTICE 义务。
