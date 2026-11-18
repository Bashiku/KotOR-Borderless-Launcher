/*
 * kotorfix.dll  -  KotOR Borderless + Widescreen fix
 *
 * Injected by KotorLauncher.exe into swkotor.exe while the game's main thread
 * is still suspended (before any game code runs). It:
 *   1) hooks the window / display-mode Win32 calls in swkotor.exe's IAT
 *      to give a real borderless window (no mode switch, no topmost, no frame)
 *   2) applies the same memory patches the Flawless Widescreen KOTOR plugin uses
 *      (enable all resolutions, intro-movie resolution, subtitle position)
 *
 * Settings are read from kotorfix.ini next to this DLL. Log: kotorfix.log.
 * Built freestanding (no CRT) for i686.
 */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

/* ------------------------------------------------------------------ */
/* freestanding helpers                                                */
/* ------------------------------------------------------------------ */
void *memset(void *d, int c, size_t n) { unsigned char *p = d; while (n--) *p++ = (unsigned char)c; return d; }
void *memcpy(void *d, const void *s, size_t n) { unsigned char *a = d; const unsigned char *b = s; while (n--) *a++ = *b++; return d; }

static int str_ieq(const char *a, const char *b)
{
    for (;; a++, b++) {
        char x = *a, y = *b;
        if (x >= 'A' && x <= 'Z') x += 32;
        if (y >= 'A' && y <= 'Z') y += 32;
        if (x != y) return 0;
        if (!x) return 1;
    }
}

/* ------------------------------------------------------------------ */
/* config + log                                                        */
/* ------------------------------------------------------------------ */
enum { MODE_FULLSCREEN = 0, MODE_WINDOWED = 1, MODE_BORDERLESS = 2 };

static struct {
    int mode;
    int width, height;
    int enableAllRes;
    int moviesFix, movieW, movieH;
    int subtitleFix;
    int clipCursor;
    int dpiAware;
    int blockGamma;
    int monitor;          /* 0 = primary, n = n-th monitor (1-based) */
    int skipIntro;
    int hudFix;
} cfg;


static WCHAR g_dir[MAX_PATH];
static HANDLE g_log = INVALID_HANDLE_VALUE;

static void logf(const char *fmt, ...)
{
    char buf[512];
    va_list ap;
    int n;
    DWORD w;
    if (g_log == INVALID_HANDLE_VALUE) return;
    va_start(ap, fmt);
    n = wvsprintfA(buf, fmt, ap);
    va_end(ap);
    if (n < 0) return;
    if (n > (int)sizeof(buf) - 3) n = sizeof(buf) - 3;
    buf[n++] = '\r'; buf[n++] = '\n';
    WriteFile(g_log, buf, n, &w, NULL);
}

static void build_path(WCHAR *out, const WCHAR *name)
{
    lstrcpyW(out, g_dir);
    lstrcatW(out, name);
}

static const WCHAR *p_ini(void)
{
    static WCHAR ini[MAX_PATH];
    build_path(ini, L"kotorfix.ini");
    return ini;
}

static void load_config(void)
{
    WCHAR ini[MAX_PATH];
    build_path(ini, L"kotorfix.ini");
#define RD(key, def) GetPrivateProfileIntW(L"KotorFix", key, def, ini)
    cfg.mode         = RD(L"Mode", MODE_BORDERLESS);
    cfg.width        = RD(L"Width", 0);
    cfg.height       = RD(L"Height", 0);
    cfg.enableAllRes = RD(L"EnableAllResolutions", 1);
    cfg.moviesFix    = RD(L"MoviesResFix", 1);
    cfg.movieW       = RD(L"MoviesWidth", 0);
    cfg.movieH       = RD(L"MoviesHeight", 0);
    cfg.subtitleFix  = RD(L"SubtitleFix", 1);
    cfg.clipCursor   = RD(L"ClipCursor", 0);
    cfg.dpiAware     = RD(L"DpiAware", 1);
    cfg.blockGamma   = RD(L"BlockGamma", 0);
    cfg.monitor      = RD(L"Monitor", 0);
    cfg.skipIntro    = RD(L"SkipIntro", 0);
    cfg.hudFix       = GetPrivateProfileIntW(L"Launcher", L"GenerateHud", 1, ini);
#undef RD
    if (cfg.width  <= 0) cfg.width  = GetSystemMetrics(SM_CXSCREEN);
    if (cfg.height <= 0) cfg.height = GetSystemMetrics(SM_CYSCREEN);
    if (cfg.movieW <= 0) cfg.movieW = cfg.width;
    if (cfg.movieH <= 0) cfg.movieH = cfg.height;
}

