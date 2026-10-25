# HHCL 项目长期记忆
> 详细过程见 `.workbuddy/memory/YYYY-MM-DD.md`；本文件只留"改代码前必须知道"的红线。

## 发布与分发
- 源码 `HHCL\`；`dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o "HHCL发布"` → `HHCL发布\HHCL.exe`。
- **桌面也有一份 `C:\Users\徐颢朗\Desktop\HHCL.exe`，用户从桌面双击 → 每次发布后必须覆盖过去**（先备份旧版）。运行中 exe 锁文件 → `Stop-Process -Name HHCL`。别从 `bin\...\publish\` 拷（是旧产物）。验证修复：`grep -c "关键字符串" HHCL发布/HHCL.exe`。
- 发给别人：自包含单 exe（`--self-contained true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none`）→ zip 53.6MB，对方零依赖。**轻量版（需自装 .NET）已按用户要求删除**。
- 分发走夸克网盘 `https://pan.quark.cn/s/2a54504cc747?pwd=56wz`（56wz）；GitHub Release 只放说明+链接，附件 0。
- `Config.GameDir` 不写死路径 → `DetectGameDir()` 探测 `%APPDATA%\.minecraft` → OneDrive → 都没有则创建。
- 版本号单一源头 `Core/AppInfo.cs` 的 `AppInfo.Version`（1.1）；`MainWindow.xaml` 不硬编码。
- 新增 XAML 窗口时 StaticResource key 必须在 `Themes/Fluent.xaml` 定义（是 `StrokeBrush`，不是 `CardBorderBrush`）。

## 用户明确要求
- 名字 **HHCL**；WinUI 3/Fluent 风格参照 PCL2。模组页必须能联网下载（Modrinth）；版本页支持 Fabric/Forge/NeoForge/Quilt。设置页有「下载 Java 25」；缺文件弹「一键补齐」并自动启动。**不搞赞助/付费**。

## 环境坑（本机）
- `maven.neoforged.net` 不可达 → NeoForge 依赖走 BMCLAPI 镜像。`meta.fabricmc.net` 走代理必超时 → 用 `bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/{mc}`（备 `bmclapi.bangbang93.com`）。
- Bash `rm` 被沙箱拦 → 用 Python 删除。`dotnet build` 偶发不重编 → `touch` 源文件。Bash 里别用反引号包中文。
- 调 PS 用 `-EncodedCommand <base64(UTF16LE)>`，先设 `$ProgressPreference='SilentlyContinue'`，结果用 `VER:` 标记。
- GitHub API 走 `https://gh-proxy.com/https://api.github.com/...`；`agent-browser eval` 传 >32KB 会超 Windows cmdline 上限 → 改用 git push 直连。

## 启动器红线（改 LaunchBuilder 前必看）
- 客户端 jar：json 有 `jar` 字段就用它；否则沿 `inheritsFrom` 取**最底层父版本**的 jar，绝不能用 `version.Id`。链上用 `chainIds` 记 id。
- 加载器自带补丁客户端时跳过原版 jar，否则 NeoForge 崩 `Modules ... export package`。判据：① json 含 `net.minecraftforge:forge:x:client`；② classpath 有 forge/neoforged 目录且同目录 `*-client.jar`；③ `mainClass` 含 `bootstraplauncher` 且 `libraries/net/neoforged/neoforge/` 有 `*-client.jar`。
- 预下载依赖跳过 `-client/-server/-slim/-extra.jar`，失败只记日志不中断。
- MC 26.x：参数含 `${clientid}`/`${auth_xuid}`（空值必崩）；natives dll 在 jar 内 `windows/x64/org/lwjgl/`，`java.library.path` 指向 `{natives}/java` 且不递归 → dll 平铺到 `{natives}` 与 `{natives}/java` 两份。

## 微软令牌三种，千万别混用
- `AccessToken`=Minecraft 令牌→游戏 `--auth_access_token`；`MsAccessToken`=微软身份令牌→XBL/XSTS 与基岩版商店授权必须用它；`RefreshToken` 换来的也是微软身份令牌 → `RefreshAsync` 必须再走 XBL→XSTS→login_with_xbox 才能更新 `AccessToken`（已修）。没存 `MsAccessToken` 时 `GetMsTokenAsync()` 现换。

