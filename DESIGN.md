# Translater - Windows 11 翻译器设计方案

> WinUI 3 全新 UI + 复用 STranslate 业务逻辑，聚焦中英翻译 + OCR 截屏翻译

---

## 1. 项目概述

| 属性 | 详情 |
|------|------|
| **应用名称** | Translater |
| **目标平台** | Windows 11 (兼容 Windows 10 1809+) |
| **UI 框架** | WinUI 3 (Windows App SDK 2.0) |
| **业务逻辑参考** | [STranslate](https://github.com/STranslate/STranslate) (MIT) |
| **语言** | C# / .NET 8+ |
| **架构模式** | MVVM (MVVM Toolkit) |
| **部署方式** | MSIX 打包 |

### 设计策略

```
┌─────────────────────────────────────────────┐
│           全新构建 (WinUI 3)                 │
│  UI 层：NavigationView / Mica / Fluent      │
│  ViewModel 层：重新编写，适配 WinUI          │
├─────────────────────────────────────────────┤
│        参考/复用 STranslate 代码             │
│  翻译服务：多源翻译调度、API 对接逻辑        │
│  OCR 服务：截屏处理、文字识别流程            │
│  基础设施：HTTP 封装、热键管理、本地存储      │
└─────────────────────────────────────────────┘
```

- **UI 层**：用 WinUI 3 全新编写，不复用 STranslate 的 WPF XAML
- **业务逻辑层**：参考 STranslate 的服务实现，按需移植核心代码（翻译、OCR、截屏）
- **功能范围**：初期只实现核心功能子集，后续按需扩展 STranslate 已有能力

### 核心功能（精简）

| 功能 | 描述 |
|------|------|
| **中英互译** | 输入文本，自动检测语言，中→英 / 英→中 翻译 |
| **OCR 文字提取** | 截屏框选区域 → 识别文字 → 展示提取结果 |
| **截屏翻译** | 截屏框选 → OCR 提取 → 自动翻译，一步到位 |
| **翻译历史** | 本地保存历史记录，可查看/复制/删除 |

---

## 2. UI 设计

### 2.1 主界面布局（NavigationView 导航）

```
┌──────────────────────────────────────────────────────┐
│  🌐 Translater                   ─   □   ×          │
│  ══════════════════════════════════════════ Mica 背景  │
├────────┬─────────────────────────────────────────────┤
│        │                                             │
│  🔤    │  ┌───────────────────────────────────────┐  │
│  翻译   │  │  中文  ⇄  English     [自动检测]     │  │
│        │  ├───────────────────────────────────────┤  │
│  📷    │  │                                       │  │
│  截屏   │  │  输入或粘贴文本...                     │  │
│  翻译   │  │                                       │  │
│        │  │                         [📋] [翻译]   │  │
│  📋    │  ├───────────────────────────────────────┤  │
│  历史   │  │  翻译结果                             │  │
│        │  │  ───────────────────────              │  │
│  ──    │  │  Translation result here...           │  │
│  ⚙     │  │                                       │  │
│  设置   │  │                    [复制] [朗读] [收藏] │  │
│        │  └───────────────────────────────────────┘  │
│        │                                             │
└────────┴─────────────────────────────────────────────┘
```

### 2.2 截屏翻译流程

```
快捷键触发 (默认 Alt+D，可在设置中自定义)
        │
        ▼
┌──────────────────┐
│  全屏半透明遮罩    │  ← 用户鼠标框选区域
│  ┌────────┐      │
│  │ 选中区域 │      │
│  └────────┘      │
└──────────────────┘
        │
        ▼
┌──────────────────┐
│  OCR 识别文字     │  ← Windows.Media.Ocr
└──────────────────┘
        │
        ▼
┌──────────────────┐
│  自动翻译并展示    │  ← 弹出结果窗口
│  ┌──────────────┐│
│  │原文: Hello   ││
│  │译文: 你好    ││
│  │    [复制] [关闭]│
│  └──────────────┘│
└──────────────────┘
```

### 2.3 Win11 风格

- **Mica 背景**：主窗口使用 Mica 材质（WinUI 3 原生支持）
- **圆角控件**：按钮、输入框、卡片均为圆角
- **暗色/亮色主题**：跟随系统主题自动切换
- **Fluent 图标**：使用 Segoe Fluent Icons 字体

---

## 3. 技术架构

```
┌──────────────────────────────────────────────┐
│               Translater.App                  │  ← 全新 WinUI 3
│            WinUI 3 / XAML / C#               │
│  ┌──────────┬──────────┬───────────────────┐ │
│  │ 翻译页面  │ 截屏翻译  │  历史 / 设置      │ │
│  └──────────┴──────────┴───────────────────┘ │
├──────────────────────────────────────────────┤
│             Translater.Core                   │  ← 参考 STranslate
│          业务逻辑层 (类库项目)                  │     移植核心逻辑
│  ┌──────────┬──────────┬───────────────────┐ │
│  │翻译服务   │ OCR 服务  │  历史记录服务      │ │
│  │ITranslator│ IOcrEngine│ IHistoryStore    │ │
│  └──────────┴──────────┴───────────────────┘ │
├──────────────────────────────────────────────┤
│          Translater.Infrastructure            │  ← 参考 STranslate
│            基础设施 (类库项目)                  │     复用/移植实现
│  ┌──────────┬──────────┬───────────────────┐ │
│  │HTTP 客户端│ 截屏管理  │  本地存储          │ │
│  │HttpClient │ScreenCap │  SQLite/LiteDB   │ │
│  └──────────┴──────────┴───────────────────┘ │
└──────────────────────────────────────────────┘
```

### 3.0 STranslate 代码复用计划

| STranslate 模块 | 复用方式 | 说明 |
|-----------------|---------|------|
| 翻译服务 (多源调度) | **直接移植** | 核心翻译逻辑、API 对接代码 |
| OCR 引擎 (WinOCR) | **移植 + 适配** | 识别逻辑复用，图像输入适配 WinUI |
| 截屏框选 | **参考重写** | WPF 窗口→Win32 窗口，交互逻辑参考 |
| 热键管理 | **直接复用** | P/Invoke 代码与 UI 框架无关 |
| HTTP 封装 | **直接复用** | HttpClient 封装与 UI 框架无关 |
| 语言检测 | **直接复用** | 纯逻辑代码 |
| 本地存储/配置 | **移植 + 简化** | 只保留翻译历史、快捷键等设置 |
| WPF UI (Views/Styles) | **不复用** | 用 WinUI 3 全新编写 |
| 插件系统 | **暂不复用** | 初期不需要，后续按需引入 |
| TTS/生词本/AI助手 | **暂不复用** | 初期精简，后续可逐步添加 |

### 3.1 关键技术选型

| 组件 | 技术方案 | 说明 |
|------|---------|------|
| **UI 框架** | WinUI 3 (Windows App SDK 2.0) | 微软主推，原生 Fluent Design |
| **MVVM 框架** | CommunityToolkit.Mvvm | 微软官方 MVVM 工具包 |
| **翻译 API** | 多源支持（见下方 3.2） | 接口抽象，可切换 |
| **OCR 引擎** | Windows.Media.Ocr | Win10/11 内置，零依赖 |
| **截屏** | Windows.Graphics.Capture | WinRT 截屏 API |
| **本地存储** | LiteDB (NoSQL) 或 SQLite | 轻量嵌入式数据库 |
| **依赖注入** | Microsoft.Extensions.DI | .NET 标准 DI 容器 |
| **HTTP** | HttpClient + System.Text.Json | 标准库，无额外依赖 |
| **热键** | Win32 RegisterHotKey (P/Invoke) | 全局快捷键 |
| **导航** | WinUI NavigationView | 侧边栏导航 |

### 3.2 翻译服务（优先级排序）

| 翻译源 | 类型 | 免费额度 | 优先级 |
|--------|------|---------|--------|
| **Azure Translator** | 微软官方 | 200万字符/月免费 | ⭐ 首选 |
| **Google Translate** | 非官方 API | 无限（网页抓取） | 备选 |
| **DeepL API Free** | 官方 | 50万字符/月免费 | 备选 |
| **LibreTranslate** | 自托管开源 | 无限（本地部署） | 离线备选 |

### 3.3 OCR 方案

| 引擎 | 优势 | 劣势 |
|------|------|------|
| **Windows.Media.Ocr** (首选) | 系统内置、零依赖、支持中英文 | 精度一般 |
| **Tesseract OCR** (备选) | 开源、精度高 | 需安装训练数据，包体积大 |

---

## 4. 项目结构

```
Translater/
├── src/
│   ├── Translater.sln                      # 解决方案文件
│   │
│   ├── Translater.App/                     # WinUI 3 主程序 (Packaged)
│   │   ├── App.xaml(.cs)                   # 应用入口
│   │   ├── MainWindow.xaml(.cs)            # 主窗口 (NavigationView)
│   │   ├── Views/                          # 页面
│   │   │   ├── TranslatePage.xaml(.cs)     # 翻译页
│   │   │   ├── ScreenCapturePage.xaml(.cs) # 截屏翻译页
│   │   │   ├── HistoryPage.xaml(.cs)       # 历史记录页
│   │   │   └── SettingsPage.xaml(.cs)      # 设置页
│   │   ├── ViewModels/                     # ViewModel
│   │   │   ├── TranslateViewModel.cs
│   │   │   ├── ScreenCaptureViewModel.cs
│   │   │   ├── HistoryViewModel.cs
│   │   │   └── SettingsViewModel.cs
│   │   ├── Controls/                       # 自定义控件
│   │   │   └── ScreenOverlayWindow.cs      # 截屏遮罩窗口
│   │   ├── Helpers/
│   │   │   ├── HotKeyManager.cs            # 全局热键管理
│   │   │   └── ThemeHelper.cs              # 主题切换
│   │   ├── Assets/                         # 图标/图片资源
│   │   ├── Strings/                        # 本地化字符串
│   │   │   ├── en-us/Resources.resw
│   │   │   └── zh-cn/Resources.resw
│   │   └── Package.appxmanifest            # MSIX 清单
│   │
│   ├── Translater.Core/                    # 核心业务逻辑 (类库)
│   │   ├── Interfaces/
│   │   │   ├── ITranslationService.cs      # 翻译服务接口
│   │   │   ├── IOcrService.cs              # OCR 服务接口
│   │   │   └── IHistoryService.cs          # 历史记录接口
│   │   ├── Models/
│   │   │   ├── TranslationResult.cs        # 翻译结果模型
│   │   │   ├── OcrResult.cs                # OCR 结果模型
│   │   │   ├── HistoryItem.cs              # 历史记录模型
│   │   │   └── Language.cs                 # 语言枚举
│   │   └── Services/
│   │       ├── TranslationService.cs       # 翻译调度服务
│   │       └── LanguageDetector.cs         # 语言检测
│   │
│   └── Translater.Infrastructure/          # 基础设施 (类库)
│       ├── Translators/                    # 翻译源实现
│       │   ├── AzureTranslator.cs
│       │   ├── GoogleTranslator.cs
│       │   └── DeepLTranslator.cs
│       ├── Ocr/
│       │   └── WindowsOcrEngine.cs         # Windows 内置 OCR
│       ├── ScreenCapture/
│       │   └── ScreenCaptureService.cs     # 截屏实现
│       └── Storage/
│           └── LiteDbHistoryStore.cs       # 本地历史存储
│
├── tests/
│   └── Translater.Core.Tests/             # 单元测试
│
├── DESIGN.md
└── README.md
```

---

## 5. 核心接口设计

### 5.1 翻译服务

```csharp
public interface ITranslationService
{
    Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLanguage,  // "auto" / "zh" / "en"
        string targetLanguage,
        CancellationToken ct = default);
}

public record TranslationResult(
    string OriginalText,
    string TranslatedText,
    string DetectedLanguage,
    string SourceName);        // "Azure" / "DeepL" / ...
```

### 5.2 OCR 服务

```csharp
public interface IOcrService
{
    Task<OcrResult> RecognizeAsync(
        SoftwareBitmap image,
        string language,       // "zh-Hans" / "en"
        CancellationToken ct = default);
}

public record OcrResult(
    string Text,
    IReadOnlyList<OcrLine> Lines);

public record OcrLine(string Text, Rect BoundingRect);
```

### 5.3 历史服务

```csharp
public interface IHistoryService
{
    Task SaveAsync(HistoryItem item);
    Task<IReadOnlyList<HistoryItem>> GetRecentAsync(int count = 50);
    Task DeleteAsync(string id);
    Task ClearAllAsync();
}
```

---

## 6. 关键实现要点

### 6.1 全局热键（P/Invoke，用户可在设置页面自定义）

```csharp
// 默认 Alt+D 触发截屏翻译（可在设置中修改）
[DllImport("user32.dll")]
static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

// MOD_ALT = 0x0001, VK_D = 0x44
RegisterHotKey(hwnd, HOTKEY_ID, 0x0001, 0x44);
```

### 6.2 Windows 内置 OCR

```csharp
var ocrEngine = OcrEngine.TryCreateFromLanguage(
    new Language("zh-Hans"));  // 或 "en"

var result = await ocrEngine.RecognizeAsync(softwareBitmap);
string text = result.Text;  // 提取的文字
```

### 6.3 截屏（Windows.Graphics.Capture）

```csharp
var picker = new GraphicsCapturePicker();
var item = await picker.PickSingleItemAsync();
// 使用 Direct3D11CaptureFramePool 捕获帧
```

---

## 7. NuGet 依赖

```xml
<!-- 主程序 Translater.App -->
<PackageReference Include="Microsoft.WindowsAppSDK" Version="2.0.*" />
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />
<PackageReference Include="CommunityToolkit.WinUI.Controls" Version="8.*" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.*" />

<!-- 基础设施 Translater.Infrastructure -->
<PackageReference Include="LiteDB" Version="5.*" />
<PackageReference Include="System.Text.Json" Version="8.*" />
```

---

## 8. 开发计划

### Phase 1 - 项目骨架

1. 克隆 STranslate 到本地作为参考代码库
2. 用 VS 创建 WinUI 3 Blank App (Packaged) 项目
3. 搭建三层项目结构 (App / Core / Infrastructure)
4. 实现 NavigationView 主界面 + 4 个页面骨架
5. 配置 Mica 背景 + 暗色/亮色主题

### Phase 2 - 中英翻译

1. 从 STranslate 移植翻译服务核心逻辑（接口定义 + 翻译源实现）
2. 移植 HTTP 封装、语言检测等基础代码
3. 翻译页面 UI（输入框、语言切换、翻译按钮、结果展示）
4. 复制结果到剪贴板

### Phase 3 - OCR 截屏翻译

1. 从 STranslate 移植 OCR 引擎对接代码
2. 参考 STranslate 截屏逻辑，用 Win32 窗口实现截屏遮罩
3. OCR → 自动翻译流程串联
4. 移植热键管理代码，注册全局快捷键

### Phase 4 - 完善

1. 历史记录存储 + 列表展示
2. 设置页面（翻译源配置、**快捷键自定义**、主题）
3. 系统托盘图标（最小化到托盘）
4. 打包发布 MSIX

---

## 9. 环境要求

```
- Windows 11 (或 Windows 10 1809+)
- Visual Studio 2022 17.8+ (含 "Windows application development" 工作负载)
  或 Visual Studio 2026
- .NET 8.0+ SDK
- Windows App SDK 2.0
```

### 快速开始

```powershell
# 1. 克隆 STranslate 作为参考代码库
git clone https://github.com/STranslate/STranslate.git c:\repos\Translater\_reference\STranslate

# 2. 确认 .NET SDK
dotnet --version

# 3. 创建项目 (或用 VS 模板)
# VS → 新建项目 → "WinUI Blank App (Packaged)" → Translater.App

# 4. 编译运行
dotnet build src/Translater.sln
# 或在 VS 中按 F5
```

---

## 10. 风险与注意事项

| 风险 | 应对方案 |
|------|---------|
| 翻译 API 需要密钥 | Azure Translator 提供 200 万字/月免费额度，注册即用 |
| Windows.Media.Ocr 精度 | 中文识别较好，英文也可；必要时补充 Tesseract |
| WinUI 3 截屏窗口 | 截屏遮罩需用原生 Win32 窗口，WinUI 不支持透明全屏覆盖 |
| MSIX 打包复杂度 | VS 模板已处理，按向导走即可 |
| 全局热键冲突 | 允许用户自定义热键组合 |
