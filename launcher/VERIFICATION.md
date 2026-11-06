# 岛屿启动器验证记录

## 0.20.19 版本标签移到面板外

日期：2026-11-04。

- 移除侧栏内的 ISLAND LAUNCHER / 版本标签，改为整个窗口底部 32px 留白中的 WindowFooter；文字与面板左边缘对齐，并在留白区垂直居中，不拦截鼠标操作。
- 侧栏底部原有 16px 空间改为布局下边距，保留账号卡片、导航及纸娃娃位置；外观预览也在面板外底部显示启动器标识。
- Release 构建无警告或错误；27 项设计、14 项侧栏及联机回归通过。三种主题、两种窗口尺寸下外边距实测仍为四边 32.0，已检查版本标签位于面板外的实际截图。


## 0.20.18 独立标题栏与等距外留白

日期：2026-11-04。

- 修正上一版将标题栏放入内容面板的处理。WindowTitleBar 恢复独立第一行，窗口拖动区与标题栏高度均为 32；最小化、最大化、关闭和左侧窗口按钮位于内容面板上方。
- ShellModules 四边外边距统一 32，顶部标题栏使用该区域，因此窗口按钮在面板外，同时整个窗口的左右、上下外留白相同。移除侧栏顶部额外 40 和右侧内容额外 40，内容继续保留常规 24 内边距。
- 外观预览恢复位于面板外的窗口控制图示，左右面板四周留白一致；可选区域坐标同步。
- Release 构建无警告或错误；27 项设计和 14 项联机侧栏回归通过。森林、玫瑰、夜色三套主题在 1240×830、1020×700 下实测四边均为 32.0，已目视确认窗口按钮位于面板外。


## 0.20.17 按窗口真实边缘统一四周间距

日期：2026-11-04。

- 修正 0.20.16 将标题栏高度排除在间距计算之外的问题。ShellModules 跨越标题栏和主体两行，直接以整个窗口为基准四周留白 12；侧栏及内容面板都从 y=12 开始。
- 标题栏作为上层显示在面板内部，控制区高度 40、上边距 12，WindowChrome 拖动区高度同步为 52。侧栏内部预留 40，内容内部上间距为 40+24，保持品牌、页面内容与底部布局原有坐标。
- 外观预览同步将左右面板延伸至顶部等距位置；窗口控制、预览点击区域与内容位置对应调整。
- Release 构建无警告或错误。27 项设计及 14 项联机侧栏回归通过。实际测量 ShellModules 对 WindowSurface 的边界，在森林、玫瑰及夜色主题的 1240×830、1020×700 六种组合下，left/top/right/bottom 均为 12.0；已检查实际截图。


## 0.20.16 外框圆角与四边留白

日期：2026-11-04。

- 主窗口 WindowChrome、WindowSurface 与内容裁切统一使用 8px 圆角，四角一致；最大化时同步使用 0，恢复后重新采用 8。外观预览的窗口外框与裁切同步。
- 标题栏下方 ShellModules 外边距由 12,0,12,12 改为四边 12；右侧 ContentLayout 内边距由 24,16,24,16 改为四边 24。内部卡片的层级和配色保留。
- 导航项纵向 padding 在普通尺寸为 12、紧凑尺寸为 7，为统一新增上边距预留空间。1240×830 下纸娃娃布局槽约 227px、显示高 220px；1020×700 下布局槽约 199px、显示高 180px，均包含完整模型及边距。
- Release 构建无警告或错误；27 项配色与布局回归、14 项联机与侧栏布局回归通过。已检查玫瑰浅色和海岛夜色的常规与紧凑窗口截图。原有弹窗对比度断言改为读取实际可见的圆角边框背景，匹配透明窗口的结构。


## 0.20.15 启动前优先清理内存

日期：2026-11-04。

- 开启启动前清理时，在最小运行状态与账号选择检查、启动日志建立后，先等待内存清理，再读取运行设置和版本配置、验证 Microsoft 身份、匹配 Java、检查修复依赖、执行启动前命令、分配内存并创建游戏进程。
- 「一键继续」也使用同一路径，其目录切换和实例刷新延后到清理结束之后。初始操作提示立即显示正在清理内存。
- 取消清理中止本次启动；拒绝管理员授权或清理失败时沿用原有提示并继续策略。关闭选项不调用清理器。
- Release 构建无警告或错误，8 项专项界面检查通过。测试在实例列表载入后破坏版本元数据，阻塞清理时验证不会先读版本、匹配 Java 或进入依赖准备；覆盖普通启动、一键继续、取消、拒绝权限、清理异常及关闭选项。使用模拟清理器，不触发真实 UAC 或清理用户系统内存。


## 0.20.14 下载列表翻页回顶

日期：2026-11-04。

- 成功加载到不同页码后，列表滚动到第一项并选中首项。适用于共用目录搜索流程的整合包、模组、光影和资源包。
- 同页刷新继续保留原有选择；失败或取消不替换列表和滚动位置。
- Release 构建无警告或错误。58 项下载中心 UI 检查通过，现有分页回归使用 40 项可滚动列表，验证滚至页尾后下一页、上一页均回到顶部且保留筛选。33 项资源安装 UI 回归通过。


## 0.20.13 弹窗边框与下载页精简

日期：2026-11-04。

- 固定大小的 FormWindow 使用透明窗口和自身圆角边框，移除原生 WindowChrome，避免额外系统底边；标题栏拖动和关闭按钮保留。
- 设置底部「独立桌面应用」改为可点击的「GitHub 下载页」，目标为 https://github.com/yosumi-maya/ISLAND-Launcher/releases ，已通过项目资料和 GitHub 仓库元数据确认；无远端写入。
- 下载中心移除「新任务安装到」及资源目标下方的独立/共享目录说明；成功搜索不再展示 Modrinth 本页与总数统计，无空白说明行。搜索进行中、失败、取消及平台返回的必要信息继续展示。
- 标题与本地整合包导入按钮同一行垂直居中。安装目标仍由原有实例选择和任务快照决定，安装确认仍展示真实文件夹。
- Release 构建无警告或错误；21 项登录、57 项下载中心、33 项资源安装 UI 检查通过。检查登录选择、模组列表及设置底部截图；本地模拟服务未打开真实购买页面或登录账号。


## 0.20.12 登录方式选择与目录入口精简

日期：2026-11-04。

- 删除设置页「游戏存放位置」及 RootBox、浏览处理和保存设置时的目录新增逻辑。目录由「我的实例」导入、管理和标签切换，保存其他设置沿用当前游戏目录。
- 账号页点击 Microsoft 登录后仅展示登录方式，不创建浏览器授权请求或打开浏览器。点击「打开浏览器登录」才启动原有授权；点击设备码方式后显示设备码并打开官方验证页。浏览器重开、失败重试、切换设备码和外部取消继续使用原有生命周期管理。
- Microsoft 登录、设备码和添加本地玩家弹窗底部左侧提供「购买 Minecraft」链接，指向 https://www.minecraft.net/zh-hans/store/minecraft-java-bedrock-edition-pc ，购买操作不会开始授权或改变账号。
- Release 构建无警告或错误；21 项登录交互检查、57 项下载中心检查、19 项拖放检查、16 项目录移除及实例删除检查通过。登录提供器与浏览器打开使用本地模拟，不登录真实账号；已检查登录方式、设备码和本地玩家弹窗截图。


## 0.20.11 拖入整合包响应优化

日期：2026-11-03。

- 对照 PCL 官方提交 `93ede5d9e638c99fcec853a9ba1eecd91ceee7f5`：FormMain 的拖放流程先提示读取压缩包，ModModpack 要求工作线程执行，并通过 ZIP 清单判断格式。HMCL 官方提交 `d3b91d439ca36a1d1ecbc7c105f37dae5278bf8d`：LocalModpackPage 先显示 Spinner，再 Task.supplyAsync 读取清单，回到 JavaFX 更新界面。本次仅参考流程，未复制实现代码。
- PCL 源码：https://github.com/Meloong-Git/PCL/blob/93ede5d9e638c99fcec853a9ba1eecd91ceee7f5/Plain%20Craft%20Launcher%202/Modules/Minecraft/ModModpack.vb ；拖放入口：https://github.com/Meloong-Git/PCL/blob/93ede5d9e638c99fcec853a9ba1eecd91ceee7f5/Plain%20Craft%20Launcher%202/FormMain.xaml.vb 。HMCL 源码：https://github.com/HMCL-dev/HMCL/blob/d3b91d439ca36a1d1ecbc7c105f37dae5278bf8d/HMCL/src/main/java/org/jackhuang/hmcl/ui/download/LocalModpackPage.java 。
- 性能根因：InspectAsync 原先对每个条目调用带磁盘存在性检查的 SafePaths.Under，检查的是未写入的临时目录。现拆分为纯路径解析与真实目标检查，识别阶段无逐条目磁盘探测；所有条目仍检查越界和符号链接，实际写入继续使用原有 Under 防护。
- 本机同一个合成 MRPACK（15,000 个两字节配置文件和一个清单）重复三次：修改前 12444.6 / 12741.8 / 12809.6 ms，修改后 85.0 / 49.5 / 40.9 ms。命令为 Island.Tests --inspect-benchmark <archive>，仅测清单识别，不代表完整安装、真实大包或其他机器的耗时。
- 松开后先绘制进度及文件名，提供取消；即使识别提供器忽略取消，也立即恢复界面且不出现过期确认框。进入本地导入时不额外启动 Minecraft 或目录搜索联网刷新。
- Release 构建无警告或错误；19 项拖放反馈及取消 UI 检查、57 项下载中心回归、46 项共享下载器与整合包安全测试、392 项核心检查通过。已检查识别进度截图。


## 0.20.10 目录移除与实例删除

日期：2026-11-03。

- 目录移除只修改登记列表，保存前可取消；移除当前目录会选择剩余目录并更新根目录设置。至少保留一条目录记录，文件与重新导入时可复用的偏好均保留。
- 实例管理最下方增加删除区域。确认默认取消，并展示实例名称和完整版本路径；确认后调用 Windows 回收站 API，仅处理 `versions/<id>`，不删除共享根目录、共享模组或其他实例。
- 删除前后都检查目标路径，拒绝越界、不匹配目标、路径链或内容中的链接和联接点、嵌套已登记根目录；游戏、启动操作及未结束下载期间拒绝删除。取消、失败或目录未实际移除时不清理记录。
- Release 构建无警告或错误；392 项核心回归（含 14 项移除与路径防护检查）、16 项专项 UI 检查、12 项实例管理回归通过，已目视检查目录移除入口、实例底部删除区及确认框。UI 回收提供器使用隔离目录移动模拟，不操作用户真实游戏文件。
- 系统回收站接口依据 Microsoft `FileSystem.DeleteDirectory` 文档： https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualbasic.fileio.filesystem.deletedirectory?view=net-8.0 。


## 0.20.9 上次游玩配色统一

日期：2026-11-03。

- 上次游玩与账号区共用淡色主题背景，地图卡片与实例共用卡片背景和边框，正文与辅助文字同步适配；浅深色与微缩预览均使用相同主题令牌。
- 默认不再使用深色山林背景；只对旧版未自定义的默认样式做一次迁移。自定义颜色、图片、材质和文字等覆盖保留，迁移版本持久化后不会重复修改。
- Release 构建无警告或错误；27 项配色与对比度 UI 检查、13 项地图独立外观检查、378 项核心回归（含 5 项默认外观迁移保护）通过。已检查玫瑰浅色与海岛夜色两种布局截图。


## 0.20.8 整合包拖放导入

日期：2026-11-03。

- 将窗口冒泡事件改为预览阶段拖放接收，防止文本框截获文件；支持现有 ZIP / MRPACK 整合包识别、确认和队列安装流程。
- 接收单个存在的文件，扩展名不区分大小写；目录、多文件、无复制权限、检查中的重复导入及启动操作期间拒绝拖入。普通文本拖动保持原行为。
- 15 项拖放 UI 检查通过，使用真实本地整合包清单验证识别、确认目录、取消及损坏 ZIP 错误恢复；57 项下载中心与队列回归通过。UI 检查通过构造 WPF 路由事件模拟拖放，没有用真实资源管理器鼠标拖动。
- Release 构建无警告或错误；已目视检查拖放提示和安装确认截图。


## 0.20.7 默认史蒂夫皮肤

日期：2026-11-03。

- 默认皮肤替换为未经修改的 Mojang 官方 Steve 模板，SHA256 和来源见 STEVE-SKIN-NOTICE.txt。内置到程序中，头像及三维纸娃娃共用同一纹理，不依赖运行时网络。
- 默认模型固定为经典宽手臂；自定义纤细皮肤、外层和披风预览保留。
- Release 构建通过；6 项默认皮肤检查、45 项皮肤工作流检查、5 项披风检查通过。已目视检查侧栏史蒂夫与头像截图。


## 0.20.6 外观设置精简

日期：2026-11-02。

- 外观入口更名，移除指定的三个动画选项与两行说明，旧配置安全回退到悬浮跟随。
- 标题栏、按钮、导航、列表、版本筛选、实例设置、图标选择、预设配色及自定义外观预览统一移除额外焦点环或选中加粗外框，普通控件边界与勾选指示保留。
- Release 构建通过；8 项专项 UI 检查、27 项配色检查、373 项核心回归检查通过。已检查设置页、图标选择和自定义预览截图。


## 0.20.5 陶瓦好友联机

日期：2026-11-02。

- 按官方 HTTP API 文档接入未经修改的 Terracotta v0.4.2 Windows x86_64 程序，启动参数为 `--hmcl <port-file>`。仅访问回环地址，禁用本地 API 的代理与重定向。
- 官方发行包及每个 exe / DLL 使用固定 SHA256 校验。测试覆盖损坏文件拒绝、缓存修复、官方包导入，并实际通过官方镜像完成首次下载、校验和安装。
- 35 项陶瓦专用核心检查通过，包括真实官方进程的启动、引导进程退出后的可用性、同数据目录实例互斥，以及退出后 HTTP 端口关闭。未创建真实公开房间。
- 14 项联机 UI 检查通过，覆盖创建、加入、成员、连接地址、导航保持连接、非法房间码、重复超时恢复、取消、关闭与取消关闭。模拟网络状态，不冒充两台设备的端到端连接。
- 370 项核心回归、14 项 Microsoft 登录 UI 回归、57 项下载中心 UI 回归通过。Release 构建无警告和错误。检查了 1240 × 830、1020 × 700 及深色页面截图，侧栏纸娃娃有足够空间完整显示。
- 官方许可、接入例外与来源说明随启动器及按需安装的组件保存；页面底部始终显示陶瓦版权署名。
- 自包含发布 EXE 使用独立测试数据目录启动，正常生成 12 张页面截图并退出，未产生 error.txt，包含新增好友联机页。
- 限制：尚未进行两台电脑及跨启动器的实际打洞联机测试；实际连接质量取决于双方网络及公共节点。双方需使用兼容的游戏版本和模组，房主需对局域网开放。


## 0.10.24 重命名 Java 8 目录

日期：2026-10-27。

- 0.10.23 的厂商前缀要求后接版本号或分隔符，导致 zulujava8 不匹配。新增按实际运行时布局收集候选的判断，要求 bin/java.exe 与 release 或标准运行库标记同时存在；不只添加一个目录名特例。后续仍执行 Java 探测，不凭文件名或标记认定有效。
- 缓存扫描修订号更新为 3；旧版及 0.10.23 修订号 2 的缓存均自动重扫一次，保留有效条目和用户运行设置。未修改本机 Java 安装、注册表或真实游戏目录。
- Release 构建 0 警告、0 错误；启动器核心 324 项、共享核心 46 项、Java 缓存界面专项 8 项通过。新增重命名 / 任意名称运行时目录和 0.10.23 缓存迁移检查。对本机真实目录只读扫描，确认 Zulu 1.8.0_372 x64 与 Zulu 25.0.1 x64 同时发现、实际探测通过。
- 发布目录：dist/win-x64-0.10.24/；便携包：dist/IslandLauncher-0.10.24-win-x64.zip。EXE SHA-256：7629c9dff611270965e01dd1639d035232f685b8ec0fb0bbbaf123c6a6eca019。源码提交：a7b92a121adb327d8696c759c7ae95cf366e28bc。
- 日志：artifacts/zulu-01024-build.log、zulu-01024-core.log、zulu-01024-shared.log、zulu-01024-java-ui.log、zulu-01024-live.log。没有生成截图，也未修改或部署官网。

