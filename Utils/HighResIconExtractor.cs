using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DesktopX.Utils;

/// <summary>
/// 高分辨率图标提取（Jumbo 256 / ExtraLarge 64 / Large 32），对应 Java 的 HiResIconExtractor。
/// 通过 SHGetFileInfo 拿索引 → SHGetImageList 拿 ImageList → IImageList::GetIcon（vtable 索引 10）→ HICON → Bitmap。
/// </summary>
internal static class HighResIconExtractor
{
    private const uint SHGFI_SYSICONINDEX = 0x4000;
    private const int SHIL_LARGE = 0;
    private const int SHIL_EXTRALARGE = 2;
    private const int SHIL_JUMBO = 4;
    private const int ILD_TRANSPARENT = 0x1;

    private static readonly Guid IImageListGuid = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    public static Bitmap? Extract(string path)
    {
        try
        {
            // 1. 获取系统图标索引
            var result = NativeMethods.SHGetFileInfo(path, 0, out var info,
                (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(), SHGFI_SYSICONINDEX);
            if (result == IntPtr.Zero) return null;
            int iconIndex = info.iIcon;

            // 2. 逐级尝试 Jumbo → ExtraLarge → Large
            IntPtr imageList = IntPtr.Zero;
            Guid iid = IImageListGuid;
            foreach (int shil in new[] { SHIL_JUMBO, SHIL_EXTRALARGE, SHIL_LARGE })
            {
                int hr = NativeMethods.SHGetImageList(shil, ref iid, out IntPtr ppv);
                if (hr == 0 && ppv != IntPtr.Zero)
                {
                    imageList = ppv;
                    break;
                }
            }
            if (imageList == IntPtr.Zero) return null;

            // 3. IImageList::GetIcon（COM 接口方法声明顺序即 vtable，GetIcon 位于声明第 8 个 → vtable 索引 10）
            var il = (NativeMethods.IImageList)Marshal.GetTypedObjectForIUnknown(imageList, typeof(NativeMethods.IImageList));
            if (il.GetIcon(iconIndex, ILD_TRANSPARENT, out IntPtr hIcon) != 0 || hIcon == IntPtr.Zero) return null;

            // 4. HICON -> Bitmap
            return HIconToImage(hIcon);
        }
        catch (Exception t)
        {
            Console.Error.WriteLine("图标提取失败 " + path + " -> " + t.Message);
            return null;
        }
    }

    private static Bitmap? HIconToImage(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero) return null;

        IntPtr hdc = IntPtr.Zero;
        IntPtr oldBmp = IntPtr.Zero;
        NativeMethods.ICONINFO iconInfo = default;

        try
        {
            if (!NativeMethods.GetIconInfo(hIcon, out iconInfo))
            {
                Console.Error.WriteLine("GetIconInfo 失败");
                return null;
            }

            // 单色图标 fallback：hbmColor 可能为 NULL，此时改用 hbmMask（复刻 Java）
            IntPtr colorBmp = iconInfo.hbmColor;
            bool monochrome = colorBmp == IntPtr.Zero;
            if (monochrome) colorBmp = iconInfo.hbmMask;
            if (colorBmp == IntPtr.Zero)
            {
                Console.Error.WriteLine("hbmColor 和 hbmMask 均为 NULL");
                return null;
            }

            var bmp = new NativeMethods.BITMAP();
            IntPtr pBmp = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.BITMAP>());
            try
            {
                int got = NativeMethods.GetObject(colorBmp, Marshal.SizeOf<NativeMethods.BITMAP>(), pBmp);
                if (got == 0)
                {
                    Console.Error.WriteLine("GetObject 返回 0，句柄无效");
                    return null;
                }
                bmp = Marshal.PtrToStructure<NativeMethods.BITMAP>(pBmp);
            }
            finally
            {
                Marshal.FreeHGlobal(pBmp);
            }

            int width = bmp.bmWidth;
            int height = bmp.bmHeight;
            Console.WriteLine("HIconToImage 位图尺寸: " + width + "x" + height + (monochrome ? " (单色)" : ""));
            if (width <= 0 || height <= 0)
            {
                Console.Error.WriteLine("位图尺寸无效: " + width + "x" + height);
                return null;
            }

            hdc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
            oldBmp = NativeMethods.SelectObject(hdc, colorBmp);

            var bmi = new NativeMethods.BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>();
            bmi.bmiHeader.biWidth = width;
            bmi.bmiHeader.biHeight = -height;   // 负数 = top-down
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0;    // BI_RGB

            byte[] pixels = new byte[width * height * 4];
            IntPtr pPixels = Marshal.AllocHGlobal(pixels.Length);
            try
            {
                int rows = NativeMethods.GetDIBits(hdc, colorBmp, 0, (uint)height,
                    pPixels, ref bmi, 0 /* DIB_RGB_COLORS */);
                if (rows == 0)
                {
                    Console.Error.WriteLine("GetDIBits 返回 0");
                    return null;
                }
                Marshal.Copy(pPixels, pixels, 0, pixels.Length);

                var img = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                var data = img.LockBits(new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
                }
                finally
                {
                    img.UnlockBits(data);
                }
                return img;
            }
            finally
            {
                Marshal.FreeHGlobal(pPixels);
            }
        }
        finally
        {
            // 与 Java 一致：统一释放 DC、位图对象与图标句柄
            if (hdc != IntPtr.Zero)
            {
                if (oldBmp != IntPtr.Zero) NativeMethods.SelectObject(hdc, oldBmp);
                NativeMethods.DeleteDC(hdc);
            }
            if (iconInfo.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(iconInfo.hbmMask);
            NativeMethods.DestroyIcon(hIcon);
        }
    }
}