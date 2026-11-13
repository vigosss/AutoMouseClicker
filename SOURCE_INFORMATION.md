# 源码与构建信息 / Source and build information

项目：[vigosss/AutoMouseClicker](https://github.com/vigosss/AutoMouseClicker)。维护者：M-ing；联系邮箱：[smellrosa@gmail.com](mailto:smellrosa@gmail.com)。应用自身采用 [GPL-3.0-only](LICENSE)，第三方条款见[第三方声明](THIRD_PARTY_NOTICES.md)。

Project: [vigosss/AutoMouseClicker](https://github.com/vigosss/AutoMouseClicker). Maintainer: M-ing, [smellrosa@gmail.com](mailto:smellrosa@gmail.com). The application uses [GPL-3.0-only](LICENSE); dependency terms are listed in [third-party notices](THIRD_PARTY_NOTICES.md).

## 确认发布版本 / Identify a publication

每次 `dotnet publish` 会在输出目录生成独立的 `BUILD_INFO.json`，记录版本、生成时间、目标运行时、依赖及原生资产。请连同可执行文件保存该文件。

Each `dotnet publish` writes a separate `BUILD_INFO.json` beside the executable, recording publication version, time, target runtime, dependencies, and native assets. Retain it with the executable.

- `sourceCommit` / `sourceTag`：正常执行构建时记录 Git 提交及匹配标签。`workingTreeDirty=true` 表示存在未提交修改，单独检出该提交不能取得该二进制的完整应用源码。 / A normal build records the Git commit and matching tag. With `workingTreeDirty=true`, checking out that commit alone does not recover all application source used for the binary.
- 源码归档 URL 仅在工作区干净，且 GitHub Actions 仓库与提交均匹配本项目时生成。本地构建即使干净，也不据此断定提交已经公开可下载。 / A source archive URL is emitted only for a clean checkout with matching repository and commit in GitHub Actions. A clean local checkout alone does not establish public availability of its commit.
- `buildReused=true` 表示使用 `--no-build` 复用构建结果；此时提交、标签和源码归档字段为空，避免把当前工作区错误地当作旧二进制的来源。 / With `--no-build`, `buildReused=true` and source commit, tag, and archive fields are null, avoiding a false match between the current checkout and reused binaries.
- 包清单包含普通依赖与 `runtimepack.*` 项；自包含发布的 .NET 运行时版本以这些记录为准。 / The package inventory includes dependencies and `runtimepack.*` entries; these record the runtime versions of a self-contained publication.

正式发布应使用干净且已公开的标签提交，保留该版本的应用源码和构建脚本。未提交的本地预览构建不能仅凭 Git 提交或 GitHub 自动生成的源码 ZIP 证明完整对应源码已经提供。

A formal release should use a clean, publicly available tagged commit and retain its application source and build scripts. A local preview with uncommitted changes cannot demonstrate complete source availability through a commit identifier or GitHub's automatic source ZIP alone.

## 从源码构建 / Build the application

在 Windows 上安装 `global.json` 允许的 .NET SDK（基线 10.0.401，允许最新功能带）。v1.1.0 的本地发布验证及 GitHub Actions 使用 SDK 10.0.404，自包含发布使用 .NET 10.0.15；工作流会核对实际选中的 SDK。然后在对应提交的仓库根目录执行：

On Windows, install an SDK allowed by `global.json` (baseline 10.0.401 with latest-feature roll-forward). Local release validation and GitHub Actions for v1.1.0 use SDK 10.0.404, with .NET 10.0.15 for self-contained publication; the workflow verifies the selected SDK. Check out the intended commit and run from the repository root:

```powershell
git clone https://github.com/vigosss/AutoMouseClicker.git
cd AutoMouseClicker
# 用 BUILD_INFO.json 中已确认的提交替换 <sourceCommit>。
# Replace <sourceCommit> with the verified commit from BUILD_INFO.json.
git checkout <sourceCommit>
dotnet restore Ming-AutoClicker.sln
dotnet build Ming-AutoClicker.sln -c Release --no-restore
dotnet run --project Tests/Ming-AutoClicker.Tests.csproj -c Release --no-build
dotnet publish Ming-AutoClicker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o bin/ReleasePackage
```

发布配置及参数以该提交的工作流和项目文件为准。以上命令会还原上游二进制 NuGet 包，不会从源码重建 Emgu、FFmpeg、libusb 或 .NET；也不保证不同工具链环境逐字节产出相同二进制。测试说明见仓库中的 [Tests/README.md](https://github.com/vigosss/AutoMouseClicker/blob/main/Tests/README.md)。

Use the workflow and project settings from the selected commit for release-specific options. These commands restore upstream binary NuGet packages; they do not rebuild Emgu, FFmpeg, libusb, or .NET from source, and do not guarantee byte-identical output across toolchains. See the repository's [Tests/README.md](https://github.com/vigosss/AutoMouseClicker/blob/main/Tests/README.md) for test details.

## 依赖源码 / Dependency source

| 依赖 / Dependency | 已确认入口 / Verified source entry | 构建与匹配说明 / Build and matching notes |
| --- | --- | --- |
| Emgu.CV 4.13.0.5924 | NuSpec 标注提交 [`800019a40abd4db90604f70aa60622dbaff088ae`](https://github.com/emgucv/emgucv/tree/800019a40abd4db90604f70aa60622dbaff088ae) | 克隆该提交后初始化递归子模块，并按该提交的上游构建说明构建。托管包的提交字段不能单独证明运行时包中所有 DLL 的来源。 / Initialize recursive submodules and follow upstream build instructions at this commit. The managed package's commit metadata alone does not establish the source of every runtime DLL. |
| MahApps.Metro.IconPacks 6.2.1 | NuSpec 标注提交 [`d7d1c4ab6202143fd83410d715d323a2dbddf320`](https://github.com/MahApps/MahApps.Metro.IconPacks/tree/d7d1c4ab6202143fd83410d715d323a2dbddf320) | 按该提交的 README、解决方案及构建脚本重建；包括 Core 与 PixelartIcons。 / Follow the README, solution, and build scripts at this commit for Core and PixelartIcons. |
| Pixelarticons 图标 / artwork | [halfmage/pixelarticons](https://github.com/halfmage/pixelarticons) | MIT 原文已保留；IconPacks 中的图标具体上游修订未标注。 / MIT text is retained; the separate artwork revision included by IconPacks is unspecified. |
| .NET 10.0.15 runtime packs | [.NET runtime 源码提交 / source commit](https://github.com/dotnet/runtime/tree/8e0f8ed2312954727f8919d7d1c2f78b483b4ee7)、[WPF](https://github.com/dotnet/wpf)、[Windows Forms](https://github.com/dotnet/winforms)、[Windows Desktop](https://github.com/dotnet/windowsdesktop) | 已读取两个 10.0.15 包的 `Microsoft.NETCore.App.versions.txt` 和 `Microsoft.WindowsDesktop.App.versions.txt`，均记录版本 10.0.15 及提交 `8e0f8ed2312954727f8919d7d1c2f78b483b4ee7`。桌面组件仍须结合上游发布构建元数据确定各仓库修订，不能将此运行时提交直接当作所有桌面仓库的提交。 / Both inspected 10.0.15 runtime-pack version files record this version and commit. Resolve individual desktop repository revisions through upstream release build metadata rather than applying one runtime commit to every repository. |
| Fusion Pixel Font 及字形来源 / and source fonts | [Fusion Pixel Font](https://github.com/TakWolf/fusion-pixel-font)、[Ark Pixel Font](https://github.com/TakWolf/ark-pixel-font)、[Galmuri](https://github.com/quiple/galmuri)、[Cubic 11](https://github.com/ACh-K/Cubic-11) | 原始声明保留在 `Assets/Fonts`。源码仓库链接不是本项目嵌入字体的确切构建修订证明。 / Original notices are retained under `Assets/Fonts`; repository links do not establish the exact revisions used to produce the embedded font. |

Emgu 源码检出示例 / Example Emgu source checkout:

```powershell
git clone https://github.com/emgucv/emgucv.git
cd emgucv
git checkout 800019a40abd4db90604f70aa60622dbaff088ae
git submodule update --init --recursive
```

须继续执行该版本上游提供的 Windows 构建步骤，并保存工具链版本、启用选项、补丁与生成脚本。上述检出命令本身不会产出与 NuGet 原生 DLL 相匹配的构建证明。

Continue with that revision's upstream Windows build instructions and retain toolchain versions, enabled options, patches, and generation scripts. The checkout commands alone do not establish a match with the native NuGet binaries.

## 已读取的原生构建信息 / Observed native build information

以下信息来自 `Emgu.CV.runtime.windows` 4.13.0.5924 中 Windows x64 DLL 的静态文件检查，未执行 DLL。记录二进制内嵌文字和文件哈希，供继续匹配源码与构建材料使用：

The following observations come from static inspection of Windows x64 DLLs in `Emgu.CV.runtime.windows` 4.13.0.5924, without executing them. Embedded strings and file hashes provide leads for matching source and build materials:

- `cvextern.dll`：内嵌 OpenCV 构建信息为 `4.13.0-274-g156f87e379`，contrib 为 `4.13.0-71-gf82c269b`；MSVC `19.44.35224.0`，构建时间 `2026-05-04T08:51:56Z`。 / Embedded build information identifies these OpenCV/contrib revisions, compiler version, and build timestamp.
- `opencv_videoio_ffmpeg4130_64.dll`：内嵌版本为 FFmpeg `4.4.6`，avcodec `58.134.100`、avformat `58.76.100`；库内许可字符串为 `LGPL version 2.1 or later`。配置包含 `--enable-static`、`--enable-libopenh264`、`--enable-libvpx` 和 `--enable-libaom`。这些记录没有单独证明所有静态组件的源码、许可声明与构建材料已齐备。 / Embedded strings identify FFmpeg 4.4.6, the listed library versions and license statement, and these static/component build options. They do not establish complete source, notices, or build materials for every linked component.
- `libusb-1.0.dll`：未发现可用的 PE 文件版本或产品版本资源，确切修订仍待核对。 / No usable PE file/product version resource was found; the exact revision remains unresolved.

| 文件 / File | SHA-256 |
| --- | --- |
| `cvextern.dll` | `d3d257d060148dcc31159025e8003b552f72e5ad549f9d36559395a80ded9b80` |
| `opencv_videoio_ffmpeg4130_64.dll` | `fcc614672159094a35815b7ea2819a648cbf86c30d90c57e8c6ba1432f594548` |
| `libusb-1.0.dll` | `d3ac64b5e8fd9fd562762e9838d698d2dd5d7c0e7d5fc286cdc5a2f0c97ee7dd` |

## 正式二进制发布前仍待完成 / Outstanding before a formal binary release

当前已保留可核实的许可原文，但尚未完成以下原生二进制核对；本文不声称已完成全部 GPL 对应源码与第三方再分发要求：

Verified license texts are retained, but the following binary checks remain open. This document does not assert that all GPL corresponding-source and third-party redistribution requirements have been satisfied:

1. 依据已读取的 OpenCV/contrib 修订信息，将 `cvextern.dll` 匹配到完整 Emgu/OpenCV 源码、子模块修订、补丁、构建配置及嵌入依赖，并保留重建所需脚本。 / Use the observed OpenCV/contrib revisions to match `cvextern.dll` to complete Emgu/OpenCV sources, submodules, patches, build configuration, embedded dependencies, and necessary build scripts.
2. 以 FFmpeg 4.4.6 及内嵌组件配置为线索，匹配 `opencv_videoio_ffmpeg4130_64.dll` 的完整对应源码、补丁及构建材料；确认 `libusb-1.0.dll` 的确切来源，补齐适用许可及对应源码。 / Use the FFmpeg 4.4.6 and component configuration evidence to match the backend to complete source, patches, and build materials; establish the exact libusb source and supply applicable notices and corresponding source.
3. 核对八个 Microsoft VC++ 运行库文件的适用再分发条款和声明。Emgu NuGet 包附带的同名许可不能替代 Microsoft 授权依据。 / Verify applicable redistribution terms and notices for all eight Microsoft VC++ runtime files; the Emgu package license is not a substitute for Microsoft's terms.
4. 在正式发布页面提供与实际二进制匹配、可持续获取的应用及所需依赖源码和构建材料。GitHub 自动生成的本仓库源码 ZIP 不包含全部第三方对应源码。 / Provide accessible application and required dependency source and build materials matching the actual binaries. GitHub's automatic ZIP of this repository does not contain all third-party corresponding source.

需要补充源码或发现遗漏，请将版本号与 `BUILD_INFO.json` 中的信息发至 [smellrosa@gmail.com](mailto:smellrosa@gmail.com)，以便维护者定位对应发布。

For source questions or omissions, send the version and relevant `BUILD_INFO.json` information to [smellrosa@gmail.com](mailto:smellrosa@gmail.com) so the maintainer can identify the publication.