## 0.10.23 Zulu Java 扫描

日期：2026-10-27。

- 根因是解压版保留 zulu25-win_x64 等名称，原扫描仅查固定 java / jdk / jre 子目录；对游戏目录及父目录的有限检查没有把它加入候选。修复后按 Java / JDK / JRE / Zulu / Azul 前缀和版本边界收集附近的文件夹，再复用有深度和数量限制的扫描。保留内容目录排除、链接排除、真实进程探测、取消与去重。
- 补充 Programs/Zulu、Azul 等系统 / 用户路径及 Zulu 32-bit 注册分支。缓存保留文件格式，增加扫描修订号；升级时保留有效条目并重扫一次，写回后不重复扫描。未修改用户 Java 安装或注册表。
- Release 构建 0 警告、0 错误；启动器核心 320 项、共享核心 46 项、Java 缓存界面专项 8 项通过。新增 7 项解压版目录发现 / 排除 / 取消 / 去重检查及 3 项旧缓存升级检查。实际只读扫描确认用户给出的 Zulu 25.0.1（x64、Azul Systems, Inc.）被找到，真实 java.exe 进程探测成功。
- 发布目录：dist/win-x64-0.10.23/；便携包：dist/IslandLauncher-0.10.23-win-x64.zip。EXE SHA-256：`6a59fa4a8c4f2e58f09571b5543252981dddbf5e66b7c0e9c3c59621ba8a4bea`。源码提交：ffa82fd762125cb7392c69796fa03e33096d2007。
- 日志：artifacts/zulu-01023-build.log、zulu-01023-core.log、zulu-01023-shared.log、zulu-01023-java-ui.log、zulu-01023-live.log。界面专项没有传入截图参数，未生成截图；官网保留本地，未随此版本更改或上线。

## 0.10.22 柔和白灰棋盘格

日期：2026-10-27。

- 外观预览透明度参照由黑白改为纯白与 #E6E6E6 浅灰，保留 12px 格子、平铺和透明度参照功能。仅修改预览底板绘制，不改变已保存外观。
- 此项为低影响视觉调整，不新增测试，也不重复运行核心和完整界面套件；通过 Release 发布构建及真实发布版截图核对。
- 发布版正常退出 0；已查看 `artifacts/checkerboard-published/Appearance-1020.png`，外观预览为白灰相间。发布目录 `dist/win-x64-0.10.22/`、便携包 `dist/IslandLauncher-0.10.22-win-x64.zip`；日志 `artifacts/checkerboard-publish.log`、`checkerboard-published.log`。EXE SHA256 为 `e853df979969e7083324a6ebb1fd48b361b3b2ed03d07cd1990f25463dcae5c0`。

## 0.10.21 地图卡片独立外观

日期：2026-10-27。

- 截图对应「上次游玩的地图」小卡片。保留既有 RecentMap 独立区域，在实际卡片右键菜单增加「单独设置地图卡片外观」，外观预览上方增加可用键盘触达的「地图卡片设置」入口；两个入口均定位并高亮该区域，不增加新的下拉或重复区域。
- 地图卡片可单独设置背景色、背景图片、背景不透明度、三种材质、模糊与蒙层。专用图片按本卡片范围裁切，纯色 / 高斯 / 液态玻璃均使用该图片，不再只能从父卡片取样；没有专用图片时沿用原有行为。保存时复用背景图片导入与哈希去重，复制到配置目录 Backgrounds，删除原图后重新打开仍可显示。
- SurfaceAppearance 增加可选 ForegroundColor，旧配置为空时保持自动适配。地图区域提供文字自动适配、白色、深色及 #RRGGBB 输入，覆盖本卡片的标题与辅助文字；颜色只作用于该区域，文字不随背景透明度或模糊一起变化。非法输入阻止切换区域及保存，不丢弃草稿。
- 修改继续先在小窗口中预览，保存后统一应用；「重置此区域」只清除地图卡片的覆盖值。没有修改地图名称、游玩记录、存档图标或实际游戏文件，父卡片与窗口背景保持原配置。
- Release 构建 0 警告、0 错误；核心 310 项、完整 WPF 回归 483 项通过。最终地图专项 13 项通过，覆盖实际右键入口、外观快捷入口、草稿隔离、保存与父区域不变、三种材质真实像素、白色文字与地图名不变、两种尺寸、无效颜色、自动恢复、图片导入后删除原图的重启恢复以及区域重置。
- 日志：`artifacts/map-appearance-build.log`、`map-appearance-core.log`、`map-appearance-full-ui.log`、`map-appearance-ui.log`。截图位于 `artifacts/map-appearance-ui/` 和 `artifacts/map-appearance-full-ui/`，为虚构配置与合成背景，不包含用户存档或账号。介绍网站未改动。
- 发布目录 `dist/win-x64-0.10.21/`，便携包 `dist/IslandLauncher-0.10.21-win-x64.zip`。真实发布 EXE 使用独立配置和运行时解压缓存目录，完成十张截图并退出 0、无错误文件；见 `artifacts/map-appearance-publish.log`、`map-appearance-published.log`、`map-appearance-published/`。EXE SHA256 为 `6f7df1a4b0ee26947bf8360d70936741a4f313f581110cc30aeb550180dde79b`。

## 0.10.20 交互与配色优化

日期：2026-10-27。

- 使用已安装的 frontend-design、ui-ux-pro-max、web-design-guidelines，先检查 0.10.19 截图再落实 WPF 改动；设计取舍见 `design-polish.md`。保留山林插画、纸娃娃、紧凑结果卡片与既有自定义外观。采用中性色正文和底板、强调色操作与选中态，补充苔原青、暮海蓝和海岛夜色，保留旧预设，共 20 套。
- 新增持久化 DarkTheme 外观字段，旧配置默认为浅色。海岛夜色覆盖窗口、表单、列表、弹窗与外观预览，重启恢复。切换配色立即刷新材质容器中的颜色资源，包括隐藏页面，避免页面再次绘制前按钮保留旧色。自定义区域按其底色修正前景，保留原有背景、透明度与玻璃功能。
- 配色计算包含 WCAG 相对亮度 / 对比度，20 套预设的正文、辅助文字、强调文字、主要按钮正常 / 悬浮 / 按下、通知与 Hero 表层文字均检查至少 4.5:1；独立焦点环不改变布局。该检查针对生成的实色配色，不宣称任意用户透明图片背景都满足相同对比度。
- 下载分类使用清楚的选中背景和底部标记；结果选中增加背景反馈，不被鼠标悬浮覆盖。按钮悬浮使用颜色与边框反馈，保持文字不透明；主按钮和导航提供独立键盘焦点环。下载页搜索有可见名称、提示和可访问名称，状态区标记为 polite，元数据与目录说明适当增大字号。
- Ctrl+F 定位实例 / 资源搜索。搜索期间保留已有结果，显示搜索中与取消按钮，可用 Esc 取消；请求包装取消等待，即使底层服务没有及时响应令牌也能恢复界面。加载时禁用旧选择的安装入口，迟到请求仍不能替换新结果。失败保留已有结果、给出重试操作，技术错误详情放在提示中；无结果时显示下一步。独立 / 共享目录提示简化，完整路径在提示中查看。
- 保留默认「全部」版本筛选、手动输入与目标安装兼容性；没有改动游戏安装、依赖解析或队列状态机。测试修正一处队列检查的时序假设：等待后台安装器实际进入执行后再核对启动顺序，避免只观察 Running 状态时日志异步写入尚未完成。
- Release 构建 0 警告、0 错误；核心 308 项、完整 WPF 回归 470 项通过。新增设计专项 27 项覆盖黑白对比度基准、20 套配色、三类主题的保存 / 预览 / 隐藏页面颜色同步、弹窗、焦点环和重启恢复；主题即时保存专项 20 项通过。搜索专项覆盖保留结果、取消 / Esc、不响应取消的服务、失败恢复和无结果禁止安装。
- 真实 Modrinth 查询、HTTPS 图标解码与分类展示三项通过。森林绿、玫瑰红、海岛夜色在 1020 / 1240 两种宽度下的真实结果截图位于 `artifacts/design-polish-live/`；已查看三套配色与夜色真实结果。完整截图位于 `artifacts/design-polish-full-ui/`，设计专项截图位于 `artifacts/design-polish-ui/`。所有验证均使用独立虚构配置，没有改动用户实际外观或账号。
- 日志：`artifacts/design-polish-build.log`、`design-polish-core.log`、`design-polish-full-ui.log`、`design-polish-design.log`、`design-polish-presets.log`、`design-polish-live.log`。本次没有更改介绍网站。
- 最后一次搜索专项 47 项通过，日志 `artifacts/design-polish-search-final.log`。发布目录 `dist/win-x64-0.10.20/`，便携包 `dist/IslandLauncher-0.10.20-win-x64.zip`；设计说明随包附带。真实发布 EXE 使用独立配置应用海岛夜色，完成两种尺寸十张截图并退出 0、无错误文件，见 `artifacts/design-polish-published/`、`design-polish-published.log`。EXE SHA256 为 `27c95988ac5942b082bf3318e1e98a216e04623831f5958491cfcc608fed4ae3`。

## 0.10.19 实例版本识别与「全部」筛选

日期：2026-10-27。

- 只读核对截图所示导入实例：目录 / id 为重命名后的实例名，`clientVersion` 明确为 1.19.2，Forge fmlloader 坐标也包含 1.19.2。原共享下载器的发现逻辑以目录名兜底，启动器没有补充读取此字段，因而把实例名传给了资源搜索。
- 启动器实例发现现在读取 clientVersion / minecraftVersion、明确的 FML 游戏版本参数、游戏补丁、Forge 版本坐标、原版 id / jar 及继承元数据。缺少足够依据时显示「未知版本」，不把实例名或资源索引当作完整 Minecraft 版本；资源安装遇到未知版本时明确提示。不改写原导入实例或共享下载器。
- 整合包、Mod、光影、资源包共用显式「全部」选项，构造时和重置时默认选中，下拉列表不包含空白项。显示文字只用于界面，传给搜索服务的版本条件保持空字符串；仍可手填列表外的版本。异步刷新候选保留当前选择，空白编辑失焦恢复显示「全部」。
- 选择或切换资源安装目标不再自动修改 Minecraft 版本搜索框；手动输入的筛选会保留，切换分类按原有重置流程恢复「全部」。整合包发布窗口同样显示「全部」，且不会把该文字当作真实版本匹配。游戏安装仍需选具体游戏版本；资源发布和安装继续使用目标实例的真实版本检查兼容性。
- Release 构建 0 警告、0 错误；核心 308 项通过。整合包下载专项 41 项、资源专项 18 项通过，覆盖显式默认值、空字符串 API 条件、重置、四个分类切换、手动版本保留、目标安装兼容性和原有下载队列。核心新增重命名实例 / 自定义父版本、FML 参数 / 补丁 / Forge 坐标及无法识别元数据的回归检查。
- 日志：`artifacts/version-filter-build.log`、`version-filter-core.log`、`version-filter-downloads.log`、`version-filter-content.log`。已查看 1020px 的资源下载截图，版本框显示「全部」，位于 `artifacts/version-filter-content-ui/`；整合包及发布窗口截图位于 `artifacts/version-filter-ui/`。除只读确认用户实例元数据外，检查均使用专门的虚构实例与配置目录。
- 完整 WPF 界面回归 436 项通过，日志 `artifacts/version-filter-full-ui.log`；最终 0.10.19 界面截图位于 `artifacts/version-filter-full-ui/`。
- 发布目录 `dist/win-x64-0.10.19/`，便携包 `dist/IslandLauncher-0.10.19-win-x64.zip`。环境 SDK 已更新为 8.0.427，本次在工作区补齐该 SDK 对应的自包含运行时包后发布，未改动系统配置。真实发布 EXE 使用独立数据与运行时解压缓存目录生成十张截图、退出 0，无错误文件；见 `artifacts/version-filter-restore.log`、`version-filter-publish.log`、`version-filter-published.log`、`version-filter-published/`。EXE SHA256 为 `ce94c14a537491d261b1de9c55f2612868d095883697061b9984212aacb6ad83`。

## 0.10.18 Mod、光影与资源包下载

日期：2026-10-25。

- 下载中心新增三个 Modrinth 分类入口，复用紧凑图标卡片、关键词、可编辑版本、加载器 / 光影格式、排序及分页。实例选择覆盖已登记目录，带入实际 Minecraft 和加载器；Mod 支持 Forge / Fabric / NeoForge / Quilt（Cleanroom 映射 Forge），原版实例可浏览但安装 Mod 时明确阻止。资源包不附加 Mod 加载器条件；光影支持 Iris / OptiFine 格式筛选。结果兼容信息显示本次查询版本，避免误显示项目的其他最新游戏版本。
- 发布窗口以安装目标的版本和加载器读取兼容发布，按发布名称和通道筛选；准备阶段获取项目和版本元数据并递归解析 required 依赖，最多 64 个项目，固定版本必须兼容，项目级依赖优先复用已管理的兼容版本。可选前置不自动安装；手动外部前置、不同固定版本冲突、项目类型错误、服务端专用项目、非法文件名和无效哈希均停止并提示。
- 安装确认列出全部文件及冻结的目标实例与实际运行目录。资源任务使用原下载队列的状态机，支持暂停 / 继续 / 失败重试 / 取消和日志；切换目标或目录不改变已经排队的任务。任务结束只刷新实例列表，不把资源名称登记成游戏实例，游戏准备 / 运行时不能新增或恢复资源安装。
- JAR / ZIP 分别写入目标实际独立 / 共享目录的 mods / shaderpacks / resourcepacks；不解压资源包或改写 options.txt。资源通过现有 DownloadClient 的大小、SHA-1 / SHA-512 校验与续传，先下载到 `.island/content-downloads/`，全部验证后再提交；同名不同内容不覆盖，安装记录使用临时文件替换，提交失败回滚本任务已创建的文件。
- `.island/content.json` 保存已管理项目、版本和声明冲突，旧的有效不同版本阻止重复安装。解析时及执行时检查声明冲突，防止队列中先执行的任务使后续安装清单过时；精确版本冲突不会误封锁同项目其他版本。该检查只覆盖本功能管理的项目及其已声明关系，不等于对任意用户 JAR / 整合包作完整冲突分析。
- Release 构建 0 警告、0 错误。核心回归 305 项通过；完整 WPF 回归 429 项通过，含 14 项资源专项（实例选择、两种宽度、模态确认、共享队列、暂停切换后恢复、安装完成无虚假实例、三类筛选与迟到结果隔离）。核心检查覆盖前置、三个文件夹、共享 / 独立目录、版本冲突、不覆盖、损坏文件、取消 / 缓存恢复、排队后的冲突复查、精确版本冲突及执行阶段路径 / 哈希复查。
- 真实 Modrinth 联网检查 10 项通过：三类搜索、目标兼容发布及带哈希安装清单；另实际下载一个 Mod 安装清单中的文件到独立的虚构测试实例，并确认文件落盘；递归前置安装由可控模拟接口另行覆盖。没有启动真实 Minecraft，也没有把测试资源写入用户实际实例；测试资源不进入发布包。截图含三类资源的 1020 / 1240 两种宽度，位于 `artifacts/content-live/`；专项界面与确认截图位于 `artifacts/content-ui/`、完整界面截图位于 `artifacts/content-full-ui/`。
- 日志：`artifacts/content-build.log`、`content-core.log`、`content-ui.log`、`content-full-ui.log`、`content-live.log`。独立下载器核心 46/46 回归通过，日志为 `content-downloader-core.log`。原下载器界面输出 43/43 项断言通过，但测试进程未自行退出，随后终止测试会话；不将该项视为完整的进程退出验证，见 `content-downloader-ui.log`。
- 光影加载组件与游戏内启用步骤仍由用户处理；CurseForge 未申请 API 的状态说明保持可见。本次未修改或发布介绍网站。
- 发布目录 `dist/win-x64-0.10.18/`，便携包 `dist/IslandLauncher-0.10.18-win-x64.zip`。真实发布 EXE 使用独立配置和受限环境专用的运行时解压缓存目录，正常生成两种宽度的十张截图并退出 0，无错误文件；见 `artifacts/content-published/`、`content-published.log`。打包前最后一轮资源专项 14 项通过，日志为 `content-ui-final.log`。EXE SHA256 为 `2fd33d78081cfd93e794f1ff96028796f2a73bfad6fff845624e442b9d88bb0f`。

