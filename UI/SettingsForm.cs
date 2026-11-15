using System;
using System.Drawing;
using System.Windows.Forms;
using DesktopX.Models;
using DesktopX.Services;
using DesktopX.Utils;

namespace DesktopX.UI;

public class SettingsForm : Form
{
    // ---- 主题色 ----
    private static readonly Color ThemeBg = Color.FromArgb(245, 246, 248);
    private static readonly Color CardBg = Color.White;
    private static readonly Color Primary = Color.FromArgb(45, 110, 210);
    private static readonly Color PrimaryHover = Color.FromArgb(38, 96, 190);
    private static readonly Color BorderGray = Color.FromArgb(225, 228, 232);
    private static readonly Color TextDark = Color.FromArgb(45, 49, 54);
    private static readonly Color TextGray = Color.FromArgb(120, 126, 134);
    private static readonly Color LightHover = Color.FromArgb(244, 246, 248);

    private readonly ConfigManager _configManager;
    private readonly Action _onSaved;

    private readonly ListBox _blockList = new();
    private readonly ListBox _folderList = new();
    private readonly Label _folderTitle = new();

    private readonly TrackBar _iconSizeBar = CreateBar(32, 128);
    private readonly TrackBar _hSpacingBar = CreateBar(0, 60);
    private readonly TrackBar _vSpacingBar = CreateBar(0, 60);
    private readonly TrackBar _opacityBar = CreateBar(0, 100);
    
    // ★ 新增：字体大小滑块 8~24 px
    private readonly TrackBar _fontSizeBar = CreateBar(8, 24); 
    
    private readonly CheckBox _autoStartCheck = new();

    // 滑块当前值显示
    private readonly Label _iconSizeVal = new();
    private readonly Label _hSpacingVal = new();
    private readonly Label _vSpacingVal = new();
    private readonly Label _opacityVal = new();
    private readonly Label _fontSizeVal = new(); // ★ 新增

    private AppConfig _config = null!;

    public SettingsForm(ConfigManager configManager, Action onSaved)
    {
        _configManager = configManager;
        _onSaved = onSaved;

        Text = "DesktopX 设置";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 700);
        MinimumSize = new Size(780, 540);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = ThemeBg;

