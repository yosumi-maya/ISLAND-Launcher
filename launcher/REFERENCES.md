# 参考与第三方说明

## 0.10.20 UI 检查依据

- 使用本机安装的 frontend-design、ui-ux-pro-max、web-design-guidelines 作为设计与检查指引，没有引入其作为运行依赖。设计说明见 `design-polish.md`。
- [Web Interface Guidelines](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md)：查阅当前焦点、交互反馈、空态、异步状态与内容可读性规则，将适用规则映射为 WPF 控件、资源与 AutomationProperties，没有在启动器内引入网页或浏览器。

## 0.10.18 Modrinth 资源下载

- [搜索项目](https://docs.modrinth.com/api/operations/searchprojects/)：使用 `project_type:mod` / `shader` / `resourcepack`、版本与加载器的 AND facets，以及排序和分页。
- [项目发布列表](https://docs.modrinth.com/api/operations/getprojectversions/)、[单个发布与依赖](https://docs.modrinth.com/api/operations/getversion/)：按实例匹配游戏版本与加载器，解析 required / incompatible 依赖及文件 SHA-1 / SHA-512，使用 HTTPS 下载链接。下载资源与图标来自用户选择的第三方项目，不包含在启动器发布包中。

## 0.10.14 Microsoft 登录

- [Microsoft 授权码协议](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-auth-code-flow)：公共桌面客户端使用 PKCE S256、state 与授权码兑换，不在应用内保存 client_secret。
- [Microsoft 本机回调规则](https://learn.microsoft.com/en-us/entra/identity-platform/reply-url#localhost-exceptions)：Mobile and desktop 平台的 `http://localhost` 回调及随机端口匹配。
- HMCL 源码核对：[OAuth](https://github.com/HMCL-dev/HMCL/blob/0874c98d215fa271630c5934e7de04651cee30f7/HMCLCore/src/main/java/org/jackhuang/hmcl/auth/OAuth.java)、[OAuthServer](https://github.com/HMCL-dev/HMCL/blob/0874c98d215fa271630c5934e7de04651cee30f7/HMCL/src/main/java/org/jackhuang/hmcl/game/OAuthServer.java)、[MicrosoftService](https://github.com/HMCL-dev/HMCL/blob/0874c98d215fa271630c5934e7de04651cee30f7/HMCLCore/src/main/java/org/jackhuang/hmcl/auth/microsoft/MicrosoftService.java)。确认双登录方式、PKCE、Xbox 用户一致性，以及先查询 entitlements 再验证 Java profile 的顺序。本项目依据公开协议独立实现 C#，没有复制 HMCL 的 GPL 代码、网页或应用凭据。

## 0.10.11 HMCL 参数兼容

- [HMCL DefaultLauncher](https://github.com/HMCL-dev/HMCL/blob/main/HMCLCore/src/main/java/org/jackhuang/hmcl/launch/DefaultLauncher.java)、[LaunchManifestNormalizer](https://github.com/HMCL-dev/HMCL/blob/main/HMCLCore/src/main/java/org/jackhuang/hmcl/game/LaunchManifestNormalizer.java)：核对 `${primary_jar_name}` 表示实际客户端 JAR 文件名，以及它被加入 Forge `ignoreList` 的含义。本项目自行在已有占位符字典中实现展开，没有复制 HMCL 源码。GTOCore 的 Java 要求从用户本机已启用 JAR 的实际入口类字节码读取；不分发用户的整合包、模组或配置。

岛屿启动器以公开元数据独立实现 C# 启动流程，没有复制 Prism 的 C++/Qt 实现、界面、图标或 API 凭据。项目不隶属于 Prism Launcher，也不是其官方版本。

- [Prism Launcher 功能介绍](https://prismlauncher.org/about/)：参考隔离实例、账号、Java/内存配置及模组管理的使用方式。
- [Prism Launcher 源码](https://github.com/PrismLauncher/PrismLauncher)：阅读 `MinecraftInstance.cpp` 与账号流程的少量文件，核对游戏参数类别和 Xbox/Minecraft 服务请求顺序。其源码许可为 GPL-3.0-only，品牌素材另有许可；本程序不内嵌这些内容。
- [Microsoft 设备码协议](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-device-code)：设备码、轮询间隔、过期、刷新与公共客户端行为。
- [Microsoft CryptProtectData](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata)：本机用户级凭据保护。
- [DWM_SYSTEMBACKDROP_TYPE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)：旧版曾使用系统材质编号 2 / 3 / 4；0.10 起清理这些系统背景，旧配置迁移为应用内高斯模糊。当前三种材质均由 WPF 绘制。
- [Apple WWDC25 · Meet Liquid Glass](https://developer.apple.com/videos/play/wwdc2025/219/)、[Apple HIG · Materials](https://developer.apple.com/design/human-interface-guidelines/materials)：参考背景感知、lensing 局部折射、磨砂散射、反光、明暗适配与前景可读性。启动器使用自行编写的 WPF 背景层取样和缓存位图近似这些视觉特征，没有使用或复制 Apple 的实现；不包含官方材质的完整动态行为。建议将玻璃用于需要突出层次的区域，避免所有层都使用浓重玻璃。
- [WPF ShaderEffect](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.effects.shadereffect)：核对 WPF 自定义像素着色器的采样和编译要求。本版本采用缓存位图处理背景折射，未引入着色器编译器、额外渲染框架或前景模糊。
- [WindowChrome.CornerRadius](https://learn.microsoft.com/en-us/dotnet/api/system.windows.shell.windowchrome.cornerradius)、[Windows 桌面应用圆角](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-rounded-corners)：普通非玻璃窗口使用 WindowChrome 的圆角轮廓；玻璃材质忽略该属性，使用 DWMWA_WINDOW_CORNER_PREFERENCE 请求原生圆角，实际效果受系统策略影响。最大化时不保留内容圆角。
- [SetLayeredWindowAttributes](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes)、[SetWindowSubclass](https://learn.microsoft.com/en-us/windows/win32/api/commctrl/nf-commctrl-setwindowsubclass)：为本程序自己的主窗口设置整体 alpha，并在调整尺寸时保留所需样式。
- [.NET WPF HwndTarget 源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/InterOp/HwndTarget.cs)：核对 WM_STYLECHANGING 对 WS_EX_LAYERED 的处理，窗口兼容层仅在整体半透明时保留该位，不改变其他样式消息。
- [Windows MEMORYSTATUSEX](https://learn.microsoft.com/windows/win32/api/sysinfoapi/ns-sysinfoapi-memorystatusex)：读取总内存与当前可用物理内存，供启动时自动内存估算使用。
- [Oracle Java 命令参数说明](https://docs.oracle.com/en/java/javase/17/docs/specs/man/java.html)：堆内存、垃圾收集器与已移除的永久代参数兼容处理。
- [Prism 的 API 设置说明](https://prismlauncher.org/wiki/help-pages/apis/)：自有 Microsoft Client ID 的配置边界。
- [Mojang 版本清单](https://piston-meta.mojang.com/mc/game/version_manifest_v2.json)：版本 JSON、库、资源索引、参数规则、Java 版本和下载校验元数据以服务返回内容为准。
- [Fabric Meta](https://meta.fabricmc.net/)：Fabric 版本与 profile。
- [Minecraft 官方皮肤说明](https://www.minecraft.net/en-us/article/what-is-minecraft-skin)：Java 皮肤以展开的 PNG 纹理导入。
- [Mojang 人形模型定义](https://github.com/Mojang/bedrock-samples/blob/main/resource_pack/models/entity/humanoid.custom.geo.json)：参考标准皮肤的部位尺寸与展开贴图；本项目自行构造 WPF 3D 网格、外层及预览动作。
- [WPF 逐帧渲染回调](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-render-on-a-per-frame-interval-using-compositiontarget)：使用 CompositionTarget.Rendering 更新 3D 预览，停止显示或窗口失去前台状态时移除订阅。
- [XMCL 的 Minecraft Services 客户端源码](https://github.com/Voxelum/minecraft-launcher-core-node/blob/master/packages/user/mojang.ts)：核对官方档案皮肤字段、带 Bearer 认证的 multipart 上传与默认皮肤恢复端点；本项目自行实现 C# 客户端，未复制其源代码。
- 工作空间已有的 MIT 授权 `BlockDock.Core`：复用下载、安装、整合包解析、安全路径和 Java 检测；保留原版权声明。下载核心引用的 Forge、NeoForge、Modrinth、CurseForge 和 BMCLAPI 接口说明见工作空间原有 `REFERENCES.md`。

- [PCL 启动流程](https://github.com/Meloong-Git/PCL/blob/main/Plain%20Craft%20Launcher%202/Modules/Minecraft/ModLaunch.vb)、[PCL 内存优化入口](https://github.com/Meloong-Git/PCL/blob/main/Plain%20Craft%20Launcher%202/Pages/PageOther/PageOtherTest.xaml.vb)：参考启动前可选内存优化的使用方式；其具体实现未公开，本项目未复制 PCL 的清理代码。
- [System Informer phnt 的 Windows NT 接口定义](https://github.com/winsiderss/systeminformer/blob/master/phnt/include/ntexapi.h)：核对 SystemMemoryListInformation = 80、MemoryEmptyWorkingSets = 2、MemoryPurgeStandbyList = 4，以及所需 SeProfileSingleProcessPrivilege。独立编写 C# 声明，未内置或复制其清理程序；接口属于 Windows 内部 API，失败时记录并跳过。
- [Microsoft AdjustTokenPrivileges](https://learn.microsoft.com/en-us/windows/win32/api/securitybaseapi/nf-securitybaseapi-adjusttokenprivileges)：仅在短时辅助进程启用已有权限，并检查 ERROR_NOT_ALL_ASSIGNED；不更改用户权限策略。

## 0.10.6 启动选项与内置兼容组件

- [Oracle Java 15 迁移说明](https://docs.oracle.com/en/java/javase/15/migrate/index.html)、[Java 21 收集器](https://docs.oracle.com/en/java/javase/21/gctuning/available-collectors.html)、[Java 23 ZGC](https://docs.oracle.com/en/java/javase/23/gctuning/z-garbage-collector.html)、[Java 24 ZGC](https://docs.oracle.com/en/java/javase/24/gctuning/z-garbage-collector.html)：核对 ZGC 可用版本、分代模式默认值及已删除的旧标志。GC 选择与版本回退逻辑由本项目独立实现。
- [Java 8 参数说明](https://docs.oracle.com/javase/8/docs/technotes/tools/windows/java.html)：核对 G1、暂停目标与 ParallelRefProcEnabled；调优预设不宣称对所有游戏均有性能提升。
- [Microsoft cmd](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/cmd)：启动前用户命令使用 `/d /s /c`，禁用 cmd AutoRun 并等待退出。
- [HttpClientHandler 证书校验回调](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclienthandler.servercertificatecustomvalidationcallback)：默认使用系统校验，仅在用户关闭登录验证时对指定登录域名使用兼容回调。
- [HMCL 高性能显卡讨论](https://github.com/HMCL-dev/HMCL/issues/4639)：核对 Windows 图形偏好按 Java 程序路径作用及其影响范围。本项目独立编写 C# 临时偏好与恢复逻辑，未复制 HMCL 的 GPL 实现。
- [Java Launch Wrapper 1.4.4](https://github.com/00ll00/java_launch_wrapper)：MIT，Copyright (c) 2022 00ll00。内置官方发行文件 `java_launch_wrapper-1.4.4.jar`，SHA256 `88eb3ee58854e916437831697ef6a2fd493da9bc4ffb018f620cd88f090952aa`。用于受 Windows UTF-8 代码页问题影响的旧 Java；Java 19 起不自动注入。完整许可位于 `third-party/JAVA-LAUNCH-WRAPPER-LICENSE.txt`，发布包与释放目录附带许可。
- [LWJGL Unsafe Agent 2.0](https://github.com/HMCL-dev/lwjgl-unsafe-agent/releases/tag/2.0)、[上游说明](https://github.com/HMCL-dev/lwjgl-unsafe-agent)：Apache-2.0，Copyright 2026 Glavo。内置官方 `lwjgl-unsafe-agent-2.0.jar`，SHA256 `6c07d3508dc090a12076876bdc227a19ce195a41c9e2c6535c3f82213b1b2015`。仅用于匹配的 Java 25+ / LWJGL 版本；参考 [Forge 兼容问题](https://github.com/Meloong-Git/PCL/issues/8399)，不向 Forge 配置自动注入。完整许可位于 `third-party/LWJGL-UNSAFE-AGENT-LICENSE.txt`，发布包与释放目录附带许可。

## 0.10.8 GTNH / Cleanroom 兼容核对

- [GTNH 官方 FAQ](https://www.gtnewhorizons.com/faq/)、[LWJGL3ify 官方说明](https://github.com/GTNewHorizons/lwjgl3ify/blob/master/README.MD)：核对现代 Java、RetroFuturaBootstrap 系统类加载器及随补丁提供的 JVM 参数。启动器保留用户已安装版本中的参数，不套用其他版本整合包的固定参数。
- [Prism VersionFile](https://github.com/PrismLauncher/PrismLauncher/blob/develop/launcher/minecraft/VersionFile.cpp)、[Prism PackProfile](https://github.com/PrismLauncher/PrismLauncher/blob/develop/launcher/minecraft/PackProfile.cpp)：参考组件、库与启动参数组成的使用方式，未复制其 C++ 实现。
- [HMCL MultiMC 兼容修复 #3547](https://github.com/HMCL-dev/HMCL/pull/3547)、[对应问题 #3540](https://github.com/HMCL-dev/HMCL/issues/3540)、[HMCLMultiMCBootstrap](https://github.com/HMCL-dev/HMCL/blob/main/minecraft/libraries/HMCLMultiMCBootstrap/src/main/java/org/jackhuang/hmcl/HMCLMultiMCBootstrap.java)：核对 MultiMC 配置转换与真正主类的传递方式。没有复制 HMCL 的 Java 实现或内置其启动器；用户导入目录中自带的兼容库只在本机读取。
- [Cleanroom 官方说明](https://github.com/CleanroomMC/Cleanroom)：核对 Foundation 与现代 Java 运行环境。用户的 0.3.31-alpha / Foundation 0.18.3 实际类文件为 Java 21；不按当前主线 README 的 Java 25 要求盲目覆盖旧版本，而以安装元数据和实际主类版本提高最低要求。
- [HMCL LauncherHelper](https://github.com/HMCL-dev/HMCL/blob/main/HMCL/src/main/java/org/jackhuang/hmcl/game/LauncherHelper.java)：进一步核对 Cleanroom 的 Java 要求需要独立于 Minecraft 1.12.2 基础要求处理；本项目使用自身的配置与类文件读取逻辑，未复制该实现。

山林插画由 WPF 原生几何绘制，程序图标为本项目生成的树形几何图案。「流光玻璃」的彩色花瓣背景由本项目程序绘制，从 0.9.5 测试样图转为内置 PNG，仅内置该背景，不包含测试玩家皮肤。界面使用系统自带字体和 Segoe MDL2 Assets 图形，不附带字体文件。用户游戏目录中的实例图标只在本机读取，未随程序分发。

Minecraft 游戏文件、Java、第三方玩家皮肤和整合包均不包含在发布 ZIP 中；相关内容的权利属于其各自权利人。世界封面仅从本机存档读取，不上传、不随发布包分发。无皮肤时的通用像素人物由本项目代码绘制。程序为非官方 Minecraft 工具。
