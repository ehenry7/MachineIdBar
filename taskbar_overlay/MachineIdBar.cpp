// MachineIdBar.cpp
// Implementation of the taskbar overlay window.
// Extracted and simplified from TrafficMonitor's taskbar embedding code
// (TaskBarDlg.cpp, ClassicalTaskbarDlg.cpp, Win11TaskbarDlg.cpp, WineTaskbarDlg.cpp).
//
// Original TrafficMonitor is MFC-based; this is a pure Win32 reimplementation
// that captures the same Win32 techniques for embedding a window onto the taskbar.

#include "MachineIdBar.h"
#include <commctrl.h>
#include <shellscalingapi.h>  // for GetDpiForMonitor
#include <cstdio>

#ifdef _MSC_VER
#pragma comment(lib, "pdh.lib")
#endif

// Custom message: show the tray menu (posted from overlay right-click)
#define MIDBAR_WM_SHOW_MENU (WM_USER + 1006)

// Static instance pointer for WndProc dispatch
MachineIdBar* MachineIdBar::s_instance = nullptr;

// Debug logging
static void DebugLog(const wchar_t* fmt, ...)
{
    FILE* f = nullptr;
    if (_wfopen_s(&f, L"MachineIdBar_debug.log", L"a") != 0 || !f)
        return;
    va_list args;
    va_start(args, fmt);
    vfwprintf(f, fmt, args);
    va_end(args);
    fputwc(L'\n', f);
    fclose(f);
}

static bool RectEqual(const RECT& a, const RECT& b)
{
    return a.left == b.left && a.top == b.top &&
           a.right == b.right && a.bottom == b.bottom;
}

MachineIdBar::MachineIdBar()
{
    s_instance = this;
    m_wm_taskbar_created = RegisterWindowMessage(L"TaskbarCreated");
}

MachineIdBar::~MachineIdBar()
{
    Destroy();
    if (s_instance == this)
        s_instance = nullptr;
}

// ============================================================================
// Public API
// ============================================================================

bool MachineIdBar::Create(HINSTANCE hInstance)
{
    m_h_instance = hInstance;
    DebugLog(L"=== Create() started ===");

    if (!RegisterWindowClass())
    {
        DebugLog(L"ERROR: RegisterWindowClass failed (GetLastError=%lu)", GetLastError());
        return false;
    }
    DebugLog(L"RegisterWindowClass OK");

    if (!CreateWindow_())
    {
        DebugLog(L"ERROR: CreateWindow_ failed (GetLastError=%lu)", GetLastError());
        return false;
    }
    DebugLog(L"CreateWindow_ OK, hwnd=%p", m_hwnd);

    if (!FindTaskbarHandle())
    {
        DebugLog(L"ERROR: FindTaskbarHandle failed (GetLastError=%lu)", GetLastError());
        return false;
    }
    DebugLog(L"FindTaskbarHandle OK, m_h_taskbar=%p", m_h_taskbar);

    if (!DetectTaskbarType())
    {
        DebugLog(L"ERROR: DetectTaskbarType failed");
        return false;
    }
    DebugLog(L"DetectTaskbarType OK, type=%d", (int)m_taskbar_type);

    if (!InitTaskbarWnd())
    {
        DebugLog(L"ERROR: InitTaskbarWnd failed");
        return false;
    }
    DebugLog(L"InitTaskbarWnd OK, m_h_bar=%p, m_h_min=%p, m_h_notify=%p", m_h_bar, m_h_min, m_h_notify);

    if (!EmbedOntoTaskbar())
    {
        DebugLog(L"ERROR: EmbedOntoTaskbar failed");
        return false;
    }
    DebugLog(L"EmbedOntoTaskbar OK");

    ApplyTransparency();
    AdjustWindowPos();
    DebugLog(L"AdjustWindowPos done");

    SetTimer(m_hwnd, MIDBAR_TIMER, MIDBAR_TIMER_INTERVAL, nullptr);
    // SW_SHOWNOACTIVATE: SW_SHOW would activate the top-level ancestor
    // (Shell_TrayWnd) and steal keyboard focus from the user's app
    ShowWindow(m_hwnd, SW_SHOWNOACTIVATE);
    DebugLog(L"ShowWindow done, m_embedded=true");

    m_embedded = true;
    return true;
}