/* ------------------------------------------------------------------ */
/* game window tracking / placement                                    */
/* ------------------------------------------------------------------ */
#define MAX_WND 16
static HWND g_wnd[MAX_WND];
static volatile LONG g_wndCount;

static void track_wnd(HWND h)
{
    LONG i = InterlockedIncrement(&g_wndCount) - 1;
    if (i < MAX_WND) g_wnd[i] = h;
}

static BOOL is_game_wnd(HWND h)
{
    LONG i, n = g_wndCount;
    if (!h) return FALSE;
    if (n > MAX_WND) n = MAX_WND;
    for (i = 0; i < n; i++)
        if (g_wnd[i] == h) return TRUE;
    return FALSE;
}

typedef struct { int want, idx; RECT rc; } MonEnum;
static BOOL CALLBACK mon_cb(HMONITOR m, HDC dc, LPRECT r, LPARAM lp)
{
    MonEnum *e = (MonEnum *)lp;
    (void)m; (void)dc;
    if (++e->idx == e->want) { e->rc = *r; return FALSE; }
    return TRUE;
}

/* rect of the monitor the game should live on */
static RECT target_monitor(HWND h)
{
    MONITORINFO mi;
    RECT r;
    if (cfg.monitor > 0) {
        MonEnum e;
        e.want = cfg.monitor; e.idx = 0; SetRectEmpty(&e.rc);
        EnumDisplayMonitors(NULL, NULL, mon_cb, (LPARAM)&e);
        if (!IsRectEmpty(&e.rc)) return e.rc;
    }
    (void)h;
    mi.cbSize = sizeof(mi);
    {
        POINT pt = {0, 0};   /* (0,0) is always on the primary monitor */
        if (GetMonitorInfoW(MonitorFromPoint(pt, MONITOR_DEFAULTTOPRIMARY), &mi)) return mi.rcMonitor;
    }
    r.left = 0; r.top = 0;
    r.right = GetSystemMetrics(SM_CXSCREEN);
    r.bottom = GetSystemMetrics(SM_CYSCREEN);
    return r;
}

/* top-left position for a cx*cy borderless window: centered on monitor */
static void place(HWND h, int cx, int cy, int *x, int *y)
{
    RECT m = target_monitor(h);
    int mw = m.right - m.left, mh = m.bottom - m.top;
    *x = m.left + (cx < mw ? (mw - cx) / 2 : 0);
    *y = m.top  + (cy < mh ? (mh - cy) / 2 : 0);
}

#define KEEP_STYLE (WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS | WS_DISABLED | WS_MINIMIZE)
#define STRIP_EX   (WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE | WS_EX_DLGMODALFRAME | WS_EX_STATICEDGE | WS_EX_TOPMOST)

static DWORD borderless_style(DWORD s) { return (s & KEEP_STYLE) | WS_POPUP; }

/* ------------------------------------------------------------------ */
/* module ranges: hooks are process-wide (inline), but only calls made */
/* by the game itself (swkotor.exe / binkw32.dll) are modified          */
/* ------------------------------------------------------------------ */
typedef struct { ULONG_PTR lo, hi; } Range;
static Range g_exe, g_bink, g_bind;   /* g_bind = SteamStub ".bind" section (DRM loader) */
static HMODULE g_exeMod;
static volatile LONG g_patchPending;  /* SteamStub: .text is encrypted until the stub hands over */
static void apply_patches(HMODULE exe);