## 0.10.17 整合包结果图标、紧凑卡片与版本下拉

日期：2026-10-25。

- 整合包结果改用独立的单层横排模板：48×48 图标、名称、最多三个分类标签、单行省略简介，以及兼容信息、下载量、相对更新时间和平台来源。分类来自 Modrinth 返回的真实 categories，常见分类本地翻译；共享目录模型只增加分类元数据，不改变独立下载器的界面和下载行为。
- 图标在后台通过 HTTPS 读取，最多四个并发，限制单图 4 MiB、解码宽度 96 像素，冻结后共享最多 128 个缓存项。每行有独立图片与首字母占位，旧图片异步完成不会覆盖新绑定；请求失败或格式不支持时保留占位。真实联网搜索已验证图标可以解码显示。
- 搜索页的 Minecraft 版本改为可输入下拉，复用已查询的正式游戏版本，保留列表之外的手填值和空白；发布窗口从实际发布列表提取可选版本，同样可手填。无匹配发布时禁用安装，修复载入完成后错误重新启用按钮的问题。
- 卡片动画附加属性默认改为关闭，实例列表继续显式使用用户选择的动画。下载结果使用独立选中 / 悬浮 / 键盘焦点边框，不套用通用实例模板的动效或第二层卡片。
- Release 构建 0 警告、0 错误；核心 278 项、完整 WPF 回归 415 项、下载中心专项 38 项通过。专项覆盖下拉选择、任意版本输入、分类元数据展示、独立选中、无实例动效、两个窗口宽度，以及发布筛选和原有队列流程。真实 Modrinth 查询、图标解码与分类传递三项检查通过，没有安装真实游戏或整合包。
- 截图：`artifacts/pack-cards-ui/`、`artifacts/pack-cards-live/`，含森林绿、玫瑰红、曜石黑两种尺寸。已查看森林绿 1240px 与玫瑰红 1020px。日志：`artifacts/pack-cards-build.log`、`pack-cards-core.log`、`pack-cards-full-ui.log`、`pack-cards-ui.log`、`pack-cards-live.log`。测试使用独立虚构配置，不修改用户实际账号、目录或外观。
- 本次未修改 `website/`，CurseForge 未开放说明继续保留。
- 独立下载器核心回归 46/46 通过，日志 `artifacts/pack-cards-downloader-core.log`。发布目录 `dist/win-x64-0.10.17/`，便携包 `dist/IslandLauncher-0.10.17-win-x64.zip`。真实发布 EXE 使用独立数据目录和受限环境专用的运行时解压缓存目录，生成两种尺寸的十张截图，退出 0、无错误文件；见 `artifacts/pack-cards-published/`、`pack-cards-published.log`。EXE SHA256 为 `3300584aeb804fe4b9be81846f98d1a569393d240f12181d4932c273bc501fee`。

## 0.10.16 下载器集成与 CurseForge 状态

日期：2026-10-25。

- 在现有原生 WPF 主题内加入下载中心，含游戏安装、Modrinth 整合包和下载任务页；“添加实例 → 下载游戏”改为直达下载中心，本地整合包也统一加入队列，支持 ZIP / mrpack 拖放。原下载器的 DownloadQueue / DownloadTask 以源文件链接复用，未改动独立下载器与共享下载核心，不需要运行第二个 EXE。
- 新任务固定目录、Java、下载源、并发数和可选模组；队列顺序执行，暂停等待安装器退出再处理后续任务，支持继续、失败重试、取消、清除已结束及独立日志。设置提供 4 / 8 / 16 / 32 并行下载数。完成后刷新实例列表并保留用户当前浏览目录；安装中阻止冲突的游戏启动准备，游戏准备 / 运行时阻止新增和恢复下载。关闭可拒绝；确认后取消并等待队列退出，再完成窗口关闭。
- 搜索支持名称、Minecraft 版本、加载器、排序和分页；发布窗口按名称 / 版本号、Minecraft、加载器、通道筛选。旧搜索结果不会覆盖新查询，重置后可继续搜索。确认整合包名称和可选模组后才入队；文件下载沿用 SHA-1 / SHA-512 校验、续传、暂存与防覆盖。
- 下载中心、Mod 管理和导入错误统一说明“暂不支持 CurseForge 整合包与 Mod 在线下载：尚未申请 API 访问资格”。API 查询、发布、整合包准备、清单导入及安装工作器都有前置拦截，不读取环境或独立下载器的凭据；测试验证没有 CurseForge 请求或 x-api-key 请求头。本地 JAR 管理保留，未新增单个 Mod 的在线搜索下载。
- Release 构建 0 警告、0 错误；核心回归 278 项、完整 WPF 界面回归 405 项通过。下载专项包含复用原队列的 9 组状态转换检查，以及原生游戏 / 搜索 / 分页 / 发布 / 确认 / 可选模组 / 进度绑定 / 多目录设置冻结 / 暂停继续 / 完成刷新 / 关闭等待。核心以模拟 HTTP 完成实际微型原版安装和 mrpack overrides 导入，并检查所有 CurseForge 入口在联网前停止。
- 实际只读元数据检查取得 920 个 Minecraft 版本、20 个 Modrinth 搜索结果及所选项目的 478 个发布；没有下载真实游戏、模组或整合包文件。日志为 `artifacts/download-center-network.log`。
- 其他日志：`artifacts/download-center-build.log`、`download-center-core.log`、`download-center-full-ui.log`。已查看 1020px 游戏页、1240px 任务卡片和整合包确认截图，均在 `artifacts/download-center-full-ui/`；下载源、并发数和平台状态说明均沿用主题。
- 发布目录 `dist/win-x64-0.10.16/`，便携包 `dist/IslandLauncher-0.10.16-win-x64.zip`。网站同步下载中心介绍与 CurseForge 未开放说明，GitHub 下载地址仍留空，不上传或部署。
- 真实发布 EXE 使用独立虚构配置完成森林绿、玫瑰红、流光玻璃三轮截图，每轮 10 张新截图，退出正常、无错误文件；记录为 `artifacts/download-center-published.log`。EXE SHA256 为 `e433f108250a2145e6b61ca1f3e04df0ee616b001e2328e80830cf03a7dcbb7e`，已确认便携包内 EXE 与发布文件一致。网站构建与 31 项静态检查通过，静态 ZIP 的 14 个文件逐个核对一致。

## 0.10.15 隐藏开发者登录配置

日期：2026-10-25。

- 从普通设置中移除 Microsoft 应用 Client ID 标签、输入框和 Azure 配置说明，原卡片保留游戏下载源选择。默认使用用户先前注册的 ISLAND 应用标识，作为公开 Client ID 内置；没有添加 client_secret。
- `MicrosoftClientId` 配置项保留为高级覆盖，空白旧配置自动使用内置标识，无效覆盖在发起登录前报错。普通设置保存不再改写覆盖字段；已登录账号仍按其原有 Client ID 刷新，刷新凭据的 DPAPI 保护不变。没有修改实际用户配置或 Azure 注册。
- Release 构建 0 警告、0 错误；核心 264 项、完整 WPF 界面 378 项通过。新增配置检查覆盖空白配置、明确覆盖及错误值；既有登录协议、设置保存和皮肤操作回归通过。
- 日志：`artifacts/hidden-client-build.log`、`hidden-client-core.log`、`hidden-client-ui.log`。已查看 `artifacts/hidden-client-ui/DownloadSettings-ClientIdHidden.png`，下载源卡片布局正常，移除项没有留下占位。
- 发布目录 `dist/win-x64-0.10.15/`、便携包 `dist/IslandLauncher-0.10.15-win-x64.zip`。内置应用标识不会改变 Minecraft API 的服务端访问资格要求；真人账号全链路登录仍未实测。

## 0.10.14 Microsoft 浏览器 PKCE 登录

日期：2026-10-25。

- 登录 Microsoft 默认打开系统浏览器，使用授权码 + PKCE S256；窗口中保留设备码备用入口、重新打开、失败重试和取消。切换备用方式或关闭窗口取消原请求，迟到结果不会写入账号。两种流程继续使用应用自己的 Client ID，没有内置 HMCL 或其他启动器凭据。
- 本机回调使用仅绑定 IPv4 loopback 的随机 TCP 端口，避免依赖管理员 HTTP URL ACL；向微软提供 `http://localhost:{随机端口}/`。每次使用独立密码学随机 state / verifier，校验 state、Host、GET 方法、根路径与重复参数。错误状态及无关请求不能兑换授权码，也不会中断合法请求。成功、关闭、取消、五分钟超时均结束监听。回调页不显示授权码、state 或令牌，禁用缓存与外部资源。
- 授权码请求不含 client_secret，兑换使用原始 verifier 和相同回调；访问令牌不持久保存，刷新令牌沿用 Windows DPAPI。Microsoft → Xbox → XSTS 要求同一 uhs，再取得 Minecraft 令牌；先以 Bearer 查询 `entitlements/mcstore`，再读取有效 Java profile。资格接口失败、格式错误、档案缺失均停止；不单独用空 items 列表拒绝可能的订阅用户。已有设备码账号的刷新协议与配置保持兼容。
- Release 构建 0 警告 / 0 错误；核心 261 项、完整 WPF 界面 378 项、登录专项界面 8 项通过。核心新增 29 项覆盖 RFC 7636 标准向量、真实本机 HTTP 回调、错误 / 缺失 state、重复参数、外部 Host、错误方法 / 路径、取消、超时、重复兑换、Xbox 用户不一致、资格失败、无档案及刷新令牌不轮换。UI 检查使用注入的模拟授权，不打开真实微软登录页或操作用户账号。
- 日志：`artifacts/pkce-build.log`、`pkce-core.log`、`pkce-ui.log`、`pkce-full-ui.log`。已查看 `artifacts/pkce-ui/Microsoft-Browser-Login.png`，备用设备码和浏览器按钮均可见且没有溢出。官网的中英文登录 / 隐私说明同步为双流程，下载地址继续留空，没有部署网站。
- Azure 应用仍需由用户在“移动和桌面应用程序”中添加 `http://localhost`；备用设备码需允许公共客户端流程。没有修改用户 Azure 配置，也没有完成真人正版账号的全链路登录；Minecraft API 的实际访问资格仍须由服务端批准。
- 发布目录 `dist/win-x64-0.10.14/`，便携包 `dist/IslandLauncher-0.10.14-win-x64.zip`。真实发布 EXE 使用独立虚构配置运行森林绿、玫瑰红、流光玻璃三轮，每轮两种尺寸 / 五页共 10 张截图，均退出 0、没有错误文件。记录为 `artifacts/pkce-publish.log` 与 `pkce-published-captures.log`。EXE SHA256：`0c61943b6e27dfea3cf6b8836849d81d6437d4a7b9fd6f26bf452fc0b5767cb9`；ZIP 中 EXE 与磁盘发布文件哈希一致。

## 0.10.13 Java 检测缓存与路径失效恢复

日期：2026-10-25。

- 检测结果以独立技术缓存 `java-runtimes.json` 原子保存到启动器配置目录。新窗口直接恢复列表、自动 / 手动选中项；通过长度和修改时间验证缓存项，发现删除 / 变化后刷新。缓存限制版本、体积和条目数量，损坏时重建，写入失败保留可用结果并提示；并发请求共用一次检测。首次、显式重新检测、缓存失效或找不到兼容候选时才完整扫描，启动前只探测实际选择的 Java。
- 全局 / 独立已保存路径删除时显示完整路径提示并自动重新检测；失效的手动路径改为自动选择并持久化，独立实例保留独立内存等设置。打开启动器后才删除路径时，也会在实际启动前恢复并记住兼容替代 Java。自动选择始终按版本和位数验证；有效但不兼容的手动 Java 继续报错，原兼容检查跳过选项保持可用。没有兼容 Java 时显示安装 / 选择提示，取消时不继续启动。
- 全局和独立设置共用检测列表；刷新列表保留表单里未保存的 Java / 内存值，技术缓存更新不提交其他草稿。旧的内存反馈 Popup 复用于 Java 非阻塞提示，原内存清理和纸娃娃入口行为保留。
- Release 构建 0 警告、0 错误，核心回归 232 项、完整界面回归 369 项通过；8 项定向界面检查通过。覆盖跨对象恢复且不扫描 / 不探测、单候选校验、删除自动 / 手动路径、版本变化、缓存损坏 / 写入失败 / 取消、一次扫描失败退出，以及全局 / 实例界面恢复、提示进度、草稿隔离、手动重新检测与重启复用。
- 完整界面回归另包含：在窗口打开后删除指定 Java，通过启动按钮真实启动本地 Java 测试游戏，确认先提示 / 重新扫描，持久保存自动选择及替代路径，真实 JVM 输出成功并退出。所有 Java 删除仅针对本轮创建的元数据测试文件，未删除或修改用户实际 JDK；真实测试 JVM 使用已安装的有效 Java，不启动用户的完整整合包。
- 日志：`artifacts/java-cache-build.log`、`java-cache-core.log`、`java-cache-targeted.log`、`java-cache-ui.log`。已查看完整回归的 `artifacts/java-cache-ui/Java-Missing-Recovered.png`，缺失路径和恢复结果显示在全局 Java 卡片内。
- 发布目录 `dist/win-x64-0.10.13/`、便携包 `dist/IslandLauncher-0.10.13-win-x64.zip`。通过独立离线配置实际检测并保存本机 13 个 Java 环境，再连续运行真实发布 EXE 两次；每次完成两种尺寸、五页共 10 张截图，均退出 0、无异常，缓存内容哈希与修改时间均保持不变，确认跨进程复用结果。脚本为 `artifacts/verify-published-java-cache.ps1`，记录为 `artifacts/java-cache-published-check.log`，两轮截图在 `artifacts/java-cache-published/first/`、`second/`。EXE SHA256 为 `133defcec7716bfd15138991af9c32931a659763cceec82396071300637e5254`，便携包内 EXE 与发布文件哈希一致。

## 0.10.12 卡片动画选择

日期：2026-10-25。

- 将卡片动效复选框替换为六个单选按钮：关、悬浮跟随、轻盈上浮、柔和缩放、弹性跃动、磁吸跟随，附所选效果说明，沿用主题按钮样式，不使用下拉菜单。点击即时应用并保存，仅保存交互偏好，不提交未保存的自定义颜色 / 材质草稿。
- 保留原抬升 / 倾斜 / 鼠标跟随；新增仅上浮、仅中心缩放、一次弹性超调后回落、按鼠标方向轻微平移。切换效果同步清除旧变换、阴影和缓存；移出平滑归位并释放缓存，快速移出 / 返回不会被旧完成回调清除新效果；静止卡片不订阅逐帧事件或启动循环动画。
- 新增枚举偏好，旧 `CardHoverEffects` false / true 分别迁移为关 / 原效果。新字段在任意 JSON 属性顺序中优先，未知值回退原效果；同时写入一致的旧开关供旧版读取。预设主题匹配、切换和自定义保存均保留当前动画。
- Release 构建 0 警告、0 错误；核心回归 217 项、完整界面回归 359 项通过。动画检查覆盖六个入口与两种窗口宽度、实际 WPF 变换 / 弹性超调 / 磁吸方向 / 复位、即时保存、切换主题、快速返回、保存失败回滚、自定义草稿隔离和自定义保存保留动画。
- 已查看 `artifacts/card-animations-targeted/CardAnimation-Settings-1020.png` 与 `CardAnimation-Spring-Bounce.png`；1020px 下所有入口均在设置卡片内且无需横向滚动。构建、核心、定向及完整日志分别为 `artifacts/card-animations-build.log`、`card-animations-core.log`、`card-animations-targeted.log`、`card-animations-ui.log`。
- 发布目录 `dist/win-x64-0.10.12/`、便携包 `dist/IslandLauncher-0.10.12-win-x64.zip`。真实 EXE 使用独立离线配置和磁吸动画设置，完成两种尺寸、五个页面共 10 张截图，退出 0、无异常。已查看发布版 1020px 玻璃主题设置页，磁吸选中状态、说明和所有选项显示正常；截图为 `artifacts/card-animations-published/`，检查日志 `artifacts/card-animations-published-check.log`。EXE SHA256 为 `49a3a1b7fd22dc6e85fa20152f991900b69e3aafd21f8f862f7414adc423b37a`，便携包内 EXE 与发布文件哈希一致。

