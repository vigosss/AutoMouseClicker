# Ming-AutoClicker 项目文档

## 项目概述

这是一个 Windows 桌面应用程序，用于创建和执行鼠标宏操作。

### 核心功能
- 创建、编辑、保存鼠标宏
- 支持"找图"和"等待"两种宏动作
- 图像识别并自动点击
- 通过 F8 热键控制宏的启动和停止

### 技术栈
- **UI框架**: WPF (XAML + MVVM)
- **图像识别**: Emgu.CV 4.8.1 (OpenCV 的 C# 封装)
- **数据存储**: JSON 文件
- **全局热键**: Win32 API (RegisterHotKey)
- **目标框架**: .NET 6.0 或更高

## 项目结构

```
Ming-AutoClicker/                     # 项目根目录
├── .vscode/                          # VSCode 配置
│   ├── launch.json                   # 调试配置
│   ├── tasks.json                    # 构建任务
│   └── settings.json                 # 工作区设置
├── CLAUDE.md                         # 本文档
├── DEVELOPMENT.md                    # 开发流程
├── LEARNING.md                       # 学习指南
├── TODO.md                           # 任务清单
├── .gitignore                        # Git 忽略文件
├── Ming-AutoClicker.sln              # 解决方案文件
├── Ming-AutoClicker.csproj           # 项目文件
├── App.xaml                          # 应用入口
├── App.xaml.cs
├── MainWindow.xaml                   # 主窗口
├── MainWindow.xaml.cs
├── Models/                           # 数据模型
│   ├── MacroAction.cs
│   ├── FindImageAction.cs
│   ├── WaitAction.cs
│   └── MacroProfile.cs
├── ViewModels/                       # MVVM 视图模型
│   ├── ViewModelBase.cs
│   ├── MainViewModel.cs
│   └── MacroEditorViewModel.cs
├── Views/                            # 视图
│   ├── MacroListView.xaml
│   └── MacroEditorView.xaml
├── Services/                         # 服务层
│   ├── MacroStorageService.cs
│   ├── ImageMatchService.cs
│   ├── ScreenCaptureService.cs
│   ├── MacroExecutor.cs
│   └── HotkeyService.cs
├── Helpers/                          # 辅助类
│   ├── RelayCommand.cs
│   └── Win32Api.cs
└── Data/                             # 数据目录（运行时生成）
    ├── macros.json                   # 宏配置
    └── screenshots/                  # 截图存储
```

**说明**：
- 扁平化结构，所有源代码文件直接在项目根目录
- 不需要嵌套的 `Ming-AutoClicker/Ming-AutoClicker/` 结构
- 添加 `.vscode/` 目录用于 VSCode 调试和构建配置

## 开发原则

### DRY (Don't Repeat Yourself)
1. **共享基类**: 所有宏动作继承自 `MacroAction` 基类
2. **服务单例**: 所有 Service 使用单例模式，避免重复实例化
3. **MVVM 基类**: ViewModelBase 提供 INotifyPropertyChanged 实现
4. **通用命令**: RelayCommand 复用于所有命令绑定
5. **Win32 封装**: Win32Api 类统一封装所有 P/Invoke 调用

### 最小化原则
- 只实现必需功能，不添加额外特性
- UI 简洁实用，不过度设计
- 代码精简，避免过度抽象

## 核心组件说明

### 1. 数据模型 (Models/)

**MacroAction.cs** - 所有动作的基类
```csharp
public abstract class MacroAction
{
    public int Order { get; set; }
    public abstract ActionType Type { get; }
}
```

**FindImageAction.cs** - 找图动作
- ImagePath: 截图文件路径
- MatchThreshold: 匹配度 (0-1)
- WaitUntilFound: 是否循环等待直到找到
- Operation: 操作类型 (目前只有 "Click")

**WaitAction.cs** - 等待动作
- WaitSeconds: 等待时间（秒）

**MacroProfile.cs** - 宏配置文件
- Id: 唯一标识
- Name: 宏名称
- Actions: 动作列表

### 2. 服务层 (Services/)

**MacroStorageService** - 数据持久化
- 使用 `System.Text.Json` 序列化
- 保存到 `Data/macros.json`
- 截图保存到 `Data/screenshots/`

**ImageMatchService** - 图像匹配
- 使用 Emgu.CV 的 `MatchTemplate` 方法
- 返回匹配位置和相似度

**ScreenCaptureService** - 屏幕截图
- 使用 `Graphics.CopyFromScreen()`
- 支持全屏和区域截图

**MacroExecutor** - 宏执行器
- 后台线程执行动作序列
- 支持启动/停止控制
- 使用 `mouse_event` 模拟鼠标点击

**HotkeyService** - 全局热键
- F8: 切换宏执行（开始/停止）
- 使用 `RegisterHotKey` API

### 3. UI 设计

**主窗口布局**
```
┌─────────────────────────────────────┐
│ [鼠标宏]                            │
├─────────────────────────────────────┤
│ 宏列表:                             │
│ ┌─────────────────────────────────┐ │
│ │ □ 宏1  [编辑] [删除]            │ │
│ │ ☑ 宏2  [编辑] [删除]            │ │
│ └─────────────────────────────────┘ │
│ [创建新宏]                          │
│                                     │
│ 提示: 按 F8 开始/停止运行          │
└─────────────────────────────────────┘
```

**宏编辑器布局**
```
┌─────────────────────────────────────┐
│ 宏名称: [____________]              │
├──────────────┬──────────────────────┤
│ 动作列表     │ 动作设置             │
│              │                      │
│ 1. 找图      │ 选择动作: [找图 ▼]  │
│    [↑][↓][×]│                      │
│              │ [截图] [清除] [测试] │
│ 2. 等待      │ 匹配度: [====] 80%   │
│    [↑][↓][×]│ 操作: [点击 ▼]      │
│              │ □ 直到找到           │
│              │ [添加动作]           │
├──────────────┴──────────────────────┤
│              [取消] [保存]          │
└─────────────────────────────────────┘
```

## NuGet 依赖包

```xml
<PackageReference Include="Emgu.CV" Version="4.8.1.5350" />
<PackageReference Include="Emgu.CV.runtime.windows" Version="4.8.1.5350" />
```

## 开发流程

详细的开发流程、打包说明和命令请查看 `DEVELOPMENT.md`

**边学边做**: 查看 `LEARNING.md` 获取每个阶段的学习要点和参考资料

任务清单通过 TODO 列表跟踪（使用 TodoWrite 工具查看）

## 注意事项

1. **管理员权限**: 全局热键和鼠标模拟可能需要管理员权限运行
2. **线程安全**: 宏执行在后台线程，UI 更新需使用 `Dispatcher.Invoke`
3. **资源释放**: Bitmap 对象使用后需 `Dispose()`
4. **异常处理**: 图像匹配失败、文件 IO 失败需友好提示
5. **路径处理**: 使用相对路径存储截图，确保可移植性

## 测试方案

### 单元测试
- 图像匹配准确性
- JSON 序列化/反序列化
- 动作排序逻辑

### 集成测试
1. 创建包含"找图→等待→点击"的宏
2. 保存并重新加载
3. 使用 F8 启动和停止，验证切换功能

## 常见问题

**Q: 图像匹配找不到目标？**
A: 降低匹配度阈值，或重新截图确保图像清晰

**Q: 热键不响应？**
A: 检查是否以管理员权限运行，或热键被其他程序占用

**Q: 宏执行卡顿？**
A: 检查"等待"时间设置，避免过短导致 CPU 占用过高
