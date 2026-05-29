namespace NdjsonLogViewer;

public sealed partial class MainForm
{
    private void ShowDetailPopup(int filteredIndex)
    {
        if (filteredIndex < 0 || filteredIndex >= _filtered.Count) return;
        using var dlg = new DetailPopup(_filtered, filteredIndex);
        dlg.ShowDialog(this);
    }

    private sealed class DetailPopup : Form
    {
        private readonly IReadOnlyList<LogEntry> _entries;
        private int _idx;
        private RichTextBox _content = null!;
        private Button _btnPrev = null!, _btnNext = null!, _btnCopy = null!, _btnClose = null!;
        private Label _position = null!;

        public DetailPopup(IReadOnlyList<LogEntry> entries, int startIndex)
        {
            _entries = entries;
            _idx = Math.Clamp(startIndex, 0, entries.Count - 1);

            Text = "Log entry details";
            BackColor = Surface;
            ClientSize = new Size(900, 680);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(560, 380);
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            KeyPreview = true;
            Font = new Font("Segoe UI", 9F);
            ShowIcon = false;

            BuildUI();
            Render();
        }

        private void BuildUI()
        {
            _content = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Surface,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9.75F),
                ScrollBars = RichTextBoxScrollBars.Both,
                DetectUrls = true,
                HideSelection = false,
            };
            var contentHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                Padding = new Padding(22, 16, 22, 12),
            };
            contentHost.Controls.Add(_content);

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = Surface2,
                Padding = new Padding(14, 11, 14, 11),
            };

            _btnPrev = MakeFooterButton("◀  Prev", (_, _) => Navigate(-1), accent: false);
            _btnNext = MakeFooterButton("Next  ▶", (_, _) => Navigate(+1), accent: false);
            _position = new Label
            {
                AutoSize = true,
                ForeColor = Text2,
                Font = new Font("Segoe UI", 9F),
                Margin = new Padding(14, 9, 14, 0),
            };
            _btnCopy = MakeFooterButton("Copy JSON", (_, _) => CopyJson(), accent: true);
            _btnClose = MakeFooterButton("Close", (_, _) => Close(), accent: false);

            var leftFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Surface2,
            };
            leftFlow.Controls.Add(_btnPrev);
            leftFlow.Controls.Add(_btnNext);
            leftFlow.Controls.Add(_position);

            var rightFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Surface2,
            };
            rightFlow.Controls.Add(_btnClose);
            rightFlow.Controls.Add(_btnCopy);

            footer.Controls.Add(rightFlow);
            footer.Controls.Add(leftFlow);

            Controls.Add(contentHost);
            Controls.Add(footer);

            CancelButton = _btnClose;
            KeyDown += OnKeyDown;
        }

        private static Button MakeFooterButton(string text, EventHandler click, bool accent)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                AutoSize = true,
                Height = 34,
                Padding = new Padding(14, 4, 14, 4),
                Margin = new Padding(0, 0, 8, 0),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                TabStop = false,
            };
            if (accent)
            {
                b.BackColor = Purple;
                b.ForeColor = Color.White;
                b.FlatAppearance.BorderSize = 0;
            }
            else
            {
                b.BackColor = Color.White;
                b.ForeColor = Text2;
                b.FlatAppearance.BorderColor = Border2;
                b.FlatAppearance.BorderSize = 1;
            }
            b.Click += click;
            return b;
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Escape:
                    Close(); e.Handled = true; break;
                case Keys.Right:
                case Keys.Down:
                case Keys.PageDown:
                case Keys.J:
                    Navigate(+1); e.Handled = e.SuppressKeyPress = true; break;
                case Keys.Left:
                case Keys.Up:
                case Keys.PageUp:
                case Keys.K:
                    Navigate(-1); e.Handled = e.SuppressKeyPress = true; break;
                case Keys.C when e.Control && _content.SelectionLength == 0:
                    CopyJson(); e.Handled = true; break;
            }
        }

        private void Navigate(int delta)
        {
            int next = Math.Clamp(_idx + delta, 0, _entries.Count - 1);
            if (next == _idx) return;
            _idx = next;
            Render();
        }

        private void Render()
        {
            var r = _entries[_idx];
            Text = $"Log entry #{r.Index}  ·  {r.Level}";
            _position.Text = $"Showing {_idx + 1:N0} of {_entries.Count:N0}   ·   Global #{r.Index:N0}";
            _btnPrev.Enabled = _idx > 0;
            _btnNext.Enabled = _idx < _entries.Count - 1;

            _content.SuspendLayout();
            _content.Clear();

            // Level badge + source
            Append($"  {r.Level.ToUpperInvariant()}  ", LevelFg(r.Level), LevelBg(r.Level), FontStyle.Bold, 11F);
            Append("   ", TextMain, Surface, FontStyle.Regular, 11F);
            Append(r.Source, PurpleT, Surface, FontStyle.Bold, 11F);
            Append("\n", TextMain, Surface, FontStyle.Regular, 11F);
            Append("Category: ", Text2, Surface, FontStyle.Regular, 9F);
            Append(r.Category + "\n\n", TextMain, Surface, FontStyle.Regular, 9F);

            AppendMeta("Time (UTC)", string.IsNullOrEmpty(r.TimeRaw) ? "—" : r.TimeRaw);
            if (!string.IsNullOrEmpty(r.ShortResource)) AppendMeta("Site",      r.ShortResource);
            if (!string.IsNullOrEmpty(r.InstanceId))    AppendMeta("Instance",  r.InstanceId);
            if (!string.IsNullOrEmpty(r.SourceFile))    AppendMeta("File",      PrettySourcePath(r.SourceFile));
            if (!string.IsNullOrEmpty(r.ResourceId))    AppendMeta("ResourceId", r.ResourceId);

            AppendSection("MESSAGE", Text2);
            Append(string.IsNullOrEmpty(r.Message) ? "(empty)\n" : r.Message + "\n",
                TextMain, Surface, FontStyle.Regular, 10F);

            if (!string.IsNullOrEmpty(r.Stacktrace))
            {
                AppendSection("STACKTRACE", RedT);
                Append(r.Stacktrace + "\n", RedT, Surface, FontStyle.Regular, 9.25F);
            }

            AppendSection("RAW JSON", Text2);
            Append(r.RawJson, TextMain, Surface, FontStyle.Regular, 9.25F);

            _content.SelectionStart = 0;
            _content.SelectionLength = 0;
            _content.ScrollToCaret();
            _content.ResumeLayout();
        }

        private void AppendMeta(string key, string value)
        {
            Append($"{key,-12}  ", Text2, Surface, FontStyle.Bold, 9F);
            Append(value + "\n", TextMain, Surface, FontStyle.Regular, 9F);
        }

        private void AppendSection(string title, Color color)
        {
            Append($"\n{title}\n", color, Surface, FontStyle.Bold, 8.75F);
        }

        private void Append(string text, Color fg, Color bg, FontStyle style, float size)
        {
            _content.SelectionStart = _content.TextLength;
            _content.SelectionLength = 0;
            _content.SelectionFont = new Font("Consolas", size, style);
            _content.SelectionColor = fg;
            _content.SelectionBackColor = bg;
            _content.AppendText(text);
        }

        private void CopyJson()
        {
            try { Clipboard.SetText(_entries[_idx].RawJson); }
            catch { /* clipboard occasionally locked */ }
        }

        private static Color LevelBg(string level) => level.ToLowerInvariant() switch
        {
            "error" or "critical" => RedBg,
            "warning"             => OrangeBg,
            "information"         => BlueBg,
            "verbose" or "debug"  => Surface2,
            _                     => Surface2,
        };

        private static Color LevelFg(string level) => level.ToLowerInvariant() switch
        {
            "error" or "critical" => RedT,
            "warning"             => OrangeT,
            "information"         => BlueT,
            "verbose" or "debug"  => Text2,
            _                     => TextMain,
        };
    }
}
