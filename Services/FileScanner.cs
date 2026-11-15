using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DesktopX.Services;

/// <summary>文件夹扫描与打开，对应 Java 的 FileScanner</summary>
public static class FileScanner
{
    /// <summary>
    /// 扫描文件夹：文件和子文件夹都返回。
    /// 跳过隐藏文件；排序：目录在前，文件在后，各自按名称不区分大小写。
    /// </summary>
    public static List<FileSystemInfo> ScanFolder(string folderPath)
    {
        var result = new List<FileSystemInfo>();
        if (string.IsNullOrWhiteSpace(folderPath)) return result;

        var dir = new DirectoryInfo(folderPath);
        if (!dir.Exists)
        {
            Console.Error.WriteLine("[FileScanner] 目录不存在: " + folderPath);
            return result;
        }

        FileSystemInfo[] entries = dir.GetFileSystemInfos();
        foreach (var f in entries)
        {
            // 跳过隐藏文件（desktop.ini、.DS_Store）。
            // 复刻 Java isHidden() 在 Windows 上的行为：以 "." 开头的文件名也视为隐藏，
            // 而不只是 DOS Hidden 属性。
            if ((f.Attributes & FileAttributes.Hidden) != 0) continue;
            if (!f.Attributes.HasFlag(FileAttributes.Directory) && f.Name.StartsWith(".", StringComparison.Ordinal)) continue;
            result.Add(f);
        }

        // 排序：目录优先（false=目录 排前面），再按名称小写（复刻 Java Comparator）
        result = result
            .OrderBy(f => !f.Attributes.HasFlag(FileAttributes.Directory))
            .ThenBy(f => f.Name.ToLowerInvariant(), StringComparer.Ordinal)
            .ToList();

        return result;
    }

    /// <summary>显示名称：目录直接返回目录名；.lnk 去掉后缀</summary>
    public static string GetDisplayName(FileSystemInfo file)
    {
        if (file.Attributes.HasFlag(FileAttributes.Directory))
        {
            return file.Name;
        }
        string name = file.Name;
        if (name.ToLowerInvariant().EndsWith(".lnk", StringComparison.Ordinal))
        {
            return name.Substring(0, name.Length - 4);
        }
        return name;
    }

    /// <summary>
    /// 打开文件或文件夹：
    /// - 普通文件：交给系统默认程序
    /// - 文件夹：用资源管理器打开
    /// 异步执行（对应 Java 的 new Thread）
    /// </summary>
    public static void OpenFile(FileSystemInfo file)
    {
        Task.Run(() =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(file.FullName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("打开失败: " + file.FullName + " -> " + ex.Message);
            }
        });
    }
}