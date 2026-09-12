# DLSSG 30-Series Manager

[简体中文](README.md) | **English**

[![Release](https://img.shields.io/github/v/release/BUNNY-19C/DLSSG-30s-manager?style=flat-square&label=download)](https://github.com/BUNNY-19C/DLSSG-30s-manager/releases/latest)
[![License](https://img.shields.io/github/license/BUNNY-19C/DLSSG-30s-manager?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078D4?style=flat-square)](#)
[![GPU](https://img.shields.io/badge/GPU-RTX%2030%20series%20(SM86)-76B900?style=flat-square)](#)
[![.NET](https://img.shields.io/badge/.NET-8-512BD4?style=flat-square)](#)

A graphical manager for [dlssg_for_sm86](https://github.com/sdli1995/dlssg_for_sm86). Deploy the mod per game and restore with one click, instead of copying DLLs into game folders by hand.

The mod is a DLL proxy: placing `version.dll` (or one of the alternative entry names) and `dlssg_sm86.ini` **beside the game's rendering executable** enables DLSS frame generation on RTX 30 series (SM86) cards.

> [!IMPORTANT]
> **Games with kernel-level anti-cheat cannot use this mod.** Such anti-cheat blocks and quarantines the proxy DLL before the game launches, so the mod cannot work, and a detection may put your account at risk. The manager detects this and blocks deployment — see [Anti-cheat](#anti-cheat-games-that-cannot-use-this-mod).

**[⬇ Download the latest release](https://github.com/BUNNY-19C/DLSSG-30s-manager/releases/latest)** — installer or portable exe; neither needs .NET installed.

**Highlights**

- Scans Steam libraries and any folder, locating games by the `nvngx_dlssg.dll` they ship, so unrelated programs never appear
- Per-game configuration (route, frame multiplier, sampling mode, log level), independent for each title
- **Anti-cheat is checked when a game is added**, and a kernel-level one blocks deployment outright
- Files displaced during deployment are backed up; restore only deletes files confirmed to belong to this project
- Batch deploy and restore
- Built-in downloader fetches the mod files on demand, so this repository carries no 75 MB of binaries

---

## Contents

- [Direct use (recommended)](#direct-use-recommended)
- [Running from source](#running-from-source)
- [Interface](#interface)
- [How it protects your files](#how-it-protects-your-files)
- [Anti-cheat: games that cannot use this mod](#anti-cheat-games-that-cannot-use-this-mod)
- [About GPUs](#about-gpus)
- [Updating the mod files](#updating-the-mod-files)
- [Adding a proxy DLL (a community entry name)](#adding-a-proxy-dll-a-community-entry-name)
- [Repository layout](#repository-layout)
- [Theme and localisation](#theme-and-localisation)
- [Building from source](#building-from-source)
- [License](#license)

---

## Direct use (recommended)

Download from [**Releases**](../../releases/latest) — two options:

| File | Description |
|---|---|
| `DLSSGManager-*-setup.exe` | **Installer.** Choose the install path, ships an uninstaller, adds a Start menu entry |
| `DLSSGManager.exe` | **Portable.** A single file; put it anywhere and run it |

Neither requires .NET or any other runtime.

The mod files (about 75 MB) are not bundled with the installer — the program fetches them. **The installer can download them during setup**, and the portable build offers to do so on first start.

### About the installer

The wizard asks for the install language first (Simplified Chinese or English), then the install location (default `C:\Program Files\DLSSG 30-Series Manager`, changeable).

It includes a **Mod files** task group; ticking it downloads the payload during setup (about 75 MB). Doing it at that point has a practical advantage: the installer is already elevated, so the files can be written into the program folder **even when installing to `Program Files`**.

Where the mod files end up:

| Situation | Location |
|---|---|
| Program folder is writable (the usual case) | `mod\` beside the program |
| Program folder is not writable (`Program Files` without elevation) | `%APPDATA%\DLSSGManager\mod` |

Keeping them beside the program makes the folder self-contained — copy it anywhere and it still works. Uninstalling asks separately whether to delete the mod files and game data; **a silent uninstall always keeps them**, so reinstalling does not mean re-downloading 75 MB.

### Using it

1. **Find your games** — use “Scan Steam library”, “Scan folder…” for a game drive, or “Add game…” for a single game folder.
   Games are located by the `nvngx_dlssg.dll` they ship: a game must include it to expose DLSS frame generation at all, so unrelated programs never appear. The Steam scan reads the registry and `libraryfolders.vdf`, covering custom library paths.
   **Anti-cheat is checked as you add a game**: a kernel-level one is reported immediately and deployment is blocked.
2. **Pick a game** — its own configuration appears on the right. The route is pre-filled from your GPU (RTX 3080 Ti → `SM86`).
3. **Click “Deploy to this game”** — done. The mod files are copied into the render directory.

To undo it, click “Restore”. Batch actions are at the bottom of the window: “Deploy all” and “Restore all”.

Releases ship a `SHA256SUMS.txt` if you want to verify the download:

```powershell
Get-FileHash DLSSGManager.exe -Algorithm SHA256
```

---

## Running from source

This repository **does not contain** the mod binaries (about 75 MB, and their licence does not permit redistribution — see [docs/mod-files.md](docs/mod-files.md)).

You need the .NET 8 SDK; see [Building from source](#building-from-source). The first run offers to fetch the mod files, exactly as the released build does.

---

## Interface

The toolbar shows the current mod file source and your GPU; its right-hand side holds the scan, download and “Add proxy DLL…” buttons (the last of these is covered under [adding a proxy DLL](#adding-a-proxy-dll-a-community-entry-name)). Top right has four controls:

- **Restart as administrator** — needed when the game lives under `C:\Program Files`, where writing requires elevation. Restarts through a UAC prompt.
- **Open data folder** — opens `%APPDATA%\DLSSGManager`, which holds the configuration and backups.
- **Theme** — switches the interface between dark and light, applied immediately and remembered.
- **Language** — switches the interface between Simplified Chinese and English, applied immediately and remembered.

Every field in the detail panel maps directly onto the mod's INI keys (see the author's [INI documentation](https://github.com/sdli1995/dlssg_for_sm86/blob/main/docs/NATIVE_INI.md)):

| Field | INI key | Notes |
|---|---|---|
| GPU route | `Router` | `SM86` for RTX 30 series, `SM75` for RTX 20 series |
| Kernel image | `KernelImage` | `PTX` (driver JIT) by default; `Cubin` requires an exact architecture match |
| Frame multiplier | `MaxGeneratedFrames` | 1/2/3 map to 2X/3X/4X. The game decides the actual multiplier |
| Log level | `Level` | Set to 2 when troubleshooting; logs land in `dlssg_sm86\logs` inside the game folder |
| Approximate sampling | `HardwareBilinear` | Off by default (exact output). Enabling it changes generated pixels; SM86 only |
| Diagnostic timings | `[Diagnostics]` | Records GPU timings; for profiling only, costs performance |

Configuration is stored **per game**; re-deploy to write changes.

---

## How it protects your files

Game folders often already contain other mods (ReShade's `dxgi.dll`, for instance), so removal has to distinguish "ours" from "theirs". Three checks are used together:

1. **Digital signature** — all five proxy DLLs carry the `CN=DLSSG Native Project` self-signed certificate; the manager reads it to confirm ownership.
2. **Hashes** — a SHA-256 is recorded for every file at deploy time and verified afterwards.
3. **Backups** — if a target name is occupied by a file that is not ours, it is copied to `%APPDATA%\DLSSGManager\restore\<game>\<timestamp>\` before being displaced, and put back on restore.

What that means in practice:

- **Anti-cheat games are blocked outright** — see the next section. Kernel-level anti-cheat makes the mod ineffective and carries account risk, so deployment is refused by default.
- **An occupied entry name is never overwritten**: the manager picks a free name from the available entries — the five bundled ones first (`version.dll` → `winmm.dll` → `dinput8.dll` → `winhttp.dll` → `dxgi.dll`), then any local entry you added. If all of them are taken it reports the conflict and leaves everything untouched.
- **Restore only deletes its own files**: a hash mismatch means the file is kept and reported, never deleted blindly.
- **A running game blocks the operation**: both deploy and restore check for processes inside the render directory first.
- **Existing manual installs can be adopted**, bringing them under management so restore works later.

---

## Anti-cheat: games that cannot use this mod

This is the most important limitation. **Games with kernel-level anti-cheat cannot use the mod**, and the manager blocks deployment to them.

The reason is direct: this mod is a DLL proxy, which is exactly the shape of thing kernel-level anti-cheat is built to catch. It **blocks and quarantines the proxy before the game even launches** — leaving a renamed copy such as `version.dll.3787982156` behind — with two consequences:

1. The mod does not work; the game keeps using its own rendering path, and may report errors.
2. **A detection may put your account at risk.**

Confirmed by testing: **Zenless Zone Zero** ships miHoYo's HoYoKProtect, which quarantined `version.dll` on sight, after which the game reported `The client component is running abnormally, please restart the client. Error Code:(0,11008,2195210578)`.

The scan covers the game folder **and three levels of parent directories**, and recognises:

| Anti-cheat | Indicators |
|---|---|
| miHoYo HoYoKProtect | `HoYoKProtect.sys`, `mhypbase.dll` |
| Tencent ACE | `ACE-*.sys`, `AntiCheatExpert\`, `SGuardSvc*.exe` |
| NetEase NEAC | `NeacSafe*.sys`, `NeacInterface.dll`, `NeacLoader.exe` |
| Easy Anti-Cheat | `EasyAntiCheat*.sys`, `start_protected_game.exe` |
| BattlEye | `BEClient*.dll`, `BEService*.exe` |
| nProtect GameGuard | `GameGuard.des`, `npgmup.des` |
| Riot Vanguard | `vgk.sys`, `vgc.exe` |
| XIGNCODE3 | `x3.xem`, `XignCode` |

Detection matches **both files and directories** (Tencent ACE typically ships as an `AntiCheatExpert\` folder), and walks up three parent levels, because anti-cheat often sits in the game root rather than the render directory. Any `.sys` kernel driver in the game folder is also reported — no shipping game needs one — which catches vendors not in the table above. Windows' own files such as `pagefile.sys` are not misreported.

Before scanning, the folder you picked is **resolved to the actual render directory**. That step is not optional: Overwatch keeps its anti-cheat in `E:\Overwatch\_retail_\`, so pointing at the outer `E:\Overwatch` and scanning upwards would miss `NeacSafe64.sys` entirely — verified in practice.

When a kernel-level anti-cheat is found:

- **A prompt appears as soon as the game is added** (several at once are summarised in a single dialog rather than one popup each);
- an orange warning bar appears at the top of the detail panel;
- **the “Deploy to this game” button is disabled**, and “Deploy all” lists such games as skipped;
- if you installed by hand earlier, “Restore” still works and can clean up the leftovers.

So even if the prompt is missed when adding a game, there is no opportunity to write files into a protected folder.

**Measured results on the development machine**, for reference:

| Game | Anti-cheat | Result |
|---|---|---|
| Zenless Zone Zero | miHoYo HoYoKProtect | ❌ blocked by anti-cheat |
| War Thunder | BattlEye | ❌ blocked by anti-cheat |
| Arknights: Endfield | Tencent ACE | ❌ blocked by anti-cheat |
| Overwatch | NetEase NEAC | ❌ blocked by anti-cheat |
| Monster Hunter Wilds | none | ⚠ crashed here — see below |

The first four are blocked by anti-cheat, and the manager recognises them and refuses to deploy.

**Monster Hunter Wilds is a different case**: it has no anti-cheat, and the mod loads successfully (its log shows four DLSSG requests taken over), but the game then crashes. Measured on one machine:

- Without the mod (after a Steam integrity check) → the game runs normally
- With the mod → crash at a fixed location (`MonsterHunterWilds.exe + 0xa4d69d0`)
- Two structurally different mod versions (0.1.0 and 0.2.4) → both crash at the same location
- Updating the NVIDIA DLSS runtime to the latest → no change
- Turning off the in-game DLSS frame generation toggle → no change

This does **not** mean the mod is universally incompatible with the game — an upstream user reported success on an RTX 3090. So the entry above reflects one machine only; **test it yourself**: restore first, confirm the game starts, then try deploying.

**If you already deployed**: click “Restore”. The manager recognises and removes the renamed copies anti-cheat leaves behind (only files confirmed by signature or hash to be ours), along with the INI. The status column reports this as “quarantined by anti-cheat” rather than a plain missing file.

**If you still want to deploy after accepting the risk**: the manager shows a second confirmation marked as not recommended. Note that on such games the mod will not work.

**Contributing results**: the [compatibility report](https://github.com/BUNNY-19C/DLSSG-30s-manager/issues/new?template=game_compatibility.yml) template is there for both working and failing cases — failures are just as useful to others.

---

## About GPUs

The mod's SM86 path was validated on real hardware by the author on an **RTX 3080 Ti**.

Worth knowing:

- **RTX 40/50 series do not need it**: Ada and Blackwell support DLSS frame generation natively. The manager says so when it detects one.
- **VRAM overhead** scales with output resolution: roughly 320–340 MiB at 1080p, 490–520 MiB at 1440p and 700–770 MiB at 4K. Insufficient headroom causes occasional stutter even when the average frame rate looks fine.
- **No 6X, no dynamic multiplier**, and no Reflex Warp support.
- **Antivirus may flag it**: DLL proxying plus hooking is the kind of behaviour heuristics watch for. All five DLLs carry a self-signed certificate (visible under file properties → Digital Signatures), but a self-signed certificate grants no default Windows trust and does not prevent alerts.
- **Enable frame generation in the game** after deploying; the mod does not turn it on by itself.

### The architecture comes from the hardware ID, not the GPU name

The manager reads the **PCI device ID** to decide between the SM86 and SM75 routes; the product name is only cross-checked.

That is not needless caution: the GPU name lives in the registry and can be rewritten by tools, while the device ID is bound to the physical chip. One machine encountered in testing reported `RTX 4090` (Ada) in the registry while hardware ID `2208` was actually an RTX 3080 Ti (Ampere) — judging by name would have produced the wrong conclusion, "a 40-series card does not need this mod", whereas the hardware ID gives the right one.

When the two disagree, the manager warns explicitly and suggests restoring the GPU name: a wrong name is not merely cosmetic, because drivers and games make functional decisions based on it.

---

## Updating the mod files

Click “Download / update mod files”. Sources are tried in order until one succeeds:

| Order | Source | Notes |
|---|---|---|
| 1 | GitHub archive (codeload) | One request, about 28 MB, fastest |
| 2 | GitHub API (zipball) | Same content, different entry point, for when codeload is throttled |
| 3 | GitHub raw files | Per-file, about 75 MB, a different network path |
| 4 | gh-proxy (China accelerator) | China-based node, measured fastest here (15 MB in under a second) |
| 5 | jsDelivr CDN mirror | A public CDN, for when GitHub is unreachable |
| 6 | ghfast (China accelerator) | China-based node, per-file download only |

**Clicking “Download / update mod files” opens a picker first**, where you can name a source or leave it on the default “Automatic” (try each in turn, falling back when one is unavailable). Choosing a specific source uses only that one — it will not quietly switch elsewhere, so the source reported in the log is trustworthy.

Each source is retried once before moving on, so one blocked or flaky endpoint causes a fallback rather than a failed update.

### Trust boundary for third-party mirrors

The last three entries are third-party forwarders, not the authority for this content, so the **certificate pin is enforced strictly** on them: only a signer matching the recorded value is accepted. On GitHub's own endpoints a mismatch is logged and accepted instead, so an upstream certificate rotation does not break updating.

That distinction is documented in the fetcher's source (`ModFetcher.Verify`), and it is why the source list is defined at compile time rather than in a runtime config file — **the source list is a trust boundary and should not be decided by configuration**.

### Downloads are verified

The payload is native DLLs destined for game folders, so the download path is treated as untrusted:

- HTTPS only, only the hosts in the table above, and every resolved IP must be public (loopback, private, CGNAT, link-local, multicast and reserved addresses are rejected). Redirects are re-validated at every hop.
- Response size is capped, and archive entries may not escape the destination folder.
- **The payload is signature-verified before anything is written**: every DLL must carry a valid Authenticode signature from the project's certificate — changing a single byte invalidates it — and the signer's certificate thumbprint must match the recorded value.

That last point matters for the mirror: a mirror is not the authority for the content, so its certificate must match exactly. On GitHub's own endpoints a mismatch is logged and accepted instead, so an upstream certificate rotation does not break updating.

You can also place the files yourself: put `version.dll`, `dlssg_sm86.ini` and `altnative\` into `mod\`; the manager recognises them by the folder structure. See [docs/mod-files.md](docs/mod-files.md).

## Adding a proxy DLL (a community entry name)

This project ships five entry names. On some games a protection module claims those names first — **Zenless Zone Zero** is one — so the community builds other entries, most commonly `d3d12.dll`: the game loads it dynamically when it initialises its DX12 backend, by which point the proxy gets a chance to load.

Click “**Add proxy DLL…**” in the toolbar and pick the file. The manager then:

- copies it into `mod\altnative\` **under its own file name** — the name *is* the entry name, the DLL name the game resolves, so it cannot be changed;
- reads the **signer** and writes it to the log (the certificate subject when the signature is intact, an explicit “unsigned” note otherwise). It does not vouch for the origin: the file is yours, so the origin is yours to confirm;
- lists it from then on in every game's “Proxy entry” picker, marked *(imported)*, deployable and removable with the normal buttons. Restore removes it by the SHA256 recorded at deploy time and touches nothing else.

Two limits: the five bundled names cannot be replaced (that would displace the official builds every signature check depends on), and a name outside the known set (`version` / `winmm` / `dinput8` / `winhttp` / `dxgi` / `d3d12`) triggers a warning that the game will most likely never load it — continuing is your call.

> **Why the name cannot be arbitrary**: an entry name is “a DLL name the game will load”. Get it right and the proxy enters the game process; get it wrong and deploying does nothing at all. It is also why the manager will **not** simply rename `version.dll` to `d3d12.dll`: each build exports only the system API surface of the name it stands in for, so the game's D3D12 imports would find no implementation and the game would not start.

---

## Repository layout

```
DLSSGManager/
├─ src/DLSSGManager/          ← source (WPF, .NET 8)
├─ test/Harness/              ← tests and diagnostic tooling
├─ installer/                 ← Inno Setup script (Simplified Chinese language file included; English uses the built-in Default.isl)
├─ docs/mod-files.md          ← why the repo has no mod binaries, and how to obtain them
├─ mod/                       ← mod file source (not committed; fetched on first run)
├─ publish/ dist/             ← build output (not committed)
```

Runtime data:

```
%APPDATA%\DLSSGManager\
├─ library.json               ← game list, per-game configuration, deployment records, UI language
├─ restore\                   ← backups of displaced files
├─ mod\                       ← mod files (only when the program folder is not writable)
└─ manager.log                ← operation log
```

---

## Theme and localisation

**Theme**: dark and light palettes, switched from the right end of the toolbar, applied immediately and remembered (`InterfaceTheme` in `library.json`). Both live under `src/DLSSGManager/Themes/` and the interface references them through `DynamicResource` — dynamic is required, because a static reference is resolved when the element is created and most of the window would stay in the old colours after a switch.

The light theme is not an inversion of the dark one; the values are chosen again, because a green or orange that reads well on a dark background is too light on white. A test checks that both themes define the same keys and validates the contrast of each text/background pair (7:1 for body text, 4.5:1 for secondary), and another check ensures no interface file or code path hard-codes a colour — a control that misses theming keeps the old palette after a switch, which is easy to overlook by eye.

**Localisation**: Simplified Chinese and English, switched from the right end of the toolbar, applied immediately and remembered. The installer wizard also asks for its language first.

Both tables live in `src/DLSSGManager/Strings.*.cs` and must define exactly the same keys — a test compares them, so a missing translation fails the build rather than surfacing a raw key in the interface. Placeholder consistency (`{0}`) is checked the same way, so arguments cannot end up in the wrong order in one language.

---

## Building from source

Requires the .NET 8 SDK.

```bash
git clone <repository-url>
cd DLSSGManager
dotnet build -c Release

# single-file, self-contained exe (target machine needs no .NET)
dotnet publish src/DLSSGManager/DLSSGManager.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none \
  -o publish
```

`publish/DLSSGManager.exe` is about 63 MB and can be copied anywhere.

For a quick local run, switch `--self-contained true` to `false`: the output is about 340 KB, but the target machine then needs the .NET 8 runtime.

### Building the installer

The installer is compiled with [Inno Setup 6](https://jrsoftware.org/isdl.php). The Simplified Chinese language file under `installer/languages/` ships with this repository because the Inno Setup distribution does not include it; English uses the built-in `Default.isl`.

```powershell
# after the publish step above:
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DAppVersion=1.4.0 installer\setup.iss
```

The result is `dist/DLSSGManager-<version>-setup.exe`. The design decisions in that script — why the mod files do not go into the install directory, and why uninstalling keeps data — are documented in its header comments.

Releases are built automatically by GitHub Actions: pushing a `v*` tag (for example `git tag v1.4.0 && git push origin v1.4.0`) builds, tests, compiles the installer and creates a Release with both executables and `SHA256SUMS.txt`. The workflow can also be triggered manually from the Actions tab.

Testing (358 cases covering deployment and restore, backup protection, anti-cheat detection and blocking, entry-name management, local proxy import, path validation, INI rendering, persistence, download URL policy, signature verification, source selection, theming and localisation):

```bash
cd test/Harness
dotnet build -c Release

# full suite; cases needing the mod files are skipped with a hint when they are absent
./bin/Release/net8.0-windows/Harness.exe

# fetch the mod files into the project's mod folder
./bin/Release/net8.0-windows/Harness.exe --fetch

# list the configured download sources and check the address policy
./bin/Release/net8.0-windows/Harness.exe --sources

# scan this machine's games and report anti-cheat, without modifying anything
./bin/Release/net8.0-windows/Harness.exe --scan "D:\Games\SomeGame"
```

Tests redirect the data folder to a temporary location (via the `DLSSGMANAGER_HOME` environment variable), so they never touch your real `library.json` or `manager.log`.

---

## License

The source code in this repository (the manager's C# implementation) is released under the [MIT License](LICENSE).

**That licence does not cover any file published by dlssg_for_sm86.** Those belong to the upstream project; this manager downloads them from their published location on demand and copies them into game folders. It does not redistribute or relicense them. See [docs/mod-files.md](docs/mod-files.md).

Please read the upstream repository's notes before using the mod, particularly regarding antivirus false positives, VRAM usage and anti-cheat restrictions.

---

## Contributing

Game compatibility results, anti-cheat signatures and other improvements are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md).

This program is a **deployment tool** for dlssg_for_sm86; it does not contain or modify the mod itself. Issues with frame generation itself (image quality, performance, per-game compatibility) belong with the [mod author](https://github.com/sdli1995/dlssg_for_sm86/issues).
