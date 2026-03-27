# 开发流程

## 阶段 0: VSCode 环境准备

### 0.1 安装必需扩展
- C# Dev Kit (Microsoft)
- .NET Install Tool (Microsoft)

### 0.2 安装 .NET SDK
```bash
# 检查是否已安装
dotnet --version

# 如果未安装，访问 https://dotnet.microsoft.com/download
```

---

## 阶段 1: 项目初始化

### 1.1 创建 WPF 项目（扁平结构）
```bash
# 在项目根目录执行
dotnet new sln -n Ming-AutoClicker
dotnet new wpf -n Ming-AutoClicker -f net6.0-windows
dotnet sln add Ming-AutoClicker.csproj
```

### 1.2 添加 NuGet 包
```bash
dotnet add package Emgu.CV --version 4.8.1.5350
dotnet add package Emgu.CV.runtime.windows --version 4.8.1.5350
```

### 1.3 创建目录结构
```bash
mkdir Models ViewModels Views Services Helpers Data
mkdir Data/screenshots
```

### 1.4 配置 VSCode 调试
创建 `.vscode/launch.json`:
```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": ".NET Core Launch (WPF)",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/bin/Debug/net6.0-windows/Ming-AutoClicker.dll",
      "args": [],
      "cwd": "${workspaceFolder}",
      "console": "internalConsole",
      "stopAtEntry": false
    }
  ]
}
```

创建 `.vscode/tasks.json`:
```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "build",
      "command": "dotnet",
      "type": "process",
      "args": ["build", "${workspaceFolder}/Ming-AutoClicker.csproj"],
      "problemMatcher": "$msCompile"
    }
  ]
}
```

---

## 阶段 2: 基础设施层

### 2.1 实现 Helpers
- `Win32Api.cs` - P/Invoke 封装（热键、鼠标事件）
- `RelayCommand.cs` - MVVM 命令实现

### 2.2 实现 Models
- `MacroAction.cs` - 基类
- `FindImageAction.cs` - 找图动作
- `WaitAction.cs` - 等待动作
- `MacroProfile.cs` - 宏配置

---

## 阶段 3: 服务层（按依赖顺序）

### 3.1 独立服务（无依赖）
- `MacroStorageService.cs` - JSON 读写
- `ScreenCaptureService.cs` - 屏幕截图

### 3.2 依赖服务
- `ImageMatchService.cs` - 图像匹配（依赖 Emgu.CV）
- `HotkeyService.cs` - 全局热键（依赖 Win32Api）

### 3.3 核心服务
- `MacroExecutor.cs` - 宏执行器（依赖所有服务）

---

## 阶段 4: MVVM 层

### 4.1 ViewModel 基类
- `ViewModelBase.cs` - INotifyPropertyChanged 实现

### 4.2 业务 ViewModel
- `MainViewModel.cs` - 主窗口逻辑
- `MacroEditorViewModel.cs` - 编辑器逻辑

---

## 阶段 5: UI 层

### 5.1 主窗口
- `MainWindow.xaml` - 主窗口布局
- `MainWindow.xaml.cs` - 代码后置

### 5.2 用户控件
- `MacroListView.xaml` - 宏列表视图
- `MacroEditorView.xaml` - 宏编辑器视图

---

## 阶段 6: 测试与调试

### 6.1 功能测试
- 创建简单宏
- 保存和加载
- 图像匹配测试
- 热键响应测试

### 6.2 集成测试
- 完整流程测试
- 异常处理测试

---

## 阶段 7: 打包发布

### 7.1 发布配置
编辑 `Ming-AutoClicker.csproj`，添加：
```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net6.0-windows</TargetFramework>
  <UseWPF>true</UseWPF>
  <PublishSingleFile>true</PublishSingleFile>
  <SelfContained>true</SelfContained>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <PublishReadyToRun>true</PublishReadyToRun>
  <ApplicationIcon>icon.ico</ApplicationIcon>
</PropertyGroup>
```

### 7.2 打包命令

**方式 1: 单文件 EXE（推荐）**
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
输出位置: `bin/Release/net6.0-windows/win-x64/publish/Ming-AutoClicker.exe`

**方式 2: 框架依赖（体积小）**
```bash
dotnet publish -c Release -r win-x64 --self-contained false
```
需要用户安装 .NET 6.0 运行时

**方式 3: 使用 ClickOnce 安装程序**
```bash
dotnet publish -c Release -r win-x64 -p:PublishProtocol=ClickOnce
```

### 7.3 打包优化

**减小体积**
```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=true \
  -p:TrimMode=link
```

**添加应用图标**
1. 准备 `icon.ico` 文件
2. 放在项目根目录
3. 在 `.csproj` 中添加 `<ApplicationIcon>icon.ico</ApplicationIcon>`

### 7.4 创建安装程序（可选）

使用 Inno Setup 或 WiX Toolset 创建安装程序：

**Inno Setup 脚本示例**
```ini
[Setup]
AppName=Ming-AutoClicker
AppVersion=1.0
DefaultDirName={pf}\Ming-AutoClicker
OutputDir=installer
OutputBaseFilename=Ming-AutoClicker-Setup

[Files]
Source: "bin\Release\net6.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: recursesubdirs

[Icons]
Name: "{commondesktop}\Ming-AutoClicker"; Filename: "{app}\Ming-AutoClicker.exe"
```

---

## 开发检查清单

- [ ] 项目结构创建完成
- [ ] NuGet 包安装成功
- [ ] Models 层实现完成
- [ ] Helpers 层实现完成
- [ ] Services 层实现完成
- [ ] ViewModels 层实现完成
- [ ] Views 层实现完成
- [ ] 功能测试通过
- [ ] 打包测试通过
- [ ] 生成最终 EXE 文件

---

## 常用命令

```bash
# 构建项目
dotnet build

# 运行项目
dotnet run

# 清理输出
dotnet clean

# 恢复依赖
dotnet restore

# 发布 Release 版本
dotnet publish -c Release
```

---

## 注意事项

1. **管理员权限**: 打包后的 EXE 可能需要以管理员身份运行（热键和鼠标模拟）
2. **依赖文件**: 如果使用单文件发布，Emgu.CV 的原生 DLL 会自动包含
3. **杀毒软件**: 鼠标模拟功能可能被杀毒软件拦截，需要添加白名单
4. **测试环境**: 在干净的 Windows 环境测试打包后的 EXE
