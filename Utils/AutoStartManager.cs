using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace DesktopX.Utils;

/// <summary>
/// 开机自启动管理，对应 Java 的 AutoStartManager（HKCU\Software\Microsoft\Windows\CurrentVersion\Run）。
/// 使用 Registry API 代替 Java 的 reg 命令，实现相同效果：
/// - enable：写入 REG_SZ 值；
/// - disable：删除值；
/// - isEnabled：值是否存在。
/// </summary>
internal static class AutoStartManager
{
    private const string RegPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesktopX";

    public static void Enable()
    {
        string? command = BuildLaunchCommand();
        if (command == null) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegPath);
            key?.SetValue(ValueName, command, RegistryValueKind.String);
            Console.WriteLine("已写入开机自启动: " + command);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("注册表操作失败: " + e.Message);
        }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            Console.WriteLine("已移除开机自启动");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("注册表操作失败: " + e.Message);
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegPath);
            return key?.GetValue(ValueName) != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>当前程序如何启动，就如何注册自启动（对应 Java 的 buildLaunchCommand）</summary>
    private static string? BuildLaunchCommand()
    {
        string? command = AppPathResolver.BuildLaunchCommand();
        if (command != null) return command;

        // 兜底：直接使用当前进程路径
        string? processPath = Environment.ProcessPath;
        if (processPath != null && File.Exists(processPath))
        {
            return "\"" + processPath + "\"";
        }
        return null;
    }
}