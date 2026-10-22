// winmain.cpp
// Entry point for the Machine ID Bar taskbar overlay.
//
// Usage:
//   taskbar_overlay.exe                  — run the overlay (settings from registry)
//   taskbar_overlay.exe --install / -i   — install as startup app
//   taskbar_overlay.exe --uninstall / -u — remove from startup
//   taskbar_overlay.exe --help / -h      — show help
//   taskbar_overlay.exe --label "X" --fg "#ffffff"  — override settings
//
// Settings (label, color, font, position) are stored in the registry
// and can be changed via the tray icon right-click menu.
//
// Left or right-click the label to copy info to clipboard.
// Ctrl+click (left or right) to exit.

// Include winsock2.h BEFORE windows.h to avoid redefinition warnings
#include <winsock2.h>
#include <ws2tcpip.h>
#include "taskbar_overlay.h"
#include <shellapi.h>
#include <tlhelp32.h>
#include <string>
#include <cstdio>

#ifdef _MSC_VER
#pragma comment(lib, "ws2_32.lib")
#endif

// ============================================================================
// Globals
// ============================================================================

static HINSTANCE      g_hInstance    = nullptr;
static HWND           g_hHiddenWnd   = nullptr;
static NOTIFYICONDATA g_nid          = {};
static TaskbarOverlay* g_overlay     = nullptr;
static OverlaySettings g_settings;

// Font color presets
struct ColorPreset { const wchar_t* name; const wchar_t* hex; COLORREF rgb; };
static const ColorPreset COLOR_PRESETS[] = {
    {L"White",  L"#ffffff", RGB(255, 255, 255)},
    {L"Black",  L"#1a1a1a", RGB(26, 26, 26)},   // not pure black — avoids transparency clash
    {L"Blue",   L"#0078d4", RGB(0, 120, 212)},
    {L"Green",  L"#107c10", RGB(16, 124, 16)},
    {L"Red",    L"#d13438", RGB(209, 52, 56)},
    {L"Orange", L"#ca5010", RGB(202, 80, 16)},
    {L"Purple", L"#5c2d91", RGB(92, 45, 145)},
    {L"Yellow", L"#ffff00", RGB(255, 255, 0)},
    {L"Cyan",   L"#00ffff", RGB(0, 255, 255)},
    {L"Pink",   L"#e3008c", RGB(227, 0, 140)},
    {L"Lime",   L"#76b900", RGB(118, 185, 0)},
    {L"Gray",   L"#a0a0a0", RGB(160, 160, 160)},
};
static const int COLOR_PRESET_COUNT = sizeof(COLOR_PRESETS) / sizeof(COLOR_PRESETS[0]);

// Menu command IDs
#define IDM_EXIT              100
#define IDM_SHOW_HOSTNAME     101
#define IDM_SHOW_IP           102
#define IDM_EDIT_LABEL        103
#define IDM_POSITION_LEFT     104
#define IDM_POSITION_RIGHT    105
#define IDM_FONT_SIZE_SMALL   106
#define IDM_FONT_SIZE_MEDIUM  107
#define IDM_FONT_SIZE_LARGE   108
#define IDM_COLOR_BASE        200   // 200 + index into COLOR_PRESETS

// Tray icon callback
#define MY_WM_NOTIFYICON (WM_USER + 1005)
#define MY_WM_SHOW_MENU  (WM_USER + 1006)

// ============================================================================
// Logging
// ============================================================================

static void WinMainLog(const wchar_t* fmt, ...)
{
    FILE* f = nullptr;
    if (_wfopen_s(&f, L"taskbar_overlay_debug.log", L"a") != 0 || !f)
        return;
    va_list args;
    va_start(args, fmt);
    vfwprintf(f, fmt, args);
    va_end(args);
    fputwc(L'\n', f);
    fclose(f);
}

// ============================================================================
// CLI argument parsing
// ============================================================================

struct CliArgs
{
    std::wstring label = L"Secure PC";
    std::wstring bg    = L"#0078d4";
    std::wstring fg    = L"white";
};

static bool HasCliArgs(const std::wstring& cmdLine)
{
    if (cmdLine.empty())
        return false;
    // Check for --label/-l, --fg/-c, --bg
    int argc = 0;
    LPWSTR* argv = CommandLineToArgvW(cmdLine.c_str(), &argc);
    if (!argv) return false;
    bool found = false;
    for (int i = 1; i < argc; i++)
    {
        std::wstring a = argv[i];
        if (a == L"--label" || a == L"-l" || a == L"--fg" || a == L"-c" || a == L"--bg")
        {
            found = true;
            break;
        }
    }
    LocalFree(argv);
    return found;
}

