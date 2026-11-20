using System;
using System.Drawing;
using System.Windows.Forms;

namespace GlyphFinder
{
    internal static class Simulator
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SimulatorWindow());
        }
    }

    internal sealed class SimulatorWindow : Form
    {
        private readonly PictureBox picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
        private readonly Timer timer = new Timer();
        private readonly ComboBox shape = new ComboBox { Width = 112, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly NumericUpDown speed = new NumericUpDown { Width = 43, Minimum = 1, Maximum = 4, Value = 4 };
        private readonly Button run = Button("开始");
        private TestBoard scene;
        private int count;

        public SimulatorWindow()
        {
            Text = "三角符号测试台";
            Font = new Font("Microsoft YaHei UI", 9f);
            var available = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(1080, available.Width - 44), Math.Min(664, available.Height - 70));
            MinimumSize = new Size(610, 365);
            StartPosition = FormStartPosition.Manual;
            Location = new Point(available.Left + 5, available.Top + 6);
            BackColor = Color.FromArgb(34, 39, 47);
            ForeColor = Color.FromArgb(237, 239, 251);
            AutoScaleMode = AutoScaleMode.Dpi;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(9, 5, 0, 0), WrapContents = false };
            shape.Items.AddRange(new object[] { "三格目标 A", "五格目标 B" });
            shape.SelectedIndex = 0;
            shape.SelectedIndexChanged += delegate { ResetBoard(); };
            var speedLabel = new Label { Text = "每秒揭格", AutoSize = true, Margin = new Padding(8, 5, 2, 0) };
            speed.ValueChanged += delegate { timer.Interval = 1000 / (int)speed.Value; };
            run.Click += delegate
            {
                if (timer.Enabled) Stop();
                else
                {
                    if (count == TestBoard.CellCount) ResetBoard();
                    timer.Interval = 1000 / (int)speed.Value;
                    timer.Start();
                    run.Text = "暂停";
                }
            };
            var step = Button("揭一格");
            step.Click += delegate { Stop(); Reveal(); };
            var reset = Button("重置");
            reset.Click += delegate { ResetBoard(); };
            actions.Controls.AddRange(new Control[] { shape, speedLabel, speed, run, step, reset });
            layout.Controls.Add(actions, 0, 0);
            layout.Controls.Add(picture, 0, 1);
            Controls.Add(layout);
            timer.Tick += delegate { Reveal(); };
            ResetBoard();
        }

        private static Button Button(string text)
        {
            return new Button { Text = text, AutoSize = true, Height = 30, FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(237, 239, 251), BackColor = Color.FromArgb(51, 58, 73) };
        }

        private void ResetBoard()
        {
            Stop();
            scene = new TestBoard(shape.SelectedIndex == 1);
            count = 0;
            Draw();
        }

        private void Reveal()
        {
            if (count == TestBoard.CellCount) return;
            count++;
            Draw();
            if (count == TestBoard.CellCount) Stop();
        }

        private void Stop()
        {
            timer.Stop();
            run.Text = "开始";
        }

        private void Draw()
        {
            var previous = picture.Image;
            picture.Image = scene.Draw(count);
            if (previous != null) previous.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                if (picture.Image != null) picture.Image.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