static Range mod_range(HMODULE m)
{
    Range r = {0, 0};
    if (m) {
        IMAGE_NT_HEADERS *nt = (IMAGE_NT_HEADERS *)((BYTE *)m + ((IMAGE_DOS_HEADER *)m)->e_lfanew);
        r.lo = (ULONG_PTR)m; r.hi = r.lo + nt->OptionalHeader.SizeOfImage;
    }
    return r;
}
static BOOL in_range(Range r, void *a) { return (ULONG_PTR)a >= r.lo && (ULONG_PTR)a < r.hi; }
/* called from the game's own (decrypted) code, not from the DRM stub */
static BOOL from_game_code(void *ret) { return in_range(g_exe, ret) && !in_range(g_bind, ret); }
static BOOL from_game(void *ret) { return from_game_code(ret) || in_range(g_bink, ret); }

static void run_deferred_patches(const char *why)
{
    if (g_patchPending && InterlockedExchange(&g_patchPending, 0)) {
        logf("game code running (%s) -> applying patches now", why);
        apply_patches(g_exeMod);
    }
}

#define CALLER __builtin_return_address(0)

/* ------------------------------------------------------------------ */
/* hooks                                                               */
/* ------------------------------------------------------------------ */
typedef HWND (WINAPI *pCreateWindowExA)(DWORD, LPCSTR, LPCSTR, DWORD, int, int, int, int, HWND, HMENU, HINSTANCE, LPVOID);
typedef LONG (WINAPI *pSetWindowLongA)(HWND, int, LONG);
typedef BOOL (WINAPI *pSetWindowPos)(HWND, HWND, int, int, int, int, UINT);
typedef BOOL (WINAPI *pMoveWindow)(HWND, int, int, int, int, BOOL);
typedef BOOL (WINAPI *pAdjustWindowRect)(LPRECT, DWORD, BOOL);
typedef LONG (WINAPI *pChangeDisplaySettingsA)(DEVMODEA *, DWORD);
typedef BOOL (WINAPI *pSetDeviceGammaRamp)(HDC, LPVOID);

static pCreateWindowExA        oCreateWindowExA;
static pSetWindowLongA         oSetWindowLongA;
static pSetWindowPos           oSetWindowPos;
static pMoveWindow             oMoveWindow;
static pAdjustWindowRect       oAdjustWindowRect;
static pChangeDisplaySettingsA oChangeDisplaySettingsA;
static pSetDeviceGammaRamp     oSetDeviceGammaRamp;

static LONG g_dpiDone;
static void ensure_dpi_aware(void)
{
    if (cfg.dpiAware && InterlockedExchange(&g_dpiDone, 1) == 0) {
        BOOL ok = SetProcessDPIAware();
        logf("SetProcessDPIAware -> %d", ok);
    }
}

static HWND WINAPI hkCreateWindowExA(DWORD ex, LPCSTR cls, LPCSTR name, DWORD style, int x, int y,
                                     int w, int h, HWND parent, HMENU menu, HINSTANCE inst, LPVOID param)
{
    HWND r;
    void *ret = CALLER;
    /* every non-child window the game creates: top-level windows and owned popups
       (the movie window is owned by the render window) */
    BOOL top;
    if (!from_game(ret)) return oCreateWindowExA(ex, cls, name, style, x, y, w, h, parent, menu, inst, param);
    run_deferred_patches("CreateWindowExA");
    top = !(style & WS_CHILD);
    ensure_dpi_aware();
    if (top && cfg.mode == MODE_BORDERLESS) {
        DWORD os = style, oex = ex;
        style = borderless_style(style);
        ex &= ~STRIP_EX;
        if (w == (int)CW_USEDEFAULT || w <= 0) w = cfg.width;
        if (h == (int)CW_USEDEFAULT || h <= 0) h = cfg.height;
        if (style & WS_VISIBLE) {
            /* immediately-visible helper windows (e.g. the 1600x1200 black cinematic backdrop) -> fit monitor */
            RECT m = target_monitor(NULL);
            if (w > m.right - m.left) w = m.right - m.left;
            if (h > m.bottom - m.top) h = m.bottom - m.top;
        }
        place(NULL, w, h, &x, &y);
        logf("CreateWindowExA top-level: style %08X->%08X ex %08X->%08X  %dx%d @ %d,%d",
             os, style, oex, ex, w, h, x, y);
    }
    r = oCreateWindowExA(ex, cls, name, style, x, y, w, h, parent, menu, inst, param);
    if (top && r) {
        track_wnd(r);
        logf("  -> hwnd %08X", (DWORD)(ULONG_PTR)r);
    }
    return r;
}