## 0.10.11 GregTech Odyssey / HMCL 启动参数

日期：2026-10-25。

- 用户实例的 Forge JVM 参数包含 HMCL 加入的 `${primary_jar_name}`。新增按 `ResolvedVersion.ClientJar` 的实际文件名展开，保留原 `ignoreList`，支持空格、中文、实例重命名及继承父版本 JAR；未知占位符仍明确报错。
- 实际 GTOCore 0.5.6-beta 入口 `com/gtocore/Core.class` 的 class major 为 65（Java 21），但游戏配置仍声明 Java 17。版本读取按当前隔离设置检查实际运行目录中已启用 `gtocore*.jar` 的该入口类，只提高最低 Java 要求；不硬编码整合包名称或最低 21，不采用多版本可选类、禁用模组或其他运行目录中的模组。
- Release 构建 0 警告、0 错误，核心回归 207 项、完整界面回归 334 项通过。新增覆盖实际父 JAR 与选中版本名称不同、真实 JVM 收到中文 / 空格参数、独立配置、未知占位符拒绝，以及 GTOCore 要求、共享 / 独立目录、禁用、可选多版本类和旧 Java 17 构建。真实实例探针还传入上次自动记住的 Java 17 路径，确认要求提高后拒绝复用并匹配 Java 21。
- 从指定 GregTech Odyssey 目录只读复制版本配置、必要库、客户端 JAR 和 GTOCore 至 `artifacts/odyssey-fixture/`。按实际配置准备参数 / 原生库，自动选择 Java 21.0.6。真实 JVM 保留原模块参数，确认 BootstrapLauncher / SecureJarHandler 模块和 BootstrapLauncher、ModLauncher、FMLLoader 类加载成功，退出 0。复现脚本为 `artifacts/verify-odyssey-compatibility.ps1`，日志为 `artifacts/odyssey-installed.log`、`odyssey-fixture/forge-bootstrap.log`；原配置哈希保持不变，原目录、设置和存档未写入。
- 验证边界：真实 JVM 使用受控测试主类完成模块和类加载，没有进入完整整合包的主菜单或世界。之前尝试实际 Forge 启动时已到达 FML 扫描阶段，但独立测试副本没有复制全部安装处理器产物，故不将该次启动视为整包启动成功；最终探针有明确成功断言，不依赖预期异常。
- 构建日志 `artifacts/odyssey-build.log`，核心测试 `artifacts/odyssey-core.log`，界面回归 `artifacts/odyssey-ui.log`。发布目录 `dist/win-x64-0.10.11/`，便携包 `dist/IslandLauncher-0.10.11-win-x64.zip`。真实发布 EXE 使用独立离线配置完成两种尺寸、五个页面共 10 张截图，退出 0、无异常；截图 `artifacts/odyssey-published/`，检查日志 `artifacts/odyssey-published-check.log`。EXE SHA256 为 `628cabd48e641cc05176fc13fe06603050a873971864ace9c757d693a48c6139`，便携包中的 EXE 与发布文件哈希一致。

## 0.10.10 游戏运行时切换目录

日期：2026-10-25。

- 修复目录标签在整个游戏运行期间禁用，以及切换事件再次拒绝操作的双重限制。游戏进程启动后，标签和切换事件允许浏览其他游戏目录；准备 / 下载时仍锁定目录切换。目录管理、实例修改和再次启动继续沿用原有运行中限制。
- 切换目录时保留「游戏运行中」状态；只更新当前浏览目录、收藏、搜索与实例选择，不改变已经捕获的启动目录、进程、任务、日志及游玩记录。退出后保留当前浏览目录，不跳回原目录。
- Release 构建 0 警告、0 错误，完整界面回归 335 项通过。新增持续运行的真实 Java 测试进程，覆盖运行中目录切换、跨目录收藏、搜索、空目录进出、禁止重复启动与修改目录设置，以及切换后结束原游戏、历史记录归属和浏览位置保留。
- 日志：`artifacts/running-browse-build.log`、`running-browse-ui.log`。已查看真实进程运行期间切换到其他目录的界面截图：`artifacts/running-browse-ui/Running-Game-FolderSwitch.png`。测试仅操作隔离的本地测试 JVM 和临时目录，未启动或结束用户正在游玩的 Minecraft。
- 发布目录 `dist/win-x64-0.10.10/`、便携包 `dist/IslandLauncher-0.10.10-win-x64.zip`。真实 EXE 使用独立离线配置正常运行，完成两种尺寸、五个页面共 10 张截图，无异常，位于 `artifacts/running-browse-published/`。发布检查日志为 `artifacts/running-browse-published-check.log`，EXE SHA256 为 `e468c3cf592b6017d8c16add405933582910d4b690f0a48c9ae7a4c1054b8bdc`。

## 0.10.9 实例右键菜单

日期：2026-10-24。

- 移除底部启动按钮旁的「管理实例」按钮，底栏保留实例摘要与启动按钮。为每个实例卡片提供右键菜单，依次为「实例管理」「Mod 管理」、分隔线、「游戏目录」「存档」「资源包」「光影」。双击实例管理继续保留，右键菜单采用现有主题样式。
- 菜单打开时明确记录所属卡片并选中它，所有操作通过该卡片执行，避免使用旧的列表选中项。管理入口在任务 / 游戏进行中禁用，文件夹入口使用实际隔离 / 共享运行目录，子目录不存在时创建后打开；Mod 管理直接打开该实例的模组管理窗口。卡片生成时即初始化菜单，加载事件作补充，避免切换目录后的菜单初始化时序问题。
- Release 构建 0 警告、0 错误。7 项定向检查通过：六项入口与顺序、未选中 / 跨目录同名卡片的目标、底部按钮移除、共享目录四种文件夹、独立存档目录、直接 Mod 管理的实际文件列表、实例管理窗口。文件夹测试注入打开回调，仅创建测试目录，不唤起资源管理器或改动真实游戏目录。
- 已查看菜单与窄窗口底栏截图：`artifacts/instance-menu-targeted/Instance-ContextMenu.png`、`Instance-Context-Library-1020.png`；模组管理截图为 `Instance-Context-Mods.png`。构建与定向日志分别为 `artifacts/instance-menu-build.log`、`instance-menu-targeted.log`。已有完整界面回归中的实例管理入口同步改为右键菜单。
- 完整界面回归 325 项通过，包括切换目录后立即进入管理、跨目录收藏、独立 Java / 内存设置与真实测试 JVM 启动。完整日志为 `artifacts/instance-menu-ui.log`，截图位于 `artifacts/instance-menu-ui/`。
- 发布目录 `dist/win-x64-0.10.9/`、便携包 `dist/IslandLauncher-0.10.9-win-x64.zip`。真实发布 EXE 以独立离线配置正常运行，完成两种尺寸、五个页面共 10 张截图，无异常；已查看主界面确认底栏仅保留启动按钮。截图与日志分别位于 `artifacts/instance-menu-published/`、`instance-menu-published-check.log`。EXE SHA256 为 `af86d940b4b804c4c24ea198f14276aa57852f8bd97a4ee1b5ea7617325f7e14`。

## 0.10.8 启动前清理反馈、启动准备与 GTNH / Cleanroom

日期：2026-10-24。

- 排查用户本机已保存配置时，启动前清理开关为关闭状态；未改写用户配置。新版本该开关点击即保存，只提交这个选项。自动清理时显示进度 Popup，等待辅助进程结束，显示可用内存净增 MB 与前后 GB，再继续分配内存和启动 Java。失败 / 拒绝授权不声称释放成功，取消启动任务不启动游戏。设置页、纸娃娃与启动日志复用结果。
- GTNH 的旧原生库 classifier 缺少 `path` 字段，旧补全逻辑把它指向 `libraries/` 目录。启动器改为依 Maven 坐标计算实际文件路径，并保留 HMCL 已转换的系统类加载器、主类与模块参数。未修改共享下载器源码。
- 用户的 Cleanroom 0.3.31-alpha 配置沿用基础 Minecraft 的 Java 8 声明；原日志确实报 Foundation 类版本 65（Java 21）无法由 Java 8 加载。新增实际运行要求识别，并将已有、但 `downloads: {}` 的本地 Cleanroom 主库保留在类路径。
- Java 检测在当前进程中复用搜索结果，每次只重新探测所选 Java；实例另存上次自动选择的路径，跨进程复用仍校验实际版本 / 位数，失效或要求提高时重新匹配，不构成手动或独立覆盖。重新检测会清除记住的自动选择。依赖 / 资源缓存以长度、修改时间、预期哈希共同失效；更新或损坏的文件会重新校验 / 修复。启动准备日志从读取配置前开始，记录阶段耗时和失败，游戏输出改为缓冲写入。
- Release 构建 0 警告、0 错误，核心 197 项与完整界面 319 项通过。新增检查覆盖清理净增量 / 零增量 / 失败 / 无读数、点击即保存、弹窗进度与结果、等待清理再启动真实测试 JVM、旧 native 路径、Cleanroom 类路径、自动 Java 持久化与复用校验、文件缓存复用 / 失效 / 修复 / 取消。
- 从用户提供的两个目录只读复制配置、库和必要资源到 `artifacts/compat-real-fixtures/`。实际生成启动参数并解压原生库：GTNH 2.8.4 自动选择 Java 25.0.3，HMCLMultiMCBootstrap 与 RetroFuturaBootstrap.Main 类加载成功；NovaEng-CleanroomMC 自动选择 Java 21.0.6，Foundation 与 Cleanroom 主类加载成功，两个 JVM 均退出 0。原游戏目录、存档与设置保持原样；这些用户游戏文件不进入发行包。
- 独立副本在禁止联网的处理器下完成依赖与资源检查。首次 / 复用耗时：GTNH 2.060 / 1.603 秒，Cleanroom 3.277 / 2.498 秒；全机 Java 检测 / 单 Java 复查为 5.023 / 0.120 秒。以上仅为本机准备阶段样本，不含游戏主菜单加载，不是与其他启动器的同条件对比。
- 日志：`artifacts/compat-launch-build.log`、`compat-launch-core.log`、`compat-launch-ui.log`、`compat-installed.log`、`compat-preparation.log`、`compat-preparation-times.json`。已查看真实 WPF 小弹窗截图 `artifacts/compat-launch-ui/PreLaunch-Memory-Progress.png`、`PreLaunch-Memory-Result.png`。
- 验证边界：界面清理结果由测试注入，没有弹出 UAC 或实际回收系统内存；管理员成功清理的交互实测仍未覆盖。GTNH / Cleanroom 已验证启动准备和实际 Java 类加载，未运行完整模组列表进入主菜单。原始 Prism ZIP 自定义组件导入能力未在本轮扩展。
- 发布目录 `dist/win-x64-0.10.8/`、便携包 `dist/IslandLauncher-0.10.8-win-x64.zip`。真实发布 EXE 使用独立离线配置正常启动，完成两种尺寸、五个页面共 10 张截图，无异常，位于 `artifacts/compat-launch-published/`；已查看主界面、右侧世界封面与新版版本号。确认当前令牌不含所需权限后，直接辅助模式返回 12，未请求提权或清理系统内存。发布检查日志为 `artifacts/compat-launch-published-check.log`。EXE SHA256 为 `d2e04f45c594cc2ddad31a5c6c405d527ac02faac2c40e11e8ab4b5ba2985baa`。

## 0.10.7 右侧方形世界缩略图

日期：2026-10-24。

- 地图卡片恢复配置的背景 / 材质，左侧显示地图名称与时间，右侧单独显示 64 × 64、8 像素圆角的封面。移除整卡片铺图、暗色蒙层及封面触发的固定白色文字，文字继续跟随主题。缺图 / 损坏时隐藏右侧方块，保留地图信息。
- Release 构建 0 警告、0 错误。复用并调整现有启动选项定向检查，10 项通过；验证 1240 与 1020 宽度下的缩略图尺寸、位于文字右侧且互不遮挡、圆角裁切和缺图回退。已查看窄窗口截图，地图名称可截断显示，时间完整保留。
- 日志：`artifacts/world-square-build.log`、`world-square-targeted.log`。截图位于 `artifacts/world-square-targeted/RecentWorldThumbnail-1240.png` 与 `RecentWorldThumbnail-1020.png`。本次为地图卡片布局调整，未更改 GC、启动命令、GPU 或登录流程。
- 发布目录 `dist/win-x64-0.10.7/`、便携包 `dist/IslandLauncher-0.10.7-win-x64.zip`。真实 EXE 使用独立离线配置完成两种尺寸、五个页面共 10 张截图，正常退出；已查看实际 `new26.2` 的右侧正方形封面与恢复的卡片材质。截图与日志分别位于 `artifacts/world-square-published/`、`world-square-published-check.log`。EXE SHA256 为 `c1a25045551d4ffa96f2d11ea1d1a1521bb2f85a99171d2ba054de106483afff`。

## 0.10.6 世界封面与高级启动设置

日期：2026-10-24。

- 上次游玩的地图从所选存档的 `icon.png` 读取封面，沿用实际隔离 / 共享游戏目录。缺图、损坏或超出限制时回退主题背景；不改动存档。地图名、时间与暗色文字蒙层保留，卡片图片按圆角裁切。
- 全局与独立实例均可保存游戏参数、启动前命令、GC 模式、两个兼容组件禁用开关、GPU 偏好与登录证书验证。默认保留 SSL 系统校验；高性能 GPU 偏好默认关闭。现有 Java 列表与重新检测入口保留。
- Release 构建 0 警告、0 错误，核心 183 项、完整界面回归 317 项通过。核心检查覆盖新旧配置默认值、独立设置、GC 的 Java 版本矩阵与冲突处理、兼容组件缓存修复、登录校验域名范围和 GPU 偏好恢复；界面检查覆盖表单往返、五种 GC 选项、设置保存及实际启动、两种宽度的缩略图和缺图 / 损坏回退。
- 真实 cmd 命令验证包含中文 / 空格工作目录、相对文件写入、非零退出码、启动前取消与执行中取消；执行中取消会结束本次命令的进程树。真实 Java 进程验证 GC 选择与含空格的中文游戏参数；Java 25+ 实际加载内置 LWJGL Agent 2.0 后成功运行测试主类。完整 UI 流程还验证继承全局设置的游戏启动执行了前置命令并传入新增参数。
- 日志：`artifacts/launch-options-build.log`、`launch-options-core.log`、`launch-options-ui.log`。新增控件与 GC 展开截图位于 `artifacts/launch-options-targeted/`，完整回归截图位于 `artifacts/launch-options-ui/`。已查看高级选项和 1020 宽度的封面 / 圆角 / 文字布局。
- 验证边界：GPU 测试注入独立内存存储，不修改本机真实图形偏好，未确认具体显卡选择；正常恢复及保留运行期间用户改动已验证。SSL 关闭模式只验证回调行为，未使用真实账号连接异常证书服务。旧 Java 与 Windows UTF-8 系统代码页组合下的 Wrapper 未进行真实环境启动，已验证条件与内置组件读取。未以本轮测试替代各整合包的实际游戏兼容验证。
- 发布目录 `dist/win-x64-0.10.6/`、便携包 `dist/IslandLauncher-0.10.6-win-x64.zip`。真实发布 EXE 以独立离线配置启动，完成两种尺寸、五个页面共 10 张截图，无异常，位于 `artifacts/launch-options-published/`；已查看真实 `new26.2` 存档封面、文字与设置布局。EXE SHA256 为 `f7ac15fef0c9402f209cf9700f0329a69fe306287f0a47da99ad85680bed3d63`，发布检查日志为 `artifacts/launch-options-published-check.log`。发布包附带两个兼容组件的完整许可。

## 0.10.5 纸娃娃菜单清理内存

日期：2026-10-24。

