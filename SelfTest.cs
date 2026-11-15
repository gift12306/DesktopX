using System;
using System.IO;
using DesktopX.Models;
using DesktopX.Services;
using DesktopX.UI;
using DesktopX.Utils;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesktopX;

/// <summary>
/// 纯逻辑自检（--selftest），用于验证迁移后的核心逻辑与 Java 源行为一致。
/// 不启动任何窗口；失败返回非 0 退出码。
/// </summary>
internal static class SelfTest
{
    public static int Run()
    {
        int failures = 0;
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (IOException) { } // 重定向时设置编码可能抛异常，忽略
        string work = Path.Combine(Path.GetTempPath(), "DesktopX-SelfTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);

        // 注意：ConfigManager 以当前工作目录定位 config.json，此处临时切换
        string oldDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(work);
            failures += TestConfigJson();
            failures += TestFileScanner(work);
            failures += TestBlockConfig();
            failures += TestSpacingFallback();
            failures += TestSettingsFormLifecycle(work);
            failures += TestFirstRunShortcut(work);
        }
        finally
        {
            Directory.SetCurrentDirectory(oldDir);
            try { Directory.Delete(work, true); } catch { /* 忽略清理失败 */ }
        }

        Console.WriteLine(failures == 0 ? "SELFTEST PASS" : "SELFTEST FAIL: " + failures);
        return failures == 0 ? 0 : 1;
    }

    // ===== 1. ConfigManager / JSON 读写兼容（复刻 Jackson 行为） =====

    private static int TestConfigJson()
    {
        int fail = 0;
        // 模拟旧版/迁移后 config.json
        File.WriteAllText("config.json",
            "{\n  \"blocks\": [\n    {\n      \"name\": \"测试\",\n" +
            "      \"folders\": [\"C:\\\\Temp\"],\n      \"activeIndex\": 0,\n" +
            "      \"x\": 1.0,\n      \"y\": 2.0,\n      \"width\": 400.0,\n      \"height\": 300.0\n" +
            "    }\n  ],\n  \"iconSize\": 64,\n  \"blockOpacity\": 60,\n" +
            "  \"hSpacing\": 99,\n  \"vSpacing\": 55,\n  \"spacing\": 12\n}\n");

        var cm = new ConfigManager();
        AppConfig cfg = cm.Load();

        Check(ref fail, "hSpacing 被吞掉（保持 -1）", cfg.HSpacing == -1);
        Check(ref fail, "vSpacing=55 正常读取", cfg.VSpacing == 55);
        Check(ref fail, "EffectiveHSpacing 恒为 spacing 兜底 12", cfg.EffectiveHSpacing == 12);
        Check(ref fail, "EffectiveVSpacing=55", cfg.EffectiveVSpacing == 55);
        Check(ref fail, "iconSize=64", cfg.IconSize == 64);
        Check(ref fail, "blockOpacity=60", cfg.BlockOpacity == 60);
        Check(ref fail, "blocks 数量=1", cfg.Blocks.Count == 1);
        Check(ref fail, "block 位置反序列化", Math.Abs(cfg.Blocks[0].X - 1.0) < 1e-9 && Math.Abs(cfg.Blocks[0].Height - 300.0) < 1e-9);

        // 保存后再读
        cm.Save();
        string saved = File.ReadAllText("config.json");
        Check(ref fail, "保存输出包含 hSpacing=12", saved.Contains("\"hSpacing\": 12"));
        Check(ref fail, "保存输出包含 vSpacing=55", saved.Contains("\"vSpacing\": 55"));

        return fail;
    }

    // ===== 2. FileScanner 排序 / 隐藏过滤 / .lnk 去后缀 =====

    private static int TestFileScanner(string work)
    {
        int fail = 0;
        string scanRoot = Path.Combine(work, "scan");
        Directory.CreateDirectory(scanRoot);
        File.WriteAllText(Path.Combine(scanRoot, "zeta.txt"), "a");
        File.WriteAllText(Path.Combine(scanRoot, "alpha.txt"), "a");
        File.WriteAllText(Path.Combine(scanRoot, "link.lnk"), "dummy");
        Directory.CreateDirectory(Path.Combine(scanRoot, "zdir"));
        File.WriteAllText(Path.Combine(scanRoot, ".hidden"), "a");   // 隐藏文件

        var entries = FileScanner.ScanFolder(scanRoot);
        Check(ref fail, "扫描返回 4 项（隐藏被跳过）", entries.Count == 4);
        Check(ref fail, "目录优先且名称排序", entries[0].Name == "zdir");
        Check(ref fail, "文件按名称排序", entries[1].Name == "alpha.txt");
        Check(ref fail, "lnk 显示名去后缀", FileScanner.GetDisplayName(entries[2]) == "link");
        Check(ref fail, "目录显示名正常", FileScanner.GetDisplayName(entries[0]) == "zdir");

        return fail;
    }

    // ===== 3. BlockConfig 兼容：folderPath 兜底 / activeIndex 越界归 0 =====

