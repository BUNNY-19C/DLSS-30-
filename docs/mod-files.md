# 为什么仓库里没有 mod 文件

运行管理器需要上游 [dlssg_for_sm86](https://github.com/sdli1995/dlssg_for_sm86) 发布的文件：

```
mod/
├─ version.dll              ← 默认代理入口
├─ dlssg_sm86.ini           ← 默认配置
├─ altnative/               ← 四个备用入口
│  ├─ winmm.dll
│  ├─ dinput8.dll
│  ├─ winhttp.dll
│  └─ dxgi.dll
└─ config/presets/          ← 两档预设（可选）
   ├─ sm86-default.ini
   └─ sm86-performance.ini
```

这些文件**不提交到本仓库**，原因有两个。

## 1. 授权

它们是 mod 作者编译发布的二进制，不是本项目的代码。其 `THIRD_PARTY_NOTICES.txt` 里写明：

> The NVIDIA runtime DLL and extracted/recompiled NVIDIA kernel assets are separate
> third-party material from this user's local installation; they are not relicensed by LICENSE.md.

也就是 NVIDIA 运行时和从中提取、重编译的内核资源**不在该项目可再授权的范围内**。本仓库因此不转发这些材料——需要的人从上游获取即可。

## 2. 体积

五个 DLL 合计约 75 MB，放进 git 历史既臃肿，也意味着每次上游更新都要多一份副本。

## 怎么获取

**方法一：程序内置下载器（推荐）**

启动管理器，点工具条上的「**从 GitHub 更新 Mod 文件**」。它从 `codeload.github.com` 下载官方源码包（约 28 MB）并解压到 `mod/`，随后自动识别版本号并在界面上显示（例如「可用 · Native 0.2.3」）。

如果检测到文件源不可用，程序会在运行输出里说明缺什么。

**方法二：手动放置**

从 <https://github.com/sdli1995/dlssg_for_sm86> 下载（Clone 或 Download ZIP），把根目录的 `version.dll`、`dlssg_sm86.ini`，以及 `altnative/`、`config/presets/` 两个目录复制到 `mod/` 下。保持原目录结构，程序按固定文件名和相对路径识别，无需额外配置。

**方法三：指向别处**

程序默认读 exe 同级的 `mod/`，也接受任意位置——把文件放好后，改 `%APPDATA%\DLSSGManager\library.json` 里的 `ModSourcePath` 指向该目录即可。

## 下载器的网络约束

内置下载器只访问白名单内的 HTTPS 地址，并且在请求前逐个校验解析出的 IP：

- 仅允许 `github.com`、`codeload.github.com`、`raw.githubusercontent.com`、`api.github.com`
- 拒绝环回、内网、CGNAT、链路本地、多播与保留地址
- 重定向的每一跳都重新校验
- 响应体积有上限，压缩包条目不允许逃出目标目录

GitHub 的下载端点在部分网络上会偶发中断连接，因此下载失败会自动重试（最多 4 次，间隔递增）。
