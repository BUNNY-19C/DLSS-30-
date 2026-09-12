# DLSSG 30 系管理器

**简体中文** | [English](README.en.md)

[![Release](https://img.shields.io/github/v/release/BUNNY-19C/DLSSG-30s-manager?style=flat-square&label=下载)](https://github.com/BUNNY-19C/DLSSG-30s-manager/releases/latest)
[![License](https://img.shields.io/github/license/BUNNY-19C/DLSSG-30s-manager?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078D4?style=flat-square)](#)
[![GPU](https://img.shields.io/badge/GPU-RTX%2030%20%E7%B3%BB%20(SM86)-76B900?style=flat-square)](#)
[![.NET](https://img.shields.io/badge/.NET-8-512BD4?style=flat-square)](#)

为 [dlssg_for_sm86](https://github.com/sdli1995/dlssg_for_sm86) 做的图形化管理器。把 mod 按游戏分别部署、一键恢复，不用再手工往游戏目录里复制 DLL。

这是一个 DLL 代理式 mod：把 `version.dll`（或其他入口名）和 `dlssg_sm86.ini` 放到**游戏渲染 EXE 旁边**，就能让 RTX 30 系（SM86）用上 DLSS 帧生成。

> [!IMPORTANT]
> **带内核级反作弊的游戏属于风险区。** 反作弊可能拦截并隔离代理 DLL，检测记录还可能危及账号。程序会检测到并提示风险，是否部署由你决定，详见[反作弊章节](#反作弊风险评估由你决断)。

**[⬇ 下载最新版](https://github.com/BUNNY-19C/DLSSG-30s-manager/releases/latest)** —— 安装包或绿色版任选，都不需要装 .NET。

**主要特性**

- 扫描 Steam 库或任意文件夹，靠 `nvngx_dlssg.dll` 找出真正支持帧生成的游戏
- 每款游戏独立配置（路由、倍率上限、采样方式、日志级别）
- 添加游戏时扫一遍内核级反作弊，命中就先提示风险，是否部署你说了算
- 部署前备份被占用的文件，恢复时只删签名和哈希都对得上的文件
- 批量部署 / 批量恢复
- 自带下载器，仓库不用塞 75 MB 二进制；绝区零要用的 `d3d12.dll` 入口也一并下发

---

## 目录

- [直接用（推荐）](#直接用推荐)
- [从源码运行](#从源码运行)
- [界面说明](#界面说明)
- [这个管理器怎么保护你的文件](#这个管理器怎么保护你的文件)
- [反作弊：风险评估，由你决断](#反作弊风险评估由你决断)
- [关于显卡](#关于显卡)
- [更新 mod 文件](#更新-mod-文件)
- [附加入口：d3d12 与自定义 DLL](#附加入口d3d12-与自定义-dll)
- [目录结构](#目录结构)
- [主题与多语言](#主题与多语言)
- [从源码构建](#从源码构建)
- [授权](#授权)

## 直接用（推荐）

到 [**Releases**](../../releases/latest) 下载，两种用法任选：

| 文件 | 说明 |
|---|---|
| `DLSSGManager-*-setup.exe` | **安装包**。安装路径可选，带卸载程序，创建开始菜单项 |
| `DLSSGManager.exe` | **绿色版**。单文件，放到任意目录双击即可 |

两者都不需要安装 .NET 或任何运行环境。

Mod 文件（约 75 MB）不随安装包分发，程序会自动获取：**安装包可在安装时勾选下载**，绿色版则在首次启动时询问。

### 安装包说明

运行后按向导选择安装路径（默认 `C:\Program Files\DLSSG 30 系管理器`，可改到任意位置）。向导启动时会先让你选安装语言（简体中文 / English）。

安装向导里有一个「**Mod 文件**」选项组，勾选后会在安装过程中直接下载（约 75 MB）。在安装阶段下载有个好处：安装程序此时已提权，所以**即使装到 `Program Files` 也能把文件写进程序目录**。

Mod 文件的位置规则：

| 情况 | 位置 |
|---|---|
| 程序目录可写（大多数情况） | 程序旁的 `mod\` |
| 程序目录不可写（装在 `Program Files` 且未提权） | `%APPDATA%\DLSSGManager\mod` |

装在程序旁让整个目录自包含，拷走就能用。卸载时会单独询问是否删除 Mod 文件与游戏数据，**静默卸载一律保留**——重装后不必重新下载 75 MB。

### 使用

1. **扫描游戏** —— 点「扫描 Steam 库」，或用「扫描文件夹…」选某个游戏盘，也可以点「添加游戏…」直接指定单个游戏目录。
   程序靠 `nvngx_dlssg.dll` 定位游戏：游戏必须自带这个文件才支持 DLSS 帧生成，所以列表里不会出现无关的程序。Steam 库会自动读注册表和 `libraryfolders.vdf`，包含所有自定义库路径。
   **添加时会自动扫一遍反作弊**：如果该游戏有内核级反作弊（可能拦下 mod，账号也有风险），会弹窗说明情况。不会禁止你部署，但部署前还会再确认一次。
2. **选一款游戏** —— 左侧点一下，右侧出现它的独立配置。程序已自动按你的显卡填好路由（RTX 3080 Ti → `SM86`）。
3. **点「部署到该游戏」** —— 完成。mod 文件被复制进渲染目录。

要撤销就点「一键恢复」。批量操作在窗口底部：「全部部署」/「全部恢复」。

**绝区零要换个入口**：它的反作弊会把 `version.dll` 这类自带入口改名隔离，实测只有 `d3d12.dll` 能用。具体做法见[绝区零要用哪个入口](#绝区零要用哪个入口)。

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

上方工具条显示当前 Mod 文件源和你的显卡；右侧是扫描、下载与「添加代理 DLL…」按钮（后者见[附加入口](#附加入口d3d12-与自定义-dll)）。窗口右上角有四个控件：

- **以管理员身份重启** —— 游戏装在 `C:\Program Files` 之类的位置时，写入需要管理员权限。点这个按钮会弹 UAC 重新启动。
- **打开数据目录** —— 打开 `%APPDATA%\DLSSGManager`，里面有配置和备份。
- **主题** —— 界面在深色与浅色之间切换，立即生效并记住选择。
- **语言** —— 界面在简体中文与英文之间切换，立即生效并记住选择。

配置面板里的每一项都直接对应 mod 的 INI 键，含义见 mod 作者的 [INI 文档](https://github.com/sdli1995/dlssg_for_sm86/blob/main/docs/NATIVE_INI.md)：

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

- **反作弊游戏会先提醒**：内核级反作弊可能拦下代理 DLL，还有账号风险，所以程序会在部署前把风险和证据摆出来，确认了才写文件。见[反作弊章节](#反作弊风险评估由你决断)。
- **入口名被占用时不会覆盖**：程序自动在可用入口名里挑一个空着的（自带的 5 个优先：`version.dll` → `winmm.dll` → `dinput8.dll` → `winhttp.dll` → `dxgi.dll`，之后才是你添加的本地入口）。如果全被占用，会明确报错并保留现场，由你决定换哪个入口或先移除占用者。
- **恢复只删自己的文件**：哈希对不上就保留并提示，绝不盲删。
- **游戏运行时拒绝操作**：部署和恢复都会先检查渲染目录里有没有进程在跑。
- **手工装过的可以「接管」**：如果你之前自己复制过 mod，程序会检测到并提示接管，纳入管理后就能一键恢复。

---

## 反作弊：风险评估，由你决断

mod 是 DLL 代理，内核级反作弊专盯这个，所以这类游戏算风险区。反作弊可能在游戏启动前就把代理 DLL 改名隔离（比如留下 `version.dll.3787982156`），也可能把这次检测记下来，账号上有风险。

实测确认的案例：**绝区零**内置米哈游 HoYoKProtect，检测到 `version.dll` 后直接改名隔离，游戏随即弹出 `The client component is running abnormally, please restart the client. Error Code:(0,11008,2195210578)`。同一款游戏上，社区编译的 `d3d12.dll` 入口就没事，见[绝区零要用哪个入口](#绝区零要用哪个入口)。

程序会扫描游戏目录**及其上溯 3 层**，认得下面这些反作弊，并在部署前提示：

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

检测同时覆盖**文件和目录**（腾讯 ACE 常以 `AntiCheatExpert\` 子目录形式存在），并会上溯 3 层父目录，因为反作弊大多躺在游戏根目录而不是渲染目录。另外游戏目录里出现任何 `.sys` 内核驱动都会被报告（正常游戏不会随包带驱动，这条能兜住表里还没收录的厂商），Windows 自己的 `pagefile.sys` 之类不会误报。

添加游戏时，你选的目录会**先解析到真正的渲染目录**再扫。不解析就会一路向上扫、把反作弊漏掉：守望先锋的反作弊在 `E:\Overwatch\_retail_\` 里，你要是指向外层的 `E:\Overwatch`，`NeacSafe64.sys` 就完全扫不到，实测过。

扫到内核级反作弊时，程序只提示、不禁止：

- 添加游戏时弹一次窗说明风险（一次扫出多个就合成一条，不逐个弹）；
- 配置面板顶部挂一条橙色警告；
- 点「部署到该游戏」时弹确认框，默认选"否"，你点了"是"才写文件；
- 「全部部署」的确认框把这类游戏单独列出来，确认后一起部署；
- 「一键恢复」一直可用，用来清残留。

扫描只查得出反作弊在不在，查不出它会不会拦你选的那个入口名。绝区零上 `version.dll` 被隔离、`d3d12.dll` 却能用，就是现成的例子。既然只有你能判断，程序就把证据和后果列清楚，由你决定。

**实测记录**（开发机上扫到的，供参考）：

| 游戏 | 反作弊 | 结论 |
|---|---|---|
| 绝区零 | 米哈游 HoYoKProtect | ⚠ 自带入口会被隔离，改用 `d3d12.dll`（[见下](#绝区零要用哪个入口)） |
| 战争雷霆 | BattlEye | 未测试 |
| 终末地 | 腾讯 ACE | 未测试 |
| 守望先锋 | 网易 NEAC | 未测试 |
| 怪物猎人荒野 | 无 | ✅ 装上前置 REFramework 后可用（4X，无错误） |

带反作弊的四款，程序都会在部署前提示风险，是否部署由你决定。

**怪物猎人荒野**：没有反作弊，但装上 mod 后必崩（`MonsterHunterWilds.exe + 0xa4d69d0`，换 mod 版本、更新 DLSS 运行库、关掉游戏内 FG 开关都没用）。

**原因是缺前置 [REFramework](https://github.com/praydog/REFramework)**，RE Engine 游戏的 mod 框架（怪猎用它的 `MHWILDS` 包）。装上之后实测通过：管理器照常部署（代理 + INI），`dinput8.dll` 让给框架、代理自动改用 `version.dll`，游戏内帧生成正常，mod 日志全程 0 错误、每个实际帧生成 3 个插帧（4X），退出时正常释放。

前置要手动装，它属于另一个项目，管理器不代装：从 REFramework 的 Releases 下载 `MHWILDS.zip`，把 `dinput8.dll`、`openvr_api.dll`、`openxr_loader.dll`、`reframework\` 解压到 `MonsterHunterWilds.exe` 旁边即可。

**已经装过了怎么办**：点「一键恢复」。程序能识别并清理反作弊留下的改名副本（只删哈希或签名确认属于本项目的文件），同时移除 INI。状态栏会把这种情况显示为"已被反作弊隔离"而不是普通缺失。

**在反作弊游戏上部署之后**：如果游戏报错、或帧生成没生效，就是这个入口名被拦了，换个入口名重试，或者直接「一键恢复」。检测记录带来的账号风险由你自己承担。

**补充你的实测结果**：欢迎用[兼容性反馈](https://github.com/BUNNY-19C/DLSSG-30s-manager/issues/new?template=game_compatibility.yml)模板提交你测试的游戏，成功和失败的案例都有价值。

---

## 关于显卡

本 mod 的 SM86 路径由作者在 **RTX 3080 Ti** 上实卡验证。

几点：

- **RTX 40/50 系用不上**：Ada 和 Blackwell 原生支持 DLSS 帧生成，程序检测到这类显卡会提示。
- **显存**：插帧额外占用按输出分辨率涨，1080p 约 320–340 MiB，1440p 约 490–520 MiB，4K 约 700–770 MiB。显存不够会偶发卡顿，平均帧率看着正常也白搭。
- **不支持 6X 和动态倍率**，也不支持 Reflex Warp。
- **杀软可能误报**：DLL 代理加 hook，启发式检测容易盯上。5 个 DLL 都有自签证书（文件属性 → 数字签名 可以看），但自签过不了 Windows 的默认信任，也不保证不报警。
- **帧生成要在游戏里手动开**：部署完进游戏，画质设置里启用 DLSS 帧生成。

### 架构按硬件 ID 判定，而非显卡名称

程序读 **PCI 设备 ID** 决定用 SM86 还是 SM75，显卡名称只作交叉校验。

不是多此一举：名称存在注册表里，工具能改；设备 ID 绑在物理芯片上。我们实测碰到过一台机器，注册表里写着 `RTX 4090`（Ada），硬件 ID `2208` 其实是 RTX 3080 Ti（Ampere）。按名字判断会得出"40 系不用装 mod"的错误结论，按硬件 ID 才对。

两者不一致时程序会警告，并建议把显卡名称改回去：名称错了不只是显示问题，驱动和游戏也会跟着做出错误的功能判断。

---

## 更新 mod 文件

点「下载 / 更新 Mod 文件」即可。程序会依次尝试多个下载源，直到有一个成功：

> 除了上游的 mod 文件，这一步还会拿本仓库分发的 `d3d12.dll`（约 10 MB，按固定哈希校验，已存在就跳过），也就是绝区零要用的那个入口。详见[附加入口](#附加入口d3d12-与自定义-dll)。

| 顺序 | 源 | 说明 |
|---|---|---|
| 1 | GitHub 归档（codeload） | 单次请求，约 28 MB，最快 |
| 2 | GitHub API（zipball） | 同一内容的不同入口，codeload 受限时可用 |
| 3 | GitHub 原始文件（raw） | 逐个文件下载，约 75 MB，与上面两条链路不同 |
| 4 | gh-proxy 国内加速 | 国内节点，实测最快（15 MB 不到 1 秒） |
| 5 | jsDelivr CDN 镜像 | 公共 CDN，GitHub 不可达时的备用路径 |
| 6 | ghfast 国内加速 | 国内节点，仅逐文件下载 |

**点「下载 / 更新 Mod 文件」会先弹出选择框**，你可以指定用哪个源，或保持默认的「自动」（依次尝试所有源，某个不可用时自动切换）。选了具体某个源就只用它，失败不会偷偷换到别处，日志里的来源才可信。

每个源失败会重试一次再换下一个，所以某个端点被墙或抖动只会导致降级，不会让更新失败。

### 第三方镜像的信任边界

后三个源（gh-proxy、jsDelivr、ghfast）是第三方转发，不是内容的权威，所以对它们**强制校验证书指纹**：只有签名证书与记录值完全一致才接受。官方源的指纹差异则记录警告后放行，以免上游更换证书后更新功能失效。

这个区别写在下载器的注释里（`ModFetcher.Verify`）。下载源只在编译期定义、不做成可配置的运行时文件，也是同一个原因：源列表本身就是一条信任边界。

### 下载内容会校验

Mod 是会被放进游戏目录的原生 DLL，所以下载路径按不可信处理：

- 仅允许 HTTPS，仅允许上表中的域名，解析出的 IP 必须是公网地址（拒绝环回、内网、保留地址），重定向每一跳都重新校验；
- 响应体积有上限，压缩包条目不允许逃出目标目录；
- **下载后校验签名**：每个 DLL 必须带有项目证书的有效 Authenticode 签名（改一个字节即失效），且签名证书指纹须与记录值一致。

也可以手动放文件：把 `version.dll`、`dlssg_sm86.ini` 和 `altnative\` 放进 `mod\`，程序按目录结构识别。详见 [docs/mod-files.md](docs/mod-files.md)。

## 附加入口：d3d12 与自定义 DLL

上游只发布 5 个入口名：`version.dll`、`winmm.dll`、`dinput8.dll`、`winhttp.dll`、`dxgi.dll`。入口名就是"游戏会去加载的 DLL 名"，游戏加载了代理才会进到进程里。有些游戏的保护模块会盯着这几个名字，所以社区编译了别的入口，`d3d12.dll` 是最常用的一个：游戏初始化 DX12 后端时才动态加载它，保护模块抢不到这个模块名。

**这个文件本仓库直接分发，不用自己去找。** 点「下载 / 更新 Mod 文件」（安装向导勾选「Mod 文件」时也一样）就会自动获取，放到 `mod\altnative\d3d12.dll`，之后在每款游戏的「代理入口」下拉里出现。

它没有本项目能验证的签名，所以改用固定 SHA-256：字节不符就丢掉，换哪个下载源都一样。哈希写在源码里（`ModFetcher.Extras`），来源与成分见 [extra-proxies/README.md](extra-proxies/README.md)。

### 绝区零要用哪个入口

绝区零是必须换入口的典型。它的 HoYoKProtect 会盯游戏目录，`version.dll` 这种自带入口名放进去会被改名隔离，游戏还会弹 `Error Code:(0,11008,2195210578)`；实测只有社区编译的 `d3d12.dll` 活了下来。

要做的事只有一件：在「代理入口」下拉里把「自动」改成 `d3d12.dll`，再点部署。这个 DLL 管理器已经下好了，不用去别处找。

顺带说一句，你在外面拿到的绝区零整合包一般还带两个 NVIDIA 运行时 DLL（`nvngx_dlss.dll`、`nvngx_dlssg.dll`，310.9.1 版），管理器只管代理和 INI，不碰这两个文件。要是只放代理和 INI、帧生成没出来，可以把那两个 DLL 也手动复制进游戏目录，记得先备份游戏自带的。

### 用自己的 DLL

自己编译的、或者社区别的构建，点工具条上的「添加代理 DLL…」选文件就行：

- 按原文件名复制到 `mod\altnative\`。名字不能改，游戏就是按这个名字找 DLL 的；
- 只读一下签名者写进日志（有签名显示证书主体，没有就标"未签名"）。文件是你给的，来源你自己确认；
- 之后和自带入口一样出现在下拉里，能部署、能一键恢复。恢复时按部署时记下的 SHA256 删，别的文件不碰。

两条限制：自带那 5 个名字不许覆盖，签名校验靠它们；名字不在已知列表里（上面 5 个加 `d3d12.dll`）会提示一句"游戏多半不会加载它"，要不要继续你定。另外，如果 `mod\altnative\d3d12.dll` 已经存在且跟仓库版本不一样，下载器会保留你的那份，不会被覆盖。

顺便解释下为什么不能随便起名字：每个构建只导出它对应名字需要的系统 API。管理器不会把 `version.dll` 改名成 `d3d12.dll` 来部署，那样游戏的 D3D12 导入找不到实现，游戏根本起不来。

---

## 目录结构

```
DLSSGManager/
├─ src/DLSSGManager/          ← 源码（WPF，.NET 8）
├─ test/Harness/              ← 测试与诊断工具
├─ installer/                 ← Inno Setup 安装脚本（含简体中文语言文件，英文用内置的 Default.isl）
├─ docs/mod-files.md          ← 为什么仓库不含 mod 二进制、如何获取
├─ mod/                       ← Mod 文件源（不提交，首次运行后自动下载）
├─ publish/ dist/             ← 发布产物（不提交）
```

程序运行时的数据：

```
%APPDATA%\DLSSGManager\
├─ library.json               ← 游戏列表、每个游戏的配置、部署记录、界面语言
├─ restore\                   ← 被占用文件的备份
├─ mod\                       ← Mod 文件（仅当程序目录不可写时使用）
└─ manager.log                ← 操作日志
```

---

## 主题与多语言

**主题**：深色 / 浅色两套，在工具条右端切换，立即生效并记住（`library.json` 的 `InterfaceTheme`）。配色定义在 `src/DLSSGManager/Themes/`，界面用 `DynamicResource` 引用。这里必须用动态引用：静态引用在元素创建时就定死了，切换主题后一大片界面会停在旧配色上。

浅色不是把深色直接反相，颜色是重新取的：同样的绿和橙放白底上对比度不够。测试会检查两套主题的键完全一致，并逐对算文字与背景的对比度（正文 7:1、次要文字 4.5:1）。还有一项检查确保界面文件和代码里没有硬编码颜色，漏掉主题化的控件切换后才露出来，肉眼很难发现。

**语言**：简体中文与英文，同样在工具条右端切换、立即生效并记住（`InterfaceLanguage`）。安装向导启动时也会先让你选安装语言。

两套文案在 `src/DLSSGManager/Strings.*.cs`，键必须一一对应：测试会比对两张表，缺翻译直接测试失败，而不是界面上冒出一个键名。占位符（`{0}`）也逐键比对，免得某种语言下参数错位。

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

测试（372 项，覆盖部署/恢复/备份保护/反作弊识别与风险提示/入口名管理/附加入口分发与哈希固定/目录解析/接管/INI 渲染/持久化/下载 URL 策略/签名校验/下载源选择/主题与多语言）：

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

本仓库的源代码（管理器的 C# 实现）采用 [MIT 许可](LICENSE)。

**MIT 许可不覆盖 dlssg_for_sm86 发布的任何文件。** 它们由上游项目提供，本项目仅在其发布位置按需下载并复制到游戏目录，不转发、不再授权。原因见 [docs/mod-files.md](docs/mod-files.md)。

使用本 mod 前请阅读上游仓库的说明，尤其是杀软误报、显存占用和反作弊相关的限制。

---

## 参与

欢迎提交游戏实测结果、反作弊特征或其他改进，见 [CONTRIBUTING.md](CONTRIBUTING.md)。

本程序只是 dlssg_for_sm86 的**部署工具**，不包含也不修改 Mod 本身。帧生成本身的问题（画质、性能、特定游戏的兼容性）请反馈给 [mod 作者](https://github.com/sdli1995/dlssg_for_sm86/issues)。
