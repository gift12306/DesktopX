using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopX.Utils;

/// <summary>
/// 纯置底悬浮窗方案（适配 Win11 24H2 的稳定方案）。
/// 不再使用 SetParent，而是作为顶层窗口强制置底。
/// Win+D 的拦截由 DesktopForm.WndProc 负责。
/// </summary>
internal static class DesktopLayerBinder
{
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

    private static System.Windows.Forms.Timer? _bottomTimer;

    public static bool Bind(Form form)
    {
        IntPtr hwnd = form.Handle;
        if (hwnd == IntPtr.Zero)
        {
            Console.Error.WriteLine("DesktopLayerBinder: 窗口句柄尚未创建");
            return false;
        }

        // 注意：不需要 SetParent，不需要 WS_EX_LAYERED 的补加
        // 因为 DesktopForm 的 CreateParams 已经在窗口创建时带上了分层样式

        // 1. 初始置底（压在桌面之上、所有普通窗口之下）
        NativeMethods.SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        // 2. 启动定时器，持续保持置底（防止其他软件改变 Z 序盖住它）
        _bottomTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _bottomTimer.Tick += (s, e) =>
        {
            NativeMethods.SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        };
        _bottomTimer.Start();

        return true;
    }

    public static void Unbind()
    {
        _bottomTimer?.Stop();
        _bottomTimer?.Dispose();
        _bottomTimer = null;
    }
}