static LONG WINAPI hkSetWindowLongA(HWND h, int idx, LONG v)
{
    if (cfg.mode == MODE_BORDERLESS && is_game_wnd(h)) {
        if (idx == GWL_STYLE) {
            LONG nv = (LONG)borderless_style((DWORD)v);
            logf("SetWindowLong(STYLE) %08X -> %08X", v, nv);
            v = nv;
        } else if (idx == GWL_EXSTYLE) {
            v &= ~STRIP_EX;
        }
    }
    return oSetWindowLongA(h, idx, v);
}

static BOOL WINAPI hkSetWindowPos(HWND h, HWND after, int x, int y, int cx, int cy, UINT f)
{
    if (cfg.mode == MODE_BORDERLESS && is_game_wnd(h)) {
        int ox = x, oy = y;
        if (!(f & SWP_NOZORDER) && after == HWND_TOPMOST) after = HWND_NOTOPMOST;
        if (!(f & SWP_NOMOVE)) {
            int w = cx, hh = cy;
            if (f & SWP_NOSIZE) {
                RECT rc;
                GetWindowRect(h, &rc);
                w = rc.right - rc.left; hh = rc.bottom - rc.top;
            }
            place(h, w, hh, &x, &y);
        }
        f |= SWP_FRAMECHANGED;
        logf("SetWindowPos %d,%d %dx%d -> %d,%d flags %X", ox, oy, cx, cy, x, y, f);
    }
    return oSetWindowPos(h, after, x, y, cx, cy, f);
}

static BOOL WINAPI hkMoveWindow(HWND h, int x, int y, int w, int hh, BOOL rp)
{
    if (cfg.mode == MODE_BORDERLESS && is_game_wnd(h)) {
        place(h, w, hh, &x, &y);
        logf("MoveWindow -> %d,%d %dx%d", x, y, w, hh);
    }
    return oMoveWindow(h, x, y, w, hh, rp);
}

static BOOL WINAPI hkAdjustWindowRect(LPRECT r, DWORD style, BOOL menu)
{
    if (cfg.mode == MODE_BORDERLESS && from_game(CALLER)) return r != NULL;   /* no frame: client == window */
    return oAdjustWindowRect(r, style, menu);
}

static LONG WINAPI hkChangeDisplaySettingsA(DEVMODEA *dm, DWORD flags)
{
    if (cfg.mode == MODE_BORDERLESS && from_game(CALLER)) {
        if (dm) logf("ChangeDisplaySettings(%ux%u, flags %X) blocked", dm->dmPelsWidth, dm->dmPelsHeight, flags);
        else    logf("ChangeDisplaySettings(NULL) blocked");
        return DISP_CHANGE_SUCCESSFUL;
    }
    return oChangeDisplaySettingsA(dm, flags);
}

static BOOL WINAPI hkSetDeviceGammaRamp(HDC dc, LPVOID ramp)
{
    if (cfg.blockGamma && from_game(CALLER)) return TRUE;
    return oSetDeviceGammaRamp(dc, ramp);
}

/* The game hides + disables the Windows taskbar while movies play (FindWindow("Shell_TrayWnd")).
   Not needed in borderless (Windows hides the taskbar behind a full-screen foreground window) and
   harmful if the game crashes mid-movie, so ignore it. */
static BOOL is_taskbar(HWND h)
{
    char cls[32];
    return h && GetClassNameA(h, cls, sizeof(cls)) && str_ieq(cls, "Shell_TrayWnd");
}

typedef BOOL (WINAPI *pShowWindow)(HWND, int);
typedef BOOL (WINAPI *pEnableWindow)(HWND, BOOL);
static pShowWindow   oShowWindow;
static pEnableWindow oEnableWindow;

static BOOL WINAPI hkShowWindow(HWND h, int cmd)
{
    if (cfg.mode == MODE_BORDERLESS && from_game(CALLER) && is_taskbar(h)) { logf("ShowWindow(taskbar, %d) ignored", cmd); return TRUE; }
    return oShowWindow(h, cmd);
}

