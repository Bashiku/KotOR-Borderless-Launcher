# KotOR Borderless Launcher

A portable launcher for **Star Wars: Knights of the Old Republic** (2003) that runs the game in a true borderless fullscreen window at any resolution, with the widescreen fixes the game needs on modern monitors.

KotOR only supports exclusive fullscreen (display-mode switch, slow alt-tab) or a small framed window. External tools that force a window borderless tend to fight with the game. This launcher fixes the problem from the inside: it starts the game suspended, loads a small DLL before any game code runs, and hooks the window and display-mode calls the game makes. swkotor.exe is never modified on disk.

## Features

**Borderless window**
- True borderless fullscreen at any resolution (1080p, 1440p, 4K, ultrawide)
- No display-mode switching, not even for intro or cutscene videos
- Instant alt-tab; the window is not "always on top"
- The game no longer hides or disables the Windows taskbar while videos play
- Multi-monitor selection and optional cursor lock
- Windowed and original fullscreen modes are still available

**Widescreen fixes**
- Enable all resolutions (1920x1080 and above)
- Movie resolution fix (videos at your resolution instead of 640x480)
- Subtitle position fix for cutscenes
- HUD generated for your real resolution, plus full-resolution menu and loading backgrounds
- No restart needed: every patch is applied before the game's own code runs

**Extras**
- Skip the startup logo videos (story cutscenes are not affected)
- Disable DPI scaling for the game automatically
- Optionally block the game's desktop gamma changes
- One-click desktop shortcut that launches with saved settings
- English and Turkish UI (follows the Windows language, switchable)

## Compatibility

| Version | Status |
|---|---|
| Retail / disc 1.03 | Supported, tested |
| Steam | Supported. SteamStub DRM is detected; `steam_appid.txt` (32370) is written and Steam is started if needed. Patches are applied after the DRM unpacks the game. |
| Other builds | Patches locate code by byte signatures, not fixed addresses |

Requirements: Windows 10/11, .NET Framework 4.x (built into Windows), 32-bit Visual C++ 2010 runtime (for HUD generation).

## Installation

1. Download the latest release and extract it into your game folder **or any folder**. Keep `KotorLauncher.exe`, `kotorfix.dll` and `resources\` together.
   - In the game folder (next to `swkotor.exe`), the game is found automatically.
   - Anywhere else, click **Game folder...** on first start and select the folder that contains `swkotor.exe`.
2. Steam version: make sure Steam is running.
3. Run `KotorLauncher.exe`, choose **Borderless**, click **Match monitor**, then **PLAY**.

Don't run other tools that attach to the game (other widescreen patchers, Borderless Gaming) at the same time. They patch the same code again and can freeze the game.

Change the resolution in the launcher, not in the in-game options, so the HUD files are regenerated.

## How it works

```
KotorLauncher.exe (C#, x86)
  ├─ writes kotorfix.ini and the resolution into swkotor.ini
  ├─ generates the HUD layout + backgrounds into Override\ (if enabled)
  ├─ CreateProcess(swkotor.exe, CREATE_SUSPENDED)
  ├─ CreateRemoteThread(LoadLibraryW, "kotorfix.dll")
  └─ ResumeThread

kotorfix.dll (C, i686, no CRT)
  ├─ hooks user32/gdi32: CreateWindowExA, SetWindowLongA, SetWindowPos, MoveWindow,
  │   AdjustWindowRect, ChangeDisplaySettingsA, ShowWindow, EnableWindow, SetDeviceGammaRamp
  │   (inline hot-patch / forwarding-stub hooks, IAT fallback; only calls made by the game are modified)
  ├─ signature-scans swkotor.exe and patches in memory:
  │   enable all resolutions · movie resolution · subtitle position · HUD layout selection · intro skip
  ├─ SteamStub (.bind section): defers patching until the game's CRT startup runs
  └─ logs every hook and patch to kotorfix.log
```

What is written to disk: `kotorfix.ini`, `kotorfix.log`, the resolution/FullScreen values in `swkotor.ini`, `Override\mipc210x7.gui` and `Override\800x600*.tga` (HUD and backgrounds), and `steam_appid.txt` for the Steam version.

## Building from source

The DLL is built freestanding (no C runtime) with the LLVM tools from MSYS2's `clang64` environment. The launcher is built with the C# compiler that ships with .NET Framework 4.

Prerequisites:
- [MSYS2](https://www.msys2.org/) with the `clang64` toolchain (`clang`, `lld`, `llvm-dlltool`) in `C:\msys64\clang64`
- .NET Framework 4.x (`C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe`)

```sh
sh src/build.sh
```

This produces `kotorfix.dll` and `KotorLauncher.exe` in the project root. Import libraries for the DLL are generated from `src/kernel32.def` and `src/user32.def`.

## Project layout

```
KotorLauncher.exe      launcher (build output)
kotorfix.dll           injected fix DLL (build output)
resources/             HUD/background generator and templates
src/
  kotorfix.c           DLL: hooks, signature scanner, patches
  Core.cs              launcher logic: settings, HUD generation, injection, Steam handling
  Ui.cs                launcher UI (custom dark theme, TR/EN)
  kernel32.def         import definitions for the freestanding DLL
  user32.def
  kotor.ico            icon
  build.sh             build script
```

## Troubleshooting

- **Game freezes on startup:** close any other tool that attaches to the game.
- **Steam "Application load error 5:0000065434":** start Steam first. If `steam_appid.txt` could not be written, create it in the game folder with the content `32370`.
- **HUD looks like 800x600:** enable *Generate HUD + backgrounds*, keep `resources\` next to the launcher, and start the game from the launcher.
- **Antivirus warning:** DLL injection is the same technique overlays and widescreen tools use; some antivirus products flag it generically.
- Anything else: open `kotorfix.log` and include it in your issue.

## Credits

- **HaYDeN**: original KotOR widescreen research and the HUD/background generator in `resources/`.
- **BioWare & LucasArts**: Star Wars: Knights of the Old Republic. The launcher reads the game logo from your own installation; no game assets are included.
