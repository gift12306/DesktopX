using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DesktopX.Models;
using DesktopX.Services;
using DesktopX.Utils;

namespace DesktopX.UI;

/// <summary>
/// 桌面层主窗口，对应 Java 的 DesktopWindow（Stage）+ BlockView。
/// 纯自绘透明窗口，通过 UpdateLayeredWindow 提交像素。
/// </summary>
public class DesktopForm : Form
{
    // ---- 吸附参数 ----
    private const double SnapThreshold = 3;
    private const double SnapGap = 5;

    // ---- 点击判定阈值 ----
    private const double ClickSlop = 5;

    private readonly ConfigManager _configManager;
    private readonly List<BlockView> _blocks = new();

    private readonly ContextMenuStrip _desktopMenu;

    // 交互状态
    private BlockView? _dragBlock;
    private double _dragStartX, _dragStartY, _dragStartLayoutX, _dragStartLayoutY;
    private bool _dragging;
    private bool _movedSincePress;

    private BlockView? _resizeBlock;
    private double _resizeStartX, _resizeStartY, _resizeStartW, _resizeStartH;
    private bool _resizing;

    private BlockView? _configBtnBlock;
    private bool _configBtnPressed;

    private BlockView? _tabBlock;
    private int _tabIndex;

    // 悬停高亮
    private BlockView? _hoverBlock;
    private int _hoverIcon = -1;
    private string? _tooltipText;
    private Point? _mousePos;

    // 拖拽文件状态
    private BlockView.IconItem? _dragIconItem;
    private bool _isDraggingFile;

    // 画布尺寸
    private double _canvasW, _canvasH;

    public Action? OnSettingsRequested { get; set; }