- 在主界面纸娃娃右键菜单底部增加「清理内存」，用分隔线与动作 / 重置视角区分。只为主界面实例菜单连接该操作，账号皮肤配置中的通用预览不暴露未连接的系统操作。
- 菜单和设置按钮复用同一个清理入口，任务 / 游戏运行期间同步禁用，清理时显示「清理中…」，结束或取消后恢复。不会跳转页面、启动游戏或提交设置草稿。动作选择和重置视角继续保留。
- 结果通过纸娃娃旁的独立 Popup 显示五秒后收起；失败、拒绝授权和取消同样显示，设置卡片及日志继续保留结果。新清理开始时关闭旧结果，窗口关闭时停止计时器并关闭 Popup，最小化时不弹出。未更改原生内存命令或权限策略。
- Release 构建 0 警告、0 错误。核心 156 项、完整界面回归 304 项通过。补充弹窗所属窗口暂时禁用的状态检查后，菜单定向检查共 11 项通过：保留原菜单、不向通用预览注入未连接操作、所属窗口重新启用后菜单可继续使用、同步忙碌状态、防重复、原页可见反馈、不改配置 / 启动游戏、拒绝授权、异常、取消与五秒自动收起。
- 日志：`artifacts/doll-memory-build.log`、`doll-memory-core.log`、`doll-memory-ui.log`、`doll-memory-targeted.log`。已查看真实 WPF 菜单及结果截图：`artifacts/doll-memory-targeted/Doll-Memory-ContextMenu.png`、`Doll-Memory-Feedback.png`。测试注入清理结果，不弹 UAC 或实际清理系统内存；此前管理员成功清理尚未交互实测的限制仍适用。
- 发布目录 `dist/win-x64-0.10.5/`、便携包 `dist/IslandLauncher-0.10.5-win-x64.zip`。实际发布 EXE 以独立离线配置完成两种尺寸、五个页面共 10 张截图，无启动异常，位于 `artifacts/doll-memory-published/`；普通权限辅助模式返回 12。发布检查日志为 `artifacts/doll-memory-published-check.log`，EXE SHA256 为 `1b42db7a46369fadf1aa002e6c7b76c4d06bf3b83b845001452c26ef4be6249f`。

## 0.10.4 内存分配布局

日期：2026-10-24。

- 按用户参考图将内存设置独立为卡片，提供「自动配置 / 自定义」单选、自定义滑块、启动前优化与右侧立即清理、已用 / 预计分配 / 空闲分段条，以及 GB 读数。重要数值为 16 号字，权限提示转入控件提示，清理反馈按需出现。Java 保持独立卡片。
- 全局和实例设置共用 MemoryAllocationEditor。自定义以 256 MB 步长调整并支持键盘；自动模式禁用滑块、显示建议量并保留自定义值。新布局继续保存原有 AutoMemory / MemoryMb，不改动 JSON 格式、启动分配算法或原生内存清理实现。实例关闭独立设置后恢复全局继承。
- 可见时每两秒刷新系统读数，隐藏 / 卸载时停止计时器，最小化时跳过读取。预览考虑所选实例的启用模组数；启动仍重新匹配 Java 并计算实际内存。过量分配不使统计图越界，内存不足时不显示伪造的建议值，旧的高手动上限不会被打开页面的操作降低。
- Release 构建 0 警告、0 错误。核心 156 项、完整界面回归 292 项通过，覆盖全局保存、独立 / 继承、真实测试 JVM 的手动及自动内存启动，以及立即清理。焦点样式补充后又完成 13 项定向检查，包括自动 / 自定义、保留草稿、真实 Thumb 拖动、256 MB 方向键步长、实时读数、内存不足、超出可用值与 1240 / 1020 两种宽度。
- 日志：`artifacts/memory-allocation-build.log`、`memory-allocation-core.log`、`memory-allocation-ui.log`、`memory-allocation-targeted-v2.log`。对照证据位于 `artifacts/memory-allocation-targeted/`，原图副本为 `artifacts/memory-allocation-reference.png`，对照报告为 `design-qa.md`（passed）。没有触发管理员 UAC 或清理当前系统内存。
- 发布目录 `dist/win-x64-0.10.4/`、便携包 `dist/IslandLauncher-0.10.4-win-x64.zip`。实际发布 EXE 以独立离线配置完成两种尺寸、五个页面共 10 张截图，无启动异常，位于 `artifacts/memory-allocation-published/`；普通权限辅助模式返回 12。发布日志为 `artifacts/memory-allocation-published-check.log`，EXE SHA256 为 `7122eac380ae5575998510f861df8962edcb6657402a50b020afb75a6c798af6`。

## 0.10.3 设置页立即清理按钮

日期：2026-10-24。

- 在「启动游戏前清理内存」同一行右侧增加「立即清理」。自动清理未启用时也可直接点击，无需保存，不会提交尚未保存的 Java、内存或外观草稿，也不会启动游戏。
- 手动清理和启动前清理复用同一个异步清理流程及原有 Windows 辅助程序。按钮在清理时显示「清理中…」并禁用；其他任务或游戏运行中同样禁用，处理器也拒绝重复调用。结束后恢复按钮，当前卡片显示结果与可用内存前后实时读数，手动结果不会显示「继续启动」。结果出现时自动滚动到反馈区域。
- 成功、部分成功、拒绝授权、异常与取消均在卡片内反馈；启动前清理仍按原策略处理失败并继续游戏。没有更改原生内存清理命令、权限策略、下载器源码或用户游戏文件。
- Release 构建无警告、无错误；核心 156 项检查通过，完整界面回归 281 项通过；结果自动滚动调整后又通过 13 项手动清理定向检查。覆盖立即执行、草稿保留、无游戏进程、忙碌防重复、取消恢复、失败反馈，以及 1240 / 1020 两种宽度下按钮在右侧且不覆盖开关。新增的核心检查验证手动结果不带启动提示且保留错误详情。
- 日志：`artifacts/memory-button-build.log`、`memory-button-core.log`、`memory-button-ui.log`、`memory-button-targeted.log`。两种尺寸截图为 `artifacts/memory-button-targeted/MemoryCleanupNow-1240.png` 和 `MemoryCleanupNow-1020.png`。测试通过注入清理结果验证按钮与启动流程，未触发 UAC 或实际清理系统内存；0.10.2 中记录的管理员清理实测限制继续适用。
- 发布目录 `dist/win-x64-0.10.3/`、便携包 `dist/IslandLauncher-0.10.3-win-x64.zip`。实际发布 EXE 以独立离线配置捕获两种尺寸、五个页面共 10 张截图，无启动异常，位于 `artifacts/memory-button-published/`；普通权限实际调用辅助模式返回 12。检查日志为 `artifacts/memory-button-published-check.log`，EXE SHA256 为 `3f967a89993d1089bfc3913caf4531bb6419058603b81b642924e7256d176c17`。

## 0.10.2 启动前内存清理

日期：2026-10-24。

- 在全局 Java 与运行内存卡片新增「启动游戏前清理内存」。新配置与旧配置升级均默认关闭，保存后生效，重新打开可恢复；保存失败会恢复开关原状态。未修改下载器源码或用户游戏文件。
- 普通启动与一键继续共享入口：身份验证、Java 匹配及文件补全通过后，清理一次系统工作集与备用列表，再读取实时可用内存进行自动分配，最后启动 Java。手动内存模式也可使用。分别判定两项操作，部分成功、权限不足、失败或取消 UAC 均记录结果并继续启动；取消整个启动任务不启动 Java。
- 短时辅助进程只接受固定 `--clean-memory-helper` 参数，通过 Windows UAC 取得权限，执行两项内存命令后退出。不读取启动器配置、接受外部文件参数、修改权限策略、清除游戏缓存或启动 Java；启动器及游戏维持原权限。已开始的系统调用完成并回收辅助进程后处理启动取消。
- PCL 的公开源码包含启动前内存优化入口，但未公开清理实现。本次独立实现 Windows NtSetSystemInformation(SystemMemoryListInformation) 命令 2 / 4；仅在辅助进程启用已有 SeProfileSingleProcessPrivilege，检查 AdjustTokenPrivileges 返回值及 ERROR_NOT_ALL_ASSIGNED。来源见 REFERENCES.md。
- Release 构建 0 警告、0 错误。核心 155 项通过，新增 11 项覆盖新旧配置、保存开关、命令顺序、权限不足、部分失败、全部失败及取消。完整 WPF 界面回归 268 项通过，以真实测试 JVM 验证两种启动入口、清理先于内存分配、失败与拒绝授权继续启动、取消启动不创建游戏进程，以及设置草稿、保存和恢复。界面测试注入清理结果，不执行系统内存清理或弹 UAC。
- 非管理员实际运行辅助模式，未拥有 SeProfileSingleProcessPrivilege 时退出码为 12，确认真实 Windows 令牌接口及权限不足路径正常，未创建启动器窗口。没有触发交互式 UAC 或实际清理当前系统内存，因此管理员授权与成功清理路径尚未在本机交互实测；不承诺释放固定内存量或提升帧率。
- 日志：`artifacts/memory-cleanup-build.log`、`memory-cleanup-core.log`、`memory-cleanup-ui.log`、`memory-cleanup-native-probe.log`。截图：`artifacts/memory-cleanup-ui/MemoryCleanupSettings.png`，已检查开关与说明文字布局。
- 发布为 `dist/win-x64-0.10.2/岛屿启动器.exe` 与 `dist/IslandLauncher-0.10.2-win-x64.zip`。实际发布 EXE 使用独立离线配置只读加载已有游戏目录，捕获两种尺寸、五个页面共 10 张图（`artifacts/memory-cleanup-published/`），界面可正常打开；发布版辅助模式在普通权限下亦返回 12。产品版本为 0.10.2，EXE SHA256 为 `b78b7a0b6ab7fe807a74f3cf614010c4946872a66a841d6fc3ead950b79f4055`。发布验证日志为 `artifacts/memory-cleanup-published-check.log`。

## 0.10.1 导入实例加载器误标修复

日期：2026-10-24。

- 根因：共享安装列表原先只识别 `net.minecraftforge:forge` 和 Forge 主类；实际的 CT / ATM9 / gto / TTF 配置使用 BootstrapLauncher 与 `net.minecraftforge:fmlloader`，因此被漏判。Cleanroom 的库和 patch 标记也未覆盖。
- 新增启动器独立的实例识别补充层，识别 Forge fmlloader、NeoForge FML、Cleanroom、Fabric、Quilt 和 LiteLoader。优先使用实际运行配置，沿安全的父版本路径查找，限制继承层数并处理循环、缺失和损坏。未知 ModLauncher 不再显示为原版，名称和通用 Mixin / 工具库不作为加载器证据。不改动下载器共享源码、安装选项或游戏文件。
- 卡片、搜索、最近游玩和底部详情使用同一份纠正后的实例身份，补齐 Cleanroom / Quilt 的备用图形与颜色。9 项新增核心检查覆盖现代 Forge、陈旧 Vanilla 元数据、共享库误判、继承、缺失父版本、Cleanroom patch、循环 / 路径校验和配置只读；核心总计 144 项通过。Release 构建成功，0 警告、0 错误。
- 对 `D:/mc/.minecraft` 做只读实目录复核：CTIIIIIIIII、ATM9、gto、TTF、NovaEngineering-World 为 Forge；NovaEng-CleanroomMC 为 Cleanroom；Fabric 与 NeoForge 实例继续保持正确身份。日志为 `artifacts/loader-detection-build.log`、`loader-detection-core.log`、`loader-detection-local.log`。未通过启动游戏来验证本次显示修复。
- 另一个已配置目录中的 GregTech Odyssey、GT New Horizons 2.8.4 为 Forge，NovaEng-CleanroomMC 为 Cleanroom。完整界面回归 256 项通过，日志为 `artifacts/loader-detection-ui.log`，截图位于 `artifacts/loader-detection-ui/`。
- 发布目录 `dist/win-x64-0.10.1/`、便携包 `dist/IslandLauncher-0.10.1-win-x64.zip`。实际运行发布 EXE，使用独立配置只读加载上述游戏目录，捕获两种尺寸、五个页面共 10 张截图，位于 `artifacts/loader-detection-published/`；确认 CT 的 Forge 标签、备用图形、最近游玩及底部详情同步更新，ATM9 保持纠正后的 Forge 身份。
- 文件版本 `0.10.1.0`，EXE SHA256 为 `980e5f9e26592c240a39fa17ba7f2193e19ec3967acb96795cb5b86ca93b640f`，ZIP 内 EXE 与发布目录校验一致，旧版本发布目录保留。

## 0.10 启动栏弹出、卡片动效与材质精简

日期：2026-10-24。当前为 0.10 开发版，正式版计划为 1.0。

- 「一键继续」复用「添加实例」的 Primary 按钮样式、颜色、字体大小和内边距，外观小窗口同步绘制强调色按钮。
- 启动器设置新增底部启动栏「常驻显示 / 鼠标靠近时弹出」，即时保存。靠近底部 104 像素触发，离开至 144 像素外收起，保留回差减少边缘抖动；220 ms 上滑淡入、170 ms 收起。隐藏时释放原底部布局行，启动栏不拦截点击；鼠标快速进出会从当前动画状态反转。焦点进入可显示，F6 提供键盘入口，离开实例页复位。
- 实例卡片增加小幅抬升、放大、鼠标跟随倾斜 / 转动与柔和阴影。开关即时保存，关闭或卸载立即取消动画与缓存；离开时平滑复位。悬浮期间缓存卡片，暂停逐帧背景位置取样，复位后释放缓存，避免为玻璃反复重建背景。切换预设保留启动栏模式、卡片动效及纸娃娃显示偏好。
- 外观材质仅保留纯色 / 图片、高斯模糊、液态玻璃，旧数字 2–5 保留用于迁移为高斯，旧云母强模糊取 64；保留图片、颜色与透明度。停止启用原生亚克力 / 云母并清理旧 DWM 背景，整体透明度、窗口圆角和缩放保留。自定义微调仍先预览、保存后应用。
- Release 构建成功，0 警告、0 错误。核心检查 135 项、完整界面回归 255 项通过；动效专项 15 项检查涵盖真实动画中间值、快速进出、隐藏点击穿透、F6、鼠标倾斜反向、关闭立即复位、主题切换保留偏好及两种宽度布局。动效截图位于 `artifacts/010-motion-ui/`，完整回归截图位于 `artifacts/010-full-ui/`，日志为 `artifacts/010-build.log`、`artifacts/010-core.log`、`artifacts/010-full-ui.log`、`artifacts/010-motion.log`。额外复查首行卡片动效留白与 1020 / 1240 宽度下的启动栏位置。
- 发布目录 `dist/win-x64-0.10/`、便携包 `dist/IslandLauncher-0.10-win-x64.zip`。实际运行发布 EXE，分别加载常驻 / 自动弹出两份独立配置，并通过预设按钮启用流光玻璃；两种尺寸、五个页面各捕获 10 张截图，共 20 张，位于 `artifacts/010-published/` 与 `artifacts/010-auto-published/`。已查看两种启动栏模式、三列卡片、统一后的继续按钮及设置控件；发布版的模式偏好在切换预设后保留。最后将模式选项统一为主题按钮样式，复查深色玻璃下的文字可读性。
- 文件版本 `0.10.0.0`，EXE SHA256 为 `489a6490dc6c1c46e9fbd3cec9cae7fd355eadba3f776a837eecb9af78978ffc`，ZIP 内 EXE 与发布目录校验一致。未修改实际用户配置或覆盖旧版发布目录。

## 0.9.6 即时预设与内置流光玻璃

日期：2026-10-24。

