using System;
using System.Windows.Forms;
using DesktopX.Services;
using DesktopX.UI;
using DesktopX.Utils;

namespace DesktopX;

public class DesktopOverlayApp
{
    private ConfigManager _configManager = null!;
    private DesktopForm _desktopForm = null!;
    private SettingsForm _settingsForm = null!;

    public void Run(bool openSettings = false)
    {
        // 1. 配置加载
        _configManager = new ConfigManager();
        _configManager.Load();

        // 2. 创建窗口
        _desktopForm = new DesktopForm(_configManager);
        _settingsForm = new SettingsForm(_configManager, _desktopForm.Reload);

        // 3. 注入设置回调
        _desktopForm.OnSettingsRequested = _settingsForm.ShowSettings;

        // 4. 注册右键菜单
        try
        {
            DesktopContextMenuManager.AddContextMenu();
        }
        catch (Exception ex)
        {
            Console.WriteLine("右键菜单注册失败: " + ex.Message);
        }

        // 5. 首次运行处理
        bool isFirstRun = !DesktopShortcutManager.IsFirstRunDone();
        DesktopShortcutManager.EnsureFirstRunShortcut(); // 内部包含 MarkFirstRunDone

        // 6. 处理首次启动时弹出设置窗口
        if (isFirstRun || openSettings||1==1)
        {
            // ★ 在 Application.Idle 中延迟弹出设置窗口，确保主桌面层已完全就绪
            EventHandler? showSettingsOnIdle = null;
            showSettingsOnIdle = (s, e) =>
            {
                Application.Idle -= showSettingsOnIdle;
                _settingsForm.ShowSettings();
            };
            Application.Idle += showSettingsOnIdle;
        }

        // 7. 启动主消息循环（阻塞，直到关闭）
        // ★ 必须把 _desktopForm 传进去，WinForms 才会正确处理消息泵
        Application.Run(_desktopForm);
    }
}