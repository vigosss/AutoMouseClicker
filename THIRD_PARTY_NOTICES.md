# 第三方声明 / Third-party notices

智点精灵由 M-ing 维护，应用自身采用 [GPL-3.0-only](LICENSE)。第三方代码、运行库、图标和字体保留各自版权与许可；本项目的许可证不会改变其原有条款。

M-ing maintains this application under [GPL-3.0-only](LICENSE). Third-party code, runtimes, icons, and fonts retain their original copyrights and license terms.

本文记录 v1.1.0 的项目依赖和 Windows x64 发布资产核对结果；发布工具链为 .NET SDK 10.0.404。发布包中的 `BUILD_INFO.json` 记录该次发布使用的包版本及原生资产；版本升级后须重新核对本文。[源码与构建信息](SOURCE_INFORMATION.md)列出了源码获取方式、原生 DLL 检查记录和尚未完成的核对事项。

This inventory covers v1.1.0 project dependencies and Windows x64 assets, with .NET SDK 10.0.404 as the release toolchain. The published `BUILD_INFO.json` records the packages and native assets used for that publication. Recheck this document after dependency updates. See [source and build information](SOURCE_INFORMATION.md) for source locations, native DLL inspection records, and unresolved verification.

## 托管依赖与运行时 / Managed dependencies and runtimes

| 组件 / Component | 已核对版本 / Inspected version | 条款与声明 / Terms and notices |
| --- | --- | --- |
| Emgu.CV, Emgu.CV.Bitmap, Emgu.CV.runtime.windows | 4.13.0.5924 | Emgu Corporation；GPLv3 / 商业双许可，本项目采用 GPLv3 路线。保留完整供应商[许可文件](licenses/Emgu.CV/LICENSE.txt)，其中也包含未在本项目选用的商业许可条款。 / GPLv3 or commercial; this project follows the GPLv3 option. The complete vendor document retains both alternatives. |
| MahApps.Metro.IconPacks.Core, MahApps.Metro.IconPacks.PixelartIcons | 6.2.1 | MIT；MahApps、Jan Karger。[许可全文](licenses/MahApps/LICENSE.txt) / MIT license text. |
| Pixelarticons 原始图标 / Original icon artwork | 随 IconPacks 6.2.1 提供；独立图标版本未标注 / Included through IconPacks 6.2.1; separate artwork revision unspecified | MIT；Copyright (c) 2019 Gerrit Halfmann。[上游许可全文](licenses/Pixelarticons/LICENSE.txt) / Upstream MIT license text. |
| Microsoft.NETCore.App.Runtime.win-x64 | 10.0.15，自包含发布 / self-contained publication | .NET Foundation and Contributors；[MIT 许可](licenses/dotnet/NETCore-LICENSE.txt)及包内[第三方声明](licenses/dotnet/NETCore-THIRD-PARTY-NOTICES.txt)。 / MIT and the runtime package's third-party notices. |
| Microsoft.WindowsDesktop.App.Runtime.win-x64 | 10.0.15，自包含发布 / self-contained publication | .NET Foundation and Contributors；包内 [MIT 许可](licenses/dotnet/WindowsDesktop-LICENSE.txt)。 / MIT license supplied in the runtime pack. |

`Microsoft.NET.ILLink.Tasks` 是构建工具，不因出现在还原结果中就视为随应用分发的运行库。自包含与依赖框架的发布内容不同，应以具体发布包为准。

`Microsoft.NET.ILLink.Tasks` is a build tool; its presence in restore output does not establish runtime distribution. Self-contained and framework-dependent publications contain different assets; inspect the actual publication.

## Windows x64 原生文件 / Native Windows x64 files

以下文件来自 NuGet 当前目标 `net10.0-windows/win-x64` 的原生资产。此表是文件清单，不能替代各文件实际适用的许可或对应源码证明。

These files are native assets of the current NuGet target `net10.0-windows/win-x64`. This is an asset inventory, not a substitute for each binary's applicable terms or corresponding-source evidence.