static CliArgs ParseArgs(const std::wstring& cmdLine)
{
    CliArgs args;
    int argc = 0;
    LPWSTR* argv = CommandLineToArgvW(cmdLine.c_str(), &argc);
    if (!argv)
        return args;

    for (int i = 1; i < argc; i++)
    {
        std::wstring arg = argv[i];
        std::wstring key, value;
        size_t eq = arg.find(L'=');
        if (eq != std::wstring::npos)
        {
            key = arg.substr(0, eq);
            value = arg.substr(eq + 1);
        }
        else
        {
            key = arg;
            if (i + 1 < argc)
                value = argv[++i];
        }

        if (key == L"--label" || key == L"-l")
            args.label = value;
        else if (key == L"--bg")
            args.bg = value;
        else if (key == L"--fg" || key == L"-c")
            args.fg = value;
    }

    LocalFree(argv);
    return args;
}

static COLORREF ParseColor(const std::wstring& s, COLORREF defaultColor)
{
    if (s.empty())
        return defaultColor;

    if (s == L"white")  return RGB(255, 255, 255);
    if (s == L"black")  return RGB(0, 0, 0);
    if (s == L"red")    return RGB(209, 52, 56);
    if (s == L"blue")   return RGB(0, 120, 212);
    if (s == L"green")  return RGB(16, 124, 16);
    if (s == L"orange") return RGB(202, 80, 16);
    if (s == L"purple") return RGB(92, 45, 145);
    if (s == L"dark")   return RGB(32, 32, 32);
    if (s == L"teal")   return RGB(0, 133, 117);
    if (s == L"pink")   return RGB(227, 0, 140);

    if (s[0] == L'#' && s.length() == 7)
    {
        std::wstring hex = s.substr(1);
        int r = 0, g = 0, b = 0;
        swscanf_s(hex.c_str(), L"%02x%02x%02x", &r, &g, &b);
        return RGB(r, g, b);
    }

    return defaultColor;
}

// (ColorToHex removed — unused)

// ============================================================================
// Network info
// ============================================================================

static std::wstring GetLocalIP()
{
    WSADATA wsaData;
    if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0)
        return L"N/A";

    std::wstring ip = L"N/A";
    SOCKET s = socket(AF_INET, SOCK_DGRAM, 0);
    if (s != INVALID_SOCKET)
    {
        struct sockaddr_in target = {};
        target.sin_family = AF_INET;
        target.sin_port = htons(80);
        target.sin_addr.s_addr = inet_addr("8.8.8.8");

        if (connect(s, (struct sockaddr*)&target, sizeof(target)) == 0)
        {
            struct sockaddr_in local = {};
            int len = sizeof(local);
            if (getsockname(s, (struct sockaddr*)&local, &len) == 0)
            {
                char* ipStr = inet_ntoa(local.sin_addr);
                if (ipStr)
                    ip = std::wstring(ipStr, ipStr + strlen(ipStr));
            }
        }
        closesocket(s);
    }

    WSACleanup();
    return ip;
}

static std::wstring GetHostname()
{
    wchar_t hostname[MAX_COMPUTERNAME_LENGTH + 1] = {};
    DWORD size = MAX_COMPUTERNAME_LENGTH + 1;
    GetComputerNameW(hostname, &size);
    return std::wstring(hostname);
}

// Build display text respecting show/hide flags
static std::wstring GetDisplayText(const OverlaySettings& s)
{
    std::wstring text = s.label;
    if (s.show_hostname)
    {
        if (!text.empty()) text += L"  |  ";
        text += GetHostname();
    }
    if (s.show_ip)
    {
        if (!text.empty()) text += L"  |  ";
        text += GetLocalIP();
    }
    return text;
}

// ============================================================================
// Install — copy exe to %LOCALAPPDATA%\MachineIdBar and create startup shortcut
// ============================================================================

static std::wstring GetShortcutPath()
{
    wchar_t appData[MAX_PATH] = {};
    if (GetEnvironmentVariableW(L"APPDATA", appData, MAX_PATH) == 0)
        return L"";
    return std::wstring(appData) + L"\\Microsoft\\Windows\\Start Menu\\Programs\\Startup\\MachineIdBar.lnk";
}

static std::wstring GetInstallDir()
{
    wchar_t localAppData[MAX_PATH] = {};
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH) == 0)
        return L"";
    return std::wstring(localAppData) + L"\\MachineIdBar";
}

