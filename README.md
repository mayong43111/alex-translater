# Translater

一款轻量级 Windows 翻译工具，支持中英互译、OCR 截屏翻译，基于 WinUI 3 构建。

![Windows](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/license-MIT-green)

## 功能特性

- **中英互译** — 输入文本自动检测语言，支持中→英 / 英→中
- **OCR 截屏翻译** — 全局热键截屏框选 → 文字识别 → 自动翻译，一步到位
- **离线翻译** — 内置 MarianMT ONNX 模型，无需网络即可翻译
- **在线翻译** — 使用 Bing 网页翻译会话接口，支持中英互译（单次最多 1000 字符）
- **点击发音** — 原文、译文均可点击扬声器朗读，再次点击停止；使用 Windows 本地中英文语音包
- **翻译历史** — 本地保存翻译记录，随时查阅
- **系统托盘** — 最小化到托盘，随时唤起
- **Fluent Design** — Mica 背景 + WinUI 3 原生控件，融入 Windows 11 设计语言

## 截图

<!-- TODO: 添加应用截图 -->

## 系统要求

- Windows 10 1809+ / Windows 11
- x64 架构

## 快速开始

### 下载安装

从 [Releases](https://github.com/mayong43111/alex-translater/releases) 下载最新版本的压缩包，解压后运行 `Translater.App.exe` 即可。

### 从源码构建

```powershell
# 克隆仓库
git clone https://github.com/mayong43111/alex-translater.git
cd alex-translater

# 构建
dotnet build src/Translater.App -c Release -r win-x64

# 发布（自包含）
dotnet publish src/Translater.App -c Release -r win-x64 --self-contained true -o publish
```

## 使用说明

| 操作 | 说明 |
|------|------|
| **文本翻译** | 在输入框中输入文字，点击"翻译"按钮 |
| **点击发音** | 点击原文或译文左下角的扬声器按钮；缺少语音时在 Windows 设置中安装对应语言的语音包 |
| **截屏翻译** | 按 `Alt+D`（默认热键）进入截屏模式，框选区域后自动 OCR + 翻译 |
| **切换引擎** | 点击设置，可在 Bing 在线 / 离线模型之间切换 |
| **离线模型** | 首次使用离线模式需在设置中下载模型（约 115MB） |

在线翻译先访问 `https://cn.bing.com/translator` 获取会话参数，再请求 `https://cn.bing.com/ttranslatev3`。这是 Bing 网页内部接口，并非承诺稳定的官方 API，可能因网页改版、网络环境或限流而不可用；此时可切换离线翻译。

## 项目结构

```
src/
├── Translater.App/             # WinUI 3 应用层（UI + 入口）
├── Translater.Core/            # 核心接口与模型定义
├── Translater.Infrastructure/  # 基础设施（翻译服务、OCR、存储）
└── Translater.sln              # 解决方案文件
```

## 技术栈

- **UI**: WinUI 3 (Windows App SDK 2.0)
- **运行时**: .NET 8
- **离线翻译**: ONNX Runtime + MarianMT 量化模型
- **OCR**: Windows.Media.Ocr (系统内置)
- **翻译参考**: [STranslate](https://github.com/JEEK-Translater/STranslate) (MIT)

## 许可证

[MIT License](LICENSE)