| 来源包 / Package | 文件 / Files | 已确认内容与缺口 / Confirmed information and gaps |
| --- | --- | --- |
| Emgu.CV.runtime.windows 4.13.0.5924 | `cvextern.dll` | 内嵌构建信息为 OpenCV `4.13.0-274-g156f87e379`、contrib `4.13.0-71-gf82c269b`。包中保留 Emgu 双许可文件；OpenCV 4.13.0 的[上游许可证](https://github.com/opencv/opencv/blob/4.13.0/LICENSE)为 Apache-2.0。完整源码、补丁、构建材料及嵌入组件仍待匹配。 / Embedded build information identifies these OpenCV/contrib revisions. The package retains the Emgu dual-license document; upstream OpenCV 4.13.0 uses Apache-2.0. Complete sources, patches, build materials, and embedded components remain to be matched. |
| Emgu.CV.runtime.windows 4.13.0.5924 | `opencv_videoio_ffmpeg4130_64.dll` | 已读取内嵌 FFmpeg `4.4.6` 及 `LGPL version 2.1 or later` 许可字符串；静态配置启用 libopenh264、libvpx、libaom。完整对应源码、补丁、各组件声明及构建材料仍待匹配，不能仅据这些字符串视为核对完成。 / Embedded strings identify FFmpeg 4.4.6 and the stated LGPL version, with static libopenh264/libvpx/libaom options. Complete source, patches, component notices, and build materials remain to be matched; these strings alone do not complete verification. |
| Emgu.CV.runtime.windows 4.13.0.5924 | `libusb-1.0.dll` | 需匹配实际 libusb 版本、上游许可与对应源码；Emgu 包级声明不能替代这项核对。 / Exact libusb revision, upstream terms, and corresponding source still require matching; the Emgu package notice does not replace this check. |
| Emgu.runtime.windows.msvc.rt.x64 19.44.35224 | `concrt140.dll`, `msvcp140.dll`, `msvcp140_1.dll`, `msvcp140_2.dll`, `msvcp140_atomic_wait.dll`, `msvcp140_codecvt_ids.dll`, `vcruntime140.dll`, `vcruntime140_1.dll` | 包内 LICENSE.txt 与 Emgu 许可文件相同；这些是 Microsoft VC++ 运行库，该包级文件不能证明 Microsoft 文件的再分发授权。适用的 Microsoft 再分发条款与原始声明仍需核对。 / Its package LICENSE.txt matches the Emgu document. These are Microsoft VC++ runtime files; that package-level document does not establish Microsoft's redistribution permission. Applicable Microsoft terms and notices still require verification. |

.NET 自包含发布也包含原生运行时文件，其清单记录在 `BUILD_INFO.json`，并保留上表中的 .NET 许可和第三方声明。

Self-contained .NET publications also contain native runtime files, inventoried in `BUILD_INFO.json`; the .NET license and third-party notices listed above are retained.

## 字体 / Fonts

应用嵌入 `fusion-pixel-12px-proportional-zh_hans.ttf`，随附以下原始许可文件。保留作者和各来源的原文，不将所有来源统一标成单一许可。

The application embeds `fusion-pixel-12px-proportional-zh_hans.ttf` and includes the following original notices. Preserve each source's authorship and terms rather than replacing them with one blanket license.

| 字体来源 / Font source | 作者与条款 / Authors and terms | 随附文件 / Included notice |
| --- | --- | --- |
| Fusion Pixel Font | Copyright (c) 2022 TakWolf；SIL OFL 1.1 | [OFL.txt](Assets/Fonts/OFL.txt) |
| Ark Pixel Font | Copyright (c) 2021 TakWolf；SIL OFL 1.1 | [ark-pixel/OFL.txt](Assets/Fonts/LICENSES/ark-pixel/OFL.txt) |
| Galmuri | Copyright (c) 2019–2025 Lee Minseo；SIL OFL 1.1 | [galmuri/LICENSE.txt](Assets/Fonts/LICENSES/galmuri/LICENSE.txt) |
| Cubic 11 / JF Dot M+H 12 / M+ Bitmap Fonts | 保留 Cubic 11 原文授权、衍生字体条款、M+ FONTS PROJECT / COZ 版权及附带 OFL 文本；文件内容包含多个来源的条款，不能仅凭文件名视为统一 OFL。 / Preserve the supplied Cubic 11 permission and derivative-font terms, M+ FONTS PROJECT / COZ copyrights, and accompanying OFL text; the filename alone does not make all included terms identical. | [cubic-11/OFL.txt](Assets/Fonts/LICENSES/cubic-11/OFL.txt) |

## 许可副本来源 / Provenance of license copies

- Emgu 原文取自 NuGet `Emgu.CV` 4.13.0.5924 的 `LICENSE.txt`；已核对其他两个直接 Emgu 包和 MSVC x64 包附带同一文本。 / The Emgu text is copied from `Emgu.CV` 4.13.0.5924; the other two direct Emgu packages and the MSVC x64 package contain the same text.
- .NET 原文取自上述两个 10.0.15 runtime pack 的许可及声明文件；没有以自行撰写的摘要替代原文。 / .NET notices are copied from the two listed 10.0.15 runtime packs, without replacing their text with a summary.
- MahApps 原文来自[固定源码提交的 LICENSE](https://github.com/MahApps/MahApps.Metro.IconPacks/blob/d7d1c4ab6202143fd83410d715d323a2dbddf320/LICENSE)。 / The MahApps license comes from the pinned source commit.
- Pixelarticons 原文来自[图标上游 LICENSE](https://github.com/halfmage/pixelarticons/blob/master/LICENSE)；此链接不是所打包图标版本的来源证明。 / The Pixelarticons license comes from its upstream repository; that moving branch link does not establish the exact bundled artwork revision.

许可遗漏或来源疑问请联系 [smellrosa@gmail.com](mailto:smellrosa@gmail.com)。 / Report missing notices or source questions to [smellrosa@gmail.com](mailto:smellrosa@gmail.com).
