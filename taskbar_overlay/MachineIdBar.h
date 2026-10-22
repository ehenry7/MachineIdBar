// MachineIdBar.h
// Taskbar overlay window — extracted from TrafficMonitor's taskbar embedding code.
// Pure Win32 API, no MFC dependency.
//
// Features (matching MachineIdBar Python implementation):
//   - Custom label, background color, text color via command-line args
//   - Display format: "label  |  hostname  |  IP"
//   - Bold font, DPI-aware
//   - Left or right-click to copy all info to clipboard
//   - Ctrl+click (left or right) to exit
//   - Drag to reposition
//   - Resolution change detection (repositions on RDP reconnect)
//   - Force-visible re-assertion every second
//
// Techniques extracted from TrafficMonitor (trafficmonitor-ref):
//   - Finding Shell_TrayWnd (and Shell_SecondaryTrayWnd for multi-monitor)
//   - Detecting Windows 11 taskbar vs classic (Win7/8/10) vs Wine
//   - Embedding a child window onto the taskbar via SetParent
//   - Classic: finding ReBarWindow32/WorkerW + MSTaskSwWClass/MSTaskListWClass,
//     resizing the minimized-window area to make room
//   - Win11: parenting to Shell_TrayWnd directly, positioning near TrayNotifyWnd
//   - Layered window with color-key transparency
//   - 1-second timer for content updates
//   - TaskbarCreated registered message (re-embed after Explorer restart)

#pragma once
#include <windows.h>
#include <string>

// ============================================================================
// Settings — persisted in registry under HKCU\Software\MachineIdBar
// ============================================================================

// Position on taskbar: left side or right side (near notification area)
enum class TaskbarPosition { Left, Right };

struct OverlaySettings
{
    std::wstring   label         = L"Secure PC";
    COLORREF       text_color    = RGB(255, 255, 255);  // white (font color)
    bool           show_hostname = true;
    bool           show_ip       = true;
    bool           bold          = true;
    TaskbarPosition position     = TaskbarPosition::Right;
    int            font_size     = 9;   // points (Small=9, Medium=12, Large=16)
};

// Registry key for settings
#define REG_KEY_MACHINE_ID_BAR  L"Software\\MachineIdBar"

// Save settings to registry
inline void SaveSettings(const OverlaySettings& s)
{
    HKEY hKey;
    if (RegCreateKeyExW(HKEY_CURRENT_USER, REG_KEY_MACHINE_ID_BAR, 0, nullptr, 0,
        KEY_SET_VALUE, nullptr, &hKey, nullptr) == ERROR_SUCCESS)
    {
        RegSetValueExW(hKey, L"label", 0, REG_SZ,
            (const BYTE*)s.label.c_str(), (DWORD)((s.label.size() + 1) * sizeof(wchar_t)));
        DWORD fg = s.text_color;
        RegSetValueExW(hKey, L"text_color", 0, REG_DWORD, (const BYTE*)&fg, sizeof(DWORD));
        DWORD sh = s.show_hostname ? 1 : 0;
        DWORD si = s.show_ip ? 1 : 0;
        DWORD bold = s.bold ? 1 : 0;
        DWORD fsize = s.font_size;
        RegSetValueExW(hKey, L"show_hostname", 0, REG_DWORD, (const BYTE*)&sh, sizeof(DWORD));
        RegSetValueExW(hKey, L"show_ip", 0, REG_DWORD, (const BYTE*)&si, sizeof(DWORD));
        RegSetValueExW(hKey, L"bold", 0, REG_DWORD, (const BYTE*)&bold, sizeof(DWORD));
        RegSetValueExW(hKey, L"font_size", 0, REG_DWORD, (const BYTE*)&fsize, sizeof(DWORD));
        DWORD pos = (s.position == TaskbarPosition::Right) ? 1 : 0;
        RegSetValueExW(hKey, L"position", 0, REG_DWORD, (const BYTE*)&pos, sizeof(DWORD));
        RegCloseKey(hKey);
    }
}

// Load settings from registry (returns true if any setting was found)
inline bool LoadSettings(OverlaySettings& s)
{
    HKEY hKey;
    bool found = false;
    if (RegOpenKeyExW(HKEY_CURRENT_USER, REG_KEY_MACHINE_ID_BAR, 0,
        KEY_QUERY_VALUE, &hKey) == ERROR_SUCCESS)
    {
        wchar_t buf[256] = {};
        DWORD bufSize = sizeof(buf);
        DWORD type = REG_SZ;
        if (RegQueryValueExW(hKey, L"label", nullptr, &type, (BYTE*)buf, &bufSize) == ERROR_SUCCESS)
        {
            s.label = buf;
            found = true;
        }

        DWORD val = 0;
        DWORD valSize = sizeof(DWORD);
        DWORD dtype = REG_DWORD;
        if (RegQueryValueExW(hKey, L"text_color", nullptr, &dtype, (BYTE*)&val, &valSize) == ERROR_SUCCESS)
            s.text_color = val;
        if (RegQueryValueExW(hKey, L"show_hostname", nullptr, &dtype, (BYTE*)&val, &valSize) == ERROR_SUCCESS)
            s.show_hostname = (val != 0);
        if (RegQueryValueExW(hKey, L"show_ip", nullptr, &dtype, (BYTE*)&val, &valSize) == ERROR_SUCCESS)
            s.show_ip = (val != 0);
        if (RegQueryValueExW(hKey, L"bold", nullptr, &dtype, (BYTE*)&val, &valSize) == ERROR_SUCCESS)
            s.bold = (val != 0);
        if (RegQueryValueExW(hKey, L"font_size", nullptr, &dtype, (BYTE*)&val, &valSize) == ERROR_SUCCESS)
            s.font_size = val;
        if (RegQueryValueExW(hKey, L"position", nullptr, &dtype, (BYTE*)&val, &valSize) == ERROR_SUCCESS)
            s.position = (val != 0) ? TaskbarPosition::Right : TaskbarPosition::Left;

        RegCloseKey(hKey);
    }
    return found;
}

