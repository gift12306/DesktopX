using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DesktopX.Models;

/// <summary>区块配置，对应 Java 的 BlockConfig</summary>
public class BlockConfig
{
    public string? Name { get; set; }

    /// <summary>新区段：一个区块对应多个文件夹</summary>
    private List<string>? _folders;

    /// <summary>旧版兼容字段：单个文件夹</summary>
    public string? FolderPath { get; set; }

    /// <summary>当前显示的是第几个文件夹</summary>
    public int ActiveIndex { get; set; }

    public double X { get; set; } = 40;
    public double Y { get; set; } = 40;
    public double Width { get; set; } = 400;
    public double Height { get; set; } = 300;
      /// <summary>是否锁定（锁定后无法拖动、无法缩放）</summary>
    public bool IsLocked { get; set; } = false; 

    public BlockConfig() { }

    public BlockConfig(string name, string folderPath)
    {
        Name = name;
        _folders = new List<string>();
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            _folders.Add(folderPath);
        }
    }

    /// <summary>读取时兼容旧 config：folders 为空则用 folderPath 兜底（复刻 Java getFolders）</summary>
    public List<string> Folders
    {
        get
        {
            _folders ??= new List<string>();
            if (_folders.Count == 0 && !string.IsNullOrWhiteSpace(FolderPath))
            {
                _folders.Add(FolderPath);
            }
            return _folders;
        }
        set => _folders = value ?? new List<string>();
    }

    /// <summary>派生属性，不参与 JSON 序列化（对应 Java @JsonIgnore getActiveFolder）</summary>
    [JsonIgnore]
    public string? ActiveFolder
    {
        get
        {
            var list = Folders;
            if (list.Count == 0) return null;
            if (ActiveIndex < 0 || ActiveIndex >= list.Count) ActiveIndex = 0;
            return list[ActiveIndex];
        }
    }
}