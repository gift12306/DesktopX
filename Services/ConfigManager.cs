using System;
using System.IO;
using System.Text.Json;
using DesktopX.Models;

namespace DesktopX.Services;

/// <summary>配置读写，对应 Java 的 ConfigManager（config.json 位于 exe 所在目录）</summary>
public class ConfigManager
{
    // ★ 核心修复：使用 AppDomain.CurrentDomain.BaseDirectory 确保定位在 exe 所在文件夹
    private static readonly string ConfigFile =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private AppConfig? _config;

    public AppConfig Load()
    {
        if (_config != null) return _config;
        try
        {
            if (File.Exists(ConfigFile))
            {
                string json = File.ReadAllText(ConfigFile);
                _config = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions) ?? new AppConfig();
            }
            else
            {
                _config = new AppConfig();
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("读取配置失败: " + e.Message);
            _config = new AppConfig();
        }
        return _config;
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(Load(), _jsonOptions);
            File.WriteAllText(ConfigFile, json);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("保存配置失败: " + e.Message);
        }
    }
}