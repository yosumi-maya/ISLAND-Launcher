# ISLAND 岛屿启动器

免费的 Windows Minecraft Java 版启动器，使用 .NET 8 / WPF。当前版本 **0.20.19**。

[下载与版本说明](https://github.com/yosumi-maya/ISLAND-Launcher/releases/tag/v0.20.19) · [问题反馈](https://github.com/yosumi-maya/ISLAND-Launcher/issues)

## 功能

- 管理游戏目录与实例，配置 Java、内存和启动参数。
- 安装游戏与加载器，支持拖入 ZIP / MRPACK 整合包导入。
- 通过 Modrinth 下载整合包、模组、光影与资源包，检查前置依赖并选择安装。
- 支持 Microsoft 正版登录、本地玩家、皮肤及正版披风管理。
- 使用官方 Terracotta 组件提供好友联机，首次使用按需下载并验证组件。
- 支持启动前内存清理、自定义外观及下载任务管理。

CurseForge 在线下载尚未开放。好友联机需要兼容的游戏版本与模组，连接效果取决于网络环境；本地玩家不能代替正版服务器的身份验证。

## 下载

在 Releases 下载 `IslandLauncher-0.20.19-win-x64.zip`，解压后运行 `岛屿启动器.exe`。便携包包含 .NET 运行时；请另行准备游戏需要的 64 位 Java。发布附件提供 SHA-256 校验值。

## 从源码构建

在 Windows 安装 .NET 8 SDK，在仓库根目录执行：

```powershell
dotnet restore launcher/Island.sln
dotnet build launcher/Island.sln -c Release
dotnet run --project launcher/tests/Island.Tests -c Release
dotnet run --project launcher/tests/Island.App.Tests -c Release
./launcher/build.ps1
```


## 许可与声明

启动器自有代码采用 MIT 许可。第三方组件、图标与素材分别遵循其原始许可，见 [第三方声明](launcher/third-party/) 与 [参考资料](launcher/REFERENCES.md)。陶瓦组件以未经修改的官方独立程序运行，具体许可和接入说明见对应声明。

ISLAND 为非官方项目，与 Mojang Studios、Microsoft 无隶属或背书关系。Minecraft 是 Mojang Studios 的商标。