void MachineIdBar::Destroy()
{
    if (m_hwnd)
    {
        KillTimer(m_hwnd, MIDBAR_TIMER);
        RestoreTaskbar();
        DestroyWindow(m_hwnd);
        m_hwnd = nullptr;
    }
    if (m_h_font)
    {
        DeleteObject(m_h_font);
        m_h_font = nullptr;
    }
    m_embedded = false;
}

void MachineIdBar::Reembed()
{
    RestoreTaskbar();
    m_h_bar = nullptr;
    m_h_min = nullptr;
    m_h_notify = nullptr;

    FindTaskbarHandle();
    DetectTaskbarType();
    InitTaskbarWnd();
    EmbedOntoTaskbar();
    ApplyTransparency();
    AdjustWindowPos();
    ShowWindow(m_hwnd, SW_SHOWNOACTIVATE);
}

void MachineIdBar::SetText(const std::wstring& text)
{
    m_text = text;
    // Recreate font if needed and recalculate window size immediately
    if (m_hwnd)
    {
        int wnd_w, wnd_h;
        CalculateWindowSize(wnd_w, wnd_h);
        // Resize the window to fit the new text
        RECT rc;
        GetWindowRect(m_hwnd, &rc);
        HWND hParent = GetParent(m_hwnd);
        if (hParent)
        {
            POINT pt = { rc.left, rc.top };
            ScreenToClient(hParent, &pt);
            ::MoveWindow(m_hwnd, pt.x, pt.y, wnd_w, wnd_h, TRUE);
        }
        AdjustWindowPos();
        InvalidateRect(m_hwnd, nullptr, FALSE);
    }
}

void MachineIdBar::SetStyle(const OverlayStyle& style)
{
    m_style = style;
    if (m_h_font)
        DeleteObject(m_h_font);
    m_h_font = nullptr;
    ApplyTransparency();
    InvalidateRect(m_hwnd, nullptr, FALSE);
}

// ============================================================================
// Window class & creation
// ============================================================================

bool MachineIdBar::RegisterWindowClass()
{
    WNDCLASSEX wc = {};
    wc.cbSize        = sizeof(WNDCLASSEX);
    wc.lpfnWndProc   = WndProc;
    wc.hInstance     = m_h_instance;
    wc.lpszClassName = L"MachineIdBarClass";
    wc.hCursor       = LoadCursor(nullptr, IDC_ARROW);
    wc.hbrBackground = nullptr;  // we paint our own background
    wc.style         = CS_HREDRAW | CS_VREDRAW;

    ATOM atom = RegisterClassEx(&wc);
    return atom != 0;
}

bool MachineIdBar::CreateWindow_()
{
    // WS_EX_TOOLWINDOW: hide from Alt-Tab and taskbar
    // WS_EX_LAYERED:    for color-key transparency
    // WS_EX_NOACTIVATE: never activate on mouse click (prevents focus stealing)
    DWORD ex_style = WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
    DWORD style    = WS_POPUP | WS_CHILD;  // WS_CHILD because we SetParent to taskbar

    m_hwnd = ::CreateWindowEx(
        ex_style,
        L"MachineIdBarClass",
        MACHINE_ID_BAR_WINDOW_NAME,
        style,
        0, 0, 200, 32,
        nullptr, nullptr, m_h_instance, nullptr);

    if (!m_hwnd)
        return false;

    SetWindowText(m_hwnd, MACHINE_ID_BAR_WINDOW_NAME);
    return true;
}

