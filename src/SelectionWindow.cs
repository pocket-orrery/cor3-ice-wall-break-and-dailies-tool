using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GlyphFinder
{
    internal sealed class SelectionWindow : Form
    {
        private readonly Bitmap image;
        private readonly Rectangle? desktop;
        private readonly UiLanguage language;
        private readonly string singlePrompt;
        private Point start;
        private Rectangle drag;
        private bool dragging;
        public Rectangle Target { get; private set; }
        public Rectangle Board { get; private set; }

        // With a prompt, one rectangle is selected and returned as Target; otherwise the target and then the board.
        public SelectionWindow(Bitmap image, Rectangle? desktop, UiLanguage language, string singlePrompt = null)
        {
            this.singlePrompt = singlePrompt;
            this.image = image;
            this.desktop = desktop;
            this.language = language;
            Text = language.Text("框选目标和棋盘", "Select the target and board");
            BackColor = Palette.Background;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            Font = Theme.Ui(10f);
            if (desktop.HasValue)
            {
                StartPosition = FormStartPosition.Manual;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                Bounds = desktop.Value;
            }
            else
            {
                StartPosition = FormStartPosition.CenterParent;
                Size screen = Screen.FromPoint(Cursor.Position).WorkingArea.Size;
                ClientSize = new Size(Math.Min(screen.Width - 90, image.Width + 18),
                                      Math.Min(screen.Height - 100, image.Height + 92));
                MinimumSize = new Size(480, 360);
            }
        }

        private RectangleF ImageArea()
        {
            if (desktop.HasValue) return new RectangleF(0, 0, image.Width, image.Height);
            float scale = Math.Min((ClientSize.Width - 14f) / image.Width, (ClientSize.Height - 88f) / image.Height);
            return new RectangleF((ClientSize.Width - image.Width * scale) / 2,
                78 + (ClientSize.Height - 78 - image.Height * scale) / 2,
                image.Width * scale, image.Height * scale);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!desktop.HasValue) Theme.DarkTitleBar(this);
        }

        private RectangleF OnScreen(RectangleF picture, Rectangle box)
        {
            float scale = picture.Width / image.Width;
            return new RectangleF(picture.X + box.X * scale, picture.Y + box.Y * scale, box.Width * scale, box.Height * scale);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var picture = ImageArea();
            g.InterpolationMode = desktop.HasValue ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
            g.DrawImage(image, picture);

            // Dim everything except the areas already chosen, so the boxes stand out against the game.
            using (var dim = new Region(picture))
            {
                if (!Target.IsEmpty) dim.Exclude(OnScreen(picture, Target));
                if (dragging) dim.Exclude(OnScreen(picture, drag));
                using (var shade = new SolidBrush(Color.FromArgb(120, 4, 7, 10))) g.FillRegion(shade, dim);
            }
            if (!Target.IsEmpty) DrawBox(g, picture, Target, Palette.Accent, language.Text("目标", "Target"));
            if (dragging)
            {
                bool board = !Target.IsEmpty || singlePrompt != null;
                DrawBox(g, picture, drag, board ? Palette.Alternate : Palette.Accent,
                    singlePrompt != null ? language.Text("区域", "Area") : board ? language.Text("棋盘", "Board") : language.Text("目标", "Target"));
                DrawSize(g, OnScreen(picture, drag), board ? Palette.Alternate : Palette.Accent);
            }
            DrawBanner(g);
        }

        private void DrawBanner(Graphics g)
        {
            bool first = Target.IsEmpty && singlePrompt == null;
            string step = singlePrompt != null ? singlePrompt : first
                ? language.Text("框住左侧目标显示区，留出轮廓变化的空间", "Select the full target area on the left.")
                : language.Text("再框住整个大三角棋盘，从最上方尖顶到最底一排", "Now select the whole triangular board.");
            string hint = singlePrompt != null ? language.Text("拖动框选   ·   Esc 取消", "Drag to select   ·   Esc cancels")
                : (first ? language.Text("避开标题和边框", "Skip titles and borders") : language.Text("贴着外沿框选", "Follow the outer edges"))
                + language.Text("   ·   右键重来   ·   Esc 取消", "   ·   Right-click resets   ·   Esc cancels");
            using (var small = new Font(Font.FontFamily, 8.5f))
            {
                int textWidth = Math.Max(TextRenderer.MeasureText(g, step, Font).Width, TextRenderer.MeasureText(g, hint, small).Width);
                int width = Math.Min(textWidth + 76, ClientSize.Width - 16);
                var panel = new Rectangle((ClientSize.Width - width) / 2, 10, width, 58);
                Theme.FillRound(g, panel, 10, Color.FromArgb(244, Palette.Panel), Palette.BorderStrong);
                Color badge = first ? Palette.Accent : Palette.Alternate;
                var circle = new Rectangle(panel.X + 14, panel.Y + 15, 28, 28);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var ring = new SolidBrush(Color.FromArgb(40, badge))) g.FillEllipse(ring, circle);
                using (var pen = new Pen(badge, 1.6f)) g.DrawEllipse(pen, circle);
                TextRenderer.DrawText(g, singlePrompt != null ? "1" : first ? "1" : "2", Font, circle, badge,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                var text = new Rectangle(circle.Right + 12, panel.Y + 8, panel.Right - circle.Right - 22, 24);
                TextRenderer.DrawText(g, step, Font, text, Palette.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                text.Offset(0, 22);
                TextRenderer.DrawText(g, hint, small, text, Palette.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }
        }

        private void DrawBox(Graphics g, RectangleF picture, Rectangle box, Color color, string label)
        {
            RectangleF drawn = OnScreen(picture, box);
            g.SmoothingMode = SmoothingMode.None;
            using (var pen = new Pen(Color.FromArgb(200, color), 1))
            using (var fill = new SolidBrush(Color.FromArgb(22, color)))
            {
                g.FillRectangle(fill, drawn);
                g.DrawRectangle(pen, drawn.X, drawn.Y, drawn.Width, drawn.Height);
            }
            int length = (int)Math.Min(16, Math.Min(drawn.Width, drawn.Height) / 3);
            var outline = Rectangle.Round(drawn);
            using (var pen = new Pen(color, 3) { StartCap = LineCap.Square, EndCap = LineCap.Square })
            {
                g.DrawLines(pen, new[] { new Point(outline.Left, outline.Top + length), new Point(outline.Left, outline.Top), new Point(outline.Left + length, outline.Top) });
                g.DrawLines(pen, new[] { new Point(outline.Right - length, outline.Top), new Point(outline.Right, outline.Top), new Point(outline.Right, outline.Top + length) });
                g.DrawLines(pen, new[] { new Point(outline.Right, outline.Bottom - length), new Point(outline.Right, outline.Bottom), new Point(outline.Right - length, outline.Bottom) });
                g.DrawLines(pen, new[] { new Point(outline.Left + length, outline.Bottom), new Point(outline.Left, outline.Bottom), new Point(outline.Left, outline.Bottom - length) });
            }
            if (label.Length == 0) return;
            using (var small = new Font(Font.FontFamily, 8.5f, FontStyle.Bold))
            {
                Size size = TextRenderer.MeasureText(g, label, small);
                var tag = new Rectangle(outline.Left, Math.Max(0, outline.Top - size.Height - 8), size.Width + 12, size.Height + 4);
                Theme.FillRound(g, tag, 4, color, color);
                TextRenderer.DrawText(g, label, small, tag, Palette.OnPrimary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        private void DrawSize(Graphics g, RectangleF drawn, Color color)
        {
            if (drag.Width < 2 || drag.Height < 2) return;
            string text = drag.Width + " × " + drag.Height;
            using (var mono = Theme.Mono(8.5f))
            {
                Size size = TextRenderer.MeasureText(g, text, mono);
                var tag = new Rectangle((int)drawn.Right - size.Width - 10, (int)drawn.Bottom + 6, size.Width + 10, size.Height + 4);
                Theme.FillRound(g, tag, 4, Color.FromArgb(230, Palette.Panel), Color.FromArgb(160, color));
                TextRenderer.DrawText(g, text, mono, tag, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        private Point ImagePoint(Point point)
        {
            RectangleF picture = ImageArea();
            float scale = picture.Width / image.Width;
            int x = Math.Max(0, Math.Min(image.Width, (int)Math.Round((point.X - picture.Left) / scale)));
            int y = Math.Max(0, Math.Min(image.Height, (int)Math.Round((point.Y - picture.Top) / scale)));
            return new Point(x, y);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                Target = Rectangle.Empty;
                dragging = false;
                Capture = false;
                Invalidate();
                return;
            }
            if (e.Button != MouseButtons.Left || !ImageArea().Contains(e.Location)) return;
            start = ImagePoint(e.Location);
            drag = new Rectangle(start, Size.Empty);
            dragging = true;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            UpdateDrag(e.Location);
            Invalidate();
        }

        private void UpdateDrag(Point point)
        {
            Point end = ImagePoint(point);
            drag = Rectangle.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
                                      Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging || e.Button != MouseButtons.Left) return;
            UpdateDrag(e.Location);
            dragging = false;
            Capture = false;
            if (singlePrompt != null && drag.Width >= 15 && drag.Height >= 6)
            {
                Target = drag;
                DialogResult = DialogResult.OK;
                Close();
            }
            else if (drag.Width >= 15 && drag.Height >= 15)
            {
                if (Target.IsEmpty) Target = drag;
                else if (!Target.IntersectsWith(drag))
                {
                    Board = drag;
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    System.Media.SystemSounds.Beep.Play();
                }
            }
            Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData != Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
    }
}
