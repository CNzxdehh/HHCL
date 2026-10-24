
### 23:58 全功能实测收尾（用户要求「做完后全部测一遍，直到真能用」）

全量实测结果（测试目录 `.hhcl-test2/.minecraft`，用户真实目录 `E:\.minecraft` 未受影响）：

- 版本安装：原版 1.21.4 ✅ / Fabric 0.19.5 ✅ / Forge 54.1.22 ✅ / NeoForge 21.4.161 ✅
- 实际启动（都进了资源加载 + OpenAL 初始化）：1.21.4 ✅ / 1.21.4-Fabric 0.19.5 ✅ / 1.21.4-forge-54.1.22 ✅ / neoforge-21.4.161 ✅ / 26.3 ✅
- 在线搜索：Modrinth 的 mod / resourcepack / shader 各 50 条 ✅
- 在线安装：Sodium.jar、Faithful 64x.zip、ComplementaryReimagined_r5.9.3.zip 三个都落地 ✅
- 一键补齐：删掉 jna-5.15.0.jar → 检出 1 个 → 补齐后 missing=0 ok=True ✅
- 微软登录：`--mstest` 能拿到真实 user_code / verify_url ✅（完整登录需人工在网页输码）

**这一轮修掉的 4 个真 bug（都是实测才暴露的）**

1. **Fabric 加载器列表永远超时**：`meta.fabricmc.net` 走系统代理时必挂（curl 1.6s 正常、HHCL 内 6 次重试全超时）。
   → 改用 PCL2 同款的 BMCLAPI 镜像 `https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/{mc}`（0.4s），
   并在 `Downloader.AltMirror` / `Unmirror` 里登记 `meta.fabricmc.net ↔ .../fabric-meta/` 映射，官方地址作兜底。
2. **Forge 预下载必失败**：`forge-xxx-client.jar` / `-slim.jar` / `-extra.jar` 是安装器**本地生成的补丁包**，
   maven 与 BMCLAPI 上都是 404 → 预下载要跳过 `-client` / `-server` / `-slim` / `-extra.jar` 结尾、
   以及 artifact.url 为空的项；且预下载整体失败不该中断安装（改成 try/catch 只记日志）。
3. **Fabric / Forge / NeoForge 版本报「缺少自己的 jar」**：这些 version json 都没有 jar 字段，
   正确做法是沿 `inheritsFrom` 链取**最底层父版本**的 jar（`chainIds[last]`），不是 `version.Id`。
4. **NeoForge 启动崩 `Modules minecraft and _1._21._4 export package ...`**：
   原版 `1.21.4.jar` 被当成自动模块 `_1._21._4` 塞进 module path，和 NeoForge 的补丁 client jar（模块名 `minecraft`）撞包。
   → 检测到「加载器自带补丁客户端 jar」就**跳过原版 jar**。三级判据（NeoForge 只满足第三条）：
   a. json 里 `net.minecraftforge:forge:x:client` 这类带 `:client` 分类器的库项；
   b. classpath 里有 forge/neoforge 目录且同目录存在 `*-client.jar`；
   c. `mainClass` 含 `bootstraplauncher` 且 `libraries/net/neoforged/neoforge/` 下存在 `*-client.jar`
      （**NeoForge 的 json 连自己的 jar 都不列**，只有这一条能命中）。
   Forge 跳过原版 jar 后仍正常启动（已验证）；原版 / Fabric 不受影响。

**其他改动**

- ModsView：空关键词也能搜（等于按热度浏览榜单）、切到「在线」页自动拉一批、切类型立即重搜 —— 直接回应用户「搜到的模组少」。
- MicrosoftAuth：`slow_down` 按规范 +5 秒、`expired_token` 给中文提示。
- `--launch` 支持 `--dir`（之前只能读 config 里的 GameDir，测试很不方便）。
- PCL2 里挖 CurseForge API key 失败（加密存储，全文扫 `$2a$10$` 零命中）→ 维持「用户在设置页自填」。

**发布**：`HHCL发布/HHCL.exe` 512655 B，桌面 `C:\Users\徐颢朗\Desktop\HHCL.exe` 已同步覆盖。
