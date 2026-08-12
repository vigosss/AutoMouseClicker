<div align="center">

# 🎯 智点精灵 Ming-AutoClicker

![License](https://img.shields.io/github/license/vigosss/AutoMouseClicker)
![Stars](https://img.shields.io/github/stars/vigosss/AutoMouseClicker)
![Language](https://img.shields.io/github/languages/top/vigosss/AutoMouseClicker)
![.NET](https://img.shields.io/badge/.NET-8.0-blue)

**智能鼠标自动化工具 | Smart Mouse Automation Tool**

一款基于 WPF + OpenCV 的 Windows 桌面自动化工具，支持鼠标连点、宏录制编辑、图像识别找图点击，适用于游戏辅助、重复操作自动化、UI 测试等场景。

A Windows desktop automation tool built with WPF + OpenCV, featuring auto-clicking, macro editing, image recognition & template matching — ideal for game assistance, repetitive task automation, and UI testing.

</div>

---

## ✨ 功能亮点 Features

### 🖱️ 鼠标连点 Auto Clicker
- 支持左键、中键、右键点击
- 可自定义点击间隔（10ms ~ 60000ms）
- 实时显示点击次数统计
- 全局热键 `F8` 一键启停

### 📋 宏编辑器 Macro Editor
- 可视化宏配置管理（创建、编辑、复制、删除）
- 支持多动作编排，拖拽排序
- 循环执行（可配置循环次数和间隔）
- JSON 持久化存储，原子写入防止数据损坏

### 🔍 图像识别找图 Image Recognition
- 基于 **Emgu.CV (OpenCV)** 高性能模板匹配引擎
- 缓存模板、灰度图和缩放结果，模板文件变化后自动失效
- 默认仅搜索原尺寸和 ±5%，可按步骤启用 0.5x ~ 2.0x 自适应缩放
- 灰度单轮匹配，低纹理模板自动切换为差异匹配
- 可调节匹配度阈值（默认 80%）
- 严格使用设置的阈值，不会自动降低阈值误判成功
- 支持全屏 / 区域截图
- 「等待直到找到」模式（自动轮询直至目标出现）
- 支持多显示器虚拟桌面和负坐标
- 可视化展示最佳候选、分数、尺度、耗时和失败原因

### 📐 区域截图 Region Capture
- 全屏覆盖式截图工具
- 拖拽选取目标区域
- 8 个调整手柄精调选区
- 实时显示选区尺寸信息

### 🎨 现代化界面 Modern UI
- WPF 自定义主题（卡片式设计语言）
- 统一的色彩系统和排版规范
- 响应式布局，窗口可自由缩放
- 底部状态栏实时反馈运行状态

---

## 📸 截图预览 Screenshots

> 📌 截图将展示主界面、宏编辑器、区域截图工具和匹配结果

| 鼠标连点 | 宏列表管理 |
|:---:|:---:|
| *Auto Clicker View* | *Macro List View* |

| 宏编辑器 | 区域截图工具 |
|:---:|:---:|
| *Macro Editor View* | *Region Select Window* |

---

## 📖 使用指南 Usage

### 鼠标连点模式

1. 启动程序，默认进入「🖱 鼠标连点」选项卡
2. 选择点击类型（左键 / 中键 / 右键）
3. 设置点击间隔（单位：毫秒）
4. 将鼠标移至目标位置
5. 按 `F8` 键开始连点，再次按 `F8` 停止

### 宏编辑模式

1. 切换到「📋 鼠标宏」选项卡
2. 点击「＋ 创建新宏」
3. 点击「✏️ 编辑」进入宏编辑器
4. 添加动作（🔍 找图 / ⏱ 等待）
5. 配置动作参数后点击「💾 保存」
6. 选中宏后点击「▶ 开始」或按 `F8` 运行

### 找图动作配置

| 参数 | 说明 |
|------|------|
| 截图 | 点击「截图」按钮，拖拽选取目标图像区域 |
| 匹配度 | 模板匹配阈值，默认 80%（越高越严格） |
| 操作 | 找到后执行的操作（左键点击 / 右键点击） |
| X/Y 偏移 | 点击位置相对于匹配中心的偏移量 |
| 等待直到找到 | 启用后会持续搜索直到目标出现或超时 |

### 快捷键

| 快捷键 | 功能 |
|--------|------|
| `F8` | 开始 / 停止（连点或宏执行） |
| `ESC` | 关闭截图窗口 / 匹配结果窗口 |


## 📋 更新日志 Changelog

### v0.3.2
- 🐛 修复第二次搜索反而更慢且找不到的问题（移除不稳定的 ORB 验证）
- 🚀 默认找图缩减为 3 个尺度，自适应缩放按步骤启用
- 💾 缓存模板及缩放结果，严格执行用户设置的阈值
- 📊 测试匹配展示最佳候选、尺度、耗时和失败原因
- 🖥️ 修复多显示器负坐标下的截图、匹配与点击换算

---

## 📄 许可证 License

本项目基于 [MIT License](LICENSE) 开源。

---

## 🔑 关键词 Keywords

<!-- SEO: 帮助 GitHub 搜索和推荐 -->

`auto-clicker` `mouse-automation` `macro-recorder` `image-recognition` `template-matching` `opencv` `emgu-cv` `wpf` `dotnet` `csharp` `windows-desktop` `screen-capture` `automation-tool` `mouse-macro` `game-assistant` `ui-testing` `desktop-automation`

`自动连点器` `鼠标宏` `图像识别` `找图点击` `屏幕自动化` `自动化工具` `WPF工具`

---

<div align="center">

**如果这个项目对你有帮助，请给个 ⭐ Star 支持一下！**

Made with ❤️ by [vigosss](https://github.com/vigosss)

</div>