// ============================================================================
// Finding the taskbar — mirrors TrafficMonitor's FindTaskbarHandle()
// ============================================================================

bool MachineIdBar::FindTaskbarHandle()
{
    // Primary monitor: Shell_TrayWnd
    // Multi-monitor secondary: Shell_SecondaryTrayWnd
    m_h_taskbar = ::FindWindow(L"Shell_TrayWnd", nullptr);
    if (!m_h_taskbar)
        m_h_taskbar = ::FindWindow(L"Shell_SecondaryTrayWnd", nullptr);

    return m_h_taskbar != nullptr;
}

// ============================================================================
// Detecting taskbar type — mirrors TrafficMonitor's CheckWindows11Taskbar()
// ============================================================================

bool MachineIdBar::DetectTaskbarType()
{
    if (!m_h_taskbar)
        return false;

    // Windows 11 taskbar: Shell_TrayWnd has a child
    // "Windows.UI.Composition.DesktopWindowContentBridge"
    HWND hChild = ::FindWindowEx(m_h_taskbar, nullptr,
        L"Windows.UI.Composition.DesktopWindowContentBridge", nullptr);
    if (hChild)
    {
        m_taskbar_type = TaskbarType::Win11;
        return true;
    }

    // Wine detection: check for wine-specific version info or window
    // Simplified check — TrafficMonitor uses RtlGetNtVersionPoints
    HMODULE hNtdll = GetModuleHandle(L"ntdll.dll");
    if (hNtdll)
    {
        // If wine_get_version exists, we're on Wine
        FARPROC fn = GetProcAddress(hNtdll, "wine_get_version");
        if (fn)
        {
            m_taskbar_type = TaskbarType::Wine;
            return true;
        }
    }

    // Default: classic taskbar (Win7/8/10)
    m_taskbar_type = TaskbarType::Classic;
    return true;
}

// ============================================================================
// InitTaskbarWnd — find child container windows (subclass-specific)
// Mirrors: ClassicalTaskbarDlg::InitTaskbarWnd, Win11TaskbarDlg::InitTaskbarWnd
// ============================================================================

bool MachineIdBar::InitTaskbarWnd()
{
    switch (m_taskbar_type)
    {
    case TaskbarType::Classic:
    {
        // Classic taskbar structure:
        //   Shell_TrayWnd
        //     └── ReBarWindow32 (or WorkerW)
        //           └── MSTaskSwWClass (or MSTaskListWClass)  ← minimized windows area
        m_h_bar = ::FindWindowEx(m_h_taskbar, nullptr, L"ReBarWindow32", nullptr);
        if (!m_h_bar)
            m_h_bar = ::FindWindowEx(m_h_taskbar, nullptr, L"WorkerW", nullptr);

        if (m_h_bar)
        {
            m_h_min = ::FindWindowEx(m_h_bar, nullptr, L"MSTaskSwWClass", nullptr);
            if (!m_h_min)
                m_h_min = ::FindWindowEx(m_h_bar, nullptr, L"MSTaskListWClass", nullptr);

            if (m_h_min)
            {
                // Save the original rect of the minimized-window area
                // so we can restore it on close.
                GetWindowRect(m_h_min, &m_min_original_rect);
            }
        }
        return m_h_bar != nullptr;
    }

    case TaskbarType::Win11:
    {
        // Win11 taskbar structure:
        //   Shell_TrayWnd
        //     └── TrayNotifyWnd (notification area)
        //     └── Start (start button)
        m_h_notify = ::FindWindowEx(m_h_taskbar, nullptr, L"TrayNotifyWnd", nullptr);
        return true;  // Win11 parents to Shell_TrayWnd directly
    }

    case TaskbarType::Wine:
    {
        // No child containers — Wine uses topmost floating window
        return true;
    }
    }

    return false;
}

// ============================================================================
// Embedding — mirrors TrafficMonitor's OnInitDialog (SetParent) + GetParentHwnd()
// ============================================================================

