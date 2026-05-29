using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;

namespace NdjsonLogViewer;

public sealed partial class MainForm : Form
{
    // ── Color palette (mirrors the HTML CSS variables) ──
    private static readonly Color BgColor    = FromHex("#F8F9FA");
    private static readonly Color Surface    = Color.White;
    private static readonly Color Surface2   = FromHex("#F2F4F6");
    private static readonly Color BorderClr  = FromHex("#E5E8EB");
    private static readonly Color Border2    = FromHex("#D1D6DB");
    private static readonly Color TextMain   = FromHex("#191F28");
    private static readonly Color Text2      = FromHex("#6B7684");
    private static readonly Color Text3      = FromHex("#ADB5BD");
    private static readonly Color Purple     = FromHex("#7C56E8");
    private static readonly Color PurpleBg   = FromHex("#F0ECFD");
    private static readonly Color PurpleT    = FromHex("#5B3DC4");
    private static readonly Color Blue       = FromHex("#3182F6");
    private static readonly Color BlueBg     = FromHex("#EBF3FE");
    private static readonly Color BlueT      = FromHex("#1B64DA");
    private static readonly Color Orange     = FromHex("#F4A623");
    private static readonly Color OrangeBg   = FromHex("#FEF6E8");
    private static readonly Color OrangeT    = FromHex("#C77D0A");
    private static readonly Color Red        = FromHex("#F04452");
    private static readonly Color RedBg      = FromHex("#FEF0F1");
    private static readonly Color RedT       = FromHex("#C8202D");

    // ── State ──
    private readonly List<LogEntry> _all = new();
    private List<LogEntry> _filtered = new();
    private readonly List<string> _excludes = new();
    private string _searchQuery = string.Empty;
    private string? _currentFile;
    private CancellationTokenSource? _loadCts;

    // ── Controls ──
    private MenuStrip _menu = null!;
    private StatusStrip _status = null!;
    private ToolStripStatusLabel _statusFile = null!;
    private ToolStripStatusLabel _statusReady = null!;
    private ToolStripProgressBar _progressBar = null!;
    private TableLayoutPanel _outer = null!;
    private Panel _statsRow = null!;
    private StatCard _cardTotal = null!, _cardError = null!, _cardWarn = null!, _cardInfo = null!, _cardShown = null!;
    private TextBox _searchBox = null!;
    private ComboBox _levelCombo = null!, _catCombo = null!, _srcCombo = null!;
    private Button _btnCsv = null!;
    private TextBox _excludeBox = null!;
    private FlowLayoutPanel _excludeTags = null!;
    private Label _excludeEmpty = null!;
    private SplitContainer _split = null!;
    private DataGridView _grid = null!;
    private RichTextBox _detail = null!;
    private System.Windows.Forms.Timer _searchTimer = null!;
    private Panel _welcomeOverlay = null!;

    public MainForm()
    {
        Text = "NDJSON Event Log Viewer";
        Icon = SystemIcons.Application;
        ClientSize = new Size(1400, 880);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BgColor;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AllowDrop = true;
        KeyPreview = true;
        MinimumSize = new Size(1000, 600);

        BuildUI();
        WireEvents();
        UpdateStats();
        UpdateExcludeTagsUi();
        ToggleWelcome(true);
    }

    // ── UI construction ─────────────────────────────────────────────