// Constants (mirroring TrafficMonitor's stdafx.h)
#define MIDBAR_TIMER           1236
#define MACHINE_ID_BAR_WINDOW_NAME     L"MachineIdBarWindow"
#define MIDBAR_TIMER_INTERVAL  1000   // ms

// Overlay text style
struct OverlayStyle
{
    COLORREF text_color     = RGB(255, 255, 255);   // white (default --fg)
    COLORREF bg_color       = RGB(0, 0, 0);          // transparent color key (always black)
    int      font_size      = 9;                     // points (matching Python "Segoe UI", 9)
    bool     bold           = true;
    std::wstring font_name  = L"Segoe UI";
};

// Taskbar type detected at runtime
enum class TaskbarType
{
    Classic,    // Windows 7/8/10 — ReBarWindow32 container
    Win11,      // Windows 11 — Shell_TrayWnd directly
    Wine,       // Wine — topmost floating fallback
};

// Core overlay class — creates and manages a child window embedded on the taskbar.
class MachineIdBar
{
public:
    MachineIdBar();
    ~MachineIdBar();

    // Create the overlay window and embed it onto the taskbar.
    // Returns true on success.
    bool Create(HINSTANCE hInstance);

    // Destroy the overlay and restore the taskbar to its original state.
    void Destroy();

    // Re-embed onto the taskbar (call after TaskbarCreated message).
    void Reembed();

    // Set the text to display.
    void SetText(const std::wstring& text);

    // Set visual style.
    void SetStyle(const OverlayStyle& style);

    // Set taskbar position (left or right).
    void SetPosition(TaskbarPosition pos) { m_position = pos; }

    // Get the overlay window handle.
    HWND GetHwnd() const { return m_hwnd; }

    // Get detected taskbar type.
    TaskbarType GetTaskbarType() const { return m_taskbar_type; }

    // Window procedure — static thunk for dispatch.
    static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam);

private:
    // --- Window creation & embedding ---
    bool RegisterWindowClass();
    bool CreateWindow_();
    bool FindTaskbarHandle();
    bool DetectTaskbarType();
    bool InitTaskbarWnd();
    bool EmbedOntoTaskbar();
    void RestoreTaskbar();

    // --- Positioning ---
    void AdjustWindowPos();
    bool CheckTaskbarOnTopOrBottom();
    void CalculateWindowSize(int& width, int& height);

    // --- Drawing ---
    void OnPaint(HDC hdc);
    void ApplyTransparency();

    // --- Message handlers ---
    void OnTimer();
    void OnLButtonDown(int x, int y);
    void OnLButtonUp(int x, int y);
    void OnMouseMove(int x, int y);
    void OnRButtonUp(int x, int y);
    void OnDestroy();

    // --- Clipboard ---
    void CopyToClipboard();

    // --- Utility ---
    HWND GetParentHwnd();
    int  GetDPI();
    void ForceVisible();

    // --- State ---
    HWND        m_hwnd          = nullptr;
    HINSTANCE   m_h_instance    = nullptr;
    HWND        m_h_taskbar     = nullptr;
    HWND        m_h_bar         = nullptr;
    HWND        m_h_min         = nullptr;
    HWND        m_h_notify      = nullptr;
    RECT        m_min_original_rect = {};
    TaskbarType m_taskbar_type  = TaskbarType::Classic;
    bool        m_embedded      = false;
    TaskbarPosition m_position  = TaskbarPosition::Right;

    // --- Content & style ---
    std::wstring m_text;
    OverlayStyle m_style;
    HFONT        m_h_font       = nullptr;
    int          m_dpi          = 96;

    // --- Drag state ---
    bool         m_dragging     = false;
    int          m_drag_start_x = 0;
    int          m_drag_start_y = 0;
    int          m_win_x        = 0;   // current position (parent-relative)
    int          m_win_y        = 0;

    // --- Registered message ---
    UINT m_wm_taskbar_created   = 0;

    // Static instance pointer for WndProc dispatch
    static MachineIdBar* s_instance;
};