bool MachineIdBar::EmbedOntoTaskbar()
{
    HWND hParent = GetParentHwnd();
    DebugLog(L"EmbedOntoTaskbar: hParent=%p", hParent);
    if (!hParent)
        return false;

    HWND hOldParent = ::SetParent(m_hwnd, hParent);
    DebugLog(L"SetParent done, old parent=%p", hOldParent);
    return true;
}

HWND MachineIdBar::GetParentHwnd()
{
    switch (m_taskbar_type)
    {
    case TaskbarType::Classic:
        return m_h_bar;  // ReBarWindow32 / WorkerW
    case TaskbarType::Win11:
        return m_h_taskbar;  // Shell_TrayWnd directly
    case TaskbarType::Wine:
        return GetDesktopWindow();
    }
    return nullptr;
}

// ============================================================================
// Restoring the taskbar — mirrors ClassicalTaskbarDlg::ResetTaskbarPos
// ============================================================================

void MachineIdBar::RestoreTaskbar()
{
    if (m_taskbar_type == TaskbarType::Classic && m_h_min && m_h_bar)
    {
        // Restore the minimized-window area to its original size.
        // m_min_original_rect holds screen coordinates (GetWindowRect), but
        // MoveWindow expects coordinates relative to the parent window.
        int w = m_min_original_rect.right - m_min_original_rect.left;
        int h = m_min_original_rect.bottom - m_min_original_rect.top;
        POINT pt = { m_min_original_rect.left, m_min_original_rect.top };
        ScreenToClient(m_h_bar, &pt);
        ::MoveWindow(m_h_min, pt.x, pt.y, w, h, TRUE);
    }
    // Win11: nothing to restore (we didn't resize any child)
    // Wine: nothing to restore
}

// ============================================================================
// Positioning — mirrors TrafficMonitor's AdjustWindowPos / AdjustTaskbarWndPos
// ============================================================================

