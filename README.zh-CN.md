# Simple Time Countdown

<div align="center">

[**English**](README.md) | [**简体中文**](README.zh-CN.md)

</div>

一款面向 Windows 11 的轻量级桌面倒计时应用，基于 WPF + .NET 8 构建。

![main](docs/images/example-main.png)

![add](docs/images/example-add.png)

![archive](docs/images/example-archive.png)

![setting](docs/images/example-setting.png)


## 项目特点

- 轻量级：原生 WPF，无浏览器运行时和 Electron 级内存负担
- 低占用：本地 JSON 存储，无后台同步服务，适合常驻
- 风格鲜明：单张羊皮纸"案录"面板上漂浮卡片

## 仓库结构

- `.github/workflows/`：Windows 构建 CI
- `docs/images/`：截图与视觉参考
- `packaging/msix/`：MSIX 清单与打包资源
- `scripts/`：资源生成、发布和安装脚本
- `src/SimpleTimeCountdown.App/`：WPF 主程序
  - `Themes/VictorianTheme.xaml`：调色板、字体与公共控件样式
  - `Controls/CigarCountdown.cs`：自绘的雪茄形进度条
  - `Converters/`、`Models/`、`Services/`、`ViewModels/`、`Views/`、`Assets/`
- `src/SimpleTimeCountdown.Setup/`：品牌化安装器
- `artifacts/`：构建输出目录（不应提交）

## 功能

### 行为与集成
- 中英文界面（运行时切换）
- 标题、副标题、标签的搜索与筛选（点报头放大镜显隐搜索栏）
- 面板任意非交互区域均可拖动
- 本地持久化：`%AppData%\TimeCountdown\state.json`
- 托盘菜单：显示面板、新建、设置、始终置顶、退出
- 注册表实现的开机自启
- 提醒气泡与到期通知

## 构建

以下命令均假设当前目录为仓库根目录。

1. 安装 .NET 8 SDK（含 Windows Desktop 支持）
2. 可用 Visual Studio 2022 打开 `SimpleTimeCountdown.sln`，也可在 PowerShell 中执行命令。
3. 还原并构建解决方案：

```powershell
dotnet restore .\SimpleTimeCountdown.sln
dotnet build .\SimpleTimeCountdown.sln
```

构建 Release 配置：

```powershell
dotnet build .\SimpleTimeCountdown.sln -c Release
```

从源码运行 WPF 主程序：

```powershell
dotnet run --project .\src\SimpleTimeCountdown.App\SimpleTimeCountdown.App.csproj
```

从 Debug 输出运行安装器项目：

```powershell
dotnet run --project .\src\SimpleTimeCountdown.Setup\SimpleTimeCountdown.Setup.csproj
```

`dotnet build` 只会更新 `src/**/bin/` 下的项目构建输出，不会更新 `artifacts/packages/` 下用于分发的安装包。如果要更新用户双击运行的经典安装器 EXE，请执行下方“发布”章节中的 `Build-SetupExe.ps1`。

## 发布

发布脚本默认使用 `Release` 和 `win-x64`。可以通过 `-Configuration` 和 `-RuntimeIdentifier` 覆盖，例如：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-SetupExe.ps1 -Configuration Release -RuntimeIdentifier win-x64
```

便携包：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Publish-Portable.ps1
```

MSIX 包：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Publish-MSIX.ps1
```

本地安装 MSIX：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Install-MSIX.ps1
```

首次安装自签名开发包时，可能需要使用提升权限的 PowerShell 执行安装脚本，以便在机器级信任证书。

经典 `Setup.exe` 安装包：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-SetupExe.ps1
```

该命令会先发布便携版应用，再把便携 zip 嵌入安装器项目，最后生成并签名 `artifacts/packages/SimpleTimeCountdown-Setup-win-x64.exe`。

输出位置（均相对仓库根目录）：

- 便携包：`artifacts/packages/SimpleTimeCountdown-Release-win-x64-portable.zip`
- 安装包：`artifacts/packages/SimpleTimeCountdown-Setup-win-x64.exe`
- MSIX：`artifacts/packages/SimpleTimeCountdown_<version>_win-x64.msix`
- 开发证书：`artifacts/certificates/TimeCountdownDev.cer`

## 下载

请优先前往本仓库 GitHub Releases 下载最新版本：

- 最新版本：[Releases / Latest](../../releases/latest)
- 历史版本：[Releases](../../releases)

各安装包区别：

- `SimpleTimeCountdown-Setup-win-x64.exe`（推荐多数用户）
  - 标准安装向导，一键安装
  - 自动创建开始菜单 / 桌面入口和卸载项
  - 适合日常长期使用
- `SimpleTimeCountdown-Release-win-x64-portable.zip`
  - 免安装，解压即用
  - 不写入系统级安装 / 卸载记录
  - 适合临时使用、U 盘携带或受限环境
- `SimpleTimeCountdown_*.msix`
  - 基于 MSIX 包模型，安装与卸载更规整
  - 与 Windows 包管理体系更契合
  - 适合偏好 MSIX 部署流程的用户