- 设置页提供 17 种预设，点击即保存并应用整套外观，不再依赖预设卡片的保存按钮。普通配色恢复其纯色外观，新增「流光玻璃」包含 0.9.5 展示的彩色花瓣背景、青蓝强调色、各区域玻璃材质、透明度与蒙层参数；保留账号、玩家皮肤、纸娃娃显示开关及运行设置。
- 将本项目程序绘制的 1400×900 背景作为嵌入资源加入程序，按内容哈希复制到配置目录 `Backgrounds/`，无网络或测试路径依赖。再次选择可修复内置背景的缺失 / 损坏，原始背景图片文件保留。没有内置或替换测试玩家皮肤。
- 隐藏纸娃娃开关即时保存，但只提交这个开关，不提交正在微调的自定义颜色、材质和透明度。自定义页面保持先预览、保存成功后应用；选择预设后同步预览与编辑控件，替换此前未保存的自定义草稿。预设及开关保存失败均回滚界面状态，不更改实际外观，不丢失自定义草稿。
- 活动预设标记同时匹配颜色和整套外观，避免仅颜色相同就将自定义方案误标为预设。设置主保存入口也同步刷新标记。预设及纸娃娃开关不验证或保存尚未提交的 Java / 内存表单。
- 即时预设专项 20 项通过，涵盖四色即用、明亮按钮文字、未完成运行设置、独立开关与草稿隔离、两类写入失败、内置背景解码和修复、整套参数、重启恢复、玻璃切换回纯色及 1020 / 1240 宽度布局。已查看 `artifacts/instant-presets-ui/QuickAppearance-1020.png` 和 `GlassPresetSettings.png`，17 个按钮可换行，纸娃娃开关可见。
- Release 构建成功，0 警告、0 错误；完整界面回归 251 项通过，包含既有启动、账号 / 皮肤、模组、窗口、材质、微调草稿与保存失败流程。日志为 `artifacts/instant-presets-build.log`、`artifacts/instant-presets-ui.log`、`artifacts/instant-presets-full-ui.log`，完整截图位于 `artifacts/instant-presets-full-ui/`。本次核心业务仅更新版本标识，未重复核心业务回归，未修改用户实际配置。
- 发布目录 `dist/win-x64-0.9.6/`、便携包 `dist/IslandLauncher-0.9.6-win-x64.zip`。从独立普通外观配置实际运行发布 EXE，通过截图诊断触发「流光玻璃」按钮，确认即时保存的背景位于该配置的 `Backgrounds/`、哈希与嵌入 PNG 一致、无测试图片路径依赖，账号皮肤字节不变。捕获 1020 / 1240 两种尺寸、五个页面共 10 张截图，位于 `artifacts/instant-presets-published/`；已查看设置页 17 个按钮及玻璃微调预览。文件版本 `0.9.6.0`，EXE SHA256 为 `1b81225b58ad372d85ac16251cc8b2b2e5ca9f0c1109aed10be40525f8e64fb1`，ZIP 内 EXE 与发布目录校验一致。
- 首次打包遇到磁盘空间不足，清理工作区内可重新生成的测试运行时解压缓存与本次不完整打包中间文件后重试成功；未删除用户游戏、配置或旧版发布包。

## 0.9.5 纯文字品牌、皮肤接缝与玻璃重绘

日期：2026-10-24。

- 侧栏顶部移除图标及中文标题，改为两行「ISLAND / Launcher」字标，预览同步。保持导航、账号位置、外层圆角和两组窗口按钮。
- 不透明身体先绘制完，再绘制全部衣物与帽子外层。每个模型面独立裁切贴图、内缩半个采样像素，避免相邻贴图串色。加入透明裤腿紧邻另一条腿的渲染回归，标准和纤细模型四个腿部截面均无背景细缝；已有双层切换、后视旋转、旧版镜像皮肤和动作检查通过。
- 查阅 Apple WWDC25「Meet Liquid Glass」、HIG Materials 及 Microsoft WPF ShaderEffect 文档，自行实现 WPF 应用内近似材质：按实际祖先背景层组合取样，圆角距离场控制局部边缘折射，磨砂散射、色彩保留、亮度压缩、方向边缘反光与细内边缘。没有背景细节时使用底色和反光，不会虚构图案；不取样桌面，也不包含 Apple 系统材质的完整动态行为。
- 保留可调背景不透明度与模糊，向全部玻璃区域开放蒙层浓度。前景文字、输入框及模型不进入背景取样或模糊，深色表面输入框文字和光标跟随前景颜色。仍仅更新独立预览，保存成功后应用。按区域缓存，布局 / 配置改变时失效，父级尺寸也计入缓存校验；皮肤动画和鼠标移动不重建背景位图。
- Release 构建成功，0 警告、0 错误。材质专项 11 项、完整界面回归 242 项通过，包括纯色父背景透色、不透明父层遮挡更底层颜色、背景及高光 0 / 50 / 100% 不透明度、模糊变化而前景保持清晰、预览隔离和保存失败保留实际外观，以及已有启动 / 账号 / 模组 / 窗口流程。本次核心业务仅更新版本标识，未重复核心业务回归。
- 日志为 `artifacts/wordmark-glass-build.log`、`artifacts/wordmark-glass-materials.log`、`artifacts/wordmark-glass-full-ui.log`。渲染截图位于 `artifacts/wordmark-glass-full-ui/`，已检查纸娃娃接缝和小窗口字标排版；`artifacts/wordmark-glass-before/` 为旧版对照。独立测试配置中的花瓣色块背景及皮肤由代码绘制，不随发布包作为素材分发，未修改用户配置。
- 发布目录 `dist/win-x64-0.9.5/`、便携包 `dist/IslandLauncher-0.9.5-win-x64.zip`。实际运行发布 EXE，以独立的彩色花瓣 / 深色玻璃 / 双层皮肤测试配置捕获 1020 / 1240 两种窗口尺寸、五个页面共 10 张截图，位于 `artifacts/wordmark-glass-published/`。已检查玻璃背景透色、预览一致性和深色输入框文字；截图外层四角 alpha 均为 0，版本为 `0.9.5.0`。EXE SHA256：`3fb153a0ecda2b8fe8495ef1d29061468a2bdcb874d913e9aabb9e70217b096f`，ZIP 内 EXE 与发布目录校验一致。

## 0.9.4 最外层窗口圆角

日期：2026-10-24。

- 普通窗口的 WindowChrome 轮廓半径改为 14，并按根元素的实际尺寸同步圆角内容裁剪，覆盖顶部栏、背景图片、材质和四个外角。最大化时将内容裁剪改为直角，恢复普通状态后恢复圆角；不修改窗口透明模式或渲染方式。
- 系统玻璃材质忽略 WindowChrome.CornerRadius，因此在支持的系统上同时设置 DWMWA_WINDOW_CORNER_PREFERENCE，请求原生圆角；保存 / 切换材质后保留该偏好。实际系统材质圆角受 DWM 策略影响。
- 外观小窗口同步裁剪外轮廓，截图绘制背景时也使用根元素的裁剪，避免用矩形底色填回已裁切的四角。左右模块、三色按钮、右侧窗口按钮、标题拖动与边缘缩放继续保留。
- Release 构建成功，0 警告、0 错误。窗口轮廓专项检查 12 项、完整界面回归 238 项通过。实际读取 HWND 区域与四角渲染像素，验证 1020 / 1240 / 1600 宽度下圆角生效、标题拖动及对角缩放命中有效；整体半透明与调整尺寸后仍保留圆角区域，保存原生云母 Alt 后圆角偏好保持。既有皮肤、启动、模组、外观草稿与保存流程回归通过。本次核心业务无变更，未重复完整核心回归。
- 日志为 `artifacts/rounded-window-build.log`、`artifacts/rounded-window-outline.log`、`artifacts/rounded-window-ui.log`。专项截图位于 `artifacts/rounded-window-outline/`，完整截图位于 `artifacts/rounded-window-ui/`，已检查小窗口的四个外角。未修改实际用户配置。
- 发布目录 `dist/win-x64-0.9.4/`，便携包 `dist/IslandLauncher-0.9.4-win-x64.zip`。实际运行发布 EXE，以独立的蓝色外框 / 玫瑰主题配置捕获两个窗口尺寸、五个页面共 10 张截图，位于 `artifacts/rounded-window-published/`；确认外层蓝色背景有弧度，发布截图的四角像素 alpha 均为 0。
- 文件版本 `0.9.4.0`，ZIP 与发布目录内 EXE 校验一致，SHA256：`3f6bf30fa1350478e22bba57ea2a37758e28729c24fabb8f3cdf8a06376292a2`。

## 0.9.3 左侧三色与右侧 Windows 窗口操作

日期：2026-10-24。

- 在外层顶部栏左上角加入红、黄、绿圆形按钮，分别复用关闭、最小化、最大化 / 还原处理；右侧继续保留 Windows 风格窗口按钮。三色按钮采用固定颜色、12 直径圆点与 20×28 点击区域，不随主题配色改变。
- 鼠标移入左侧按钮组时显示操作符号，按下时改变圆点亮度；支持键盘焦点、焦点环、操作提示与辅助功能名称。两组按钮均保留 WindowChrome 命中标记，标题右移避让左侧按钮，其余顶部空白继续作为窗口拖动区域。
- 外观小窗口同步显示左侧三色圆点与右侧 Windows 符号，预览中的三色圆点仅选择外框区域，不触发实际窗口关闭等操作。未新增配置字段，先预览、保存后应用及已有外观配置行为保持。
- Release 构建成功，0 警告、0 错误。完整界面回归 224 项通过，包括 1020 / 1240 / 1600 宽度下两组窗口按钮的 Chrome 命中标记、可见性、键盘焦点和点击区域，以及按钮 / 标题不重叠、左右模块、外观预览和既有启动流程。本次核心业务无变更，未重复完整核心回归。
- 日志为 `artifacts/traffic-lights-build.log`、`artifacts/traffic-lights-ui.log`，截图位于 `artifacts/traffic-lights-ui/`；已检查小窗口正常显示三色圆点及右侧按钮。未修改实际用户配置。
- 发布目录 `dist/win-x64-0.9.3/`，便携包 `dist/IslandLauncher-0.9.3-win-x64.zip`。实际运行发布 EXE，以独立蓝色外框 / 玫瑰主题配置捕获两个窗口尺寸、五个页面共 10 张截图，位于 `artifacts/traffic-lights-published/`；确认左侧三色按钮、右侧 Windows 按钮与外观预览均可显示。
- 文件版本 `0.9.3.0`，ZIP 与发布目录内 EXE 校验一致，SHA256：`bb1e31ac82cd819badcceb7b3a3edb0774b7f7fbebb2a0153312e23e09210469`。

## 0.9.2 外层背景与左右子模块

日期：2026-10-24。

- 将窗口最外层背景作为整个启动器的容器，覆盖顶部栏、左右外圈、底部外圈和模块之间的间隙。左侧导航与右侧内容作为两个圆角子模块，左右 / 底部外边距及模块间距均为 12；模块上边对齐于外层顶部栏下方。
- 最小化、最大化 / 还原、关闭按钮移到覆盖整个窗口的外层顶部栏，保留原窗口操作处理与 40 高的 WindowChrome 拖动区域。按钮保留 Chrome 命中标记，并增加控件名称、提示及辅助功能名称。
- 右侧新增独立 Content 外观区域，默认沿用主题底色，可单独配置材质、颜色和透明度。追加配置字段与区域编号，原有字段及编号保持；旧配置自动补充默认内容模块。外观预览同步为外框、左右两模块及内部卡片的层级，共八个可编辑区域，继续先预览、保存后应用。
- 保留账号与启动按钮的垂直对齐、实例卡片自适应铺满、内部页面滚动和底部启动栏可见性。较矮窗口中的侧栏模型调整为 180 高，避免与导航、账号按钮挤压。
- Release 构建成功，0 警告、0 错误。核心检查 133 项、完整界面回归 224 项通过，包含旧外观配置兼容、内容模块独立保存与草稿隔离、1020 / 1240 / 1600 宽度下外圈边距、模块对齐、窗口操作命中与可见性、卡片铺满及既有启动 / 皮肤 / 模组流程。日志为 `artifacts/window-shell-build.log`、`artifacts/window-shell-core.log`、`artifacts/window-shell-ui.log`。
- 已检查 `artifacts/window-shell-ui/ResponsiveCards-1020.png` 与 `ResponsiveCards-1240.png`，确认两个模块完整嵌入外层背景，底部操作仍对齐；界面其余截图位于同目录。未修改实际用户配置。
- 发布目录 `dist/win-x64-0.9.2/`，便携包 `dist/IslandLauncher-0.9.2-win-x64.zip`。实际运行发布 EXE，使用独立的蓝色外框 / 玫瑰主题配置捕获两种窗口尺寸、五个页面共 10 张截图，位于 `artifacts/window-shell-published/`。已检查实例页与外观页：蓝色背景形成完整外圈，左右模块内缩，窗口操作位于外层顶部；预览同步展示两个模块。
- 文件版本 `0.9.2.0`，ZIP 与发布目录内 EXE 校验一致，SHA256：`d0c1dc16a37fdb895656e6ce39475f95c512ce506fe202591f6f2a1f8c71fe20`。

## 0.9.1 外观先预览、保存后应用

日期：2026-10-23。

- 主题色、预设配色、背景图片、材质、区域背景不透明度、窗口整体不透明度、纸娃娃显示状态的编辑先进入外观草稿。仅更新上方预览和选色样本，实际启动器维持上次保存的外观；重置和切换页面也不会自动应用。
- 为预览创建独立渲染上下文、主题资源、图片与模糊缓存，通过继承属性传给预览中的材质卡片。预览整体不透明度只作用于小窗口，外侧棋盘格保持不变；增加当前玩家纸娃娃，供显示开关预览。取消编辑不触碰实际窗口的 DWM 背景、整体 alpha 或全局主题资源。
- 「保存外观设置」、设置页外观卡片的保存按钮，以及主设置「保存设置」均在配置写入成功后统一应用主题、材质、窗口 alpha 与纸娃娃显示；保存失败恢复原外观状态，不将草稿应用到实际界面。未保存内容在本次窗口中保留，关闭后丢弃。
- Release 构建成功，0 警告、0 错误。完整界面回归 215 项通过，涵盖草稿与实际渲染隔离、各类保存入口、预设与自定义配色、显示开关、整体 alpha、原生 DWM 材质仅在保存后更新、重置与页面切换不应用、锁定配置文件时保存失败不应用，以及既有启动、皮肤、模组和材质渲染检查。本次核心业务无变更，未重复完整核心回归。
- 日志为 `artifacts/preview-before-save-build.log`、`artifacts/preview-before-save-full-ui.log`；初步外观 / 皮肤检查另通过 138 项，日志为 `artifacts/preview-before-save-ui.log`。已检查 1020 / 1240 两种窗口尺寸的蓝色背景 / 高斯模糊 / 70% 整体不透明度未保存截图，确认只有预览小窗口变化，实际背景与窗口不透明度保持原样，截图位于 `artifacts/preview-before-save-full-ui/UnsavedPreview-1020.png` 与 `UnsavedPreview-1240.png`。未修改实际用户配置。
- 发布目录 `dist/win-x64-0.9.1/`，便携包 `dist/IslandLauncher-0.9.1-win-x64.zip`。实际运行发布 EXE，以独立的已保存背景 / 材质配置捕获两个窗口尺寸、五个页面共 10 张截图，位于 `artifacts/preview-before-save-published/`；确认启动后加载已保存外观，预览显示对应背景和玩家模型。
- 文件版本 `0.9.1.0`，ZIP 与发布目录内 EXE 校验一致，SHA256：`601b1aaae5c5dfcf4b622ba75ec1d7a0f91d194cc4b2bed52c036a3db6c9384b`。

## 0.9.0 云母 Alt、轻薄亚克力与液态玻璃

日期：2026-10-23。

