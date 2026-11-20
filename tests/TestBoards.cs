using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;

namespace GlyphFinder
{
    internal sealed class TestBoard
    {
        internal static readonly Size CanvasSize = new Size(1080, 620);
        internal static readonly Rectangle TargetBox = new Rectangle(44, 109, 330, 390);
        internal static readonly Rectangle BoardBox = new Rectangle(481, 103, 522, 455);
        private static readonly Grid Board = new Grid(new RectangleF(476, 100, 532, 460), 10);
        private static readonly Color CanvasColor = Color.FromArgb(34, 39, 47);
        private static readonly Color CardColor = Color.FromArgb(25, 31, 43);
        private static readonly Color Frame = Color.FromArgb(36, 48, 61);
        private static readonly Color Border = Color.FromArgb(129, 144, 226);
        private static readonly Color Face = Color.FromArgb(56, 64, 103);
        private static readonly Color Ink = Color.FromArgb(218, 213, 255);
        private readonly Point[] shape;
        private readonly int[] glyphs;
        private readonly int[] sequence;
        private readonly bool variant;

        public int AnchorRow { get; private set; }
        public int AnchorColumn { get; private set; }
        public int TargetCells { get { return shape.Length; } }
        public const int CellCount = 100;

        public TestBoard(bool second)
        {
            variant = second;
            shape = second
                ? new[] { new Point(0, 0), new Point(0, 1), new Point(-1, 2), new Point(0, 2), new Point(1, 2) }
                : new[] { new Point(0, 0), new Point(2, 0), new Point(1, 1) };
            AnchorRow = second ? 4 : 5;
            AnchorColumn = 4;
            var rng = new Random(second ? 9458 : 817);
            glyphs = Enumerable.Range(0, CellCount).Select(i => rng.Next(6)).ToArray();
            var priority = new List<int> { BoardIndex(AnchorRow, AnchorColumn) };
            if (!second)
            {
                glyphs[BoardIndex(2, 2)] = 6;
                priority.Add(BoardIndex(2, 2));
            }
            for (int i = 0; i < shape.Length; i++)
            {
                Point offset = shape[i];
                int index = BoardIndex(AnchorRow + offset.Y, AnchorColumn + offset.X + offset.Y);
                glyphs[index] = 6 + i;
                if (i != 0) priority.Add(index);
            }
            if (!second) priority.Add(BoardIndex(2, 4));
            var randomOrder = Enumerable.Range(0, CellCount).Except(priority).OrderBy(i => rng.Next()).ToList();
            sequence = priority.Take(second ? 1 : 2)
                .Concat(randomOrder.Take(9))
                .Concat(priority.Skip(second ? 1 : 2))
                .Concat(randomOrder.Skip(9)).ToArray();
        }

        private static int BoardIndex(int row, int col) { return row * row + col; }

        public Bitmap Draw(int revealed)
        {
            var result = new Bitmap(CanvasSize.Width, CanvasSize.Height, PixelFormat.Format24bppRgb);
            var shown = new bool[CellCount];
            for (int i = 0; i < Math.Min(revealed, sequence.Length); i++) shown[sequence[i]] = true;
            using (Graphics g = Graphics.FromImage(result))
            using (var card = new SolidBrush(CardColor))
            using (var ink = new SolidBrush(Color.FromArgb(237, 239, 251)))
            using (var heading = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold))
            using (var label = new Font("Microsoft YaHei UI", 10f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(CanvasColor);
                g.FillRectangle(card, new Rectangle(26, 76, 369, 518));
                g.FillRectangle(card, new Rectangle(433, 76, 622, 518));
                g.DrawString("三角符号测试台", heading, ink, 26, 20);
                g.DrawString(variant ? "目标 B" : "目标 A", label, ink, 42, 84);
                g.DrawString("已揭开  " + revealed + " / " + CellCount, label, ink, 866, 30);
                float x0 = variant ? 207f : 149f;
                for (int i = 0; i < shape.Length; i++)
                {
                    Point p = shape[i];
                    DrawTile(g, new PointF(x0 + 51f * p.X, 143f + 88f * p.Y), 51, 88,
                        ((p.X + p.Y) & 1) != 0, 6 + i);
                }
                for (int row = 0; row < Board.Rows; row++)
                for (int c = 0; c <= row * 2; c++)
                {
                    int index = BoardIndex(row, c);
                    DrawTile(g, Board.CenterAtTop(row, c), Board.HalfWidth, Board.RowHeight,
                        (c & 1) != 0, shown[index] ? glyphs[index] : -1);
                }
            }
            return result;
        }

        private static void DrawTile(Graphics g, PointF start, float hw, float rh, bool down, int mark)
        {
            Func<float, float, PointF> p = (x, y) => new PointF(start.X + x * hw, start.Y + (down ? .96f - y : y) * rh);
            PointF[] outline = { p(0, .076f), p(.825f, .945f), p(-.825f, .945f) };
            if (mark < 0)
            {
                using (var faint = new Pen(Frame)) g.DrawPolygon(faint, outline);
                return;
            }
            using (var edge = new Pen(Border, Math.Max(1, rh * .026f)) { LineJoin = LineJoin.Round })
            using (var fill = new SolidBrush(Face))
            using (var ink = new SolidBrush(Ink))
            {
                g.FillPolygon(fill, outline);
                g.DrawPolygon(edge, outline);
                Action<float, float, float, float> rect = (x, y, width, height) =>
                {
                    PointF a = p(x, y), b = p(x + width, y + height);
                    g.FillRectangle(ink, a.X, Math.Min(a.Y, b.Y), b.X - a.X, Math.Abs(b.Y - a.Y));
                };
                switch (mark)
                {
                    case 0:
                        rect(-.085f, .40f, .17f, .39f);
                        break;
                    case 1:
                        rect(-.28f, .49f, .56f, .12f);
                        break;
                    case 2:
                        rect(-.38f, .73f, .76f, .10f);
                        break;
                    case 3:
                        rect(-.32f, .61f, .19f, .20f);
                        rect(.13f, .61f, .19f, .20f);
                        break;
                    case 4:
                        g.FillPolygon(ink, new[] { p(0, .42f), p(-.30f, .66f), p(0, .83f), p(.30f, .66f) });
                        break;
                    case 5:
                        rect(-.27f, .51f, .14f, .28f);
                        rect(.13f, .51f, .14f, .28f);
                        break;
                    case 6:
                        foreach (float y in new[] { .46f, .68f })
                        foreach (float x in new[] { -.24f, .08f })
                            rect(x, y, .16f, .13f);
                        break;
                    case 7:
                        g.FillPolygon(ink, new[] { p(0, .33f), p(-.38f, .81f), p(.38f, .81f) });
                        break;
                    case 8:
                        g.FillPolygon(ink, new[] { p(-.29f, .53f), p(.29f, .53f), p(0, .84f) });
                        break;
                    case 9:
                        rect(-.10f, .36f, .20f, .43f);
                        rect(-.34f, .58f, .68f, .12f);
                        break;
                    default:
                        g.FillPolygon(ink, new[] { p(.04f, .37f), p(.28f, .51f), p(-.32f, .80f), p(-.43f, .70f) });
                        break;
                }
            }
        }
    }
}
