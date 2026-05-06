using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Translater_App.Helpers;

/// <summary>
/// Full-screen overlay for region selection using pure Win32 APIs.
/// No WinForms dependency.
/// </summary>
public class ScreenOverlay
{
    #region Win32 APIs
    [DllImport("user32.dll")]
    private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern IntPtr GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);
    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);
    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);
    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);
    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr hCursor);
    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);
    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint crColor);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreatePen(int iStyle, int cWidth, uint color);
    [DllImport("gdi32.dll", EntryPoint = "Rectangle")]
    private static extern bool GdiRectangle(IntPtr hdc, int left, int top, int right, int bottom);
    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const int SW_SHOW = 5;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const uint WM_PAINT = 0x000F;
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_SETCURSOR = 0x0020;
    private const uint WM_DESTROY = 0x0002;
    private const int VK_ESCAPE = 0x1B;
    private const int IDC_CROSS = 32515;
    private const uint SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public POINT pt; }
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT { public IntPtr hdc; public bool fErase; public RECT rcPaint; public bool fRestore; public bool fIncUpdate; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved; }
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASS { public uint style; public IntPtr lpfnWndProc; public int cbClsExtra; public int cbWndExtra; public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground; public string? lpszMenuName; public string lpszClassName; }

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT lpPaint);
    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT lpPaint);
    #endregion

    private static Point _startPoint;
    private static Point _currentPoint;
    private static bool _isSelecting;
    private static bool _regionSelected;
    private static Rectangle _selectedRect;
    private static IntPtr _bgBitmapHandle;
    private static int _screenX, _screenY, _screenW, _screenH;
    private static IntPtr _hwnd;

    public Rectangle SelectedRegion => _selectedRect;
    public bool RegionSelected => _regionSelected;

    /// <summary>
    /// Show overlay and let user select a region. Must be called from STA thread.
    /// </summary>
    public static (bool selected, Rectangle region) ShowAndSelect(byte[] screenCapture)
    {
        _regionSelected = false;
        _selectedRect = Rectangle.Empty;
        _isSelecting = false;

        _screenX = GetSystemMetrics(SM_XVIRTUALSCREEN);
        _screenY = GetSystemMetrics(SM_YVIRTUALSCREEN);
        _screenW = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        _screenH = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        // Create background bitmap from PNG
        using var ms = new MemoryStream(screenCapture);
        using var bitmap = new Bitmap(ms);
        _bgBitmapHandle = bitmap.GetHbitmap();

        try
        {
            var hInstance = GetModuleHandle(null);
            var className = "TranslaterOverlay_" + Environment.TickCount;

            var wndProc = new WndProcDelegate(OverlayWndProc);
            var wc = new WNDCLASS
            {
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc),
                hInstance = hInstance,
                hCursor = LoadCursor(IntPtr.Zero, IDC_CROSS),
                lpszClassName = className
            };
            RegisterClass(ref wc);

            _hwnd = CreateWindowEx(
                WS_EX_TOPMOST | WS_EX_TOOLWINDOW,
                className, "Overlay",
                WS_POPUP | WS_VISIBLE,
                _screenX, _screenY, _screenW, _screenH,
                IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

            ShowWindow(_hwnd, SW_SHOW);
            UpdateWindow(_hwnd);
            SetForegroundWindow(_hwnd);
            SetFocus(_hwnd);

            // Message loop
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) != IntPtr.Zero)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            // prevent GC of delegate during message loop
            GC.KeepAlive(wndProc);
        }
        finally
        {
            if (_bgBitmapHandle != IntPtr.Zero)
            {
                DeleteObject(_bgBitmapHandle);
                _bgBitmapHandle = IntPtr.Zero;
            }
        }

        return (_regionSelected, _selectedRect);
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static IntPtr OverlayWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_PAINT:
                var hdc = BeginPaint(hWnd, out var ps);
                // Double-buffer: paint to memory DC then blit
                var memDC = CreateCompatibleDC(hdc);
                var memBitmap = CreateCompatibleBitmap(hdc, _screenW, _screenH);
                var oldMemBitmap = SelectObject(memDC, memBitmap);
                try { PaintOverlay(memDC); } catch { /* prevent crash in native callback */ }
                BitBlt(hdc, 0, 0, _screenW, _screenH, memDC, 0, 0, SRCCOPY);
                SelectObject(memDC, oldMemBitmap);
                DeleteObject(memBitmap);
                DeleteDC(memDC);
                EndPaint(hWnd, ref ps);
                return IntPtr.Zero;

            case WM_LBUTTONDOWN:
                _startPoint = PointFromLParam(lParam);
                _currentPoint = _startPoint;
                _isSelecting = true;
                return IntPtr.Zero;

            case WM_MOUSEMOVE:
                if (_isSelecting)
                {
                    _currentPoint = PointFromLParam(lParam);
                    InvalidateRect(hWnd, IntPtr.Zero, false);
                }
                return IntPtr.Zero;

            case WM_LBUTTONUP:
                if (_isSelecting)
                {
                    _isSelecting = false;
                    _currentPoint = PointFromLParam(lParam);
                    var rect = GetRect();
                    if (rect.Width > 5 && rect.Height > 5)
                    {
                        _selectedRect = rect;
                        _regionSelected = true;
                    }
                    DestroyWindow(hWnd);
                }
                return IntPtr.Zero;

            case WM_RBUTTONDOWN:
            case WM_KEYDOWN when (int)wParam == VK_ESCAPE:
                _regionSelected = false;
                DestroyWindow(hWnd);
                return IntPtr.Zero;

            case WM_SETCURSOR:
                SetCursor(LoadCursor(IntPtr.Zero, IDC_CROSS));
                return (IntPtr)1;

            case WM_ERASEBKGND:
                return (IntPtr)1; // Prevent background erase to reduce flicker

            case WM_DESTROY:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private static void PaintOverlay(IntPtr hdc)
    {
        // Draw the background screenshot
        if (_bgBitmapHandle != IntPtr.Zero)
        {
            var srcDC = CreateCompatibleDC(hdc);
            var oldBitmap = SelectObject(srcDC, _bgBitmapHandle);
            BitBlt(hdc, 0, 0, _screenW, _screenH, srcDC, 0, 0, SRCCOPY);
            SelectObject(srcDC, oldBitmap);
            DeleteDC(srcDC);
        }

        // Use GDI+ for semi-transparent overlays
        using var g = System.Drawing.Graphics.FromHdc(hdc);

        if (_isSelecting)
        {
            var rect = GetRect();
            if (rect.Width > 0 && rect.Height > 0)
            {
                // Semi-transparent dark overlay outside selection
                using var dimBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(100, 0, 0, 0));
                // Top
                g.FillRectangle(dimBrush, 0, 0, _screenW, rect.Y);
                // Bottom
                g.FillRectangle(dimBrush, 0, rect.Y + rect.Height, _screenW, _screenH - rect.Y - rect.Height);
                // Left
                g.FillRectangle(dimBrush, 0, rect.Y, rect.X, rect.Height);
                // Right
                g.FillRectangle(dimBrush, rect.X + rect.Width, rect.Y, _screenW - rect.X - rect.Width, rect.Height);

                // Blue border around selection
                using var borderPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0, 120, 215), 2);
                g.DrawRectangle(borderPen, rect);

                // Size label
                var sizeText = $"{rect.Width} × {rect.Height}";
                using var font = new System.Drawing.Font("Segoe UI", 10);
                var textSize = g.MeasureString(sizeText, font);
                float labelX = rect.X;
                float labelY = rect.Y - textSize.Height - 4;
                if (labelY < 0) labelY = rect.Y + rect.Height + 4;
                using var labelBg = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(180, 0, 0, 0));
                using var labelFg = new System.Drawing.SolidBrush(System.Drawing.Color.White);
                g.FillRectangle(labelBg, labelX, labelY, textSize.Width + 8, textSize.Height + 2);
                g.DrawString(sizeText, font, labelFg, labelX + 4, labelY + 1);
            }
        }
        else
        {
            // No selection yet - slight dim
            using var dimBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(60, 0, 0, 0));
            g.FillRectangle(dimBrush, 0, 0, _screenW, _screenH);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int i);

    private static Point PointFromLParam(IntPtr lParam)
    {
        int val = lParam.ToInt32();
        return new Point((short)(val & 0xFFFF), (short)((val >> 16) & 0xFFFF));
    }

    private static Rectangle GetRect()
    {
        int x = Math.Min(_startPoint.X, _currentPoint.X);
        int y = Math.Min(_startPoint.Y, _currentPoint.Y);
        int w = Math.Abs(_currentPoint.X - _startPoint.X);
        int h = Math.Abs(_currentPoint.Y - _startPoint.Y);
        return new Rectangle(x, y, w, h);
    }
}

