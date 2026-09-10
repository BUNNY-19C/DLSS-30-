# DLSSG 30 系管理器

[![Release](https://img.shields.io/github/v/release/BUNNY-19C/DLSSG-30s-manager?style=flat-square&label=下载)](https://github.com/BUNNY-19C/DLSSG-30s-manager/releases/latest)
[![License](https://img.shields.io/github/license/BUNNY-19C/DLSSG-30s-manager?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078D4?style=flat-square)](#)
[![GPU](https://img.shields.io/badge/GPU-RTX%2030%20%E7%B3%BB%20(SM86)-76B900?style=flat-square)](#)
[![.NET](https://img.shields.io/badge/.NET-8-512BD4?style=flat-square)](#)

为 [dlssg_for_sm86](https://github.com/sdli1995/dlssg_for_sm86) 做的图形化管理器。把 mod 按游戏分别部署、一键恢复，不用再手工往游戏目录里复制 DLL。

这是一个 DLL 代理式 mod：把 `version.dll`（或其他入口名）和 `dlssg_sm86.ini` 放到**游戏渲染 EXE 旁边**，就能让 RTX 30 系（SM86）用上 DLSS 帧生成。

> [!IMPORTANT]
> **带内核级反作弊的游戏不能用。** 反作弊会在游戏启动前拦截并隔离代理 DLL，Mod 无法生效，检测记录还可能危及账号。程序会自动检测并禁止部署，详见[反作弊章节](#反作弊哪些游戏不能用)。

**[⬇ 下载最新版](https://github.com/BUNNY-19C/DLSSG-30s-manager/releases/latest)** —— 安装包或绿色版任选，都不需要装 .NET。

**主要特性**

- 自动扫描 Steam 库和任意文件夹，靠 `nvngx_dlssg.dll` 精确定位支持帧生成的游戏
- 每个游戏独立配置（路由、倍率上限、采样方式、日志级别），互不影响
- **添加游戏时自动检测内核级反作弊**，命中则弹窗告知并禁止部署
- 部署前备份被占用的文件，恢复时只删除经签名和哈希双重确认属于本项目的文件
- 批量部署 / 批量恢复
- 内置下载器可按需获取 mod 文件，仓库不必携带 75 MB 二进制

---

## 目录

- [直接用（推荐）](#直接用推荐)
- [从源码运行](#从源码运行)
- [界面说明](#界面说明)
- [这个管理器怎么保护你的文件](#这个管理器怎么保护你的文件)
- [反作弊：哪些游戏不能用](#反作弊哪些游戏不能用)
- [关于显卡](#关于显卡)
- [更新 mod 文件](#更新-mod-文件)
- [目录结构](#目录结构)
- [从源码构建](#从源码构建)
- [授权](#授权)

## 直接用（推荐）

到 [**Releases**](../../releases/latest) 下载，两种用法任选：

| 文件 | 说明 |
|---|---|
| `DLSSGManager-*-setup.exe` | **安装包**。安装路径可选，带卸载程序，创建开始菜单项 |
| `DLSSGManager.exe` | **绿色版**。单文件，放到任意目录双击即可 |

两者都不需要安装 .NET 或任何运行环境。

首次启动会问你要不要获取 Mod 文件（约 75 MB），选「是」等它下载完就能用。

### 安装包说明

运行后按向导选择安装路径（默认 `C:\Program Files\DLSSG Manager`，可改到任意位置）。

安装目录只放程序本体。Mod 文件在首次启动时下载，位置取决于安装方式：

- **安装版** → `%APPDATA%\DLSSGManager\mod`
- **绿色版** → 程序旁的 `mod\` 子目录（该目录不可写时同样回退到 `%APPDATA%`）

安装版这样安排是为了让**卸载不影响数据**：卸载只删除程序本体，会单独询问是否清除游戏列表、备份和已下载的 Mod 文件；保留的话重装后不必重新下载 75 MB。

### 使用

1. **扫描游戏** —— 点「扫描 Steam 库」，或用「扫描文件夹…」选某个游戏盘，也可以点「添加游戏…」直接指定单个游戏目录。
   程序靠 `nvngx_dlssg.dll` 定位游戏：游戏必须自带这个文件才支持 DLSS 帧生成，所以列表里不会出现无关的程序。Steam 库会自动读注册表和 `libraryfolders.vdf`，包含所有自定义库路径。
   **添加时会自动检测反作弊**：如果该游戏有内核级反作弊（会导致 mod 失效并有账号风险），会立即弹窗说明并禁止部署。
2. **选一款游戏** —— 左侧点一下，右侧出现它的独立配置。程序已自动按你的显卡填好路由（RTX 3080 Ti → `SM86`）。
3. **点「部署到该游戏」** —— 完成。mod 文件被复制进渲染目录。

要撤销就点「一键恢复」。批量操作在窗口底部：「全部部署」/「全部恢复」。

下载后可以核对哈希，发布页附有 `SHA256SUMS.txt`：

```powershell
Get-FileHash DLSSGManager.exe -Algorithm SHA256
```

---

## 从源码运行

本仓库**不含** mod 的二进制文件（约 75 MB，且授权不允许转发，原因见 [docs/mod-files.md](docs/mod-files.md)）。

需要 .NET 8 SDK，构建步骤见下方[从源码构建](#从源码构建)。构建后首次运行同样会提示获取 Mod 文件。

---

## 界面说明

上方工具条显示当前 Mod 文件源和你的显卡。右上角有两个按钮：

- **以管理员身份重启** —— 游戏装在 `C:\Program Files` 之类的位置时，写入需要管理员权限。点这个按钮会弹 UAC 重新启动。
- **打开数据目录** —— 打开 `%APPDATA%\DLSSGManager`，里面有配置和备份。

右侧配置面板里的每一项都直接对应 mod 的 INI 键，含义见 mod 作者的 [INI 文档](https://github.com/sdli1995/dlssg_for_sm86/blob/main/docs/NATIVE_INI.md)：

| 界面项 | INI 键 | 说明 |
|---|---|---|
| 显卡路由 | `Router` | RTX 30 系用 `SM86`，RTX 20 系用 `SM75` |
| 内核镜像 | `KernelImage` | 默认 `PTX`（驱动 JIT）。`Cubin` 要求精确匹配架构 |
| 倍率上限 | `MaxGeneratedFrames` | 1/2/3 对应 2X/3X/4X。实际倍率由游戏内设置决定 |
| 日志级别 | `Level` | 排查问题时设为 2，日志在游戏目录的 `dlssg_sm86\logs` |
| 近似采样 | `HardwareBilinear` | 默认关（精确输出）。开了会改变生成像素，仅 SM86 生效 |
| 诊断计时 | `[Diagnostics]` | 记录 GPU 计时，只用于剖析，会影响性能 |

配置是**按游戏独立保存**的，改完重新部署即可写入。

---

## 这个管理器怎么保护你的文件

游戏目录里往往已经躺着别的 mod（ReShade 的 `dxgi.dll` 之类），所以删除操作必须能分清"哪些是我的"。程序用三重判据：

1. **数字签名** —— mod 的 5 个 DLL 都带 `CN=DLSSG Native Project` 自签证书，程序会读签名确认归属。
2. **哈希指纹** —— 部署时记录每个文件的 SHA256，之后校验。
3. **备份** —— 部署前若发现目标文件名被非本项目的文件占用，会先把它备份到 `%APPDATA%\DLSSGManager\restore\<游戏>\<时间戳>\`，恢复时原样还原。

具体行为：

- **反作弊游戏直接拦截**：见上一节。内核级反作弊会让 mod 失效且有账号风险，所以默认拒绝。
- **入口名被占用时不会覆盖**：程序自动在 5 个入口名里挑一个空着的（`version.dll` → `winmm.dll` → `dinput8.dll` → `winhttp.dll` → `dxgi.dll`）。如果全被占用，会明确报错并保留现场，由你决定换哪个入口或先移除占用者。
- **恢复只删自己的文件**：哈希对不上就保留并提示，绝不盲删。
- **游戏运行时拒绝操作**：部署和恢复都会先检查渲染目录里有没有进程在跑。
- **手工装过的可以「接管」**：如果你之前自己复制过 mod，程序会检测到并提示接管，纳入管理后就能一键恢复。

---

## 反作弊：哪些游戏不能用

这是最重要的限制。**带内核级反作弊的游戏不能装这个 mod**，程序会主动拦截。

原因很直接：本 mod 是 DLL 代理，而内核级反作弊专门盯这类东西。它会**在游戏启动前就把 `version.dll` 拦截并改名隔离**（例如留下 `version.dll.3787982156`），结果有两个：

1. mod 根本不生效——游戏继续用自己那套渲染路径，还会报错；
2. **检测记录可能危及账号安全。**

实测确认的案例：**绝区零**内置米哈游 HoYoKProtect，检测到 `version.dll` 后直接改名隔离，游戏随即弹出 `The client component is running abnormally, please restart the client. Error Code:(0,11008,2195210578)`。

程序会扫描游戏目录**及其上溯 3 层**，识别这些反作弊并拦截部署：

| 反作弊 | 识别特征 |
|---|---|
| 米哈游 HoYoKProtect | `HoYoKProtect.sys`、`mhypbase.dll` |
| 腾讯 ACE | `ACE-*.sys`、`AntiCheatExpert\`、`SGuardSvc*.exe` |
| 网易 NEAC | `NeacSafe*.sys`、`NeacInterface.dll`、`NeacLoader.exe` |
| Easy Anti-Cheat | `EasyAntiCheat*.sys`、`start_protected_game.exe` |
| BattlEye | `BEClient*.dll`、`BEService*.exe` |
| nProtect GameGuard | `GameGuard.des`、`npgmup.des` |
| Riot Vanguard | `vgk.sys`、`vgc.exe` |
| XIGNCODE3 | `x3.xem`、`XignCode` |

检测同时覆盖**文件和目录**（腾讯 ACE 常以 `AntiCheatExpert\` 子目录形式存在），并会上溯 3 层父目录（反作弊常在游戏根目录而非渲染目录）。此外，游戏目录里出现任何 `.sys` 内核驱动都会被报告——正常游戏不会随包分发内核驱动，这能兜住尚未收录的厂商。Windows 自身的 `pagefile.sys` 等文件不会误报。

添加游戏时，你选中的目录会被**先解析到真正的渲染目录**再检测。这一步不能省：守望先锋的反作弊在 `E:\Overwatch\_retail_\`，如果你指向的是外层 `E:\Overwatch`，不解析就会一路向上扫描、完全错过 `NeacSafe64.sys`——实测确认过这种行为。

检测为内核级时：

- **添加游戏时立即弹窗**提示该游戏无法使用（若一次扫描出多个，合并成一条汇总提示，不逐条弹）；
- 配置面板顶部显示橙色警告条；
- **「部署到该游戏」按钮直接置灰不可点**，「全部部署」会把这类游戏排除在清单外并单独列出；
- 若你之前手工装过，「一键恢复」仍可用，用于清理残留。

这样即使在添加时漏看了提示，也不会有机会把文件写进受保护的游戏目录。

**实测记录**（开发机上扫描到的游戏，供参考）：

| 游戏 | 反作弊 | 能否使用 |
|---|---|---|
| 怪物猎人荒野 | 无 | ✅ |
| 绝区零 | 米哈游 HoYoKProtect | ❌ |
| 战争雷霆 | BattlEye | ❌ |
| 终末地 | 腾讯 ACE | ❌ |
| 守望先锋 | 网易 NEAC | ❌ |

绝区零和终末地都实测确认过：游戏启动时反作弊会弹窗并拦截 DLL，`version.dll` 被改名隔离（留下 `version.dll.3787982156` 之类的副本）。

**已经装过了怎么办**：点「一键恢复」。程序能识别并清理反作弊留下的改名副本（只删哈希或签名确认属于本项目的文件），同时移除 INI。状态栏会把这种情况显示为"已被反作弊隔离"而不是普通缺失。

**如果你确认风险后仍要部署**：程序会弹出二次确认，明确标注不推荐。但请注意，这类游戏上 Mod 不会生效。

**补充你的实测结果**：欢迎用[兼容性反馈](https://github.com/BUNNY-19C/DLSSG-30s-manager/issues/new?template=game_compatibility.yml)模板提交你测试的游戏，成功和失败的案例都有价值。

---

## 关于显卡

本 mod 的 SM86 路径由作者在 **RTX 3080 Ti** 上实卡验证，正是当前这台机器的配置。

几点需要知道：

- **RTX 40/50 系不需要它**：Ada 和 Blackwell 原生支持 DLSS 帧生成。程序检测到这类显卡会提示。
- **显存开销**：插帧额外占用按输出分辨率增长，1080p 约 320–340 MiB，1440p 约 490–520 MiB，4K 约 700–770 MiB。显存不足会出现偶发卡顿，即使平均帧率看着正常。
- **不支持 6X 和动态倍率**，也不支持 Reflex Warp。
- **杀软可能误报**：这类 DLL 代理 + hook 的行为容易被启发式检测盯上。5 个 DLL 都有自签证书（文件属性 → 数字签名 可查），但自签不提供 Windows 默认信任，也不保证消除告警。
- **游戏内要手动开帧生成**：部署完进游戏，在画面设置里启用 DLSS 帧生成。

---

## 更新 mod 文件

点「从 GitHub 更新 Mod 文件」，程序会从 `codeload.github.com` 下载最新源码包并写入 Mod 文件源目录，随后自动识别版本号。

网络访问是受限的：仅允许 HTTPS，仅允许 `github.com` / `codeload.github.com` / `raw.githubusercontent.com` / `api.github.com`，解析出的 IP 必须是公网地址（拒绝环回、内网、保留地址），重定向每一跳都重新校验，下载体积有上限，压缩包条目不允许逃出目标目录。GitHub 端点在部分网络上会偶发断连，因此失败会自动重试（最多 4 次）。

也可以手动换文件源：把新版 `version.dll`、`dlssg_sm86.ini` 和 `altnative\` 放进 `mod\` 即可，程序按目录结构识别。详见 [docs/mod-files.md](docs/mod-files.md)。

---

## 目录结构

```
DLSSGManager/
├─ src/DLSSGManager/          ← 源码（WPF，.NET 8）
├─ test/Harness/              ← 测试与诊断工具
├─ installer/                 ← Inno Setup 安装脚本（含简体中文语言文件）
├─ docs/mod-files.md          ← 为什么仓库不含 mod 二进制、如何获取
├─ mod/                       ← Mod 文件源（不提交，首次运行后自动下载）
├─ publish/ dist/             ← 发布产物（不提交）
```

程序运行时的数据：

```
%APPDATA%\DLSSGManager\
├─ library.json               ← 游戏列表、每个游戏的配置、部署记录
├─ restore\                   ← 被占用文件的备份
└─ manager.log                ← 操作日志
```

---

## 从源码构建

需要 .NET 8 SDK。

```bash
git clone <仓库地址>
cd DLSSGManager
dotnet build -c Release

# 打包成单文件 exe（自包含，目标机器无需装 .NET）
dotnet publish src/DLSSGManager/DLSSGManager.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none \
  -o publish
```

`publish/DLSSGManager.exe` 约 63 MB，可直接拷给别人用。

如果只想在本机快速跑，把 `--self-contained true` 换成 `false`，产物约 260 KB，但目标机器需要 .NET 8 运行时。

### 构建安装包

安装包用 [Inno Setup 6](https://jrsoftware.org/isdl.php) 编译（`installer/languages/` 下的简体中文语言文件随仓库提供，因为 Inno Setup 安装包未内置它）：

```powershell
# 先完成上面的 publish，再：
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DAppVersion=1.1.0 installer\setup.iss
```

产物是 `dist/DLSSGManager-<版本>-setup.exe`。脚本里的设计取舍（Mod 文件为何不装到安装目录、卸载为何保留数据）写在 `installer/setup.iss` 的头部注释里。

发布成品由 GitHub Actions 自动构建：推送 `v*` 标签（如 `git tag v1.1.0 && git push origin v1.1.0`）会构建、测试、编译安装包并创建 Release，附上两个 exe 与 `SHA256SUMS.txt`。也可以在 Actions 页面手动触发。

测试（153 项，覆盖部署/恢复/备份保护/反作弊识别与拦截/目录解析/接管/INI 渲染/持久化/下载 URL 策略）：

```bash
cd test/Harness
dotnet build -c Release

# 全量测试。未获取 Mod 文件时，依赖它们的用例会跳过并给出提示
./bin/Release/net8.0-windows/Harness.exe

# 获取 Mod 文件（写入项目的 mod 目录，等同于点界面上的更新按钮）
./bin/Release/net8.0-windows/Harness.exe --fetch

# 只扫描本机游戏并报告反作弊情况，不改动任何文件
./bin/Release/net8.0-windows/Harness.exe --scan "G:\SomeGame"
```

测试会把数据目录指向临时位置（通过 `DLSSGMANAGER_HOME` 环境变量），不会读写你的 `library.json` 和 `manager.log`。

---

## 授权

本仓库的源代码采用 [MIT 许可](LICENSE)。

不含 dlssg_for_sm86 发布的任何文件——那些是上游项目的产物，本项目仅按需下载并复制到游戏目录，不转发、不再授权。原因见 [docs/mod-files.md](docs/mod-files.md)。

使用本 mod 前请阅读上游仓库的说明，尤其是杀软误报、显存占用和反作弊相关的限制。

---

## 参与

欢迎提交游戏实测结果、反作弊特征或其他改进，见 [CONTRIBUTING.md](CONTRIBUTING.md)。

本程序只是 dlssg_for_sm86 的**部署工具**，不包含也不修改 Mod 本身。帧生成本身的问题（画质、性能、特定游戏的兼容性）请反馈给 [mod 作者](https://github.com/sdli1995/dlssg_for_sm86/issues)。