    private void BuildUI()
    {
        BuildMenu();
        BuildStatusBar();

        _outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = BgColor,
            Padding = new Padding(16, 12, 16, 8),
        };
        _outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // stats
        _outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // filter row
        _outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // exclude row
        _outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));// content

        BuildStatsRow();
        BuildFilterRow();
        BuildExcludeRow();
        BuildContent();

        _outer.Controls.Add(_statsRow, 0, 0);
        _outer.Controls.Add(BuildToolbarRow1(), 0, 1);
        _outer.Controls.Add(BuildToolbarRow2(), 0, 2);
        _outer.Controls.Add(_split, 0, 3);

        Controls.Add(_outer);
        Controls.Add(_status);
        Controls.Add(_menu);
        MainMenuStrip = _menu;
    }

    private void BuildMenu()
    {
        _menu = new MenuStrip
        {
            BackColor = Surface,
            Renderer = new ToolStripProfessionalRenderer(new SoftColorTable()),
            Padding = new Padding(8, 2, 8, 2),
        };

        var file = new ToolStripMenuItem("&File");
        var open = new ToolStripMenuItem("&Open NDJSON…", null, (_, _) => OpenFileDialogAndLoad())
        { ShortcutKeys = Keys.Control | Keys.O };
        var openFolder = new ToolStripMenuItem("Open &folder…", null, (_, _) => OpenFolderDialogAndLoad())
        { ShortcutKeys = Keys.Control | Keys.Shift | Keys.O };
        var sample = new ToolStripMenuItem("Load &sample data", null, (_, _) => LoadSampleData())
        { ShortcutKeys = Keys.Control | Keys.D1 };
        var closeFile = new ToolStripMenuItem("&Close file", null, (_, _) => CloseFile());
        var exportCsv = new ToolStripMenuItem("&Export CSV…", null, (_, _) => ExportCsv())
        { ShortcutKeys = Keys.Control | Keys.E };
        var exit = new ToolStripMenuItem("E&xit", null, (_, _) => Close())
        { ShortcutKeys = Keys.Alt | Keys.F4 };
        file.DropDownItems.AddRange(new ToolStripItem[]
        {
            open, openFolder, sample, closeFile,
            new ToolStripSeparator(),
            exportCsv,
            new ToolStripSeparator(),
            exit
        });

        var view = new ToolStripMenuItem("&View");
        var focusSearch = new ToolStripMenuItem("Focus &search", null, (_, _) => _searchBox.Focus())
        { ShortcutKeys = Keys.Control | Keys.F };
        var clearFilters = new ToolStripMenuItem("&Clear filters", null, (_, _) => ClearAllFilters())
        { ShortcutKeys = Keys.Control | Keys.K };
        var toggleDetail = new ToolStripMenuItem("Toggle &detail pane", null, (_, _) => ToggleDetailPane())
        { ShortcutKeys = Keys.F4 };
        view.DropDownItems.AddRange(new ToolStripItem[] { focusSearch, clearFilters, toggleDetail });

        var help = new ToolStripMenuItem("&Help");
        var about = new ToolStripMenuItem("&About…", null, (_, _) => ShowAbout());
        help.DropDownItems.Add(about);

        _menu.Items.AddRange(new ToolStripItem[] { file, view, help });
    }

    private void BuildStatusBar()
    {
        _status = new StatusStrip
        {
            BackColor = Surface,
            SizingGrip = false,
            Padding = new Padding(8, 2, 12, 2),
        };
        _statusReady = new ToolStripStatusLabel("Drop an NDJSON file or use File > Open…")
        {
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Text2,
        };
        _statusFile = new ToolStripStatusLabel("")
        {
            ForeColor = PurpleT,
            Font = new Font(Font, FontStyle.Bold),
        };
        _progressBar = new ToolStripProgressBar { Visible = false, Width = 160 };
        _status.Items.AddRange(new ToolStripItem[] { _statusReady, _progressBar, _statusFile });
    }

    private void BuildStatsRow()
    {
        _statsRow = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = BgColor, Margin = new Padding(0, 0, 0, 12) };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            BackColor = BgColor,
        };
        for (int i = 0; i < 5; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _cardTotal = new StatCard("Total lines",     "0", PurpleT);
        _cardError = new StatCard("Critical / Error", "0", RedT);
        _cardWarn  = new StatCard("Warning",          "0", OrangeT);
        _cardInfo  = new StatCard("Information",      "0", BlueT);
        _cardShown = new StatCard("Displayed",        "0", PurpleT);

        grid.Controls.Add(_cardTotal, 0, 0);
        grid.Controls.Add(_cardError, 1, 0);
        grid.Controls.Add(_cardWarn,  2, 0);
        grid.Controls.Add(_cardInfo,  3, 0);
        grid.Controls.Add(_cardShown, 4, 0);

        _statsRow.Controls.Add(grid);
    }

    private void BuildFilterRow() { /* split into separate method below */ }

    private Panel BuildToolbarRow1()
    {
        var panel = new RoundedPanel(topRadius: 12, bottomRadius: 0)
        {
            Dock = DockStyle.Top,
            Height = 52,
            BackColor = Surface,
            Padding = new Padding(12, 8, 12, 8),
        };

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Surface,
        };

        _searchBox = new TextBox
        {
            Width = 360,
            Height = 30,
            Font = new Font(Font.FontFamily, 9.5F),
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 3, 8, 0),
            PlaceholderText = "🔍  Search message, source, category, resource…",
        };

        _levelCombo = MakeCombo(new[] { "All levels", "Critical", "Error", "Warning", "Information", "Verbose/Debug" }, 150);
        _catCombo   = MakeCombo(new[] { "All categories" }, 180);
        _srcCombo   = MakeCombo(new[] { "All sources" }, 220);

        _btnCsv = MakeButton("⤓  Export CSV", click: (_, _) => ExportCsv(), accent: false);

        flow.Controls.Add(_searchBox);
        flow.Controls.Add(_levelCombo);
        flow.Controls.Add(_catCombo);
        flow.Controls.Add(_srcCombo);
        flow.Controls.Add(MakeToolStripSeparatorPanel());
        flow.Controls.Add(_btnCsv);

        panel.Controls.Add(flow);
        return panel;
    }

    private Panel BuildToolbarRow2()
    {
        var panel = new RoundedPanel(topRadius: 0, bottomRadius: 12)
        {
            Dock = DockStyle.Top,
            Height = 46,
            BackColor = Surface,
            Padding = new Padding(12, 6, 12, 8),
            Margin = new Padding(0, 0, 0, 12),
        };

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Surface,
        };

        var lbl = new Label
        {
            AutoSize = true,
            Text = "⊘  Exclude:",
            ForeColor = RedT,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
            Margin = new Padding(0, 8, 8, 0),
        };

        _excludeBox = new TextBox
        {
            Width = 280,
            Height = 30,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 4, 6, 0),
            PlaceholderText = "Type text to exclude and press Enter or +",
        };
        var addBtn = new Button
        {
            Text = "+ Add",
            FlatStyle = FlatStyle.Flat,
            BackColor = RedBg,
            ForeColor = RedT,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
            Height = 30,
            AutoSize = true,
            Padding = new Padding(8, 0, 8, 0),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 4, 12, 0),
        };
        addBtn.FlatAppearance.BorderColor = FromHex("#F5B8BC");
        addBtn.FlatAppearance.BorderSize = 1;
        addBtn.Click += (_, _) => AddExclude();

        _excludeTags = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Surface,
            Margin = new Padding(0, 4, 0, 0),
        };
        _excludeEmpty = new Label
        {
            AutoSize = true,
            Text = "No exclusions active",
            Font = new Font(Font.FontFamily, 8.5F, FontStyle.Italic),
            ForeColor = Text3,
            Margin = new Padding(0, 7, 0, 0),
        };
        _excludeTags.Controls.Add(_excludeEmpty);

        flow.Controls.Add(lbl);
        flow.Controls.Add(_excludeBox);
        flow.Controls.Add(addBtn);
        flow.Controls.Add(_excludeTags);

        panel.Controls.Add(flow);
        return panel;
    }

    private void BuildExcludeRow() { /* handled inside BuildToolbarRow2 */ }

    private void BuildContent()
    {
        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            BackColor = BorderClr,
            SplitterWidth = 6,
            FixedPanel = FixedPanel.Panel2,
        };

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            VirtualMode = true,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToOrderColumns = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 30,
            BorderStyle = BorderStyle.None,
            GridColor = BorderClr,
            BackgroundColor = Surface,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            EnableHeadersVisualStyles = false,
            Font = new Font("Consolas", 9F),
            RowTemplate = { Height = 24, DefaultCellStyle = { Padding = new Padding(4, 2, 4, 2) } },
        };
        _grid.DefaultCellStyle.SelectionBackColor = PurpleBg;
        _grid.DefaultCellStyle.SelectionForeColor = TextMain;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Surface2;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Text2;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 4, 6, 4);
        _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        _grid.RowsDefaultCellStyle.BackColor = Surface;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Surface;

        AddColumn("Index",   "#",            58,  alignRight: true);
        AddColumn("Time",    "Time (UTC)",   150);
        AddColumn("Level",   "Level",        88);
        AddColumn("Source",  "Trace Source", 230);
        AddColumn("Cat",     "Category",     160);
        AddColumn("Msg",     "Message",      0,   fill: true);

        var gridContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1) };
        gridContainer.Controls.Add(_grid);

        _detail = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Surface,
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 9.5F),
            ForeColor = TextMain,
            DetectUrls = true,
            ScrollBars = RichTextBoxScrollBars.Both,
        };
        var detailHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        detailHost.Controls.Add(_detail);

        _split.Panel1.Controls.Add(gridContainer);
        _split.Panel2.Controls.Add(detailHost);
        _split.Panel1MinSize = 200;
        _split.Panel2MinSize = 80;
        _split.SplitterDistance = 520;

        _welcomeOverlay = BuildWelcomeOverlay();
        _split.Panel1.Controls.Add(_welcomeOverlay);
        _welcomeOverlay.BringToFront();

        return;

        void AddColumn(string name, string header, int width, bool alignRight = false, bool fill = false)
        {
            var c = new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                ReadOnly = true,
                Resizable = DataGridViewTriState.True,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                MinimumWidth = 40,
            };
            if (fill)
            {
                c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                c.FillWeight = 100;
            }
            else
            {
                c.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                c.Width = width;
            }
            if (alignRight) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            c.DefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
            _grid.Columns.Add(c);
        }
    }

    private Panel BuildWelcomeOverlay()
    {
        var p = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(40),
        };
        var lblTitle = new Label
        {
            Text = "Drag & drop an NDJSON / JSONL file or a folder",
            Font = new Font("Segoe UI", 16F, FontStyle.Bold),
            ForeColor = TextMain,
            AutoSize = true,
        };
        var lblSub = new Label
        {
            Text = "Azure App Service structured logs are supported out of the box.\n" +
                   "Drop a folder to aggregate all PT1H.json / *.ndjson files inside it, sorted by time.\n" +
                   "Tens of thousands of lines render smoothly via virtualized scrolling.",
            Font = new Font("Segoe UI", 10F),
            ForeColor = Text2,
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 24),
        };
        var btnOpen = MakeButton("📂  Select file…", click: (_, _) => OpenFileDialogAndLoad(), accent: true);
        var btnFolder = MakeButton("🗂  Select folder…", click: (_, _) => OpenFolderDialogAndLoad(), accent: false);
        var btnSample = MakeButton("Try with sample data", click: (_, _) => LoadSampleData(), accent: false);

        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Anchor = AnchorStyles.None,
            BackColor = Surface,
        };
        stack.Controls.Add(lblTitle);
        stack.Controls.Add(lblSub);
        var btnsRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            BackColor = Surface,
        };
        btnsRow.Controls.Add(btnOpen);
        btnsRow.Controls.Add(btnFolder);
        btnsRow.Controls.Add(btnSample);
        stack.Controls.Add(btnsRow);

        // Center the stack
        var center = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Surface };
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        center.Controls.Add(stack, 0, 0);
        stack.Anchor = AnchorStyles.None;
        center.SetCellPosition(stack, new TableLayoutPanelCellPosition(0, 0));

        p.Controls.Add(center);
        return p;
    }

    private ComboBox MakeCombo(string[] items, int width)
    {
        var cb = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Width = width,
            Height = 30,
            Margin = new Padding(0, 3, 8, 0),
            BackColor = Surface2,
            ForeColor = TextMain,
            Font = new Font(Font.FontFamily, 9F),
        };
        cb.Items.AddRange(items);
        cb.SelectedIndex = 0;
        return cb;
    }

    private Button MakeButton(string text, EventHandler click, bool accent)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            AutoSize = true,
            Height = 32,
            Padding = new Padding(12, 4, 12, 4),
            Margin = new Padding(0, 3, 8, 0),
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        if (accent)
        {
            b.BackColor = Purple;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderSize = 0;
        }
        else
        {
            b.BackColor = Surface;
            b.ForeColor = Text2;
            b.FlatAppearance.BorderColor = Border2;
            b.FlatAppearance.BorderSize = 1;
        }
        b.Click += click;
        return b;
    }

    private static Panel MakeToolStripSeparatorPanel() => new()
    {
        Width = 1,
        Height = 22,
        BackColor = BorderClr,
        Margin = new Padding(4, 7, 4, 0),
    };

    // ── Event wiring ────────────────────────────────────────────────

    private void WireEvents()
    {
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        KeyDown += OnKeyDown;

        _searchTimer = new System.Windows.Forms.Timer { Interval = 220 };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            _searchQuery = _searchBox.Text.Trim().ToLowerInvariant();
            ApplyFilters();
        };
        _searchBox.TextChanged += (_, _) => { _searchTimer.Stop(); _searchTimer.Start(); };

        _levelCombo.SelectedIndexChanged += (_, _) => ApplyFilters();
        _catCombo.SelectedIndexChanged += (_, _) => ApplyFilters();
        _srcCombo.SelectedIndexChanged += (_, _) => ApplyFilters();

        _excludeBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { AddExclude(); e.SuppressKeyPress = true; }
        };

        _grid.CellValueNeeded += OnCellValueNeeded;
        _grid.CellFormatting += OnCellFormatting;
        _grid.SelectionChanged += OnGridSelectionChanged;
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < _filtered.Count) ShowDetailPopup(e.RowIndex);
        };
        _grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _grid.CurrentCell?.RowIndex is int row && row >= 0 && row < _filtered.Count)
            {
                ShowDetailPopup(row);
                e.SuppressKeyPress = true;
            }
        };
        _grid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.RowIndex < _filtered.Count)
            {
                _grid.ClearSelection();
                _grid.Rows[e.RowIndex].Selected = true;
                _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
            }
        };
        _grid.ContextMenuStrip = BuildGridContextMenu();
    }

    private ContextMenuStrip BuildGridContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Show &details…", null, (_, _) => ShowDetailPopup(_grid.CurrentCell?.RowIndex ?? -1))
        { ShortcutKeyDisplayString = "Enter / Double-click" });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Copy line &JSON", null, (_, _) => CopyCurrent(r => r.RawJson)));
        menu.Items.Add(new ToolStripMenuItem("Copy &message", null, (_, _) => CopyCurrent(r => r.Message)));
        menu.Items.Add(new ToolStripMenuItem("Copy &time", null, (_, _) => CopyCurrent(r => r.TimeRaw)));
        return menu;
    }

    private void CopyCurrent(Func<LogEntry, string> selector)
    {
        var row = _grid.CurrentCell?.RowIndex ?? -1;
        if (row < 0 || row >= _filtered.Count) return;
        try { Clipboard.SetText(selector(_filtered[row]) ?? string.Empty); }
        catch { }
    }

    // ── Drag-drop & shortcuts ───────────────────────────────────────

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

        if (paths.Length == 1 && Directory.Exists(paths[0]))
        {
            _ = LoadFolderAsync(paths[0]);
            return;
        }

        var files = new List<string>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p)) files.AddRange(NdjsonParser.EnumerateLogFiles(p));
            else if (File.Exists(p)) files.Add(p);
        }
        if (files.Count == 0) return;

        string label = files.Count == 1
            ? Path.GetFileName(files[0])
            : $"{files.Count} dropped files";
        _ = LoadPathsAsync(files, label);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape && _searchBox.Focused && _searchBox.Text.Length > 0)
        {
            _searchBox.Clear();
            e.SuppressKeyPress = true;
        }
    }

    // ── File loading ────────────────────────────────────────────────

    private void OpenFileDialogAndLoad()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Open NDJSON log",
            Filter = "NDJSON / JSONL (*.ndjson;*.jsonl;*.json;*.log;*.txt)|*.ndjson;*.jsonl;*.json;*.log;*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string label = dlg.FileNames.Length == 1
            ? Path.GetFileName(dlg.FileNames[0])
            : $"{dlg.FileNames.Length} files";
        _ = LoadPathsAsync(dlg.FileNames, label);
    }

    private void OpenFolderDialogAndLoad()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Pick a folder — every *.json / *.ndjson / *.jsonl file inside (recursive) will be aggregated.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _ = LoadFolderAsync(dlg.SelectedPath);
    }

    private async Task LoadFolderAsync(string folder)
    {
        var files = NdjsonParser.EnumerateLogFiles(folder);
        if (files.Count == 0)
        {
            MessageBox.Show(this,
                $"No .json / .ndjson / .jsonl files found under:\n\n{folder}",
                "Empty folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var label = $"{TruncateFolder(folder)}  ·  {files.Count} files";
        await LoadPathsAsync(files, label).ConfigureAwait(true);
    }

    private async Task LoadPathsAsync(IReadOnlyList<string> paths, string displayName)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        try
        {
            ToggleWelcome(false);
            BeginProgress(paths.Count == 1
                ? $"Parsing {Path.GetFileName(paths[0])}…"
                : $"Parsing {paths.Count} files…");
            var progress = new Progress<ParseProgress>(p =>
            {
                _progressBar.Value = Math.Clamp(p.Percent, 0, 100);
                _statusReady.Text = $"Parsing {p.Status} — {p.Percent}%";
            });

            var entries = await Task.Run(() => NdjsonParser.ParseFilesAsync(paths, progress, ct), ct).ConfigureAwait(true);
            ApplyLoadedEntries(entries, displayName);
            _currentFile = paths.Count == 1 ? paths[0] : null;
        }
        catch (OperationCanceledException) { /* swallow */ }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to load:\n\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusReady.Text = "Load failed.";
            ToggleWelcome(_all.Count == 0);
        }
        finally
        {
            EndProgress();
        }
    }

    private void LoadSampleData()
    {
        try
        {
            ToggleWelcome(false);
            BeginProgress("Generating sample data…");
            var text = SampleData.Generate(150);
            var entries = NdjsonParser.ParseText(text, "sample_azure_kudu_stream.ndjson");
            ApplyLoadedEntries(entries, "sample_azure_kudu_stream.ndjson");
            _currentFile = null;
        }
        finally { EndProgress(); }
    }

    private static string TruncateFolder(string folder)
    {
        try
        {
            var name = new DirectoryInfo(folder).Name;
            return string.IsNullOrEmpty(name) ? folder : name;
        }
        catch { return folder; }
    }

    private void ApplyLoadedEntries(List<LogEntry> entries, string displayName)
    {
        _all.Clear();
        _all.AddRange(entries);

        PopulateCombo(_catCombo, "All categories", _all.Select(r => r.Category));
        PopulateCombo(_srcCombo, "All sources", _all.Select(r => r.Source));

        _statusFile.Text = displayName;
        _statusReady.Text = $"{_all.Count:N0} entries parsed.";
        ApplyFilters();
        if (_grid.RowCount > 0) _grid.CurrentCell = _grid.Rows[0].Cells[0];
    }

    private static void PopulateCombo(ComboBox cb, string allLabel, IEnumerable<string> values)
    {
        var distinct = values.Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s).ToArray();
        cb.BeginUpdate();
        cb.Items.Clear();
        cb.Items.Add(allLabel);
        cb.Items.AddRange(distinct);
        cb.SelectedIndex = 0;
        cb.EndUpdate();
    }

    private void CloseFile()
    {
        _all.Clear();
        _filtered = new List<LogEntry>();
        _excludes.Clear();
        _searchBox.Clear();
        _excludeBox.Clear();
        _searchQuery = string.Empty;
        _currentFile = null;
        _statusFile.Text = string.Empty;
        _statusReady.Text = "Drop an NDJSON file or use File > Open…";
        PopulateCombo(_catCombo, "All categories", Array.Empty<string>());
        PopulateCombo(_srcCombo, "All sources", Array.Empty<string>());
        _levelCombo.SelectedIndex = 0;
        UpdateExcludeTagsUi();
        _grid.RowCount = 0;
        _detail.Clear();
        UpdateStats();
        ToggleWelcome(true);
    }

    private void BeginProgress(string text)
    {
        _progressBar.Value = 0;
        _progressBar.Visible = true;
        _statusReady.Text = text;
        UseWaitCursor = true;
    }

    private void EndProgress()
    {
        _progressBar.Visible = false;
        UseWaitCursor = false;
    }

    private void ToggleWelcome(bool show)
    {
        _welcomeOverlay.Visible = show;
        if (show) _welcomeOverlay.BringToFront();
    }

    // ── Filtering ───────────────────────────────────────────────────

    private void ApplyFilters()
    {
        string lvl = _levelCombo.SelectedItem as string ?? "All levels";
        string cat = _catCombo.SelectedItem as string ?? "All categories";
        string src = _srcCombo.SelectedItem as string ?? "All sources";
        bool anyLevel = lvl == "All levels";
        bool anyCat = cat == "All categories";
        bool anySrc = src == "All sources";

        _filtered = _all.Where(r =>
        {
            if (!anyLevel)
            {
                var rl = r.Level.ToLowerInvariant();
                if (lvl == "Verbose/Debug")
                {
                    if (rl != "verbose" && rl != "debug") return false;
                }
                else if (!string.Equals(r.Level, lvl, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            if (!anyCat && !string.Equals(r.Category, cat, StringComparison.Ordinal)) return false;
            if (!anySrc && !string.Equals(r.Source, src, StringComparison.Ordinal)) return false;
            if (_searchQuery.Length > 0 && !r.Haystack.Contains(_searchQuery)) return false;
            if (_excludes.Count > 0 && _excludes.Any(t => r.Haystack.Contains(t))) return false;
            return true;
        }).ToList();

        _grid.RowCount = _filtered.Count;
        _grid.Invalidate();
        UpdateStats();
        if (_grid.RowCount > 0)
        {
            _grid.CurrentCell = _grid.Rows[0].Cells[0];
            ShowDetailFor(_filtered[0]);
        }
        else
        {
            _detail.Clear();
        }
    }

    private void ClearAllFilters()
    {
        _searchBox.Clear();
        _excludeBox.Clear();
        _excludes.Clear();
        _levelCombo.SelectedIndex = 0;
        if (_catCombo.Items.Count > 0) _catCombo.SelectedIndex = 0;
        if (_srcCombo.Items.Count > 0) _srcCombo.SelectedIndex = 0;
        _searchQuery = string.Empty;
        UpdateExcludeTagsUi();
        ApplyFilters();
    }

    private void ToggleDetailPane()
    {
        _split.Panel2Collapsed = !_split.Panel2Collapsed;
    }

    // ── Exclude tags ────────────────────────────────────────────────

    private void AddExclude()
    {
        var term = _excludeBox.Text.Trim().ToLowerInvariant();
        if (term.Length == 0) return;
        if (_excludes.Contains(term))
        {
            _excludeBox.SelectAll();
            return;
        }
        _excludes.Add(term);
        _excludeBox.Clear();
        UpdateExcludeTagsUi();
        ApplyFilters();
    }

    private void RemoveExclude(string term)
    {
        _excludes.Remove(term);
        UpdateExcludeTagsUi();
        ApplyFilters();
    }

    private void UpdateExcludeTagsUi()
    {
        _excludeTags.SuspendLayout();
        _excludeTags.Controls.Clear();
        if (_excludes.Count == 0)
        {
            _excludeTags.Controls.Add(_excludeEmpty);
        }
        else
        {
            foreach (var t in _excludes)
            {
                _excludeTags.Controls.Add(BuildExcludeChip(t));
            }
        }
        _excludeTags.ResumeLayout();
    }

    private Control BuildExcludeChip(string term)
    {
        var panel = new RoundedPanel(topRadius: 14, bottomRadius: 14)
        {
            BackColor = RedBg,
            AutoSize = true,
            Padding = new Padding(10, 2, 4, 2),
            Margin = new Padding(4, 4, 0, 0),
            BorderColor = FromHex("#F5B8BC"),
            BorderWidth = 1,
        };
        var label = new Label
        {
            Text = term,
            AutoSize = true,
            ForeColor = RedT,
            Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold),
            Margin = new Padding(0, 4, 4, 0),
        };
        var x = new Button
        {
            Text = "×",
            FlatStyle = FlatStyle.Flat,
            Width = 18,
            Height = 18,
            Margin = new Padding(0, 3, 0, 0),
            Padding = new Padding(0),
            Cursor = Cursors.Hand,
            BackColor = RedBg,
            ForeColor = RedT,
            Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
            TabStop = false,
        };
        x.FlatAppearance.BorderSize = 0;
        x.Click += (_, _) => RemoveExclude(term);

        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        row.Controls.Add(label);
        row.Controls.Add(x);
        panel.Controls.Add(row);
        return panel;
    }

    // ── Stats ───────────────────────────────────────────────────────

    private void UpdateStats()
    {
        int err = 0, warn = 0, info = 0;
        foreach (var r in _all)
        {
            var l = r.Level;
            if (string.Equals(l, "Error", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(l, "Critical", StringComparison.OrdinalIgnoreCase)) err++;
            else if (string.Equals(l, "Warning", StringComparison.OrdinalIgnoreCase)) warn++;
            else if (string.Equals(l, "Information", StringComparison.OrdinalIgnoreCase)) info++;
        }
        _cardTotal.SetValue(Fmt(_all.Count));
        _cardError.SetValue(Fmt(err));
        _cardWarn.SetValue(Fmt(warn));
        _cardInfo.SetValue(Fmt(info));
        _cardShown.SetValue(Fmt(_filtered.Count));
    }

    private static string Fmt(int n) =>
        n >= 10_000 ? (n / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + "K"
                    : n.ToString("N0", CultureInfo.InvariantCulture);

    // ── Grid virtual mode ───────────────────────────────────────────

    private void OnCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _filtered.Count) { e.Value = string.Empty; return; }
        var r = _filtered[e.RowIndex];
        e.Value = _grid.Columns[e.ColumnIndex].Name switch
        {
            "Index"  => r.Index,
            "Time"   => r.TimeDisplay,
            "Level"  => r.Level,
            "Source" => r.Source,
            "Cat"    => r.Category,
            "Msg"    => r.MessageOneLine,
            _        => string.Empty
        };
    }

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _filtered.Count) return;
        var r = _filtered[e.RowIndex];
        var (bg, fg) = ColorsForLevel(r.Level);
        e.CellStyle!.BackColor = bg;
        e.CellStyle.ForeColor = fg;
        e.CellStyle.SelectionBackColor = Mix(bg, Purple, 0.35);
        e.CellStyle.SelectionForeColor = TextMain;
        if (_grid.Columns[e.ColumnIndex].Name == "Source")
        {
            e.CellStyle.ForeColor = (r.Level.Equals("Error", StringComparison.OrdinalIgnoreCase)
                                  || r.Level.Equals("Critical", StringComparison.OrdinalIgnoreCase))
                ? RedT : PurpleT;
            e.CellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
        }
        else if (_grid.Columns[e.ColumnIndex].Name == "Level")
        {
            e.CellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
            e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
        else if (_grid.Columns[e.ColumnIndex].Name == "Index"
              || _grid.Columns[e.ColumnIndex].Name == "Time")
        {
            e.CellStyle.ForeColor = Mix(fg, Text2, 0.5);
        }
    }

    private static (Color Bg, Color Fg) ColorsForLevel(string level)
    {
        return level.ToLowerInvariant() switch
        {
            "error" or "critical" => (RedBg, RedT),
            "warning"             => (OrangeBg, OrangeT),
            "information"         => (BlueBg, BlueT),
            "verbose" or "debug"  => (Surface2, Text2),
            _                     => (Surface, TextMain),
        };
    }

    private void OnGridSelectionChanged(object? sender, EventArgs e)
    {
        var idx = _grid.CurrentCell?.RowIndex ?? -1;
        if (idx < 0 || idx >= _filtered.Count) { _detail.Clear(); return; }
        ShowDetailFor(_filtered[idx]);
    }

    private void ShowDetailFor(LogEntry r)
    {
        _detail.SuspendLayout();
        _detail.Clear();

        // header line
        AppendStyled($"[{r.Level}]  ", PurpleT, FontStyle.Bold);
        AppendStyled(r.Source, TextMain, FontStyle.Bold);
        AppendStyled($"   ({r.Category})\n", Text2, FontStyle.Regular);

        AppendStyled("Time (UTC): ", Text2, FontStyle.Bold);
        AppendStyled(r.TimeRaw + "\n", TextMain, FontStyle.Regular);

        if (!string.IsNullOrEmpty(r.ShortResource))
        {
            AppendStyled("Site: ", Text2, FontStyle.Bold);
            AppendStyled(r.ShortResource + "\n", TextMain, FontStyle.Regular);
        }
        if (!string.IsNullOrEmpty(r.InstanceId))
        {
            AppendStyled("Instance: ", Text2, FontStyle.Bold);
            AppendStyled(r.InstanceId + "\n", TextMain, FontStyle.Regular);
        }
        if (!string.IsNullOrEmpty(r.ResourceId))
        {
            AppendStyled("ResourceId: ", Text2, FontStyle.Bold);
            AppendStyled(r.ResourceId + "\n", Text2, FontStyle.Regular);
        }
        if (!string.IsNullOrEmpty(r.SourceFile))
        {
            AppendStyled("File: ", Text2, FontStyle.Bold);
            AppendStyled(PrettySourcePath(r.SourceFile) + "\n", Text2, FontStyle.Regular);
        }

        AppendStyled("\nMessage\n", Text2, FontStyle.Bold);
        AppendStyled(string.IsNullOrEmpty(r.Message) ? "(empty)\n" : r.Message + "\n", TextMain, FontStyle.Regular);

        if (!string.IsNullOrEmpty(r.Stacktrace))
        {
            AppendStyled("\nStacktrace\n", RedT, FontStyle.Bold);
            AppendStyled(r.Stacktrace + "\n", RedT, FontStyle.Regular);
        }

        AppendStyled("\nRaw JSON\n", Text2, FontStyle.Bold);
        AppendStyled(r.RawJson, TextMain, FontStyle.Regular);

        _detail.SelectionStart = 0;
        _detail.SelectionLength = 0;
        _detail.ResumeLayout();
    }

    private void AppendStyled(string text, Color color, FontStyle style)
    {
        _detail.SelectionStart = _detail.TextLength;
        _detail.SelectionLength = 0;
        _detail.SelectionColor = color;
        _detail.SelectionFont = new Font(_detail.Font, style);
        _detail.AppendText(text);
    }

    private void CopyRawJson(LogEntry r)
    {
        try { Clipboard.SetText(r.RawJson); }
        catch { /* clipboard occasionally locked */ }
    }

    private static string PrettySourcePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        // For Azure structured-log paths like "...\m=05\d=27\h=17\m=00\PT1H.json"
        // show only the bucket-encoded suffix instead of the absolute path.
        var parts = path.Split('\\', '/');
        int firstBucket = -1;
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (p.Length >= 3 && (p[0] == 'm' || p[0] == 'd' || p[0] == 'h' || p[0] == 'y') && p[1] == '=')
            {
                firstBucket = i;
                break;
            }
        }
        return firstBucket >= 0
            ? string.Join('\\', parts[firstBucket..])
            : Path.GetFileName(path);
    }

    // ── CSV export ──────────────────────────────────────────────────

    private void ExportCsv()
    {
        if (_filtered.Count == 0) { MessageBox.Show(this, "Nothing to export.", "Export CSV"); return; }
        using var dlg = new SaveFileDialog
        {
            Title = "Export to CSV",
            Filter = "CSV (*.csv)|*.csv",
            FileName = "ndjson_export.csv",
            AddExtension = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var fs = new FileStream(dlg.FileName, FileMode.Create, FileAccess.Write);
            using var w = new StreamWriter(fs, new UTF8Encoding(true));
            w.WriteLine(string.Join(",", Csv("Index"), Csv("TimestampUTC"), Csv("LogLevel"),
                Csv("TraceSource"), Csv("LoggingCategory"), Csv("AppResourcePath"), Csv("MessageSummary"), Csv("SourceFile")));
            foreach (var r in _filtered)
            {
                w.WriteLine(string.Join(",",
                    Csv(r.Index.ToString(CultureInfo.InvariantCulture)),
                    Csv(r.TimeDisplay),
                    Csv(r.Level),
                    Csv(r.Source),
                    Csv(r.Category),
                    Csv(r.ShortResource),
                    Csv(r.Message),
                    Csv(PrettySourcePath(r.SourceFile))));
            }
            _statusReady.Text = $"{_filtered.Count:N0} rows exported.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to write CSV:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"").Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ") + "\"";
    }

    // ── About ───────────────────────────────────────────────────────

    private void ShowAbout()
    {
        MessageBox.Show(this,
            "NDJSON Event Log Viewer\nv1.0\n\nDesktop port of the in-house HTML log viewer.\nReads Azure App Service structured JSON line streams.",
            "About",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static Color FromHex(string hex)
    {
        if (hex.StartsWith("#")) hex = hex[1..];
        return Color.FromArgb(
            Convert.ToInt32(hex.Substring(0, 2), 16),
            Convert.ToInt32(hex.Substring(2, 2), 16),
            Convert.ToInt32(hex.Substring(4, 2), 16));
    }

    private static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    // ── Custom controls ─────────────────────────────────────────────

    private sealed class StatCard : Panel
    {
        private readonly Label _lbl, _val;

        public StatCard(string caption, string value, Color valColor)
        {
            BackColor = Color.White;
            Margin = new Padding(0, 0, 12, 0);
            Padding = new Padding(14, 12, 14, 12);
            Dock = DockStyle.Fill;
            Height = 64;

            _lbl = new Label
            {
                Text = caption.ToUpperInvariant(),
                Dock = DockStyle.Top,
                AutoSize = true,
                ForeColor = FromHex("#6B7684"),
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                Margin = new Padding(0),
            };
            _val = new Label
            {
                Text = value,
                Dock = DockStyle.Bottom,
                AutoSize = true,
                ForeColor = valColor,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                Margin = new Padding(0),
            };
            Controls.Add(_val);
            Controls.Add(_lbl);
        }

        public void SetValue(string v) => _val.Text = v;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(FromHex("#E5E8EB"));
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedRect(r, 10);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(pen, path);
        }
    }

    private sealed class RoundedPanel : Panel
    {
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int TopRadius { get; set; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int BottomRadius { get; set; }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; set; } = FromHex("#E5E8EB");

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int BorderWidth { get; set; } = 1;

        public RoundedPanel(int topRadius, int bottomRadius)
        {
            TopRadius = topRadius;
            BottomRadius = bottomRadius;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (BorderWidth <= 0) return;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = RoundedRectMixed(r, TopRadius, BottomRadius);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(BorderColor, BorderWidth);
            e.Graphics.DrawPath(pen, path);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath RoundedRectMixed(Rectangle r, int topRadius, int bottomRadius)
    {
        var path = new GraphicsPath();
        int td = topRadius * 2;
        int bd = bottomRadius * 2;
        if (topRadius > 0)
        {
            path.AddArc(r.X, r.Y, td, td, 180, 90);
            path.AddArc(r.Right - td, r.Y, td, td, 270, 90);
        }
        else
        {
            path.AddLine(r.X, r.Y, r.Right, r.Y);
        }
        if (bottomRadius > 0)
        {
            path.AddArc(r.Right - bd, r.Bottom - bd, bd, bd, 0, 90);
            path.AddArc(r.X, r.Bottom - bd, bd, bd, 90, 90);
        }
        else
        {
            path.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
        }
        path.CloseFigure();
        return path;
    }

    private sealed class SoftColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => FromHex("#F0ECFD");
        public override Color MenuItemSelectedGradientBegin => FromHex("#F0ECFD");
        public override Color MenuItemSelectedGradientEnd => FromHex("#F0ECFD");
        public override Color MenuItemBorder => FromHex("#7C56E8");
        public override Color MenuStripGradientBegin => Color.White;
        public override Color MenuStripGradientEnd => Color.White;
        public override Color ToolStripDropDownBackground => Color.White;
        public override Color ImageMarginGradientBegin => Color.White;
        public override Color ImageMarginGradientMiddle => Color.White;
        public override Color ImageMarginGradientEnd => Color.White;
    }
}
