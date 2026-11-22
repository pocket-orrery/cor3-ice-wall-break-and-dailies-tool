using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GlyphFinder
{
    // A modeless tracker for one market round. The round itself lives in FinderWindow, so closing this window keeps it.
    internal sealed class JobGuideWindow : Form
    {
        internal const int ColDone = 0, ColServer = 1, ColType = 2, ColAccess = 3, ColTargets = 4, ColNote = 5;
        private const int EntryHeight = 280, FooterHeight = 108;
        private const int IpCopySize = 14, IpCopySpace = 22;
        private static readonly Size NormalSize = new Size(960, 680);
        private static readonly Size CompactSize = new Size(540, 320);
        private static readonly Size NormalMinimum = new Size(720, 540);

        private readonly JobRound round;
        private readonly Func<string, string, Task<Bitmap>> pickImage;
        private readonly Func<Bitmap, List<string>> recognize;
        private readonly Func<Bitmap, List<OcrTextLine>> recognizeLines;
        private readonly Action<string> writeClipboard;
        private UiLanguage language;

        private readonly TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(14, 12, 14, 10) };
        private readonly Segmented viewSwitch = new Segmented();
        private readonly Label roundLabel = new Label { AutoSize = true, ForeColor = Palette.Muted, Margin = new Padding(12, 0, 0, 0) };
        private readonly ProgressLine progress = new ProgressLine { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 10) };
        private readonly FlatButton compactButton = new FlatButton { Kind = ButtonKind.Ghost };
        private readonly FlatButton onTop = new FlatButton { Kind = ButtonKind.Ghost, Margin = Padding.Empty };

        private readonly Card listCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(6), Margin = new Padding(0, 0, 0, 10) };
        private readonly DataGridView grid = new DataGridView();
        private readonly DataGridView catalog = new DataGridView();
        private readonly Panel catalogPanel = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Palette.Panel };
        private readonly TextBox generalNote = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = true,
            ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
            ForeColor = Palette.Muted, BackColor = Palette.Panel
        };
        private readonly Label emptyNote = new Label { Dock = DockStyle.Fill, ForeColor = Palette.Muted, BackColor = Palette.Panel, TextAlign = ContentAlignment.MiddleCenter };

        private readonly Card entryCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 8), Margin = new Padding(0, 0, 0, 8) };
        private readonly Label typeLabel = Caption(), serverLabel = Caption(), accessLabel = Caption(), targetsLabel = Caption(), noteLabel = Caption();
        private readonly FlatButton typeButton = new FlatButton { Chevron = true, MinimumSize = new Size(190, 30), AutoSize = false, Width = 190 };
        private readonly ContextMenuStrip typeMenu = new ContextMenuStrip();
        private readonly InputBox serverInput = new InputBox(false) { Width = 180 };
        private readonly Segmented accessBox = new Segmented();
        private readonly InputBox targetsInput = new InputBox(true) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0) };
        private readonly InputBox noteInput = new InputBox(false) { Dock = DockStyle.Top, Margin = Padding.Empty };
        private readonly FlatButton addButton = new FlatButton { Icon = ButtonGlyph.Plus };
        private readonly FlatButton readTargets = new FlatButton { Icon = ButtonGlyph.Select };
        private readonly FlatButton importJob = new FlatButton { Kind = ButtonKind.Primary, Icon = ButtonGlyph.Select };
        private readonly Label hint = new Label { Dock = DockStyle.Top, AutoSize = true, ForeColor = Palette.Muted, BackColor = Palette.Panel };
        private readonly Label caution = new Label { Dock = DockStyle.Top, AutoSize = true, ForeColor = Palette.Accent, BackColor = Palette.Panel };
        private readonly TextBox serverBox, targetsBox, noteBox;
        private JobEntry firstIpClick;
        private string firstIpText;
        private bool openingIpEditor;
        private int typeIndex;

        private readonly FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty };
        private readonly FlatButton copy = new FlatButton { Icon = ButtonGlyph.Copy }, delete = new FlatButton { Icon = ButtonGlyph.Trash }, clear = new FlatButton { Kind = ButtonKind.Ghost };
        private readonly Label status = new Label { Dock = DockStyle.Fill, ForeColor = Palette.Muted, UseMnemonic = false };
        private readonly ContextMenuStrip rowMenu = new ContextMenuStrip();
        private readonly ToolTip tips = Theme.MakeToolTip();
        private readonly Timer clock = new Timer { Interval = 30000 };
        private readonly Font doneFont, monoFont, monoDoneFont, smallFont, headerFont;

        private bool refreshing, refreshPending, sizingRows, sizePending, compact, showingGuide;
        private Size normalClientSize = NormalSize;
        private JobEntry selectAfterRefresh;
        private int hoverRow = -1;

        internal JobGuideWindow(UiLanguage language, JobRound round, Func<string, string, Task<Bitmap>> pickImage = null,
            Func<Bitmap, List<string>> recognize = null, Func<Bitmap, List<OcrTextLine>> recognizeLines = null,
            Action<string> writeClipboard = null)
        {
            this.language = language;
            this.round = round;
            this.pickImage = pickImage;
            this.recognize = recognize ?? WindowsOcr.Recognize;
            this.recognizeLines = recognizeLines ?? (recognize == null
                ? (Func<Bitmap, List<OcrTextLine>>)WindowsOcr.RecognizeLines : null);
            this.writeClipboard = writeClipboard ?? Clipboard.SetText;
            serverBox = serverInput.Box;
            targetsBox = targetsInput.Box;
            noteBox = noteInput.Box;
            BackColor = Palette.Background;
            ForeColor = Palette.Text;
            Font = Theme.Ui(9f);
            doneFont = new Font(Font, FontStyle.Strikeout);
            monoFont = Theme.Mono(9f);
            monoDoneFont = new Font(monoFont, FontStyle.Strikeout);
            smallFont = Theme.Ui(8f);
            headerFont = Theme.Ui(8.5f);
            Icon icon = Theme.AppIcon();
            if (icon != null) Icon = icon;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = NormalSize;
            MinimumSize = NormalMinimum;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            KeyPreview = true;

            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, EntryHeight));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, FooterHeight));
            Controls.Add(layout);

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 4, Margin = Padding.Empty };
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            foreach (Control c in new Control[] { viewSwitch, roundLabel }) c.Anchor = AnchorStyles.Left;
            foreach (Control c in new Control[] { compactButton, onTop }) c.Anchor = AnchorStyles.Right;
            header.Controls.Add(viewSwitch, 0, 0);
            header.Controls.Add(roundLabel, 1, 0);
            header.Controls.Add(compactButton, 2, 0);
            header.Controls.Add(onTop, 3, 0);
            layout.Controls.Add(header, 0, 0);
            layout.Controls.Add(progress, 0, 1);

            ConfigureGrid();
            ConfigureCatalog();
            var notesPanel = new Panel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(8, 6, 0, 8) };
            notesPanel.Controls.Add(generalNote);
            Theme.DarkScrollBars(generalNote);
            catalogPanel.Controls.Add(catalog);
            catalogPanel.Controls.Add(notesPanel);
            listCard.Controls.Add(grid);
            listCard.Controls.Add(emptyNote);
            listCard.Controls.Add(catalogPanel);
            layout.Controls.Add(listCard, 0, 2);

            BuildEntryCard();
            layout.Controls.Add(entryCard, 0, 3);

            footer.Controls.AddRange(new Control[] { importJob, copy, delete, clear });
            var footerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            footerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            footerLayout.Controls.Add(footer, 0, 0);
            footerLayout.Controls.Add(status, 0, 1);
            layout.Controls.Add(footerLayout, 0, 4);
            AcceptButton = addButton;

            viewSwitch.SelectedIndexChanged += delegate { ShowGuide(viewSwitch.SelectedIndex == 1); };
            compactButton.Click += delegate { SetCompact(!compact); };
            onTop.Click += delegate { SetOnTop(!onTop.Checked); };
            addButton.Click += delegate { AddJob(); };
            readTargets.Enabled = importJob.Enabled = pickImage != null;
            readTargets.Click += async delegate { await ReadTargets(); };
            importJob.Click += async delegate { await ImportJob(); };
            copy.Click += delegate { CopyText(round.Summary(this.language), Translate("已复制本轮汇总。", "Round summary copied.")); };
            delete.Click += delegate { DeleteSelected(); };
            clear.Click += delegate
            {
                if (MessageBox.Show(this, Translate("市场已刷新？清空本轮所有记录。", "Market refreshed? Clear every job in this round."), Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                    ClearRound();
            };
            round.Changed += RoundChanged;
            clock.Tick += delegate { UpdateHeader(); };
            clock.Start();

            SelectType(0);
            accessBox.SelectedIndex = 0;
            viewSwitch.SelectedIndex = 0;
            ApplyLanguage();
            RefreshGrid();
        }

        private string Translate(string chinese, string english)
        {
            return language.Text(chinese, english);
        }

        private static Label Caption()
        {
            return new Label { AutoSize = true, ForeColor = Palette.Muted, BackColor = Palette.Panel, Margin = new Padding(0, 8, 8, 0) };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
        }

        private void StyleGrid(DataGridView view)
        {
            view.Dock = DockStyle.Fill;
            view.AllowUserToAddRows = false;
            view.AllowUserToDeleteRows = false;
            view.AllowUserToResizeRows = false;
            view.AllowUserToResizeColumns = false;
            view.MultiSelect = false;
            view.RowHeadersVisible = false;
            view.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            view.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            view.BackgroundColor = Palette.Panel;
            view.BorderStyle = BorderStyle.None;
            view.CellBorderStyle = DataGridViewCellBorderStyle.None;
            view.GridColor = Palette.Panel;
            view.EnableHeadersVisualStyles = false;
            view.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            view.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            view.ColumnHeadersHeight = 32;
            view.ColumnHeadersDefaultCellStyle.BackColor = Palette.Panel;
            view.ColumnHeadersDefaultCellStyle.ForeColor = Palette.Muted;
            view.ColumnHeadersDefaultCellStyle.SelectionBackColor = Palette.Panel;
            view.DefaultCellStyle.BackColor = Palette.Panel;
            view.DefaultCellStyle.ForeColor = Palette.Text;
            view.DefaultCellStyle.SelectionBackColor = Palette.Hover;
            view.DefaultCellStyle.SelectionForeColor = Palette.Text;
            view.DefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
            view.CellPainting += PaintHeader;
            Theme.DarkScrollBars(view);
        }

        private void PaintHeader(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex >= 0) return;
            Graphics g = e.Graphics;
            using (var back = new SolidBrush(Palette.Panel)) g.FillRectangle(back, e.CellBounds);
            using (var line = new Pen(Palette.Border))
                g.DrawLine(line, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
            if (e.ColumnIndex >= 0)
                TextRenderer.DrawText(g, ((DataGridView)sender).Columns[e.ColumnIndex].HeaderText, headerFont,
                    new Rectangle(e.CellBounds.X + 10, e.CellBounds.Y, e.CellBounds.Width - 12, e.CellBounds.Height - 2), Palette.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            e.Handled = true;
        }

        private void ConfigureGrid()
        {
            StyleGrid(grid);
            grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
            var done = new DataGridViewCheckBoxColumn { Width = 46, ReadOnly = true };
            var server = new DataGridViewTextBoxColumn { Width = 140 };
            var type = new DataGridViewTextBoxColumn { Width = 170, ReadOnly = true };
            var access = new DataGridViewTextBoxColumn { Width = 118, ReadOnly = true };
            var targets = new DataGridViewTextBoxColumn { AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 66 };
            targets.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            var note = new DataGridViewTextBoxColumn { AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 34 };
            note.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.Columns.AddRange(done, server, type, access, targets, note);
            foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            Theme.Style(rowMenu);
            grid.ContextMenuStrip = rowMenu;

            grid.CellPainting += PaintCell;
            grid.ColumnWidthChanged += delegate { QueueGridSize(); };
            grid.CellValueChanged += GridValueChanged;
            grid.CellBeginEdit += delegate(object sender, DataGridViewCellCancelEventArgs e)
            {
                if (e.ColumnIndex == ColTargets && IpEntry(e.RowIndex) != null && !openingIpEditor) e.Cancel = true;
            };
            grid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            grid.SelectionChanged += delegate { UpdateActions(); grid.Invalidate(); };
            grid.EditingControlShowing += delegate(object sender, DataGridViewEditingControlShowingEventArgs e)
            {
                e.Control.BackColor = Palette.Raised;
                e.Control.ForeColor = Palette.Text;
                e.Control.Font = grid.CurrentCell != null && grid.CurrentCell.ColumnIndex == ColTargets ? monoFont : Font;
            };
            grid.CellMouseEnter += delegate(object sender, DataGridViewCellEventArgs e) { SetHoverRow(e.RowIndex); };
            grid.MouseLeave += delegate { SetHoverRow(-1); };
            grid.CellMouseDown += delegate(object sender, DataGridViewCellMouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
                    grid.CurrentCell = grid.Rows[e.RowIndex].Cells[Math.Max(ColServer, e.ColumnIndex)];
                if (e.Button != MouseButtons.Left) return;
                JobEntry entry = e.ColumnIndex == ColTargets ? IpEntry(e.RowIndex) : null;
                if (e.Clicks == 1)
                {
                    firstIpClick = entry;
                    firstIpText = entry == null ? null : IpAtText(entry, TargetCell(e.RowIndex), e.Location);
                }
                if (entry == null || grid.IsCurrentCellInEditMode) return;
                string ip = IpAtCopyButton(entry, TargetCell(e.RowIndex), e.Location);
                if (ip != null) CopyText(ip, Translate("已复制 ", "Copied ") + ip);
            };
            grid.CellMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || e.RowIndex < 0) return;
                var entry = grid.Rows[e.RowIndex].Tag as JobEntry;
                if (entry == null) return;
                if (e.ColumnIndex == ColDone) ToggleDone(entry);
                else if (e.ColumnIndex == ColAccess && entry.Type.NeedsServer)
                    ChangeAccess(entry, (JobAccess)(((int)entry.Access + 1) % JobCatalog.AccessNames.Length));
            };
            grid.CellMouseDoubleClick += delegate(object sender, DataGridViewCellMouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || e.RowIndex < 0 || e.ColumnIndex < 0) return;
                var cell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                if (cell.ReadOnly) return;
                JobEntry entry = e.ColumnIndex == ColTargets ? IpEntry(e.RowIndex) : null;
                if (entry != null && (entry != firstIpClick || firstIpText == null ||
                    firstIpText != IpAtText(entry, TargetCell(e.RowIndex), e.Location))) return;
                if (grid.CurrentCell != cell) grid.CurrentCell = cell;
                openingIpEditor = entry != null;
                try { grid.BeginEdit(true); }
                finally { openingIpEditor = false; }
            };
            grid.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (grid.IsCurrentCellInEditMode) return;
                if (e.KeyCode == Keys.Delete) DeleteSelected();
                else if (e.KeyCode == Keys.Space && SelectedEntry() != null) ToggleDone(SelectedEntry());
                else return;
                e.Handled = true;
            };
            rowMenu.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs e) { e.Cancel = !BuildRowMenu(); };
        }

        private void SetHoverRow(int row)
        {
            if (row == hoverRow) return;
            if (hoverRow >= 0 && hoverRow < grid.Rows.Count) grid.InvalidateRow(hoverRow);
            hoverRow = row;
            if (row >= 0 && row < grid.Rows.Count) grid.InvalidateRow(row);
        }

        private static bool StartsGroup(DataGridView view, int row)
        {
            return row == 0 || ((JobEntry)view.Rows[row - 1].Tag).ServerKey != ((JobEntry)view.Rows[row].Tag).ServerKey;
        }

        internal bool IsGroupStart(int row)
        {
            return StartsGroup(grid, row);
        }

        internal bool IsCompact { get { return compact; } }

        private int IpLineHeight { get { return monoFont.Height + 3; } }

        private JobEntry IpEntry(int row)
        {
            var entry = row >= 0 ? grid.Rows[row].Tag as JobEntry : null;
            return entry != null && entry.Type.Targets == TargetKind.Ip ? entry : null;
        }

        private Rectangle TargetCell(int row)
        {
            return new Rectangle(0, 0, grid.Columns[ColTargets].Width, grid.Rows[row].Height);
        }

        private Rectangle IpTargetLine(Rectangle cell, int index, int count)
        {
            int top = cell.Top + (cell.Height - count * IpLineHeight) / 2 + index * IpLineHeight;
            return new Rectangle(cell.Left + 10, top, Math.Max(0, cell.Width - 16), IpLineHeight);
        }

        internal string IpAtCopyButton(JobEntry entry, Rectangle cell, Point cursor)
        {
            return HitIp(entry, cell, cursor, true);
        }

        private string IpAtText(JobEntry entry, Rectangle cell, Point cursor)
        {
            // The empty target cell must also be editable.
            return entry.Targets.Count == 0 ? "" : HitIp(entry, cell, cursor, false);
        }

        private string HitIp(JobEntry entry, Rectangle cell, Point cursor, bool copy)
        {
            for (int i = 0; i < entry.Targets.Count; i++)
            {
                Rectangle line = IpTargetLine(cell, i, entry.Targets.Count);
                if (copy)
                {
                    if (line.Width < IpCopySize) return null;
                    line.Width = Math.Min(line.Width, IpCopySpace);
                }
                else line = Rectangle.FromLTRB(line.Left + IpCopySpace, line.Top, line.Right, line.Bottom);
                if (line.Contains(cursor)) return entry.Targets[i];
            }
            return null;
        }

        private void PaintCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            DataGridViewRow row = grid.Rows[e.RowIndex];
            var entry = row.Tag as JobEntry;
            if (entry == null) return;
            Graphics g = e.Graphics;
            Rectangle b = e.CellBounds;
            bool selected = row.Selected;
            using (var back = new SolidBrush(selected ? Palette.Hover : e.RowIndex == hoverRow ? Palette.RowHover : Palette.Panel))
                g.FillRectangle(back, b);
            if (e.RowIndex > 0 && StartsGroup(grid, e.RowIndex))
                using (var line = new Pen(Palette.Border)) g.DrawLine(line, b.Left, b.Top, b.Right, b.Top);
            if (selected && e.ColumnIndex == ColDone)
                Theme.FillRound(g, new Rectangle(b.Left, b.Top + 6, 3, b.Height - 12), 1.5f, Palette.Primary, Palette.Primary);

            Color text = entry.Done ? Palette.Faint : Palette.Text;
            var inner = new Rectangle(b.X + 10, b.Y, b.Width - 16, b.Height);
            const TextFormatFlags line1 = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            switch (e.ColumnIndex)
            {
                case ColDone:
                    var circle = new RectangleF(b.X + 15, b.Y + b.Height / 2f - 8, 16, 16);
                    if (entry.Done)
                    {
                        using (var fill = new SolidBrush(Palette.Success)) g.FillEllipse(fill, circle);
                        using (var pen = new Pen(Palette.Panel, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                            g.DrawLines(pen, new[] { new PointF(circle.X + 4.5f, circle.Y + 8.5f), new PointF(circle.X + 7, circle.Y + 11),
                                new PointF(circle.X + 11.5f, circle.Y + 5.5f) });
                    }
                    else
                        using (var pen = new Pen(e.RowIndex == hoverRow || selected ? Palette.Muted : Palette.Faint, 1.6f)) g.DrawEllipse(pen, circle);
                    break;
                case ColServer:
                    TextRenderer.DrawText(g, JobRound.GroupName(entry, language), Font, Rectangle.Inflate(inner, 0, -6),
                        entry.Type.NeedsServer ? text : Palette.Muted, WrapText | TextFormatFlags.VerticalCenter);
                    break;
                case ColType:
                    Color dot = entry.Done ? Palette.Blend(entry.Type.Color, Palette.Faint, 130) : entry.Type.Color;
                    using (var fill = new SolidBrush(dot)) g.FillEllipse(fill, inner.X, b.Y + b.Height / 2f - 3, 6, 6);
                    TextRenderer.DrawText(g, entry.Type.Name, entry.Done ? doneFont : Font,
                        new Rectangle(inner.X + 13, b.Y + 6, inner.Width - 13, b.Height - 12),
                        entry.Done ? text : entry.Type.Color, WrapText | TextFormatFlags.VerticalCenter);
                    break;
                case ColAccess:
                    if (!entry.Type.NeedsServer) break;
                    DrawAccessPill(g, entry, inner, e.RowIndex == hoverRow);
                    break;
                case ColTargets:
                    if (entry.Targets.Count == 0)
                        TextRenderer.DrawText(g, "—", Font, inner, Palette.Faint, line1);
                    else if (entry.Type.Targets == TargetKind.Ip)
                    {
                        Font font = entry.Done ? monoDoneFont : monoFont;
                        Color icon = selected || e.RowIndex == hoverRow ? Palette.Primary : Palette.Muted;
                        for (int i = 0; i < entry.Targets.Count; i++)
                        {
                            Rectangle line = IpTargetLine(b, i, entry.Targets.Count);
                            if (line.Width < IpCopySize) break;
                            var button = new Rectangle(line.Left, line.Top + (line.Height - IpCopySize) / 2, IpCopySize, IpCopySize);
                            Theme.DrawGlyph(g, ButtonGlyph.Copy, button, icon);
                            var value = new Rectangle(line.Left + IpCopySpace, line.Top, line.Width - IpCopySpace, line.Height);
                            if (value.Width > 0) TextRenderer.DrawText(g, entry.Targets[i], font, value, text, line1);
                        }
                    }
                    else
                        TextRenderer.DrawText(g, string.Join(Environment.NewLine, entry.Targets), entry.Done ? monoDoneFont : monoFont,
                            Rectangle.Inflate(inner, 0, -6), text, WrapText | TextFormatFlags.VerticalCenter);
                    break;
                case ColNote:
                    TextRenderer.DrawText(g, entry.Note, Font, Rectangle.Inflate(inner, 0, -6), entry.Done ? Palette.Faint : Palette.Muted,
                        WrapText | TextFormatFlags.VerticalCenter);
                    break;
            }
            e.Handled = true;
        }

        private const TextFormatFlags WrapText = TextFormatFlags.Left | TextFormatFlags.WordBreak |
            TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;

        private void QueueGridSize()
        {
            if (refreshing || sizingRows || sizePending || !IsHandleCreated) return;
            sizePending = true;
            // ColumnWidthChanged fires during fill-column sizing; row sizing is only safe after it completes.
            BeginInvoke(new Action(delegate
            {
                sizePending = false;
                if (!IsDisposed) SizeGridRows();
            }));
        }

        private void SizeGridRows()
        {
            if (sizingRows || grid.Columns.Count == 0) return;
            sizingRows = true;
            try
            {
                foreach (DataGridViewRow row in grid.Rows)
                {
                    var entry = row.Tag as JobEntry;
                    if (entry == null) continue;
                    int height = 40;
                    foreach (int column in new[] { ColServer, ColType, ColTargets, ColNote })
                    {
                        if (!grid.Columns[column].Visible) continue;
                        string text = column == ColServer ? JobRound.GroupName(entry, language) : Convert.ToString(row.Cells[column].Value);
                        int width = Math.Max(1, grid.Columns[column].Width - (column == ColType ? 29 : 16));
                        Font font = column == ColTargets ? (entry.Done ? monoDoneFont : monoFont) :
                            column == ColType && entry.Done ? doneFont : Font;
                        if (column == ColTargets && entry.Type.Targets == TargetKind.Ip && entry.Targets.Count > 0)
                        {
                            height = Math.Max(height, entry.Targets.Count * IpLineHeight + 14);
                            row.Cells[column].ToolTipText = Translate("点图标复制单个 IP，双击 IP 文字修改。", "Click an icon to copy one IP. Double-click the IP text to edit.") +
                                Environment.NewLine + text;
                            continue;
                        }
                        height = Math.Max(height, TextRenderer.MeasureText(text, font, new Size(width, 0), WrapText).Height + 14);
                        row.Cells[column].ToolTipText = text;
                    }
                    row.Height = Math.Min(65536, height);
                }
            }
            finally { sizingRows = false; }
        }

        private void DrawAccessPill(Graphics g, JobEntry entry, Rectangle inner, bool hover)
        {
            string name = entry.Access == JobAccess.Unknown ? (hover ? Translate("点击设置", "Set") : "—") : JobCatalog.AccessNames[(int)entry.Access];
            Color tone = entry.Access == JobAccess.Acquisition ? Palette.Accent : entry.Access == JobAccess.Grant ? Palette.Success : Palette.Faint;
            if (entry.Done) tone = Palette.Faint;
            Size size = TextRenderer.MeasureText(g, name, smallFont, Size.Empty, TextFormatFlags.NoPadding);
            var pill = new Rectangle(inner.X, inner.Y + inner.Height / 2 - 11, size.Width + 18, 22);
            if (entry.Access == JobAccess.Unknown)
            {
                if (hover) Theme.FillRound(g, pill, 11, Palette.Panel, Palette.Border);
                TextRenderer.DrawText(g, name, smallFont, pill, hover ? Palette.Muted : Palette.Faint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                return;
            }
            Theme.FillRound(g, pill, 11, Palette.Blend(tone, Palette.Panel, 30), Palette.Blend(tone, Palette.Panel, 120));
            TextRenderer.DrawText(g, name, smallFont, pill, tone, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private void ConfigureCatalog()
        {
            StyleGrid(catalog);
            catalog.ReadOnly = true;
            var kind = new DataGridViewTextBoxColumn { Width = 190 };
            var requirements = new DataGridViewTextBoxColumn { AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 80 };
            requirements.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            requirements.DefaultCellStyle.ForeColor = Palette.Muted;
            var count = new DataGridViewTextBoxColumn { Width = 120 };
            catalog.Columns.AddRange(kind, requirements, count);
            foreach (DataGridViewColumn column in catalog.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            foreach (JobType type in JobCatalog.Types)
            {
                int index = catalog.Rows.Add("     " + type.Name, "", "");
                DataGridViewCellStyle style = catalog.Rows[index].Cells[0].Style;
                style.ForeColor = style.SelectionForeColor = type.Color;
            }
            catalog.CellPainting += delegate(object sender, DataGridViewCellPaintingEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
                e.Paint(e.CellBounds, DataGridViewPaintParts.All);
                JobType type = JobCatalog.Types[e.RowIndex];
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var dot = new SolidBrush(type.Color))
                    e.Graphics.FillEllipse(dot, e.CellBounds.X + 12, e.CellBounds.Y + e.CellBounds.Height / 2f - 3, 6, 6);
                e.Handled = true;
            };
            catalog.Resize += delegate { SizeCatalogRows(); };
            catalog.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) StartJobOfType(e.RowIndex); };
            catalog.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter || catalog.CurrentRow == null) return;
                StartJobOfType(catalog.CurrentRow.Index);
                e.Handled = true;
            };
        }

        // Rows are sized by hand so long requirements wrap the same way on every runtime.
        private void SizeCatalogRows()
        {
            int width = catalog.Columns[1].Width - 18;
            if (width <= 20) return;
            foreach (DataGridViewRow row in catalog.Rows)
            {
                Size text = TextRenderer.MeasureText(Convert.ToString(row.Cells[1].Value), Font, new Size(width, 0), TextFormatFlags.WordBreak);
                row.Height = Math.Max(38, text.Height + 18);
            }
        }

        private void BuildEntryCard()
        {
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, BackColor = Palette.Panel, Margin = Padding.Empty };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var fields = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, BackColor = Palette.Panel };
            typeButton.Margin = new Padding(0, 0, 18, 0);
            serverInput.Margin = new Padding(0, 0, 18, 0);
            accessBox.Margin = Padding.Empty;
            accessBox.Tints = new[] { Palette.Muted, Palette.Accent, Palette.Success };
            accessBox.Items = JobCatalog.AccessNames;
            fields.Controls.AddRange(new Control[] { typeLabel, typeButton, serverLabel, serverInput });
            body.Controls.Add(fields, 0, 0);
            body.SetColumnSpan(fields, 2);
            var access = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            access.Controls.AddRange(new Control[] { accessLabel, accessBox });
            body.Controls.Add(access, 0, 1);
            body.SetColumnSpan(access, 2);

            targetsLabel.Margin = noteLabel.Margin = new Padding(0, 4, 0, 0);
            body.Controls.Add(targetsLabel, 0, 2);
            body.Controls.Add(noteLabel, 1, 2);
            targetsBox.Font = monoFont;
            targetsBox.AcceptsReturn = true;
            body.Controls.Add(targetsInput, 0, 3);
            body.SetRowSpan(targetsInput, 2);
            body.Controls.Add(noteInput, 1, 3);
            var addRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, BackColor = Palette.Panel };
            addButton.Margin = new Padding(0, 4, 0, 0);
            readTargets.Margin = new Padding(6, 4, 0, 0);
            addRow.Controls.AddRange(new Control[] { addButton, readTargets });
            body.Controls.Add(addRow, 1, 4);

            var notes = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Panel, Margin = new Padding(0, 8, 0, 0) };
            notes.Controls.Add(caution);
            notes.Controls.Add(hint);
            notes.Resize += delegate { hint.MaximumSize = caution.MaximumSize = new Size(notes.ClientSize.Width, 0); };
            body.Controls.Add(notes, 0, 5);
            body.SetColumnSpan(notes, 2);
            entryCard.Controls.Add(body);

            Theme.Style(typeMenu);
            for (int i = 0; i < JobCatalog.Types.Length; i++)
            {
                int index = i;
                typeMenu.Items.Add(JobCatalog.Types[i].Name, null, delegate { SelectType(index); serverBox.Focus(); });
            }
            typeButton.Click += delegate { typeMenu.Show(typeButton, new Point(0, typeButton.Height + 2)); };
            serverBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            serverBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
            serverBox.TextChanged += delegate
            {
                JobAccess known = round.AccessFor(serverBox.Text);
                accessBox.SelectedIndex = (int)known;
            };
        }

        internal void SelectType(int index)
        {
            typeIndex = index;
            typeButton.Text = JobCatalog.Types[index].Name;
            for (int i = 0; i < typeMenu.Items.Count; i++) ((ToolStripMenuItem)typeMenu.Items[i]).Checked = i == index;
            UpdateForm();
        }

        internal void SetLanguage(UiLanguage selected)
        {
            language = selected;
            ApplyLanguage();
            RefreshGrid();
        }

        private void ApplyLanguage()
        {
            Text = Translate("工作手册 · 本轮任务", "Job Guide · This round");
            compactButton.Text = compact ? Translate("展开", "Expand") : Translate("精简", "Compact");
            onTop.Text = Translate("置顶", "Keep on top");
            grid.Columns[ColDone].HeaderText = "";
            grid.Columns[ColServer].HeaderText = Translate("服务器", "Server");
            grid.Columns[ColType].HeaderText = Translate("工作类型", "Job type");
            grid.Columns[ColAccess].HeaderText = Translate("权限", "Access");
            grid.Columns[ColTargets].HeaderText = Translate("目标", "Targets");
            grid.Columns[ColNote].HeaderText = Translate("备注", "Note");
            string accessTip = Translate("Acquisition：需要先入侵该服务器才能登录。Grant：任务期间自动获得权限。点表格里的权限可以切换。",
                "Acquisition: you must have hacked the server to log in. Grant: access is given while the job is active. Click an access value in the list to change it.");
            tips.SetToolTip(accessBox, accessTip);
            tips.SetToolTip(compactButton, Translate("只保留任务清单，适合放在游戏旁边。Esc 恢复。", "Show only the checklist so it fits beside the game. Esc restores."));
            tips.SetToolTip(onTop, Translate("让这个窗口保持在浏览器上方。", "Keep this window above the browser."));
            catalog.Columns[0].HeaderText = Translate("工作类型", "Job type");
            catalog.Columns[1].HeaderText = Translate("要求", "Requirements");
            catalog.Columns[2].HeaderText = Translate("本轮", "This round");
            for (int i = 0; i < JobCatalog.Types.Length; i++)
                catalog.Rows[i].Cells[1].Value = Translate(JobCatalog.Types[i].Chinese, JobCatalog.Types[i].English);
            SizeCatalogRows();
            generalNote.AccessibleName = Translate("工作类型说明", "Job type instructions");
            generalNote.Text = Translate(
                "·  除 File Decryption 外，每项工作都要连接市场列出的服务器（网络地图上）。双击一行开始记录该类工作。" + Environment.NewLine +
                "·  Acquisition 需要先入侵服务器才能登录；Grant 在任务期间自动授权。" + Environment.NewLine +
                "·  删除未列出的文件、日志或 IP 会让服务器进入维护，地图上的红色计时结束前无法访问。" + Environment.NewLine +
                "·  在市场点 Job 按钮即可刷新列表、检查是否完成，不必离开再回来。",
                "·  Every job except File Decryption uses the server listed in the market (on the Network Map). Double-click a row to record one." + Environment.NewLine +
                "·  Acquisition: you must have hacked the server to log in. Grant: access is given while the job is active." + Environment.NewLine +
                "·  Removing a file, log, or IP that is not listed puts the server into maintenance until the red timer on the map ends." + Environment.NewLine +
                "·  Click the Job button in the market to refresh the list and check completion.");
            emptyNote.Text = Translate("本轮还没有记录" + Environment.NewLine + "在市场接单后，用下方表单逐个添加",
                "No jobs yet" + Environment.NewLine + "Add each job below as you accept it in the market");
            typeLabel.Text = Translate("类型", "Type");
            serverLabel.Text = Translate("服务器", "Server");
            accessLabel.Text = Translate("权限", "Access");
            noteLabel.Text = Translate("备注（可选）", "Note (optional)");
            addButton.Text = Translate("添加任务", "Add job");
            readTargets.Text = Translate("截图识别", "Read targets");
            importJob.Text = Translate("截图导入任务", "Import from screenshot");
            tips.SetToolTip(importJob, Translate("框住一张展开的任务卡，将识别结果添加到本轮列表。",
                "Select one expanded job card to add the recognized job to this round."));
            tips.SetToolTip(targetsBox, Translate("按回车换行，也可粘贴多行。", "Press Enter for a new line or paste multiple lines."));
            tips.SetToolTip(readTargets, Translate("框选游戏里的目标文字，提取到目标栏供核对。", "Select the in-game text to fill the editable target box."));
            copy.Text = Translate("复制汇总", "Copy summary");
            delete.Text = Translate("删除", "Delete");
            clear.Text = Translate("清空本轮", "Clear round");
            status.Text = Translate("记录只保存在内存中，退出定位器后清空。", "Jobs stay in memory until you exit the Finder.");
            UpdateForm();
            UpdateHeader();
        }

        private void UpdateForm()
        {
            JobType type = JobCatalog.Types[typeIndex];
            serverInput.Enabled = accessBox.Enabled = type.NeedsServer;
            switch (type.Targets)
            {
                case TargetKind.Ip:
                    targetsLabel.Text = Translate("IP 地址（每行一个）", "IP addresses (one per line)");
                    break;
                case TargetKind.Log:
                    targetsLabel.Text = Translate("日志名（每行一个）", "Log names (one per line)");
                    break;
                case TargetKind.File:
                    targetsLabel.Text = Translate("文件名（每行一个）", "File names (one per line)");
                    break;
                default:
                    targetsLabel.Text = Translate("文件名（可选）", "File name (optional)");
                    break;
            }
            hint.Text = Translate(type.Chinese, type.English);
            caution.Text = type.Removes ? Translate("只删除列出的项目，删错会让服务器进入维护。", "Remove only what is listed. A wrong removal puts the server into maintenance.") : "";
        }

        private async Task ReadImage(string chinese, string english, Action<Bitmap> read)
        {
            if (pickImage == null) return;
            try
            {
                using (Bitmap image = await pickImage(chinese, english))
                {
                    if (image == null || IsDisposed) return;
                    Cursor = Cursors.WaitCursor;
                    read(image);
                }
            }
            catch (OcrUnavailableException ex)
            {
                SetStatus(Translate("文字识别失败：", "Text recognition failed: ") + ex.Message);
            }
            finally { if (!IsDisposed) Cursor = Cursors.Default; }
        }

        private Task ReadTargets()
        {
            TargetKind kind = JobCatalog.Types[typeIndex].Targets;
            return ReadImage("框住任务详情里的 IP 或名称", "Select the IP addresses or names in the job details.",
                image => AddDraftTargets(JobRound.ExtractTargets(recognize(image), kind), kind));
        }

        private void AddDraftTargets(List<string> found, TargetKind kind)
        {
            if (found.Count == 0)
            {
                SetStatus(kind == TargetKind.Ip
                    ? Translate("未找到 IPv4 地址，请只框选包含地址的文字。", "No IPv4 addresses found. Select just the text containing the addresses.")
                    : kind == TargetKind.Log
                        ? Translate("未找到日志标题，请只框住标题文字。", "No log title found. Select just the title text.")
                        : Translate("未找到带扩展名的名称，请缩小框选范围或手动输入。", "No names with extensions found. Try a tighter selection or enter them manually."));
                return;
            }
            var existing = new HashSet<string>(JobRound.ParseTargets(targetsBox.Text, kind));
            var added = found.Where(existing.Add).ToList();
            if (added.Count > 0)
            {
                string previous = targetsBox.Text.TrimEnd();
                targetsBox.Text = (previous.Length == 0 ? "" : previous + Environment.NewLine) + string.Join(Environment.NewLine, added);
            }
            targetsBox.Focus();
            SetStatus(string.Format(Translate("识别到 {0} 项，新增 {1} 项。请对照游戏核对后再添加任务。",
                "Found {0}, added {1} to Targets. Compare with the game before adding the job."), found.Count, added.Count));
        }

        private Task ImportJob()
        {
            return ReadImage("框住一张或多张上下排列的展开任务卡，包含标题和左右两栏",
                "Select one or more stacked, expanded job cards, including titles and both columns.",
                image =>
                {
                    List<JobEntry> entries = RecognizeCards(image, recognize, recognizeLines);
                    int added = 0, duplicates = 0, incomplete = 0;
                    foreach (JobEntry entry in entries)
                    {
                        if (entry == null) incomplete++;
                        else if (TryRecord(entry)) added++;
                        else duplicates++;
                    }
                    if (added == 0 && duplicates == 0)
                    {
                        SetStatus(Translate("任务信息识别不完整。请重新框选展开的任务卡，或手动添加。",
                            "Could not read all job fields. Select the full expanded cards or add them manually."));
                        return;
                    }
                    SetStatus(string.Format(Translate("新增 {0} 项，重复 {1} 项，信息不全 {2} 项。请核对列表。",
                        "Added {0}, skipped {1} duplicates and {2} incomplete cards. Check the list."),
                        added, duplicates, incomplete));
                });
        }

        internal static List<JobEntry> RecognizeCards(Bitmap image, Func<Bitmap, List<string>> recognize,
            Func<Bitmap, List<OcrTextLine>> recognizeLines)
        {
            if (image.Width < 80 || image.Height < 60 || recognizeLines == null)
                return new List<JobEntry> { RecognizeCard(image, recognize) };

            using (Bitmap titleStrip = image.Clone(new Rectangle(0, 0, image.Width * 3 / 4, image.Height), PixelFormat.Format32bppArgb))
            {
                // Card titles start near the left edge. The condition value in the
                // info column can repeat the title, particularly for File Decryption.
                var headings = recognizeLines(titleStrip)
                    .Where(line => !line.Bounds.IsEmpty && line.Bounds.Left < image.Width / 4f)
                    .Select(line => new { Type = JobRound.ReadCardType(line.Text), Bounds = line.Bounds })
                    .Where(line => line.Type != null)
                    .OrderBy(line => line.Bounds.Top)
                    .ToList();
                if (headings.Count < 2) return new List<JobEntry> { RecognizeCard(image, recognize) };

                // Each card has the same top inset from its title. Use the first card's inset,
                // capped at twice the title height in case the selection includes extra space.
                float inset = Math.Min(headings[0].Bounds.Top, 2 * headings[0].Bounds.Height);
                int[] starts = headings.Select(heading => Math.Max(0, (int)Math.Floor(heading.Bounds.Top - inset))).ToArray();
                var entries = new List<JobEntry>();
                for (int i = 0; i < headings.Count; i++)
                {
                    int bottom = i + 1 == headings.Count ? image.Height : starts[i + 1];
                    if (bottom - starts[i] < 60)
                    {
                        entries.Add(null);
                        continue;
                    }
                    using (Bitmap card = image.Clone(new Rectangle(0, starts[i], image.Width, bottom - starts[i]), PixelFormat.Format32bppArgb))
                        entries.Add(RecognizeCardBody(card, headings[i].Type, recognize));
                }
                return entries;
            }
        }

        internal static JobEntry RecognizeCard(Bitmap image, Func<Bitmap, List<string>> recognize)
        {
            if (image.Width < 80 || image.Height < 60) return null;
            int splitY = image.Height / 3;
            JobType type;
            using (Bitmap heading = image.Clone(new Rectangle(0, 0, image.Width * 3 / 4, splitY), PixelFormat.Format32bppArgb))
                type = JobRound.IdentifyCardType(recognize(heading));
            if (type == null) return null;
            return RecognizeCardBody(image, type, recognize);
        }

        private static JobEntry RecognizeCardBody(Bitmap image, JobType type, Func<Bitmap, List<string>> recognize)
        {
            int splitY = image.Height / 3;
            int splitX = type.NeedsServer ? FindCardDivider(image, splitY) : image.Width / 2;
            int valuesX = image.Width / 4;

            // Log titles can look like ordinary sentences. Exclude the instructions and Info button
            // from the right column before reading the target bands of a fully framed card.
            int descriptionTop = type.Targets == TargetKind.Log ? image.Height * 54 / 100 : splitY;
            int descriptionBottom = type.Targets == TargetKind.Log ? image.Height * 84 / 100 : image.Height;
            using (Bitmap info = image.Clone(new Rectangle(valuesX, splitY, splitX - valuesX, image.Height - splitY), PixelFormat.Format32bppArgb))
            using (Bitmap description = image.Clone(new Rectangle(splitX, descriptionTop, image.Width - splitX,
                descriptionBottom - descriptionTop), PixelFormat.Format32bppArgb))
                return JobRound.ReadCard(type, recognize(info), recognize(description));
        }

        private static int FindCardDivider(Bitmap image, int bodyTop)
        {
            // The game draws a thin vertical rule between the columns. A crop can cut
            // off part of the right margin, so the rule may be away from the midpoint.
            int bottom = image.Height * 9 / 10;
            int step = Math.Max(2, image.Height / 180);
            int samples = (bottom - bodyTop + step - 1) / step;
            int bestX = image.Width / 2;
            int bestCount = samples / 2;
            for (int x = image.Width * 35 / 100; x < image.Width * 65 / 100; x++)
            {
                int count = 0;
                for (int y = bodyTop; y < bottom; y += step)
                {
                    int neighbors = Math.Max(CardBrightness(image.GetPixel(x - 3, y)),
                        CardBrightness(image.GetPixel(x + 3, y)));
                    if (CardBrightness(image.GetPixel(x, y)) - neighbors >= 12) count++;
                }
                if (count > bestCount)
                {
                    bestX = x;
                    bestCount = count;
                }
            }
            return bestX;
        }

        private static int CardBrightness(Color pixel)
        {
            return pixel.R + pixel.G + pixel.B;
        }

        private void UpdateHeader()
        {
            viewSwitch.Items = new[]
            {
                Translate("本轮任务", "This round") + (round.Entries.Count > 0 ? "  " + round.Entries.Count : ""),
                Translate("工作类型", "Job types")
            };
            if (round.Started == null)
            {
                roundLabel.Text = Translate("本轮尚无记录", "No jobs recorded");
                tips.SetToolTip(roundLabel, "");
            }
            else
            {
                TimeSpan age = DateTime.Now - round.Started.Value;
                string ago = age.TotalMinutes < 1 ? Translate("刚刚", "just now")
                    : age.TotalHours < 1 ? string.Format(Translate("{0} 分钟前", "{0} min ago"), (int)age.TotalMinutes)
                    : string.Format(Translate("{0} 小时 {1} 分钟前", "{0} h {1} min ago"), (int)age.TotalHours, age.Minutes);
                int servers = round.Entries.Where(e => e.Type.NeedsServer).Select(e => e.ServerKey).Distinct().Count();
                roundLabel.Text = string.Format(Translate("已完成 {0}/{1} · {2} 台服务器", "{0}/{1} done · {2} servers"),
                    round.DoneCount, round.Entries.Count, servers);
                tips.SetToolTip(roundLabel, string.Format(Translate("始于 {0}（{1}）", "Started {0} ({1})"),
                    round.Started.Value.ToString("HH:mm"), ago));
            }
            bool finished = round.Entries.Count > 0 && round.DoneCount == round.Entries.Count;
            roundLabel.ForeColor = finished ? Palette.Success : Palette.Muted;
            progress.Fraction = round.Entries.Count == 0 ? 0 : (float)round.DoneCount / round.Entries.Count;
        }

        private void RoundChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated)
            {
                RefreshGrid();
                return;
            }
            if (refreshPending) return;
            refreshPending = true;
            BeginInvoke(new Action(RefreshGrid));
        }

        internal void RefreshGrid()
        {
            refreshPending = false;
            JobEntry keep = selectAfterRefresh ?? SelectedEntry();
            int first = grid.FirstDisplayedScrollingRowIndex;
            JobEntry topEntry = first >= 0 ? grid.Rows[first].Tag as JobEntry : null;
            bool selectingNew = selectAfterRefresh != null && !grid.Rows.Cast<DataGridViewRow>().Any(row => row.Tag == keep);
            DataGridViewRow topRow = null, selectedRow = null;
            selectAfterRefresh = null;
            refreshing = true;
            hoverRow = -1;
            try
            {
                grid.Rows.Clear();
                foreach (JobEntry entry in round.RouteOrder())
                {
                    int index = grid.Rows.Add(entry.Done, entry.Type.NeedsServer ? entry.Server : "Downloads", entry.Type.Name,
                        JobCatalog.AccessNames[(int)entry.Access], string.Join(Environment.NewLine, entry.Targets), entry.Note);
                    DataGridViewRow row = grid.Rows[index];
                    row.Tag = entry;
                    if (entry.Done)
                    {
                        row.DefaultCellStyle.ForeColor = Palette.Faint;
                        row.DefaultCellStyle.Font = doneFont;
                    }
                    if (!entry.Type.NeedsServer) row.Cells[ColServer].ReadOnly = true;
                    if (entry == topEntry) topRow = row;
                    if (entry == keep) selectedRow = row;
                }
                if (selectedRow == null) grid.ClearSelection();
            }
            finally { refreshing = false; }
            emptyNote.Visible = round.Entries.Count == 0 && !showingGuide;
            grid.Visible = !showingGuide && round.Entries.Count > 0;
            SizeGridRows();
            if (selectedRow != null) grid.CurrentCell = selectedRow.Cells[ColTargets];
            if (!selectingNew && topRow != null && grid.Visible) grid.FirstDisplayedScrollingRowIndex = topRow.Index;
            for (int i = 0; i < JobCatalog.Types.Length; i++)
            {
                JobType type = JobCatalog.Types[i];
                int total = round.Entries.Count(e => e.Type == type);
                int done = round.Entries.Count(e => e.Type == type && e.Done);
                catalog.Rows[i].Cells[2].Value = total == 0 ? "" : string.Format(Translate("{0} 项 · 完成 {1}", "{0} · {1} done"), total, done);
            }
            serverBox.AutoCompleteCustomSource.Clear();
            serverBox.AutoCompleteCustomSource.AddRange(round.Servers().ToArray());
            UpdateHeader();
            UpdateActions();
        }

        private JobEntry SelectedEntry()
        {
            DataGridViewRow row = grid.CurrentRow;
            return row != null && row.Selected ? row.Tag as JobEntry : null;
        }

        private void UpdateActions()
        {
            copy.Enabled = clear.Enabled = round.Entries.Count > 0;
            delete.Enabled = !showingGuide && SelectedEntry() != null;
        }

        private void GridValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (refreshing || e.RowIndex < 0) return;
            var entry = grid.Rows[e.RowIndex].Tag as JobEntry;
            if (entry == null) return;
            object value = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
            string text = Convert.ToString(value) ?? "";
            switch (e.ColumnIndex)
            {
                case ColDone:
                    entry.Done = value is bool && (bool)value;
                    break;
                case ColServer:
                    if (entry.Type.NeedsServer) entry.Server = text.Trim();
                    break;
                case ColTargets:
                    entry.Targets = JobRound.ParseTargets(text, entry.Type.Targets);
                    break;
                case ColNote:
                    entry.Note = text.Trim();
                    break;
                default:
                    return;
            }
            selectAfterRefresh = entry;
            round.OnChanged();
        }

        private void ToggleDone(JobEntry entry)
        {
            entry.Done = !entry.Done;
            selectAfterRefresh = entry;
            round.OnChanged();
        }

        private void ChangeType(JobEntry entry, JobType type)
        {
            if (type.Targets != entry.Type.Targets)
                entry.Targets = JobRound.ParseTargets(string.Join(Environment.NewLine, entry.Targets), type.Targets);
            if (!type.NeedsServer)
            {
                entry.Server = "";
                entry.Access = JobAccess.Unknown;
            }
            entry.Type = type;
            selectAfterRefresh = entry;
            round.OnChanged();
        }

        private void ChangeAccess(JobEntry entry, JobAccess access)
        {
            entry.Access = access;
            selectAfterRefresh = entry;
            round.OnChanged();
        }

        internal bool AddJob()
        {
            JobType type = JobCatalog.Types[typeIndex];
            string server = serverBox.Text.Trim();
            if (type.NeedsServer && server.Length == 0)
            {
                status.Text = Translate("请先填写服务器。", "Enter the server first.");
                status.ForeColor = Palette.Accent;
                serverBox.Focus();
                return false;
            }
            var entry = new JobEntry
            {
                Type = type,
                Server = type.NeedsServer ? server : "",
                Targets = JobRound.ParseTargets(targetsBox.Text, type.Targets),
                Note = noteBox.Text.Trim(),
                Access = type.NeedsServer ? (JobAccess)Math.Max(0, accessBox.SelectedIndex) : JobAccess.Unknown
            };
            if (!TryRecord(entry)) return false;
            SetStatus(string.Format(Translate("已添加 {0}{1}。", "Added {0}{1}."), type.Name, type.NeedsServer ? " · " + server : ""));
            targetsBox.Clear();
            noteBox.Clear();
            targetsBox.Focus();
            return true;
        }

        private bool TryRecord(JobEntry entry)
        {
            if (round.HasDuplicate(entry))
            {
                SetStatus(Translate("本轮已有内容相同的任务。", "An identical job is already in this round."));
                return false;
            }
            selectAfterRefresh = entry;
            round.Add(entry);
            if (showingGuide) viewSwitch.SelectedIndex = 0;
            return true;
        }

        private void SetStatus(string text)
        {
            status.Text = text;
            status.ForeColor = Palette.Muted;
        }

        private void StartJobOfType(int index)
        {
            SelectType(index);
            viewSwitch.SelectedIndex = 0;
            if (JobCatalog.Types[index].NeedsServer) serverBox.Focus();
            else targetsBox.Focus();
        }

        private void DeleteSelected()
        {
            JobEntry entry = SelectedEntry();
            if (entry == null) return;
            round.Remove(entry);
            SetStatus(string.Format(Translate("已删除 {0}。", "Deleted {0}."), entry.Type.Name));
        }

        internal void ClearRound()
        {
            round.Clear();
            serverBox.Text = "";
            accessBox.SelectedIndex = 0;
            SetStatus(Translate("已清空本轮。", "Round cleared."));
        }

        internal string RoundSummary()
        {
            return round.Summary(language);
        }

        private bool BuildRowMenu()
        {
            JobEntry entry = SelectedEntry();
            rowMenu.Items.Clear();
            if (entry == null) return false;
            rowMenu.Items.Add(entry.Done ? Translate("标记为未完成", "Mark as not done") : Translate("标记为完成", "Mark as done"), null,
                delegate { ToggleDone(entry); });
            var typeItem = new ToolStripMenuItem(Translate("更改类型", "Change type"));
            foreach (JobType type in JobCatalog.Types)
            {
                JobType choice = type;
                var item = new ToolStripMenuItem(type.Name, null, delegate { ChangeType(entry, choice); }) { Checked = type == entry.Type };
                typeItem.DropDownItems.Add(item);
            }
            rowMenu.Items.Add(typeItem);
            if (entry.Type.NeedsServer)
            {
                var accessItem = new ToolStripMenuItem(Translate("权限", "Access"));
                for (int i = 0; i < JobCatalog.AccessNames.Length; i++)
                {
                    var access = (JobAccess)i;
                    accessItem.DropDownItems.Add(new ToolStripMenuItem(JobCatalog.AccessNames[i], null,
                        delegate { ChangeAccess(entry, access); }) { Checked = entry.Access == access });
                }
                rowMenu.Items.Add(accessItem);
            }
            rowMenu.Items.Add(new ToolStripSeparator());
            if (entry.Type.NeedsServer && entry.Server.Length > 0)
            {
                string server = entry.Server;
                rowMenu.Items.Add(Translate("复制服务器：", "Copy server: ") + server, null,
                    delegate { CopyText(server, Translate("已复制服务器名。", "Server copied.")); });
            }
            foreach (string target in entry.Targets.Take(12))
            {
                string value = target;
                rowMenu.Items.Add(Translate("复制：", "Copy: ") + value, null,
                    delegate { CopyText(value, Translate("已复制 ", "Copied ") + value); });
            }
            if (entry.Targets.Count > 1)
                rowMenu.Items.Add(Translate("复制全部目标", "Copy all targets"), null,
                    delegate { CopyText(string.Join(Environment.NewLine, entry.Targets), Translate("已复制全部目标。", "All targets copied.")); });
            rowMenu.Items.Add(new ToolStripSeparator());
            rowMenu.Items.Add(Translate("删除这项", "Delete job"), null, delegate { DeleteSelected(); });
            foreach (ToolStripItem item in rowMenu.Items)
            {
                var menuItem = item as ToolStripMenuItem;
                if (menuItem != null && menuItem.HasDropDownItems) Theme.Style((ToolStripDropDownMenu)menuItem.DropDown);
            }
            return true;
        }

        private void CopyText(string text, string done)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                writeClipboard(text);
                SetStatus(done);
            }
            catch (ExternalException)
            {
                SetStatus(Translate("剪贴板暂不可用，请再试一次。", "Clipboard unavailable. Try again."));
            }
        }

        private void ShowGuide(bool guide)
        {
            if (guide && compact) SetCompact(false);
            showingGuide = guide;
            catalogPanel.Visible = guide;
            if (viewSwitch.SelectedIndex != (guide ? 1 : 0)) viewSwitch.SelectedIndex = guide ? 1 : 0;
            RefreshGrid();
            if (guide) SizeCatalogRows();
        }

        private void SetOnTop(bool on)
        {
            onTop.Checked = on;
            TopMost = on;
        }

        internal void SetCompact(bool on)
        {
            if (on == compact) return;
            if (on)
            {
                if (showingGuide) viewSwitch.SelectedIndex = 0;
                normalClientSize = ClientSize;
                compact = true;
                MinimumSize = new Size(380, 220);
                SetOnTop(true);
            }
            else compact = false;
            layout.RowStyles[3].Height = compact ? 0 : EntryHeight;
            layout.RowStyles[4].Height = compact ? 0 : FooterHeight;
            entryCard.Visible = footer.Parent.Visible = viewSwitch.Visible = !compact;
            grid.Columns[ColAccess].Visible = grid.Columns[ColNote].Visible = !compact;
            grid.Columns[ColServer].Width = compact ? 116 : 140;
            grid.Columns[ColType].Width = compact ? 150 : 170;
            DataGridViewColumn targets = grid.Columns[ColTargets];
            targets.AutoSizeMode = DataGridViewAutoSizeColumnMode.NotSet;
            targets.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            compactButton.Text = compact ? Translate("展开", "Expand") : Translate("精简", "Compact");
            if (compact) ClientSize = CompactSize;
            else
            {
                ClientSize = normalClientSize;
                MinimumSize = NormalMinimum;
            }
            SizeGridRows();
        }

        // The round outlives this window, so unsubscribe even when the window is disposed without being shown.
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                round.Changed -= RoundChanged;
                clock.Dispose();
                rowMenu.Dispose();
                typeMenu.Dispose();
                tips.Dispose();
                foreach (Font font in new[] { doneFont, monoFont, monoDoneFont, smallFont, headerFont }) font.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && compact)
            {
                SetCompact(false);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