static bool DoInstall()
{
    wchar_t exePath[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, exePath, MAX_PATH);

    std::wstring installDir = GetInstallDir();
    std::wstring exeDst = installDir + L"\\taskbar_overlay.exe";
    std::wstring shortcutPath = GetShortcutPath();

    // Kill any running instance
    HANDLE hSnap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (hSnap != INVALID_HANDLE_VALUE)
    {
        PROCESSENTRY32W pe = {};
        pe.dwSize = sizeof(pe);
        if (Process32FirstW(hSnap, &pe))
        {
            do {
                if (_wcsicmp(pe.szExeFile, L"taskbar_overlay.exe") == 0 &&
                    pe.th32ProcessID != GetCurrentProcessId())
                {
                    HANDLE hProc = OpenProcess(PROCESS_TERMINATE | SYNCHRONIZE, FALSE, pe.th32ProcessID);
                    if (hProc)
                    {
                        TerminateProcess(hProc, 0);
                        WaitForSingleObject(hProc, 3000);  // wait for it to fully exit
                        CloseHandle(hProc);
                    }
                }
            } while (Process32NextW(hSnap, &pe));
        }
        CloseHandle(hSnap);
    }

    // Create install directory
    CreateDirectoryW(installDir.c_str(), nullptr);

    // Copy exe (retry in case the file is still being released)
    bool copied = false;
    for (int attempt = 0; attempt < 5; attempt++)
    {
        if (CopyFileW(exePath, exeDst.c_str(), FALSE))
        {
            copied = true;
            break;
        }
        Sleep(500);
    }
    if (!copied)
    {
        wprintf(L"ERROR: Could not copy to %ls\n", exeDst.c_str());
        wprintf(L"  Make sure no other instance is running.\n");
        return false;
    }

    // Create startup shortcut
    std::wstring psCmd = L"$ws = New-Object -ComObject WScript.Shell; "
                         L"$sc = $ws.CreateShortcut('" + shortcutPath + L"'); "
                         L"$sc.TargetPath = '" + exeDst + L"'; "
                         L"$sc.WorkingDirectory = '" + installDir + L"'; "
                         L"$sc.WindowStyle = 7; "
                         L"$sc.Description = 'Machine ID Bar'; "
                         L"$sc.Save()";

    HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    ShellExecuteW(nullptr, L"open", L"powershell.exe",
        (L"-NoProfile -Command \"" + psCmd + L"\"").c_str(),
        nullptr, SW_HIDE);
    if (hr == S_OK) CoUninitialize();

    // Register in Windows Settings -> Apps (Add/Remove Programs)
    std::wstring uninstallKey = L"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\MachineIdBar";
    HKEY hKey;
    if (RegCreateKeyExW(HKEY_CURRENT_USER, uninstallKey.c_str(), 0, nullptr, 0,
        KEY_SET_VALUE, nullptr, &hKey, nullptr) == ERROR_SUCCESS)
    {
        std::wstring displayName = L"Machine ID Bar";
        std::wstring uninstallStr = exeDst + L" --uninstall";
        std::wstring publisher = L"MachineIdBar";
        RegSetValueExW(hKey, L"DisplayName", 0, REG_SZ,
            (const BYTE*)displayName.c_str(), (DWORD)((displayName.size() + 1) * sizeof(wchar_t)));
        RegSetValueExW(hKey, L"UninstallString", 0, REG_SZ,
            (const BYTE*)uninstallStr.c_str(), (DWORD)((uninstallStr.size() + 1) * sizeof(wchar_t)));
        RegSetValueExW(hKey, L"DisplayIcon", 0, REG_SZ,
            (const BYTE*)exeDst.c_str(), (DWORD)((exeDst.size() + 1) * sizeof(wchar_t)));
        RegSetValueExW(hKey, L"Publisher", 0, REG_SZ,
            (const BYTE*)publisher.c_str(), (DWORD)((publisher.size() + 1) * sizeof(wchar_t)));
        DWORD noModify = 1, noRepair = 1;
        RegSetValueExW(hKey, L"NoModify", 0, REG_DWORD, (const BYTE*)&noModify, sizeof(DWORD));
        RegSetValueExW(hKey, L"NoRepair", 0, REG_DWORD, (const BYTE*)&noRepair, sizeof(DWORD));
        RegCloseKey(hKey);
    }

    wprintf(L"Machine ID Bar installed.\n");
    wprintf(L"  Installed:  %ls\n", exeDst.c_str());
    wprintf(L"  Startup:    %ls\n", shortcutPath.c_str());
    wprintf(L"  The app will auto-launch on every login.\n");
    wprintf(L"  Uninstall via Settings -> Apps or: taskbar_overlay.exe --uninstall\n");

    // Launch the installed copy
    ShellExecuteW(nullptr, L"open", exeDst.c_str(), nullptr, installDir.c_str(), SW_SHOWNORMAL);

    wprintf(L"  Launched.\n");
    return true;
}

// ============================================================================
// Uninstall — remove startup shortcut and install directory
// ============================================================================

