// KotorLauncher - portable borderless + widescreen launcher for Star Wars: KotOR (2003, swkotor.exe)
// Build: csc /platform:x86 /target:winexe  (see build.sh). Must be x86 to inject into the 32-bit game
// and to call the 32-bit fws_kotor_gff.dll HUD generator.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace KotorLauncher
{
    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetPrivateProfileStringW(string sec, string key, string def, StringBuilder ret, int size, string file);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool WritePrivateProfileStringW(string sec, string key, string val, string file);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct STARTUPINFO
        {
            public int cb; public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CreateProcessW(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit,
            uint flags, IntPtr env, string cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualAllocEx(IntPtr p, IntPtr addr, IntPtr size, uint type, uint prot);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualFreeEx(IntPtr p, IntPtr addr, IntPtr size, uint type);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr p, IntPtr addr, byte[] buf, IntPtr size, out IntPtr written);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr CreateRemoteThread(IntPtr p, IntPtr attr, IntPtr stack, IntPtr start, IntPtr param, uint flags, out int tid);
        [DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")] public static extern bool GetExitCodeThread(IntPtr h, out uint code);
        [DllImport("kernel32.dll")] public static extern uint ResumeThread(IntPtr h);
        [DllImport("kernel32.dll")] public static extern bool TerminateProcess(IntPtr h, uint code);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandleW(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] public static extern IntPtr GetProcAddress(IntPtr mod, string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool SetDllDirectoryW(string path);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        public static extern bool EnumDisplaySettingsA(string dev, int mode, ref DEVMODE dm);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public delegate int GenFn(string src, string dst, IntPtr param);
    }

    // UI language: "auto" follows Windows' display language (Turkish -> TR, anything else -> EN)
    static class L
    {
        public static bool Tr;
        public static string Pref = "auto";
        public static string T(string tr, string en) { return Tr ? tr : en; }
        public static void Init(string pref)
        {
            Pref = string.IsNullOrEmpty(pref) ? "auto" : pref.ToLowerInvariant();
            if (Pref == "tr") Tr = true;
            else if (Pref == "en") Tr = false;
            else Tr = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr";
        }
    }

    static class Ini
    {
        public static string Get(string file, string sec, string key, string def)
        {
            var sb = new StringBuilder(1024);
            Native.GetPrivateProfileStringW(sec, key, def, sb, sb.Capacity, file);
            return sb.ToString();
        }
        public static int GetInt(string file, string sec, string key, int def)
        {
            int v; return int.TryParse(Get(file, sec, key, def.ToString()).Trim(), out v) ? v : def;
        }
        public static void Set(string file, string sec, string key, string val)
        {
            if (!Native.WritePrivateProfileStringW(sec, key, val, file))
                throw new IOException(L.T("Yazılamadı: ", "Cannot write: ") + file + " (" + Marshal.GetLastWin32Error() + ")");
        }
    }

    class Settings
    {
        public string GamePath = "";
        public int Mode = 2;                 // 0 fullscreen, 1 windowed, 2 borderless
        public int Width, Height, Monitor;
        public bool EnableAllRes = true, MoviesFix = true, SubtitleFix = true, GenerateHud = true;
        public int MoviesWidth, MoviesHeight; // 0 = same as game
        public bool ClipCursor, DpiAware = true, BlockGamma, CloseAfterLaunch = true, SkipIntro;
        public string HudGeneratedFor = "", Language = "auto";

        public static string IniPath { get { return Path.Combine(Program.BaseDir, "kotorfix.ini"); } }

        public static Settings Load()
        {
            var s = new Settings(); string f = IniPath;
            s.GamePath = Ini.Get(f, "Launcher", "GamePath", "");
            s.CloseAfterLaunch = Ini.GetInt(f, "Launcher", "CloseAfterLaunch", 1) != 0;
            s.GenerateHud = Ini.GetInt(f, "Launcher", "GenerateHud", 1) != 0;
            s.HudGeneratedFor = Ini.Get(f, "Launcher", "HudGeneratedFor", "");
            s.Language = Ini.Get(f, "Launcher", "Language", "auto");
            s.Mode = Ini.GetInt(f, "KotorFix", "Mode", 2);
            s.Width = Ini.GetInt(f, "KotorFix", "Width", 0);
            s.Height = Ini.GetInt(f, "KotorFix", "Height", 0);
            s.Monitor = Ini.GetInt(f, "KotorFix", "Monitor", 0);
            s.EnableAllRes = Ini.GetInt(f, "KotorFix", "EnableAllResolutions", 1) != 0;
            s.MoviesFix = Ini.GetInt(f, "KotorFix", "MoviesResFix", 1) != 0;
            s.MoviesWidth = Ini.GetInt(f, "KotorFix", "MoviesWidth", 0);
            s.MoviesHeight = Ini.GetInt(f, "KotorFix", "MoviesHeight", 0);
            s.SubtitleFix = Ini.GetInt(f, "KotorFix", "SubtitleFix", 1) != 0;
            s.ClipCursor = Ini.GetInt(f, "KotorFix", "ClipCursor", 0) != 0;
            s.DpiAware = Ini.GetInt(f, "KotorFix", "DpiAware", 1) != 0;
            s.BlockGamma = Ini.GetInt(f, "KotorFix", "BlockGamma", 0) != 0;
            s.SkipIntro = Ini.GetInt(f, "KotorFix", "SkipIntro", 0) != 0;
            return s;
        }

        public void Save()
        {
            string f = IniPath;
            Func<bool, string> b = x => x ? "1" : "0";
            Ini.Set(f, "Launcher", "GamePath", GamePath);
            Ini.Set(f, "Launcher", "CloseAfterLaunch", b(CloseAfterLaunch));
            Ini.Set(f, "Launcher", "GenerateHud", b(GenerateHud));
            Ini.Set(f, "Launcher", "HudGeneratedFor", HudGeneratedFor);
            Ini.Set(f, "Launcher", "Language", Language);
            Ini.Set(f, "KotorFix", "Mode", Mode.ToString());
            Ini.Set(f, "KotorFix", "Width", Width.ToString());
            Ini.Set(f, "KotorFix", "Height", Height.ToString());
            Ini.Set(f, "KotorFix", "Monitor", Monitor.ToString());
            Ini.Set(f, "KotorFix", "EnableAllResolutions", b(EnableAllRes));
            Ini.Set(f, "KotorFix", "MoviesResFix", b(MoviesFix));
            Ini.Set(f, "KotorFix", "MoviesWidth", MoviesWidth.ToString());
            Ini.Set(f, "KotorFix", "MoviesHeight", MoviesHeight.ToString());
            Ini.Set(f, "KotorFix", "SubtitleFix", b(SubtitleFix));
            Ini.Set(f, "KotorFix", "ClipCursor", b(ClipCursor));
            Ini.Set(f, "KotorFix", "DpiAware", b(DpiAware));
            Ini.Set(f, "KotorFix", "BlockGamma", b(BlockGamma));
            Ini.Set(f, "KotorFix", "SkipIntro", b(SkipIntro));
        }
    }

    static class Core
    {
        public static readonly string[] HudArt = { "back", "comp0", "comp1", "load", "map", "pazaak", "store" };
        public const string HudGui = "mipc210x7.gui";

        public static bool IsGameDir(string d)
        {
            return !string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, "swkotor.exe"));
        }

        public static string FindGameDir(string saved)
        {
            if (IsGameDir(saved)) return saved;
            string d = Program.BaseDir;
            for (int i = 0; i < 3 && d != null; i++, d = Path.GetDirectoryName(d))
                if (IsGameDir(d)) return d;
            return "";
        }

        // Folder holding fws_kotor_gff.dll, mipc210x7.gui and template_*.jpg
        public static string FindResources()
        {
            var cands = new List<string> { Path.Combine(Program.BaseDir, "resources") };
            string rel = @"PluginCache\FWS_Plugins\Modules\SWKOTOR\Dependencies\Resources";
            foreach (var root in new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Flawless"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Flawless Widescreen"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Flawless Widescreen") })
                cands.Add(Path.Combine(root, rel));
            foreach (var c in cands)
                if (File.Exists(Path.Combine(c, "fws_kotor_gff.dll")) && File.Exists(Path.Combine(c, HudGui))) return c;
            return null;
        }

        // 16:9 HUD box inside the screen (pillar-boxed on ultrawide)
        public static void HudBox(int w, int h, out int x, out int y, out int bw, out int bh)
        {
            x = 0; y = 0; bw = w; bh = h;
            if (w * 9 > h * 16 + 9) { bw = (int)Math.Round(h * 16.0 / 9.0); x = (w - bw) / 2; }
        }

        public static string GenerateHud(string resDir, string outDir, int w, int h)
        {
            Directory.CreateDirectory(outDir);
            Native.SetDllDirectoryW(resDir);
            IntPtr mod = Native.LoadLibraryW(Path.Combine(resDir, "fws_kotor_gff.dll"));
            Native.SetDllDirectoryW(null);
            if (mod == IntPtr.Zero)
                throw new Exception(L.T("fws_kotor_gff.dll yüklenemedi (hata ", "Failed to load fws_kotor_gff.dll (error ") + Marshal.GetLastWin32Error() +
                    L.T("). 32-bit Visual C++ 2010 (msvcr100) gerekli.", "). The 32-bit Visual C++ 2010 runtime (msvcr100) is required."));
            var gui = (Native.GenFn)Marshal.GetDelegateForFunctionPointer(Native.GetProcAddress(mod, "GenerateGUI"), typeof(Native.GenFn));
            var img = (Native.GenFn)Marshal.GetDelegateForFunctionPointer(Native.GetProcAddress(mod, "GenerateImages"), typeof(Native.GenFn));

            int hx, hy, hw, hh; HudBox(w, h, out hx, out hy, out hw, out hh);
            // Same parameter block the FWS SWKOTOR plugin passes (16 ints + CheckByte)
            int[] p = { 1024, 768, hx, hy, hw, hh, 0, 450, 0, 100, 1, 99, 40, 40, 110, 110, 0 };
            IntPtr mem = Marshal.AllocHGlobal(p.Length * 4);
            IntPtr wh = Marshal.AllocHGlobal(8);
            try
            {
                Marshal.Copy(p, 0, mem, p.Length);
                Marshal.Copy(new[] { w, h }, 0, wh, 2);
                gui(Path.Combine(resDir, HudGui), Path.Combine(outDir, HudGui), mem);
                img(resDir.TrimEnd('\\'), outDir.TrimEnd('\\'), wh);
            }
            finally { Marshal.FreeHGlobal(mem); Marshal.FreeHGlobal(wh); }
            return string.Format(L.T("HUD {0}x{1} (kutu {2},{3} {4}x{5}) üretildi -> {6}", "HUD {0}x{1} (box {2},{3} {4}x{5}) generated -> {6}"), w, h, hx, hy, hw, hh, outDir);
        }

        public static int RemoveHud(string overrideDir)
        {
            int n = 0;
            var files = new List<string> { HudGui };
            foreach (var a in HudArt) files.Add("800x600" + a + ".tga");
            foreach (var f in files)
            {
                string p = Path.Combine(overrideDir, f);
                if (File.Exists(p)) { File.Delete(p); n++; }
            }
            return n;
        }

        public static void WriteGameIni(string gameDir, Settings s)
        {
            string f = Path.Combine(gameDir, "swkotor.ini");
            string fs = s.Mode == 0 ? "1" : "0";
            foreach (var sec in new[] { "Graphics Options", "Display Options" })
            {
                if (sec == "Display Options" && Ini.Get(f, sec, "Width", "") == "") continue; // only if present
                Ini.Set(f, sec, "Width", s.Width.ToString());
                Ini.Set(f, sec, "Height", s.Height.ToString());
                Ini.Set(f, sec, "FullScreen", fs);
            }
            Ini.Set(f, "Graphics Options", "AllowWindowedMode", "1");
        }

        // Start swkotor.exe suspended, load kotorfix.dll into it, then let it run.
        public static int LaunchWithFix(string gameDir, string dll)
        {
            string exe = Path.Combine(gameDir, "swkotor.exe");
            var si = new Native.STARTUPINFO(); si.cb = Marshal.SizeOf(si);
            Native.PROCESS_INFORMATION pi;
            const uint CREATE_SUSPENDED = 4;
            if (!Native.CreateProcessW(exe, new StringBuilder("\"" + exe + "\""), IntPtr.Zero, IntPtr.Zero, false,
                    CREATE_SUSPENDED, IntPtr.Zero, gameDir, ref si, out pi))
                throw new Exception(L.T("swkotor.exe başlatılamadı (hata ", "Could not start swkotor.exe (error ") + Marshal.GetLastWin32Error() + ")");
            bool ok = false;
            try
            {
                byte[] path = Encoding.Unicode.GetBytes(dll + "\0");
                IntPtr mem = Native.VirtualAllocEx(pi.hProcess, IntPtr.Zero, (IntPtr)path.Length, 0x3000, 0x04);
                if (mem == IntPtr.Zero) throw new Exception(L.T("VirtualAllocEx hata ", "VirtualAllocEx error ") + Marshal.GetLastWin32Error());
                IntPtr wr;
                if (!Native.WriteProcessMemory(pi.hProcess, mem, path, (IntPtr)path.Length, out wr))
                    throw new Exception(L.T("WriteProcessMemory hata ", "WriteProcessMemory error ") + Marshal.GetLastWin32Error());
                IntPtr ll = Native.GetProcAddress(Native.GetModuleHandleW("kernel32.dll"), "LoadLibraryW");
                int tid;
                IntPtr th = Native.CreateRemoteThread(pi.hProcess, IntPtr.Zero, IntPtr.Zero, ll, mem, 0, out tid);
                if (th == IntPtr.Zero) throw new Exception(L.T("CreateRemoteThread hata ", "CreateRemoteThread error ") + Marshal.GetLastWin32Error());
                uint wait = Native.WaitForSingleObject(th, 15000);
                uint code; Native.GetExitCodeThread(th, out code);
                Native.CloseHandle(th);
                if (wait != 0) throw new Exception(L.T("DLL yükleme zaman aşımı", "DLL load timed out"));
                Native.VirtualFreeEx(pi.hProcess, mem, IntPtr.Zero, 0x8000);
                if (code == 0) throw new Exception(L.T("kotorfix.dll oyuna yüklenemedi (LoadLibrary başarısız)", "kotorfix.dll could not be loaded into the game (LoadLibrary failed)"));
                Native.ResumeThread(pi.hThread);
                ok = true;
                return pi.dwProcessId;
            }
            finally
            {
                if (!ok) Native.TerminateProcess(pi.hProcess, 1);
                Native.CloseHandle(pi.hThread); Native.CloseHandle(pi.hProcess);
            }
        }

        public static void LaunchPlain(string gameDir)
        {
            var psi = new ProcessStartInfo(Path.Combine(gameDir, "swkotor.exe")) { WorkingDirectory = gameDir, UseShellExecute = false };
            Process.Start(psi);
        }

        public static bool IsRunning(string name) { return Process.GetProcessesByName(name).Length > 0; }

        // Full launch pipeline; log receives progress lines. Returns true on success.
        // Steam build: swkotor.exe is wrapped by SteamStub DRM (".bind" section). Started outside Steam it fails
        // with "Application load error 5:0000065434" unless steam_appid.txt is present and Steam is running.
        public static bool IsSteamExe(string gameDir)
        {
            try
            {
                byte[] d = File.ReadAllBytes(Path.Combine(gameDir, "swkotor.exe"));
                int pe = BitConverter.ToInt32(d, 0x3C);
                int n = BitConverter.ToUInt16(d, pe + 6), opt = BitConverter.ToUInt16(d, pe + 20);
                for (int i = 0; i < n; i++)
                    if (Encoding.ASCII.GetString(d, pe + 24 + opt + i * 40, 5) == ".bind") return true;
            }
            catch { }
            return File.Exists(Path.Combine(gameDir, "steam_api.dll"));
        }

        static void PrepareSteam(string gameDir, Action<string> log)
        {
            string appid = Path.Combine(gameDir, "steam_appid.txt");
            if (!File.Exists(appid) || File.ReadAllText(appid).Trim() != "32370")
            {
                File.WriteAllText(appid, "32370");
                log(L.T("Steam sürümü: steam_appid.txt (32370) oluşturuldu.", "Steam build: created steam_appid.txt (32370)."));
            }
            if (IsRunning("steam")) return;
            log(L.T("Steam açılıyor…", "Starting Steam…"));
            try { Process.Start("steam://open/main"); } catch { }
            for (int i = 0; i < 60 && !IsRunning("steam"); i++) System.Threading.Thread.Sleep(500);
            if (!IsRunning("steam"))
                throw new Exception(L.T("Steam sürümü için Steam'in açık olması gerekiyor. Steam'i açıp tekrar deneyin.",
                                        "The Steam version needs Steam running. Start Steam and try again."));
            System.Threading.Thread.Sleep(4000);   // let Steam finish logging in
        }

        public static bool Launch(Settings s, Action<string> log)
        {
            string game = s.GamePath;
            if (!IsGameDir(game)) throw new Exception(L.T("Oyun klasörü bulunamadı (swkotor.exe yok).", "Game folder not found (no swkotor.exe)."));
            if (IsRunning("swkotor")) throw new Exception(L.T("Oyun zaten çalışıyor.", "The game is already running."));
            if (IsRunning("FlawlessWidescreen"))
                throw new Exception(L.T("Flawless Widescreen açık. FWS oyuna bağlanıp aynı yamaları tekrar uygulamaya çalışır ", "Flawless Widescreen is running. FWS attaches to the game and re-applies the same patches, ") +
                    L.T("ve oyunu kilitler. Bu launcher FWS'in tüm düzeltmelerini zaten içeriyor; lütfen FWS'i kapatın.", "which freezes the game. This launcher already includes all FWS fixes; please close FWS."));

            s.Save();
            WriteGameIni(game, s);
            log(string.Format("swkotor.ini: {0}x{1}, FullScreen={2}", s.Width, s.Height, s.Mode == 0 ? 1 : 0));

            string ovr = Path.Combine(game, "Override");
            string key = s.Width + "x" + s.Height;
            if (s.GenerateHud)
            {
                bool missing = !File.Exists(Path.Combine(ovr, HudGui)) || !File.Exists(Path.Combine(ovr, "800x600back.tga"));
                if (missing || s.HudGeneratedFor != key)
                {
                    string res = FindResources();
                    if (res == null) log(L.T("UYARI: HUD kaynakları (resources klasörü) bulunamadı, HUD üretimi atlandı.", "WARNING: HUD resources (resources folder) not found, HUD generation skipped."));
                    else { log(GenerateHud(res, ovr, s.Width, s.Height)); s.HudGeneratedFor = key; s.Save(); }
                }
                else log(L.T("HUD dosyaları zaten ", "HUD files already prepared for ") + key + L.T(" için hazır.", "."));
            }

            if (IsSteamExe(game)) PrepareSteam(game, log);
            string dll = Path.Combine(Program.BaseDir, "kotorfix.dll");
            if (!File.Exists(dll)) throw new Exception(L.T("kotorfix.dll bulunamadı: ", "kotorfix.dll not found: ") + dll);
            int pid = LaunchWithFix(game, dll);
            log(L.T("Oyun başlatıldı (PID ", "Game started (PID ") + pid + L.T("), kotorfix.dll yüklendi. Ayrıntılar: kotorfix.log", "), kotorfix.dll loaded. Details: kotorfix.log"));
            return true;
        }
    }

}
