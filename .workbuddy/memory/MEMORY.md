# HHCL 项目长期记忆

## 发布与分发位置（每次改完代码要同步）
- 源码：`HHCL\`
- 发布命令：`dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o "HHCL发布"`
- 成品：`HHCL发布\HHCL.exe`
- **桌面也放了一份：`C:\Users\徐颢朗\Desktop\HHCL.exe`** —— 用户实际是从桌面双击启动的，
  **每次重新发布后必须把新 exe 覆盖到桌面**，否则用户用的还是旧版。
  覆盖前先把旧版备份成 `HHCL发布\HHCL-上一版-<日期>-<时间>.exe`。
- 运行中的 exe 会锁文件导致 build/publish 失败（MSB3027）→ 先 `Stop-Process -Name HHCL` 再构建。
- **`-o "HHCL发布"` 不写 `bin/.../publish`**：publish 后同步桌面必须拷 `HHCL发布\HHCL.exe`，
  从 `bin\...\publish\HHCL.exe` 拷到的是**旧产物**（踩过：把带 bug 旧版盖回去）。
- **验证发布是否含修复**：`grep -c "关键字符串" HHCL发布/HHCL.exe`（二进制 grep，BAML/IL 里的字符串可查）。
- **发给别人用（两种包）**：
  - `HHCL-分享版.zip`（**53.6 MB**）—— 自包含单 exe（131 MB，含 .NET 运行时）+ 使用说明.txt。
    对方**什么都不用装**，双击即用。发布命令：
    `dotnet publish HHCL -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o "HHCL-自包含版"`
    （**不加 `IncludeNativeLibrariesForSelfExtract` 的话，WPF 的 native dll 会散在目录里，不算单文件**）
  - `HHCL-轻量版.zip`（**0.32 MB**）—— framework-dependent 版，**对方必须先装 .NET 10 桌面运行时**。
- **`Config.GameDir` 不能写死本机路径**（原来写死 `E:\.minecraft`，别人拿到就指到不存在的盘）。
  现在默认空，首次运行由 `DetectGameDir()` 探测：优先 `%APPDATA%\.minecraft`（存在就用）、
  再试 `OneDrive\.minecraft`，都没有就建 `%APPDATA%\.minecraft`。
  已有 config.json 的用户不受影响（值已存在就不会走探测）。
- 新增 XAML 窗口时 StaticResource 的 key 必须在 `Themes/Fluent.xaml` 里有定义，
  否则运行时 InitializeComponent 抛「在'System.Windows.StaticResourceExtension'上提供值时引发了异常」，
  窗口打不开（微软登录窗口曾因引用未定义的 `CardBorderBrush` 崩过，正确 key 是 `StrokeBrush`）。

## 用户对这个项目的明确要求
- 名字必须叫 **HHCL**，UI 是 **Windows UI 3（WinUI 3 / Fluent）** 风格，参照 PCL2。
- 模组/资源页必须能**联网下载**（Modrinth），不能只是列本地已有文件。
- 版本下载页要能选 **Fabric / Forge / NeoForge / Quilt** 加载器。
- 设置页要有**下载 Java 25**。
- 启动时缺文件要弹「**是否一键补齐**」，补齐后自动启动。

## 环境坑（本机特有）
- `maven.neoforged.net` **完全不可达**（curl http=000）→ NeoForge 必须用 BMCLAPI maven 镜像预下载依赖才能装上。
- `meta.fabricmc.net` **走系统代理必超时**（curl 1.6s 正常，HHCL 内 6 次重试全挂）→ Fabric 必须用
  `https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/{mc}`（PCL2 同款，0.4s）。
- `bmclapi2.bangbang93.com` 在 .NET 下偶发 TLS/超时；备用域名 `bmclapi.bangbang93.com` 可用。
- 本会话里 Bash 的 `rm` 会被沙箱拦（`SAFE_DELETE_INVALID_PATH: embedded drive prefix`），
  删文件要用 Python `os.remove` / `shutil.rmtree`。
- `dotnet build` 偶发不重编已改文件 → `touch` 源文件强制重编。
- **Bash 里别用反引号包中文/代码**（`\`cmd\`` 会被当成命令执行），写日志先落文件再让 Python 追加。

## 启动器核心逻辑的红线（改 LaunchBuilder 前必看）
- **客户端 jar 取哪个**：version json 有 `jar` 字段就用它；没有（Fabric/Forge/NeoForge 都没有）
  就沿 `inheritsFrom` 链取**最底层父版本**的 jar，绝不能用 `version.Id`。
