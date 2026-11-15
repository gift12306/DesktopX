using System;
using System.Diagnostics;

namespace DesktopX.Utils;

/// <summary>
/// 桌面空白处右键菜单注册，对应 Java 的 DesktopContextMenuManager。
/// 注册 Directory\Background\shell 菜单项，用 reg.exe 执行（与 Java 一致），
/// 启动命令为 应用路径 + --settings（点击菜单即打开配置界面）。
/// 注册在 HKCU\Software\Classes（HKCR 的用户级视图），普通权限即可生效，
/// 无需管理员提权（Java 直接写 HKCR 需要管理员权限）。
/// </summary>
internal static class DesktopContextMenuManager
{
    private const string RegPath = @"HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell\DesktopXSettings";
    private const string MenuName = "打开配置界面";

    /// <summary>添加菜单项</summary>
   public static void AddContextMenu()
{
    try
    {
        // 获取当前程序 exe 的真实绝对路径（支持 dotnet run 和独立 exe）
        string exePath = Application.ExecutablePath;
        string command = $"\"{exePath}\" --settings"; 

        // 使用 HKCU 避免管理员权限限制
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
            @"Software\Classes\Directory\Background\shell\DesktopXSettings\command");
        
        key.SetValue("", command);
        
        // 可选：设置菜单显示的名称
        using var titleKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
            @"Software\Classes\Directory\Background\shell\DesktopXSettings");
        titleKey.SetValue("", "打开 DesktopX 配置");
    }
    catch (Exception ex)
    {
        Console.WriteLine("注册右键菜单失败: " + ex.Message);
    }
}
    public static void RemoveContextMenu()
    {
        try
        {
            Exec("reg", "delete", RegPath, "/f");
            Console.WriteLine("桌面右键菜单已移除");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("移除右键菜单失败: " + e.Message);
        }
    }

    private static void Exec(params string[] args)
    {
        var psi = new ProcessStartInfo(args[0])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        for (int i = 1; i < args.Length; i++)
        {
            psi.ArgumentList.Add(args[i]);
        }
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        if (p.ExitCode != 0)
        {
            throw new Exception("reg 命令失败 (exit=" + p.ExitCode + "): " + output);
        }
    }
}