static BOOL WINAPI hkEnableWindow(HWND h, BOOL en)
{
    if (cfg.mode == MODE_BORDERLESS && from_game(CALLER) && is_taskbar(h)) { logf("EnableWindow(taskbar, %d) ignored", en); return FALSE; }
    return oEnableWindow(h, en);
}

/* CRT startup of the real game calls these first; under SteamStub that is the moment the
   .text section has been decrypted, so it is the earliest safe point to patch. */
typedef BOOL  (WINAPI *pGetVersionExA)(LPOSVERSIONINFOA);
typedef LPSTR (WINAPI *pGetCommandLineA)(void);
typedef void  (WINAPI *pGetStartupInfoA)(LPSTARTUPINFOA);
static pGetVersionExA   oGetVersionExA;
static pGetCommandLineA oGetCommandLineA;
static pGetStartupInfoA oGetStartupInfoA;
static BOOL WINAPI hkGetVersionExA(LPOSVERSIONINFOA v)
{ if (from_game_code(CALLER)) run_deferred_patches("GetVersionExA"); return oGetVersionExA(v); }
static LPSTR WINAPI hkGetCommandLineA(void)
{ if (from_game_code(CALLER)) run_deferred_patches("GetCommandLineA"); return oGetCommandLineA(); }
static void WINAPI hkGetStartupInfoA(LPSTARTUPINFOA si)
{ if (from_game_code(CALLER)) run_deferred_patches("GetStartupInfoA"); oGetStartupInfoA(si); }

static void *proc(const char *dll, const char *fn);

/* ------------------------------------------------------------------ */
/* inline (hot-patch) hooks for user32/gdi32: work no matter how the   */
/* exe resolves its imports (SteamStub)                                */
/* ------------------------------------------------------------------ */
static BYTE *g_tramp; static int g_trampUsed;

static int hook_inline(const char *dll, const char *fn, void *hook, void **orig)
{
    BYTE *t = (BYTE *)proc(dll, fn), *tr;
    DWORD old;
    int hot, fwd, len;
    if (!t) return 0;
    /* standard /hotpatch prologue: mov edi,edi ; push ebp ; mov ebp,esp */
    hot = t[0] == 0x8B && t[1] == 0xFF && t[2] == 0x55 && t[3] == 0x8B && t[4] == 0xEC;
    /* Win10+ forwarding stub: jmp dword ptr [abs]  (position independent -> copy as-is) */
    fwd = t[0] == 0xFF && t[1] == 0x25;
    if (!hot && !fwd) return 0;
    len = hot ? 5 : 6;
    if (!g_tramp) g_tramp = (BYTE *)VirtualAlloc(NULL, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!g_tramp || g_trampUsed + 16 > 4096) return 0;
    tr = g_tramp + g_trampUsed; g_trampUsed += 16;
    memcpy(tr, t, len);                                              /* original bytes */
    if (hot) { tr[5] = 0xE9; *(INT32 *)(tr + 6) = (INT32)((t + 5) - (tr + 10)); }  /* jmp back */
    if (!VirtualProtect(t, len, PAGE_EXECUTE_READWRITE, &old)) return 0;
    *orig = tr;
    *(INT32 *)(t + 1) = (INT32)((BYTE *)hook - (t + 5));
    t[0] = 0xE9;
    if (fwd) t[5] = 0x90;
    VirtualProtect(t, len, old, &old);
    FlushInstructionCache(GetCurrentProcess(), t, len);
    return 1;
}

