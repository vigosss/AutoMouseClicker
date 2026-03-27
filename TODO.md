# 开发任务清单

## 阶段 1: 项目初始化 ✅

- [x] 创建 WPF 项目结构和解决方案文件
- [x] 添加 NuGet 包依赖 (Emgu.CV)
- [x] 创建目录结构 (Models, ViewModels, Views, Services, Helpers, Data)

---

## 阶段 2: 数据模型层 ✅

- [x] 实现 `Models/MacroAction.cs` - 宏动作基类
- [x] 实现 `Models/FindImageAction.cs` - 找图动作
- [x] 实现 `Models/WaitAction.cs` - 等待动作
- [x] 实现 `Models/MacroProfile.cs` - 宏配置文件

---

## 阶段 3: 辅助类层 ✅

- [x] 实现 `Helpers/Win32Api.cs` - P/Invoke 封装
- [x] 实现 `Helpers/RelayCommand.cs` - MVVM 命令

---

## 阶段 4: 服务层

### 4.1 独立服务
- [x] 实现 `Services/MacroStorageService.cs` - JSON 读写
- [x] 实现 `Services/ScreenCaptureService.cs` - 屏幕截图

### 4.2 依赖服务
- [x] 实现 `Services/ImageMatchService.cs` - 图像匹配
- [x] 实现 `Services/HotkeyService.cs` - F8 热键切换

### 4.3 核心服务
- [x] 实现 `Services/MacroExecutor.cs` - 宏执行器

---

## 阶段 5: ViewModel 层 ✅

- [x] 实现 `ViewModels/ViewModelBase.cs` - MVVM 基类
- [x] 实现 `ViewModels/MainViewModel.cs` - 主窗口逻辑
- [x] 实现 `ViewModels/MacroEditorViewModel.cs` - 编辑器逻辑

---

## 阶段 6: UI 层 ✅

- [x] 实现 `MainWindow.xaml` - 主窗口和 ContentControl 视图切换
- [x] 实现 `MainWindow.xaml.cs` - 热键注册与视图切换逻辑
- [x] 修改 `App.xaml.cs` - 服务初始化与 ViewModel 注入
- [x] 实现 `Views/MacroListView.xaml` - 宏列表视图
- [x] 实现 `Views/MacroListView.xaml.cs` - 列表事件处理
- [x] 实现 `Views/MacroEditorView.xaml` - 宏编辑器视图
- [x] 实现 `Views/MacroEditorView.xaml.cs` - 编辑器事件处理

---

## 阶段 7: 集成测试

- [ ] 测试创建和保存宏
- [ ] 测试图像匹配功能
- [ ] 测试 F8 热键响应
- [ ] 测试宏执行流程

---

## 阶段 8: 打包发布

- [ ] 配置发布选项
- [ ] 生成单文件 EXE
- [ ] 测试打包后的程序

---

## 当前进度

**当前阶段**: 阶段 7 - 集成测试
**下一步**: 测试创建和保存宏

---

## 注意事项

- 每完成一个任务，在前面的 `[ ]` 中打 `x` 标记为 `[x]`
- 遇到问题记录在对应任务下方
- 按顺序完成，避免跳过依赖项