void MachineIdBar::AdjustWindowPos()
{
    if (!m_h_taskbar)
        return;

    RECT rcTaskbar;
    GetWindowRect(m_h_taskbar, &rcTaskbar);

    int wnd_w, wnd_h;
    CalculateWindowSize(wnd_w, wnd_h);

    bool horizontal = CheckTaskbarOnTopOrBottom();

    // Suppress repeated logging: only log when position/size actually changes
    static int last_x = -1, last_y = -1, last_w = -1, last_h = -1;

    switch (m_taskbar_type)
    {
    case TaskbarType::Classic:
    {
        if (horizontal)
        {
            // Horizontal taskbar (top or bottom)
            // Position to the right of the minimized-window area
            int x = 0;  // relative to parent (ReBar)
            int y = (rcTaskbar.bottom - rcTaskbar.top - wnd_h) / 2;

            if (m_h_min)
            {
                // Shrink the minimized-window area to make room.
                // Base the new width on the ORIGINAL size saved in
                // InitTaskbarWnd — using the current size would shrink the
                // area by wnd_w again on every call.
                RECT rcMin;
                GetWindowRect(m_h_min, &rcMin);
                POINT pt = { rcMin.left, rcMin.top };
                ScreenToClient(m_h_bar, &pt);

                int min_w = (m_min_original_rect.right - m_min_original_rect.left) - wnd_w - 4;
                if (min_w < 10) min_w = 10;
                int min_h = rcMin.bottom - rcMin.top;
                ::MoveWindow(m_h_min, pt.x, pt.y, min_w, min_h, TRUE);

                x = pt.x + min_w + 2;
            }

            ::MoveWindow(m_hwnd, x, y, wnd_w, wnd_h, TRUE);
            if (x != last_x || y != last_y || wnd_w != last_w || wnd_h != last_h)
            {
                DebugLog(L"Classic horizontal: MoveWindow x=%d y=%d w=%d h=%d", x, y, wnd_w, wnd_h);
                last_x = x; last_y = y; last_w = wnd_w; last_h = wnd_h;
            }
        }
        else
        {
            // Vertical taskbar (left or right side)
            int x = 2;
            int y = 0;
            if (m_h_min)
            {
                // Same as above: shrink from the ORIGINAL height, not the
                // current one, so repeated calls don't compound.
                RECT rcMin;
                GetWindowRect(m_h_min, &rcMin);
                POINT pt = { rcMin.left, rcMin.top };
                ScreenToClient(m_h_bar, &pt);

                int min_h = (m_min_original_rect.bottom - m_min_original_rect.top) - wnd_h - 4;
                if (min_h < 10) min_h = 10;
                int min_w = rcMin.right - rcMin.left;
                ::MoveWindow(m_h_min, pt.x, pt.y, min_w, min_h, TRUE);

                y = pt.y + min_h + 2;
            }
            ::MoveWindow(m_hwnd, x, y, wnd_w, wnd_h, TRUE);
            if (x != last_x || y != last_y || wnd_w != last_w || wnd_h != last_h)
            {
                DebugLog(L"Classic vertical: MoveWindow x=%d y=%d w=%d h=%d", x, y, wnd_w, wnd_h);
                last_x = x; last_y = y; last_w = wnd_w; last_h = wnd_h;
            }
        }
        break;
    }

    case TaskbarType::Win11:
    {
        // Win11: position on left or right side of the taskbar
        RECT rcNotify = {};
        if (m_h_notify)
            GetWindowRect(m_h_notify, &rcNotify);

        int taskbar_h = rcTaskbar.bottom - rcTaskbar.top;
        int y = rcTaskbar.top + (taskbar_h - wnd_h) / 2;

        int x;
        if (m_position == TaskbarPosition::Right)
        {
            // Right side: left of the notification area
            if (rcNotify.left > 0)
                x = rcNotify.left - wnd_w - 8;
            else
                x = rcTaskbar.right - wnd_w - 8;
        }
        else
        {
            // Left side: right of the Start button area
            x = rcTaskbar.left + 8;
        }

        // Convert screen coords to taskbar-client coords
        POINT pt = { x, y };
        ScreenToClient(m_h_taskbar, &pt);
        ::MoveWindow(m_hwnd, pt.x, pt.y, wnd_w, wnd_h, TRUE);
        if (pt.x != last_x || pt.y != last_y || wnd_w != last_w || wnd_h != last_h)
        {
            DebugLog(L"Win11: MoveWindow x=%d y=%d w=%d h=%d (pos=%ls)",
                     pt.x, pt.y, wnd_w, wnd_h,
                     m_position == TaskbarPosition::Right ? L"right" : L"left");
            last_x = pt.x; last_y = pt.y; last_w = wnd_w; last_h = wnd_h;
        }
        break;
    }

    case TaskbarType::Wine:
    {
        // Wine: topmost floating window at bottom corner
        int screen_w = GetSystemMetrics(SM_CXSCREEN);
        int screen_h = GetSystemMetrics(SM_CYSCREEN);
        int wx = (m_position == TaskbarPosition::Right) ? (screen_w - wnd_w - 4) : 0;
        SetWindowPos(m_hwnd, HWND_TOPMOST, wx, screen_h - wnd_h - 4, wnd_w, wnd_h, SWP_NOACTIVATE);
        break;
    }
    }
}

bool MachineIdBar::CheckTaskbarOnTopOrBottom()
{
    if (!m_h_taskbar)
        return true;
    RECT rc;
    GetWindowRect(m_h_taskbar, &rc);
    // If width >= height, it's a horizontal taskbar (top or bottom)
    return (rc.right - rc.left) >= (rc.bottom - rc.top);
}

