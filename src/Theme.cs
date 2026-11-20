using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GlyphFinder
{
    // Colors follow the game's HUD: near-black surfaces, one cyan accent, amber for caution, green for done.
    internal static class Palette
    {
        public static readonly Color Background = Color.FromArgb(11, 15, 20);
        public static readonly Color Panel = Color.FromArgb(18, 25, 34);
        public static readonly Color Raised = Color.FromArgb(27, 36, 48);
        public static readonly Color Hover = Color.FromArgb(35, 47, 62);
        public static readonly Color Pressed = Color.FromArgb(21, 29, 39);
        public static readonly Color RowHover = Color.FromArgb(23, 32, 43);
        public static readonly Color Border = Color.FromArgb(38, 51, 67);
        public static readonly Color BorderStrong = Color.FromArgb(58, 77, 99);
        public static readonly Color Text = Color.FromArgb(222, 232, 242);
        public static readonly Color Muted = Color.FromArgb(132, 150, 170);
        public static readonly Color Faint = Color.FromArgb(82, 98, 117);
        public static readonly Color Primary = Color.FromArgb(79, 200, 235);
        public static readonly Color PrimaryHover = Color.FromArgb(122, 217, 243);
        public static readonly Color PrimaryPressed = Color.FromArgb(58, 168, 201);
        public static readonly Color OnPrimary = Color.FromArgb(5, 19, 27);
        public static readonly Color Success = Color.FromArgb(92, 214, 155);
        public static readonly Color Danger = Color.FromArgb(242, 116, 126);
        public static readonly Color Accent = Color.FromArgb(255, 204, 112);
        public static readonly Color Alternate = Color.FromArgb(111, 216, 255);

        public static Color Blend(Color over, Color under, int alpha)
        {
            return Color.FromArgb(
                (over.R * alpha + under.R * (255 - alpha)) / 255,
                (over.G * alpha + under.G * (255 - alpha)) / 255,
                (over.B * alpha + under.B * (255 - alpha)) / 255);
        }
    }

    internal enum ButtonGlyph { None, Play, Pause, Select, List, Plus, Copy, Trash, Pulse }

    internal static class Theme
    {
        public const string UiFamily = "Microsoft YaHei UI";
        private static string monoFamily;

        public static Font Ui(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font(UiFamily, size, style);
        }

        public static Font Mono(float size)
        {
            if (monoFamily == null)
            {
                monoFamily = FontFamily.GenericMonospace.Name;
                foreach (string name in new[] { "Cascadia Mono", "Consolas" })
                    using (var probe = new Font(name, size))
                        if (probe.Name == name)
                        {
                            monoFamily = name;
                            break;
                        }
            }
            return new Font(monoFamily, size);
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, Rectangle r, float radius, Color fill, Color border)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Round(new RectangleF(r.X + .5f, r.Y + .5f, r.Width - 1, r.Height - 1), radius))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(border))
            {
                g.FillPath(brush, path);
                if (border != fill) g.DrawPath(pen, path);
            }
            g.SmoothingMode = old;
        }

        // HUD-style corner brackets, as drawn around panels in the game.
        public static void Brackets(Graphics g, Rectangle r, Color color, int length)
        {
            using (var pen = new Pen(color, 1.6f))
            {
                pen.StartCap = pen.EndCap = LineCap.Square;
                g.DrawLines(pen, new[] { new Point(r.Left, r.Top + length), new Point(r.Left, r.Top), new Point(r.Left + length, r.Top) });
                g.DrawLines(pen, new[] { new Point(r.Right - length, r.Bottom), new Point(r.Right, r.Bottom), new Point(r.Right, r.Bottom - length) });
            }
        }

        public static Color BackOf(Control control)
        {
            for (Control c = control.Parent; c != null; c = c.Parent)
                if (c.BackColor.A == 255) return c.BackColor;
            return Palette.Background;
        }

        public static void DrawGlyph(Graphics g, ButtonGlyph glyph, Rectangle box, Color color)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float x = box.X, y = box.Y;
            using (var pen = new Pen(color, 1.6f))
            using (var brush = new SolidBrush(color))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                switch (glyph)
                {
                    case ButtonGlyph.Play:
                        g.FillPolygon(brush, new[] { new PointF(x + 3, y + 1.5f), new PointF(x + 12.5f, y + 7), new PointF(x + 3, y + 12.5f) });
                        break;
                    case ButtonGlyph.Pause:
                        g.FillRectangle(brush, x + 3, y + 2, 3, 10);
                        g.FillRectangle(brush, x + 8, y + 2, 3, 10);
                        break;
                    case ButtonGlyph.Select:
                        pen.StartCap = pen.EndCap = LineCap.Square;
                        g.DrawLines(pen, new[] { new PointF(x + 1, y + 5), new PointF(x + 1, y + 1), new PointF(x + 5, y + 1) });
                        g.DrawLines(pen, new[] { new PointF(x + 9, y + 1), new PointF(x + 13, y + 1), new PointF(x + 13, y + 5) });
                        g.DrawLines(pen, new[] { new PointF(x + 13, y + 9), new PointF(x + 13, y + 13), new PointF(x + 9, y + 13) });
                        g.DrawLines(pen, new[] { new PointF(x + 5, y + 13), new PointF(x + 1, y + 13), new PointF(x + 1, y + 9) });
                        g.FillEllipse(brush, x + 5.5f, y + 5.5f, 3, 3);
                        break;
                    case ButtonGlyph.List:
                        for (int i = 0; i < 3; i++)
                        {
                            float row = y + 2.5f + i * 4.5f;
                            g.FillEllipse(brush, x + 1, row - 1.2f, 2.4f, 2.4f);
                            g.DrawLine(pen, x + 5.5f, row, x + 13, row);
                        }
                        break;
                    case ButtonGlyph.Plus:
                        g.DrawLine(pen, x + 7, y + 2, x + 7, y + 12);
                        g.DrawLine(pen, x + 2, y + 7, x + 12, y + 7);
                        break;
                    case ButtonGlyph.Copy:
                        using (GraphicsPath back = Round(new RectangleF(x + 4.5f, y + 1, 8.5f, 9.5f), 1.5f)) g.DrawPath(pen, back);
                        using (GraphicsPath front = Round(new RectangleF(x + 1, y + 4, 8.5f, 9.5f), 1.5f)) g.DrawPath(pen, front);
                        break;
                    case ButtonGlyph.Pulse:
                        g.DrawRectangle(pen, x + .8f, y + 4.5f, 2.6f, 5);
                        g.FillRectangle(brush, x + 5, y + 4, 4.5f, 6);
                        g.DrawRectangle(pen, x + 11, y + 4.5f, 2.6f, 5);
                        break;
                    case ButtonGlyph.Trash:
                        g.DrawLine(pen, x + 1.5f, y + 3.5f, x + 12.5f, y + 3.5f);
                        g.DrawLine(pen, x + 5.5f, y + 1.5f, x + 8.5f, y + 1.5f);
                        g.DrawLines(pen, new[] { new PointF(x + 3, y + 3.5f), new PointF(x + 4, y + 13), new PointF(x + 10, y + 13), new PointF(x + 11, y + 3.5f) });
                        break;
                }
            }
            g.SmoothingMode = old;
        }

        public static void Chevron(Graphics g, float x, float y, Color color)
        {
            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, 1.5f))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                g.DrawLines(pen, new[] { new PointF(x, y), new PointF(x + 4, y + 4), new PointF(x + 8, y) });
            }
            g.SmoothingMode = old;
        }

        public static void Style(ToolStripDropDownMenu menu)
        {
            menu.Renderer = new DarkMenuRenderer();
            menu.BackColor = Palette.Panel;
            menu.ForeColor = Palette.Text;
            menu.Font = Ui(9f);
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = true;
            menu.Padding = new Padding(2, 4, 2, 4);
        }

        public static ToolTip MakeToolTip()
        {
            var tips = new ToolTip { OwnerDraw = true, InitialDelay = 450 };
            Font font = Ui(8.5f);
            const TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left;
            tips.Popup += delegate(object sender, PopupEventArgs e)
            {
                Size text = TextRenderer.MeasureText(tips.GetToolTip(e.AssociatedControl), font, new Size(340, 0), flags);
                e.ToolTipSize = new Size(text.Width + 18, text.Height + 12);
            };
            tips.Draw += delegate(object sender, DrawToolTipEventArgs e)
            {
                using (var fill = new SolidBrush(Palette.Raised)) e.Graphics.FillRectangle(fill, e.Bounds);
                using (var pen = new Pen(Palette.BorderStrong))
                    e.Graphics.DrawRectangle(pen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, font, Rectangle.Inflate(e.Bounds, -9, -6), Palette.Text, flags);
            };
            tips.Disposed += delegate { font.Dispose(); };
            return tips;
        }

        public static Icon AppIcon()
        {
            try
            {
                using (var image = new Bitmap(32, 32))
                {
                    using (Graphics g = Graphics.FromImage(image))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);
                        Theme.FillRound(g, new Rectangle(0, 0, 32, 32), 7, Palette.Panel, Palette.Border);
                        PointF[] outer = { new PointF(16, 6), new PointF(27, 25), new PointF(5, 25) };
                        using (var pen = new Pen(Palette.Primary, 2.4f) { LineJoin = LineJoin.Round }) g.DrawPolygon(pen, outer);
                        using (var dot = new SolidBrush(Palette.Accent)) g.FillEllipse(dot, 13.5f, 15.5f, 5, 5);
                    }
                    return Icon.FromHandle(image.GetHicon());
                }
            }
            catch (Exception ex)
            {
                if (ex is ExternalException || ex is NotImplementedException || ex is ArgumentException) return null;
                throw;
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr window, string application, string idList);

        private static bool OnWindows
        {
            get { return Environment.OSVersion.Platform == PlatformID.Win32NT; }
        }

        // Dark caption on Windows 10 1809+, matching caption and border colors on Windows 11. Older systems ignore it.
        public static void DarkTitleBar(Form form)
        {
            if (!OnWindows) return;
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(form.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(form.Handle, 19, ref on, 4);
                int caption = ColorTranslator.ToWin32(Palette.Background);
                DwmSetWindowAttribute(form.Handle, 35, ref caption, 4);
                int border = ColorTranslator.ToWin32(Palette.Border);
                DwmSetWindowAttribute(form.Handle, 34, ref border, 4);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        // Dark scroll bars for grids and text boxes on Windows 10+.
        public static void DarkScrollBars(Control control)
        {
            if (!OnWindows) return;
            EventHandler apply = delegate(object sender, EventArgs e)
            {
                try { SetWindowTheme(((Control)sender).Handle, "DarkMode_Explorer", null); }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
            };
            var targets = new System.Collections.Generic.List<Control> { control };
            foreach (Control child in control.Controls) targets.Add(child);
            foreach (Control target in targets)
            {
                target.HandleCreated += apply;
                if (target.IsHandleCreated) apply(target, EventArgs.Empty);
            }
        }
    }

    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Palette.Text : Palette.Faint;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Palette.Muted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Palette.Primary, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                e.Graphics.DrawLines(pen, new[] {
                    new PointF(r.X + r.Width * .22f, r.Y + r.Height * .52f),
                    new PointF(r.X + r.Width * .42f, r.Y + r.Height * .72f),
                    new PointF(r.X + r.Width * .78f, r.Y + r.Height * .3f) });
        }

        private sealed class DarkMenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Palette.Panel; } }
            public override Color ImageMarginGradientBegin { get { return Palette.Panel; } }
            public override Color ImageMarginGradientMiddle { get { return Palette.Panel; } }
            public override Color ImageMarginGradientEnd { get { return Palette.Panel; } }
            public override Color MenuBorder { get { return Palette.BorderStrong; } }
            public override Color MenuItemBorder { get { return Palette.Hover; } }
            public override Color MenuItemSelected { get { return Palette.Hover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Palette.Hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Palette.Hover; } }
            public override Color MenuItemPressedGradientBegin { get { return Palette.Raised; } }
            public override Color MenuItemPressedGradientEnd { get { return Palette.Raised; } }
            public override Color SeparatorDark { get { return Palette.Border; } }
            public override Color SeparatorLight { get { return Palette.Panel; } }
            public override Color CheckBackground { get { return Palette.Panel; } }
            public override Color CheckSelectedBackground { get { return Palette.Hover; } }
            public override Color CheckPressedBackground { get { return Palette.Hover; } }
        }
    }

    internal enum ButtonKind { Normal, Primary, Ghost }

    internal class FlatButton : Button
    {
        private bool hover, down;
        private ButtonKind kind;
        private ButtonGlyph icon;
        private bool chevron, isChecked;

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Palette.Raised;
            ForeColor = Palette.Text;
            Height = 30;
            Margin = new Padding(0, 0, 6, 0);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Cursor = Cursors.Hand;
            UseMnemonic = false;
        }

        public ButtonKind Kind { get { return kind; } set { kind = value; Invalidate(); } }
        public ButtonGlyph Icon { get { return icon; } set { icon = value; Invalidate(); } }
        public bool Chevron { get { return chevron; } set { chevron = value; Invalidate(); } }
        public bool Checked { get { return isChecked; } set { isChecked = value; Invalidate(); } }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int text = Text.Length == 0 ? 0 : TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix).Width;
            int width = 24 + text + (icon != ButtonGlyph.None ? (text > 0 ? 21 : 14) : 0) + (chevron ? 16 : 0);
            return new Size(Math.Max(width, Math.Max(30, MinimumSize.Width)), Math.Max(30, MinimumSize.Height));
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); down = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color back = Theme.BackOf(this);
            g.Clear(back);
            Color fill, border, text;
            if (!Enabled)
            {
                fill = kind == ButtonKind.Ghost ? back : Palette.Blend(Palette.Raised, back, 120);
                border = kind == ButtonKind.Ghost ? back : Palette.Blend(Palette.Border, back, 140);
                text = Palette.Faint;
            }
            else if (kind == ButtonKind.Primary)
            {
                fill = down ? Palette.PrimaryPressed : hover ? Palette.PrimaryHover : Palette.Primary;
                border = fill;
                text = Palette.OnPrimary;
            }
            else if (isChecked)
            {
                fill = Palette.Blend(Palette.Primary, Palette.Raised, hover ? 54 : 38);
                border = Palette.Blend(Palette.Primary, Palette.Raised, 150);
                text = Palette.Text;
            }
            else if (kind == ButtonKind.Ghost)
            {
                fill = down ? Palette.Pressed : hover ? Palette.Raised : back;
                border = hover ? Palette.Border : back;
                text = hover ? Palette.Text : Palette.Muted;
            }
            else
            {
                fill = down ? Palette.Pressed : hover ? Palette.Hover : Palette.Raised;
                border = hover ? Palette.BorderStrong : Palette.Border;
                text = Palette.Text;
            }
            Theme.FillRound(g, ClientRectangle, 5, fill, border);
            if (Focused && ShowFocusCues && Enabled)
            {
                Rectangle ring = Rectangle.Inflate(ClientRectangle, -2, -2);
                Theme.FillRound(g, ring, 4, Color.FromArgb(0, fill), kind == ButtonKind.Primary ? Palette.OnPrimary : Palette.Primary);
            }

            int textWidth = Text.Length == 0 ? 0 : TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPrefix).Width;
            int total = textWidth + (icon != ButtonGlyph.None ? (textWidth > 0 ? 21 : 14) : 0) + (chevron ? 16 : 0);
            // Fixed-width dropdowns read like a select box: content on the left, chevron on the right.
            bool select = chevron && !AutoSize;
            int x = select ? 12 : (Width - total) / 2;
            if (icon != ButtonGlyph.None)
            {
                Theme.DrawGlyph(g, icon, new Rectangle(x, (Height - 14) / 2, 14, 14), text);
                x += textWidth > 0 ? 21 : 14;
            }
            if (textWidth > 0)
            {
                TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, textWidth + 2, Height - 1), text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                x += textWidth;
            }
            if (chevron) Theme.Chevron(g, select ? Width - 21 : x + 7, Height / 2 - 2, kind == ButtonKind.Primary ? text : Palette.Muted);
        }
    }

    // A pill-shaped group of mutually exclusive choices.
    internal sealed class Segmented : Control
    {
        private string[] items = new string[0];
        private Color[] tints;
        private int selected = -1, hover = -1;
        public event EventHandler SelectedIndexChanged;

        public Segmented()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Height = 30;
            Cursor = Cursors.Hand;
            Margin = new Padding(0, 0, 6, 0);
        }

        public string[] Items
        {
            get { return items; }
            set { items = value ?? new string[0]; Size = GetPreferredSize(Size.Empty); Invalidate(); }
        }

        // Optional color per item for the selected state, e.g. amber for Acquisition.
        public Color[] Tints { get { return tints; } set { tints = value; Invalidate(); } }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value == selected) return;
                selected = value;
                Invalidate();
                EventHandler handler = SelectedIndexChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        private int SegmentWidth(int i)
        {
            return TextRenderer.MeasureText(items[i], Font, Size.Empty, TextFormatFlags.NoPrefix).Width + 24;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = 6;
            for (int i = 0; i < items.Length; i++) width += SegmentWidth(i);
            return new Size(width, 30);
        }

        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Size = GetPreferredSize(Size.Empty); }

        private Rectangle Segment(int index)
        {
            int x = 3;
            for (int i = 0; i < index; i++) x += SegmentWidth(i);
            return new Rectangle(x, 3, SegmentWidth(index), Height - 6);
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < items.Length; i++) if (Segment(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = HitTest(e.Location); if (h != hover) { hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int hit = HitTest(e.Location);
            if (e.Button == MouseButtons.Left && hit >= 0) SelectedIndex = hit;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (items.Length == 0) return;
            if (e.KeyCode == Keys.Left) SelectedIndex = Math.Max(0, selected - 1);
            else if (e.KeyCode == Keys.Right) SelectedIndex = Math.Min(items.Length - 1, selected + 1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.BackOf(this));
            Theme.FillRound(g, ClientRectangle, 6, Palette.Raised, Focused && ShowFocusCues ? Palette.Blend(Palette.Primary, Palette.Raised, 150) : Palette.Border);
            for (int i = 0; i < items.Length; i++)
            {
                Rectangle r = Segment(i);
                Color tint = tints != null && i < tints.Length ? tints[i] : Palette.Primary;
                Color text = !Enabled ? Palette.Faint : i == selected ? Palette.Text : i == hover ? Palette.Text : Palette.Muted;
                if (i == selected)
                {
                    Theme.FillRound(g, r, 4, Palette.Blend(tint, Palette.Raised, Enabled ? 46 : 20), Palette.Blend(tint, Palette.Raised, Enabled ? 140 : 50));
                    if (Enabled && tints != null) text = tint;
                }
                else if (i == hover && Enabled) Theme.FillRound(g, r, 4, Palette.Hover, Palette.Hover);
                TextRenderer.DrawText(g, items[i], Font, r, text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }
    }

    // A compact "− 10 +" number picker that replaces the system NumericUpDown.
    internal sealed class Stepper : Control
    {
        private int value, minimum, maximum = 100, hover = 0;
        public event EventHandler ValueChanged;

        public Stepper()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Size = new Size(92, 30);
            Margin = new Padding(0, 0, 6, 0);
        }

        public int Minimum { get { return minimum; } set { minimum = value; Value = Math.Max(this.value, value); } }
        public int Maximum { get { return maximum; } set { maximum = value; Value = Math.Min(this.value, value); } }

        public int Value
        {
            get { return value; }
            set
            {
                int next = Math.Max(minimum, Math.Min(maximum, value));
                if (next == this.value) return;
                this.value = next;
                Invalidate();
                EventHandler handler = ValueChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        private int Zone(Point p)
        {
            return p.X < 28 ? -1 : p.X >= Width - 28 ? 1 : 0;
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int z = Zone(e.Location); if (z != hover) { hover = z; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = 0; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Left) Value += Zone(e.Location);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Value += Math.Sign(e.Delta);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Right) Value++;
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Left) Value--;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.BackOf(this));
            Theme.FillRound(g, ClientRectangle, 5, Palette.Raised, Focused && ShowFocusCues ? Palette.Blend(Palette.Primary, Palette.Raised, 150) : Palette.Border);
            foreach (int side in new[] { -1, 1 })
            {
                var zone = new Rectangle(side < 0 ? 3 : Width - 27, 3, 24, Height - 6);
                bool active = side < 0 ? value > minimum : value < maximum;
                if (hover == side && active) Theme.FillRound(g, zone, 4, Palette.Hover, Palette.Hover);
                using (var pen = new Pen(active ? (hover == side ? Palette.Text : Palette.Muted) : Palette.Faint, 1.6f))
                {
                    float cx = zone.X + zone.Width / 2f, cy = zone.Y + zone.Height / 2f;
                    g.DrawLine(pen, cx - 4, cy, cx + 4, cy);
                    if (side > 0) g.DrawLine(pen, cx, cy - 4, cx, cy + 4);
                }
            }
            TextRenderer.DrawText(g, value.ToString(), Font, new Rectangle(28, 0, Width - 56, Height), Palette.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    // A rounded frame around a borderless TextBox, with a cyan border while focused.
    internal sealed class InputBox : Panel
    {
        public readonly TextBox Box;

        public InputBox(bool multiline)
        {
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Palette.Raised;
            Box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Palette.Raised, ForeColor = Palette.Text, Dock = DockStyle.Fill, Multiline = multiline };
            if (multiline) Box.ScrollBars = ScrollBars.Vertical;
            Controls.Add(Box);
            Height = 30;
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            Box.EnabledChanged += delegate { Box.BackColor = Palette.Raised; Invalidate(); };
            Cursor = Cursors.IBeam;
            Click += delegate { Box.Focus(); };
            Theme.DarkScrollBars(Box);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            int top = Box.Multiline ? 6 : Math.Max(2, (Height - Box.PreferredHeight) / 2 + 1);
            Padding = new Padding(9, top, Box.Multiline ? 3 : 9, Box.Multiline ? 4 : 0);
            base.OnLayout(levent);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.BackOf(this));
            Color border = !Enabled ? Palette.Blend(Palette.Border, Palette.Panel, 120) : Box.Focused ? Palette.Blend(Palette.Primary, Palette.Raised, 190) : Palette.Border;
            Theme.FillRound(e.Graphics, ClientRectangle, 5, Enabled ? Palette.Raised : Palette.Blend(Palette.Raised, Palette.Panel, 110), border);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Box.BackColor = Enabled ? Palette.Raised : Palette.Blend(Palette.Raised, Palette.Panel, 110);
            Invalidate();
        }
    }

    // A rounded surface. Optional HUD brackets and a status dot at the top left.
    internal class Card : Panel
    {
        private Color? dot;
        public bool Brackets { get; set; }

        public Card()
        {
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Palette.Panel;
            Padding = new Padding(14, 10, 14, 10);
            Margin = new Padding(0, 0, 0, 8);
        }

        public Color? Dot { get { return dot; } set { dot = value; Invalidate(); } }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.BackOf(this));
            Theme.FillRound(g, ClientRectangle, 8, Palette.Panel, Palette.Border);
            if (Brackets) Theme.Brackets(g, new Rectangle(4, 4, Width - 9, Height - 9), Color.FromArgb(150, Palette.Primary), 8);
            if (dot.HasValue)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float cx = 18, cy = Padding.Top + 10;
                using (var glow = new SolidBrush(Color.FromArgb(48, dot.Value))) g.FillEllipse(glow, cx - 7, cy - 7, 14, 14);
                using (var core = new SolidBrush(dot.Value)) g.FillEllipse(core, cx - 3.5f, cy - 3.5f, 7, 7);
            }
        }
    }

    internal sealed class ProgressLine : Control
    {
        private float fraction;

        public ProgressLine()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 4;
        }

        public float Fraction { get { return fraction; } set { fraction = Math.Max(0, Math.Min(1, value)); Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.BackOf(this));
            Theme.FillRound(e.Graphics, ClientRectangle, 2, Palette.Raised, Palette.Raised);
            int width = (int)Math.Round(Width * fraction);
            if (width > 0)
                Theme.FillRound(e.Graphics, new Rectangle(0, 0, Math.Max(width, Height), Height), 2,
                    fraction >= 1 ? Palette.Success : Palette.Primary, fraction >= 1 ? Palette.Success : Palette.Primary);
        }
    }
}
