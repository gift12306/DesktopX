using System;
using System.Windows.Forms;

namespace DesktopX;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--selftest")
        {
            Environment.Exit(SelfTest.Run());
        }

        // 桌面右键菜单“打开配置界面”以 --settings 启动时，直接唤起设置窗口
        bool openSettings = args.Length > 0 && args[0] == "--settings";

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

    var app = new DesktopOverlayApp();
    app.Run(openSettings); // 把参数传进去

        // 与 Java 的 Platform.setImplicitExit(false) 对应：
        // 所有窗口关闭（设置窗口按 X 只是隐藏）都不会退出进程。
        Application.Run();
    }
}