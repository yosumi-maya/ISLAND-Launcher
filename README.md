# ISLAND 岛屿启动器

完全免费的 Windows Minecraft Java 版启动器，使用 .NET 8 / WPF。当前版本 **1.1.0 正式版**。

[下载正式版](https://github.com/yosumi-maya/ISLAND-Launcher/releases/tag/v1.1.0) · [问题反馈](https://github.com/yosumi-maya/ISLAND-Launcher/issues)

## 下载

在 Releases 下载 **IslandLauncher-1.1.0-win-x64.exe** 后直接运行，或者下载 ZIP 并解压其中的同一个 EXE。支持 Windows x64，无需另装 .NET。游戏所需 Java 可由启动器自动下载。

## 功能

- 管理多个游戏目录与实例，配置 Java、内存和启动参数；缺少兼容 Java 时自动下载，支持选择供应商和国内镜像。
- 安装 Minecraft 与加载器，拖入 ZIP / MRPACK 整合包导入，兼容 GTNH 的 MultiMC / Prism 本地组件补丁。
- 搜索并下载 Modrinth / CurseForge 的整合包、模组、光影与资源包，检测前置依赖并选择安装。
- Microsoft 正版登录、本地玩家、皮肤与正版披风管理。
- 通过官方 Terracotta 独立组件提供好友联机，首次使用按需下载并验证。
- 顶部卡片跟随实例选择；一键继续对应实例最近的单人存档，没有存档时正常启动。
- 启动前清理内存、自定义外观及下载任务管理。

## 从源码构建

在 Windows 安装 .NET 8 SDK 与 PowerShell 7，然后在仓库根目录执行：

```powershell
dotnet restore launcher/Island.sln
dotnet build launcher/Island.sln -c Release
dotnet run --project launcher/tests/Island.Tests -c Release
dotnet run --project launcher/tests/Island.App.Tests -c Release
./launcher/build.ps1
```

启用 CurseForge 的发布构建需要维护者通过构建环境提供 `CF_API_KEY`；不要把密钥提交到源码。普通开发构建未提供密钥时，该来源不可用。旧版存档继续组件的源码与重建工具见 [Island.QuickPlay](launcher/tools/Island.QuickPlay/)。

## 许可与声明

自有代码采用 MIT 许可。第三方组件、图标与素材遵循各自原始许可，见 [第三方声明](launcher/third-party/) 与 [参考资料](launcher/REFERENCES.md)。必要许可与声明已嵌入发布 EXE。

ISLAND 是非官方项目，与 Mojang Studios、Microsoft 无隶属或背书关系。Minecraft 是 Mojang Studios 的商标。本地玩家不能代替正版服务器的身份验证。
