# 附加入口：d3d12.dll

这个目录存放管理器分发的**附加入口**。管理器在「下载 / 更新 Mod 文件」时（安装器的「Mod 文件」任务调用的是同一个下载器）会自动获取它，落到 `mod\altnative\d3d12.dll`，之后它就和自带的五个入口一样出现在每款游戏的「代理入口」下拉里（标注"附加入口"），可部署、可一键恢复。

## 这是什么

上游 [dlssg_for_sm86](https://github.com/sdli1995/dlssg_for_sm86) 只发布 5 个入口名（`version` / `winmm` / `dinput8` / `winhttp` / `dxgi`）。有些游戏的保护模块会抢先占用这些名字——**绝区零**的 HoYoKProtect 就是先例，实测会把 `version.dll` 改名隔离——社区因此编译了其他入口。`d3d12.dll` 是其中最常见的：游戏初始化 DX12 后端时才动态加载它，那时代理才有机会被加载。

## 来源与成分

- 代码主体是上游 **archive 代**（旧构建，10,522,624 字节）的代理 DLL；
- 有人在其上**追加**了两个节区（`.d12code` 17 KB / `.d12data` 2 KB）和一张 d3d12 导出表，使其导出系统 `d3d12.dll` 的 18 个入口，原有 9 个节区逐字节未改；
- 随后用自签证书 `CN=Local DLL Signing` 重新签名（文件属性 → 数字签名 可查）。

本仓库中这份副本的 SHA-256：

```
65E6F912F5D485DC56BC6B48430DF046FF06D38B8A69595B42E316E9644F7C2B
```

**这个哈希固定在管理器源码里**（`ModFetcher.Extras`）。管理器无法为这种文件校验签名——它不是本项目构建的——所以哈希是唯一的完整性保证：字节不符即丢弃，换哪个下载源都一样。

## 授权说明（重要）

- 本仓库的 `LICENSE`（MIT）**不覆盖**这个文件，也不覆盖这里可能出现的其他二进制。
- 该文件包含上游项目的编译产物。上游在其 `THIRD_PARTY_NOTICES.txt` 中声明：NVIDIA 运行时与提取/重编译的内核资源"不属于其授权范围"（not relicensed by LICENSE.md）。
- 这里按"随管理器一起分发、方便用户开箱即用"的原样收录，**不声明任何授权**。若权利人提出异议，会立即移除；管理器的功能不受影响——届时用「添加代理 DLL…」按钮手动添加同一个文件即可。

## 如果不想下载它

删掉 `mod\altnative\d3d12.dll` 即可（或在 `--fetch` 之前删）。下载器发现目标已存在且与仓库版本不同时会**保留你的文件**，所以放一个你自己的构建进去，更新时不会被覆盖。

## 如何更新这个文件

1. 用新构建替换 `extra-proxies/d3d12.dll`；
2. 计算哈希：`sha256sum extra-proxies/d3d12.dll`（PowerShell：`Get-FileHash extra-proxies\d3d12.dll`）；
3. 更新 `src/DLSSGManager/ModFetcher.cs` 中 `Extras` 的哈希串；
4. 把 `SelfRef` 常量指向本次发布的 tag（tag 不可变，比分支安全）；
5. 同步更新本文件的哈希与说明，提交、打 tag。