/* ------------------------------------------------------------------ */
/* IAT patching of swkotor.exe                                         */
/* ------------------------------------------------------------------ */
static int hook_iat(HMODULE mod, const char *dll, const char *fn, void *hook, void **orig)
{
    BYTE *base = (BYTE *)mod;
    IMAGE_NT_HEADERS *nt = (IMAGE_NT_HEADERS *)(base + ((IMAGE_DOS_HEADER *)base)->e_lfanew);
    IMAGE_DATA_DIRECTORY *dd = &nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    IMAGE_IMPORT_DESCRIPTOR *imp;
    if (!dd->VirtualAddress) return 0;
    for (imp = (IMAGE_IMPORT_DESCRIPTOR *)(base + dd->VirtualAddress); imp->Name; imp++) {
        IMAGE_THUNK_DATA *names, *addrs;
        if (!str_ieq((const char *)(base + imp->Name), dll)) continue;
        names = (IMAGE_THUNK_DATA *)(base + (imp->OriginalFirstThunk ? imp->OriginalFirstThunk : imp->FirstThunk));
        addrs = (IMAGE_THUNK_DATA *)(base + imp->FirstThunk);
        for (; names->u1.AddressOfData; names++, addrs++) {
            IMAGE_IMPORT_BY_NAME *ibn;
            DWORD old;
            if (IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal)) continue;
            ibn = (IMAGE_IMPORT_BY_NAME *)(base + names->u1.AddressOfData);
            if (!str_ieq((const char *)ibn->Name, fn)) continue;
            if (!VirtualProtect(&addrs->u1.Function, sizeof(void *), PAGE_READWRITE, &old)) return 0;
            if (orig) *orig = (void *)addrs->u1.Function;
            addrs->u1.Function = (ULONG_PTR)hook;
            VirtualProtect(&addrs->u1.Function, sizeof(void *), old, &old);
            return 1;
        }
    }
    return 0;
}

/* fallback original pointer if the IAT entry was not found */
static void *proc(const char *dll, const char *fn)
{
    HMODULE m = GetModuleHandleA(dll);
    if (!m) m = LoadLibraryA(dll);
    return m ? (void *)GetProcAddress(m, fn) : NULL;
}