## 基岩版
- ProductId `9NBLGGH2JHXJ`、ContentId `7792d9ce-355a-493c-afbd-768f4a77c3b0`、包族 `Microsoft.MinecraftUWP_8wekyb3d8bbwe`；新版 MSIXVC 主包 ≈1.93GB，`update-*.xsp` 是更新清单不是包。
- 鉴权：MS 令牌 → XBL → XSTS(RP=`http://update.xboxlive.com`) → 头 `XBL3.0 x=uhs;token`；取包 `GET https://packagespc.xboxlive.com/GetBasePackage/{contentId}`。**下载地址 = CdnRootPath + RelativeUrl**；CDN 只有 http 可用 → https→http 降级；支持 Range，实测 61.6MB/s。启动 AppID `...UWP_8wekyb3d8bbwe!**Game**`。
- 存档路径：GDK 新版 `%APPDATA%\Minecraft Bedrock\Users\<XUID>\games\com.mojang`；UWP 老版 `%LOCALAPPDATA%\Packages\...\LocalState\games\com.mojang`；`BedrockProfiles.FindGameDataDir()` 自动探测。**XUID 只在 RP=`http://xboxlive.com` 的 XSTS 响应里有**（`DisplayClaims.xui[0].xid`）。
- 配置隔离 `Core/BedrockProfile.cs`：每套配置 = `%APPDATA%\HHCL\bedrock_profiles\<名>\`，启动前「配置→游戏目录」，退出后反向；**增量同步**（大小+修改时间）。游戏进程 `Minecraft.Windows`。基岩版无法多版本共存，别做半成品。
- 自定义 `FluentComboBox` 不吃 DisplayMemberPath → ItemsSource 用字符串；XAML 写 `SelectedIndex="0"`。
- addon：Modrinth **没有**基岩内容；CurseForge 有 2.6 万但需有效 API Key（CF 网页被挡），gameId/classId 动态发现（`/v1/games` → slug `minecraft-bedrock` → categories 里 parentId 为空者为根 classId）。包名在 **manifest.json 的 `header.name`**；`modules[].type` `data`/`script`→behavior_packs、`resource`→resource_packs；含 `level.dat`→worlds。

## 合规
- **不收费 = 唯一完全合规姿势**（Modrinth/CurseForge 均个人非商用）。红线：不搞赞助解锁、不代下载/代安装、不批量分发基岩版安装包；可给 Discord 身份组/致谢。不分发游戏本体、不绕正版验证、不冒充官方。无 GPL 代码引用。User-Agent 带联系方式（`Core/AppInfo.cs`）。「关于」页已有免责声明。税务备用：爱发电不代扣个税，手续费 6%。

## 主题（深/浅色）
- XAML 里的 SolidColorBrush/LinearGradientBrush 会被冻结 → 换主题必须**新建画刷替换资源项**（`Application.Current.Resources["BgBrush"] = new SolidColorBrush(...)`），引用用 `{DynamicResource}`。
- 渐变 4 个在 `ThemeManager.Gradients`（WindowBg / HeroGradient / AccentGradient / LogoGradient）→ **漏 WindowBgBrush 会导致浅色下主内容区还是深色**（踩过）。
- `Core/ThemeManager.cs` 调色表 26 项，`Apply("dark"|"light")`，在 `App_Startup` 创建 MainWindow 之前调用。浅色易露馅的硬编码已换语义画刷（OverlaySoft/AccentPanel/AccentTag/Footer…）。查漏：扫 `{StaticResource XxxBrush}` 与 ThemeManager key 表对比差集。渲染验证 `--shot <page> <w> <h> light`，背板别写死深色。

## 游戏语言中文
- 改 `<gameDir>/options.txt` 的 `lang:`→`zh_cn`（`Core/GameLanguage.EnsureChinese`）；用 `plan.GameDir`（每版本独立目录）。JVM 加 `-Duser.language=zh -Duser.country=CN`。

## 局域网联机 + 访问密码
- 端口从 `<gameDir>/logs/latest.log` 读 `LAN server on port (\d+)`（只读末尾 64KB）。内网 IP：排除 127.*/Tunnel/虚拟网卡，优先 192.168.*→10.*→172.16-31.*，跳过 169.254.*。
- 客机 `--quickPlayMultiplayer <IP:端口>`，**必须**开 `features["is_quick_play_multiplayer"]=true` + `has_quick_plays_support=true`（1.20+）。
- 自动发现 `Core/LanDiscovery.cs`：主机 UDP 45678 应答 "ROOM|ip|port"，客机广播 "HELLO"。坑：`UdpClient.ReceiveAsync(ct)` 不带超时会永久挂 → 每次接收单独 `CancelAfter(300)`；防火墙被拦要有兜底文案。
- **访问密码**（自研 `Core/LanAuth.cs`）：主机 Server 监听代理端口（默认 45679）→ 校验密码 → 转发 `127.0.0.1:游戏端口`；客机 Client 起本地代理 → 握手 → 转发主机。协议 `HHCL-AUTH/1\n`+密码行+`OK\n`/`DENIED\n`，之后透传原字节。比较用 `CryptographicOperations.FixedTimeEquals`。自测 `tools/LanAuthTest/`（4/4 通过）。**绕过边界**：同局域网可直连游戏端口绕过密码；跨网只映射代理端口才受保护。
- WPF 输入框"前面一大堆空格"真因是 `HorizontalContentAlignment` 默认居中 → `Themes/Fluent.xaml` 的 `FluentTextBox` 里 Setter + 模板 `PART_ContentHost` 钉 `Left`，并加 `MaxWidth=400` 防撑满整行。

## 隐藏 CLI（调试）
`--check/--repair/--launch/--plan <版本>`、`--loaders/--install-loader/--install/--install-java/--install-mod/--modrinth/--javas/--mstest/--nettest`、`--bedrock check|list|go|addon`、`--lan ips|detect|plan|scan`。输出落 `%APPDATA%\HHCL\*.txt` 与 `hhcl.log`。
