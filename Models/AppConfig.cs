using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DesktopX.Models;

/// <summary>应用配置，对应 Java 的 AppConfig</summary>
public class AppConfig
{
    public List<BlockConfig> Blocks { get; set; } = new();

    public int IconSize { get; set; } = 48;

    public int BlockOpacity { get; set; } = 35;

    /// <summary>
    /// 水平间距（不受 JSON 读写影响，复刻 Java 缺陷 setHSpacing：
    /// Java 的 setHSpacing(int) 返回 int 且不赋值，Jackson 反序列化/设置时值被丢弃，hSpacing 恒为 -1）。
    /// </summary>
    [JsonIgnore]
    public int HSpacing { get; set; } = -1;

    public int VSpacing { get; set; } = -1;

    /// <summary>旧版单值间距，仅用于迁移兼容，新代码读取时作为兜底</summary>
    public int Spacing { get; set; } = 12;

    /// <summary>
    /// JSON 层面的 hSpacing 读写接口：写入时输出 getHSpacing()（恒 12），读取时吞掉（保持 -1）。
    /// 与 Jackson 对 Java setHSpacing 缺陷方法的行为保持一致。
    /// </summary>
    [JsonPropertyName("hSpacing")]
    public int HSpacingWire
    {
        get => EffectiveHSpacing;
        set { _ = value; }   // 复刻 Java setHSpacing 缺陷：不赋值
    }

    /// <summary>水平间距：新值优先，其次旧单值，最后默认 12（复刻 Java getHSpacing）</summary>
    public int EffectiveHSpacing => HSpacing >= 0 ? HSpacing : (Spacing >= 0 ? Spacing : 12);

    /// <summary>垂直间距：新值优先，其次旧单值，最后默认 12（复刻 Java getVSpacing）</summary>
    public int EffectiveVSpacing => VSpacing >= 0 ? VSpacing : (Spacing >= 0 ? Spacing : 12);

    // ★ 新增：字体大小，默认 12px
    public int FontSize { get; set; } = 12; 

}