void MachineIdBar::CalculateWindowSize(int& width, int& height)
{
    m_dpi = GetDPI();

    if (!m_h_font)
    {
        int font_size = -MulDiv(m_style.font_size, m_dpi, 72);
        int weight = m_style.bold ? FW_BOLD : FW_NORMAL;
        m_h_font = CreateFontW(font_size, 0, 0, 0, weight, FALSE, FALSE, FALSE,
            DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
            CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_SWISS, m_style.font_name.c_str());
    }

    HDC hdc = GetDC(m_hwnd);
    HGDIOBJ oldFont = SelectObject(hdc, m_h_font);

    SIZE sz = {};
    std::wstring label = m_text.empty() ? L"Sample" : m_text;
    GetTextExtentPoint32(hdc, label.c_str(), (int)label.length(), &sz);

    SelectObject(hdc, oldFont);
    ReleaseDC(m_hwnd, hdc);

    width = sz.cx + 20;   // padx=10 on each side (matching Python)
    height = sz.cy + 4;   // pady=2 on each side (matching Python)

    // For horizontal taskbar, constrain to taskbar height
    if (CheckTaskbarOnTopOrBottom() && m_h_taskbar)
    {
        RECT rc;
        GetWindowRect(m_h_taskbar, &rc);
        int taskbar_h = rc.bottom - rc.top;
        if (height > taskbar_h - 2)
            height = taskbar_h - 2;
    }
}

// ============================================================================
// Transparency — mirrors TrafficMonitor's ApplyWindowTransparentColor
// ============================================================================

void MachineIdBar::ApplyTransparency()
{
    LONG ex = GetWindowLong(m_hwnd, GWL_EXSTYLE);
    SetWindowLong(m_hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED);

    // Use color-key transparency: the bg_color becomes transparent
    SetLayeredWindowAttributes(m_hwnd, m_style.bg_color, 255, LWA_COLORKEY);
}

// ============================================================================
// Drawing — mirrors TrafficMonitor's ShowInfo / DrawDisplayItem (simplified)
// ============================================================================

void MachineIdBar::OnPaint(HDC hdc)
{
    if (!m_h_font)
    {
        int dummy_w, dummy_h;
        CalculateWindowSize(dummy_w, dummy_h);  // force font creation
    }

    RECT rc;
    GetClientRect(m_hwnd, &rc);

    // Fill background with the transparent color key
    HBRUSH hBrush = CreateSolidBrush(m_style.bg_color);
    FillRect(hdc, &rc, hBrush);
    DeleteObject(hBrush);

    // Draw text
    HGDIOBJ oldFont = SelectObject(hdc, m_h_font);
    SetBkMode(hdc, TRANSPARENT);
    SetTextColor(hdc, m_style.text_color);

    RECT rcText = rc;
    rcText.left += 10;   // padx=10 (matching Python)
    rcText.top += 2;     // pady=2
    DrawText(hdc, m_text.c_str(), (int)m_text.length(), &rcText,
        DT_LEFT | DT_VCENTER | DT_SINGLELINE);

    SelectObject(hdc, oldFont);
}

// ============================================================================
// Timer — mirrors TrafficMonitor's OnTimer
// ============================================================================

void MachineIdBar::OnTimer()
{
    // Trigger repaint to refresh displayed text
    InvalidateRect(m_hwnd, nullptr, FALSE);

    // Re-assert visibility (matching Python's force_visible in check_resolution)
    ForceVisible();

    // Re-check positioning only when the taskbar geometry actually changed
    // (RDP reconnect, moved/resized taskbar or its child areas). Re-asserting
    // every second would undo a user drag within a second. WM_DISPLAYCHANGE
    // only reaches top-level windows, so this child window has to poll.
    if (!m_embedded || m_dragging)
        return;

    RECT rc = { 0, 0, 0, 0 };
    RECT rcAnchor = { 0, 0, 0, 0 };
    if (m_h_taskbar)
        GetWindowRect(m_h_taskbar, &rc);
    if (m_taskbar_type == TaskbarType::Win11 && m_h_notify)
        GetWindowRect(m_h_notify, &rcAnchor);
    else if (m_taskbar_type == TaskbarType::Classic && m_h_min)
        GetWindowRect(m_h_min, &rcAnchor);

    if (!RectEqual(rc, m_last_taskbar_rect) || !RectEqual(rcAnchor, m_last_anchor_rect))
    {
        m_last_taskbar_rect = rc;
        m_last_anchor_rect = rcAnchor;
        AdjustWindowPos();
    }
}

