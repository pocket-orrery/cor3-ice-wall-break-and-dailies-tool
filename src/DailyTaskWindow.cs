using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GlyphFinder
{
    // Helpers for the two daily tasks: Signal Decode and System Log Integrity.
    internal sealed class DailyTaskWindow : Form
    {
        private readonly Func<Rectangle, Task<Bitmap>> capture;
        private readonly Func<string, string, Task<ScreenSelection>> pickArea;
        private readonly Func<Bitmap, List<string>> recognize;
        private Rectangle? signalArea, logArea;
        private UiLanguage language;

        private readonly Segmented pages = new Segmented();
        private readonly FlatButton onTop = new FlatButton { Kind = ButtonKind.Ghost, Margin = Padding.Empty };
        private readonly Panel signalPage = new Panel { Dock = DockStyle.Fill };
        private readonly Panel logPage = new Panel { Dock = DockStyle.Fill, Visible = false };
        private readonly Label status = new Label { Dock = DockStyle.Fill, ForeColor = Palette.Muted, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };

        // Signal Decode
        private readonly Card pulseCard = new Card { Dock = DockStyle.Fill, Brackets = true, Padding = new Padding(16, 14, 16, 10) };
        private readonly PulseStrip strip = new PulseStrip { Dock = DockStyle.Fill };
        private readonly FlatButton readButton = new FlatButton { Kind = ButtonKind.Primary, Icon = ButtonGlyph.Select };
        private readonly FlatButton reselect = new FlatButton { Kind = ButtonKind.Ghost };
        private readonly FlatButton shortButton = new FlatButton(), longButton = new FlatButton(), undoButton = new FlatButton { Kind = ButtonKind.Ghost },
            clearSignal = new FlatButton { Kind = ButtonKind.Ghost, Margin = Padding.Empty };
        private readonly InputBox pulseInput = new InputBox(false) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 0) };
        private readonly Label modeLabel = new Label { AutoSize = true, ForeColor = Palette.Muted, Margin = new Padding(0, 0, 8, 0) };
        private readonly Segmented modeBox = new Segmented { Margin = Padding.Empty };
        private readonly Card resultCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(18, 10, 14, 10) };
        private readonly Label codeLabel = new Label { AutoSize = true, BackColor = Palette.Panel, UseMnemonic = false, Margin = Padding.Empty };
        private readonly Label infoLabel = new Label { AutoSize = true, BackColor = Palette.Panel, ForeColor = Palette.Muted, Margin = new Padding(2, 2, 0, 0) };
        private readonly Label otherLabel = new Label { AutoSize = true, BackColor = Palette.Panel, ForeColor = Palette.Accent, Margin = new Padding(2, 2, 0, 0) };
        private readonly FlatButton copyCode = new FlatButton { Icon = ButtonGlyph.Copy, Margin = Padding.Empty };
        private readonly Card referenceCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(16, 10, 16, 8), Margin = Padding.Empty };
        private readonly Label referenceLabel = new Label { Dock = DockStyle.Fill, BackColor = Palette.Panel, ForeColor = Palette.Muted, UseMnemonic = false };
        private SignalResult signal = new SignalResult();
        private bool syncing;

        // Log Integrity
        private readonly Card formatCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(16, 10, 16, 8) };
        private readonly Label formatLabel = new Label { Dock = DockStyle.Top, Height = 22, BackColor = Palette.Panel, ForeColor = Palette.Primary, UseMnemonic = false };
        private readonly Label typesLabel = new Label { Dock = DockStyle.Fill, BackColor = Palette.Panel, ForeColor = Palette.Muted, UseMnemonic = false };
        private readonly Label logsLabel = new Label { AutoSize = true, ForeColor = Palette.Muted, Margin = new Padding(0, 0, 0, 6) };
        private readonly InputBox logInput = new InputBox(true) { Dock = DockStyle.Fill, Margin = Padding.Empty };
        private readonly Card resultsCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(6), Margin = Padding.Empty };
        private readonly DataGridView results = new DataGridView();
        private readonly Label emptyResults = new Label { Dock = DockStyle.Fill, BackColor = Palette.Panel, ForeColor = Palette.Muted, TextAlign = ContentAlignment.MiddleCenter };
        private readonly FlatButton readLogs = new FlatButton { Kind = ButtonKind.Primary, Icon = ButtonGlyph.Select },
            reselectLogs = new FlatButton { Kind = ButtonKind.Ghost, Visible = false }, clearLogs = new FlatButton { Kind = ButtonKind.Ghost },
            copyReport = new FlatButton { Icon = ButtonGlyph.Copy, Margin = Padding.Empty };
        private readonly Label summary = new Label { AutoSize = true, ForeColor = Palette.Muted, Margin = new Padding(10, 0, 0, 0), UseMnemonic = false };
        private List<LogCheck> checks = new List<LogCheck>();
        private int hoverRow = -1;

        private readonly ToolTip tips = Theme.MakeToolTip();
        private readonly Font monoFont, codeFont, tagFont, boldFont;

        // pickArea asks the user to drag one rectangle on screen (prompt in Chinese and English); recognize turns an image into
        // text lines and defaults to the Windows OCR engine.
        internal DailyTaskWindow(UiLanguage language, Func<Rectangle, Task<Bitmap>> capture, Func<string, string, Task<ScreenSelection>> pickArea,
            Func<Bitmap, List<string>> recognize = null)
        {
            this.language = language;
            this.capture = capture;
            this.pickArea = pickArea;
            this.recognize = recognize ?? LogReader.Read;
            BackColor = Palette.Background;
            ForeColor = Palette.Text;
            Font = Theme.Ui(9f);
            monoFont = Theme.Mono(9f);
            codeFont = Theme.Mono(24f);
            tagFont = Theme.Ui(7.5f);
            boldFont = Theme.Ui(8.5f, FontStyle.Bold);
            Icon icon = Theme.AppIcon();
            if (icon != null) Icon = icon;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(940, 690);
            MinimumSize = new Size(790, 610);
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            KeyPreview = true;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14, 12, 14, 6) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            Controls.Add(layout);

            var header = Bar(new Control[] { pages }, onTop);
            header.Margin = new Padding(0, 0, 0, 10);
            layout.Controls.Add(header, 0, 0);
            var content = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            content.Controls.Add(signalPage);
            content.Controls.Add(logPage);
            layout.Controls.Add(content, 0, 1);
            layout.Controls.Add(status, 0, 2);

            BuildSignalPage();
            BuildLogPage();

            pages.SelectedIndexChanged += delegate
            {
                signalPage.Visible = pages.SelectedIndex == 0;
                logPage.Visible = pages.SelectedIndex == 1;
                SetStatus("");
            };
            onTop.Click += delegate { onTop.Checked = !onTop.Checked; TopMost = onTop.Checked; };
            pages.SelectedIndex = 0;
            modeBox.SelectedIndex = 0;
            ApplyLanguage();
            UpdateSignal();
            UpdateLogs();
        }

        private string Translate(string chinese, string english)
        {
            return language.Text(chinese, english);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
        }

        private static TableLayoutPanel Bar(Control[] left, params Control[] right)
        {
            var bar = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = left.Length + 1 + right.Length, Margin = Padding.Empty };
            bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            foreach (Control c in left) bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (Control c in right) bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < left.Length; i++)
            {
                left[i].Anchor = AnchorStyles.Left;
                bar.Controls.Add(left[i], i, 0);
            }
            for (int i = 0; i < right.Length; i++)
            {
                right[i].Anchor = AnchorStyles.Right;
                bar.Controls.Add(right[i], left.Length + 1 + i, 0);
            }
            return bar;
        }

        private static TableLayoutPanel Rows(params int[] heights)
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = heights.Length, Margin = Padding.Empty };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in heights)
                table.RowStyles.Add(height < 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
            return table;
        }

        // ---------------- Signal Decode ----------------

        private void BuildSignalPage()
        {
            TableLayoutPanel page = Rows(-1, 44, 44, 148, 124);
            pulseCard.Controls.Add(strip);
            page.Controls.Add(pulseCard, 0, 0);

            TableLayoutPanel actions = Bar(new Control[] { readButton, reselect }, shortButton, longButton, undoButton, clearSignal);
            actions.Margin = new Padding(0, 0, 0, 6);
            page.Controls.Add(actions, 0, 1);

            pulseInput.Box.Font = monoFont;
            TableLayoutPanel entry = Bar(new Control[0], modeLabel, modeBox);
            entry.Controls.Add(pulseInput, 0, 0);
            pulseInput.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            entry.Margin = new Padding(0, 0, 0, 10);
            page.Controls.Add(entry, 0, 2);
            modeBox.Items = new[] { "Auto", "Morse ×5", "Binary ×4" };

            var result = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, BackColor = Palette.Panel, Margin = Padding.Empty };
            result.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            result.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            result.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            result.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            result.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            codeLabel.Font = codeFont;
            codeLabel.Anchor = AnchorStyles.Left;
            copyCode.Anchor = AnchorStyles.Right;
            result.Controls.Add(codeLabel, 0, 0);
            result.Controls.Add(copyCode, 1, 0);
            result.Controls.Add(infoLabel, 0, 1);
            result.SetColumnSpan(infoLabel, 2);
            result.Controls.Add(otherLabel, 0, 2);
            result.SetColumnSpan(otherLabel, 2);
            result.Resize += delegate
            {
                infoLabel.MaximumSize = otherLabel.MaximumSize = new Size(Math.Max(1, result.ClientSize.Width - 8), 0);
            };
            resultCard.Controls.Add(result);
            page.Controls.Add(resultCard, 0, 3);

            referenceLabel.Font = Theme.Mono(8.5f);
            referenceCard.Controls.Add(referenceLabel);
            page.Controls.Add(referenceCard, 0, 4);
            signalPage.Controls.Add(page);

            readButton.Click += async delegate { await ReadSignal(false); };
            reselect.Click += async delegate { await ReadSignal(true); };
            shortButton.Click += delegate { AppendPulse('S'); };
            longButton.Click += delegate { AppendPulse('L'); };
            undoButton.Click += delegate { Undo(); };
            clearSignal.Click += delegate { SetPulses(""); SetStatus(""); };
            copyCode.Click += delegate { CopyText(signal.Complete ? signal.Code : "", Translate("已复制代码。", "Code copied.")); };
            pulseInput.Box.TextChanged += delegate
            {
                if (syncing) return;
                SetStatus("");
                UpdateSignal();
            };
            modeBox.SelectedIndexChanged += delegate { UpdateSignal(); };
            strip.PulseClicked = delegate(int index)
            {
                char[] pulses = Pulses.ToCharArray();
                pulses[index] = pulses[index] == 'S' ? 'L' : 'S';
                SetPulses(new string(pulses));
                SetStatus(string.Format(Translate("已把第 {0} 个脉冲改为 {1}。", "Pulse {0} changed to {1}."), index + 1, pulses[index] == 'S' ? Translate("短", "short") : Translate("长", "long")));
            };
        }

        internal string Pulses { get { return SignalDecoder.Normalize(pulseInput.Box.Text); } }

        internal void SetPulses(string pulses)
        {
            syncing = true;
            pulseInput.Box.Text = pulses;
            pulseInput.Box.SelectionStart = pulses.Length;
            syncing = false;
            UpdateSignal();
        }

        private void AppendPulse(char pulse)
        {
            SetPulses(Pulses + pulse);
            SetStatus("");
        }

        private void Undo()
        {
            string pulses = Pulses;
            if (pulses.Length > 0) SetPulses(pulses.Substring(0, pulses.Length - 1));
            SetStatus("");
        }

        internal SignalResult Signal { get { return signal; } }

        private void UpdateSignal()
        {
            string pulses = Pulses;
            signal = SignalDecoder.Solve(pulses, (SignalMode)Math.Max(0, modeBox.SelectedIndex));
            strip.SetSignal(pulses, signal.GroupSize, signal.Code);
            otherLabel.Text = "";
            if (pulses.Length == 0)
            {
                codeLabel.Text = "– – –";
                codeLabel.ForeColor = Palette.Faint;
                infoLabel.Text = Translate("点「读取信号」，或用 S / L 键逐个输入脉冲。", "Choose Read signal, or enter pulses with the S and L keys.");
            }
            else if (signal.GroupSize == 0)
            {
                codeLabel.Text = pulses.Length.ToString();
                codeLabel.ForeColor = Palette.Faint;
                int morse = 5 - pulses.Length % 5, binary = 4 - pulses.Length % 4;
                infoLabel.Text = string.Format(Translate("{0} 个脉冲，不是 4 或 5 的倍数。可能漏读或多读了脉冲（再加 {1} 个是 Binary，再加 {2} 个是 Morse）。",
                    "{0} pulses is not a multiple of 4 or 5. A pulse may be missing or extra ({1} more for Binary, {2} more for Morse)."),
                    pulses.Length, binary, morse);
            }
            else
            {
                codeLabel.Text = signal.Code;
                codeLabel.ForeColor = signal.Complete ? Palette.Primary : Palette.Danger;
                string mode = signal.Mode == SignalMode.Morse ? "Morse" : "Binary";
                infoLabel.Text = string.Format(Translate("{0} 个脉冲 · {1} · {2} 位数字", "{0} pulses · {1} · {2} digits"), pulses.Length, mode, signal.Code.Length) +
                    (pulses.Length % signal.GroupSize != 0
                        ? Translate("  ·  最后一组不完整，请重新框选或补齐脉冲", "  ·  Incomplete final group. Reselect or add the missing pulses.")
                        : signal.Complete ? "" : Translate("  ·  红色分组不是有效数字，点脉冲可以改正", "  ·  Red groups are not digits. Click a pulse to fix it."));
                if (signal.OtherCode.Length > 0)
                    otherLabel.Text = string.Format(Translate("这个长度两种编码都可能。另一种（{0}）：{1}。不确定就听一下信号：Morse 的数字之间有明显停顿。",
                        "This length fits both encodings. As {0}: {1}. If unsure, play the signal: Morse has clear pauses between digits."),
                        signal.OtherMode == SignalMode.Morse ? "Morse" : "Binary", signal.OtherCode);
            }
            copyCode.Enabled = signal.Complete;
            undoButton.Enabled = clearSignal.Enabled = pulses.Length > 0;
        }

        private async Task ReadSignal(bool chooseArea)
        {
            Bitmap shot;
            if (chooseArea || signalArea == null)
            {
                ScreenSelection selected = await PickSnapshot("框住 PULSE SIGNAL 的所有脉冲行", "Select all rows of pulses under PULSE SIGNAL.");
                if (selected == null) return;
                signalArea = selected.Bounds;
                shot = selected.Image;
                reselect.Visible = true;
            }
            else shot = await CaptureArea(signalArea.Value);
            if (shot == null) return;
            string pulses;
            using (shot) pulses = SignalDecoder.ReadPulses(shot);
            if (pulses.Length == 0)
            {
                SetStatus(Translate("没有找到脉冲。点「重选区域」框住 PULSE SIGNAL 的所有脉冲行。", "No pulses found. Choose Reselect and include all PULSE SIGNAL rows."), true);
                return;
            }
            SetPulses(pulses);
            SetStatus(string.Format(Translate("读取到 {0} 个脉冲。如果游戏换了信号，再点一次「读取信号」。", "Read {0} pulses. If the game shows a new signal, choose Read signal again."), pulses.Length));
        }

        private async Task<ScreenSelection> PickSnapshot(string chinese, string english)
        {
            ScreenSelection selection = await pickArea(chinese, english);
            if (!IsDisposed) return selection;
            if (selection != null) selection.Image.Dispose();
            return null;
        }

        // The Finder hides every overlapping tool window before capturing this area.
        private async Task<Bitmap> CaptureArea(Rectangle area)
        {
            try
            {
                Bitmap image = await capture(area);
                if (!IsDisposed) return image;
                if (image != null) image.Dispose();
                return null;
            }
            catch (Exception ex)
            {
                if (!(ex is ExternalException || ex is ArgumentException || ex is OutOfMemoryException)) throw;
                if (!IsDisposed)
                    SetStatus(Translate("无法读取屏幕。请检查游戏是否处于窗口化或无边框窗口。", "Could not capture the screen. Check that the game is windowed or borderless."), true);
                return null;
            }
        }

        private async Task ReadLogs(bool chooseArea)
        {
            Bitmap shot;
            if (chooseArea || logArea == null)
            {
                ScreenSelection selected = await PickSnapshot("框住 SCAN LOGS OUTPUT 下方的日志行，包含左侧时间戳",
                    "Select the rows below SCAN LOGS OUTPUT, including the timestamps on the left.");
                if (selected == null) return;
                logArea = selected.Bounds;
                shot = selected.Image;
                reselectLogs.Visible = true;
            }
            else shot = await CaptureArea(logArea.Value);
            if (shot == null) return;
            List<string> lines;
            Cursor = Cursors.WaitCursor;
            try
            {
                using (shot) lines = recognize(shot);
            }
            catch (OcrUnavailableException ex)
            {
                SetStatus(Translate("无法识别文字：", "Could not recognize text: ") + ex.Message, true);
                return;
            }
            finally { Cursor = Cursors.Default; }

            var existing = new HashSet<string>(LogLines(logInput.Box.Text));
            var added = new List<string>();
            int found = 0, skipped = 0;
            foreach (string raw in lines)
            {
                string line = LogValidator.FixOcr(raw);
                if (line.Length == 0) continue;
                if (!LogReader.CanImport(line))
                {
                    skipped++;
                    continue;
                }
                found++;
                if (existing.Add(line)) added.Add(line);
            }
            if (found == 0)
            {
                SetStatus(Translate("没有读到完整日志。请把左侧时间戳和日志正文一起框进去再读。",
                    "No complete logs found. Select both the timestamps and the log text, then read again."), true);
                return;
            }
            if (added.Count > 0)
            {
                string text = logInput.Box.Text.TrimEnd();
                logInput.Box.Text = (text.Length > 0 ? text + Environment.NewLine : "") + string.Join(Environment.NewLine, added);
            }
            string message = string.Format(Translate("识别到 {0} 行，新增 {1} 行。", "Read {0} lines, {1} new."), found, added.Count);
            if (skipped > 0)
                message += string.Format(Translate(" 跳过 {0} 个未读完整的片段。", " Skipped {0} incomplete fragments."), skipped);
            SetStatus(message + Translate(" 滚动后可以继续读取。", " Scroll to read more."));
        }

        private static IEnumerable<string> LogLines(string text)
        {
            return text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0);
        }

        // ---------------- Log Integrity ----------------

        private void BuildLogPage()
        {
            TableLayoutPanel page = Rows(108, -1, 34, 42);
            formatLabel.Font = monoFont;
            formatCard.Controls.Add(typesLabel);
            formatCard.Controls.Add(formatLabel);
            formatCard.Margin = new Padding(0, 0, 0, 10);
            page.Controls.Add(formatCard, 0, 0);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            TableLayoutPanel left = Rows(24, -1);
            left.Margin = new Padding(0, 0, 12, 0);
            logsLabel.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            left.Controls.Add(logsLabel, 0, 0);
            logInput.Box.Font = monoFont;
            logInput.Box.WordWrap = true;
            logInput.Box.ScrollBars = ScrollBars.Vertical;
            left.Controls.Add(logInput, 0, 1);
            body.Controls.Add(left, 0, 0);
            ConfigureResults();
            resultsCard.Controls.Add(results);
            resultsCard.Controls.Add(emptyResults);
            body.Controls.Add(resultsCard, 1, 0);
            page.Controls.Add(body, 0, 1);

            summary.AutoSize = false;
            summary.Dock = DockStyle.Fill;
            summary.TextAlign = ContentAlignment.MiddleLeft;
            summary.Margin = new Padding(2, 0, 0, 0);
            page.Controls.Add(summary, 0, 2);
            TableLayoutPanel actions = Bar(new Control[] { readLogs, reselectLogs, clearLogs }, copyReport);
            page.Controls.Add(actions, 0, 3);
            logPage.Controls.Add(page);

            logInput.Box.TextChanged += delegate { UpdateLogs(); };
            readLogs.Click += async delegate { await ReadLogs(false); };
            reselectLogs.Click += async delegate { await ReadLogs(true); };
            clearLogs.Click += delegate { logInput.Box.Clear(); logInput.Box.Focus(); };
            copyReport.Click += delegate { CopyText(Report(), Translate("已复制检查结果。", "Results copied.")); };
        }

        private void ConfigureResults()
        {
            results.Dock = DockStyle.Fill;
            results.ReadOnly = true;
            results.AllowUserToAddRows = results.AllowUserToDeleteRows = results.AllowUserToResizeRows = results.AllowUserToResizeColumns = false;
            results.MultiSelect = false;
            results.RowHeadersVisible = false;
            results.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            results.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            results.BackgroundColor = Palette.Panel;
            results.BorderStyle = BorderStyle.None;
            results.CellBorderStyle = DataGridViewCellBorderStyle.None;
            results.EnableHeadersVisualStyles = false;
            results.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            results.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            results.ColumnHeadersHeight = 30;
            results.DefaultCellStyle.BackColor = Palette.Panel;
            results.DefaultCellStyle.SelectionBackColor = Palette.Hover;
            results.Columns.Add(new DataGridViewTextBoxColumn { Width = 40 });
            results.Columns.Add(new DataGridViewTextBoxColumn { Width = 36 });
            results.Columns.Add(new DataGridViewTextBoxColumn { AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            foreach (DataGridViewColumn column in results.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            results.CellPainting += PaintResult;
            results.CellMouseEnter += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex == hoverRow) return;
                if (hoverRow >= 0 && hoverRow < results.Rows.Count) results.InvalidateRow(hoverRow);
                hoverRow = e.RowIndex;
                if (hoverRow >= 0) results.InvalidateRow(hoverRow);
            };
            results.MouseLeave += delegate { if (hoverRow >= 0 && hoverRow < results.Rows.Count) results.InvalidateRow(hoverRow); hoverRow = -1; };
            Theme.DarkScrollBars(results);
        }

        private void PaintResult(object sender, DataGridViewCellPaintingEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle b = e.CellBounds;
            if (e.RowIndex < 0)
            {
                using (var back = new SolidBrush(Palette.Panel)) g.FillRectangle(back, b);
                using (var line = new Pen(Palette.Border)) g.DrawLine(line, b.Left, b.Bottom - 1, b.Right, b.Bottom - 1);
                if (e.ColumnIndex >= 0)
                    TextRenderer.DrawText(g, results.Columns[e.ColumnIndex].HeaderText, Font, new Rectangle(b.X + 10, b.Y, b.Width - 12, b.Height - 2),
                        Palette.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
                e.Handled = true;
                return;
            }
            if (e.ColumnIndex < 0 || e.RowIndex >= checks.Count) return;
            LogCheck check = checks[e.RowIndex];
            bool selected = results.Rows[e.RowIndex].Selected;
            using (var back = new SolidBrush(selected ? Palette.Hover : e.RowIndex == hoverRow ? Palette.RowHover : Palette.Panel)) g.FillRectangle(back, b);
            if (e.RowIndex > 0) using (var line = new Pen(Palette.Blend(Palette.Border, Palette.Panel, 140))) g.DrawLine(line, b.Left, b.Top, b.Right, b.Top);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            const TextFormatFlags single = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            switch (e.ColumnIndex)
            {
                case 0:
                    var circle = new RectangleF(b.X + 13, b.Y + 10, 16, 16);
                    Color tone = check.Ok ? Palette.Success : Palette.Accent;
                    using (var fill = new SolidBrush(check.Ok ? Palette.Blend(tone, Palette.Panel, 50) : tone)) g.FillEllipse(fill, circle);
                    if (check.Ok)
                        using (var pen = new Pen(tone, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                            g.DrawLines(pen, new[] { new PointF(circle.X + 4.5f, circle.Y + 8.5f), new PointF(circle.X + 7, circle.Y + 11), new PointF(circle.X + 11.5f, circle.Y + 5.5f) });
                    else
                        TextRenderer.DrawText(g, "!", boldFont, Rectangle.Round(circle), Palette.OnPrimary,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    break;
                case 1:
                    TextRenderer.DrawText(g, check.Number.ToString(), Font, new Rectangle(b.X + 6, b.Y + 8, b.Width - 8, 20),
                        check.Ok ? Palette.Faint : Palette.Text, single);
                    break;
                case 2:
                    // The line on top, then one row per problem: the game's error type as a tag and the exact reason.
                    TextRenderer.DrawText(g, check.Line, monoFont, new Rectangle(b.X + 8, b.Y + 9, b.Width - 12, 20),
                        check.Ok ? Palette.Muted : Palette.Text, single);
                    int y = b.Y + 34;
                    foreach (LogIssue issue in check.Issues)
                    {
                        string tag = LogValidator.FieldName(issue.Field);
                        Size size = TextRenderer.MeasureText(g, tag, tagFont, Size.Empty, TextFormatFlags.NoPadding);
                        var pill = new Rectangle(b.X + 8, y + 1, Math.Max(size.Width + 16, 170), 19);
                        Color color = issue.Field == LogField.Format ? Palette.Muted : Palette.Accent;
                        Theme.FillRound(g, pill, 9, Palette.Blend(color, Palette.Panel, 34), Palette.Blend(color, Palette.Panel, 120));
                        TextRenderer.DrawText(g, tag, tagFont, pill, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        TextRenderer.DrawText(g, Translate(issue.Chinese, issue.English), Font,
                            new Rectangle(pill.Right + 10, y, b.Right - pill.Right - 16, 21), Palette.Text, single | TextFormatFlags.VerticalCenter);
                        y += 24;
                    }
                    break;
            }
            e.Handled = true;
        }

        internal List<LogCheck> Checks { get { return checks; } }

        private void UpdateLogs()
        {
            checks = LogValidator.Check(logInput.Box.Text);
            results.Rows.Clear();
            foreach (LogCheck check in checks)
            {
                int index = results.Rows.Add("", check.Number, check.Line);
                results.Rows[index].Height = check.Ok ? 38 : 40 + check.Issues.Count * 24;
            }
            results.ClearSelection();
            hoverRow = -1;
            results.Visible = checks.Count > 0;
            emptyResults.Visible = checks.Count == 0;
            List<LogCheck> bad = checks.Where(c => !c.Ok).ToList();
            if (checks.Count == 0)
            {
                summary.Text = Translate("读取日志后自动检查。", "Results appear as soon as the logs are read.");
                summary.ForeColor = Palette.Muted;
            }
            else if (bad.Count == 0)
            {
                summary.Text = string.Format(Translate("已检查的 {0} 行符合格式。", "All {0} checked lines are valid."), checks.Count);
                summary.ForeColor = Palette.Success;
            }
            else
            {
                summary.Text = string.Format(Translate("{0} 行中有 {1} 行违规：第 {2} 行", "{1} of {0} lines are violations: {2}"),
                    checks.Count, bad.Count, string.Join(language == UiLanguage.Chinese ? "、" : ", ", bad.Select(c => (language == UiLanguage.Chinese ? "" : "#") + c.Number)));
                summary.ForeColor = Palette.Accent;
            }
            copyReport.Enabled = bad.Count > 0;
            clearLogs.Enabled = checks.Count > 0;
        }

        internal string Report()
        {
            return string.Join(Environment.NewLine, checks.Where(c => !c.Ok).Select(c => "#" + c.Number + "  " + c.Line + Environment.NewLine +
                string.Join(Environment.NewLine, c.Issues.Select(i => "    " + LogValidator.FieldName(i.Field) + ": " + Translate(i.Chinese, i.English)))));
        }

        // ---------------- Shared ----------------

        internal void SetLanguage(UiLanguage selected)
        {
            language = selected;
            ApplyLanguage();
            UpdateSignal();
            UpdateLogs();
            results.Invalidate();
        }

        private void ApplyLanguage()
        {
            Text = Translate("每日任务", "Daily Tasks");
            pages.Items = new[] { "Signal Decode", "Log Integrity" };
            onTop.Text = Translate("置顶", "Keep on top");
            readButton.Text = Translate("读取信号", "Read signal");
            reselect.Text = Translate("重选区域", "Reselect");
            reselect.Visible = signalArea != null;
            shortButton.Text = Translate("短 S", "Short S");
            longButton.Text = Translate("长 L", "Long L");
            undoButton.Text = Translate("退格", "Undo");
            clearSignal.Text = Translate("清空", "Clear");
            modeLabel.Text = Translate("编码", "Encoding");
            copyCode.Text = Translate("复制代码", "Copy code");
            strip.EmptyText = Translate("框选游戏里的所有 PULSE SIGNAL 脉冲行", "Select all PULSE SIGNAL rows in the game");
            tips.SetToolTip(readButton, Translate("截取保存的区域并识别脉冲。第一次请框住 PULSE SIGNAL 的所有脉冲行。",
                "Capture and read pulses from the saved area. The first time, select all PULSE SIGNAL rows."));
            tips.SetToolTip(pulseInput, Translate("可以粘贴或输入：S/L、./-、0/1 都行。", "Paste or type pulses: S/L, ./-, or 0/1 all work."));
            tips.SetToolTip(strip, Translate("点某个脉冲可以在长短之间切换。", "Click a pulse to switch it between short and long."));
            string morse = string.Join("  ", Enumerable.Range(0, 10).Select(i => i + " " + SignalDecoder.Morse[i]));
            string binary = string.Join("  ", Enumerable.Range(0, 10).Select(i => i + " " + SignalDecoder.Binary[i]));
            referenceLabel.Text = "Morse   ×5   " + morse + Environment.NewLine + "Binary  ×4   " + binary + Environment.NewLine + Environment.NewLine +
                Translate("15、25、50 个脉冲是 Morse；12、16 是 Binary；20、40 两种都可能，默认 Binary。",
                    "15, 25, and 50 pulses are Morse; 12 and 16 are binary; 20 and 40 fit both and default to binary.");

            formatLabel.Text = "[HH:MM:SS] TYPE /sector=1..256 /status=OK|WARN|ERROR";
            typesLabel.Text = "TYPE  " + string.Join(" · ", LogValidator.Types) + Environment.NewLine +
                Translate("HH 00–23，MM、SS 00–59。勾选违规的行，再为每一行选对应的错误类型。", "HH 00–23, MM and SS 00–59. Tick each violation, then pick the matching error type for it.");
            logsLabel.Text = Translate("识别出的日志（可以直接修改）", "Recognized log lines (you can edit them)");
            emptyResults.Text = Translate("还没有日志" + Environment.NewLine + "点「读取日志」，框住游戏里的日志列表", "No logs yet" + Environment.NewLine + "Choose Read logs and select the log list in the game");
            results.Columns[0].HeaderText = "";
            results.Columns[1].HeaderText = "#";
            results.Columns[2].HeaderText = Translate("日志与问题（标签即游戏里要选的错误类型）", "Log line and problem (the tag is the error type to pick in the game)");
            readLogs.Text = Translate("读取日志", "Read logs");
            reselectLogs.Text = Translate("重选区域", "Reselect");
            tips.SetToolTip(readLogs, Translate("截取日志列表并用 Windows 自带的文字识别读出来。第一次会先让你框选日志区域。",
                "Capture the log list and read it with the text recognition built into Windows. The first time, you select the log area."));
            clearLogs.Text = Translate("清空", "Clear");
            copyReport.Text = Translate("复制结果", "Copy results");
        }

        private void SetStatus(string text, bool warning = false)
        {
            status.Text = text;
            status.ForeColor = warning ? Palette.Accent : Palette.Muted;
        }

        private void CopyText(string text, string done)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                Clipboard.SetText(text);
                SetStatus(done);
            }
            catch (ExternalException)
            {
                SetStatus(Translate("剪贴板暂不可用，请再试一次。", "Clipboard unavailable. Try again."), true);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            bool typing = ActiveControl is TextBoxBase || (ActiveControl is InputBox);
            if (pages.SelectedIndex == 0 && !typing)
            {
                if (keyData == Keys.S) { AppendPulse('S'); return true; }
                if (keyData == Keys.L) { AppendPulse('L'); return true; }
                if (keyData == Keys.Back) { Undo(); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tips.Dispose();
                foreach (Font font in new[] { monoFont, codeFont, tagFont, boldFont }) font.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // Draws the pulses grouped into digits, with the decoded digit under each group. Click a pulse to flip it.
    internal sealed class PulseStrip : Control
    {
        private string pulses = "", code = "";
        private int group;
        private readonly List<RectangleF> boxes = new List<RectangleF>();
        private readonly List<PointF> labels = new List<PointF>();
        private int hover = -1;
        private float digitSize = 12;
        public Action<int> PulseClicked;
        public string EmptyText = "";

        public PulseStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Palette.Panel;
        }

        public void SetSignal(string nextPulses, int groupSize, string nextCode)
        {
            pulses = nextPulses ?? "";
            group = groupSize;
            code = nextCode ?? "";
            hover = -1;
            Arrange();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Arrange();
        }

        private void Arrange()
        {
            boxes.Clear();
            labels.Clear();
            if (pulses.Length == 0 || Width < 40) return;
            // Shrink the pulses until every group fits; groups wrap to new lines whole.
            for (float scale = 1f; scale >= .45f; scale -= .05f)
            {
                if (TryLayout(scale)) return;
            }
            TryLayout(.45f);
        }

        private bool TryLayout(float scale)
        {
            boxes.Clear();
            labels.Clear();
            float shortW = 12 * scale, longW = 30 * scale, height = 22 * scale, gap = 5 * scale, groupGap = 20 * scale;
            digitSize = Math.Max(7, 12 * scale);
            float lineHeight = height + (group > 0 ? digitSize * 2.5f : 12 * scale);
            int size = group > 0 ? group : 1;
            if (group == 0) groupGap = gap;
            float x = 2, y = 6;
            for (int start = 0; start < pulses.Length; start += size)
            {
                int count = Math.Min(size, pulses.Length - start);
                float width = 0;
                for (int i = 0; i < count; i++) width += (pulses[start + i] == 'S' ? shortW : longW) + (i > 0 ? gap : 0);
                if (x > 2 && x + width > Width - 2)
                {
                    x = 2;
                    y += lineHeight;
                }
                float left = x;
                for (int i = 0; i < count; i++)
                {
                    float w = pulses[start + i] == 'S' ? shortW : longW;
                    boxes.Add(new RectangleF(x, y, w, height));
                    x += w + gap;
                }
                if (group > 0) labels.Add(new PointF(left + width / 2, y + height + 4 * scale));
                x += groupGap - gap;
            }
            float used = y + lineHeight - 6;
            if (used > Height && scale > .45f) return false;
            float shift = Math.Max(0, (Height - used) / 2 - 6);
            for (int i = 0; i < boxes.Count; i++) boxes[i] = new RectangleF(boxes[i].X, boxes[i].Y + shift, boxes[i].Width, boxes[i].Height);
            for (int i = 0; i < labels.Count; i++) labels[i] = new PointF(labels[i].X, labels[i].Y + shift);
            return true;
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < boxes.Count; i++)
            {
                RectangleF r = boxes[i];
                r.Inflate(2, 4);
                if (r.Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = HitTest(e.Location);
            if (hit == hover) return;
            hover = hit;
            Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int hit = HitTest(e.Location);
            if (hit >= 0 && e.Button == MouseButtons.Left && PulseClicked != null) PulseClicked(hit);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            if (pulses.Length == 0)
            {
                float cx = Width / 2f, cy = Height / 2f - 12;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Palette.Border, 1.6f))
                {
                    float x = cx - 52;
                    foreach (int w in new[] { 10, 26, 10, 10, 26, 10 })
                    {
                        if (w == 10) g.DrawRectangle(pen, x, cy - 8, w, 16);
                        else using (var fill = new SolidBrush(Palette.Border)) g.FillRectangle(fill, x, cy - 8, w, 16);
                        x += w + 5;
                    }
                }
                TextRenderer.DrawText(g, EmptyText, Font, new Rectangle(0, (int)cy + 16, Width, 24), Palette.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int size = group > 0 ? group : pulses.Length;
            using (var digitFont = Theme.Mono(digitSize))
            {
                for (int i = 0; i < boxes.Count; i++)
                {
                    int groupIndex = i / size;
                    bool invalid = group > 0 && groupIndex < code.Length && code[groupIndex] == '?';
                    Color color = group == 0 ? Palette.Muted : invalid ? Palette.Danger : Palette.Primary;
                    if (i == hover) color = Palette.Text;
                    RectangleF r = boxes[i];
                    if (pulses[i] == 'L')
                        using (GraphicsPath path = Theme.Round(r, 2))
                        using (var fill = new SolidBrush(color))
                            g.FillPath(fill, path);
                    else
                        using (GraphicsPath path = Theme.Round(new RectangleF(r.X + .8f, r.Y + .8f, r.Width - 1.6f, r.Height - 1.6f), 2))
                        using (var pen = new Pen(color, 1.6f))
                            g.DrawPath(pen, path);
                }
                if (group == 0) return;
                for (int k = 0; k < labels.Count && k < code.Length; k++)
                {
                    string digit = code[k].ToString();
                    Size s = TextRenderer.MeasureText(g, digit, digitFont, Size.Empty, TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, digit, digitFont, new Point((int)(labels[k].X - s.Width / 2f), (int)labels[k].Y),
                        code[k] == '?' ? Palette.Danger : Palette.Text, TextFormatFlags.NoPadding);
                }
            }
        }
    }
}