- 外观编辑器新增「轻薄亚克力」「云母 Alt」「液态玻璃」，原有亚克力明确为「标准亚克力」，七种材质继续平铺选择，小窗口自动换行。七个预览区域分别保存材质、颜色与 0–100% 背景不透明度；窗口整体不透明度仍独立提供 40–100%。
- 轻薄亚克力降低蒙层与颗粒强度；云母 Alt 使用更浓的蒙层；液态玻璃通过背景图片局部放大形成折射边界，加入可调高斯模糊、渐变反光与圆角高光。全部绘制处于同一背景透明度组，不影响前景文字、图标和控件。没有背景图片时使用底色与反光，液态玻璃与轻薄亚克力均为应用内效果。模糊图缓存复用，渐变冻结，不增加逐帧动画。
- 原生窗口云母 Alt 使用文档规定的 DWM TABBEDWINDOW (4)，云母 / 标准亚克力继续使用 2 / 3。原生效果仅在系统支持、无自定义窗口图片且整体及背景不透明度均为 100% 时使用；调低任一不透明度会清理原生背景，切换应用内绘制，恢复后可重新启用原生效果。
- 配置枚举保留原有 0–3，新增值追加为 4–6。核心检查 132 项、完整界面回归 205 项通过，涵盖旧配置兼容、新材质在全部区域保存 / 恢复、背景 0 / 50 / 100% 的实际像素、边缘高光透明度、前景不透明与清晰度、效果差异、液态玻璃模糊、窗口整体透明度、原生 DWM 4 与回退 / 恢复、1020 / 1240 窗口材质选项换行及既有启动流程。
- Release 构建成功，0 警告、0 错误。日志为 `artifacts/glass-materials-build.log`、`artifacts/glass-materials-core.log`、`artifacts/glass-materials-ui.log`。界面截图位于 `artifacts/glass-materials-ui/`；已检查小窗口七个材质选项与应用内背景效果。未更改实际用户配置。
- 发布目录 `dist/win-x64-0.9.0/`，便携包 `dist/IslandLauncher-0.9.0-win-x64.zip`。实际运行发布 EXE，以隔离配置加载程序绘制的山景测试图片、液态玻璃实例 / 启动栏 / 地图卡片、轻薄亚克力侧栏及云母 Alt 卡片，捕获 1020 / 1240 两种窗口尺寸、五个页面共 10 张截图。已检查实例页与外观预览，位于 `artifacts/glass-materials-published/`；图片仅用于验证，不随程序作为预设分发。
- 文件版本 `0.9.0.0`，ZIP 与发布目录内 EXE 校验一致，SHA256：`0cfb4f6781852d321fc0e2d3467f938897ff8fff50e0c26c94c77b1b2c97026c`。

## 0.8.2 设置页主题与纸娃娃开关

日期：2026-10-23。

- 将预设主题移入启动器设置页的「外观设置」卡片，保留进入交互预览的按钮；预览页不再重复显示预设栏。卡片内提供独立「保存外观设置」按钮，未完成的内存 / Java 输入不阻止保存外观偏好。
- 预设扩充为 16 种：原有森林绿、海洋蓝、鸢尾紫、玫瑰红、暖琥珀、石墨灰，新增纯红、纯蓝、纯粉、纯紫、鲜橙、明黄、草绿、青蓝、樱花粉、曜石黑。即时更新主题资源，明亮主题沿用深色按钮文字。
- 新增「隐藏主界面纸娃娃」开关，立即预览；隐藏时停止模型的渲染回调及鼠标位置计算，保留账号头像与按钮。偏好保存在外观配置中，局部保存与主设置保存共用读取逻辑；旧配置默认显示，重开窗口可恢复隐藏状态，也可取消隐藏后重新保存。
- Release 构建成功，0 警告、0 错误。核心检查 128 项、完整界面回归 186 项通过，包括偏好序列化、新旧配置、红 / 蓝 / 粉 / 黄配色、亮色文字、隐藏时停止动画、重开恢复与重新显示。相关界面检查另通过 112 项。日志为 `artifacts/quick-appearance-build.log`、`artifacts/quick-appearance-core.log`、`artifacts/quick-appearance-ui.log`、`artifacts/quick-appearance-full-ui.log`。
- 已检查 1020 / 1240 两种窗口宽度的设置页截图，16 个预设均完整换行，开关与局部保存按钮可见。截图位于 `artifacts/quick-appearance-ui/QuickAppearance-1020.png`、`QuickAppearance-1240.png` 和 `artifacts/quick-appearance-full-ui/`。
- 发布目录 `dist/win-x64-0.8.2/`，便携包 `dist/IslandLauncher-0.8.2-win-x64.zip`。实际运行发布 EXE，以独立的纯粉 / 隐藏纸娃娃配置捕获两个窗口尺寸、五个页面共 10 张截图，确认启动后恢复配色与隐藏状态，位于 `artifacts/quick-appearance-published/`；未修改实际用户配置。
- 文件版本 `0.8.2.0`，ZIP 与发布目录内 EXE 校验一致，SHA256：`78bda64d841edd59ed8be3d254fecbfe242906c829eac4888a1d690ed107a9cc`。

## 0.8.1 模型提示与动画刷新优化

日期：2026-10-23。

- 移除 3D 模型上的悬浮操作提示。侧栏与皮肤配置窗口共用更新后的控件。
- 将固定 33 ms 的 DispatcherTimer 改为 CompositionTarget.Rendering 逐帧回调，按 RenderingTime 排除同一帧的重复通知，不再由应用固定限制在约 30 fps。后台、隐藏、最小化、卸载时解除静态事件订阅并暂停动画时钟，恢复后继续。
- 头部使用基于帧间时间的指数平滑，在 30 / 60 / 144 fps 下经过相同时间可得到一致的转向角度；动作使用经过时间计算，不随刷新频率加速。每帧直接写入最终部位角度，减少先清零再赋值产生的重复更新。
- Release 构建成功，0 警告、0 错误。完整界面回归 175 项通过，包括不同帧率下的动作 / 跟随速度、外层贴图、旧皮肤、账号切换、窗口生命周期及既有运行 / 模组 / 外观流程。日志为 `artifacts/skin-smooth-build.log`、`artifacts/skin-smooth-ui.log`，截图位于 `artifacts/skin-smooth-ui/`。本次未重复完整核心回归。
- 前台测试窗口的约 1.2 秒采样中收到 196 个不同渲染帧回调，模型角度改变 195 次，约 163.3 次回调 / 秒，确认没有每隔一帧维持旧姿态。此数据为本机 WPF 渲染回调频率，不代表所有设备的最终屏幕帧率。
- 发布目录 `dist/win-x64-0.8.1/`，便携包 `dist/IslandLauncher-0.8.1-win-x64.zip`。实际运行发布 EXE，以独立配置捕获两个窗口尺寸、五个页面共 10 张截图，位于 `artifacts/skin-smooth-published/`；未修改实际用户配置。
- 文件版本 `0.8.1.0`，ZIP 与发布目录内 EXE 校验一致，SHA256：`2cf02ac64f7b95395b3d9c4cbda2322bb3c8170836d77f0ef994df8b6c8258a1`。

## 0.8.0 双层 3D 皮肤与侧栏纸娃娃

日期：2026-10-23。

- 皮肤配置使用 WPF 3D 网格展示六面贴图。标准 / 纤细模型分别使用 4 / 3 像素手臂；帽子、外套、袖子、裤腿使用独立外层网格，与对应部位共同运动，可隐藏外层查看底层。外层保留全透明 / 半透明像素，内层按标准皮肤区域保持不透明。
- 预览提供站立、行走、奔跑、招手；支持拖动旋转、滚轮缩放、左右键旋转和 Home / 双击重置视角。头部在窗口内跟随鼠标平滑转动，限制左右 / 上下角度，并可关闭跟随。
- 去掉侧栏「每一个世界，都有新的可能。」标语，替换为当前账号的 3D 纸娃娃。保存皮肤、恢复默认或切换账号时同步更新模型；侧栏右键可切换动作。动作、外层开关及视角只影响预览，不改写 PNG 或游戏动作。
- 支持 64×32 旧皮肤：镜像补齐左臂、左腿，保留帽子层；导入及模型切换时保持 Classic，避免提交不兼容的 Slim。损坏缓存回退到完整默认模型。默认模型的侧面、背面及顶底纹理由程序生成，不包含第三方皮肤资源。
- 原生动画仅在所属窗口处于前台、可见且未最小化时运行；卸载控件后停止定时器并移除鼠标 / 窗口事件。模型纹理放大后共享于六面材质，动画仅更新部位变换。
- Release 构建成功，0 警告、0 错误。相关外观 / 皮肤检查 98 项、完整界面回归 172 项通过，涵盖双层渲染像素差异、前后旋转、模型宽度、四种动作、头部角度限制、旧皮肤左右镜像像素、账号切换、上传失败 / 取消、最小化 / 关闭停止动画、大小窗口布局及既有 JVM / 模组 / 外观流程。本次未重复完整核心回归。
- 日志为 `artifacts/skin-3d-build.log`、`artifacts/skin-3d-ui.log`、`artifacts/skin-3d-full-ui.log`。已检查底层 / 外层、背面、招手、奔跑、头部转向及 1020 / 1240 侧栏截图，位于 `artifacts/skin-3d-ui/`、`artifacts/skin-3d-full-ui/`。上传仍使用模拟服务验证，未操作真实 Microsoft 账号皮肤。
- 发布目录 `dist/win-x64-0.8.0/`，便携包 `dist/IslandLauncher-0.8.0-win-x64.zip`。实际运行发布 EXE，以独立配置捕获两个窗口尺寸、五个页面共 10 张截图，确认侧栏模型及既有页面可加载，位于 `artifacts/skin-3d-published/`；未修改实际用户配置。
- 文件版本 `0.8.0.0`，ZIP 与发布目录内 EXE 校验一致，SHA256：`f316723c8f1c8253db8720ba1ff557dc0dadebb014aab0438ea298b099c5b143`。

## 0.7.3 独立预设主题栏

日期：2026-10-23。

- 六种配色从下方主题编辑区移到外观页上方的独立「预设主题」栏，位于预览之外；切换七类预览区域或滚动设置时始终可选。下方手动输入区域改名为「自定义主题色」。
- 仅调整布局与命名，预设选色、实时预览、独立区域颜色和保存逻辑沿用现有实现。
- Release 构建成功，0 警告、0 错误。现有外观 / 皮肤界面检查 72 项通过，同时核对所有预览区域的预设栏可见性及滚动后的布局。日志为 `artifacts/preset-themes-build.log`、`artifacts/preset-themes-ui.log`；截图位于 `artifacts/preset-themes-ui/`。
- 1020 / 1240 两种窗口宽度下，六个预设均完整显示，上方预览和保存按钮保持可见，下方设置可滚动。未重复完整核心回归。
- 发布目录 `dist/win-x64-0.7.3/`，便携包 `dist/IslandLauncher-0.7.3-win-x64.zip`。实际运行发布 EXE，以独立配置捕获两个窗口尺寸、五个页面共 10 张截图，位于 `artifacts/preset-themes-published/`；未修改实际用户配置。
- 文件版本 `0.7.3.0`，ZIP 与发布目录内 EXE 校验一致，SHA256：`f24ca022ddc935361270fa03d943120577fe1f21ace4f423ac6cd06eded09a74`。

## 0.7.2 常用颜色与调色盘

日期：2026-10-23。

- 七类外观区域均提供 12 个常用色块，包含黑白、灰、绿、蓝、紫、红、琥珀色。调色盘在当前区域内展开，支持二维拖动、方向键微调，以及色相 / 饱和度 / 亮度滑块；与十六进制输入、实时预览保持同步。
- 点击色块或调色盘自动取消跟随主题，恢复跟随时清除独立颜色，并显示该区域实际主题底色。区域切换与保存恢复对应颜色；调整黑色状态下的色相后提高亮度，不丢失所选色相。
- Release 构建成功，0 警告、0 错误。外观 / 皮肤界面检查 72 项通过，包含选色切换、手动输入、键盘调色、黑色边界、独立区域保存、跟随主题，以及 1020 / 1240 宽度适配。仅重跑本次涉及的界面检查，未重复完整核心回归。
- 展开调色盘时自动滚动到完整控件，上方预览保持固定。已检查两个窗口尺寸的渲染截图，见 `artifacts/color-palette-ui/ColorPalette-1020.png` 与 `ColorPalette-1240.png`；构建与界面日志为 `artifacts/color-palette-build.log`、`artifacts/color-palette-ui.log`。
- 发布目录 `dist/win-x64-0.7.2/`，便携包 `dist/IslandLauncher-0.7.2-win-x64.zip`。实际运行发布 EXE，以独立配置捕获两个窗口尺寸、五个页面共 10 张截图，位于 `artifacts/color-palette-published/`；未修改实际用户配置。
- 文件版本 `0.7.2.0`，ZIP 与发布目录内 EXE 校验一致，SHA256：`b55b758efe42abeb994639cbfe7afb1701495853ad6480f095787fd49f0b358a`。

## 0.7.1 预览透明底纹

日期：2026-10-23。

- 外观预览使用固定黑白棋盘格底纹，覆盖缩略窗口外侧的留白；背景透明时也能透出底纹。提示文字保留原背景，底纹不受主题颜色影响。
- Release 构建成功，0 警告、0 错误。现有外观 / 皮肤界面检查 61 项通过，包含区域选择、独立配置保存、透明度与返回导航；本次未重复完整核心回归。日志为 `artifacts/preview-checkerboard-build.log`、`artifacts/preview-checkerboard-ui.log`。
- 检查 1020 / 1240 两种窗口宽度的实际渲染截图，棋盘格填满预览两侧、缩略窗口居中，区域选择正常。截图位于 `artifacts/preview-checkerboard-ui/`。
- 发布到 `dist/win-x64-0.7.1/`，便携包为 `dist/IslandLauncher-0.7.1-win-x64.zip`。实际运行发布 EXE，以独立配置捕获两个窗口尺寸、五个页面共 10 张截图，位于 `artifacts/preview-checkerboard-published/`；未修改实际用户配置。
- 文件版本为 `0.7.1.0`，ZIP 与发布目录内 EXE 校验一致，SHA256：`7e1ea238d013fbc4fd99a7f4c41b8d00e79c7b4b55637dc455d5f4f2b8a977df`。

## 0.7.0 独立外观页面与交互预览

日期：2026-10-23。

- 启动器设置保留外观入口按钮，主题与材质编辑迁移到独立页面。支持返回设置、左侧导航跳转与始终可见的保存按钮。
- 预览直接绘制七类区域；点击后高亮并在下方显示对应设置。地图卡片拥有独立点击区域，覆盖于最近游玩区域之上。主题提供六个色块按钮，材质提供四个平铺选项，外观页面无下拉菜单。
- 预览与主窗口共用材质绘制、图片缓存与主题状态；预览以自己的窗口范围采样背景。原生 DWM 材质最终效果以实际窗口为准。
- 小窗口使用较矮预览，大窗口适度增高；下方设置单独滚动。区域切换时保留各区域设置，非法颜色输入阻止切换，避免选择标记与编辑值不一致。
- Release 构建成功，0 警告、0 错误。核心检查 127 项、完整界面回归 135 项通过，包含七类预览选择、嵌套地图点击、固定预览、返回导航、非法输入保留，以及 1020 / 1240 窗口布局。日志为 `artifacts/appearance-preview-build.log`；交互截图位于 `artifacts/appearance-preview-ui/` 与 `artifacts/appearance-preview-full-ui/`。
- 发布目录 `dist/win-x64-0.7.0/`，便携包 `dist/IslandLauncher-0.7.0-win-x64.zip`。直接运行自包含 EXE，以独立预览配置在 1020 / 1240 两种宽度完成五个页面共 10 张截图，位于 `artifacts/appearance-preview-published/`；未修改实际用户配置。
- ZIP 中 EXE 与发布目录 EXE 校验一致，SHA256：`40301ecb9a357e9ea0ba3c8197d8bb79235287bf5a70c63115755b0c1e19b71c`。

## 0.6.0 背景、透明度与材质

日期：2026-10-23。

