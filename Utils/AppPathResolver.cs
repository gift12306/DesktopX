using System;
using System.IO;
using System.Reflection;

namespace DesktopX.Utils;

/// <summary>
/// 应用路径解析，对应 Java 的 AppPathResolver。
/// 返回当前应用的启动命令（含引号，可直接写入注册表）；探测失败返回 null。
/// C# 中等价形态：已发布的 exe，或 dotnet 启动的 dll。
/// </summary>
internal static class AppPathResolver
{
    public static string? BuildLaunchCommand()
    {
        // ① 优先：当前进程的可执行文件（对应 Java 的 jpackage.app-path / 已发布形态）
        string? processPath = Environment.ProcessPath;
        if (processPath != null && File.Exists(processPath))
        {
            Console.WriteLine("检测到进程路径: " + processPath);
            return "\"" + processPath + "\"";
        }

        // ② dotnet 运行形态（对应 Java 的 jar / classes 形态）：
        //    "dotnet.exe" "path\to\DesktopX.dll"
        string? dllPath = Assembly.GetEntryAssembly()?.Location;
        if (dllPath != null && File.Exists(dllPath))
        {
            // 尝试从运行的环境推导 dotnet.exe（一般位于 %ProgramFiles%\dotnet\dotnet.exe）
            string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            string? dotnetExe = null;
            if (dotnetRoot != null)
            {
                string candidate = Path.Combine(dotnetRoot, "dotnet.exe");
                if (File.Exists(candidate)) dotnetExe = candidate;
            }
            if (dotnetExe == null)
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string candidate = Path.Combine(programFiles, "dotnet", "dotnet.exe");
                if (File.Exists(candidate)) dotnetExe = candidate;
            }
            if (dotnetExe != null)
            {
                Console.WriteLine("检测到 dll: " + dllPath);
                return "\"" + dotnetExe + "\" \"" + dllPath + "\"";
            }
        }

        return null;
    }
}