// ============================================================================
// Mouse interaction — click-to-copy, ctrl+click exit, drag-to-move
// (matching Python's on_press/on_drag/on_release)
// ============================================================================

void MachineIdBar::OnLButtonDown(int x, int y)
{
    // Check for Ctrl key
    if (GetKeyState(VK_CONTROL) & 0x8000)
    {
        PostQuitMessage(0);
        return;
    }

    m_dragging = false;
    m_drag_start_x = x;
    m_drag_start_y = y;

    // Store current window position
    RECT rc;
    GetWindowRect(m_hwnd, &rc);
    POINT pt = { rc.left, rc.top };
    ScreenToClient(GetParent(m_hwnd), &pt);
    m_win_x = pt.x;
    m_win_y = pt.y;

    SetCapture(m_hwnd);
}

void MachineIdBar::OnMouseMove(int x, int y)
{
    if (!GetCapture() || GetCapture() != m_hwnd)
        return;

    // Mark as dragging if moved beyond threshold (matching Python's 3px)
    if (!m_dragging)
    {
        if (abs(x - m_drag_start_x) > 3 || abs(y - m_drag_start_y) > 3)
            m_dragging = true;
        else
            return;
    }

    // Move window by drag delta
    int new_x = m_win_x + (x - m_drag_start_x);
    int new_y = m_win_y + (y - m_drag_start_y);

    int wnd_w, wnd_h;
    CalculateWindowSize(wnd_w, wnd_h);
    ::MoveWindow(m_hwnd, new_x, new_y, wnd_w, wnd_h, TRUE);
}

void MachineIdBar::OnLButtonUp(int /*x*/, int /*y*/)
{
    if (GetCapture() == m_hwnd)
        ReleaseCapture();

    // If it was a click (not a drag), copy to clipboard
    if (!m_dragging)
        CopyToClipboard();

    m_dragging = false;
}

void MachineIdBar::OnRButtonUp(int /*x*/, int /*y*/)
{
    // Ctrl+right-click = exit (matching Python)
    if (GetKeyState(VK_CONTROL) & 0x8000)
    {
        PostQuitMessage(0);
        return;
    }

    // Right-click = show the tray menu (same as tray icon right-click)
    // Post a message to the hidden window to show the menu at cursor position
    HWND hHidden = FindWindowW(L"MachineIdBarHidden", nullptr);
    if (hHidden)
        PostMessageW(hHidden, MIDBAR_WM_SHOW_MENU, 0, 0);
}

// ============================================================================
// Clipboard — copy display text
// ============================================================================

void MachineIdBar::CopyToClipboard()
{
    if (!OpenClipboard(m_hwnd))
        return;

    EmptyClipboard();

    // Allocate global memory for the wide string
    size_t len = (m_text.length() + 1) * sizeof(wchar_t);
    HGLOBAL hMem = GlobalAlloc(GMEM_MOVEABLE, len);
    if (hMem)
    {
        wchar_t* pMem = (wchar_t*)GlobalLock(hMem);
        if (pMem)
        {
            wcscpy_s(pMem, m_text.length() + 1, m_text.c_str());
            GlobalUnlock(hMem);
            SetClipboardData(CF_UNICODETEXT, hMem);
        }
    }

    CloseClipboard();
}

// ============================================================================
// Force visible — re-assert visibility (matching Python's force_visible)
// ============================================================================

