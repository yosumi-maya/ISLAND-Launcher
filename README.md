# ISLAND 岛屿启动器

独立的 Windows Minecraft Java 版启动器，使用 .NET 8 / WPF。当前开发预览版 **0.10.24**。

[下载与版本说明](https://github.com/yosumi-maya/ISLAND-Launcher/releases) · [问题反馈](https://github.com/yosumi-maya/ISLAND-Launcher/issues)

0.10.24 按实际运行时布局识别 zulujava8 和自定义名称的 Java 目录，升级后自动重扫一次（包括 0.10.23 缓存）。

## 功能

- 管理多个游戏目录、独立实例、收藏和存档；按实例配置 Java、内存与启动参数。
- 安装 Minecraft 与受支持的加载器，导入整合包；通过 Modrinth 下载整合包、Mod、资源包与光影。
- 下载队列支持暂停、继续、重试、取消与日志，安装前检查兼容版本、依赖和文件校验。
- 浅色和深色主题、20 套预设、区域背景与玻璃材质、实例卡片动效、独立地图卡片外观。
- 本地玩家、Microsoft 系统浏览器 PKCE 与备用设备码流程、皮肤管理。

**服务状态：** Modrinth 已接入。Minecraft Services API 的应用访问资格仍待确认，正版登录与官方皮肤操作暂不保证可用；CurseForge API 尚未申请，在线下载暂未开放。离线账号不能加入要求正版认证的服务器。

## 下载

在 Releases 下载 `IslandLauncher-0.10.24-win-x64.zip`，解压后运行 `岛屿启动器.exe`。便携包包含 .NET 运行时；请另行安装游戏所需的 64 位 Java。随包提供 SHA-256 校验与第三方组件许可。

## 从源码构建

在 Windows 上安装 .NET 8 SDK，然后在仓库根目录执行：

```powershell
dotnet restore launcher/Island.sln
dotnet build launcher/Island.sln -c Release
dotnet run --project launcher/tests/Island.Tests -c Release
dotnet run --project launcher/tests/Island.App.Tests -c Release
./launcher/build.ps1
```

启动器沿用共享 `src/BlockDock.Core` 与两个下载队列源文件；这些依赖及测试辅助文件已包含。不要单独移动 `launcher/` 后构建。发布脚本默认执行测试，再生成 Windows x64 自包含便携包。

## 官网

`launcher/website/` 包含中英文静态官网与隐私说明，运行 `node launcher/website/build.mjs` 即可构建。界面截图由项目所有者提供；截图配置留空时明确展示占位，不冒充实际界面。

## 许可与关联声明

项目采用 MIT 许可。嵌入组件的许可位于 `launcher/third-party/`，构建产物还附带 .NET 许可。ISLAND 是非官方项目，与 Mojang Studios 或 Microsoft 无隶属、合作或背书关系。Minecraft 是 Mojang Studios 的商标。