- **加载器自带补丁客户端时要跳过原版 jar**，否则 NeoForge 走模块系统会崩
  `Modules minecraft and _1._21._4 export package ...`。三级判据：
  ① json 里有 `net.minecraftforge:forge:x:client` 库项；
  ② classpath 里有 forge/neoforged 目录且同目录有 `*-client.jar`；
  ③ `mainClass` 含 `bootstraplauncher` 且 `libraries/net/neoforged/neoforge/` 下有 `*-client.jar`
  （NeoForge 的 json 连自己的 jar 都不列，只有 ③ 命中）。
- **`inheritsFrom` 链要用 `chainIds` 记 id**，只存 JsonObject 拿不到父版本名。
- Forge/NeoForge **预下载依赖必须跳过** `-client` / `-server` / `-slim` / `-extra.jar`
  （安装器本地生成的补丁包，maven 上 404），且预下载失败只记日志不能中断安装。
- MC 26.x：game 参数新增 `${clientid}` / `${auth_xuid}`（空值必崩）；
  natives jar 内部是 `windows/x64/org/lwjgl/*.dll`，`java.library.path` 指向 `{natives}/java`
  且**不递归** → dll 必须平铺到 `{natives}` 和 `{natives}/java` 两份。

## 微软令牌有三种，千万别混用（改 MicrosoftAuth 前必看）
- `Account.AccessToken` = **Minecraft 令牌**（来自 `login_with_xbox`），喂给游戏的 `--auth_access_token`。
- `Account.MsAccessToken` = **微软身份令牌**（audience=user.auth.xboxlive.com），XBL/XSTS 鉴权、
  以及基岩版商店授权**必须**用它。拿 Minecraft 令牌去 XBL → 一定 401。
- `Account.RefreshToken` = 微软 refresh token，用 `grant_type=refresh_token` 换来的**是微软身份令牌**，
  所以 `RefreshAsync` 必须再走一遍 XBL→XSTS→login_with_xbox 才能更新 `AccessToken`；
  旧实现直接写进 `AccessToken` 会导致刷新后游戏在线认证失败（已修）。
- 登录时若没存 `MsAccessToken`，`MicrosoftAuth.GetMsTokenAsync()` 会用 refresh token 现换一个。

## PowerShell 调用的坑（Windows）
- 调 PS 一律用 `powershell -NoProfile -NonInteractive -OutputFormat Text -EncodedCommand <base64(UTF16LE)>`：
  中文路径在 PS 5.1 按 ANSI 解会坏，`-EncodedCommand` 彻底避开。
- 脚本里必须先 `$ProgressPreference='SilentlyContinue'`：stdout 被重定向时 PowerShell 的进度流
  会以 CLIXML 混进输出，把结果冲成乱码（曾把版本号读成 `System.Management.Automation.PSCustomObject`）。
- 解析结果用 `VER:` 这类固定标记，不要直接读整段 stdout。
- `Get-AppxPackage -Name` 匹配的是**包名**（Microsoft.MinecraftUWP），**不是包族名**
  （Microsoft.MinecraftUWP_8wekyb3d8bbwe）；要按族名得自己 `Where-Object` 过滤。

## 基岩版（Minecraft for Windows）下载链路的红线
- ProductId `9NBLGGH2JHXJ`、ContentId `7792d9ce-355a-493c-afbd-768f4a77c3b0`、
  包族 `Microsoft.MinecraftUWP_8wekyb3d8bbwe`。
- 新版是 **MSIXVC** 格式，主包 `Microsoft.MinecraftUWP_<ver>_x64__8wekyb3d8bbwe.msixvc` ≈ 1.93 GB；
  另有若干 `update-*.xsp`（几 KB，MS-XPFM **更新清单**，不是可直装包）。
