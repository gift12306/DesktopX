using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace DesktopX.Utils;

/// <summary>
/// 系统图标获取，对应 Java 的 IconUtils：
/// 1. 优先尝试 HiRes（Jumbo 256），裁掉透明边；
/// 2. 回退 Shell 系统图标（SHGetFileInfo + SHGFI_ICON），同样裁切；
/// 3. 按绝对路径缓存（.lnk 每文件图标不同）。
/// </summary>
internal static class IconUtils
{
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

    /// <summary>key = 文件绝对路径，缓存图标</summary>
    private static readonly Dictionary<string, Bitmap> Cache = new();

    public static Bitmap? GetSystemIcon(string path)
    {
        string key = Path.GetFullPath(path);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        Bitmap? img = null;

        // 1. 尝试 Jumbo 256x256（HiResIconExtractor 内部已降级到 ExtraLarge/Large）
        Bitmap? buf = HighResIconExtractor.Extract(key);
        if (buf != null)
        {
            img = TrimTransparentBorder(buf);   // 裁掉透明边
        }

        // 2. 回退 Shell 系统图标（保证可用）
        if (img == null)
        {
            try
            {
                var r = NativeMethods.SHGetFileInfo(key, 0, out var info,
                    (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
                if (r != IntPtr.Zero && info.hIcon != IntPtr.Zero)
                {
                    using (var icon = Icon.FromHandle(info.hIcon))
                    {
                        using var bi = new Bitmap(icon.ToBitmap());
                        img = TrimTransparentBorder(bi);
                    }
                    NativeMethods.DestroyIcon(info.hIcon);
                }
            }
            catch (Exception)
            {
                // 兜底失败时忽略，img 保持 null
            }
        }

        if (img != null) Cache[key] = img;
        return img;
    }

    public static void ClearCache() => Cache.Clear();

    /// <summary>
    /// 裁掉四周全透明的边。
    /// 用于处理"假 Jumbo"：256×256 画布上只有左上角一小块有像素。
    /// 如果有效区域几乎占满整张图（&gt;= 90% 宽高），就不裁。
    /// </summary>
    private static Bitmap TrimTransparentBorder(Bitmap img)
    {
        int w = img.Width;
        int h = img.Height;
        if (w <= 0 || h <= 0) return img;

        int minX = w, minY = h, maxX = -1, maxY = -1;

        var data = img.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            byte[] row = new byte[Math.Abs(stride)];
            for (int y = 0; y < h; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * stride), row, 0, Math.Abs(stride));
                for (int x = 0; x < w; x++)
                {
                    int alpha = row[x * 4 + 3];   // BGRA
                    if (alpha > 16)               // 忽略很淡的像素（抗锯齿边缘）
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
        }
        finally
        {
            img.UnlockBits(data);
        }

        // 全透明或有效区域占满 90% 以上 → 不裁
        if (maxX < 0) return img;
        int cw = maxX - minX + 1;
        int ch = maxY - minY + 1;
        if (cw >= w * 0.9 && ch >= h * 0.9) return img;

        // 裁切（复制绘制，避免共享数据问题）
        var result = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.DrawImage(img,
                new Rectangle(0, 0, cw, ch),
                new Rectangle(minX, minY, cw, ch),
                GraphicsUnit.Pixel);
        }
        return result;
    }
}