static bool DoUninstall()
{
    // Kill running instance
    HANDLE hSnap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (hSnap != INVALID_HANDLE_VALUE)
    {
        PROCESSENTRY32W pe = {};
        pe.dwSize = sizeof(pe);
        if (Process32FirstW(hSnap, &pe))
        {
            do {
                if (_wcsicmp(pe.szExeFile, L"taskbar_overlay.exe") == 0 &&
                    pe.th32ProcessID != GetCurrentProcessId())
                {
                    HANDLE hProc = OpenProcess(PROCESS_TERMINATE, FALSE, pe.th32ProcessID);
                    if (hProc) { TerminateProcess(hProc, 0); CloseHandle(hProc); }
                }
            } while (Process32NextW(hSnap, &pe));
        }
        CloseHandle(hSnap);
    }

    std::wstring shortcutPath = GetShortcutPath();
    std::wstring installDir = GetInstallDir();

    // Remove shortcut
    DWORD attr = GetFileAttributesW(shortcutPath.c_str());
    if (attr != INVALID_FILE_ATTRIBUTES)
    {
        DeleteFileW(shortcutPath.c_str());
        wprintf(L"  Removed startup shortcut: %ls\n", shortcutPath.c_str());
    }
    else
    {
        wprintf(L"  No startup shortcut found.\n");
    }

    // Remove install directory
    attr = GetFileAttributesW(installDir.c_str());
    if (attr != INVALID_FILE_ATTRIBUTES && (attr & FILE_ATTRIBUTE_DIRECTORY))
    {
        std::wstring exeDst = installDir + L"\\taskbar_overlay.exe";
        DeleteFileW(exeDst.c_str());
        RemoveDirectoryW(installDir.c_str());
        wprintf(L"  Removed install directory: %ls\n", installDir.c_str());
    }
    else
    {
        wprintf(L"  No install directory found.\n");
    }

    // Remove the Add/Remove Programs registry entry
    RegDeleteKeyW(HKEY_CURRENT_USER,
        L"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\MachineIdBar");

    wprintf(L"Machine ID Bar uninstalled.\n");
    wprintf(L"  Registry settings (HKCU\\Software\\MachineIdBar) left intact.\n");
    wprintf(L"  To clear: reg delete HKCU\\Software\\MachineIdBar /f\n");
    return true;
}

// ============================================================================
// Help
// ============================================================================

static void ShowHelp()
{
    wprintf(L"Machine ID Bar — taskbar overlay for RDP session identification\n");
    wprintf(L"\n");
    wprintf(L"Usage:\n");
    wprintf(L"  taskbar_overlay.exe                   Run the overlay\n");
    wprintf(L"  taskbar_overlay.exe -i  / --install   Install as startup app\n");
    wprintf(L"  taskbar_overlay.exe -u  / --uninstall Remove from startup\n");
    wprintf(L"  taskbar_overlay.exe -h  / --help      Show this help\n");
    wprintf(L"  taskbar_overlay.exe -l  / --label \"X\" Override label\n");
    wprintf(L"  taskbar_overlay.exe -c  / --fg \"#fff\" Override font color\n");
    wprintf(L"\n");
    wprintf(L"Settings are stored in registry (HKCU\\Software\\MachineIdBar)\n");
    wprintf(L"and can be changed via the tray icon right-click menu.\n");
    wprintf(L"\n");
    wprintf(L"Left or right-click the label to copy info to clipboard.\n");
    wprintf(L"Ctrl+click to exit.\n");
}

// ============================================================================
// Label edit dialog — simple modal dialog with a text field
// ============================================================================

static std::wstring g_labelInput;

static INT_PTR CALLBACK LabelDlgProc(HWND hDlg, UINT msg, WPARAM wParam, LPARAM /*lParam*/)
{
    switch (msg)
    {
    case WM_INITDIALOG:
        SetDlgItemTextW(hDlg, 100, g_labelInput.c_str());
        SetFocus(GetDlgItem(hDlg, 100));
        return FALSE;  // we set focus manually

    case WM_COMMAND:
        if (LOWORD(wParam) == IDOK)
        {
            wchar_t buf[256] = {};
            GetDlgItemTextW(hDlg, 100, buf, 256);
            g_labelInput = buf;
            EndDialog(hDlg, IDOK);
            return TRUE;
        }
        if (LOWORD(wParam) == IDCANCEL)
        {
            EndDialog(hDlg, IDCANCEL);
            return TRUE;
        }
        break;

    case WM_CLOSE:
        EndDialog(hDlg, IDCANCEL);
        return TRUE;
    }
    return FALSE;
}

