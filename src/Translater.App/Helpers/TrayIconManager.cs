using System.Runtime.InteropServices;

namespace Translater_App.Helpers;

/// <summary>
/// Manages the system tray (notification area) icon using Win32 Shell_NotifyIcon.
/// </summary>
public class TrayIconManager : IDisposable
{
    #region Win32 APIs
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint NIM_ADD = 0x00;
    private const uint NIM_MODIFY = 0x01;
    private const uint NIM_DELETE = 0x02;

    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;

    private const uint WM_USER = 0x0400;
    public const uint WM_TRAYICON = WM_USER + 1;

    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;

    public const uint WM_COMMAND = 0x0111;

    private const uint MF_STRING = 0x00;
    private const uint MF_SEPARATOR = 0x0800;

    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const uint LR_DEFAULTSIZE = 0x0040;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
    #endregion

    private const uint TRAY_ID = 1;
    public const uint CMD_SHOW = 1001;
    public const uint CMD_SCREENSHOT = 1002;
    public const uint CMD_EXIT = 1003;

    private IntPtr _hwnd;
    private IntPtr _hIcon;
    private bool _created;

    public Action? OnShowWindow { get; set; }
    public Action? OnScreenshot { get; set; }
    public Action? OnExit { get; set; }

    public void Create(IntPtr hwnd, string iconPath, string tooltip = "Translater")
    {
        _hwnd = hwnd;

        // Load icon from file
        _hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = TRAY_ID,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = tooltip
        };

        Shell_NotifyIcon(NIM_ADD, ref nid);
        _created = true;
    }

    public void HandleTrayMessage(IntPtr lParam)
    {
        uint msg = (uint)(lParam.ToInt64() & 0xFFFF);

        switch (msg)
        {
            case WM_LBUTTONDBLCLK:
                OnShowWindow?.Invoke();
                break;
            case WM_RBUTTONUP:
                ShowContextMenu();
                break;
        }
    }

    public void HandleCommand(uint commandId)
    {
        switch (commandId)
        {
            case CMD_SHOW:
                OnShowWindow?.Invoke();
                break;
            case CMD_SCREENSHOT:
                OnScreenshot?.Invoke();
                break;
            case CMD_EXIT:
                OnExit?.Invoke();
                break;
        }
    }

    private void ShowContextMenu()
    {
        var hMenu = CreatePopupMenu();
        AppendMenu(hMenu, MF_STRING, CMD_SHOW, "显示主窗口");
        AppendMenu(hMenu, MF_STRING, CMD_SCREENSHOT, "截屏翻译 (Alt+D)");
        AppendMenu(hMenu, MF_SEPARATOR, 0, string.Empty);
        AppendMenu(hMenu, MF_STRING, CMD_EXIT, "退出");

        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd);

        uint cmd = (uint)TrackPopupMenu(hMenu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        DestroyMenu(hMenu);

        if (cmd > 0)
            HandleCommand(cmd);
    }

    public void Dispose()
    {
        if (_created)
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = TRAY_ID
            };
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _created = false;
        }

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }
}
