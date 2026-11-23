using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GlyphFinder
{
    internal sealed class TerminalPuzzleWindow : Form
    {
        private readonly TerminalPuzzle puzzle;
        private UiLanguage language;
        private readonly Font mono = Theme.Mono(9f), largeMono = Theme.Mono(14f);
        private readonly Label recommendationTitle = Caption(), recommendation = Caption(), guidance = Caption();
        private readonly Label mismatchLabel = Caption(), historyLabel = Caption(), remainingLabel = Caption(), status = Caption();
        private readonly Label[] fieldLabels = new Label[TerminalPuzzle.FieldCount];
        private readonly Segmented[] fields = new Segmented[TerminalPuzzle.FieldCount];
        private readonly Segmented mismatch = new Segmented { Items = new[] { "0", "1", "2", "3", "4" } };
        private readonly FlatButton onTop = new FlatButton { Kind = ButtonKind.Ghost, Checked = true, Margin = Padding.Empty };
        private readonly FlatButton useSuggestion = new FlatButton();
        private readonly FlatButton record = new FlatButton { Kind = ButtonKind.Primary, Icon = ButtonGlyph.Plus };
        private readonly FlatButton undo = new FlatButton(), reset = new FlatButton { Kind = ButtonKind.Ghost };
        private readonly ListBox history = new ListBox(), candidates = new ListBox();
        private bool conflicting;

        public TerminalPuzzleWindow(UiLanguage language, TerminalPuzzle puzzle)
        {
            this.language = language;
            this.puzzle = puzzle;
            BackColor = Palette.Background;
            ForeColor = Palette.Text;
            Font = Theme.Ui(9f);
            Icon icon = Theme.AppIcon();
            if (icon != null) Icon = icon;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(690, 510);
            MinimumSize = new Size(670, 530);
            StartPosition = FormStartPosition.Manual;
            TopMost = onTop.Checked;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(14, 10, 14, 10)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 114));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            Controls.Add(layout);

            var solution = new Card { Dock = DockStyle.Fill, Padding = new Padding(14, 8, 10, 8), Margin = new Padding(0, 0, 0, 8) };
            var suggested = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            suggested.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            suggested.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            suggested.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
            suggested.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
            suggested.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            recommendation.Font = largeMono;
            recommendation.ForeColor = Palette.Primary;
            onTop.Anchor = AnchorStyles.Right;
            useSuggestion.Anchor = AnchorStyles.Right;
            suggested.Controls.Add(recommendationTitle, 0, 0);
            suggested.Controls.Add(onTop, 1, 0);
            suggested.Controls.Add(recommendation, 0, 1);
            suggested.Controls.Add(useSuggestion, 1, 1);
            suggested.Controls.Add(guidance, 0, 2);
            suggested.SetColumnSpan(guidance, 2);
            solution.Controls.Add(suggested);
            layout.Controls.Add(solution, 0, 0);

            var selectors = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Margin = Padding.Empty };
            for (int row = 0; row < 2; row++)
            {
                selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                selectors.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
                selectors.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            }
            for (int field = 0; field < fields.Length; field++)
            {
                fieldLabels[field] = Caption();
                fields[field] = new Segmented { Items = TerminalPuzzle.Options[field], Anchor = AnchorStyles.Left | AnchorStyles.Top };
                selectors.Controls.Add(fieldLabels[field], field % 2, field / 2 * 2);
                selectors.Controls.Add(fields[field], field % 2, field / 2 * 2 + 1);
            }
            layout.Controls.Add(selectors, 0, 1);

            var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            mismatchLabel.Margin = new Padding(0, 7, 6, 0);
            mismatch.Margin = new Padding(0, 0, 8, 0);
            controls.Controls.AddRange(new Control[] { mismatchLabel, mismatch, record });
            layout.Controls.Add(controls, 0, 2);

            var lists = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            lists.Controls.Add(BuildList(historyLabel, history), 0, 0);
            lists.Controls.Add(BuildList(remainingLabel, candidates), 1, 0);
            layout.Controls.Add(lists, 0, 3);
            status.Dock = DockStyle.Fill;
            status.AutoSize = false;
            status.Padding = new Padding(0, 5, 0, 0);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var roundActions = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right, Margin = new Padding(8, 5, 0, 0)
            };
            roundActions.Controls.AddRange(new Control[] { undo, reset });
            footer.Controls.Add(status, 0, 0);
            footer.Controls.Add(roundActions, 1, 0);
            layout.Controls.Add(footer, 0, 4);
            AcceptButton = record;

            onTop.Click += delegate { onTop.Checked = TopMost = !TopMost; };
            useSuggestion.Click += delegate { SetGuess(puzzle.Recommend()); mismatch.Focus(); };
            mismatch.SelectedIndexChanged += delegate { record.Enabled = !puzzle.Solved && mismatch.SelectedIndex >= 0; };
            record.Click += delegate
            {
                int guess = fields.Aggregate(0, (code, field) => code * 3 + field.SelectedIndex);
                if (!puzzle.Record(guess, mismatch.SelectedIndex))
                {
                    conflicting = true;
                    UpdateText();
                    return;
                }
                RefreshRound();
            };
            undo.Click += delegate
            {
                TerminalTrial last = puzzle.Undo();
                if (last == null) return;
                RefreshRound();
                SetGuess(last.Guess);
                mismatch.SelectedIndex = last.Mismatched;
            };
            reset.Click += delegate { puzzle.Reset(); RefreshRound(); };
            RefreshRound();
        }

        public void SetLanguage(UiLanguage selected)
        {
            language = selected;
            UpdateText();
        }

        private void UpdateText()
        {
            Text = language.Text("终端入侵助手", "Terminal Intrusion Helper");
            string[] names = language.Text("版本|方法|信道|加密", "Version|Method|Channel|Cipher").Split('|');
            for (int i = 0; i < fields.Length; i++) fields[i].AccessibleName = fieldLabels[i].Text = names[i];
            mismatch.AccessibleName = mismatchLabel.Text = language.Text("错误数", "Mismatched");
            onTop.Text = language.Text("置顶", "Keep on top");
            useSuggestion.Text = language.Text("填入建议", "Use suggestion");
            record.Text = language.Text("记录反馈", "Record result");
            undo.Text = language.Text("撤销上次", "Undo");
            reset.Text = language.Text("新一局", "New round");
            history.AccessibleName = historyLabel.Text = language.Text("尝试记录 · 错误数", "Attempts · Mismatched");
            candidates.AccessibleName = remainingLabel.Text = string.Format(language.Text("剩余组合（{0}）", "Remaining combinations ({0})"), puzzle.Remaining.Count);
            recommendationTitle.Text = puzzle.Solved ? language.Text("已确认正确", "Confirmed") : language.Text("建议尝试", "Suggested guess");
            int guess = puzzle.Recommend();
            recommendation.Text = TerminalPuzzle.Format(guess);
            recommendation.ForeColor = puzzle.Solved ? Palette.Success : Palette.Primary;
            guidance.Text = puzzle.Remaining.Contains(guess) ? "" : language.Text("这组尝试能区分更多候选。", "This guess separates more of the remaining combinations.");
            status.ForeColor = conflicting ? Palette.Accent : Palette.Muted;
            status.Text = conflicting
                ? language.Text("这个反馈与之前的记录矛盾，请核对所选组合和错误数。", "This result conflicts with earlier entries. Check the guess and mismatch count.")
                : puzzle.Solved ? language.Text("本轮已解出。开始下一局时点「新一局」。", "Solved. Choose New round for the next puzzle.")
                : language.Text("在游戏里提交所选组合，然后记录游戏显示的 Mismatched 数字。", "Submit the chosen combination in the game, then record the displayed mismatch count.");
        }

        private void RefreshRound()
        {
            conflicting = false;
            SetGuess(puzzle.Recommend());
            mismatch.SelectedIndex = -1;
            foreach (Segmented field in fields) field.Enabled = !puzzle.Solved;
            mismatch.Enabled = useSuggestion.Enabled = !puzzle.Solved;
            record.Enabled = false;
            undo.Enabled = reset.Enabled = puzzle.Trials.Count > 0;
            history.Items.Clear();
            int index = 0;
            foreach (TerminalTrial trial in puzzle.Trials)
                history.Items.Add(string.Format("{0}. {1}  · {2}", ++index, TerminalPuzzle.Format(trial.Guess, " "), trial.Mismatched));
            if (history.Items.Count > 0) history.TopIndex = history.Items.Count - 1;
            candidates.Items.Clear();
            candidates.Items.AddRange(puzzle.Remaining.Select(candidate => TerminalPuzzle.Format(candidate)).ToArray());
            UpdateText();
        }

        private void SetGuess(int guess)
        {
            for (int field = 0; field < fields.Length; field++) fields[field].SelectedIndex = TerminalPuzzle.OptionAt(guess, field);
        }

        private Card BuildList(Label title, ListBox list)
        {
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 6), Margin = new Padding(0, 0, 6, 0) };
            title.Dock = DockStyle.Top;
            title.AutoSize = false;
            title.Height = 24;
            list.Dock = DockStyle.Fill;
            list.IntegralHeight = false;
            list.BorderStyle = BorderStyle.None;
            list.Font = mono;
            list.BackColor = Palette.Panel;
            list.ForeColor = Palette.Text;
            Theme.DarkScrollBars(list);
            card.Controls.Add(list);
            card.Controls.Add(title);
            return card;
        }

        private static Label Caption()
        {
            return new Label { AutoSize = true, ForeColor = Palette.Muted, UseMnemonic = false };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            mono.Dispose();
            largeMono.Dispose();
        }
    }
}