static bool ShowLabelDialog(std::wstring& currentLabel)
{
    g_labelInput = currentLabel;

    // Build a dialog template in memory:
    // Dialog (200x70 dlg units) with:
    //   - Edit field (ID 100) at (10,10,170,14)
    //   - OK button (IDOK) at (70,40,50,14)
    //   - Cancel button (IDCANCEL) at (130,40,50,14)
    static unsigned char dlgBuf[512];
    memset(dlgBuf, 0, sizeof(dlgBuf));
    unsigned char* p = dlgBuf;

    // DLGTEMPLATE
    DLGTEMPLATE* pDlg = (DLGTEMPLATE*)p;
    pDlg->style = WS_POPUP | WS_CAPTION | DS_MODALFRAME | DS_CENTER | DS_SETFONT;
    pDlg->dwExtendedStyle = 0;
    pDlg->cdit = 3;  // 3 controls: edit, OK, Cancel
    pDlg->cx = 200;
    pDlg->cy = 70;
    p += sizeof(DLGTEMPLATE);
    // menu (0), class (0), title
    *(WORD*)p = 0; p += 2;  // no menu
    *(WORD*)p = 0; p += 2;  // default class
    // title: "Edit Label"
    const wchar_t* title = L"Edit Label";
    size_t titleLen = wcslen(title) + 1;
    memcpy(p, title, titleLen * sizeof(wchar_t));
    p += titleLen * sizeof(wchar_t);
    // font (DS_SETFONT requires it): "Segoe UI" size 9
    *(WORD*)p = 9; p += 2;  // font size
    const wchar_t* font = L"Segoe UI";
    size_t fontLen = wcslen(font) + 1;
    memcpy(p, font, fontLen * sizeof(wchar_t));
    p += fontLen * sizeof(wchar_t);
    // Align to DWORD
    while ((uintptr_t)p % 4 != 0) p++;

    // Control 1: Edit field (ID 100)
    DLGITEMTEMPLATE* pItem = (DLGITEMTEMPLATE*)p;
    pItem->style = WS_CHILD | WS_VISIBLE | ES_AUTOHSCROLL | WS_BORDER | WS_TABSTOP;
    pItem->dwExtendedStyle = 0;
    pItem->x = 10; pItem->y = 10; pItem->cx = 170; pItem->cy = 14;
    pItem->id = 100;
    p += sizeof(DLGITEMTEMPLATE);
    *(WORD*)p = 0xFFFF; p += 2;  // class atom
    *(WORD*)p = 0x0081; p += 2;  // Edit class
    const wchar_t* editText = L"";
    size_t editTextLen = wcslen(editText) + 1;
    memcpy(p, editText, editTextLen * sizeof(wchar_t));
    p += editTextLen * sizeof(wchar_t);
    *(WORD*)p = 0; p += 2;  // creation data
    while ((uintptr_t)p % 4 != 0) p++;

    // Control 2: OK button (IDOK)
    pItem = (DLGITEMTEMPLATE*)p;
    pItem->style = WS_CHILD | WS_VISIBLE | BS_DEFPUSHBUTTON | WS_TABSTOP;
    pItem->dwExtendedStyle = 0;
    pItem->x = 70; pItem->y = 40; pItem->cx = 50; pItem->cy = 14;
    pItem->id = IDOK;
    p += sizeof(DLGITEMTEMPLATE);
    *(WORD*)p = 0xFFFF; p += 2;
    *(WORD*)p = 0x0080; p += 2;  // Button class
    const wchar_t* okText = L"OK";
    size_t okTextLen = wcslen(okText) + 1;
    memcpy(p, okText, okTextLen * sizeof(wchar_t));
    p += okTextLen * sizeof(wchar_t);
    *(WORD*)p = 0; p += 2;
    while ((uintptr_t)p % 4 != 0) p++;

    // Control 3: Cancel button (IDCANCEL)
    pItem = (DLGITEMTEMPLATE*)p;
    pItem->style = WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON | WS_TABSTOP;
    pItem->dwExtendedStyle = 0;
    pItem->x = 130; pItem->y = 40; pItem->cx = 50; pItem->cy = 14;
    pItem->id = IDCANCEL;
    p += sizeof(DLGITEMTEMPLATE);
    *(WORD*)p = 0xFFFF; p += 2;
    *(WORD*)p = 0x0080; p += 2;
    const wchar_t* cancelText = L"Cancel";
    size_t cancelTextLen = wcslen(cancelText) + 1;
    memcpy(p, cancelText, cancelTextLen * sizeof(wchar_t));
    p += cancelTextLen * sizeof(wchar_t);
    *(WORD*)p = 0; p += 2;

    INT_PTR result = DialogBoxIndirectParamW(g_hInstance,
        (LPCDLGTEMPLATE)dlgBuf, g_hHiddenWnd, LabelDlgProc, 0);

    if (result == IDOK)
    {
        currentLabel = g_labelInput;
        return true;
    }
    return false;
}

// ============================================================================
// Apply settings to the overlay
// ============================================================================

static void ApplySettingsToOverlay()
{
    OverlayStyle style;
    style.text_color = g_settings.text_color;
    style.bg_color   = RGB(0, 0, 0);  // transparent color key (always black)
    style.bold       = g_settings.bold;
    style.font_size  = g_settings.font_size;
    style.font_name  = L"Segoe UI";

    if (g_overlay)
    {
        g_overlay->SetStyle(style);
        g_overlay->SetText(GetDisplayText(g_settings));
    }
}

// Save settings to registry
static void SaveAndUpdate()
{
    SaveSettings(g_settings);
}

// ============================================================================
// Tray icon
// ============================================================================

