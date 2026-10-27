# API 申请项目说明草稿

项目名称：ISLAND Launcher / 岛屿启动器

公开源码：https://github.com/yosumi-maya/ISLAND-Launcher
发布文件：https://github.com/yosumi-maya/ISLAND-Launcher/releases/tag/v0.10.22
官网与隐私页面：官网上线后填写实际公开地址；不要把本地预览地址填进申请表。

## 中文项目介绍

ISLAND 是面向 Windows x64 的独立、开源 Minecraft Java 版启动器，使用 .NET 8 和 WPF。提供多个游戏目录与实例管理、Java 和内存配置、下载队列、Modrinth 内容搜索下载及自定义外观。

Microsoft 登录使用系统浏览器授权码 + PKCE S256，提供备用设备码流程。密码仅在 Microsoft 官方网页输入，客户端不使用 client_secret。随后通过 Xbox、XSTS 与 Minecraft 服务验证游戏资格和 Java 玩家档案。刷新凭据在本机以当前 Windows 用户的 DPAPI 加密保存；本项目不提供独立网站账号系统。

申请 Minecraft Services API 的用途是为拥有 Minecraft Java 版的玩家提供正版登录、读取玩家档案和按用户主动操作管理皮肤。当前资格尚待确认，官网和发布说明明确披露这一限制。

拟申请 CurseForge API 的用途是搜索、展示并下载玩家选择的模组与整合包。该能力尚未开放，获批并完成接入前不宣称可用；具体服务条款和获准用途应由项目所有者在申请时核实。

## English project description

ISLAND Launcher is an independent, MIT-licensed Windows x64 launcher for Minecraft: Java Edition, built with .NET 8 and WPF. It manages game folders and instances, configures Java and memory, queues downloads, browses Modrinth content, and provides customizable appearance settings.

Microsoft sign-in uses the system browser with an authorization code and PKCE S256, with device-code sign-in as an alternative. Passwords are entered only on Microsoft’s official pages; no client secret is used. Xbox, XSTS and Minecraft services are used to validate game ownership and obtain the Java player profile. Refresh credentials are stored locally and protected with Windows DPAPI for the current user.

Minecraft Services API access is intended for sign-in by players who own Java Edition, profile retrieval, and user-initiated skin management. Access approval has not been confirmed, and this limitation is disclosed publicly. CurseForge integration is planned for user-selected mod and modpack discovery and downloads, but is not yet available.

ISLAND is not affiliated with or endorsed by Mojang Studios or Microsoft.

## 所有者补充

申请时填写本人确认的联系信息、实际应用注册信息、公开官网和隐私地址。此文是基于现有代码的介绍草稿，并非服务商当前申请表或审批要求。申请提交由项目所有者完成。