#define HOOK(dll, name) do { \
        if (hook_inline(dll, #name, (void *)hk##name, (void **)&o##name)) logf("hook %s: inline", #name); \
        else if (hook_iat(exe, dll, #name, (void *)hk##name, (void **)&o##name)) logf("hook %s: IAT", #name); \
        else { o##name = (p##name)proc(dll, #name); logf("hook %s FAILED", #name); } \
    } while (0)

#define TRIGGER(name) do { \
        logf("trigger %s: %d", #name, hook_iat(exe, "kernel32.dll", #name, (void *)hk##name, (void **)&o##name)); \
        if (!o##name) o##name = (p##name)proc("kernel32.dll", #name); \
    } while (0)

static void install_hooks(HMODULE exe)
{
    HOOK("user32.dll", CreateWindowExA);
    HOOK("user32.dll", SetWindowLongA);
    HOOK("user32.dll", SetWindowPos);
    HOOK("user32.dll", MoveWindow);
    HOOK("user32.dll", AdjustWindowRect);
    HOOK("user32.dll", ChangeDisplaySettingsA);
    HOOK("gdi32.dll",  SetDeviceGammaRamp);
    HOOK("user32.dll", ShowWindow);
    HOOK("user32.dll", EnableWindow);
    if (g_patchPending) {
        /* deferred-patch triggers via the exe IAT (kernel32 exports are forwarders, not hot-patchable) */
        TRIGGER(GetVersionExA);
        TRIGGER(GetCommandLineA);
        TRIGGER(GetStartupInfoA);
    }
}

/* ------------------------------------------------------------------ */
/* signature scanning + memory patches (same signatures as the FWS     */
/* SWKOTOR plugin, so it works on any exe build FWS supports)          */
/* ------------------------------------------------------------------ */
static int hexv(char c)
{
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    return -1;
}

/* pattern like "908B44????3D2003??0075" ; returns first match or NULL */
static BYTE *sigscan(HMODULE mod, const char *sig)
{
    BYTE pat[64], msk[64];
    int n = 0, i;
    BYTE *base = (BYTE *)mod, *p, *end;
    IMAGE_NT_HEADERS *nt = (IMAGE_NT_HEADERS *)(base + ((IMAGE_DOS_HEADER *)base)->e_lfanew);
    IMAGE_SECTION_HEADER *sec = IMAGE_FIRST_SECTION(nt);
    int s;
    while (sig[0] && sig[1] && n < 64) {
        if (sig[0] == '?') { pat[n] = 0; msk[n] = 0; }
        else { pat[n] = (BYTE)(hexv(sig[0]) * 16 + hexv(sig[1])); msk[n] = 1; }
        n++; sig += 2;
    }
    for (s = 0; s < nt->FileHeader.NumberOfSections; s++, sec++) {
        if (!(sec->Characteristics & IMAGE_SCN_MEM_EXECUTE)) continue;
        p = base + sec->VirtualAddress;
        end = p + (sec->Misc.VirtualSize ? sec->Misc.VirtualSize : sec->SizeOfRawData) - n;
        for (; p <= end; p++) {
            for (i = 0; i < n; i++)
                if (msk[i] && p[i] != pat[i]) break;
            if (i == n) return p;
        }
    }
    return NULL;
}

static int patch(void *addr, const void *bytes, int len)
{
    DWORD old;
    if (!VirtualProtect(addr, len, PAGE_EXECUTE_READWRITE, &old)) return 0;
    memcpy(addr, bytes, len);
    VirtualProtect(addr, len, old, &old);
    FlushInstructionCache(GetCurrentProcess(), addr, len);
    return 1;
}

static void write_float(float *where, float v)
{
    DWORD old;
    if (VirtualProtect(where, sizeof(float), PAGE_READWRITE, &old)) {
        *where = v;
        VirtualProtect(where, sizeof(float), old, &old);
    }
}

static void apply_patches(HMODULE exe)
{
    BYTE *a;

    if (cfg.skipIntro) {
        /* PlayIntroMovies(): if (this->moviesDone == 0) { queue leclogo, biologo, legal; play }
           jne skip  ->  nop ; jmp skip   (rel32 unchanged). Only the 3 startup logos, not cutscenes. */
        a = sigscan(exe, "8B86FC01000085C00F85????000068");
        if (a) {
            static const BYTE code[] = { 0x90, 0xE9 };
            logf("SkipIntro @ %08X: %s", (DWORD)(a + 8), patch(a + 8, code, 2) ? "ok" : "FAILED");
        } else logf("SkipIntro: signature NOT found");
    }

    if (cfg.hudFix) {
        /* HUD layout picker: width==1024 -> mipc210x7, 1280/1600 -> own files, ANY other width -> mipc28x6 (800x600).
           The launcher regenerates mipc210x7.gui for the real resolution, so always pick it:
           cmp eax,0x400  ->  cmp eax,eax ; nop*3   (same as the FWS "PreRegenerate" patch) */
        a = sigscan(exe, "89??????????0FBF????3D??????00B3??88");
        if (a && a[10] == 0x3D) {
            static const BYTE code[] = { 0x39, 0xC0, 0x90, 0x90, 0x90 };
            logf("HUD layout (force mipc210x7) @ %08X: %s", (DWORD)(a + 10), patch(a + 10, code, 5) ? "ok" : "FAILED");
        } else logf("HUD layout: signature NOT found");
    }

    if (cfg.enableAllRes) {
        /* bool IsSupportedResolution(w,h)  ->  mov eax,1 ; ret 8 */
        a = sigscan(exe, "908B44????3D2003??0075");
        if (a) {
            static const BYTE code[] = { 0xB8, 0x01, 0x00, 0x00, 0x00, 0xC2, 0x08, 0x00 };
            a += 1;
            logf("EnableAllResolutions @ %08X: %s", (DWORD)a, patch(a, code, sizeof(code)) ? "ok" : "FAILED");
        } else logf("EnableAllResolutions: signature NOT found");
    }

    if (cfg.moviesFix) {
        /* push 0 ; push 1 ; push 32 ; push 480 ; push 640 ; call SetupDisplay */
        a = sigscan(exe, "6A006A016A2068????000068????0000E8");
        if (a) {
            int h = cfg.movieH, w = cfg.movieW;
            int ok = patch(a + 7, &h, 4) && patch(a + 12, &w, 4);
            logf("MoviesResolution @ %08X -> %dx%d: %s", (DWORD)(a + 6), w, h, ok ? "ok" : "FAILED");
        } else logf("MoviesResolution: signature NOT found");
    }

    if (cfg.subtitleFix) {
        float mul = (float)(cfg.height - 250) / (float)cfg.width;
        BYTE *top = sigscan(exe, "8944????DB44????D80D????????E8????????0FBF");
        BYTE *bot = sigscan(exe, "89??????DB44????D80D????????E8????????0FBF????2B??89");
        if (top) { float *f = *(float **)(top + 10); write_float(f, -mul); logf("SubtitleFix top    @ %08X = -%d/1000", (DWORD)f, (int)(mul * 1000)); }
        else logf("SubtitleFix top: signature NOT found");
        if (bot) { float *f = *(float **)(bot + 10); write_float(f,  mul); logf("SubtitleFix bottom @ %08X = %d/1000", (DWORD)f, (int)(mul * 1000)); }
        else logf("SubtitleFix bottom: signature NOT found");
    }
}

/* ------------------------------------------------------------------ */
/* cursor confinement thread (borderless / windowed)                   */
/* ------------------------------------------------------------------ */
static DWORD WINAPI clip_thread(LPVOID p)
{
    BOOL clipped = FALSE;
    (void)p;
    for (;;) {
        HWND fg = GetForegroundWindow();
        if (is_game_wnd(fg) && !IsIconic(fg)) {
            RECT rc;
            POINT a, b;
            GetClientRect(fg, &rc);
            a.x = rc.left; a.y = rc.top; b.x = rc.right; b.y = rc.bottom;
            ClientToScreen(fg, &a); ClientToScreen(fg, &b);
            rc.left = a.x; rc.top = a.y; rc.right = b.x; rc.bottom = b.y;
            ClipCursor(&rc);
            clipped = TRUE;
        } else if (clipped) {
            ClipCursor(NULL);
            clipped = FALSE;
        }
        Sleep(100);
    }
    return 0;
}

/* ------------------------------------------------------------------ */
BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        HMODULE exe = GetModuleHandleW(NULL);
        WCHAR p[MAX_PATH];
        int i, n;
        DisableThreadLibraryCalls(inst);

        n = GetModuleFileNameW(inst, g_dir, MAX_PATH);
        for (i = n - 1; i >= 0 && g_dir[i] != L'\\' && g_dir[i] != L'/'; i--) {}
        g_dir[i + 1] = 0;
        build_path(p, L"kotorfix.log");
        g_log = CreateFileW(p, GENERIC_WRITE, FILE_SHARE_READ, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
        load_config();
        logf("kotorfix loaded. mode=%d res=%dx%d allres=%d movies=%d(%dx%d) subs=%d clip=%d dpi=%d gamma=%d mon=%d",
             cfg.mode, cfg.width, cfg.height, cfg.enableAllRes, cfg.moviesFix, cfg.movieW, cfg.movieH,
             cfg.subtitleFix, cfg.clipCursor, cfg.dpiAware, cfg.blockGamma, cfg.monitor);
        logf("skipIntro=%d hudFix=%d", cfg.skipIntro, cfg.hudFix);
        g_exeMod = exe;
        g_exe = mod_range(exe);
        g_bink = mod_range(GetModuleHandleA("binkw32.dll"));
        {
            IMAGE_NT_HEADERS *nt = (IMAGE_NT_HEADERS *)((BYTE *)exe + ((IMAGE_DOS_HEADER *)exe)->e_lfanew);
            IMAGE_SECTION_HEADER *sec = IMAGE_FIRST_SECTION(nt);
            for (i = 0; i < nt->FileHeader.NumberOfSections; i++, sec++)
                if (sec->Name[0] == '.' && sec->Name[1] == 'b' && sec->Name[2] == 'i' && sec->Name[3] == 'n' && sec->Name[4] == 'd') {
                    g_bind.lo = (ULONG_PTR)exe + sec->VirtualAddress;
                    g_bind.hi = g_bind.lo + sec->Misc.VirtualSize;
                    g_patchPending = 1;
                    logf("SteamStub DRM detected (.bind) -> patches deferred until game code runs");
                }
        }
        if (GetPrivateProfileIntW(L"KotorFix", L"DeferPatches", 0, p_ini())) { g_patchPending = 1; logf("DeferPatches=1 (test)"); }
        install_hooks(exe);
        if (!g_patchPending) apply_patches(exe);
        if (cfg.clipCursor && cfg.mode != MODE_FULLSCREEN) {
            HANDLE t = CreateThread(NULL, 0, clip_thread, NULL, 0, NULL);
            if (t) CloseHandle(t);
        }
        logf("init done");
    }
    return TRUE;
}
