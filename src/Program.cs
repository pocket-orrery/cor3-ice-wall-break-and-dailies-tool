using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle(GlyphFinder.Program.ProductName)]
[assembly: AssemblyProduct(GlyphFinder.Program.ProductName)]
[assembly: AssemblyDescription("A screen-based pattern finder for " + GlyphFinder.Program.GameName)]
[assembly: AssemblyVersion(GlyphFinder.Program.ReleaseVersion + ".0")]
[assembly: AssemblyFileVersion(GlyphFinder.Program.ReleaseVersion + ".0")]
[assembly: AssemblyInformationalVersion(GlyphFinder.Program.ReleaseVersion)]

namespace GlyphFinder
{
    internal static class Program
    {
        internal const string GameName = "ICE WALL BREAK";
        internal const string ProductName = GameName + " Finder";
        internal const string ReleaseVersion = "1.0.3";

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new FinderWindow());
        }
    }

    internal sealed class Calibration
    {
        public Rectangle Target, Board;
        public int BoardRows = 10;
        public int ScanMilliseconds = 100;

        private static readonly string PathName = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TriangleGlyphFinder", "layout.txt");

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathName));
                int[] values = { Target.X, Target.Y, Target.Width, Target.Height,
                    Board.X, Board.Y, Board.Width, Board.Height, BoardRows, ScanMilliseconds };
                File.WriteAllText(PathName, string.Join(",", values.Select(v => v.ToString(CultureInfo.InvariantCulture))));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static Calibration Load()
        {
            try
            {
                string[] fields = File.ReadAllText(PathName).Split(',');
                if (fields.Length != 9 && fields.Length != 10 && fields.Length != 12) return null;
                int[] v = fields.Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                int rows = v.Length == 12 ? v[9] : v[8];
                if (v[2] < 15 || v[3] < 15 || v[6] < 15 || v[7] < 15 ||
                    rows < 1 || rows > FinderWindow.MaxRows) return null;
                return new Calibration
                {
                    Target = new Rectangle(v[0], v[1], v[2], v[3]),
                    Board = new Rectangle(v[4], v[5], v[6], v[7]),
                    BoardRows = rows,
                    ScanMilliseconds = v.Length == 10 ? v[9] : 100
                };
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (FormatException) { return null; }
            catch (OverflowException) { return null; }
        }
    }

    internal sealed class ScreenSelection
    {
        public readonly Rectangle Bounds;
        public readonly Bitmap Image;

        public ScreenSelection(Rectangle bounds, Bitmap image)
        {
            Bounds = bounds;
            Image = image;
        }
    }

    internal static class ScreenCapture
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmFlush();

        public static async Task WaitForDesktop()
        {
            // Return to the message loop so hide/activation and repaint messages can finish before taking a frame.
            await Task.Delay(320);
            DwmFlush();
        }

        public static Bitmap Read(Rectangle area)
        {
            var image = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
            try
            {
                using (Graphics g = Graphics.FromImage(image))
                    g.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
                return image;
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }

        public static Rectangle WithMargin(Rectangle area, int rows, Rectangle bounds)
        {
            area.Inflate((int)Math.Ceiling(.42 * area.Width / rows) + 2,
                         (int)Math.Ceiling(.42 * area.Height / rows) + 2);
            return Rectangle.Intersect(area, bounds);
        }
    }

    internal sealed class FinderWindow : Form
    {
        internal enum ViewMode { Idle, Live, Paused, Screenshot, Demo }
        private enum ScreenIssue { None, CellsTooSmall, AreaChanged, Covered, CaptureFailed }

        internal const int MaxRows = 20;
        private static readonly int[] ScanIntervals = { 100, 500, 1000, 1500, 2000 };
        private static readonly string[] ScanLabels = { "100 ms", "500 ms", "1 s", "1.5 s", "2 s" };
        private readonly Stepper boardRows = new Stepper { Minimum = 1, Maximum = MaxRows, Value = 10 };
        private readonly FlatButton scanInterval = new FlatButton { Chevron = true, MinimumSize = new Size(98, 30) };
        private readonly ContextMenuStrip scanMenu = new ContextMenuStrip();
        private readonly FlatButton select = new FlatButton { Icon = ButtonGlyph.Select };
        private readonly FlatButton watch = new FlatButton { Kind = ButtonKind.Primary, MinimumSize = new Size(88, 30) };
        private readonly FlatButton jobsButton = new FlatButton { Icon = ButtonGlyph.List };
        private readonly FlatButton dailyButton = new FlatButton { Icon = ButtonGlyph.Pulse };
        private DailyTaskWindow dailyWindow;
        private readonly JobRound jobRound = new JobRound();
        private JobGuideWindow jobsWindow;
        private readonly FlatButton testButton = new FlatButton { Kind = ButtonKind.Ghost, Chevron = true, Margin = Padding.Empty };
        private readonly ContextMenuStrip testMenu = new ContextMenuStrip();
        private readonly ToolStripMenuItem screenshotItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem demoItem = new ToolStripMenuItem();
        private readonly FlatButton languageButton = new FlatButton { Kind = ButtonKind.Ghost, Chevron = true, Margin = Padding.Empty };
        private readonly ContextMenuStrip languageMenu = new ContextMenuStrip();
        private readonly Label rowsLabel = OptionLabel();
        private readonly Label intervalLabel = OptionLabel();
        private readonly ToolTip tips = Theme.MakeToolTip();
        private readonly Card statusCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(32, 9, 12, 8) };
        private readonly Label status = new Label { Dock = DockStyle.Fill, BackColor = Palette.Panel, ForeColor = Palette.Text, UseMnemonic = false };
        private readonly Card boardCard = new Card { Dock = DockStyle.Fill, Brackets = true, Padding = new Padding(14, 14, 14, 12), Margin = Padding.Empty };
        private readonly BoardPreview preview = new BoardPreview { Dock = DockStyle.Fill };
        private readonly Label ranking = new Label { Dock = DockStyle.Bottom, Height = 58, BackColor = Palette.Panel, ForeColor = Palette.Text, UseMnemonic = false };
        private readonly Timer timer = new Timer { Interval = 100 };
        private readonly Stopwatch lastFit = Stopwatch.StartNew();
        private readonly Func<Rectangle, Bitmap> capture;
        private readonly Func<Rectangle> desktopBounds;
        private readonly LanguagePreference preference;
        private UiLanguage language;
        private Calibration live = Calibration.Load();
        private Calibration current;
        private Bitmap still;
        private Grid boardGrid;
        private Pixels lastTarget, lastBoard;
        private TargetPattern lastPattern;
        private int boardKnownAtFit, lastBoardCount;
        private bool running;
        private bool assigning;
        private bool fitAgain = true;
        private ViewMode mode;
        private ScreenIssue issue;
        private Matches lastResult;
        private Candidate[] leading = new Candidate[0];
        private bool exact;

        public FinderWindow() : this(area => ScreenCapture.Read(area), () => SystemInformation.VirtualScreen,
            null, new WindowSizePreference()) { }

        private readonly WindowSizePreference windowSizes;

        internal FinderWindow(Func<Rectangle, Bitmap> capture, Func<Rectangle> desktopBounds, LanguagePreference preference = null,
            WindowSizePreference windowSizes = null)
        {
            this.capture = capture;
            this.desktopBounds = desktopBounds;
            this.windowSizes = windowSizes;
            this.preference = preference ?? new LanguagePreference();
            language = this.preference.Load();
            BackColor = Palette.Background;
            ForeColor = Palette.Text;
            Font = Theme.Ui(9f);
            Icon icon = Theme.AppIcon();
            if (icon != null) Icon = icon;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(660, 600);
            MinimumSize = new Size(630, 440);
            StartPosition = FormStartPosition.CenterScreen;
            if (windowSizes != null)
            {
                windowSizes.Bind(this, "finder");
                FitWindow(this, Screen.FromPoint(Cursor.Position).WorkingArea);
            }

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(14, 12, 14, 12) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);
            foreach (ContextMenuStrip menu in new[] { scanMenu, testMenu, languageMenu }) Theme.Style(menu);
            select.Click += async delegate { await SelectScreen(); };
            jobsButton.Click += delegate { ShowJobs(); };
            dailyButton.Click += delegate { ShowDaily(); };
            screenshotItem.Click += delegate { OpenImage(); };
            demoItem.Click += delegate { Demo(); };
            testMenu.Items.AddRange(new ToolStripItem[] { screenshotItem, demoItem });
            testButton.Click += delegate { testMenu.Show(testButton, new Point(0, testButton.Height + 2)); };
            watch.Click += async delegate
            {
                if (running) Pause();
                else if (live == null) await SelectScreen();
                else StartLive(live, true);
            };
            layout.Controls.Add(Bar(new Control[] { watch, select, jobsButton, dailyButton }, testButton), 0, 0);

            for (int i = 0; i < ScanIntervals.Length; i++)
            {
                int index = i;
                scanMenu.Items.Add(ScanLabels[i], null, delegate { ChooseScanInterval(index, true); });
            }
            ChooseScanInterval(live == null ? 0 : Math.Max(0, Array.IndexOf(ScanIntervals, live.ScanMilliseconds)), false);
            scanInterval.Click += delegate { scanMenu.Show(scanInterval, new Point(0, scanInterval.Height + 2)); };
            intervalLabel.Margin = new Padding(10, 0, 6, 0);
            languageMenu.Items.Add("中文", null, delegate { ChooseLanguage(UiLanguage.Chinese); });
            languageMenu.Items.Add("English", null, delegate { ChooseLanguage(UiLanguage.English); });
            languageButton.Click += delegate { languageMenu.Show(languageButton, new Point(0, languageButton.Height + 2)); };
            layout.Controls.Add(Bar(new Control[] { rowsLabel, boardRows, intervalLabel, scanInterval }, languageButton), 0, 1);
            boardRows.ValueChanged += SettingsChanged;

            statusCard.Controls.Add(status);
            layout.Controls.Add(statusCard, 0, 2);
            ranking.Font = Theme.Ui(8.5f);
            ranking.Padding = new Padding(4, 8, 0, 0);
            boardCard.Controls.Add(preview);
            boardCard.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Palette.Border });
            boardCard.Controls.Add(ranking);
            layout.Controls.Add(boardCard, 0, 3);
            if (live != null) SetOptions(live);
            ApplyLanguage();
            timer.Tick += delegate { RefreshMatch(); };
            FormClosed += delegate
            {
                if (jobsWindow != null) jobsWindow.Close();
                if (dailyWindow != null) dailyWindow.Close();
                timer.Dispose();
                scanMenu.Dispose();
                testMenu.Dispose();
                languageMenu.Dispose();
                tips.Dispose();
                if (still != null) still.Dispose();
            };
        }

        private string Translate(string chinese, string english)
        {
            return language.Text(chinese, english);
        }

        private void ChooseLanguage(UiLanguage selected)
        {
            if (selected == language) return;
            language = selected;
            preference.Save(language);
            ApplyLanguage();
            if (jobsWindow != null) jobsWindow.SetLanguage(language);
            if (dailyWindow != null) dailyWindow.SetLanguage(language);
        }

        private void ApplyLanguage()
        {
            Text = Translate(Program.GameName + " 定位器", Program.ProductName) + " v" + Program.ReleaseVersion;
            select.Text = Translate("框选", "Select");
            jobsButton.Text = Translate("工作手册", "Job Guide");
            dailyButton.Text = Translate("每日任务", "Daily");
            tips.SetToolTip(dailyButton, Translate("Signal Decode 与 Log Integrity 两个每日任务的辅助工具。", "Helpers for the Signal Decode and Log Integrity daily tasks."));
            testButton.Text = Translate("测试", "Test");
            screenshotItem.Text = Translate("打开截图", "Open screenshot");
            demoItem.Text = Translate("样图", "Demo");
            UpdateWatchText();
            rowsLabel.Text = Translate("棋盘行", "Rows");
            intervalLabel.Text = Translate("扫描间隔", "Interval");
            languageButton.Text = Translate("中文", "EN");
            ((ToolStripMenuItem)languageMenu.Items[0]).Checked = language == UiLanguage.Chinese;
            ((ToolStripMenuItem)languageMenu.Items[1]).Checked = language == UiLanguage.English;
            tips.SetToolTip(select, Translate("从游戏画面框选目标和整个棋盘。", "Select the target area and the whole board."));
            tips.SetToolTip(watch, Translate("开始或暂停实时监看。", "Start or pause live scanning."));
            tips.SetToolTip(scanInterval, Translate("两次实时截屏之间的间隔，监看时也可以修改。", "Time between scans. You can change it while scanning."));
            tips.SetToolTip(languageButton, Translate("切换界面语言", "Change interface language"));
            preview.EmptyCaption = Translate("棋盘预览", "Board preview");
            preview.Invalidate();
            UpdateResultText();
        }

        // The job tracker is modeless so it can stay beside the game while the Finder keeps scanning.
        private void ShowJobs()
        {
            if (jobsWindow != null)
            {
                if (jobsWindow.WindowState == FormWindowState.Minimized) jobsWindow.WindowState = FormWindowState.Normal;
                jobsWindow.Activate();
                return;
            }
            jobsWindow = new JobGuideWindow(language, jobRound, PickJobImage);
            if (windowSizes != null)
            {
                JobGuideWindow guide = jobsWindow;
                windowSizes.Bind(guide, "jobs", () => !guide.IsCompact);
            }
            jobsWindow.FormClosed += delegate { jobsWindow = null; };
            PlaceBeside(jobsWindow);
            jobsWindow.Show();
        }

        private void ShowDaily()
        {
            if (dailyWindow != null)
            {
                if (dailyWindow.WindowState == FormWindowState.Minimized) dailyWindow.WindowState = FormWindowState.Normal;
                dailyWindow.Activate();
                return;
            }
            dailyWindow = new DailyTaskWindow(language, CaptureToolArea, PickSelection);
            if (windowSizes != null) windowSizes.Bind(dailyWindow, "daily");
            dailyWindow.FormClosed += delegate { dailyWindow = null; };
            PlaceBeside(dailyWindow);
            dailyWindow.Show();
        }

        private void PlaceBeside(Form window)
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            FitWindow(window, area);
            int x = Right + 6 + window.Width <= area.Right ? Right + 6 : Math.Max(area.Left, Left - 6 - window.Width);
            window.Location = new Point(Math.Max(area.Left, Math.Min(x, area.Right - window.Width)),
                Math.Max(area.Top, Math.Min(Top, area.Bottom - window.Height)));
        }

        private static void FitWindow(Form window, Rectangle area)
        {
            window.Size = new Size(Math.Min(window.Width, area.Width), Math.Min(window.Height, area.Height));
        }

        private async Task<Bitmap> PickJobImage(string chinese, string english)
        {
            ScreenSelection selection = await PickSelection(chinese, english);
            return selection == null ? null : selection.Image;
        }

        private IEnumerable<Form> VisibleToolWindows()
        {
            return new Form[] { this, jobsWindow, dailyWindow }
                .Where(form => form != null && form.Visible && form.WindowState != FormWindowState.Minimized);
        }

        // Keep the selected pixels from the same clean frame that the user saw in the picker.
        private async Task<ScreenSelection> PickSelection(string chinese, string english)
        {
            try
            {
                return await CaptureWithoutTools(() =>
                {
                    Rectangle desktop = SystemInformation.VirtualScreen;
                    using (Bitmap frozen = ScreenCapture.Read(desktop))
                    using (var picker = new SelectionWindow(frozen, desktop, language, Translate(chinese, english)))
                    {
                        if (picker.ShowDialog() != DialogResult.OK) return null;
                        Rectangle area = picker.Target;
                        Bitmap image = frozen.Clone(area, PixelFormat.Format32bppArgb);
                        area.Offset(desktop.Location);
                        return new ScreenSelection(area, image);
                    }
                });
            }
            catch (Exception ex)
            {
                if (!IsCaptureError(ex)) throw;
                MessageBox.Show(Translate("截屏失败。请切换到窗口化或无边框窗口，再重新框选。",
                    "Screen capture failed. Switch the game to windowed or borderless mode and select again."), Text);
                return null;
            }
        }

        private Task<Bitmap> CaptureToolArea(Rectangle area)
        {
            return CaptureWithoutTools(() => capture(area), area);
        }

        private bool capturePending;

        private async Task<T> CaptureWithoutTools<T>(Func<T> read, Rectangle? area = null)
        {
            if (capturePending || IsDisposed) return default(T);
            capturePending = true;
            bool resume = timer.Enabled;
            Form active = Form.ActiveForm;
            Form[] hidden = VisibleToolWindows().Where(form => !area.HasValue || form.Bounds.IntersectsWith(area.Value)).ToArray();
            timer.Stop();
            try
            {
                foreach (Form form in hidden) form.Hide();
                await ScreenCapture.WaitForDesktop();
                return IsDisposed ? default(T) : read();
            }
            finally
            {
                try
                {
                    if (!IsDisposed)
                    {
                        foreach (Form form in hidden) if (!form.IsDisposed) form.Show();
                        if (active != null && !active.IsDisposed && active.Visible) active.Activate();
                        if (resume) timer.Start();
                    }
                }
                finally { capturePending = false; }
            }
        }

        private bool CoversGame(Rectangle area)
        {
            return VisibleToolWindows().Any(form => form.Bounds.IntersectsWith(area));
        }

        private void UpdateWatchText()
        {
            watch.Text = running ? Translate("暂停", "Pause") : Translate("开始", "Start");
            watch.Icon = running ? ButtonGlyph.Pause : ButtonGlyph.Play;
            watch.Kind = running ? ButtonKind.Normal : ButtonKind.Primary;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
        }

        // Left-aligned controls, a flexible gap, then one control pinned to the right edge. Everything is centered vertically.
        private static TableLayoutPanel Bar(Control[] left, Control right)
        {
            var bar = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = left.Length + 2, Margin = Padding.Empty };
            bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < left.Length; i++) bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < left.Length; i++)
            {
                left[i].Anchor = AnchorStyles.Left;
                bar.Controls.Add(left[i], i, 0);
            }
            right.Anchor = AnchorStyles.Right;
            bar.Controls.Add(right, left.Length + 1, 0);
            return bar;
        }

        private static Label OptionLabel()
        {
            return new Label { AutoSize = true, ForeColor = Palette.Muted, Margin = new Padding(0, 0, 6, 0) };
        }

        private void ChooseScanInterval(int index, bool save)
        {
            timer.Interval = ScanIntervals[index];
            scanInterval.Text = ScanLabels[index];
            for (int i = 0; i < scanMenu.Items.Count; i++)
                ((ToolStripMenuItem)scanMenu.Items[i]).Checked = i == index;
            if (!save || live == null) return;
            live.ScanMilliseconds = timer.Interval;
            live.Save();
        }

        private Calibration ReadOptions(Rectangle target, Rectangle board)
        {
            return new Calibration
            {
                Target = target, Board = board, BoardRows = (int)boardRows.Value, ScanMilliseconds = timer.Interval
            };
        }

        private void SetOptions(Calibration values)
        {
            assigning = true;
            boardRows.Value = values.BoardRows;
            assigning = false;
        }

        private void SettingsChanged(object sender, EventArgs e)
        {
            Calibration edited = current ?? live;
            if (assigning || edited == null) return;
            edited.BoardRows = (int)boardRows.Value;
            fitAgain = true;
            if (edited == live) edited.Save();
            if (running || still != null) RefreshMatch();
        }

        private async Task SelectScreen()
        {
            Calibration selected = null;
            try
            {
                selected = await CaptureWithoutTools(() =>
                {
                    Rectangle desktop = SystemInformation.VirtualScreen;
                    using (Bitmap frozen = ScreenCapture.Read(desktop))
                    using (var picker = new SelectionWindow(frozen, desktop, language))
                    {
                        if (picker.ShowDialog() != DialogResult.OK) return null;
                        Rectangle target = picker.Target;
                        Rectangle board = picker.Board;
                        target.Offset(desktop.Location);
                        board.Offset(desktop.Location);
                        return ReadOptions(target, board);
                    }
                });
            }
            catch (Exception ex)
            {
                if (!IsCaptureError(ex)) throw;
                MessageBox.Show(Translate("截屏失败。请切换到窗口化或无边框窗口，再重新框选。",
                    "Screen capture failed. Switch the game to windowed or borderless mode and select again."), Text);
            }
            if (selected != null)
            {
                live = selected;
                live.Save();
                StartLive(live, true);
            }
        }

        private void OpenImage()
        {
            using (var dialog = new OpenFileDialog
            {
                Title = Translate("打开游戏截图", "Open a game screenshot"),
                Filter = Translate("图片", "Images") + " (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|" + Translate("所有文件", "All files") + " (*.*)|*.*"
            })
            {
                timer.Stop();
                try
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    using (var image = new Bitmap(dialog.FileName))
                    using (var picker = new SelectionWindow(image, null, language))
                    {
                        if (picker.ShowDialog(this) != DialogResult.OK) return;
                        StartStill(new Bitmap(image), ReadOptions(picker.Target, picker.Board), ViewMode.Screenshot);
                    }
                }
                catch (Exception ex)
                {
                    if (!IsCaptureError(ex) && !(ex is IOException)) throw;
                    MessageBox.Show(this, Translate("无法读取截图。请选用 PNG、JPG 或 BMP 图片。",
                        "Could not read the screenshot. Choose a PNG, JPG, or BMP image."), Text);
                }
                finally { if (running) timer.Start(); }
            }
        }

        private void Demo()
        {
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("sample.png"))
            using (var image = new Bitmap(source))
                StartStill(new Bitmap(image), new Calibration
                {
                    Target = new Rectangle(44, 109, 330, 390),
                    Board = new Rectangle(481, 103, 522, 455)
                }, ViewMode.Demo);
        }

        private void StartStill(Bitmap image, Calibration selection, ViewMode view)
        {
            Pause();
            if (still != null) still.Dispose();
            still = image;
            current = selection;
            SetOptions(selection);
            mode = view;
            fitAgain = true;
            RefreshMatch();
        }

        private void StartLive(Calibration selection, bool move)
        {
            if (still != null)
            {
                still.Dispose();
                still = null;
            }
            current = selection;
            SetOptions(selection);
            mode = ViewMode.Live;
            fitAgain = true;
            running = true;
            UpdateWatchText();
            TopMost = true;
            if (move) MoveAway(selection);
            RefreshMatch();
            timer.Start();
        }

        private void Pause()
        {
            timer.Stop();
            running = false;
            UpdateWatchText();
            TopMost = false;
            mode = ViewMode.Paused;
            issue = ScreenIssue.None;
            UpdateResultText();
        }

        private void MoveAway(Calibration selection)
        {
            var forbidden = new[] { selection.Target, selection.Board };
            if (!forbidden.Any(r => r.IntersectsWith(Bounds))) return;
            foreach (Size size in new[] { Size, MinimumSize })
            foreach (Screen screen in Screen.AllScreens.OrderBy(s => s.Bounds.IntersectsWith(selection.Board)))
            {
                Rectangle bounds = screen.WorkingArea;
                int[] xs = { bounds.Left + 5, bounds.Right - size.Width - 5,
                             selection.Target.Right + 6, selection.Board.Right + 6, selection.Board.Left - size.Width - 6 };
                int[] ys = { bounds.Top + 5, bounds.Bottom - size.Height - 5,
                             selection.Target.Bottom + 6, selection.Board.Top - size.Height - 6 };
                foreach (int x in xs)
                foreach (int y in ys)
                {
                    var trial = new Rectangle(new Point(x, y), size);
                    if (!bounds.Contains(trial) || forbidden.Any(r => r.IntersectsWith(trial))) continue;
                    Bounds = trial;
                    return;
                }
            }
        }

        private void RefreshMatch()
        {
            if (current.Board.Width / current.BoardRows < 14 || current.Board.Height / current.BoardRows < 11)
            {
                ClearResult(ScreenIssue.CellsTooSmall);
                return;
            }
            Rectangle bounds = still == null ? desktopBounds() : new Rectangle(Point.Empty, still.Size);
            if (!bounds.Contains(current.Target) || !bounds.Contains(current.Board))
            {
                ClearResult(ScreenIssue.AreaChanged);
                return;
            }
            if (running && (CoversGame(current.Target) || CoversGame(current.Board)))
            {
                ClearResult(ScreenIssue.Covered);
                return;
            }
            Rectangle boardArea = ScreenCapture.WithMargin(current.Board, current.BoardRows, bounds);
            try
            {
                using (Bitmap targetImage = Read(current.Target))
                using (Bitmap boardImage = Read(boardArea))
                    Analyze(targetImage, boardArea, boardImage);
            }
            catch (Exception ex)
            {
                if (!IsCaptureError(ex)) throw;
                ClearResult(ScreenIssue.CaptureFailed);
            }
        }

        private Bitmap Read(Rectangle area)
        {
            return still == null ? capture(area) : still.Clone(area, PixelFormat.Format32bppArgb);
        }

        private static Rectangle InArea(Rectangle selection, Rectangle area)
        {
            selection.Offset(-area.X, -area.Y);
            return selection;
        }

        private void Analyze(Bitmap targetImage, Rectangle boardArea, Bitmap boardImage)
        {
            var targetPixels = new Pixels(targetImage);
            var boardPixels = new Pixels(boardImage);
            Rectangle boardBox = InArea(current.Board, boardArea);
            bool initialFit = fitAgain;
            if (initialFit)
            {
                boardGrid = Grid.Fit(boardPixels, boardBox, current.BoardRows);
                lastFit.Restart();
                fitAgain = false;
                lastTarget = null;
                lastBoard = null;
            }
            bool sameTarget = targetPixels.SameGreen(lastTarget);
            if (sameTarget && boardPixels.SameGreen(lastBoard) && !ShouldRefit(lastBoardCount)) return;
            lastTarget = targetPixels;
            lastBoard = boardPixels;
            if (!sameTarget) lastPattern = TargetPattern.Read(targetPixels, 2 * boardGrid.HalfWidth / boardGrid.RowHeight);
            TargetPattern pattern = lastPattern;
            Matches found = Matcher.Find(pattern, boardPixels, boardGrid);
            if (initialFit) boardKnownAtFit = found.BoardCount;
            if (ShouldRefit(found.BoardCount))
            {
                boardGrid = Grid.Fit(boardPixels, boardBox, current.BoardRows);
                found = Matcher.Find(pattern, boardPixels, boardGrid);
                boardKnownAtFit = found.BoardCount;
                lastFit.Restart();
            }
            lastBoardCount = found.BoardCount;
            Present(found, boardImage, pattern);
        }

        private bool ShouldRefit(int revealed)
        {
            int difference = Math.Abs(revealed - boardKnownAtFit);
            return running && lastFit.ElapsedMilliseconds >= 850 &&
                (difference >= 7 || (boardKnownAtFit < 6 && difference >= 3));
        }

        private void Present(Matches found, Bitmap boardImage, TargetPattern pattern)
        {
            issue = ScreenIssue.None;
            lastResult = found;
            Candidate first = found.Ranked.FirstOrDefault();
            int best = first == null || first.Conflicts != 0 ? 0 : first.Confirmed;
            leading = best == 0 ? new Candidate[0] :
                found.Ranked.TakeWhile(c => c.Conflicts == 0 && c.Confirmed == best).ToArray();
            exact = leading.Length == 1 && best == found.TargetCount;
            UpdateResultText();
            preview.UpdateBoard((Bitmap)boardImage.Clone(), boardGrid, pattern, leading, exact);
        }

        private void UpdateResultText()
        {
            statusCard.Dot = issue != ScreenIssue.None ? Palette.Danger
                : running ? Palette.Success
                : mode == ViewMode.Paused ? Palette.Accent
                : mode == ViewMode.Demo || mode == ViewMode.Screenshot ? Palette.Primary
                : Palette.Faint;
            if (issue != ScreenIssue.None)
            {
                status.Text = IssueText();
                ranking.Text = "";
                return;
            }
            if (lastResult == null || leading.Length == 0)
                ranking.Text = Translate("行从棋盘顶部数，格从对应行的左边数。", "Count rows from the top and cells from the left.");
            if (mode == ViewMode.Idle)
            {
                status.Text = live == null
                    ? Translate("点「框选」选择目标和棋盘区域。", "Choose Select to mark the target and board areas.")
                    : Translate("已加载上次屏幕范围。游戏位置相同时可直接开始监看。", "Last selection loaded. Choose Start if the game has not moved.");
                return;
            }
            if (mode == ViewMode.Paused)
                status.Text = Translate("已暂停，画面保留在上一次分析。", "Paused. Showing the last scan.");
            else if (lastResult != null)
            {
                string view = mode == ViewMode.Live ? Translate("监看中", "Scanning") :
                    mode == ViewMode.Demo ? Translate("样图", "Demo") : Translate("截图", "Screenshot");
                string counts = string.Format(Translate("{0}   ·   目标 {1} 格   ·   棋盘 {2}/{3}",
                    "{0}   ·   Target cells {1}   ·   Board {2}/{3}"),
                    view, lastResult.TargetCount, lastResult.BoardCount, current.BoardRows * current.BoardRows);
                status.Text = counts + Environment.NewLine + MatchNote();
            }

            if (leading.Length > 3)
            {
                string cell = Translate("{0}. {1}行{2}格", "{0}. R{1} C{2}");
                ranking.Text = string.Join(Environment.NewLine, leading.Take(9)
                    .Select((candidate, index) => new
                    {
                        Line = index / 3,
                        Text = string.Format(cell, index + 1, candidate.Row + 1, candidate.Column + 1)
                    })
                    .GroupBy(entry => entry.Line)
                    .Select(line => string.Join("    ", line.Select(entry => entry.Text))));
                if (leading.Length > 9) ranking.Text += " …";
            }
            else if (leading.Length > 0)
                ranking.Text = string.Join(Environment.NewLine, leading.Select((c, i) =>
                    string.Format(Translate("{0}. 第 {1} 行第 {2} 格  相符 {3}  未揭 {4}{5}",
                        "{0}. Row {1}, cell {2}   Matched {3}   Hidden {4}{5}"),
                        i + 1, c.Row + 1, c.Column + 1, c.Confirmed, c.Unknown,
                        c.Uncertain == 0 ? "" : Translate("  待辨 ", "   Unclear ") + c.Uncertain)));
        }

        private string MatchNote()
        {
            Matches found = lastResult;
            if (found.TargetCount == 0)
                return Translate("请检查目标框选，或等待目标显示。", "Check the target selection or wait for the target to appear.");
            if (found.Ranked.Count == 0)
                return Translate("棋盘上没有能放下这个轮廓的位置。", "The target shape does not fit anywhere on the board.");
            if (found.Ranked[0].Conflicts != 0)
                return Translate("暂未找到相符位置，请检查框选和棋盘行数。", "No compatible positions. Check the selection and row count.");
            if (leading.Length == 0)
                return Translate("还没有足够的已揭符号定位。", "Too few revealed symbols to locate the target.");
            if (exact)
                return Translate("找到唯一全部匹配的位置。", "One full match found.");
            int best = leading[0].Confirmed;
            if (best == found.TargetCount)
                return string.Format(Translate("{0} 个位置全部匹配。", "{0} full matches found."), leading.Length);
            if (leading.Length > 1)
            {
                int unseen = found.Ranked.TakeWhile(c => c.Conflicts == 0).Count(c => c.Unknown == found.TargetCount);
                return string.Format(Translate("{0} 个并列 · 各相符 {1}/{2}{3}", "{0} tied · {1}/{2} matched each{3}"),
                    leading.Length, best, found.TargetCount,
                    unseen == 0 ? "" : string.Format(Translate(" · {0} 个全未揭", " · {0} all hidden"), unseen));
            }
            return string.Format(Translate("暂领先 · 相符 {0}/{1} · 等更多格子揭开", "Leading · {0}/{1} matched · Waiting for more"),
                best, found.TargetCount);
        }

        private string IssueText()
        {
            switch (issue)
            {
                case ScreenIssue.CellsTooSmall:
                    return Translate("格子太小。请放大游戏画面，或检查行数。", "Cells are too small. Enlarge the game or check the board row count.");
                case ScreenIssue.AreaChanged:
                    return Translate("屏幕范围已变化，请重新框选。", "Screen bounds changed. Select the areas again.");
                case ScreenIssue.Covered:
                    return Translate("定位器或工作手册窗口遮住了游戏区域。移开后会自动恢复。", "A Finder window covers the game area. Move it aside to resume.");
                default:
                    return Translate("无法读取屏幕。请检查游戏是否处于窗口化或无边框窗口。",
                        "Could not capture the screen. Check that the game is windowed or borderless.");
            }
        }

        private void ClearResult(ScreenIssue reason)
        {
            issue = reason;
            lastTarget = null;
            lastBoard = null;
            lastResult = null;
            leading = new Candidate[0];
            exact = false;
            UpdateResultText();
            preview.UpdateBoard(null, null, null, leading, false);
        }

        private static bool IsCaptureError(Exception ex)
        {
            return ex is ExternalException || ex is ArgumentException || ex is OutOfMemoryException;
        }
    }

    internal sealed class BoardPreview : Control
    {
        private Bitmap image;
        private Grid grid;
        private TargetPattern pattern;
        private Candidate[] ranked;
        private bool exactMatch;
        public string EmptyCaption { get; set; }

        public BoardPreview()
        {
            DoubleBuffered = true;
            BackColor = Palette.Panel;
            EmptyCaption = "";
        }

        public void UpdateBoard(Bitmap next, Grid nextGrid, TargetPattern nextPattern, Candidate[] candidates, bool exact)
        {
            if (image != null) image.Dispose();
            image = next;
            grid = nextGrid;
            pattern = nextPattern;
            ranked = candidates;
            exactMatch = exact;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (image == null)
            {
                float cx = Width / 2f, cy = Height / 2f - 14;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Palette.Border, 1.6f) { LineJoin = LineJoin.Round })
                {
                    for (int ring = 0; ring < 3; ring++)
                    {
                        float r = 34 - ring * 11;
                        e.Graphics.DrawPolygon(pen, new[] { new PointF(cx, cy - r), new PointF(cx + r * .87f, cy + r / 2), new PointF(cx - r * .87f, cy + r / 2) });
                    }
                }
                TextRenderer.DrawText(e.Graphics, EmptyCaption, Font, new Rectangle(0, (int)cy + 30, Width, 24), Palette.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            float scale = Math.Min((Width - 8f) / image.Width, (Height - 8f) / image.Height);
            if (scale <= 0) return;
            float left = (Width - image.Width * scale) / 2;
            float top = (Height - image.Height * scale) / 2;
            var graphics = e.Graphics;
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.DrawImage(image, left, top, image.Width * scale, image.Height * scale);
            Color color = exactMatch ? Palette.Accent : Palette.Alternate;
            using (var pen = new Pen(color, exactMatch ? 2.8f : 1.7f))
            using (var fill = new SolidBrush(Color.FromArgb(exactMatch ? 30 : 11, color)))
            using (var marker = new SolidBrush(color))
            {
                pen.LineJoin = LineJoin.Round;
                var drawn = new HashSet<Point>();
                foreach (Candidate candidate in ranked)
                foreach (PatternCell cell in pattern.Cells)
                {
                    int row = candidate.Row + cell.Row;
                    int column = candidate.Column + cell.Column;
                    if (!drawn.Add(new Point(column, row))) continue;
                    PointF[] points = grid.Outline(row, column);
                    for (int j = 0; j < points.Length; j++)
                        points[j] = new PointF(left + points[j].X * scale, top + points[j].Y * scale);
                    graphics.FillPolygon(fill, points);
                    graphics.DrawPolygon(pen, points);
                }
                for (int i = 0; i < ranked.Length; i++)
                {
                    Candidate c = ranked[i];
                    PointF anchor = grid.CenterAtTop(c.Row, c.Column);
                    PointF p = new PointF(left + anchor.X * scale, top + anchor.Y * scale);
                    int markerY = Math.Max(1, (int)p.Y - 17);
                    int width = i >= 9 ? 24 : 18;
                    graphics.FillEllipse(marker, p.X - width / 2f, markerY, width, 18);
                    TextRenderer.DrawText(graphics, (i + 1).ToString(), Font,
                        new Rectangle((int)p.X - width / 2, markerY, width, 18), Palette.Background,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && image != null) image.Dispose();
            base.Dispose(disposing);
        }
    }
}
