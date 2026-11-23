using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using GlyphFinder;

internal static class TestChecks
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private static void Require(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
    }

    private static Bitmap Crop(Bitmap image, Rectangle bounds)
    {
        return image.Clone(bounds, PixelFormat.Format32bppArgb);
    }

    private static Matches Match(Bitmap image, Rectangle targetBox, Rectangle boardBox, out TargetPattern pattern)
    {
        var bounds = new Rectangle(Point.Empty, image.Size);
        Rectangle boardArea = ScreenCapture.WithMargin(boardBox, 10, bounds);
        using (var target = Crop(image, targetBox))
        using (var board = Crop(image, boardArea))
        {
            boardBox.Offset(-boardArea.X, -boardArea.Y);
            var pixels = new Pixels(board);
            Grid grid = Grid.Fit(pixels, boardBox, 10);
            pattern = TargetPattern.Read(new Pixels(target), 2 * grid.HalfWidth / grid.RowHeight);
            return Matcher.Find(pattern, pixels, grid);
        }
    }

    private static bool Expected(Candidate candidate, TestBoard scene)
    {
        return candidate.Row == scene.AnchorRow && candidate.Column == scene.AnchorColumn &&
               candidate.Conflicts == 0 && candidate.Confirmed == scene.TargetCells;
    }

    private static void Describe(string title, Matches matches)
    {
        Console.WriteLine("{0}: {1} target, {2} revealed", title, matches.TargetCount, matches.BoardCount);
        foreach (Candidate c in matches.Ranked.Take(3))
            Console.WriteLine("   {0},{1}: {2} match, {3} hidden, {4} conflict, {5} unsure",
                c.Row + 1, c.Column + 1, c.Confirmed, c.Unknown, c.Conflicts, c.Uncertain);
    }

    private static void CreateHandles(Control control)
    {
        var handle = control.Handle;
        foreach (Control child in control.Controls) CreateHandles(child);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetBaseException().Message);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string output = args[0];
        Directory.CreateDirectory(output);
        var freshPreference = new LanguagePreference(Path.Combine(output, Guid.NewGuid().ToString("N") + ".txt"));
        using (var freshWindow = TestWindow(freshPreference))
            Require(freshWindow.Text == "ICE WALL BREAK Finder v" + Program.ReleaseVersion, "English title and version on first launch");
        var preference = new LanguagePreference(Path.Combine(output, "test-language.txt"));
        preference.Save(UiLanguage.Chinese);
        VerifyJobGuide(output);
        VerifyJobGuideList();
        VerifyTerminalPuzzle(output);
        VerifyDailyTasks(output);
        GenerateLogExamples(output);
        VerifyToolWindows(freshPreference);
        VerifyWindowSize(output);
        var a = new TestBoard(false);
        var b = new TestBoard(true);
        Generate(output, a, 0, "01-empty", false);
        Generate(output, a, 2, "02-two-candidates", false);
        Generate(output, a, 14, "03-three-tile-match", false);
        Generate(output, b, 17, "04-five-tile-match", false);
        Generate(output, b, 45, "05-five-tiles-scaled", true);

        using (var window = TestWindow(preference))
        {
            VerifyScanOptions(window);
            MethodInfo start = typeof(FinderWindow).GetMethod("StartStill", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo rank = typeof(FinderWindow).GetField("ranking", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo status = typeof(FinderWindow).GetField("status", BindingFlags.NonPublic | BindingFlags.Instance);
            var selection = new Calibration { Board = TestBoard.BoardBox, Target = TestBoard.TargetBox, BoardRows = 10 };
            start.Invoke(window, new object[] { a.Draw(0), selection, FinderWindow.ViewMode.Screenshot });
            Require(((Label)status.GetValue(window)).Text.Contains("还没有足够"), "empty status");
            start.Invoke(window, new object[] { a.Draw(2), selection, FinderWindow.ViewMode.Screenshot });
            Require(((Label)status.GetValue(window)).Text.Contains("2 个并列"), "ambiguity status");
            start.Invoke(window, new object[] { a.Draw(14), selection, FinderWindow.ViewMode.Screenshot });
            Require(((Label)rank.GetValue(window)).Text.StartsWith("1. 第 6 行第 5 格"), "shape A UI");
            start.Invoke(window, new object[] { b.Draw(17), selection, FinderWindow.ViewMode.Screenshot });
            Require(((Label)rank.GetValue(window)).Text.StartsWith("1. 第 5 行第 5 格"), "shape B UI");
            CreateHandles(window);
            window.PerformLayout();
            using (var bitmap = new Bitmap(window.Width, window.Height))
            {
                window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(output, "ui-preview.png"), ImageFormat.Png);
            }
            VerifyLanguage(window, preference, output);
        }

        using (var targetScene = b.Draw(0))
        using (var target = Crop(targetScene, TestBoard.TargetBox))
        {
            TargetPattern pattern = TargetPattern.Read(new Pixels(target), 1.15);
            Require(pattern.Cells.Count == 5, "five-cell extracted geometry");
            int previous = 0;
            for (int count = 1; count <= 100; count++)
            using (var boardScene = b.Draw(count))
            {
                TargetPattern now;
                Matches found = Match(boardScene, TestBoard.TargetBox, TestBoard.BoardBox, out now);
                Require(found.BoardCount >= previous && found.BoardCount == count, "sequential reveal " + count);
                if (count >= 14) Require(Expected(found.Ranked[0], b), "revealed ranking " + count);
                previous = found.BoardCount;
            }
            Console.WriteLine("All 100 sequential reveal steps counted exactly; all target cells ranked first after reveal #14.");
        }

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--partial-frame" || args[i] == "--full-frame")
            {
                bool full = args[i] == "--full-frame";
                CheckGameFrame(args[++i], full, output, preference);
            }
            else
            {
                using (var sample = new Bitmap(args[i]))
                {
                    TargetPattern pattern;
                    Matches result = Match(sample, new Rectangle(35, 107, 285, 288),
                        new Rectangle(445, 224, 591, 508), out pattern);
                    Describe("Original reference", result);
                    Require(result.TargetCount == 9 && result.Ranked[0].Row == 3 && result.Ranked[0].Column == 2 &&
                        result.Ranked[0].Confirmed == 9, "original reference");
                }
            }
        }
        VerifyLiveTimer(a, b, preference);
        return 0;
    }

    private static FinderWindow TestWindow(LanguagePreference preference)
    {
        return new FinderWindow(area => ScreenCapture.Read(area), () => SystemInformation.VirtualScreen, preference);
    }

    private static void VerifyLanguage(FinderWindow window, LanguagePreference preference, string output)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        Type type = typeof(FinderWindow);
        var menu = (ContextMenuStrip)type.GetField("languageMenu", fields).GetValue(window);
        var status = (Label)type.GetField("status", fields).GetValue(window);
        var ranking = (Label)type.GetField("ranking", fields).GetValue(window);
        var selector = (Button)type.GetField("select", fields).GetValue(window);
        var jobs = (Button)type.GetField("jobsButton", fields).GetValue(window);
        menu.Items[1].PerformClick();
        string englishTitle = "ICE WALL BREAK Finder v" + Program.ReleaseVersion;
        Require(window.Text == englishTitle && selector.Text == "Select" && jobs.Text == "Job Guide" &&
            status.Text.Contains("One full match found.") && ranking.Text.StartsWith("1. Row 5, cell 5"),
            "English text on the existing scan");
        using (var reopened = TestWindow(preference))
            Require(reopened.Text == englishTitle, "persistent English preference");
        using (var bitmap = new Bitmap(window.Width, window.Height))
        {
            window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(output, "ui-english-preview.png"), ImageFormat.Png);
        }
        Size normalSize = window.Size;
        window.Size = window.MinimumSize;
        window.PerformLayout();
        var button = (Button)type.GetField("languageButton", fields).GetValue(window);
        Require(button.Right <= button.Parent.ClientSize.Width, "language button fits the minimum window width");
        window.Size = normalSize;
        using (var sample = new TestBoard(true).Draw(17))
        using (var picker = new SelectionWindow(sample, null, preference.Load()))
        {
            Require(picker.Text == "Select the target and board", "English selection dialog");
            CreateHandles(picker);
            using (var bitmap = new Bitmap(picker.Width, picker.Height))
            {
                picker.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(output, "picker-english-preview.png"), ImageFormat.Png);
            }
        }
        menu.Items[0].PerformClick();
        Require(preference.Load() == UiLanguage.Chinese && window.Text == "ICE WALL BREAK 定位器 v" + Program.ReleaseVersion &&
            jobs.Text == "工作手册" &&
            ranking.Text.StartsWith("1. 第 5 行第 5 格"), "switch back to Chinese");
        Console.WriteLine("English first launch, both interface languages, current results and saved preference passed.");
    }

    private static T Field<T>(object owner, string name)
    {
        return (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    }

    private static void SaveWindow(Form window, string path)
    {
        CreateHandles(window);
        window.PerformLayout();
        using (var bitmap = new Bitmap(window.Width, window.Height))
        {
            window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    private static void VerifyJobGuide(string output)
    {
        List<string> ips = JobRound.ParseTargets("Remove 10.0.4.17 and 192.168.1.250." + Environment.NewLine +
            "Again 10.0.4.17, skip 999.1.1.1 and 1.2.3.4.5", TargetKind.Ip);
        Require(ips.SequenceEqual(new[] { "10.0.4.17", "192.168.1.250" }), "IPs picked out of pasted job details");
        Require(JobRound.ParseTargets(" sys_a.log\r\n- sys_b.log ; sys_a.log，sys_c.log ", TargetKind.Log)
            .SequenceEqual(new[] { "sys_a.log", "sys_b.log", "sys_c.log" }), "names split by lines and separators");
        Require(JobRound.ExtractTargets(new[] { "Target l0 . ø. 4 .17", "Repeat 10.0.4.17 and 10.0.4.18",
            "Ignore 999.1.1.1 and 1.2.3.4.5" }, TargetKind.Ip).SequenceEqual(new[] { "10.0.4.17", "10.0.4.18" }),
            "OCR extracts distinct valid IPs");

        var draftRound = new JobRound();
        var recognized = new List<string> { "File: ledger-01 . DAT", "Size: 12.8 MB", "Target IP: 172.16.ø.9",
            "audit.tar.gz", "ledger-01.DAT" };
        using (var draft = new JobGuideWindow(UiLanguage.English, draftRound, (chinese, english) => Task.FromResult(new Bitmap(10, 10)), image => recognized))
        {
            draft.SelectType(6);
            TextBox targetBox = Field<TextBox>(draft, "targetsBox");
            targetBox.Text = "draft.bin";
            var read = typeof(JobGuideWindow).GetMethod("ReadTargets", BindingFlags.Instance | BindingFlags.NonPublic);
            Finish((Task)read.Invoke(draft, null));
            Finish((Task)read.Invoke(draft, null));
            Require(targetBox.Lines.SequenceEqual(new[] { "draft.bin", "ledger-01.DAT", "audit.tar.gz" }) &&
                draftRound.Entries.Count == 0, "OCR appends reviewable filenames without adding a job");
            recognized.Clear();
            Finish((Task)read.Invoke(draft, null));
            Require(targetBox.Lines.Length == 3, "no OCR matches preserve the existing draft");
        }
        VerifyCardImport();

        var round = new JobRound();
        Func<string, JobEntry> job = name => new JobEntry { Type = JobCatalog.Find(name) };
        JobEntry logs = job("Log Deletion"), decrypt = job("File Decryption"), download = job("File Download"), wipe = job("File Elimination");
        logs.Server = "Relay-7";
        logs.Access = JobAccess.Acquisition;
        logs.Targets = JobRound.ParseTargets("auth.log", TargetKind.Log);
        download.Server = "Vault";
        download.Targets.Add("ledger.dat");
        wipe.Server = "relay-7 ";
        wipe.Targets.Add("trace.bin");
        round.Add(logs);
        round.Add(decrypt);
        round.Add(download);
        round.Add(wipe);
        Require(round.Started.HasValue && round.RouteOrder().SequenceEqual(new[] { logs, wipe, download, decrypt }),
            "route groups jobs by server and puts Downloads last");
        Require(round.AccessFor("RELAY-7") == JobAccess.Acquisition && round.Servers().Count() == 2, "known access is suggested");

        using (var guide = new JobGuideWindow(UiLanguage.English, round))
        {
            var grid = Field<DataGridView>(guide, "grid");
            var catalog = Field<DataGridView>(guide, "catalog");
            Require(guide.Text == "Job Guide · This round" && catalog.Rows.Count == 9 &&
                ((string)catalog.Rows[0].Cells[1].Value).Contains("ICE WALL BREAK Finder") &&
                (string)catalog.Rows[5].Cells[2].Value == "1 · 0 done", "job type catalog with round counts");
            Require(JobCatalog.Types.All(jobType => !jobType.Color.IsEmpty) &&
                JobCatalog.Types.Where(jobType => jobType.Removes).All(jobType => jobType.Color == Palette.Danger) &&
                JobCatalog.Find("Log Download").Color == Palette.Success && JobCatalog.Find("File Download").Color == Palette.Success &&
                JobCatalog.Types.Select(jobType => jobType.Color).Distinct().Count() == 6 &&
                catalog.Rows.Cast<DataGridViewRow>().All(row => row.Cells[0].Style.ForeColor == JobCatalog.Types[row.Index].Color),
                "each job family has a color in the catalog");
            Require(grid.Rows.Count == 4 && grid.Rows[1].Tag == wipe && (string)grid.Rows[3].Cells[JobGuideWindow.ColServer].Value == "Downloads" &&
                guide.IsGroupStart(0) && !guide.IsGroupStart(1) && guide.IsGroupStart(2) && guide.IsGroupStart(3), "grid shows the route grouped by server");

            guide.SelectType(2);
            Field<TextBox>(guide, "serverBox").Text = "Vault";
            Field<Segmented>(guide, "accessBox").SelectedIndex = (int)JobAccess.Grant;
            Field<TextBox>(guide, "targetsBox").Text = "Add 172.16.0.9 and 172.16.0.10 to Transit Access";
            Require(guide.AddJob() && round.Entries.Count == 5 && download.Access == JobAccess.Unknown &&
                round.Entries[4].Targets.SequenceEqual(new[] { "172.16.0.9", "172.16.0.10" }) &&
                grid.Rows[3].Tag == round.Entries[4], "a new job at the same server keeps independent access");
            Field<TextBox>(guide, "serverBox").Text = "relay-7";
            Require(Field<Segmented>(guide, "accessBox").SelectedIndex == (int)JobAccess.Acquisition, "known server fills in access");
            guide.SelectType(3);
            Field<TextBox>(guide, "serverBox").Text = "";
            Require(!guide.AddJob() && round.Entries.Count == 5, "server is required for server jobs");

            grid.Rows[0].Cells[JobGuideWindow.ColDone].Value = true;
            grid.Rows[2].Cells[JobGuideWindow.ColTargets].Value = "ledger.dat, ledger_old.dat";
            Application.DoEvents();
            Require(logs.Done && round.DoneCount == 1 && download.Targets.Count == 2 &&
                grid.Rows.Cast<DataGridViewRow>().First(r => r.Tag == logs).DefaultCellStyle.Font.Strikeout, "inline edits update the round");

            string summary = guide.RoundSummary();
            string n = Environment.NewLine;
            Require(summary.EndsWith("Relay-7" + n + "  [x] Log Deletion [Acquisition]: auth.log" + n + "  [ ] File Elimination: trace.bin" + n + n +
                "Vault" + n + "  [ ] File Download: ledger.dat, ledger_old.dat" + n + "  [ ] IP (adding) [Grant]: 172.16.0.9, 172.16.0.10" + n + n +
                "Downloads" + n + "  [ ] File Decryption") && summary.StartsWith("Round ") && summary.Contains("1/5 done"), "round summary by server");
            SaveWindow(guide, Path.Combine(output, "ui-job-guide-preview.png"));
            guide.SetCompact(true);
            Require(guide.TopMost && !grid.Columns[JobGuideWindow.ColNote].Visible && guide.ClientSize.Width < 600 &&
                Field<Button>(guide, "compactButton").Text == "Expand", "compact checklist");
            // Mono's DrawToBitmap renders hidden zero-height children one by one and fails; Windows paints the window in one pass.
            if (Type.GetType("Mono.Runtime") == null) SaveWindow(guide, Path.Combine(output, "ui-job-compact-preview.png"));
            guide.SetCompact(false);
            Require(grid.Columns[JobGuideWindow.ColNote].Visible && guide.ClientSize.Width > 800, "expand restores the full view");
            guide.Size = guide.MinimumSize;
            DataGridViewRow tracked = grid.Rows.Cast<DataGridViewRow>().First(r => r.Tag == download);
            int shortRow = tracked.Height;
            string longNote = string.Join(" ", Enumerable.Repeat("A long note should wrap at the right edge.", 5));
            tracked.Cells[JobGuideWindow.ColNote].Value = longNote;
            Application.DoEvents();
            tracked = grid.Rows.Cast<DataGridViewRow>().First(r => r.Tag == download);
            Require(tracked.Height > shortRow && tracked.Cells[JobGuideWindow.ColNote].ToolTipText == longNote,
                "long task text grows the row at the minimum window width");
            guide.SetLanguage(UiLanguage.Chinese);
            Require(guide.Text == "工作手册 · 本轮任务" && grid.Columns[JobGuideWindow.ColServer].HeaderText == "服务器" &&
                (string)catalog.Rows[5].Cells[2].Value == "1 项 · 完成 0", "live language switch");
        }

        using (var reopened = new JobGuideWindow(UiLanguage.Chinese, round))
        {
            var grid = Field<DataGridView>(reopened, "grid");
            Require(grid.Rows.Count == 5 && reopened.RoundSummary().StartsWith("本轮 ") && reopened.RoundSummary().Contains("已完成 1/5"),
                "same round after reopening in Chinese");
            reopened.ClearRound();
            Application.DoEvents();
            Require(round.Entries.Count == 0 && round.Started == null && grid.Rows.Count == 0 && reopened.RoundSummary() == "",
                "clear round removes every job");
        }
        Console.WriteLine("Job tracker parsing, server route, inline edits, compact view, summary, and both languages passed.");
    }

    private static void VerifyJobGuideList()
    {
        var round = new JobRound();
        var copiedIps = new List<string>();
        JobType type = JobCatalog.Find("IP Cleanup");
        for (int i = 0; i < 32; i++)
            round.Add(new JobEntry
            {
                Type = type,
                Server = "Relay-7",
                Targets = new List<string> { "192.0.2.17", "192.0.2.23", "192.0.2.45" },
                Note = "Job " + (i + 1)
            });

        using (var guide = new JobGuideWindow(UiLanguage.English, round, writeClipboard: copiedIps.Add))
        {
            guide.ShowInTaskbar = false;
            guide.Location = new Point(-guide.Width - 20, 0);
            guide.Show();
            Application.DoEvents();

            var grid = Field<DataGridView>(guide, "grid");
            const int rowIndex = 21;
            grid.CurrentCell = grid.Rows[rowIndex].Cells[JobGuideWindow.ColDone];
            grid.FirstDisplayedScrollingRowIndex = 20;
            Require(grid.Rows[rowIndex].Displayed, "the job being checked is visible after scrolling");

            JobEntry job = round.Entries[rowIndex];
            var cell = new Rectangle(0, 0, grid.Columns[JobGuideWindow.ColTargets].Width, grid.Rows[rowIndex].Height);
            int step = Field<Font>(guide, "monoFont").Height + 3;
            int firstLine = (cell.Height - job.Targets.Count * step) / 2 + step / 2;
            for (int i = 0; i < job.Targets.Count; i++)
                Require(guide.IpAtCopyButton(job, cell, new Point(16, firstLine + i * step)) == job.Targets[i],
                    "the copy icon selects only its own IP");
            Require(guide.IpAtCopyButton(job, cell, new Point(40, firstLine)) == null,
                "clicking the IP text stays available for selection and editing");

            Rectangle target = grid.GetCellDisplayRectangle(JobGuideWindow.ColTargets, rowIndex, false);
            var firstCopy = new Point(target.Left + 16, target.Top + firstLine);
            var secondCopy = new Point(firstCopy.X, firstCopy.Y + step);
            var secondIp = new Point(target.Left + 46, secondCopy.Y);
            MouseClick(grid, firstCopy, false);
            MouseClick(grid, secondCopy, true);
            Require(copiedIps.SequenceEqual(job.Targets.Take(2)) && !grid.IsCurrentCellInEditMode && !grid.BeginEdit(true),
                "rapid copying across two IPs uses both copy buttons without opening an editor");
            MouseClick(grid, secondIp, true);
            Require(!grid.IsCurrentCellInEditMode, "a click on the icon followed by IP text cannot start editing");
            MouseClick(grid, secondIp, false);
            Require(!grid.IsCurrentCellInEditMode, "a single click on selected IP text does not start editing");
            MouseClick(grid, secondIp, true);
            Require(grid.IsCurrentCellInEditMode, "a double click on the same IP text starts editing");
            grid.CancelEdit();

            MethodInfo click = typeof(DataGridView).GetMethod("OnCellMouseClick", BindingFlags.Instance | BindingFlags.NonPublic);
            var mouse = new DataGridViewCellMouseEventArgs(JobGuideWindow.ColDone, rowIndex, 19, 20,
                new MouseEventArgs(MouseButtons.Left, 1, 19, 20, 0));
            click.Invoke(grid, new object[] { mouse });
            Application.DoEvents();
            Require(job.Done && round.DoneCount == 1 && grid.FirstDisplayedScrollingRowIndex == 20 &&
                grid.CurrentRow.Tag == job && grid.Rows[rowIndex].Displayed,
                "checking a scrolled job preserves the visible position and selection");

            click.Invoke(grid, new object[] { mouse });
            Application.DoEvents();
            Require(!job.Done && round.DoneCount == 0 && grid.FirstDisplayedScrollingRowIndex == 20 && grid.CurrentRow.Tag == job,
                "unchecking a scrolled job also preserves the visible position and selection");

            guide.SelectType(Array.IndexOf(JobCatalog.Types, type));
            Field<TextBox>(guide, "serverBox").Text = "Relay-8";
            var targets = Field<TextBox>(guide, "targetsBox");
            targets.Focus();
            targets.Text = "192.0.2.60";
            targets.SelectionStart = targets.TextLength;
            var enter = Message.Create(targets.Handle, 0x0100, (IntPtr)Keys.Enter, IntPtr.Zero);
            Require(targets.Multiline && targets.AcceptsReturn && !targets.PreProcessMessage(ref enter) && round.Entries.Count == 32,
                "Enter in the IP input reaches the multiline editor without submitting a job");
            SendMessage(targets.Handle, 0x0102, (IntPtr)'\r', IntPtr.Zero);
            targets.AppendText("192.0.2.61");
            Require(targets.Lines.SequenceEqual(new[] { "192.0.2.60", "192.0.2.61" }),
                "pressing Enter lets the user type the next IP on its own line");
            Field<Button>(guide, "addButton").PerformClick();
            Require(round.Entries.Count == 33 && round.Entries[32].Targets.SequenceEqual(new[] { "192.0.2.60", "192.0.2.61" }),
                "adding a manually entered multiline task records both IPs");
        }
        Console.WriteLine("Individual IP copy targets and scrolling while checking jobs passed.");
    }

    private static void MouseClick(Control control, Point point, bool doubleClick)
    {
        const int leftDown = 0x0201, leftDoubleClick = 0x0203, leftUp = 0x0202;
        var position = (IntPtr)((point.Y << 16) | (point.X & 0xffff));
        SendMessage(control.Handle, doubleClick ? leftDoubleClick : leftDown, (IntPtr)1, position);
        SendMessage(control.Handle, leftUp, IntPtr.Zero, position);
    }

    private static void VerifyTerminalPuzzle(string output)
    {
        var example = new TerminalPuzzle();
        Require(example.Remaining.Count == 81 && example.Record(0, 3) && example.Record(13, 1) &&
            example.Remaining.Select(candidate => TerminalPuzzle.Format(candidate)).SequenceEqual(new[]
            {
                "v1.0  PUT  Fiber  DES", "v1.0  PUT  Sat  RSA", "v1.0  POST  Fiber  RSA"
            }), "the two observed terminal trials leave exactly three combinations");
        Require(!example.Record(13, 3) && example.Trials.Count == 2 && example.Remaining.Count == 3,
            "contradictory feedback preserves the terminal history and possibilities");
        int maxTrials = 0;
        for (int secret = 0; secret < TerminalPuzzle.CombinationCount; secret++)
        {
            var puzzle = new TerminalPuzzle();
            for (int attempt = 0; attempt < 6 && !puzzle.Solved; attempt++)
            {
                int guess = puzzle.Recommend();
                int different = Enumerable.Range(0, TerminalPuzzle.FieldCount)
                    .Count(field => TerminalPuzzle.OptionAt(secret, field) != TerminalPuzzle.OptionAt(guess, field));
                Require(puzzle.Record(guess, different) && puzzle.Remaining.Contains(secret),
                    "correct game feedback always retains the hidden combination");
            }
            Require(puzzle.Solved && puzzle.Remaining.Single() == secret, "the terminal strategy solves every combination");
            maxTrials = Math.Max(maxTrials, puzzle.Trials.Count);
        }
        Require(maxTrials == 5, "the recommended strategy solves all combinations within five attempts");

        using (var window = new TerminalPuzzleWindow(UiLanguage.English, example))
        {
            window.ShowInTaskbar = false;
            window.Location = new Point(-window.Width - 20, 0);
            window.Show();
            window.Size = window.MinimumSize;
            Application.DoEvents();
            var mismatch = Field<Segmented>(window, "mismatch");
            var record = Field<FlatButton>(window, "record");
            var reset = Field<FlatButton>(window, "reset");
            var onTop = Field<FlatButton>(window, "onTop");
            Require(window.TopMost && onTop.Checked && onTop.Text == "Keep on top" &&
                onTop.Parent.ClientRectangle.Contains(onTop.Bounds), "Terminal opens on top with a visible checked button");
            onTop.PerformClick();
            Require(!window.TopMost && !onTop.Checked, "the Terminal button disables always-on-top");
            Require(reset.Parent.ClientRectangle.Contains(reset.Bounds), "English terminal controls fit at the minimum width");
            Field<FlatButton>(window, "undo").PerformClick();
            Require(example.Trials.Count == 1 && mismatch.SelectedIndex == 1 && record.Enabled, "undo refills the removed trial for correction");
            record.PerformClick();
            Require(example.Trials.Count == 2 && Field<ListBox>(window, "candidates").Items.Count == 3 && !record.Enabled,
                "record refreshes the recommendation and the remaining combinations");
            window.SetLanguage(UiLanguage.Chinese);
            Application.DoEvents();
            SaveWindow(window, Path.Combine(output, "ui-terminal-preview.png"));
            Require(window.Text == "终端入侵助手" && example.Trials.Count == 2, "language switching preserves the terminal round");
            Require(onTop.Text == "置顶" && !window.TopMost && !onTop.Checked, "language switching preserves the Terminal pin state");
            onTop.PerformClick();
            Require(window.TopMost && onTop.Checked, "the Terminal button restores always-on-top");
            Require(reset.Parent.ClientRectangle.Contains(reset.Bounds),
                "translated terminal controls fit (" + reset.Bounds + " inside " + reset.Parent.ClientRectangle + ")");
            reset.PerformClick();
            Require(example.Trials.Count == 0 && example.Remaining.Count == 81, "new round starts with a blank terminal history");
        }
        Console.WriteLine("Terminal recommendations solved all 81 combinations in at most {0} attempts.", maxTrials);
    }

    private static void VerifyCardImport()
    {
        var round = new JobRound();
        var info = new List<string> { "RM7-EILS", "Access Grant", "PCF", "109" };
        Func<Bitmap, List<string>> recognize = image => image.Width == 900
            ? new List<string> { "IP Cleanup", "Time Left" }
            : image.Width == 300 ? info : new List<string> { "172.20.35.11", "100.64.2.118" };
        using (var guide = new JobGuideWindow(UiLanguage.English, round, (chinese, english) => Task.FromResult(new Bitmap(1200, 420)), recognize))
        {
            TextBox draft = Field<TextBox>(guide, "targetsBox");
            draft.Text = "unfinished draft";
            MethodInfo import = typeof(JobGuideWindow).GetMethod("ImportJob", BindingFlags.Instance | BindingFlags.NonPublic);
            Finish((Task)import.Invoke(guide, null));
            JobEntry entry = round.Entries.Single();
            Require(entry.Type.Name == "IP Cleanup" && entry.Server == "RM7-E1L5" && entry.Access == JobAccess.Grant &&
                entry.Targets.SequenceEqual(new[] { "172.20.35.11", "100.64.2.118" }) && draft.Text == "unfinished draft",
                "full card import corrects server OCR and preserves the current draft");
            entry.Done = true;
            Finish((Task)import.Invoke(guide, null));
            Require(round.Entries.Count == 1 && entry.Done, "reimport of the same card preserves completion");
            info[1] = "Access Acquisition";
            Finish((Task)import.Invoke(guide, null));
            Require(round.Entries.Count == 2 && entry.Access == JobAccess.Grant &&
                round.Entries[1].Access == JobAccess.Acquisition && round.AccessFor(entry.Server) == JobAccess.Unknown,
                "identical server, type and targets with different access are separate jobs");
            info.Add("RM7-N2L3");
            Finish((Task)import.Invoke(guide, null));
            Require(round.Entries.Count == 2 && Field<Label>(guide, "status").Text.Contains("Could not read"),
                "ambiguous server is not added");
            guide.SelectType(1);
            Field<TextBox>(guide, "serverBox").Text = entry.Server;
            Field<Segmented>(guide, "accessBox").SelectedIndex = (int)entry.Access;
            Field<TextBox>(guide, "targetsBox").Text = string.Join(Environment.NewLine, entry.Targets.AsEnumerable().Reverse());
            Require(!guide.AddJob() && round.Entries.Count == 2, "manual entry follows the same duplicate rule");
            Field<TextBox>(guide, "noteBox").Text = "Separate note about this job";
            Require(guide.AddJob() && round.Entries.Count == 3, "a different note distinguishes the task");
        }
        JobEntry file = JobRound.ReadCard(JobCatalog.Find("File Download"), new[] { "服务器：SRM7-N4L2", "Access Acquisition" },
            new[] { "Download requested files", "trace-01.bin", "log.tar.gz" });
        Require(file != null && file.Server == "SRM7-N4L2" && file.Access == JobAccess.Acquisition &&
            file.Targets.SequenceEqual(new[] { "trace-01.bin", "log.tar.gz" }), "file card with a Chinese server label");
        using (var croppedDownload = new Bitmap(1127, 424))
        {
            using (Graphics graphics = Graphics.FromImage(croppedDownload))
            {
                graphics.Clear(Color.FromArgb(18, 25, 34));
                using (var divider = new Pen(Color.FromArgb(36, 45, 53)))
                    graphics.DrawLine(divider, 601, 164, 601, 415);
            }
            Func<Bitmap, List<string>> readDownload = image =>
            {
                if (image.Width == 845) return new List<string> { "Data Download" };
                if (image.Width == 320) return new List<string> { "RM7-N4L2", "Access Acquisition", "EXAMPLE-S01IS CORP" };
                if (image.Width == 526) return new List<string> { "sample_1001.bin" };
                return new List<string> { "RM7-N4", "Access Acquisiti" };
            };
            JobEntry download = JobGuideWindow.RecognizeCard(croppedDownload, readDownload);
            Require(download != null && download.Type.Name == "File Download" && download.Server == "RM7-N4L2" &&
                download.Access == JobAccess.Acquisition && download.Targets.SequenceEqual(new[] { "sample_1001.bin" }),
                "a right-cropped Data Download card uses the visible column divider");
        }
        Require(JobRound.ReadCard(JobCatalog.Find("IP Cleanup"), new[] { "Access Grant" }, new[] { "172.20.35.11" }) == null,
            "a card without a server is not added");
        using (var decryptCard = new Bitmap(1200, 420))
        {
            string title = "Decrypt & Extract";
            Func<Bitmap, List<string>> readDecrypt = image => image.Width == 900
                ? new List<string> { title }
                : image.Width == 300 ? new List<string> { "RM7-S4L3", "Access Acquisition", "N/A", "712" }
                : new List<string> { "Decrypt the downloaded file", "data_4176.png" };
            foreach (string heading in new[] { "Decrypt & Extract", "Decrypt 8 Extract" })
            {
                title = heading;
                JobEntry decrypt = JobGuideWindow.RecognizeCard(decryptCard, readDecrypt);
                Require(decrypt != null && decrypt.Type.Name == "Download And Decrypt" && decrypt.Server == "RM7-S4L3" &&
                    decrypt.Access == JobAccess.Acquisition && decrypt.Targets.SequenceEqual(new[] { "data_4176.png" }),
                    "Decrypt & Extract card still imports when OCR reads the ampersand as 8");
            }
        }
        using (var decryptionCard = new Bitmap(1200, 448))
        {
            var lines = new List<OcrTextLine>
            {
                new OcrTextLine("File Decryption", new RectangleF(143, 45, 192, 26)),
                new OcrTextLine("File Decryption", new RectangleF(458, 269, 129, 18))
            };
            Func<Bitmap, List<string>> readDecryption = image => image.Width == 900
                ? new List<string> { "File Decryption" }
                : image.Width == 300 ? new List<string> { "N/A", "File Decryption", "N/A" }
                : new List<string> { "archive_1001.a9b2c" };
            JobEntry entry = JobGuideWindow.RecognizeCards(decryptionCard, readDecryption, image => lines).Single();
            Require(entry != null && entry.Type.Name == "File Decryption" && entry.Server == "" &&
                entry.Access == JobAccess.Unknown && entry.Targets.SequenceEqual(new[] { "archive_1001.a9b2c" }),
                "a repeated condition value does not split a File Decryption card");
        }
        using (var logCard = new Bitmap(1200, 420))
        {
            Func<Bitmap, List<string>> readLog = image =>
            {
                if (image.Width == 900) return new List<string> { "Log Download" };
                if (image.Width == 300) return new List<string> { "RM7-EIL3", "Access Grant", "StarProm Industries", "129" };
                if (image.Height > 140) return new List<string> { "Description", "Download the target log", "Session expired for user root", "Info" };
                return new List<string> { "Session expired for user root", "Session expired for user root", "audit.log" };
            };
            JobEntry log = JobGuideWindow.RecognizeCard(logCard, readLog);
            Require(log != null && log.Type.Name == "Log Download" && log.Server == "RM7-E1L3" &&
                log.Access == JobAccess.Grant && log.Targets.SequenceEqual(new[] { "Session expired for user root", "audit.log" }),
                "log import reads complete titles from the target area without instructions");
        }
        VerifyBatchCardImport();
    }

    private static void VerifyBatchCardImport()
    {
        Func<string, string, Task<Bitmap>> pick = (chinese, english) =>
        {
            var image = new Bitmap(1200, 1260);
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.FillRectangle(Brushes.Red, 0, 0, 1200, 420);
                graphics.FillRectangle(Brushes.Lime, 0, 420, 1200, 420);
                graphics.FillRectangle(Brushes.Blue, 0, 840, 1200, 420);
            }
            return Task.FromResult(image);
        };
        Func<Bitmap, List<OcrTextLine>> layout = image => new List<OcrTextLine>
        {
            new OcrTextLine("File Download", new RectangleF(35, 870, 260, 22)),
            new OcrTextLine("File Upload", new RectangleF(690, 540, 110, 18)), // Target text, not a card title.
            new OcrTextLine("IP Cleanup", new RectangleF(35, 30, 210, 22)),
            new OcrTextLine("IP Cleanup", new RectangleF(35, 450, 260, 22))
        };
        Func<Bitmap, List<string>> recognize = image =>
        {
            Color pixel = image.GetPixel(0, 0);
            if (image.Width == 300)
                return pixel.R == 255 ? new List<string> { "RM7-N4L2", "Access Grant" }
                    : pixel.G == 255 ? new List<string> { "RM7-S4L3", "Access Acquisition" }
                    : new List<string> { "Access Grant" };
            return pixel.R == 255 ? new List<string> { "172.20.35.11" }
                : pixel.G == 255 ? new List<string> { "100.64.2.118" } : new List<string> { "incomplete.bin" };
        };
        var round = new JobRound();
        using (var guide = new JobGuideWindow(UiLanguage.English, round, pick, recognize, layout))
        {
            MethodInfo import = typeof(JobGuideWindow).GetMethod("ImportJob", BindingFlags.Instance | BindingFlags.NonPublic);
            Finish((Task)import.Invoke(guide, null));
            string status = Field<Label>(guide, "status").Text;
            Require(round.Entries.Count == 2 && status.Contains("2, skipped 0 duplicates and 1 incomplete") &&
                round.Entries[0].Server == "RM7-N4L2" && round.Entries[0].Access == JobAccess.Grant &&
                round.Entries[0].Targets.SequenceEqual(new[] { "172.20.35.11" }) &&
                round.Entries[1].Server == "RM7-S4L3" && round.Entries[1].Access == JobAccess.Acquisition &&
                round.Entries[1].Targets.SequenceEqual(new[] { "100.64.2.118" }),
                "stacked cards of the same type retain their own targets and access");
            round.Entries[0].Done = true;
            Finish((Task)import.Invoke(guide, null));
            Require(round.Entries.Count == 2 && round.Entries[0].Done &&
                Field<Label>(guide, "status").Text.Contains("0, skipped 2 duplicates and 1 incomplete"),
                "batch reimport preserves completed jobs and reports skipped cards");
        }
    }

    private static void VerifyToolWindows(LanguagePreference preference)
    {
        Func<Rectangle, Bitmap> read = null;
        using (var finder = new FinderWindow(area => read(area), () => SystemInformation.VirtualScreen, preference))
        {
            finder.ShowInTaskbar = false;
            finder.Opacity = 0;
            finder.Show();
            var type = typeof(FinderWindow);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            type.GetMethod("ShowJobs", flags).Invoke(finder, null);
            type.GetMethod("ShowDaily", flags).Invoke(finder, null);
            var terminalButton = Field<FlatButton>(finder, "terminalButton");
            terminalButton.PerformClick();
            TerminalPuzzleWindow terminal = Field<TerminalPuzzleWindow>(finder, "terminalWindow");
            Require(terminalButton.Kind == ButtonKind.Primary && terminal != null && terminal.Owner == null && terminal.ShowInTaskbar,
                "Terminal opens from the blue Finder toolbar button");
            finder.Size = finder.MinimumSize;
            var languageButton = Field<FlatButton>(finder, "languageButton");
            foreach (UiLanguage language in new[] { UiLanguage.Chinese, UiLanguage.English })
            {
                type.GetMethod("ChooseLanguage", flags).Invoke(finder, new object[] { language });
                Application.DoEvents();
                Require(terminalButton.Right <= languageButton.Left && terminalButton.Parent.ClientRectangle.Contains(terminalButton.Bounds),
                    "the Terminal button fits the Finder toolbar in " + language + " (" + terminalButton.Bounds +
                    " beside " + languageButton.Bounds + " within " + terminalButton.Parent.ClientRectangle + ")");
            }
            JobGuideWindow guide = Field<JobGuideWindow>(finder, "jobsWindow");
            DailyTaskWindow daily = Field<DailyTaskWindow>(finder, "dailyWindow");
            Require(guide.Owner == null && daily.Owner == null && guide.ShowInTaskbar && daily.ShowInTaskbar,
                "tools have independent taskbar windows");
            finder.WindowState = FormWindowState.Minimized;
            Application.DoEvents();
            Require(guide.Visible && daily.Visible && terminal.Visible && guide.WindowState == FormWindowState.Normal &&
                daily.WindowState == FormWindowState.Normal && terminal.WindowState == FormWindowState.Normal, "minimizing Finder leaves its tools open");
            guide.WindowState = FormWindowState.Minimized;
            Require(daily.WindowState == FormWindowState.Normal, "minimizing Job Guide leaves Daily Tasks open");
            finder.WindowState = guide.WindowState = FormWindowState.Normal;
            guide.Size = guide.MinimumSize;
            var import = Field<FlatButton>(guide, "importJob");
            var add = Field<FlatButton>(guide, "addButton");
            var clear = Field<FlatButton>(guide, "clear");
            Require(import.Text == "Import from screenshot" && import.Kind == ButtonKind.Primary &&
                add.Kind == ButtonKind.Normal && import.Parent.ClientRectangle.Contains(clear.Bounds),
                "screenshot import is the primary button and the longer English footer fits the minimum width");
            Field<Segmented>(guide, "viewSwitch").SelectedIndex = 1;
            var notes = Field<TextBox>(guide, "generalNote");
            Require(notes.Visible && notes.ReadOnly && notes.WordWrap && notes.ScrollBars == ScrollBars.Vertical,
                "job type instructions have their own vertical scrollbar");
            foreach (UiLanguage language in new[] { UiLanguage.Chinese, UiLanguage.English })
            {
                guide.SetLanguage(language);
                Application.DoEvents();
                notes.Focus();
                notes.Select(notes.TextLength, 0);
                notes.ScrollToCaret();
                const int getFirstVisibleLine = 0x00CE;
                Require(SendMessage(notes.Handle, getFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32() > 0,
                    "the user can scroll down to the remaining job type instructions");
            }
            bool repainted = false;
            finder.BeginInvoke(new Action(() => repainted = true));
            read = area =>
            {
                Require(!finder.Visible && !guide.Visible && !daily.Visible && !terminal.Visible, "all overlapping tools are hidden before capture");
                Require(repainted, "the UI processes queued repaint work before capture");
                return new Bitmap(10, 10);
            };
            Rectangle covered = Rectangle.Union(Rectangle.Union(Rectangle.Union(finder.Bounds, guide.Bounds), daily.Bounds), terminal.Bounds);
            var pending = (Task<Bitmap>)type.GetMethod("CaptureToolArea", flags).Invoke(finder, new object[] { covered });
            Require(!pending.IsCompleted && !finder.Visible, "capture yields after hiding the window");
            Finish(pending);
            using (Bitmap image = pending.GetAwaiter().GetResult()) Require(image != null, "capture returns the new frame");
            Require(finder.Visible && guide.Visible && daily.Visible && terminal.Visible, "tools are restored after capture");
            finder.Close();
            Require(guide.IsDisposed && daily.IsDisposed && terminal.IsDisposed, "closing Finder closes its tools");
        }
    }

    private static void Finish(Task task)
    {
        var clock = Stopwatch.StartNew();
        while (!task.IsCompleted && clock.ElapsedMilliseconds < 5000)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(1);
        }
        Require(task.IsCompleted, "asynchronous test timed out");
        task.GetAwaiter().GetResult();
    }

    private sealed class ResizeProbe : Form
    {
        public void EndDrag() { OnResizeEnd(EventArgs.Empty); }
    }

    private static void VerifyWindowSize(string output)
    {
        var preference = new WindowSizePreference(Path.Combine(output, Guid.NewGuid().ToString("N")));
        bool normalMode = true;
        using (var first = new ResizeProbe())
        {
            preference.Bind(first, "jobs", () => normalMode);
            first.ClientSize = new Size(900, 600);
            first.EndDrag();
            normalMode = false;
            first.ClientSize = new Size(540, 320);
            first.EndDrag();
        }
        using (var later = new ResizeProbe())
        {
            preference.Bind(later, "jobs");
            Require(later.ClientSize == new Size(900, 600) && preference.Load("daily").IsEmpty, "window size is restored per window");
            later.WindowState = FormWindowState.Minimized;
            later.EndDrag();
            Require(preference.Load("jobs") == new Size(900, 600), "minimized and compact sizes are ignored");
        }
    }

    // Draws a pulse row the way the game does: short = hollow cell, long = three filled cells with hairline gaps,
    // thin bars at both ends, a title above, and a panel border around it, so a loose selection is also covered.
    private static Bitmap DrawSignal(string pulses, int scale)
    {
        return DrawSignal(new[] { pulses }, scale);
    }

    private static Bitmap DrawSignal(string[] rows, int scale)
    {
        int cell = 7 * scale, height = 11 * scale, gap = 7 * scale;
        int width = 60 * scale + rows.Max(row => row.Sum(p => p == 'S' ? cell + gap : 3 * cell + 2 * scale + gap));
        int panelHeight = (90 + 18 * (rows.Length - 1)) * scale;
        var image = new Bitmap(width, panelHeight);
        Color pulse = Color.FromArgb(125, 196, 211);
        using (Graphics g = Graphics.FromImage(image))
        using (var fill = new SolidBrush(pulse))
        using (var pen = new Pen(rows.Length > 1 ? Color.FromArgb(74, 108, 117) : pulse, scale))
        using (var border = new Pen(Color.FromArgb(70, 90, 100), scale))
        using (var font = new Font(FontFamily.GenericMonospace, 7 * scale, FontStyle.Bold))
        {
            g.Clear(Color.FromArgb(14, 18, 22));
            g.DrawRectangle(border, 2 * scale, 2 * scale, width - 5 * scale, panelHeight - 5 * scale);
            g.DrawString("PULSE SIGNAL", font, fill, 10 * scale, 8 * scale);
            for (int row = 0; row < rows.Length; row++)
            {
                int top = (50 + row * 18) * scale, x = 20 * scale;
                if (row == 0)
                {
                    g.FillRectangle(fill, x, top, scale, height);
                    x += 10 * scale;
                }
                foreach (char p in rows[row])
                {
                    if (p == 'S')
                    {
                        g.DrawRectangle(pen, x + scale / 2f, top + scale / 2f, cell - scale, height - scale);
                        x += cell + gap;
                    }
                    else
                    {
                        for (int i = 0; i < 3; i++) g.FillRectangle(fill, x + i * (cell + scale), top, cell, height);
                        x += 3 * cell + 2 * scale + gap;
                    }
                }
                if (row == rows.Length - 1) g.FillRectangle(fill, x + 3 * scale, top, scale, height);
            }
            if (rows.Length > 1)
                g.FillRectangle(fill, 10 * scale, panelHeight - 20 * scale, width - 20 * scale, 13 * scale);
        }
        return image;
    }

    private static void VerifyDailyTasks(string output)
    {
        Require(SignalDecoder.Normalize("S.0□ l-1█ x") == "SSSSLLLL", "pulse characters are normalized");
        string morse = "SSSSSSSSSLSSSLLSLLLLLLLSS";
        SignalResult result = SignalDecoder.Solve(morse, SignalMode.Auto);
        Require(result.Mode == SignalMode.Morse && result.Code == "54318" && result.Complete, "the guide's 25-pulse Morse example");
        string allMorse = string.Concat(Enumerable.Range(0, 10).Select(d => SignalDecoder.Morse[d]));
        string allBinary = string.Concat(Enumerable.Range(0, 10).Select(d => SignalDecoder.Binary[(d + 1) % 10]));
        Require(SignalDecoder.Solve(allMorse, SignalMode.Auto).Code == "0123456789", "50 pulses decode as ten Morse digits");
        result = SignalDecoder.Solve(allBinary, SignalMode.Auto);
        Require(result.Mode == SignalMode.Binary && result.Code == "1234567890" && result.OtherCode.Length == 0, "40 pulses that only work as binary");
        result = SignalDecoder.Solve(new string('S', 20), SignalMode.Auto);
        Require(result.Mode == SignalMode.Binary && result.Code == "00000" && result.OtherMode == SignalMode.Morse && result.OtherCode == "5555",
            "20 pulses valid both ways default to binary and show the Morse reading");
        result = SignalDecoder.Solve(new string('L', 20), SignalMode.Auto);
        Require(result.Mode == SignalMode.Morse && result.Code == "0000" && result.OtherCode.Length == 0, "20 pulses that only decode as Morse");
        Require(SignalDecoder.Solve(new string('S', 23), SignalMode.Auto).GroupSize == 0, "23 pulses fit neither encoding");
        result = SignalDecoder.Solve("SLSLS" + "SSSSS" + "SSSSS", SignalMode.Auto);
        Require(result.Code == "?55" && !result.Complete, "a group that is not a digit is flagged");
        Require(SignalDecoder.Solve(new string('S', 20), SignalMode.Morse).Code == "5555", "manual Morse mode");
        Require(!SignalDecoder.Solve("SSSSS", SignalMode.Binary).Complete, "manual mode rejects an incomplete final group");

        foreach (string pulses in new[] { morse, allBinary, allMorse, new string('L', 15), new string('S', 12) })
            foreach (int scale in new[] { 1, 2 })
                using (Bitmap image = DrawSignal(pulses, scale))
                    Require(SignalDecoder.ReadPulses(image) == pulses, "screen reader at scale " + scale + " for " + pulses.Length + " pulses");

        string[] wrapped = { "LSSSSSSSSLLSLSSLSSLS", "LSSLSSSSSLSSSLLLSLS", "L" };
        foreach (int scale in new[] { 1, 2 })
            using (Bitmap image = DrawSignal(wrapped, scale))
            {
                string pulses = SignalDecoder.ReadPulses(image);
                Require(pulses == string.Concat(wrapped), "all three signal rows read in order at scale " + scale);
                result = SignalDecoder.Solve(pulses, SignalMode.Auto);
                Require(result.Complete && result.Mode == SignalMode.Binary && result.Code == "8069290475", "wrapped signal code");
            }

        var lines = new[]
        {
            "[21:14:53] SCAN /sector=12 /status=OK",
            "☐ [09:13:59] SCAN /sector=06",
            "[06:27:29] PING /sector=200 /status=WARN",
            "[18:67:31] ROUTE-CHECK /sector=289 /status=OK",
            "[24:10:02] scan /sector=8 /status=OK",
            "[9:10:02] SCANNER /sector=0 /status=FAIL",
            "TEMP-SYNC /sector=256 /status=ERROR",
            "[07:30:00] RADIO-TEST /sectr=5 /status=OK",
            ""
        };
        List<LogCheck> checks = LogValidator.Check(string.Join(Environment.NewLine, lines));
        Func<int, LogField[]> fields = n => checks[n].Issues.Select(i => i.Field).ToArray();
        Require(checks.Count == 8 && checks[0].Ok && checks[2].Ok, "valid lines pass and blank lines are skipped");
        Require(fields(1).SequenceEqual(new[] { LogField.MissingStatus }) && checks[1].Line.StartsWith("[09"), "missing status, checkbox stripped");
        Require(fields(3).SequenceEqual(new[] { LogField.Time, LogField.Sector }) && checks[3].Issues[0].English.Contains("67"), "minute 67 and sector 289");
        Require(fields(4).SequenceEqual(new[] { LogField.Time, LogField.Type }) && checks[4].Issues[1].English.Contains("SCAN"), "hour 24 and lowercase type");
        Require(fields(5).SequenceEqual(new[] { LogField.Time, LogField.Type, LogField.Sector, LogField.Status }), "every field wrong");
        Require(fields(6).SequenceEqual(new[] { LogField.Time }), "missing time");
        Require(fields(7).SequenceEqual(new[] { LogField.Format, LogField.MissingSector }), "misspelled sector key");
        Require(LogValidator.CheckLine("[01:02:03] PING /sector= /status=").Issues.Select(i => i.Field)
            .SequenceEqual(new[] { LogField.Sector, LogField.Status }), "empty values are incorrect parameters");
        var gameOptions = new[] { LogField.Time, LogField.Type, LogField.MissingSector, LogField.MissingStatus, LogField.Sector, LogField.Status };
        Require(gameOptions.Select(LogValidator.FieldName).SequenceEqual(new[]
        {
            "Time format is incorrect", "Event type is incorrect", "Missing /sector parameter", "Missing /status parameter",
            "/sector parameter is incorrect", "/status parameter is incorrect"
        }), "all six error labels match the game");

        // OCR clean-up fixes misread characters but keeps the game's deliberate errors.
        var ocr = new[]
        {
            new[] { "O [21:14:53] SCAN Isector = 12 /status=0K", "[21:14:53] SCAN /sector=12 /status=OK" },
            new[] { "(O9:l3;59] SCAN /sect0r=O6", "[09:13:59] SCAN /sector=06" },
            new[] { "[18:67:31]ROUTE-CHECK /sector=289/status=OK", "[18:67:31] ROUTE-CHECK /sector=289 /status=OK" },
            new[] { "[02:44:10] P1NG | sector=3 /status=WARN", "[02:44:10] PING /sector=3 /status=WARN" },
            new[] { "[02:44:10] SCANER /sector=3 /status=0KAY", "[02:44:10] SCANER /sector=3 /status=OKAY" },
            new[] { "[9:10:02] scan /sector=0 /status=FAIL", "[9:10:02] scan /sector=0 /status=FAIL" },
        };
        foreach (string[] pair in ocr)
            Require(LogValidator.FixOcr(pair[0]) == pair[1], "OCR clean-up: " + pair[0] + " -> " + LogValidator.FixOcr(pair[0]));

        using (Bitmap text = DrawLogs(new[] { "[21:14:53] SCAN /sector=12 /status=OK" }))
        using (Bitmap prepared = WindowsOcr.Prepare(text))
            Require(prepared.Width > text.Width && prepared.GetPixel(2, 2).R == 255 && prepared.GetPixel(2, prepared.Height / 2).R == 255,
                "OCR input is enlarged, dark on white, with the scanline background removed");
        VerifyLogClockFallback();
        TryWindowsOcr();

        using (Bitmap screen = DrawSignal(morse, 2))
        {
            int picks = 0;
            int captures = 0;
            var area = new Rectangle(0, 0, screen.Width, screen.Height);
            var reads = new Queue<List<string>>(new[]
            {
                new List<string> { "PULSE", "AUTH /sector=126 /status=OK", "[08:42 SYNC /sector=81 /status=ERROR", "[12:34:56]",
                    "O [21:14:53] SCAN Isector = 12 /status=0K", "(O9:l3;59] SCAN /sect0r=O6" },
                new List<string> { "(O9:l3;59] SCAN /sect0r=O6", "[18:67:31] ROUTE-CHECK /sector=289 /status=OK",
                    "[18-07-31] PING /sector=12 /status=OK" },
                new List<string> { "[18:67:31 ROUTE-CHECK /sector=289 /status=OK", "SYNC /sector=81 /status=ERROR" }
            });
            Func<Bitmap, List<string>> recognize = image =>
            {
                if (reads.Count == 0) throw new OcrUnavailableException("No text recognition language is installed.");
                return reads.Dequeue();
            };
            using (var daily = new DailyTaskWindow(UiLanguage.English,
                r => { captures++; return Task.FromResult(screen.Clone(r, PixelFormat.Format32bppArgb)); },
                (zh, en) => { picks++; return Task.FromResult(new ScreenSelection(area, screen.Clone(area, PixelFormat.Format32bppArgb))); },
                recognize))
            {
                MethodInfo read = typeof(DailyTaskWindow).GetMethod("ReadSignal", BindingFlags.NonPublic | BindingFlags.Instance);
                Finish((Task)read.Invoke(daily, new object[] { false }));
                Finish((Task)read.Invoke(daily, new object[] { false }));
                Require(picks == 1 && captures == 1 && daily.Signal.Code == "54318" && daily.Text == "Daily Tasks" &&
                    Field<Label>(daily, "codeLabel").Text == "54318", "read signal from the screen once, then reuse the area");
                Field<PulseStrip>(daily, "strip").PulseClicked(0);
                Require(daily.Pulses.StartsWith("LSSSS") && daily.Signal.Code == "64318", "clicking a pulse flips it");
                daily.SetPulses(new string('S', 20));
                Require(Field<Label>(daily, "otherLabel").Text.Contains("5555"), "both readings shown for 20 pulses");
                Field<Segmented>(daily, "pages").SelectedIndex = 1;
                MethodInfo readLogs = typeof(DailyTaskWindow).GetMethod("ReadLogs", BindingFlags.NonPublic | BindingFlags.Instance);
                Finish((Task)readLogs.Invoke(daily, new object[] { false }));
                Require(daily.Checks.Count == 2 && Field<Label>(daily, "status").Text.Contains("Skipped 4 incomplete fragments"),
                    "screenshot fragments with a missing or partial timestamp or no log text are skipped");
                Finish((Task)readLogs.Invoke(daily, new object[] { false }));
                Require(picks == 2 && daily.Checks.Count == 4 && daily.Checks[0].Ok && daily.Checks[1].Line == "[09:13:59] SCAN /sector=06" &&
                    daily.Checks[2].Issues.Select(i => i.Field).SequenceEqual(new[] { LogField.Time, LogField.Sector }) &&
                    daily.Checks[3].Issues.Select(i => i.Field).SequenceEqual(new[] { LogField.Time }),
                    "fully read timestamps are imported even when the time or its separators violate the format");
                Finish((Task)readLogs.Invoke(daily, new object[] { false }));
                Require(daily.Checks.Count == 4 && Field<Label>(daily, "status").Text.Contains("No complete logs found"),
                    "a capture of only fragments keeps the existing logs and explains how to reselect");
                Finish((Task)readLogs.Invoke(daily, new object[] { false }));
                Require(Field<Label>(daily, "status").Text.Contains("No text recognition language"), "a missing OCR engine is reported");
                Field<InputBox>(daily, "logInput").Box.Text = string.Join(Environment.NewLine, lines);
                Require(daily.Checks.Count == 8 && Field<DataGridView>(daily, "results").Rows.Count == 8 &&
                    Field<Label>(daily, "summary").Text == "6 of 8 lines are violations: #2, #4, #5, #6, #7, #8" &&
                    daily.Report().StartsWith("#2  [09:13:59] SCAN /sector=06" + Environment.NewLine + "    Missing /status parameter: /status is missing"),
                    "log results and summary");
                string report = daily.Report();
                int start = report.IndexOf("#6  ", StringComparison.Ordinal);
                int end = report.IndexOf("#7  ", StringComparison.Ordinal);
                Require(start >= 0 && end > start, "multi-error line appears in the report");
                string entry = report.Substring(start, end - start);
                Require(new[] { LogField.Time, LogField.Type, LogField.Sector, LogField.Status }
                    .All(field => entry.Contains(LogValidator.FieldName(field))), "one report row lists every error category");
                daily.SetLanguage(UiLanguage.Chinese);
                Require(daily.Text == "每日任务" && Field<Label>(daily, "summary").Text.StartsWith("8 行中有 6 行违规"), "Chinese daily tasks");
                SaveWindow(daily, Path.Combine(output, "ui-daily-logs-preview.png"));
            }
        }
        Console.WriteLine("Signal decoding, screen reading at two scales, log validation, and the Daily Tasks window passed.");
    }

    private static void GenerateLogExamples(string output)
    {
        string[][] examples =
        {
            new[] { "[00:00:00] AUTH /sector=1 /status=OK", "[23:59:59] SYNC /sector=256 /status=ERROR",
                "[12:34:56] PING /sector=42 /status=WARN" },
            new[] { "[24:07:01] SCN /sector=12 /status=OK", "[08:15:23] AUTH /sector=289 /status=FAIL",
                "[12:45:10] RADIO-TEST" },
            new[] { "[27:65:90] SCANNER /sector=999 /status=FAIL", "[18:03:07] SCAN /sector=5 /status=WARN",
                "[12:45:10] RADAR /status=BAD" }
        };
        LogField[][][] expected =
        {
            new[] { new LogField[0], new LogField[0], new LogField[0] },
            new[] { new[] { LogField.Time, LogField.Type }, new[] { LogField.Sector, LogField.Status },
                new[] { LogField.MissingSector, LogField.MissingStatus } },
            new[] { new[] { LogField.Time, LogField.Type, LogField.Sector, LogField.Status }, new LogField[0],
                new[] { LogField.Type, LogField.MissingSector, LogField.Status } }
        };
        string[] names = { "01-valid", "02-two-errors-per-line", "03-mixed-errors" };
        string path = Path.Combine(output, "log-integrity-examples");
        Directory.CreateDirectory(path);
        var answer = new List<string> { "# 日志完整性测试样例", "", "把 TXT 内容贴到每日任务的日志框，或打开 PNG 再用「读取日志」。下列结果以 TXT 原文为准。" };
        for (int set = 0; set < examples.Length; set++)
        {
            string text = string.Join(Environment.NewLine, examples[set]);
            List<LogCheck> checks = LogValidator.Check(text);
            Require(checks.Count == expected[set].Length, "all example lines checked");
            File.WriteAllText(Path.Combine(path, names[set] + ".txt"), text + Environment.NewLine);
            using (Bitmap image = DrawLogs(examples[set], 860))
                image.Save(Path.Combine(path, names[set] + ".png"), ImageFormat.Png);
            answer.Add("");
            answer.Add("## " + names[set]);
            answer.Add("");
            for (int row = 0; row < checks.Count; row++)
            {
                Require(checks[row].Issues.Select(issue => issue.Field).Distinct().SequenceEqual(expected[set][row]),
                    "all error categories on example " + (set + 1) + ", row " + (row + 1));
                answer.Add("- 第 " + (row + 1) + " 行 · " + (checks[row].Ok ? "通过" :
                    string.Join("；", expected[set][row].Select(LogValidator.FieldName))));
                foreach (LogIssue issue in checks[row].Issues) answer.Add("  - " + issue.Chinese);
            }
        }
        File.WriteAllLines(Path.Combine(path, "答案.md"), answer);
        Console.WriteLine("Three log example sets passed, including multiple error categories and multiple time errors on one line.");
    }

    // Light monospace text on a dark background with scanlines, like the game's log list.
    private static Bitmap DrawLogs(string[] lines, int width = 560, string fontFamily = null)
    {
        var image = new Bitmap(width, 30 + lines.Length * 34);
        using (Graphics g = Graphics.FromImage(image))
        using (var text = new SolidBrush(Color.FromArgb(190, 205, 215)))
        using (var scan = new Pen(Color.FromArgb(22, 30, 36)))
        using (var font = new Font(fontFamily ?? FontFamily.GenericMonospace.Name, 13f))
        {
            g.Clear(Color.FromArgb(12, 17, 21));
            for (int y = 0; y < image.Height; y += 3) g.DrawLine(scan, 0, y, image.Width, y);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            for (int i = 0; i < lines.Length; i++) g.DrawString(lines[i], font, text, 14, 14 + i * 34);
        }
        return image;
    }

    private static void VerifyLogClockFallback()
    {
        string[] clocks = { "[01:23:45]", "[17:58:09]", "[06:40:07]", "[12:63:10]", "(12:63:10]", "" };
        using (Bitmap image = DrawLogs(clocks, fontFamily: "Courier New"))
        {
            var recognizer = new LogClockRecognizer(image);
            Func<int, Rectangle> area = row => new Rectangle(0, 13 + row * 34, image.Width, 30);
            for (int i = 0; i < 3; i++) recognizer.Learn(area(i), clocks[i], Rectangle.Empty, "");
            Require(recognizer.Read(area(3)) == clocks[3],
                "visual timestamp recovery preserves minutes outside the valid range");
            Require(recognizer.Read(area(4)) == null,
                "visual timestamp recovery does not replace a real parenthesis with a bracket");
            Require(recognizer.Read(area(5)) == null,
                "visual timestamp recovery does not invent a timestamp for a blank area");
        }
    }

    // Runs the real Windows text recognition when it is available. It reports instead of failing, because the
    // result depends on the OCR languages installed on this computer.
    private static void TryWindowsOcr()
    {
        string[] sample = { "[21:14:53] SCAN /sector=12 /status=OK", "[18:67:31] ROUTE-CHECK /sector=289 /status=OK" };
        using (Bitmap image = DrawLogs(sample))
        {
            try
            {
                List<string> read = LogReader.Read(image).Select(LogValidator.FixOcr).ToList();
                bool same = read.SequenceEqual(sample);
                Console.WriteLine("Windows OCR {0}: {1}", same ? "read the sample exactly" : "read the sample with differences",
                    string.Join(" | ", read));
            }
            catch (OcrUnavailableException ex)
            {
                Console.WriteLine("Windows OCR not checked here: " + ex.Message);
            }
        }
    }

    private static void CheckGameFrame(string path, bool full, string output, LanguagePreference preference)
    {
        Rectangle target = full ? new Rectangle(58, 143, 313, 316) : new Rectangle(60, 167, 319, 305);
        Rectangle board = full ? new Rectangle(520, 250, 662, 572) : new Rectangle(528, 286, 663, 569);
        Rectangle wide = board;
        wide.Inflate(28, 28);
        Rectangle shifted = board;
        shifted.Offset(18, -18);
        using (var sample = new Bitmap(path))
        {
            foreach (Rectangle select in new[] { board, wide, shifted })
            {
                TargetPattern pattern;
                Matches result = Match(sample, target, select, out pattern);
                Require(result.TargetCount == 3 && result.BoardCount == (full ? 100 : 38), "game frame cell count");
                if (full)
                {
                    Candidate first = result.Ranked[0];
                    Require(first.Row == 6 && first.Column == 6 && first.Confirmed == 3 && first.Conflicts == 0 &&
                        result.Ranked.Count(c => c.Confirmed == 3) == 1, "full game frame unique match");
                    Require(result.Ranked.Any(c => c.Row == 4 && c.Column == 2 && c.Confirmed == 2 && c.Conflicts == 1),
                        "lookalike triangle rejected as hexagon");
                }
                else
                {
                    Require(result.Ranked.Count(c => c.Conflicts == 0 && c.Confirmed == 1 && c.Unknown == 2) == 7 &&
                        result.Ranked.Count(c => c.Unknown == 3) == 5 &&
                        result.Ranked[0].Confirmed == 1, "partial game frame ties");
                }
            }
            using (var window = TestWindow(preference))
            {
                const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Instance;
                var start = typeof(FinderWindow).GetMethod("StartStill", fields);
                start.Invoke(window, new object[] { (Bitmap)sample.Clone(), new Calibration { Board = wide, Target = target },
                    FinderWindow.ViewMode.Screenshot });
                var status = (Label)typeof(FinderWindow).GetField("status", fields).GetValue(window);
                var preview = typeof(FinderWindow).GetField("preview", fields).GetValue(window);
                bool exact = (bool)typeof(BoardPreview).GetField("exactMatch", fields).GetValue(preview);
                Require(exact == full && status.Text.Contains(full ? "唯一全部匹配" : "7 个并列"), "game frame UI evidence");
                var ranking = (Label)typeof(FinderWindow).GetField("ranking", fields).GetValue(window);
                Require(ranking.Text.Contains(full ? "第 7 行第 7 格" : "7. 8行13格"), "game frame visible positions");
                CreateHandles(window);
                window.PerformLayout();
                using (var bitmap = new Bitmap(window.Width, window.Height))
                {
                    window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(output, full ? "game-full-preview.png" : "game-partial-preview.png"), ImageFormat.Png);
                }
            }
        }
        Console.WriteLine(full ? "Full game frame: 100/100 visible, one 3/3 match at row 7 slot 7."
            : "Partial game frame: 38 visible, seven tied suggestions and five untouched positions.");
    }

    private static void VerifyLiveTimer(TestBoard a, TestBoard b, LanguagePreference preference)
    {
        var frames = Enumerable.Range(0, 15).Select(n => Tuple.Create(a, n))
            .Concat(Enumerable.Range(0, 15).Select(n => Tuple.Create(b, n))).ToArray();
        int cursor = 0, seen = 0;
        bool english = false;
        var measured = new bool[frames.Length];
        long maxDelay = 0, arrival = 0;
        var clock = Stopwatch.StartNew();
        Bitmap frame = a.Draw(0);
        try
        {
            using (var window = new FinderWindow(area => Crop(frame, area), () => new Rectangle(Point.Empty, TestBoard.CanvasSize), preference))
            using (var driver = new Timer { Interval = 16 })
            {
                VerifyScanOptions(window);
                window.Location = new Point(TestBoard.CanvasSize.Width + 10, 0);
                MethodInfo start = typeof(FinderWindow).GetMethod("StartLive", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo statusField = typeof(FinderWindow).GetField("status", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo rankingField = typeof(FinderWindow).GetField("ranking", BindingFlags.NonPublic | BindingFlags.Instance);
                var status = (Label)statusField.GetValue(window);
                var ranking = (Label)rankingField.GetValue(window);
                var languageMenu = (ContextMenuStrip)typeof(FinderWindow).GetField("languageMenu",
                    BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
                start.Invoke(window, new object[] { new Calibration { Target = TestBoard.TargetBox, Board = TestBoard.BoardBox }, false });
                clock.Restart();
                driver.Tick += delegate
                {
                    long now = clock.ElapsedMilliseconds;
                    var expected = frames[cursor];
                    string target = english ? "Target cells " + expected.Item1.TargetCells : "目标 " + expected.Item1.TargetCells + " 格";
                    string board = (english ? "Board " : "棋盘 ") + expected.Item2 + "/100";
                    if (!measured[cursor] && status.Text.Contains(target) && status.Text.Contains(board))
                    {
                        measured[cursor] = true;
                        seen++;
                        maxDelay = Math.Max(maxDelay, now - arrival);
                    }
                    if (now - arrival < 250) return;
                    if (cursor + 1 == frames.Length)
                    {
                        driver.Stop();
                        Application.ExitThread();
                        return;
                    }
                    cursor++;
                    if (cursor == 8 || cursor == 23)
                    {
                        english = cursor == 8;
                        languageMenu.Items[english ? 1 : 0].PerformClick();
                    }
                    var old = frame;
                    frame = frames[cursor].Item1.Draw(frames[cursor].Item2);
                    old.Dispose();
                    arrival = clock.ElapsedMilliseconds;
                };
                driver.Start();
                Application.Run();
                Require(seen == frames.Length, "live timer observed " + seen + "/" + frames.Length + " frames");
                Require(ranking.Text.StartsWith("1. 第 5 行第 5 格"), "live timer final rank after shape change");
            }
        }
        finally { frame.Dispose(); }
        Console.WriteLine("Live polling with simulated captures at 4 reveals/s: {0}/{1} states, max observed update delay {2} ms.",
            seen, frames.Length, maxDelay);
    }

    private static void VerifyScanOptions(FinderWindow window)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        Type type = typeof(FinderWindow);
        type.GetField("live", fields).SetValue(window, null);
        var option = (ContextMenuStrip)type.GetField("scanMenu", fields).GetValue(window);
        var caption = (Button)type.GetField("scanInterval", fields).GetValue(window);
        var scanner = (Timer)type.GetField("timer", fields).GetValue(window);
        int[] values = { 100, 500, 1000, 1500, 2000 };
        Require(option.Items.Count == values.Length, "scan interval choices");
        for (int i = 0; i < values.Length; i++)
        {
            option.Items[i].PerformClick();
            Require(scanner.Interval == values[i] && caption.Text.StartsWith(option.Items[i].Text), "scan interval " + values[i]);
        }
        option.Items[0].PerformClick();
    }

    private static Rectangle Scale(Rectangle r)
    {
        return Rectangle.FromLTRB((int)(r.Left * .75), (int)(r.Top * .75),
            (int)Math.Ceiling(r.Right * .75), (int)Math.Ceiling(r.Bottom * .75));
    }

    private static void Generate(string output, TestBoard scene, int shown, string name, bool scale)
    {
        using (var original = scene.Draw(shown))
        using (var image = scale ? new Bitmap(original, new Size((int)(original.Width * .75), (int)(original.Height * .75))) :
                                   new Bitmap(original))
        {
            TargetPattern pattern;
            var clock = Stopwatch.StartNew();
            Matches found = Match(image, scale ? Scale(TestBoard.TargetBox) : TestBoard.TargetBox,
                scale ? Scale(TestBoard.BoardBox) : TestBoard.BoardBox, out pattern);
            clock.Stop();
            Describe(name, found);
            Console.WriteLine("   Fit + analyze {0:F1} ms", clock.Elapsed.TotalMilliseconds);
            image.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
            Require(found.TargetCount == scene.TargetCells, name + " target count");
            Require(found.BoardCount == shown, name + " board count");
            if (shown == 0) Require(found.Ranked.All(c => c.Confirmed == 0 && c.Conflicts == 0), name + " no evidence");
            if (shown == 2)
                Require(found.Ranked.Take(2).All(c => c.Confirmed == 1 && c.Unknown == 2 && c.Conflicts == 0), name + " tie");
            if (shown > 2) Require(Expected(found.Ranked[0], scene), name + " final match");
        }
    }
}