static void ShowTrayMenu(HWND hwnd)
{
    HMENU hMenu = CreatePopupMenu();

    // Title header (disabled, not owner-drawn — simple and safe)
    AppendMenuW(hMenu, MF_STRING | MF_DISABLED, 999, L"  Machine ID Bar");
    AppendMenuW(hMenu, MF_SEPARATOR, 0, nullptr);

    // Show/Hide toggles
    AppendMenuW(hMenu, MF_STRING | (g_settings.show_hostname ? MF_CHECKED : 0),
                IDM_SHOW_HOSTNAME, L"Show hostname");
    AppendMenuW(hMenu, MF_STRING | (g_settings.show_ip ? MF_CHECKED : 0),
                IDM_SHOW_IP, L"Show IP address");

    // Separator
    AppendMenuW(hMenu, MF_SEPARATOR, 0, nullptr);

    // Edit label
    AppendMenuW(hMenu, MF_STRING, IDM_EDIT_LABEL, L"Edit label...");

    // Position submenu
    HMENU hPosMenu = CreatePopupMenu();
    AppendMenuW(hPosMenu,
        MF_STRING | (g_settings.position == TaskbarPosition::Left ? MF_CHECKED : 0),
        IDM_POSITION_LEFT, L"Left");
    AppendMenuW(hPosMenu,
        MF_STRING | (g_settings.position == TaskbarPosition::Right ? MF_CHECKED : 0),
        IDM_POSITION_RIGHT, L"Right");
    AppendMenuW(hMenu, MF_POPUP, (UINT_PTR)hPosMenu, L"Position");

    // Font color submenu — each item gets a colored square bitmap
    HMENU hColorMenu = CreatePopupMenu();
    for (int i = 0; i < COLOR_PRESET_COUNT; i++)
    {
        AppendMenuW(hColorMenu,
            MF_STRING | (g_settings.text_color == COLOR_PRESETS[i].rgb ? MF_CHECKED : 0),
            IDM_COLOR_BASE + i, COLOR_PRESETS[i].name);

        // Create a 12x12 colored bitmap for this item
        COLORREF c = COLOR_PRESETS[i].rgb;
        HDC hdcScreen = GetDC(nullptr);
        HDC hdcMem = CreateCompatibleDC(hdcScreen);
        HBITMAP hBmp = CreateCompatibleBitmap(hdcScreen, 12, 12);
        HGDIOBJ oldBmp = SelectObject(hdcMem, hBmp);
        HBRUSH hBrush = CreateSolidBrush(c);
        RECT rcBmp = {0, 0, 12, 12};
        FillRect(hdcMem, &rcBmp, hBrush);
        DeleteObject(hBrush);
        // Thin border
        HPEN hPen = CreatePen(PS_SOLID, 1, RGB(128, 128, 128));
        HGDIOBJ oldPen = SelectObject(hdcMem, hPen);
        HGDIOBJ oldBrush = SelectObject(hdcMem, GetStockObject(NULL_BRUSH));
        Rectangle(hdcMem, 0, 0, 12, 12);
        SelectObject(hdcMem, oldPen);
        SelectObject(hdcMem, oldBrush);
        SelectObject(hdcMem, oldBmp);
        DeleteObject(hPen);
        DeleteDC(hdcMem);
        ReleaseDC(nullptr, hdcScreen);

        SetMenuItemBitmaps(hColorMenu, i, MF_BYPOSITION, hBmp, hBmp);
    }
    AppendMenuW(hMenu, MF_POPUP, (UINT_PTR)hColorMenu, L"Font color");

    // Font size submenu
    HMENU hFontMenu = CreatePopupMenu();
    AppendMenuW(hFontMenu,
        MF_STRING | (g_settings.font_size == 9 ? MF_CHECKED : 0),
        IDM_FONT_SIZE_SMALL, L"Small (9pt)");
    AppendMenuW(hFontMenu,
        MF_STRING | (g_settings.font_size == 12 ? MF_CHECKED : 0),
        IDM_FONT_SIZE_MEDIUM, L"Medium (12pt)");
    AppendMenuW(hFontMenu,
        MF_STRING | (g_settings.font_size == 16 ? MF_CHECKED : 0),
        IDM_FONT_SIZE_LARGE, L"Large (16pt)");
    AppendMenuW(hMenu, MF_POPUP, (UINT_PTR)hFontMenu, L"Font size");

    // Separator + Exit
    AppendMenuW(hMenu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(hMenu, MF_STRING, IDM_EXIT, L"Exit");

    POINT pt;
    GetCursorPos(&pt);
    SetForegroundWindow(hwnd);
    TrackPopupMenu(hMenu, TPM_RIGHTBUTTON, pt.x, pt.y, 0, hwnd, nullptr);
    DestroyMenu(hMenu);
    DestroyMenu(hColorMenu);
    DestroyMenu(hPosMenu);
    DestroyMenu(hFontMenu);
}

static HWND CreateHiddenWindow(HINSTANCE hInstance)
{
    WNDCLASSEX wc = {};
    wc.cbSize        = sizeof(WNDCLASSEX);
    wc.lpfnWndProc   = [](HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) -> LRESULT
    {
        if (msg == MY_WM_NOTIFYICON)
        {
            if (lParam == WM_RBUTTONUP || lParam == WM_LBUTTONUP)
            {
                ShowTrayMenu(hwnd);
            }
            return 0;
        }

        // Right-click on the overlay label posts this to show the same menu
        if (msg == MY_WM_SHOW_MENU)
        {
            ShowTrayMenu(hwnd);
            return 0;
        }

        if (msg == WM_COMMAND)
        {
            WORD cmd = LOWORD(wParam);

            if (cmd == IDM_EXIT)
            {
                PostQuitMessage(0);
                return 0;
            }

            if (cmd == IDM_SHOW_HOSTNAME)
            {
                g_settings.show_hostname = !g_settings.show_hostname;
                SaveAndUpdate();
                ApplySettingsToOverlay();
                return 0;
            }

            if (cmd == IDM_SHOW_IP)
            {
                g_settings.show_ip = !g_settings.show_ip;
                SaveAndUpdate();
                ApplySettingsToOverlay();
                return 0;
            }

            if (cmd == IDM_EDIT_LABEL)
            {
                std::wstring newLabel = g_settings.label;
                if (ShowLabelDialog(newLabel))
                {
                    g_settings.label = newLabel;
                    SaveAndUpdate();
                    ApplySettingsToOverlay();
                }
                return 0;
            }

            if (cmd == IDM_POSITION_LEFT)
            {
                g_settings.position = TaskbarPosition::Left;
                SaveAndUpdate();
                if (g_overlay)
                    g_overlay->SetPosition(g_settings.position);
                return 0;
            }

            if (cmd == IDM_POSITION_RIGHT)
            {
                g_settings.position = TaskbarPosition::Right;
                SaveAndUpdate();
                if (g_overlay)
                    g_overlay->SetPosition(g_settings.position);
                return 0;
            }

            if (cmd >= IDM_COLOR_BASE && cmd < IDM_COLOR_BASE + COLOR_PRESET_COUNT)
            {
                int idx = cmd - IDM_COLOR_BASE;
                g_settings.text_color = COLOR_PRESETS[idx].rgb;
                SaveAndUpdate();
                ApplySettingsToOverlay();
                return 0;
            }

            if (cmd == IDM_FONT_SIZE_SMALL)
            {
                g_settings.font_size = 9;
                SaveAndUpdate();
                ApplySettingsToOverlay();
                return 0;
            }

            if (cmd == IDM_FONT_SIZE_MEDIUM)
            {
                g_settings.font_size = 12;
                SaveAndUpdate();
                ApplySettingsToOverlay();
                return 0;
            }

            if (cmd == IDM_FONT_SIZE_LARGE)
            {
                g_settings.font_size = 16;
                SaveAndUpdate();
                ApplySettingsToOverlay();
                return 0;
            }
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    };
    wc.hInstance     = hInstance;
    wc.lpszClassName = L"TaskbarOverlayHidden";

    RegisterClassEx(&wc);
    return CreateWindowEx(0, L"TaskbarOverlayHidden", L"", 0,
        0, 0, 0, 0, nullptr, nullptr, hInstance, nullptr);
}

static void AddTrayIcon(HWND hwnd)
{
    // Detect dark/light theme to pick the right icon color
    int iconId = 1;  // dark hash for light theme
    HKEY hKey;
    if (RegOpenKeyExW(HKEY_CURRENT_USER,
        L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
        0, KEY_QUERY_VALUE, &hKey) == ERROR_SUCCESS)
    {
        DWORD lightTheme = 1;
        DWORD sz = sizeof(DWORD);
        DWORD tp = REG_DWORD;
        if (RegQueryValueExW(hKey, L"AppsUseLightTheme", nullptr, &tp,
            (BYTE*)&lightTheme, &sz) == ERROR_SUCCESS)
        {
            if (lightTheme == 0)
                iconId = 2;  // dark theme -> light hash
        }
        RegCloseKey(hKey);
    }

    g_nid.cbSize           = sizeof(NOTIFYICONDATA);
    g_nid.hWnd             = hwnd;
    g_nid.uID              = 1;
    g_nid.uFlags           = NIF_ICON | NIF_MESSAGE | NIF_TIP;
    g_nid.uCallbackMessage = MY_WM_NOTIFYICON;
    g_nid.hIcon            = (HICON)LoadImageW(g_hInstance, MAKEINTRESOURCEW(iconId),
                            IMAGE_ICON, 16, 16, 0);
    if (!g_nid.hIcon)
        g_nid.hIcon        = LoadIcon(nullptr, IDI_APPLICATION);
    wcscpy_s(g_nid.szTip, L"Machine ID Bar");
    Shell_NotifyIcon(NIM_ADD, &g_nid);
}

static void RemoveTrayIcon()
{
    Shell_NotifyIcon(NIM_DELETE, &g_nid);
    if (g_nid.hIcon)
        DestroyIcon(g_nid.hIcon);
}

// ============================================================================
// WinMain
// ============================================================================

int WINAPI wWinMain(HINSTANCE hInstance, HINSTANCE, PWSTR pCmdLine, int)
{
    (void)pCmdLine;
    g_hInstance = hInstance;

    // Use GetCommandLineW() to get the FULL command line (including exe path),
    // because pCmdLine only contains the arguments without the program name.
    // CommandLineToArgvW needs the full command line to parse correctly.
    std::wstring cmdLine = GetCommandLineW();

    // Parse for special commands
    int argc = 0;
    LPWSTR* argv = CommandLineToArgvW(cmdLine.c_str(), &argc);

    // Check for --install/-i, --uninstall/-u, --help/-h
    if (argv)
    {
        for (int i = 1; i < argc; i++)
        {
            std::wstring arg = argv[i];
            if (arg == L"--install" || arg == L"-i")
            {
                LocalFree(argv);
                if (!AttachConsole(ATTACH_PARENT_PROCESS)) AllocConsole();
                freopen_s((FILE**)stdout, "CONOUT$", "w", stdout);
                DoInstall();
                return 0;
            }
            if (arg == L"--uninstall" || arg == L"-u")
            {
                LocalFree(argv);
                if (!AttachConsole(ATTACH_PARENT_PROCESS)) AllocConsole();
                freopen_s((FILE**)stdout, "CONOUT$", "w", stdout);
                DoUninstall();
                return 0;
            }
            if (arg == L"--help" || arg == L"-h")
            {
                LocalFree(argv);
                if (!AttachConsole(ATTACH_PARENT_PROCESS)) AllocConsole();
                freopen_s((FILE**)stdout, "CONOUT$", "w", stdout);
                ShowHelp();
                return 0;
            }
        }
        LocalFree(argv);
    }

    WinMainLog(L"=== wWinMain started ===");

    // Singleton: ensure only one instance runs
    HANDLE hMutex = CreateMutexW(nullptr, TRUE, L"MachineIdBar_Singleton");
    if (GetLastError() == ERROR_ALREADY_EXISTS)
    {
        // Another instance is already running — exit silently
        if (hMutex) CloseHandle(hMutex);
        return 0;
    }

    if (HasCliArgs(cmdLine))
    {
        CliArgs cliArgs = ParseArgs(cmdLine);
        g_settings.label = cliArgs.label;
        g_settings.text_color = ParseColor(cliArgs.fg, RGB(255, 255, 255));
        g_settings.show_hostname = true;
        g_settings.show_ip = true;
        g_settings.bold = true;
        WinMainLog(L"Using CLI args: label=%ls fg=%ls",
                   cliArgs.label.c_str(), cliArgs.fg.c_str());
        SaveSettings(g_settings);
    }
    else
    {
        if (LoadSettings(g_settings))
            WinMainLog(L"Loaded from registry: label=%ls", g_settings.label.c_str());
        else
            WinMainLog(L"No registry settings, using defaults");
    }

    // Create hidden window for tray icon
    g_hHiddenWnd = CreateHiddenWindow(hInstance);
    AddTrayIcon(g_hHiddenWnd);
    WinMainLog(L"Tray icon added");

    // Create and embed the taskbar overlay
    TaskbarOverlay overlay;
    g_overlay = &overlay;

    OverlayStyle style;
    style.text_color = g_settings.text_color;
    style.bg_color   = RGB(0, 0, 0);
    style.bold       = g_settings.bold;
    style.font_size  = g_settings.font_size;
    style.font_name  = L"Segoe UI";
    overlay.SetStyle(style);
    overlay.SetPosition(g_settings.position);

    if (!overlay.Create(hInstance))
    {
        WinMainLog(L"ERROR: overlay.Create() failed");
        MessageBox(nullptr, L"Failed to create taskbar overlay", L"Error", MB_ICONERROR);
        RemoveTrayIcon();
        if (hMutex) CloseHandle(hMutex);
        return 1;
    }
    WinMainLog(L"overlay.Create() OK");

    std::wstring text = GetDisplayText(g_settings);
    WinMainLog(L"Display text: %ls", text.c_str());
    overlay.SetText(text);

    // Message loop
    MSG msg;
    while (GetMessage(&msg, nullptr, 0, 0))
    {
        TranslateMessage(&msg);
        DispatchMessage(&msg);
    }

    WinMainLog(L"Message loop exited");
    g_overlay = nullptr;
    overlay.Destroy();
    RemoveTrayIcon();
    if (hMutex) CloseHandle(hMutex);
    return (int)msg.wParam;
}