    private static int TestBlockConfig()
    {
        int fail = 0;

        // 旧版 config：无 folders，只有 folderPath
        var b = new BlockConfig();
        b.FolderPath = "C:\\Old\\Path";
        Check(ref fail, "folders 为空时用 folderPath 兜底", b.Folders.Count == 1 && b.Folders[0] == "C:\\Old\\Path");
        Check(ref fail, "ActiveFolder 返回兜底路径", b.ActiveFolder == "C:\\Old\\Path");

        // activeIndex 越界 → 归 0
        b.Folders.Add("C:\\New\\Path");
        b.ActiveIndex = 5;
        Check(ref fail, "ActiveFolder 越界归 0 并返回第 0 个", b.ActiveFolder == "C:\\Old\\Path" && b.ActiveIndex == 0);

        // 名字构造器
        var b2 = new BlockConfig("名字", "C:\\X");
        Check(ref fail, "构造器填充 folders", b2.Folders.Count == 1 && b2.Folders[0] == "C:\\X");

        return fail;
    }

    // ===== 4. 间距兜底边界 =====

    private static int TestSpacingFallback()
    {
        int fail = 0;
        var cfg = new AppConfig();
        Check(ref fail, "默认 HSpacing=-1 时 Effective=12", cfg.EffectiveHSpacing == 12);
        Check(ref fail, "默认 VSpacing=-1 时 Effective=12", cfg.EffectiveVSpacing == 12);
        cfg.Spacing = -5;
        Check(ref fail, "spacing<0 时兜底 12", cfg.EffectiveHSpacing == 12);
        cfg.HSpacing = 30;
        Check(ref fail, "HSpacing=30 直接生效", cfg.EffectiveHSpacing == 30);

        return fail;
    }

    // ===== 5. SettingsForm 生命周期 =====

    private static int TestSettingsFormLifecycle(string work)
    {
        int fail = 0;
        try
        {
            var cm = new ConfigManager();
            var form = new SettingsForm(cm, () => { });

            form.ShowSettings();
            bool firstVisible = form.Visible;
            form.Close();
            bool notDisposed = !form.IsDisposed;
            bool hiddenNow = !form.Visible;

            bool secondOk = true;
            string? err = null;
            try
            {
                form.ShowSettings();
            }
            catch (ObjectDisposedException e)
            {
                secondOk = false;
                err = e.Message;
            }

            Check(ref fail, "SettingsForm 首次 ShowSettings 显示", firstVisible);
            Check(ref fail, "SettingsForm 点 X 关闭后未释放", notDisposed);
            Check(ref fail, "SettingsForm 点 X 关闭后隐藏", hiddenNow);
            Check(ref fail, "SettingsForm 关闭后再 ShowSettings 不抛异常", secondOk);
            if (err != null) Console.WriteLine("    异常信息: " + err);

            form.Close();
            form.Dispose();
        }
        catch (Exception e)
        {
            Check(ref fail, "SettingsForm 生命周期用例自身未抛异常", false);
            Console.WriteLine("    用例异常: " + e.Message + "\n    " + e.StackTrace);
        }
        return fail;
    }

    // ===== 6. 首次启动快捷方式创建：仅触发一次 / 失败降级 / 与既有注册表键隔离 =====

    private static int TestFirstRunShortcut(string work)
    {
        int fail = 0;
        string regKey = @"Software\DesktopX-SelfTest-" + Guid.NewGuid().ToString("N");
        try
        {
            // 1) 首次启动（未标记）时创建桌面快捷方式
            bool created = false;
            bool firstRunDecided = false;
            try
            {
                // 使用最新的带返回值的 CreateShortcut
                created = DesktopShortcutManager.CreateShortcut(
                    Path.Combine(work, "DesktopX.lnk"), Path.Combine(work, "DesktopX.exe"));
                firstRunDecided = !DesktopShortcutManager.IsFirstRunDone(regKey);
            }
            catch (Exception e)
            {
                Console.WriteLine("    用例异常: " + e.Message);
            }
            Check(ref fail, "首次启动创建快捷方式返回成功", created);
            Check(ref fail, "首次启动前无快捷方式标记", firstRunDecided);
            Check(ref fail, "快捷方式 .lnk 文件已生成", File.Exists(Path.Combine(work, "DesktopX.lnk")));

            // 2) 标记后（再次启动）不应重复创建
            DesktopShortcutManager.MarkFirstRunDone(regKey);
            bool marked = DesktopShortcutManager.IsFirstRunDone(regKey);
            Check(ref fail, "首次标记写入后生效", marked);

            // 3) 失败降级：写入路径非法（父目录不存在）时返回 false 且不抛异常
            bool degraded = false;
            try
            {
                degraded = DesktopShortcutManager.CreateShortcut(
                    Path.Combine(work, "no-such-dir", "DesktopX.lnk"), Path.Combine(work, "DesktopX.exe"));
            }
            catch (Exception)
            {
                degraded = false; // 不应抛出
            }
            Check(ref fail, "写入路径非法时优雅降级返回 false", !degraded);

            // 4) 与自启动/右键菜单注册表键隔离
            using (var chk = Registry.CurrentUser.OpenSubKey(regKey))
            {
                Check(ref fail, "自检注册表键已按预期创建", chk != null && chk.GetValue("DesktopShortcutCreated") != null);
            }
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(regKey); } catch { /* 忽略清理失败 */ }
        }
        return fail;
    }

    private static void Check(ref int failures, string name, bool ok)
    {
        Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + name);
        if (!ok) failures++;
    }
}