void MachineIdBar::ForceVisible()
{
    if (!m_hwnd)
        return;

    if (!IsWindowVisible(m_hwnd))
        ShowWindow(m_hwnd, SW_SHOWNOACTIVATE);

    // Re-assert position within the taskbar
    SetWindowPos(m_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
}

// ============================================================================
// DPI — mirrors TrafficMonitor's GetDPI
// ============================================================================

int MachineIdBar::GetDPI()
{
    typedef HRESULT (WINAPI *GetDpiForMonitorFn)(HMONITOR, MONITOR_DPI_TYPE, UINT*, UINT*);

    // Resolve shcore.dll once and keep it for the process lifetime —
    // GetDPI runs every second via the timer, and loading/freeing the
    // DLL on each call is wasteful.
    static HMODULE hShcore = LoadLibrary(L"shcore.dll");
    static GetDpiForMonitorFn fnGetDpi = hShcore
        ? reinterpret_cast<GetDpiForMonitorFn>(
            reinterpret_cast<void*>(GetProcAddress(hShcore, "GetDpiForMonitor")))
        : nullptr;

    // Try per-monitor DPI
    if (fnGetDpi)
    {
        HMONITOR hMon = MonitorFromWindow(m_hwnd, MONITOR_DEFAULTTONEAREST);
        if (hMon)
        {
            UINT dpiX = 96, dpiY = 96;
            if (fnGetDpi(hMon, MDT_EFFECTIVE_DPI, &dpiX, &dpiY) == S_OK)
                return (int)dpiX;
        }
    }
    // Fallback: system DPI
    HDC hdc = GetDC(nullptr);
    int dpi = GetDeviceCaps(hdc, LOGPIXELSX);
    ReleaseDC(nullptr, hdc);
    return dpi;
}

// ============================================================================
// Window procedure — static thunk
// ============================================================================

LRESULT CALLBACK MachineIdBar::WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    MachineIdBar* self = s_instance;

    // Never activate this window when the user clicks it — activating would
    // move keyboard focus away from the app the user is working in.
    // MA_NOACTIVATE: don't activate, but still deliver the mouse messages
    // (click-to-copy and drag keep working).
    if (msg == WM_MOUSEACTIVATE)
        return MA_NOACTIVATE;

    // Handle registered TaskbarCreated message
    if (self && msg == self->m_wm_taskbar_created)
    {
        self->Reembed();
        return 0;
    }

    switch (msg)
    {
    case WM_PAINT:
    {
        PAINTSTRUCT ps;
        HDC hdc = BeginPaint(hwnd, &ps);
        if (self)
            self->OnPaint(hdc);
        EndPaint(hwnd, &ps);
        return 0;
    }

    case WM_TIMER:
        if (self)
            self->OnTimer();
        return 0;

    case WM_LBUTTONDOWN:
        if (self)
            self->OnLButtonDown(LOWORD(lParam), HIWORD(lParam));
        return 0;

    case WM_MOUSEMOVE:
        if (self)
            self->OnMouseMove(LOWORD(lParam), HIWORD(lParam));
        return 0;

    case WM_LBUTTONUP:
        if (self)
            self->OnLButtonUp(LOWORD(lParam), HIWORD(lParam));
        return 0;

    case WM_RBUTTONUP:
        if (self)
            self->OnRButtonUp(LOWORD(lParam), HIWORD(lParam));
        return 0;

    case WM_DISPLAYCHANGE:
        // Monitor configuration changed — just reposition, don't reembed
        // (reembedding every WM_DISPLAYCHANGE causes thrashing on multi-monitor)
        if (self && self->m_embedded)
            self->AdjustWindowPos();
        return 0;

    case WM_DESTROY:
        if (self)
            self->OnDestroy();
        PostQuitMessage(0);
        return 0;

    default:
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }
}

void MachineIdBar::OnDestroy()
{
    KillTimer(m_hwnd, MIDBAR_TIMER);
    RestoreTaskbar();
    m_embedded = false;
    m_hwnd = nullptr;
}
