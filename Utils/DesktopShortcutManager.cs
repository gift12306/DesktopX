using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DesktopX.Utils;

public static class DesktopShortcutManager
{
    // 默认注册表路径
    private const string RegKeyPath = @"Software\DesktopX";
    private const string RegValueName = "DesktopShortcutCreated"; // 对齐测试代码的键名

    public static bool IsFirstRunDone() => IsFirstRunDone(RegKeyPath);
    public static void MarkFirstRunDone() => MarkFirstRunDone(RegKeyPath);

    /// <summary>检查是否已经完成过首次运行（支持测试自定义路径）</summary>
    public static bool IsFirstRunDone(string regPath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(regPath);
            if (key != null)
            {
                var val = key.GetValue(RegValueName);
                return val != null && val.ToString() == "1";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("读取注册表失败: " + ex.Message);
        }
        return false;
    }

    /// <summary>标记首次运行已完成（支持测试自定义路径）</summary>
    public static void MarkFirstRunDone(string regPath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(regPath);
            key?.SetValue(RegValueName, "1", RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            Console.WriteLine("写入注册表失败: " + ex.Message);
        }
    }

    /// <summary>首次运行时在桌面创建快捷方式</summary>
    public static void EnsureFirstRunShortcut()
    {
        if (IsFirstRunDone()) return;

        try
        {
            string exePath = Application.ExecutablePath;
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string shortcutPath = Path.Combine(desktopPath, "DesktopX.lnk");

            if (!File.Exists(shortcutPath))
            {
                CreateShortcut(shortcutPath, exePath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("创建快捷方式失败: " + ex.Message);
        }
        finally
        {
            MarkFirstRunDone();
        }
    }

    /// <summary>使用 COM 创建 Windows 快捷方式 (.lnk) —— 修改为返回 bool 以支持测试</summary>
    public static bool CreateShortcut(string shortcutPath, string targetPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return false;

            dynamic shell = Activator.CreateInstance(shellType)!;
            var shortcut = shell.CreateShortcut(shortcutPath);
            
            shortcut.TargetPath = targetPath;
            // 如果目标路径没有目录（比如自检时的假文件），则跳过设置工作目录
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? "";
            shortcut.Description = "DesktopX 桌面管理工具";
            shortcut.Save();

            Marshal.ReleaseComObject(shortcut);
            Marshal.ReleaseComObject(shell);
            return true;
        }
        catch
        {
            return false; // 发生异常（例如路径非法）时返回 false，符合自检预期
        }
    }
}