        BuildUi();
    }

    private static TrackBar CreateBar(int min, int max)
    {
        return new TrackBar { Minimum = min, Maximum = max, TickStyle = TickStyle.None, Height = 36 };
    }

    private void BuildUi()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 52,
            BackColor = CardBg,
            ColumnCount = 1,
            Padding = new Padding(16, 0, 16, 0)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.Controls.Add(new Label
        {
            Text = "DesktopX 设置",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            ForeColor = TextDark,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            BackColor = CardBg,
            ColumnCount = 2,
            Padding = new Padding(16, 12, 16, 12)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        var saveBtn = CreatePrimaryButton("保存并应用");
        saveBtn.Dock = DockStyle.Fill;
        saveBtn.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        saveBtn.Click += (_, _) => OnSave();
        footer.Controls.Add(saveBtn, 1, 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(14),
            BackColor = ThemeBg
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.Controls.Add(BuildBlockCard(), 0, 0);
        body.Controls.Add(BuildRightPanel(), 1, 0);

        Controls.Add(body);
        Controls.Add(header);
        Controls.Add(footer);
    }

    private Control BuildBlockCard()
    {
        var card = MakeCard();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = CardBg };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        layout.Controls.Add(MakeCardTitle("区块列表"), 0, 0);

        _blockList.Dock = DockStyle.Fill;
        _blockList.IntegralHeight = false;
        _blockList.BorderStyle = BorderStyle.FixedSingle;
        _blockList.Font = new Font("Microsoft YaHei UI", 9F);
        _blockList.SelectedIndexChanged += (_, _) => RefreshFolderList();
        _blockList.DoubleClick += (_, _) => OnRenameBlock();
        layout.Controls.Add(_blockList, 0, 1);

        var btnRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = CardBg,
            Padding = new Padding(0, 7, 0, 0)
        };
        btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        var addBlockBtn = CreatePrimaryButton("＋ 添加区块");
        addBlockBtn.Dock = DockStyle.Fill;
        addBlockBtn.Click += (_, _) => OnAddBlock();

        var removeBlockBtn = CreateSecondaryButton("删除选中区块");
        removeBlockBtn.Dock = DockStyle.Fill;
        removeBlockBtn.Click += (_, _) => OnRemoveBlock();

        btnRow.Controls.Add(addBlockBtn, 0, 0);
        btnRow.Controls.Add(removeBlockBtn, 1, 0);
        layout.Controls.Add(btnRow, 0, 2);

        card.Controls.Add(layout);
        return card;
    }

    private void OnRenameBlock()
    {
        int idx = _blockList.SelectedIndex;
        if (idx < 0 || idx >= _config.Blocks.Count) return;

        var block = _config.Blocks[idx];
        string? newName = Prompt("重命名区块", block.Name);
        
        if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == block.Name) return;

        block.Name = newName.Trim();
        _configManager.Save();
        _blockList.Items[idx] = $"{block.Name}  ({block.Folders.Count} 个文件夹)";
        RefreshFolderList();
        _onSaved?.Invoke();
    }

    private Control BuildRightPanel()
    {
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = ThemeBg };
        // ★ 调整高度：参数卡片需要容纳 5 个滑块，高度从 246 放大到 290
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 290)); 
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.Controls.Add(BuildParamsCard(), 0, 0);
        right.Controls.Add(BuildFolderCard(), 0, 1);
        return right;
    }

    private Control BuildParamsCard()
    {
        var card = MakeCard();
        // ★ 调整 RowCount 为 6（1个标题 + 5个参数）
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 3, BackColor = CardBg };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        for (int i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));

        var headRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = CardBg };
        headRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        headRow.Controls.Add(MakeCardTitle("区块参数"), 0, 0);
        _autoStartCheck.Text = "开机自启动";
        _autoStartCheck.Dock = DockStyle.Fill;
        _autoStartCheck.TextAlign = ContentAlignment.MiddleRight;
        _autoStartCheck.ForeColor = TextDark;
        headRow.Controls.Add(_autoStartCheck, 1, 0);

        layout.Controls.Add(headRow, 0, 0);
        layout.SetColumnSpan(headRow, 3);

        // ★ 原有参数
        AddParamRow(layout, 1, "图标大小", _iconSizeBar, _iconSizeVal, "{0} px");
        AddParamRow(layout, 2, "水平间距", _hSpacingBar, _hSpacingVal, "{0} px");
        AddParamRow(layout, 3, "垂直间距", _vSpacingBar, _vSpacingVal, "{0} px");
        AddParamRow(layout, 4, "不透明度", _opacityBar, _opacityVal, "{0} %");
        // ★ 新增参数
        AddParamRow(layout, 5, "字体大小", _fontSizeBar, _fontSizeVal, "{0} px");

        card.Controls.Add(layout);
        return card;
    }

    private void AddParamRow(TableLayoutPanel layout, int row, string labelText, TrackBar bar, Label valueLabel, string fmt)
    {
        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextDark,
            Font = new Font("Microsoft YaHei UI", 9F)
        };
        bar.Dock = DockStyle.Fill;
        bar.ValueChanged += (_, _) => valueLabel.Text = string.Format(fmt, bar.Value);
        valueLabel.Dock = DockStyle.Fill;
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;
        valueLabel.ForeColor = TextGray;
        valueLabel.Text = string.Format(fmt, bar.Value);

        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(bar, 1, row);
        layout.Controls.Add(valueLabel, 2, row);
    }

    private Control BuildFolderCard()
    {
        var card = MakeCard();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = CardBg };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        _folderTitle.Text = "文件夹";
        _folderTitle.Dock = DockStyle.Fill;
        _folderTitle.TextAlign = ContentAlignment.MiddleLeft;
        _folderTitle.ForeColor = TextDark;
        _folderTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        layout.Controls.Add(_folderTitle, 0, 0);

        _folderList.Dock = DockStyle.Fill;
        _folderList.IntegralHeight = false;
        _folderList.BorderStyle = BorderStyle.FixedSingle;
        _folderList.Font = new Font("Microsoft YaHei UI", 9F);
        layout.Controls.Add(_folderList, 0, 1);

        var btnRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = CardBg,
            Padding = new Padding(0, 7, 0, 0)
        };
        btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var addFolderBtn = CreatePrimaryButton("＋ 添加文件夹");
        addFolderBtn.Dock = DockStyle.Fill;
        addFolderBtn.Click += (_, _) => OnAddFolder();

        var removeFolderBtn = CreateSecondaryButton("移除选中文件夹");
        removeFolderBtn.Dock = DockStyle.Fill;
        removeFolderBtn.Click += (_, _) => OnRemoveFolder();

        btnRow.Controls.Add(addFolderBtn, 0, 0);
        btnRow.Controls.Add(removeFolderBtn, 1, 0);
        layout.Controls.Add(btnRow, 0, 2);

        card.Controls.Add(layout);
        return card;
    }

    private static Panel MakeCard()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = CardBg,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 14),
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    private static Label MakeCardTitle(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
            ForeColor = TextDark
        };
    }

    private static Button CreatePrimaryButton(string text)
    {
        return new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = Primary,
            ForeColor = Color.White,
            FlatAppearance = { BorderSize = 0, MouseOverBackColor = PrimaryHover },
            Font = new Font("Microsoft YaHei UI", 9F),
            Height = 30,
            Cursor = Cursors.Hand
        };
    }

    private static Button CreateSecondaryButton(string text)
    {
        return new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = CardBg,
            ForeColor = TextDark,
            FlatAppearance = { BorderColor = BorderGray, BorderSize = 1, MouseOverBackColor = LightHover },
            Font = new Font("Microsoft YaHei UI", 9F),
            Height = 30,
            Cursor = Cursors.Hand
        };
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.ApplicationExitCall)
        {
            base.OnFormClosing(e);
            return;
        }
        e.Cancel = true;
        Hide();
        base.OnFormClosing(e);
    }

    public void ShowSettings()
    {
        LoadConfig(_configManager.Load());
        Show();
        Activate();
        TopMost = true;
        TopMost = false;
    }

    public void LoadConfig(AppConfig config)
    {
        _config = config;

        _blockList.Items.Clear();
        foreach (var b in config.Blocks)
        {
            _blockList.Items.Add($"{b.Name}  ({b.Folders.Count} 个文件夹)");
        }
        if (_blockList.Items.Count > 0) _blockList.SelectedIndex = 0;

        _iconSizeBar.Value = Math.Clamp(config.IconSize, _iconSizeBar.Minimum, _iconSizeBar.Maximum);
        _hSpacingBar.Value = Math.Clamp((int)config.EffectiveHSpacing, _hSpacingBar.Minimum, _hSpacingBar.Maximum);
        _vSpacingBar.Value = Math.Clamp((int)config.EffectiveVSpacing, _vSpacingBar.Minimum, _vSpacingBar.Maximum);
        _opacityBar.Value = Math.Clamp(config.BlockOpacity, _opacityBar.Minimum, _opacityBar.Maximum);
        // ★ 加载字体大小
        _fontSizeBar.Value = Math.Clamp(config.FontSize, _fontSizeBar.Minimum, _fontSizeBar.Maximum);

        _iconSizeVal.Text = _iconSizeBar.Value + " px";
        _hSpacingVal.Text = _hSpacingBar.Value + " px";
        _vSpacingVal.Text = _vSpacingBar.Value + " px";
        _opacityVal.Text = _opacityBar.Value + " %";
        _fontSizeVal.Text = _fontSizeBar.Value + " px"; // ★

        _autoStartCheck.Checked = AutoStartManager.IsEnabled();
        RefreshFolderList();
    }

    private void RefreshFolderList()
    {
        _folderList.Items.Clear();
        if (_blockList.SelectedIndex < 0 || _blockList.SelectedIndex >= _config.Blocks.Count)
        {
            _folderTitle.Text = "文件夹（请先在左侧选择区块）";
            return;
        }

        var block = _config.Blocks[_blockList.SelectedIndex];
        _folderTitle.Text = $"「{block.Name}」的文件夹（{block.Folders.Count} 个）";
        foreach (var f in block.Folders)
        {
            _folderList.Items.Add(f);
        }
    }

    private void OnSave()
    {
        _config.IconSize = _iconSizeBar.Value;
        _config.HSpacing = _hSpacingBar.Value;
        _config.VSpacing = _vSpacingBar.Value;
        _config.BlockOpacity = _opacityBar.Value;
        _config.FontSize = _fontSizeBar.Value; // ★ 保存字体大小

        _configManager.Save();
        _onSaved?.Invoke();

        if (_autoStartCheck.Checked)
            AutoStartManager.Enable();
        else
            AutoStartManager.Disable();

        MessageBox.Show("已保存", "DesktopX", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnAddBlock()
    {
        string? name = Prompt("添加区块", "新区块");
        if (string.IsNullOrWhiteSpace(name)) return;

        var block = new BlockConfig { Name = name.Trim() };
        block.ActiveIndex = 0;

        double maxBottom = 40;
        foreach (var b in _config.Blocks)
        {
            maxBottom = Math.Max(maxBottom, b.Y + b.Height + 20);
        }
        block.X = 40;
        block.Y = maxBottom;
        block.Width = 400;
        block.Height = 300;

        _config.Blocks.Add(block);
        _configManager.Save();
        _blockList.Items.Add($"{block.Name}  ({block.Folders.Count} 个文件夹)");
        _blockList.SelectedIndex = _blockList.Items.Count - 1;
        _onSaved?.Invoke();
    }

    private void OnRemoveBlock()
    {
        int idx = _blockList.SelectedIndex;
        if (idx < 0 || idx >= _config.Blocks.Count) return;

        _config.Blocks.RemoveAt(idx);
        _blockList.Items.RemoveAt(idx);
        if (_blockList.Items.Count > 0)
        {
            _blockList.SelectedIndex = Math.Min(idx, _blockList.Items.Count - 1);
        }
        RefreshFolderList();
        _configManager.Save();
        _onSaved?.Invoke();
    }

    private void OnAddFolder()
    {
        if (_blockList.SelectedIndex < 0 || _blockList.SelectedIndex >= _config.Blocks.Count) return;
        var block = _config.Blocks[_blockList.SelectedIndex];

        using var folderDialog = new FolderBrowserDialog
        {
            Description = "选择要添加到区块的文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (folderDialog.ShowDialog(this) != DialogResult.OK) return;

        string folderPath = folderDialog.SelectedPath;
        if (block.Folders.Contains(folderPath))
        {
            MessageBox.Show("该文件夹已在列表中", "DesktopX", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        block.Folders.Add(folderPath);
        _blockList.Items[_blockList.SelectedIndex] = $"{block.Name}  ({block.Folders.Count} 个文件夹)";
        RefreshFolderList();
        _configManager.Save();
        _onSaved?.Invoke();
    }

    private void OnRemoveFolder()
    {
        int blockIdx = _blockList.SelectedIndex;
        int folderIdx = _folderList.SelectedIndex;
        if (blockIdx < 0 || blockIdx >= _config.Blocks.Count) return;
        if (folderIdx < 0 || folderIdx >= _config.Blocks[blockIdx].Folders.Count) return;

        var block = _config.Blocks[blockIdx];
        block.Folders.RemoveAt(folderIdx);

        if (block.ActiveIndex >= block.Folders.Count)
        {
            block.ActiveIndex = 0;
        }

        _blockList.Items[blockIdx] = $"{block.Name}  ({block.Folders.Count} 个文件夹)";
        RefreshFolderList();
        _configManager.Save();
        _onSaved?.Invoke();
    }

    private static string? Prompt(string title, string? defaultValue)
    {
        using var dlg = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(400, 120),
            BackColor = CardBg,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            Font = new Font("Microsoft YaHei UI", 9F)
        };
        var hint = new Label
        {
            Text = "区块名称：",
            Location = new Point(16, 12),
            AutoSize = true,
            ForeColor = TextDark
        };
        var input = new TextBox
        {
            Text = defaultValue ?? "",
            Location = new Point(16, 36),
            Width = 366,
            Font = new Font("Microsoft YaHei UI", 10F)
        };
        var ok = new Button
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Location = new Point(236, 74),
            Width = 70,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Primary,
            ForeColor = Color.White,
            FlatAppearance = { BorderSize = 0 }
        };
        var cancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Location = new Point(314, 74),
            Width = 70,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = CardBg,
            ForeColor = TextDark,
            FlatAppearance = { BorderColor = BorderGray, BorderSize = 1 }
        };
        dlg.Controls.Add(hint);
        dlg.Controls.Add(input);
        dlg.Controls.Add(ok);
        dlg.Controls.Add(cancel);
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;

        return dlg.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(input.Text)
            ? input.Text.Trim()
            : null;
    }
}