- 配置主背景与上次游玩独立图片；七个区域分别选择纯色 / 原图、高斯模糊、亚克力、云母、颜色及背景 alpha。主窗口有独立的整体不透明度控制，40–100%；文字、图标不参与卡片背景的模糊和透明度。
- 图片保存到用户配置目录的 Backgrounds 缓存，通过内容哈希共用文件，移动原文件后正常加载。图片大小、尺寸、格式及完整解码均有校验；读取失败使用默认背景。原图保留、显示解码尺寸受限，模糊结果缓存供多个卡片使用。
- 原生材质在 Windows 11 22H2 及更新版本、主窗口无自定义背景图且整体不透明度 100% 时启用。当前 Windows 构建 26200：读回 DWM 云母值 2、亚克力值 3。切换整体半透明或重置时清除原生背景；卡片 / 图片使用应用内材质。DWM 最终显示效果仍受系统设置与激活状态影响。
- 检查真实原生窗口 alpha、调整窗口尺寸后的 alpha 保留、重开窗口恢复、颜色与每个区域的独立保存。像素检查确认卡片背景 alpha、前景保持不透明，以及高斯背景的颜色柔化。
- 外观实时预览支持单独保存，重置当前区域 / 全部背景与材质；不修改游戏运行设置。新增可选离线构建参数 `build.ps1 -NoRestore`，已有还原结果时可使用。
- Release 构建成功，0 警告、0 错误。核心检查 127 项、完整界面回归 120 项通过，包含图片回退、蒙层边界、上次游玩纯色模式、深色系统材质、真实 JVM 启动、模组与账号回归。日志 `artifacts/materials-build.log`；交互截图位于 `artifacts/materials-ui/`、`artifacts/materials-full-ui/`。本次测试临时目录位于工作区，右键菜单测试禁用无关的粘贴查询，不读取桌面剪贴板。
- 发布目录 `dist/win-x64-0.6.0/`，便携包 `dist/IslandLauncher-0.6.0-win-x64.zip`。实际运行自包含 EXE，使用独立预览配置在 1020 / 1240 两种窗口宽度完成四个页面共 8 张截图，位于 `artifacts/materials-published/`；未修改用户的实际账号或外观配置。
- EXE SHA256：`4abab338ac7d18b64529b699055dff86553ae45e949d869a08228f3c463fd99a`。发布 EXE 与 ZIP 中 EXE 校验一致。WPF 截图用于布局检查，系统 DWM 材质另以窗口属性读回验证。

## 0.5.0 简化布局与玩家皮肤

日期：2026-10-23。

- 移除整个顶部「我的实例 / 收好你的世界」标题与日期区域，其他页面共用的标题占位同时移除。普通 / 矮窗口的最近游玩卡片高度分别从 210 / 185 缩减为 180 / 160。
- 创建和导入合并为列表右侧「添加实例」，包含下载版本与加载器、批量添加游戏文件夹、导入整合包三个入口。空实例列表也进入同一选择窗口。原文件夹去重、命名与安装逻辑继续使用。
- 侧栏账号按钮按内容伸展，文字和头像居中，两行账号信息完整可见。账号页显示头像、当前玩家标记及皮肤配置入口。
- 皮肤支持 PNG 尺寸 / 大小与完整图像解码校验、Classic / Slim 模型、正面预览和半透明帽子图层叠加。图像随配置持久保存，取消编辑不覆盖原皮肤。
- Microsoft 档案读取活动皮肤，刷新登录保留同一皮肤的缓存；纹理仅从官方主机以 HTTPS 下载且不携带账号令牌，大小有界限。上传 / 重置只在皮肤窗口明确保存后发起，失败保留编辑窗口，取消会终止请求。未使用真实 Microsoft 账号执行在线换肤；协议与错误处理由模拟 HTTP 和真实 WPF 窗口验证。
- 本地账号皮肤用于启动器头像与预览，游戏内皮肤需要额外模组支持，界面与说明中明确标注。
- Release 解决方案构建成功，0 警告、0 错误。核心检查 119 项、完整界面回归 96 项通过，覆盖上传等待 / 失败 / 取消、真实 JVM 启动、存档、模组、主题，以及 1020 / 1240 / 1600 窗口宽度。界面截图见 `artifacts/player-skins-ui/`。无截图模式也独立跑完全部界面检查。
- 使用独立目录 `dist/win-x64-0.5.0/` 发布，便携包为 `dist/IslandLauncher-0.5.0-win-x64.zip`。直接运行自包含 EXE，以独立预览配置读取已有游戏目录，完成 1020 / 1240 两种宽度、四个页面共 8 张截图；用户实际账号配置不用于预览。截图位于 `artifacts/player-skins-published/`。
- ZIP 内 EXE 与发布目录 EXE 的 SHA256 一致：`321253b1127624e9574d0b0dc98824fd57e3e105c9d8732014ebb9698f841108`。

## 0.4.1 输入框文字与尺寸

日期：2026-10-23。

- WPF 界面回归 81 项通过，包含键盘输入、下拉框、右键菜单、日志双向滚动及真实 JVM 启动流程。
- Release 解决方案构建成功，0 警告、0 错误。原发布目录的 EXE 正在运行而被占用，本次使用 `dist/win-x64-0.4.1/` 独立发布；构建脚本新增可选输出目录参数，默认路径保持不变。
- 修正统一 TextBox 模板将 Padding 同时作为内容宿主 Margin 使用的问题，避免内边距计算两次。13 号文字的自然输入框高度由约 59.3 缩减为 38；较大字号继续自然增高，20 / 29 号示例分别约 45.3 / 57.3，未通过缩小文字适配。
- 显示名称、内存、目录、账号、颜色、模组搜索与目录命名等字段统一应用。Java 下拉框内部编辑器不重复添加内边距，并沿用下拉框的字体及对齐设置。实例搜索使用最小高度，避免固定高度裁切较大文字。
- JVM 参数、日志等 AcceptsReturn 多行输入按顶部对齐，保留原有高度范围、换行与滚动设置。
- 使用真实控件核对默认 / 较大字号、中文与英文、数字、路径、可编辑下拉框、只读、禁用及多行输入。尺寸记录与截图位于 `launcher/artifacts/input-sizing/`，修复前对照位于 `launcher/artifacts/input-sizing-before/`；完整界面回归截图位于 `launcher/artifacts/input-sizing-ui/`。
- WPF 行为参考：[TextBoxBase 的内边距转交逻辑](https://source.dot.net/PresentationFramework/System/Windows/Controls/Primitives/TextBoxBase.cs.html)、[官方 TextBox 模板](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Fluent/Styles/TextBox.xaml)。

## 0.4.0 布局、卡片收藏与主题颜色

日期：2026-10-23。

- 核心离线检查 100 项、WPF 界面检查 81 项通过。主题颜色持久化、合法性校验及错误配置回退已覆盖，其他运行设置保持回归通过。
- 1020×700、1240×830 和 1600×900 下验证卡片首行均分可用宽度、右侧无固定宽度造成的余白，以及底部账号按钮与启动按钮中心对齐。启动区下方状态文字不再显示，运行进度与错误弹窗继续可用。
- 卡片右上角收藏操作直接保存，保持原选中实例；收藏页取消收藏即时移除对应卡片。双击收藏按钮不会打开实例设置。
- 独立模组管理窗口使用 301 个本地测试模组，覆盖大小窗口、文件名列随宽度变化、大小 / 状态列保留、多选启用 / 停用、文件名搜索和状态筛选。920×690 与 740×520 下操作栏保持可见，窗口默认有更高的表格区域。
- 主题支持 6 个预设及自定义十六进制颜色。验证即时预览、保存后新窗口恢复，以及明亮色彩使用深色按钮文字。按钮、导航、选中状态、滚动条、侧栏、山林插画与弹窗使用动态颜色资源。
- 核对截图位于 `launcher/artifacts/layout-theme/`，包含宽度适配、紫色主题、外观设置及大列表模组管理。图中的账号、地图和模组为隔离测试夹具。
- 本次测试机可用物理内存不足 1 GB，自动内存的界面回归验证了不足时提示并恢复启动按钮；手动设置的实际 JVM 启动、日志与退出仍通过。自动分配的正常启动在 0.3.0 已通过，核心内存估算边界检查继续通过。
- 仅修改启动器；原下载器源码与发布目录未改动。发布包不包含测试配置、账号、地图或模组。

## 0.3.0 全局 / 独立 JVM 设置与自动内存

日期：2026-10-23。

- Release 解决方案构建成功，0 警告、0 错误。核心离线检查 97 项、WPF 界面检查 61 项通过。
- 全局与版本独立设置涵盖 Java、手动 / 自动内存、默认 JVM 参数开关、基础优化开关、手动 Java 兼容性检查开关、自定义 JVM 参数及旧版内存选项。切回全局会清除运行覆盖，继承最新全局值；同名跨目录版本分别保存。
- 自动内存读取真实 Windows 物理内存，按可用内存、总内存、启用模组数量估算并预留系统空间，覆盖低空闲内存、32 位上限、模组规模、向下取整和不足时提示。旧配置保留手动值，已有单项 Java / 内存覆盖迁移为独立设置快照。
- 实际 JVM 夹具确认自定义属性、中文及空格、用户堆内存优先级、禁用默认 / 优化参数仍能启动、明确 GC 参数不会与自动优化冲突。最低版本兼容性检查及显式跳过均已验证；`MaxPermSize` 只验证旧版参数生成，未运行 Java 7 或 32 位 Java。
- WPF 流程实际保存全局与独立设置、恢复全局、修改全局后保留独立值、JVM 引号错误提示、自动分配启动和独立手动内存启动。游戏夹具输出对应自定义属性，观察到正常退出。
- 截图核对全局 JVM 设置与独立 JVM 设置，位于 `launcher/artifacts/jvm-memory/`。实例保存按钮保持固定可见，长表单可滚动，旧版内存选项默认折叠。
- 自动内存是启发式估算，未验证所有整合包的内存需求或性能；本次没有对 NovaEng / Cleanroom 进行完整游戏启动实测。原下载器源码及发布目录未改动。

## 0.2.1 单版本 Java 与文件夹版本隔离

日期：2026-10-23。

- Release 解决方案构建成功，0 警告、0 错误。
- 核心检查：离线模式 73 项通过。新增单版本 Java 覆盖、显式自动选择与继承的持久化、同名版本跨目录设置、每个文件夹的隔离设置、混合根目录/版本目录自动检测、空文件夹与新安装实例、路径校验，以及共享目录的 Quick Play 和旧版资源映射。
- 界面检查：离线模式 52 项通过。实际操作独立 Java 输入与检测、无效路径提示、自动/继承切换、文件夹三种隔离设置、共享模组管理、根目录地图记录及跨目录继续游玩。真实 JVM 夹具验证独立 Java 可覆盖不可运行的全局 Java，并验证工作目录与正常退出。
- 核对独立 Java 下拉列表、共享/独立实例管理与文件夹隔离设置截图，位于 `launcher/artifacts/runtime-settings/`。长说明换行，实例设置保存按钮固定在滚动区域下方。
- 对 `E:/NovaEng-CleanroomMC-1.19.2/NovaEng-CleanroomMC/.minecraft` 只读检查：NovaEng-CleanroomMC 自动使用根目录；GregTech Odyssey、GT New Horizons 2.8.4 自动使用各自版本目录。没有修改这个目录的文件，也没有运行该整合包；不将目录识别验证视为 Cleanroom 完整启动验证。
- 发布为独立 0.2.1 便携包；原下载器源码和发布目录未改动。

## 0.2.0 文件夹标签与继续游玩

日期：2026-10-23。

- 核心检查：联网模式 56 项通过。新增旧配置迁移、多目录命名与去重、各目录选择恢复、同名实例设置隔离、gzip NBT 世界名与时间、中文/表情的 modified UTF-8、损坏存档跳过、Quick Play 元数据参数及路径校验。
- 界面检查：联网模式 42 项通过。实际操作批量导入与命名弹窗、文件夹重命名、横向标签、全目录收藏、同名版本跨目录启动、空目录、取消导入、真实 JVM 启动及「一键继续」跨目录定位。
- 1240×830 和 1020×700 核对「收藏 / 文件夹」标签与上次游玩区域，截图位于 `launcher/artifacts/folders-ui/`。示例地图为测试夹具，已标注「测试地图」，不包含用户账号。
- 读取单人地图的真实 `level.dat` 元数据；快速进入地图的新增参数经过元数据和已有地图目录验证，未覆盖所有 Minecraft 版本的完整进图实测。
- 原下载器源码和发布目录不变；启动器发布为独立 0.2.0 便携包。

## 0.1.1 样式修复

日期：2026-10-23。补齐所有应用内下拉框（含 Java 手动输入）、下拉选项、复选框、横纵滚动条、模组表格、进度条、工具提示、文本编辑菜单和信息/确认弹窗，弹窗改用统一标题栏。

- 新增控件模板后的界面流程检查：联网模式 28 项通过，覆盖原有 19 项流程和键盘切换、下拉展开尺寸、Java 手动输入、复选框选中、右键菜单目标、确认/取消、横纵滚动和版本菜单滚动。
- 修复文本框模板遗漏滚动条可见性绑定，以及横向滚动条受系统最小高度影响的问题。
- 逐张核对创建实例、实例管理、确认弹窗、设置、日志、版本菜单、下载源菜单和文本右键菜单截图；截图位于 `launcher/artifacts/style-fix/`。
- Release 构建与自包含发布成功，应用与便携包版本为 0.1.1。运行逻辑沿用已通过的 0.1.0 游戏启动链。

## 0.1.0 初版

日期：2026-10-23（Asia/Hong_Kong）。环境：Windows x64、.NET SDK 8.0.426；真实 Minecraft 使用 Java 21.0.6。

## 构建与自动检查

- `dotnet build launcher/Island.sln -c Release`：成功，0 警告、0 错误。
- 启动核心：44 项检查通过（含 Mojang / Fabric 实时接口）。覆盖离线 UUID、独立配置与备份、DPAPI、版本继承与循环、重命名 JAR、规则、参数、原生库路径、模组开关、可读日志、模拟 Microsoft 授权与刷新，以及真实 JVM。
- WPF 流程：19 项检查通过（含联网创建窗口）。覆盖实例发现、恢复玩家、搜索、收藏、导航、保存设置、模组开关、显示名称、真实启动按钮、双流日志、退出恢复、游玩时间，以及官方版本/Fabric 选择产生完整安装请求。
- 原下载器回归：核心 46/46，WPF 43/43 通过。启动器源码和发布文件位于独立 `launcher` 子目录，原下载器解决方案和发布脚本未改动。

## 真实 Minecraft

在 `launcher/artifacts/live-game/.minecraft` 安装并测试：

| 游戏 | 加载器 | 结果 |
| --- | --- | --- |
| Minecraft 1.21.1 | 原版 | 真实安装、资源校验/修复、Java 参数、LWJGL、OpenAL、资源重载和纹理图集初始化通过 |
| Minecraft 1.21.1 | Fabric Loader 0.19.5 | 真实安装、继承父版本、Fabric/Knot/Mixin 运行、LWJGL、OpenAL 和纹理图集初始化通过 |

游戏进程使用离线测试玩家，仅到渲染初始化，不创建世界、不加入服务器。自动检查随后结束自己的进程。原版上游输出有缺失个别声音和着色器采样器警告，没有阻止渲染初始化；烟雾检查不把它们当成崩溃。

日志：`launcher/artifacts/smoke-vanilla.log`、`launcher/artifacts/smoke-fabric.log`。测试游戏和日志不包含在发布包中。

## 视觉核对

以 1240×830 与最低窗口尺寸 1020×700 离屏渲染我的实例、账号、日志、设置，检查背景、层次、空态、滚动和底部启动区；另检查创建实例对话框。截图位于 `launcher/artifacts/ui-empty`、`ui-live`、`ui-dialogs`。

发布版启动后再次进行离屏渲染，确认单文件 EXE 可启动。发布 ZIP 仅包含启动器、说明、许可和运行时声明，无测试账号、游戏文件或凭据。

## 验证边界

- Microsoft 的设备码、Xbox、XSTS、Minecraft profile 与刷新经过模拟 HTTP 验证；未提供此应用自己的可用 Client ID，因此没有真人正版账号的全链路登录实测。
- 没有逐一实测全部历史版本、Forge/NeoForge 游戏启动、所有整合包或任意第三方启动器专有补丁；这些不能据此宣称全部兼容。
- 不自动下载 Java，不支持 Quilt/OptiFine、自定义认证、在线模组更新和皮肤管理。
- 此次没有进入世界长时间游玩或测试多人服务器。

## 0.20.19 GitHub 发布验证

日期：2026-11-06。

- 从 GitHub main 历史建立独立发布副本，使用当前启动器源码及所需共享核心；Release 构建为 0 警告、0 错误。
- 392 项核心检查、583 项完整界面检查、439 项外观与皮肤专项、29 项静态网站检查通过；网站构建通过。
- 同步旧回归中的 32px 外边距、8px 圆角、紧凑窗口自适应列数及外观入口定位，未更改发布程序行为；网站下载链接同步到 0.20.19。
- 便携 ZIP 完整性和 SHA-256 校验通过。CurseForge 申请材料仅存于本地忽略目录，不进入 GitHub 源码或发布包。