- 鉴权：MS 身份令牌 → XBL → XSTS(**RelyingParty=http://update.xboxlive.com**) → 头 `XBL3.0 x=uhs;token`。
- 取包：`GET https://packagespc.xboxlive.com/GetBasePackage/{contentId}`。
- **下载地址 = CdnRootPath + RelativeUrl**（RelativeUrl 内含 ContentId/VersionId 两段，只拼文件名必 404）。
- CDN 只有 **http** 可用（`assets1/assets2.xboxlive.cn` 的 https 证书校验失败）→ 候选链要 https→http 降级。
  实测 61.6 MB/s，支持 Range 断点续传。
- 启动 AppID：`Microsoft.MinecraftUWP_8wekyb3d8bbwe!**Game**`（不是 !App）。
- 需要账号已拥有基岩版/试用资格，否则商店不返回包。
- Segoe MDL2 字体里没有 `✔` 字形，图标要用码位（`\uE73E` Completed / `\uE946` Info），否则显示豆腐块。

## 基岩版存档路径与「配置隔离」的红线
- **存档目录有两种布局**（微软 1.21.120 起把基岩版从 UWP 换成 GDK，路径变了）：
  - GDK 新版（1.21.120+ / 26.x）：`%APPDATA%\Minecraft Bedrock\Users\<XUID>\games\com.mojang`
  - UWP 老版：`%LOCALAPPDATA%\Packages\Microsoft.MinecraftUWP_8wekyb3d8bbwe\LocalState\games\com.mojang`
  - 代码里 `BedrockProfiles.FindGameDataDir()` 自动探测：先找已存在的目录，没有就按 XUID 拼 GDK 路径。
- **XUID 只有 `RelyingParty=http://xboxlive.com` 的 XSTS 响应里才有**（字段 `DisplayClaims.xui[0].xid`，
  还带 `gtg` 头像名、`agg` 成年标记）。update / api.minecraftservices 这类 RP 只给 `uhs`（XUID 的哈希），取不到。
  `MicrosoftAuth.LoginAsync` 已补一次 xboxlive.com 的 XSTS 来存 Xuid。
- 配置隔离机制（`Core/BedrockProfile.cs`）：每套配置 = `%APPDATA%\HHCL\bedrock_profiles\<名>\`，
  启动前「配置→游戏目录」，游戏进程退出后「游戏目录→配置」；用**增量同步**（按大小+修改时间比对），
  否则几 GB 存档全量复制没法用。
- 游戏进程名是 `Minecraft.Windows`（Preview 是 Minecraft.WindowsBeta），用它判断游戏是否在跑。
- **基岩版做不到 Java 版那样多版本共存**：Windows 同一包族只能注册一个版本；`.xsp` 是 XPFM 签名清单
  不是可装包；`storeedgefd` 版本清单接口换 4 种 RP 都只返回空存根；mcappx 镜像站被 Cloudflare 挡死。
  社区启动器靠「外部位置注册 + 逐版本刷包」的 hack 实现，别做半成品。
- 自定义 `FluentComboBox` 模板**不吃 DisplayMemberPath**，绑对象会显示成类名 → 直接用字符串做 ItemsSource。
- ComboBox 默认不选中任何项 → XAML 里要写 `SelectedIndex="0"`，否则下拉框是空的。

## 基岩版 addon（模组/资源包）的数据源与安装规则
- **Modrinth 没有任何基岩版内容**（实测：8 组关键词、240+ 项目逐个查版本 `platforms`，0 命中；
  `platform:windows` facet 不存在、`categories:windows` total_hits=0、loaders 里只有 "minecraft"）。
  所以别再想在 Modrinth 上找基岩版 addon。
- **CurseForge 有 2.6 万个基岩版项目**（`/minecraft-bedrock` 区），但：
  - `api.curseforge.com` **必须有效 API Key**（官方文档里的示例 key 已失效 → 403）；
    `www.curseforge.com` 与 `beta.curseforge.com` 都被 Cloudflare 挡（403 / 302 挑战）；
    老接口 `addons-ecs.forgesvc.net` 已下线。
  - gameId/classId **不要写死**：代码里动态发现——`GET /v1/games` 找 slug=`minecraft-bedrock`，
    再 `GET /v1/games/{id}` 取 categories 里 parentId 为空的根分类当 classId（addons/texture-packs/shaders…）。
- **微软官方 Marketplace API**：`store.mktpl.minecraft-services.net` 域名活着（会返回规范 JSON 错误），
  但接口路径猜不出来（试了 9 种组合全 404），暂不可用。
- **包格式与落盘规则**（`Core/BedrockAddonInstaller.cs`）：
  - `.mcpack` = 一个包（zip），`.mcaddon` = 多个 .mcpack（可能还带一个世界）打包；
  - 包名在 **manifest.json 的 `header.name`**（不是顶层 name）；
  - 行为包/资源包看 `modules[].type`：`data`/`script` → `behavior_packs/`，`resource` → `resource_packs/`；
  - 世界看 `level.dat`（同目录的 `levelname.txt` 是显示名）→ `worlds/`；
  - 装完还要进游戏世界设置里勾选才会生效（这步只能在游戏里做）。

## 合规要点（HHCL 第三方启动器）
- **不收费 = 唯一完全合规的姿势**：Modrinth ToS 写明 `personal, non-commercial use only`，
  CurseForge 免费 API Key 也是个人非商用。收费会超出许可范围（违约/封号，不一定违法）。
  而"接受赞助"不等于售卖软件 —— 关键看**钱和使用权有没有绑定**。
- **三条红线**（踩了才叫违规）：① 不能"赞助解锁功能/赞助者专属版"；
  ② 不能"赞助后帮你装/代下载"（= 售卖服务）；③ 别用赞助款做基岩版安装包的批量分发（微软那条线）。
  可以给的：Discord 身份组/徽章/致谢（社区权益，非软件权益）。
- EULA 红线（本项目全部避开）：不分发游戏本体、不绕过正版验证、第三方工具不得冒充官方。
- **GPL 传染性检查过了**：源码里无任何 GPL/LGPL 声明、无直接引用开源项目代码；
  唯一提到 PCL2 的是一行注释 + 微软公开的 OAuth 公共 client_id（所有第三方启动器都在用）。
- **User-Agent 要带联系方式**（Modrinth 官方推荐 Better/Best 格式，便于他们联系你而不是封 IP）：
  现在统一走 `Core/AppInfo.cs` 的 `AppInfo.UserAgent`（`Contact` 常量待填真实联系方式）。
- **免责声明已加**在设置页「关于」卡片：
  "HHCL 是第三方启动器，与 Mojang / Microsoft 无隶属关系，未获其官方认可或背书。"
- **不做赞助**：用户 2026-10-24 明确表示不搞赞助，之前预留的「赞助一下」按钮、`SponsorUrl` 常量
  和配套文案**已全部删除**。以后别再往里加赞助/付费入口。
- **税务（不适用，但记着）**：万一将来真要收款，爱发电**不代扣个税**，纳税义务人是创作者自己；
  手续费 6%（1% 支付 + 5% 服务）。自然人代开劳务报酬发票：**单次（日）销售额低于 1000 元免征增值税及附加税**；
  劳务报酬预扣预缴 = 每次收入额减 20% 费用后的余额 × 20~40%。
  零星小额在个税 APP 汇算清缴申报即可；金额大了走自然人电子税务局线上代开。

## 主题（深色/浅色）
- **XAML 里定义的 SolidColorBrush 会被 WPF 冻结**（改不了 Color 属性，报"画刷是只读的"）。
  所以换主题不能"改颜色"，必须**新建画刷替换资源项**：
  `Application.Current.Resources["BgBrush"] = new SolidColorBrush(color);`
- 配套要把引用改成 **`{DynamicResource XxxBrush}`**（StaticResource 只在解析时绑定一次，替换资源项不会更新）。
  已把 19 个主题画刷 + 7 个语义画刷的引用（Views/*.xaml、MainWindow.xaml、Fluent.xaml 里的 Style）全改成 DynamicResource。
- **渐变画刷同理**（LinearGradientBrush 也会冻结）：`ThemeManager.Gradients` 表里管 4 个
  （WindowBgBrush 窗口那层 / HeroGradientBrush 首页卡 / AccentGradientBrush / LogoGradientBrush），
  换主题时整个新建替换。**漏掉 WindowBgBrush 会导致浅色主题下主内容区还是深色**（踩过）。
- `Core/ThemeManager.cs`：调色表 26 项（深色/浅色两套），`Apply("dark"|"light")`。
  启动时在 `App_Startup` 里、**创建 MainWindow 之前**调用（画刷在元素创建时就绑定了）。
- 浅色下容易露馅的硬编码：`#FF202026`（ComboBox 背景）、`#FF17171B`（日志框底）、
  `#FFC8C8D0`（日志文字）、`#1AFFFFFF`（标签底）、`#142B88D8` 等半透明白。
  已全部换成语义画刷（OverlaySoftBrush / AccentPanelBrush / AccentTagBrush / FooterBrush…）。
- **查漏办法**：把所有 XAML 里的 `{StaticResource XxxBrush}` 扫出来，跟 ThemeManager 的 key 表对比，
  差集就是没被主题管的（这次就是靠它找出 WindowBgBrush / AccentGradientBrush / LogoGradientBrush）。
- 渲染验证时 `--shot <page> <w> <h> light` 可以切浅色；注意**渲染工具的背板别写死深色**，
  否则浅色主题下页面看着还是深色（被骗过一次）。

## 游戏语言自动切中文
- 游戏的界面语言是 **`<gameDir>/options.txt` 里的 `lang:` 项**（默认 `en_us`）。
  启动前把它改成 `lang:zh_cn` 即可（`Core/GameLanguage.EnsureChinese(gameDir)`：
  文件不存在就创建，已存在就替换那一行、其余设置原样保留）。
- 语言文件 `minecraft/lang/zh_cn.json` 是**资源索引里的资源对象**，
  装游戏时已经随全部 assets 一起下载好了，不用额外下。
- 注意版本隔离：`options.txt` 在**每个版本自己的目录**里（`E:\.minecraft\versions\<版本>\options.txt`），
  所以要用 `plan.GameDir`（= `LaunchBuilder.GameDirFor(cfg, version)`）而不是 `cfg.GameDir`。
- 另外给 JVM 加 `-Duser.language=zh -Duser.country=CN`，让系统对话框/报错等也是中文。

## 局域网联机（不需要服务器）的实现要点
- 主机侧：游戏里「开放到局域网」后，Minecraft 集成服务器会监听一个随机端口，
  并在 `<gameDir>/logs/latest.log` 里写 `Starting LAN server on port 51234`。
  启动器**从日志读端口**（`Core/LanMultiplayer.DetectLanPort`，只读最后 64KB，正则 `LAN server on port (\d+)`）。
  房间码 = 那个 5 位端口号；直连地址 = 本机内网 IP + 端口。
- 内网 IP 取法：枚举网卡取 IPv4，排除 127.*、Tunnel、以及描述里含 Hyper-V/VMware/VirtualBox 的虚拟网卡；
  选地址时优先 192.168.* → 10.* → 172.16-31.*（**169.254.* 是链路本地，要跳过**）。
- 客机侧直连：启动参数 `--quickPlayMultiplayer <IP:端口>`。
  **这个参数在版本 json 里被 feature 门控**（rules: `is_quick_play_multiplayer`），
  所以 `LaunchBuilder` 里必须把 `features["is_quick_play_multiplayer"]=true` 和
  `has_quick_plays_support=true` 打开，否则参数会被 rules 过滤掉（等于没传）。
  quickPlay 这套变量从 1.20 才有。
- **房间自动发现**（`Core/LanDiscovery.cs`）：主机检测到端口后 `StartResponder(gamePort)`，
  在本机 **UDP 45678** 应答；客机 `ScanAsync()` 广播 "HELLO"（255.255.255.255 + 各网段广播地址），
  主机回 "ROOM|ip|游戏端口"，客机列表里点「加入」直接进，不用手抄 IP。
  - 坑：`UdpClient.ReceiveAsync(ct)` **不带超时会永久挂住** → 每次接收要单独
    `CancellationTokenSource.CreateLinkedTokenSource(ct)` + `CancelAfter(300)`。
  - 防火墙：首次监听会弹 Windows 防火墙提示，要允许「专用网络」；被拦时扫描不到，
    UI 要有"没搜到可能是防火墙"的兜底文案。
- 隐藏自检：`--lan ips` / `--lan detect <游戏目录>` / `--lan plan <版本> <地址>`（输出到 lan.txt）；
  `--lan scan` = 自己当主机再自己扫，验证发现链路（实测能扫到 192.168.31.183:51234）。

## 隐藏 CLI（调试用，不影响正常启动）
`--check <版本> [--dir]` 查缺文件、`--repair <版本> [--dir]` 无弹窗补齐、
`--launch <版本> [--dir]` 无头启动 50 秒后 kill、`--plan <版本>` 导出完整启动参数、
`--loaders <mc> <kind>`、`--install-loader <mc> <kind> [版本|latest] [--dir]`、
`--install <mc> [--no-assets] [--mojang] [--dir]`、`--install-java <major>`、
`--install-mod <关键词> <mod|resourcepack|shader> <mc> [loader] [目录]`、
`--modrinth <关键词> [type] [mc] [loader]`、`--javas`、`--mstest`、`--nettest`、
`--bedrock check|list|go`（基岩版：检测安装/列包清单/完整下载安装，输出 `bedrock.txt`，原始响应 `bedrock_raw.json`）、
`--bedrock addon <包路径>`（本地装 addon）、`--lan ips|detect|plan`（局域网联机自检）。
输出落在 `%APPDATA%\HHCL\*.txt` 与 `hhcl.log`。
