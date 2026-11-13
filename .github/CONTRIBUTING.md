# 贡献指南 / Contributing

欢迎提交问题、改进文档、翻译或代码。中文和英文反馈都可以。参与交流时请遵守[行为准则](CODE_OF_CONDUCT.md)。安全漏洞请按[安全政策](SECURITY.md)私密报告。

We welcome bug reports, documentation, translations and code contributions in Chinese or English. Follow the [Code of Conduct](CODE_OF_CONDUCT.md). Report vulnerabilities privately as described in the [Security Policy](SECURITY.md).

## 开始之前 / Before starting

- 先阅读 [README](../README.md) 或 [English README](../README.en.md)，搜索已有 Issues 和 PR，避免重复。
- 小修复可以直接提交 PR；较大的功能或架构调整，请先提交 Issue 说明场景和方案。
- 不要提交真实录制、个人宏、敏感截图、凭据或本机配置。需要复现数据时，提供最小的脱敏样例。

- Read the [README](../README.en.md) and search existing issues and pull requests first.
- Small fixes can go straight to a PR. Discuss larger features or architectural changes in an issue first.
- Keep personal recordings, macros, sensitive screenshots, credentials and machine-specific settings out of commits. Use minimal sanitized samples when needed.

## 开发与验证 / Development and validation

使用 Windows 和 [`global.json`](../global.json) 指定的 .NET SDK（当前为 10.0.401）。项目使用 WPF；Windows 11 是正式支持的系统，Windows 10 为尽力兼容。Visual Studio 是可选的，命令行也可以构建。

Use Windows and the .NET SDK selected by [`global.json`](../global.json), currently 10.0.401. This is a WPF application. Windows 11 is supported; Windows 10 is best-effort. Visual Studio is optional.

在仓库根目录运行 / Run from the repository root:

```powershell
dotnet restore Ming-AutoClicker.sln
dotnet build Ming-AutoClicker.sln -c Release --no-restore
dotnet run --project Tests/Ming-AutoClicker.Tests.csproj -c Release --no-build
```

测试入口和专项检查见 [Tests/README.md](../Tests/README.md)。默认回归测试使用模拟输入，不会操作真实鼠标和键盘；需要显示窗口的检查是单独启用的。构建文件被程序占用时，先退出正在运行的程序及托盘实例。

See [Tests/README.md](../Tests/README.md) for focused checks. Default regression tests use fake input rather than controlling your mouse or keyboard. Checks that show desktop windows are opt-in. Exit the running application, including its tray instance, if it locks build output.

## 代码和界面约定 / Code and UI conventions

- 保持改动围绕同一个问题，沿用附近代码的格式和命名。
- 视图按 `Views` 下的功能目录组织；XAML 与对应代码放在一起，共享视图放入 `Views/Shared`，公共样式放入 `Styles`。
- 用户可见文案同步更新 `Localization/Strings.zh-CN.xaml` 和 `Localization/Strings.en-US.xaml`。
- 修改界面时，检查中英文、小窗口和适用的显示缩放；PR 中附上能展示改动的截图。
- 根据行为变化运行相关检查；逻辑修复或新行为应提供有意义的回归覆盖，纯文档和简单样式修改不必新增行为测试。

- Keep each change focused and follow nearby formatting and naming.
- Group views by feature under `Views`, keeping XAML and code-behind together. Reusable views belong in `Views/Shared`; common styles belong in `Styles`.
- Update both Chinese and English localization dictionaries for user-visible text.
- For UI changes, check both languages, smaller windows and relevant display scaling, and include useful screenshots in the PR.
- Run checks appropriate to the changed behavior. Add meaningful regression coverage for logic fixes or new behavior; documentation and simple styling changes do not need new behavior tests.

## 提交 PR / Submitting a pull request

从当前 `main` 创建分支，说明问题、改动及验证结果，并关联已有 Issue（如有）。标注尚未完成的检查或已知限制。PR 面向 `main`，由维护者审阅。

Create a branch from the current `main`. Explain the problem, changes and verification, and link the related issue when there is one. State checks you could not run and any known limitations. Target `main` for maintainer review.

贡献按项目的 [GPL-3.0-only 许可证](../LICENSE)提供，贡献者保留其著作权。引入第三方代码或资源时，确认许可兼容性并保留原始许可和署名；同步更新第三方声明及所需许可附件。

Contributions are provided under the project's [GPL-3.0-only license](../LICENSE); contributors retain their copyright. When adding third-party code or assets, check license compatibility, retain their notices and attribution, and update the third-party notices and required license attachments.