    private readonly int _createExStyle;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= _createExStyle;
            return cp;
        }
    }

    // ===== Win+D 拦截相关常量与结构 =====
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_SHOWDESKTOP = 0xF140;
    private const int WM_WINDOWPOSCHANGING = 0x0046;

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    public DesktopForm(ConfigManager configManager)
    {
        _configManager = configManager;

        FormBorderStyle = FormBorderStyle.None;
        TopMost = false;
        Text = "DesktopX_" + Guid.NewGuid();
        ShowInTaskbar = false;

        // 窗口创建时即带样式：分层透明、隐藏任务栏、不抢焦点
        const int WS_EX_LAYERED = 0x00080000;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_NOACTIVATE = 0x08000000;
        _createExStyle = WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;

        var b = Screen.PrimaryScreen!.WorkingArea;
        StartPosition = FormStartPosition.Manual;
        Location = b.Location;
        Size = b.Size;

        // 全局右键菜单
        _desktopMenu = new ContextMenuStrip();
        var openSettings = new ToolStripMenuItem("打开配置界面");
        openSettings.Click += (_, _) => OnSettingsRequested?.Invoke();
        _desktopMenu.Items.Add(openSettings);

        MouseDown += OnCanvasMouseDown;
        MouseMove += OnCanvasMouseMove;
        MouseUp += OnCanvasMouseUp;
        MouseWheel += OnCanvasMouseWheel;

        // 开启拖入支持
        AllowDrop = true;
        DragEnter += OnFormDragEnter;
        DragDrop += OnFormDragDrop;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        DesktopLayerBinder.Bind(this);
        Reload();
    }

    /// <summary>拦截系统消息，阻止 Win+D 最小化窗口</summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_SYSCOMMAND && m.WParam.ToInt32() == SC_SHOWDESKTOP)
        {
            return; // 拦截显示桌面
        }

        if (m.Msg == WM_WINDOWPOSCHANGING)
        {
            var pos = (WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(WINDOWPOS))!;
            if (pos.x == -32000 || pos.y == -32000) // 防止被移到屏幕外
            {
                pos.flags |= 0x0002 | 0x0001; // SWP_NOMOVE | SWP_NOSIZE
                Marshal.StructureToPtr(pos, m.LParam, false);
            }
        }

        base.WndProc(ref m);
    }

    // ==================== 渲染 ====================

    public void Reload()
    {
        var config = _configManager.Load();
        _blocks.Clear();
        _hoverBlock = null;
        _hoverIcon = -1;
        _tooltipText = null;

        double maxRight = 0, maxBottom = 0;
        foreach (var block in config.Blocks)
        {
            var view = new BlockView(block, config, this);
            _blocks.Add(view);
            maxRight = Math.Max(maxRight, block.X + block.Width);
            maxBottom = Math.Max(maxBottom, block.Y + block.Height);
        }
        _canvasW = Math.Max(maxRight + 200, ClientSize.Width);
        _canvasH = Math.Max(maxBottom + 200, ClientSize.Height);

        Render();
    }

    private void Render()
    {
        if (!IsHandleCreated || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

        int w = ClientSize.Width, h = ClientSize.Height;

        var bmi = new NativeMethods.BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = w;
        bmi.bmiHeader.biHeight = -h;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0;

        IntPtr screenDC = NativeMethods.GetDC(IntPtr.Zero);
        IntPtr memDC = NativeMethods.CreateCompatibleDC(screenDC);
        IntPtr dib = NativeMethods.CreateDIBSection(memDC, ref bmi, 0, out _, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero)
        {
            NativeMethods.DeleteDC(memDC);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDC);
            return;
        }
        IntPtr old = NativeMethods.SelectObject(memDC, dib);

        using (var g = Graphics.FromHdc(memDC))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.CompositingMode = CompositingMode.SourceOver;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);

            foreach (var bv in _blocks) bv.Draw(g);

            if (_tooltipText != null && _mousePos != null)
                DrawTooltip(g, _mousePos.Value, _tooltipText);
        }

        var ptDst = new NativeMethods.POINT(Location.X, Location.Y);
        var ptSrc = new NativeMethods.POINT(0, 0);
        var size = new NativeMethods.SIZE(w, h);
        var blend = new NativeMethods.BLENDFUNCTION
        {
            BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1
        };

        NativeMethods.UpdateLayeredWindow(Handle, screenDC, ref ptDst, ref size, memDC, ref ptSrc, 0, ref blend, 2);

        NativeMethods.SelectObject(memDC, old);
        NativeMethods.DeleteObject(dib);
        NativeMethods.DeleteDC(memDC);
        NativeMethods.ReleaseDC(IntPtr.Zero, screenDC);
    }

    private void DrawTooltip(Graphics g, Point mouse, string text)
    {
        using var font = new Font("HarmonyOS Sans SC", 11f, GraphicsUnit.Pixel);
        var sz = g.MeasureString(text, font);
        float x = mouse.X + 12, y = mouse.Y + 12;
        const float pad = 6;
        using var bg = new SolidBrush(Color.FromArgb(230, 20, 20, 20));
        using var border = new Pen(Color.FromArgb(120, 255, 255, 255));
        using var textBrush = new SolidBrush(Color.White);
        var rect = new RectangleF(x, y, sz.Width + pad * 2, sz.Height + pad * 2);
        g.FillRectangle(bg, rect);
        g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
        g.DrawString(text, font, textBrush, x + pad, y + pad);
    }

    // ==================== 鼠标事件 ====================

    private void OnCanvasMouseDown(object? sender, MouseEventArgs e)
    {
        _mousePos = e.Location;
        ClearHover();

        // 右键菜单
        if (e.Button == MouseButtons.Right)
        {
            BlockView? hit = HitTestBlock(e.X, e.Y);
            int iconIdx = hit?.HitTestIcon(e.X, e.Y) ?? -1;
            
            if (hit != null && iconIdx >= 0)
            {
                NativeMethods.SetForegroundWindow(this.Handle);
                var item = hit.Icons[iconIdx];
                var fileMenu = new ContextMenuStrip();

                var openItem = new ToolStripMenuItem("打开");
                openItem.Font = new Font(fileMenu.Font, FontStyle.Bold);
                openItem.Click += (_, _) => FileScanner.OpenFile(item.Info);
                
                var openWithItem = new ToolStripMenuItem("打开方式...");
                openWithItem.Click += (_, _) => RunShellCommand("rundll32.exe", $"shell32.dll,OpenAs_RunDLL \"{item.Path}\"");
                
                var copyPathItem = new ToolStripMenuItem("复制文件地址");
                copyPathItem.Click += (_, _) => Clipboard.SetText(item.Path);
                
                var showInExplorerItem = new ToolStripMenuItem("在资源管理器中显示");
                showInExplorerItem.Click += (_, _) => RunShellCommand("explorer.exe", $"/select,\"{item.Path}\"");

                var renameItem = new ToolStripMenuItem("重命名");
                renameItem.Click += (_, _) => OnRenameFile(item, hit);

                var deleteItem = new ToolStripMenuItem("删除");
                deleteItem.ForeColor = Color.Red;
                deleteItem.Click += (_, _) => {
                    if (MessageBox.Show($"确定要将 '{item.DisplayName}' 放入回收站吗？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.Path, 
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, 
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        hit.RebuildIcons();
                        Render();
                    }
                };

                var propertiesItem = new ToolStripMenuItem("属性");
                propertiesItem.Click += (_, _) => RunShellCommand("rundll32.exe", $"shell32.dll,ShellExec_RunDLL \"{item.Path}\"");

                fileMenu.Items.AddRange(new ToolStripItem[] {
                    openItem, openWithItem, new ToolStripSeparator(),
                    copyPathItem, showInExplorerItem, new ToolStripSeparator(),
                    renameItem, deleteItem, new ToolStripSeparator(),
                    propertiesItem
                });

                fileMenu.Show(this, e.Location);
            }
            else
            {
                NativeMethods.SetForegroundWindow(this.Handle);
                _desktopMenu.Show(this, e.Location);
            }
            return;
        }

        // 左键逻辑
        if (e.Button != MouseButtons.Left) return;

        BlockView? hitBlock = HitTestBlock(e.X, e.Y);
        if (hitBlock == null) return;
// 2. 锁定按钮 🔒/🔓
        if (hitBlock.IsOnLockButton(e.X, e.Y))
        {
            hitBlock.Config.IsLocked = !hitBlock.Config.IsLocked;
            _configManager.Save(); // 实时保存状态
            Render();              // 立即重绘
            return;                // 拦截本次点击，不触发拖动
        }
        // 1. 缩放柄
        if (hitBlock.IsOnResizeHandle(e.X, e.Y))
        {
            _resizeBlock = hitBlock;
            _resizing = true;
            _resizeStartX = e.X; _resizeStartY = e.Y;
            _resizeStartW = hitBlock.Config.Width; _resizeStartH = hitBlock.Config.Height;
            this.Cursor = Cursors.SizeNWSE; // ★ 按下时立即改变光标
             return;
        }
        

       

        
        // 2. 配置按钮 ⚙
        if (hitBlock.IsOnConfigButton(e.X, e.Y))
        {
            _configBtnBlock = hitBlock;
            _configBtnPressed = true;
            return;
        }

        // 3. 文件夹 Tab
        int tabIdx = hitBlock.HitTestTab(e.X, e.Y);
        if (tabIdx >= 0)
        {
            _tabBlock = hitBlock;
            _tabIndex = tabIdx;
            return;
        }
 // 如果区块已锁定，则直接返回（阻止缩放、拖动，但允许点击图标打开文件）
        if (hitBlock.Config.IsLocked)
        {
            // 如果点击了图标，依然允许打开
            int icon = hitBlock.HitTestIcon(e.X, e.Y);
            if (icon >= 0)
            {
                FileScanner.OpenFile(hitBlock.Icons[icon].Info);
            }
            return; 
        }
        // 4. 拖动区块
        _dragBlock = hitBlock;
        _dragging = true;
        _movedSincePress = false;
        _dragStartX = e.X; _dragStartY = e.Y;
        _dragStartLayoutX = hitBlock.Config.X; _dragStartLayoutY = hitBlock.Config.Y;
        
        // 记录按下的图标，用于拖拽判断
        int iconIdxLeft = hitBlock.HitTestIcon(e.X, e.Y);
        _dragIconItem = iconIdxLeft >= 0 ? hitBlock.Icons[iconIdxLeft] : null;
    }

     private void OnCanvasMouseMove(object? sender, MouseEventArgs e)
    {
        _mousePos = e.Location;

        // 1. 拖动区块 / 拖出文件
        if (_dragging && _dragBlock != null)
        {
            double dx = e.X - _dragStartX;
            double dy = e.Y - _dragStartY;
            if (Math.Abs(dx) > ClickSlop || Math.Abs(dy) > ClickSlop)
            {
                if (_dragIconItem != null && !_isDraggingFile)
                {
                    _isDraggingFile = true;
                    _dragging = false;
                    
                    this.Cursor = Cursors.Default; // 拖拽文件时恢复默认指针
                    var data = new DataObject(DataFormats.FileDrop, new string[] { _dragIconItem.Path });
                    DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move);

                    _isDraggingFile = false;
                    _dragIconItem = null;
                    _movedSincePress = false;
                    Render();
                    return; 
                }

                _movedSincePress = true;
                double newX = Math.Max(0, _dragStartLayoutX + dx);
                double newY = Math.Max(0, _dragStartLayoutY + dy);
                var snapped = ApplySnap(_dragBlock, newX, newY);
                _dragBlock.Config.X = snapped[0];
                _dragBlock.Config.Y = snapped[1];
            }
            Render();
            return;
        }

        // 2. 缩放区块
        if (_resizing && _resizeBlock != null)
        {
            this.Cursor = Cursors.SizeNWSE; // ★ 强制保持缩放光标

            double dw = e.X - _resizeStartX;
            double dh = e.Y - _resizeStartY;
            double newW = Math.Max(160, _resizeStartW + dw);
            double newH = Math.Max(120, _resizeStartH + dh);
            
            if (Math.Abs(_resizeBlock.Config.Width - newW) > 0.1 || Math.Abs(_resizeBlock.Config.Height - newH) > 0.1)
            {
                _resizeBlock.Config.Width = newW;
                _resizeBlock.Config.Height = newH;
                _resizeBlock.UpdateLayout(); 
                Render();
            }
            return;
        }

        // 3. 悬停检测与光标更新
        BlockView? hover = HitTestBlock(e.X, e.Y);
        
         // ★ 修改：如果在缩放柄上且区块未锁定，显示斜向双箭头；否则恢复默认
        if (hover != null && hover.IsOnResizeHandle(e.X, e.Y) && !hover.Config.IsLocked)
        {
            this.Cursor = Cursors.SizeNWSE; 
        }
        else
        {
            this.Cursor = Cursors.Default;
        }

        // 悬停高亮 + tooltip
        int hoverIcon = -1;
        string? tip = null;
        if (hover != null)
        {
            hoverIcon = hover.HitTestIcon(e.X, e.Y);
            if (hoverIcon >= 0)
            {
                var item = hover.Icons[hoverIcon];
                tip = (item.IsDirectory ? "文件夹：" : "文件：") + item.Path;
            }
        }
        if (!ReferenceEquals(hover, _hoverBlock) || hoverIcon != _hoverIcon ||
            (tip ?? string.Empty) != (_tooltipText ?? string.Empty))
        {
            _hoverBlock = hover;
            _hoverIcon = hoverIcon;
            _tooltipText = tip;
            Render();
        }
    }

    private void OnCanvasMouseUp(object? sender, MouseEventArgs e)
    {
        _mousePos = e.Location;

        if (e.Button == MouseButtons.Left)
        {
            if (_dragging && _dragBlock != null)
            {
                if (_movedSincePress)
                {
                    _dragBlock.Config.X = Math.Max(0, _dragBlock.Config.X);
                    _dragBlock.Config.Y = Math.Max(0, _dragBlock.Config.Y);
                    _configManager.Save();
                    OnBlockChanged(_dragBlock);
                }
                else if (!_movedSincePress && !_isDraggingFile)
                {
                    int icon = _dragBlock.HitTestIcon(e.X, e.Y);
                    if (icon >= 0) FileScanner.OpenFile(_dragBlock.Icons[icon].Info);
                }
                _dragging = false; _dragBlock = null;
            }

            if (_resizing && _resizeBlock != null)
            {
                _resizeBlock.Config.Width = Math.Max(160, _resizeBlock.Config.Width);
                _resizeBlock.Config.Height = Math.Max(120, _resizeBlock.Config.Height);
                _configManager.Save();
                OnBlockChanged(_resizeBlock);
                _resizing = false; _resizeBlock = null;
                this.Cursor = Cursors.Default; // ★ 松开后恢复默认光标
            }

            if (_configBtnPressed && _configBtnBlock != null)
            {
                if (_configBtnBlock.IsOnConfigButton(e.X, e.Y)) OnSettingsRequested?.Invoke();
                _configBtnPressed = false; _configBtnBlock = null;
            }

            if (_tabBlock != null)
            {
                int idx = _tabIndex;
                if (_tabBlock.HitTestTab(e.X, e.Y) == idx)
                {
                    _tabBlock.Config.ActiveIndex = idx;
                    _configManager.Save();
                    _tabBlock.RebuildIcons();
                    OnBlockChanged(_tabBlock);
                }
                _tabBlock = null;
            }

            _dragIconItem = null;
            _isDraggingFile = false;
        }
        
        Render();
        ClearHoverIfOutside();
    }

    private void OnCanvasMouseWheel(object? sender, MouseEventArgs e)
    {
        BlockView? hit = HitTestBlock(e.X, e.Y);
        if (hit != null)
        {
            int delta = (e.Delta > 0) ? -40 : 40;
            hit.ScrollBy(delta);
            Render();
        }
    }

    // ==================== 拖入文件 ====================

    private void OnFormDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effect = DragDropEffects.Copy;
        else
            e.Effect = DragDropEffects.None;
    }

    private void OnFormDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        string[] files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        Point clientPoint = PointToClient(new Point(e.X, e.Y));
        
        BlockView? targetBlock = HitTestBlock(clientPoint.X, clientPoint.Y);
        if (targetBlock == null || string.IsNullOrEmpty(targetBlock.Config.ActiveFolder)) return;

        string targetFolder = targetBlock.Config.ActiveFolder!;
        
        try
        {
            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file);
                string destPath = Path.Combine(targetFolder, fileName);
                if (File.Exists(destPath) || Directory.Exists(destPath)) continue;

                if (Directory.Exists(file))
                    Directory.CreateDirectory(destPath);
                else
                    File.Copy(file, destPath);
            }
            targetBlock.RebuildIcons();
            Render();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"拖入文件失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ==================== 辅助方法 ====================

    private void RunShellCommand(string fileName, string arguments)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName, Arguments = arguments, UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "执行命令失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnRenameFile(BlockView.IconItem item, BlockView blockView)
    {
        string? newName = ShowRenameDialog("重命名", item.DisplayName);
        if (string.IsNullOrWhiteSpace(newName) || newName == item.DisplayName) return;

        try
        {
            string dir = Path.GetDirectoryName(item.Path)!;
            string newPath = Path.Combine(dir, newName);

            if (File.Exists(newPath) || Directory.Exists(newPath))
            {
                MessageBox.Show("该名称已存在。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (item.IsDirectory) Directory.Move(item.Path, newPath);
            else File.Move(item.Path, newPath);

            blockView.RebuildIcons();
            Render();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"重命名失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string? ShowRenameDialog(string title, string? defaultValue)
    {
        using var form = new Form
        {
            Text = title, Size = new Size(380, 150), StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            BackColor = Color.White, Font = new Font("HarmonyOS Sans SC", 9f)
        };

        var label = new Label { Text = "请输入新名称：", Location = new Point(20, 15), AutoSize = true };
        var textBox = new TextBox { Text = defaultValue ?? "", Location = new Point(20, 40), Width = 320 };
        
        if (!string.IsNullOrEmpty(defaultValue) && defaultValue.Contains('.'))
        {
            textBox.SelectionStart = 0;
            textBox.SelectionLength = defaultValue.LastIndexOf('.');
        }
        else textBox.SelectAll();

        var btnOk = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(170, 80), Width = 80 };
        var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(260, 80), Width = 80 };

        form.Controls.AddRange(new Control[] { label, textBox, btnOk, btnCancel });
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        return form.ShowDialog() == DialogResult.OK ? textBox.Text.Trim() : null;
    }

    private void ClearHoverIfOutside()
    {
        if (_hoverBlock != null && _hoverBlock != _dragBlock && _hoverBlock != _resizeBlock)
        {
            BlockView? cur = HitTestBlock(_mousePos?.X ?? int.MinValue, _mousePos?.Y ?? int.MinValue);
            if (!ReferenceEquals(cur, _hoverBlock))
            {
                _hoverBlock = null; _hoverIcon = -1; _tooltipText = null;
            }
        }
    }

    private void ClearHover()
    {
        _hoverBlock = null; _hoverIcon = -1; _tooltipText = null;
    }

    private void OnBlockChanged(BlockView changed)
    {
        double maxRight = 0, maxBottom = 0;
        foreach (var bv in _blocks)
        {
            maxRight = Math.Max(maxRight, bv.Config.X + bv.Config.Width);
            maxBottom = Math.Max(maxBottom, bv.Config.Y + bv.Config.Height);
        }
        _canvasW = Math.Max(maxRight + 200, ClientSize.Width);
        _canvasH = Math.Max(maxBottom + 200, ClientSize.Height);
    }

    private BlockView? HitTestBlock(double x, double y)
    {
        for (int i = _blocks.Count - 1; i >= 0; i--)
        {
            var bv = _blocks[i];
            if (x >= bv.Config.X && x <= bv.Config.X + bv.Config.Width &&
                y >= bv.Config.Y && y <= bv.Config.Y + bv.Config.Height)
                return bv;
        }
        return null;
    }

      private double[] ApplySnap(BlockView self, double x, double y)
    {
        double w = self.Config.Width, h = self.Config.Height;
        double bestDx = SnapThreshold + 1, bestDy = SnapThreshold + 1;
        double snappedX = x, snappedY = y;

        foreach (var o in _blocks)
        {
            if (ReferenceEquals(o, self)) continue;
            double ox = o.Config.X, oy = o.Config.Y, ow = o.Config.Width, oh = o.Config.Height;

            double[] xCands = {
                Math.Abs(x - (ox + ow + SnapGap)),         ox + ow + SnapGap,
                Math.Abs((x + w) - (ox - SnapGap)),        ox - SnapGap - w,
                Math.Abs(x - ox),                          ox,
                Math.Abs((x + w) - (ox + ow)),             ox + ow - w
            };
            for (int i = 0; i < xCands.Length; i += 2)
                if (xCands[i] < bestDx) { bestDx = xCands[i]; snappedX = xCands[i + 1]; }

            double[] yCands = {
                Math.Abs(y - (oy + oh + SnapGap)),         oy + oh + SnapGap,
                Math.Abs((y + h) - (oy - SnapGap)),        oy - SnapGap - h,
                Math.Abs(y - oy),                          oy,
                Math.Abs((y + h) - (oy + oh)),             oy + oh - h
            };
            for (int i = 0; i < yCands.Length; i += 2)
                if (yCands[i] < bestDy) { bestDy = yCands[i]; snappedY = yCands[i + 1]; }
        }

        // ================= 屏幕边缘吸附 =================
        double screenW = ClientSize.Width;
        double screenH = ClientSize.Height;

        // 1. 左侧边缘
        if (Math.Abs(x) < bestDx) { bestDx = Math.Abs(x); snappedX = 0; }
        // 2. 上侧边缘
        if (Math.Abs(y) < bestDy) { bestDy = Math.Abs(y); snappedY = 0; }
        
        // 3. 右侧边缘 (右边距屏幕右侧的距离)
        double distRight = Math.Abs((x + w) - screenW);
        if (distRight < bestDx) { bestDx = distRight; snappedX = screenW - w; }
        
        // 4. 下侧边缘 (下边距屏幕下侧的距离)
        double distBottom = Math.Abs((y + h) - screenH);
        if (distBottom < bestDy) { bestDy = distBottom; snappedY = screenH - h; }

        // 最终确定坐标（留一点余量，防止负坐标）
        double rx = bestDx <= SnapThreshold ? snappedX : x;
        double ry = bestDy <= SnapThreshold ? snappedY : y;
        
        // 强制限制在屏幕内，防止拖出边界外（如果不需要可以删掉这行）
        rx = Math.Max(0, Math.Min(rx, screenW - w));
        ry = Math.Max(0, Math.Min(ry, screenH - h));

        return new[] { rx, ry };
    }
    // ==================== 区块视图 ====================

    public sealed class BlockView
    {
        private readonly DesktopForm _owner;
        private const double TitleBarH = 38;
        private const double FolderBarH = 26;
        private const double TilePaddingLeft = 12;
        private const double TilePaddingRight = 12;
        private const double TilePaddingBottom = 12;

        internal readonly BlockConfig Config;
        private readonly AppConfig _appCfg;

        public sealed class IconItem
        {
            public FileSystemInfo Info = null!;
            public string Path = "";
            public string DisplayName = "";
            public Image? Image;
            public bool IsDirectory;
            public RectangleF Bounds;
        }

        private readonly List<IconItem> _icons = new();
        private double _scrollOffset;
        private int _cols = 1;
        private double _contentH;

        public IReadOnlyList<IconItem> Icons => _icons;

        public BlockView(BlockConfig config, AppConfig appCfg, DesktopForm owner)
        {
            Config = config;
            _appCfg = appCfg;
            _owner = owner;
            RebuildIcons();
        }

          /// <summary>扫描当前文件夹并重排图标网格（对应 Java rebuildTile）</summary>
        public void RebuildIcons()
        {
            _icons.Clear();
            string? folderPath = Config.ActiveFolder;
            if (string.IsNullOrEmpty(folderPath))
            {
                UpdateLayout(); // 如果没有文件夹，清空布局
                return;
            }

            var files = FileScanner.ScanFolder(folderPath);
            foreach (var f in files)
            {
                var item = new IconItem
                {
                    Info = f,
                    Path = f.FullName,
                    DisplayName = FileScanner.GetDisplayName(f),
                    Image = IconUtils.GetSystemIcon(f.FullName),
                    IsDirectory = f.Attributes.HasFlag(FileAttributes.Directory)
                };
                _icons.Add(item);
            }

            UpdateLayout();
        }

        /// <summary>只重新计算图标位置，不重新扫描文件（用于拖拽缩放时实时刷新）</summary>
        public void UpdateLayout()
        {
            if (_icons.Count == 0)
            {
                _cols = 1;
                _contentH = 0;
                ClampScroll();
                return;
            }

            int iconSize = _appCfg.IconSize;
            double hgap = _appCfg.EffectiveHSpacing;
            double vgap = _appCfg.EffectiveVSpacing;
            double cellW = iconSize + 32;
            double tileW = Config.Width - TilePaddingLeft - TilePaddingRight;
            
            _cols = (int)Math.Floor((tileW + hgap) / (cellW + hgap));
            if (_cols < 1) _cols = 1;

            double iconDrawSize = iconSize;
            int idx = 0;
            _contentH = 0;

            // 移到外部实例化，避免循环中重复创建
            using var fontName = new Font("HarmonyOS Sans SC", _appCfg.FontSize, GraphicsUnit.Pixel);
            using var measureG = Graphics.FromImage(new Bitmap(1, 1));
            
            for (int row = 0; idx < _icons.Count; row++)
            {
                int countInRow = Math.Min(_cols, _icons.Count - idx);
                double maxBoxH = 0;
                
                for (int c = 0; c < countInRow; c++)
                {
                    var it = _icons[idx + c];
                    double labelH = MeasureLabel(measureG, fontName, it.DisplayName, iconSize);
                    double boxH = iconDrawSize + 4 + labelH + 8;
                    if (boxH > maxBoxH) maxBoxH = boxH;
                }

                for (int c = 0; c < countInRow; c++)
                {
                    var it = _icons[idx + c];
                    double x = (c == 0) ? TilePaddingLeft : TilePaddingLeft + (c) * cellW + c * hgap;
                    double y = row * (maxBoxH + vgap);
                    it.Bounds = new RectangleF((float)x, (float)y, (float)cellW, (float)maxBoxH);
                }
                idx += countInRow;
                _contentH = row * (maxBoxH + vgap) + maxBoxH;
            }

            ClampScroll();
        }



        private static double MeasureLabel(Graphics g, Font font, string text, int iconSize)
        {
            if (string.IsNullOrEmpty(text)) return 14;
            var sz = g.MeasureString(text, font, (int)(iconSize + 36));
            double lineH = font.GetHeight(g);
            double lines = Math.Max(1, Math.Ceiling(sz.Height / lineH - 0.01));
            if (lines > 2) lines = 2;
            return lines * lineH + 2;
        }

        private void ClampScroll()
        {
            double viewportH = Config.Height - TitleBarH - FolderBarVisibleHeight() - TilePaddingBottom;
            if (viewportH < 0) viewportH = 0;
            double max = Math.Max(0, _contentH - viewportH);
            if (_scrollOffset > max) _scrollOffset = max;
            if (_scrollOffset < 0) _scrollOffset = 0;
        }

        public void ScrollBy(double delta)
        {
            _scrollOffset += delta;
            ClampScroll();
        }

        private double FolderBarVisibleHeight() => Config.Folders.Count > 1 ? FolderBarH : 0;

        public void Draw(Graphics g)
        {
            double x = Config.X, y = Config.Y, w = Config.Width, h = Config.Height;
            double alpha = _appCfg.BlockOpacity / 100.0;

            // 背景
            var rect = new RectangleF((float)x, (float)y, (float)w, (float)h);
            using var path = RoundedRect(rect, 8);
            using var bg = new SolidBrush(Color.FromArgb((int)(alpha * 255), 20, 20, 20));
            g.FillPath(bg, path);

            // 描边
            var borderRect = new RectangleF((float)(x + 0.5), (float)(y + 0.5), (float)(w - 1), (float)(h - 1));
            using var borderPath = RoundedRect(borderRect, 8);
            using var borderPen = new Pen(Color.FromArgb(50, 255, 255, 255), 1f);
            g.DrawPath(borderPen, borderPath);

            // 标题
            using var titleFont = new Font("HarmonyOS Sans SC", 19f, GraphicsUnit.Pixel);
            using var titleBrush = new SolidBrush(Color.White);
            g.DrawString(Config.Name ?? "", titleFont, titleBrush, (float)(x + 12), (float)(y + 8));
            // 锁定按钮 🔒/🔓 (放在 ⚙ 左侧)
            using var lockFont = new Font("Segoe UI Emoji", 18f, GraphicsUnit.Pixel);
            using var lockBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
            float lockX = (float)(x + w - 70), lockY = (float)(y + 6);
            g.DrawString(Config.IsLocked ? "🔒" : "🔓", lockFont, lockBrush, lockX, lockY);

           
            // ⚙ 按钮
            using var btnFont = new Font("Segoe UI Symbol", 24f, GraphicsUnit.Pixel);
            using var btnBrush = new SolidBrush(Color.FromArgb(166, 255, 255, 255));
            float btnX = (float)(x + w - 40), btnY = (float)(y + 2);
            g.DrawString("⚙", btnFont, btnBrush, btnX, btnY);

            // Tabs
            var folders = Config.Folders;
            if (folders.Count > 1)
            {
                using var tabFont = new Font("HarmonyOS Sans SC", 15f, GraphicsUnit.Pixel);
                using var typographic = StringFormat.GenericTypographic;
                float tabY = (float)(y + TitleBarH - 2);
                float cursor = (float)(x + 12);
                for (int i = 0; i < folders.Count; i++)
                {
                                      string name = Path.GetFileName(folders[i]);
                    if (string.IsNullOrWhiteSpace(name)) name = folders[i];
                    var sz = g.MeasureString(name, tabFont, 1000, typographic);
                    float tabW = sz.Width + 20;
                    var tabRect = new RectangleF(cursor, tabY, tabW, (float)FolderBarH - 4);
                    bool active = (i == Config.ActiveIndex);
                    
                    using var tabPath = RoundedRect(tabRect, 4);
                    
                    // ★ 核心修改：选中时黑色填充，未选中时只画描边
                    if (active)
                    {
                        // 选中：半透明白色描边
                        using var tabPen = new Pen(Color.FromArgb(200, 255, 255, 255), 1f);
                        g.DrawPath(tabPen, tabPath);
                    }
                    else
                    {
                        
                         // 未选中：黑色背景+半透明白色描边
                        using var tabBg = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
                        g.FillPath(tabBg, tabPath);
                         using var tabPen = new Pen(Color.FromArgb(60, 255, 255, 255), 1f);
                        g.DrawPath(tabPen, tabPath);
                    }

                    // 文字颜色：选中纯白，未选中灰白
                    using var tabBrush = new SolidBrush(active ? Color.White : Color.FromArgb(140, 255, 255, 255));
                    var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(name, tabFont, tabBrush, tabRect, fmt);
                    cursor += tabW + 4;
                }
            }

            // 图标网格
            double top = y + TitleBarH + FolderBarVisibleHeight();
            double viewportH = h - TitleBarH - FolderBarVisibleHeight() - TilePaddingBottom;
            if (viewportH > 0 && _icons.Count > 0)
            {
                g.SetClip(new RectangleF((float)x, (float)top, (float)w, (float)viewportH));

                int iconSize = _appCfg.IconSize;
                for (int i = 0; i < _icons.Count; i++)
                {
                    var it = _icons[i];
                    double drawY = it.Bounds.Y - _scrollOffset + top;
                    if (drawY + it.Bounds.Height < top) continue;
                    if (drawY > top + viewportH) continue;

                    bool hover = ReferenceEquals(_owner._hoverBlock, this) && _owner._hoverIcon == i;
                    var cellRect = new RectangleF((float)(x + it.Bounds.X), (float)drawY, (float)it.Bounds.Width, (float)it.Bounds.Height);
                    if (hover)
                    {
                        using var hoverPath = RoundedRect(cellRect, 6);
                        using var hb = new SolidBrush(Color.FromArgb(46, 255, 255, 255));
                        g.FillPath(hb, hoverPath);
                    }

                    double imgX = x + it.Bounds.X, imgY = drawY + 4, dH = 0;
                    if (it.Image != null)
                    {
                        double scale = Math.Min(iconSize / (double)it.Image.Width, iconSize / (double)it.Image.Height);
                        if (scale > 1) scale = 1;
                        double dW = it.Image.Width * scale;
                        dH = it.Image.Height * scale;
                        imgX = x + it.Bounds.X + (it.Bounds.Width - dW) / 2;
                        g.DrawImage(it.Image, (float)imgX, (float)imgY, (float)dW, (float)dH);
                    }

using (var fontName = new Font("HarmonyOS Sans SC", _appCfg.FontSize, GraphicsUnit.Pixel))                    using (var nameBrush = new SolidBrush(Color.White))
                    {
                        float nameTop = (float)(imgY + dH + 4);
                        var labelRect = new RectangleF((float)(x + it.Bounds.X), nameTop, (float)Math.Min(it.Bounds.Width, iconSize + 36), (float)(it.Bounds.Height - (nameTop - drawY)));
                        var fmt2 = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };
                        g.DrawString(it.DisplayName, fontName, nameBrush, labelRect, fmt2);
                    }
                }
                g.ResetClip();
            }

            // 缩放柄-不用显示了
            
        }

        public bool IsOnResizeHandle(double x, double y)
        {
            double bx = Config.X + Config.Width - 14, by = Config.Y + Config.Height - 14;
            return x >= bx && x <= bx + 12 && y >= by && y <= by + 12;
        }

        public bool IsOnConfigButton(double x, double y)
        {
            double bx = Config.X + Config.Width - 50, by = Config.Y;
            return x >= bx && x <= bx + 50 && y >= by && y <= by + 38;
        }
                public bool IsOnLockButton(double x, double y)
        {
            // 给锁定按钮划一块点击热区
            double bx = Config.X + Config.Width - 75;
            double by = Config.Y;
            return x >= bx && x <= bx + 35 && y >= by && y <= by + 38;
        }

        public int HitTestTab(double x, double y)
        {
            var folders = Config.Folders;
            if (folders.Count <= 1) return -1;
            double tabY = Config.Y + TitleBarH - 2;
            double endY = tabY + FolderBarH - 4;
            if (y < tabY || y > endY) return -1;

using var tabFont = new Font("HarmonyOS Sans SC", _appCfg.FontSize, GraphicsUnit.Pixel);            using var measureG = Graphics.FromImage(new Bitmap(1, 1));
            double cursor = Config.X + 12;
            for (int i = 0; i < folders.Count; i++)
            {
                string name = Path.GetFileName(folders[i]);
                if (string.IsNullOrWhiteSpace(name)) name = folders[i];
                var sz = measureG.MeasureString(name, tabFont);
                double tabW = sz.Width + 20;
                if (x >= cursor - 2 && x <= cursor + tabW + 2) return i;
                cursor += tabW + 4;
            }
            return -1;
        }

        public int HitTestIcon(double x, double y)
        {
            if (_icons.Count == 0 || Config.Folders.Count <= 0) return -1;

            double top = Config.Y + TitleBarH + FolderBarVisibleHeight();
            for (int i = 0; i < _icons.Count; i++)
            {
                var it = _icons[i];
                double drawY = it.Bounds.Y - _scrollOffset + top;
                double bx = Config.X + it.Bounds.X;
                if (x >= bx && x <= bx + it.Bounds.Width && y >= drawY && y <= drawY + it.Bounds.Height)
                    return i;
            }
            return -1;
        }

        internal static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}