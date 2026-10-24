# HHCL

Windows 上的 Minecraft 启动器（.NET 10 / WPF，WinUI 3 风格界面）。

支持原版与 **Fabric / Forge / NeoForge / Quilt** 加载器，可联网搜索安装模组、
资源包、光影和整合包，支持局域网直连联机，也可安装和启动基岩版。

## 功能

- **多版本管理**：自动读取本地所有版本，按加载器分组，一键安装 / 启动
- **联网资源**：Modrinth / CurseForge 搜索模组、资源包、光影、整合包
  （中文关键词会自动补搜英文同义词，"小地图"也能搜到 Xaero 小地图）
- **版本分组浏览**：详情页按游戏版本分组，每个组可折叠展开
- **局域网联机**：主机开放到局域网后自动读出房间码与地址，
  客机可**自动搜索附近房间**（UDP 广播发现），不用手抄 IP
- **基岩版**：可下载安装基岩版，支持多存档配置隔离
- **界面**：深色 / 浅色双主题，界面语言中文，游戏语言自动切成简体中文
- **启动 Java**：可一键下载 Java 运行时

## 构建

需要 .NET 10 SDK。

```bash
# 依赖 .NET 桌面运行时的小体积版本（对方需自己装 .NET 10）
dotnet publish -c Release -r win-x64 --self-contained false \
    -p:PublishSingleFile=true -o "HHCL发布"

# 自包含版本（对方什么都不用装，单文件约 126 MB）
dotnet publish -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:DebugType=none -o "HHCL-自包含版"
```

> 自包含版本必须加 `IncludeNativeLibrariesForSelfExtract=true`，
> 否则 WPF 的 native dll 会散落在目录里，就不算真正的单文件了。

## 使用

1. 双击 `HHCL.exe`（首次运行会在 `%APPDATA%\HHCL` 建立配置，游戏目录自动探测）
2. 到「账号」页登录自己的微软账号
3. 到「下载」页安装游戏版本和加载器
4. 回到「启动」页选版本启动

## 说明

HHCL 是**第三方启动器**，与 Mojang / Microsoft 无隶属关系，未获其官方认可或背书。
Minecraft 是 Mojang Studios 的商标。本项目不包含、不分发任何 Minecraft 游戏文件，
用户需要自己拥有正版账号，游戏文件由启动器从 Mojang 官方源获取。

## 许可

MIT License，见 [LICENSE](LICENSE)。
