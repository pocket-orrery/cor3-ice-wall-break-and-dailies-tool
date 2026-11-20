using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GlyphFinder
{
    internal sealed class Pixels
    {
        private readonly byte[] data;
        private readonly int width;
        private readonly int height;
        private readonly int stride;
        public int Width { get { return width; } }
        public int Height { get { return height; } }

        public Pixels(Bitmap bitmap)
        {
            width = bitmap.Width;
            height = bitmap.Height;
            var bounds = new Rectangle(0, 0, width, height);
            using (var rgb = bitmap.Clone(bounds, PixelFormat.Format32bppArgb))
            {
                BitmapData locked = rgb.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    stride = Math.Abs(locked.Stride);
                    data = new byte[stride * height];
                    for (int y = 0; y < height; y++)
                        Marshal.Copy(IntPtr.Add(locked.Scan0, y * locked.Stride), data, y * stride, stride);
                }
                finally { rgb.UnlockBits(locked); }
            }
        }

        public double At(double x, double y)
        {
            if (x < 0 || y < 0 || x >= width - 1 || y >= height - 1) return 0;
            int ix = (int)x;
            int iy = (int)y;
            double dx = x - ix;
            double dy = y - iy;
            int p = iy * stride + ix * 4;
            double top = data[p + 1] * (1 - dx) + data[p + 5] * dx;
            double bottom = data[p + stride + 1] * (1 - dx) + data[p + stride + 5] * dx;
            return top * (1 - dy) + bottom * dy;
        }

        public byte Green(int x, int y) { return data[y * stride + x * 4 + 1]; }

        public bool SameGreen(Pixels other)
        {
            if (other == null || width != other.width || height != other.height) return false;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (Green(x, y) != other.Green(x, y)) return false;
            return true;
        }
    }

    internal sealed class Grid
    {
        public readonly RectangleF Bounds;
        public readonly int Rows;
        public float HalfWidth { get { return Bounds.Width / (2 * Rows); } }
        public float RowHeight { get { return Bounds.Height / Rows; } }

        public Grid(RectangleF bounds, int rows) { Bounds = bounds; Rows = rows; }

        public PointF CenterAtTop(int row, int col)
        {
            return new PointF(Bounds.Left + Bounds.Width / 2 + (col - row) * HalfWidth,
                              Bounds.Top + row * RowHeight);
        }

        public PointF[] Outline(int row, int col)
        {
            PointF top = CenterAtTop(row, col);
            bool down = (col & 1) != 0;
            float tipY = top.Y + (down ? .965f : .035f) * RowHeight;
            float baseY = top.Y + (down ? .035f : .965f) * RowHeight;
            return new[] { new PointF(top.X, tipY), new PointF(top.X + .93f * HalfWidth, baseY),
                               new PointF(top.X - .93f * HalfWidth, baseY) };
        }

        public static Grid Fit(Pixels pixels, RectangleF selected, int rows)
        {
            // The visible triangles have rounded corners and small gutters between cells.
            selected.Inflate(selected.Width / rows * .10f, selected.Height / rows * .065f);
            var seed = new Grid(selected, rows);
            Grid fromTiles = TargetPattern.FitBoardGrid(pixels, seed);
            if (fromTiles != null) return fromTiles;
            var best = FitAxis(pixels, seed, false, 4, seed.RowHeight * .065f);
            best = FitAxis(pixels, best, true, 5, seed.HalfWidth * .10f);
            best = FitAxis(pixels, best, false, 2, seed.RowHeight * .019f);
            return FitAxis(pixels, best, true, 2, seed.HalfWidth * .024f);
        }

        private static Grid FitAxis(Pixels pixels, Grid seed, bool horizontal, int radius, float step)
        {
            var best = seed;
            double score = EdgeScore(pixels, seed, horizontal);
            for (int start = -radius; start <= radius; start++)
            for (int end = -radius; end <= radius; end++)
            {
                if (start == 0 && end == 0) continue;
                RectangleF bounds = seed.Bounds;
                if (horizontal)
                    bounds = RectangleF.FromLTRB(bounds.Left + start * step, bounds.Top, bounds.Right + end * step, bounds.Bottom);
                else
                    bounds = RectangleF.FromLTRB(bounds.Left, bounds.Top + start * step, bounds.Right, bounds.Bottom + end * step);
                var trial = new Grid(bounds, seed.Rows);
                double trialScore = EdgeScore(pixels, trial, horizontal)
                    - (Math.Abs(start) + Math.Abs(end)) * seed.Rows * seed.Rows * 3.4;
                if (trialScore <= score) continue;
                score = trialScore;
                best = trial;
            }
            return best;
        }

        private static double EdgeScore(Pixels image, Grid grid, bool sides)
        {
            double score = 0;
            double hw = grid.HalfWidth;
            double rh = grid.RowHeight;
            for (int row = 0; row < grid.Rows; row++)
            for (int col = 0; col <= 2 * row; col += 2)
            {
                var top = grid.CenterAtTop(row, col);
                for (int i = 0; i < 5; i++)
                {
                    double u = .32 + .084 * i;
                    if (sides)
                    {
                        double y = top.Y + rh * u;
                        double xRight = top.X + hw * (u + .045);
                        double xLeft = top.X - hw * (u + .045);
                        score += image.At(xRight - .13 * hw, y) - image.At(xRight, y);
                        score += image.At(xLeft + .13 * hw, y) - image.At(xLeft, y);
                    }
                    else
                    {
                        double x = top.X + hw * (u - .49) * 3;
                        double y = top.Y + rh * .952;
                        score += image.At(x, y - .07 * rh) - image.At(x, y);
                    }
                }
            }
            return score;
        }
    }

    internal sealed class Glyph
    {
        private static readonly PointF[] Sites = MakeSites();
        private static readonly int[][] Shifts = MakeShifts();
        private readonly double[] values;
        private readonly double ink;
        public readonly bool Visible;
        public readonly double Contrast;

        private static PointF[] MakeSites()
        {
            var points = new List<PointF>();
            for (int row = 0; row <= 20; row++)
            {
                float y = .23f + row * .031f;
                for (int col = -16; col <= 16; col++)
                {
                    float x = col * .048f;
                    if (Math.Abs(x) <= y - .20f)
                        points.Add(new PointF(x, y));
                }
            }
            return points.ToArray();
        }

        private static int[][] MakeShifts()
        {
            var coordinates = new Point[Sites.Length];
            var indices = new Dictionary<Point, int>();
            for (int i = 0; i < Sites.Length; i++)
            {
                var point = new Point((int)Math.Round(Sites[i].X / .048), (int)Math.Round((Sites[i].Y - .23) / .031));
                coordinates[i] = point;
                indices[point] = i;
            }
            var shifts = new List<int[]>();
            for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0) continue;
                var map = new int[Sites.Length];
                for (int i = 0; i < map.Length; i++)
                {
                    int index;
                    var shifted = new Point(coordinates[i].X + x, coordinates[i].Y + y);
                    map[i] = indices.TryGetValue(shifted, out index) ? index : -1;
                }
                shifts.Add(map);
            }
            return shifts.ToArray();
        }

        public Glyph(Pixels image, Grid grid, int row, int col)
            : this(image, grid.CenterAtTop(row, col), grid.HalfWidth, grid.RowHeight, (col & 1) == 1) { }

        public Glyph(Pixels image, PointF top, double hw, double rh, bool down)
        {
            values = new double[Sites.Length];
            for (int i = 0; i < Sites.Length; i++)
            {
                PointF p = Sites[i];
                double x = top.X + p.X * hw;
                double y = top.Y + (down ? 1 - p.Y : p.Y) * rh;
                values[i] = (2 * image.At(x, y) + image.At(x - .045 * hw, y) + image.At(x + .045 * hw, y) +
                             image.At(x, y - .033 * rh) + image.At(x, y + .033 * rh)) / 6;
            }

            var sorted = (double[])values.Clone();
            Array.Sort(sorted);
            double low = sorted[sorted.Length / 5];
            double high = sorted[sorted.Length * 9 / 10];
            Contrast = high - low;
            Visible = Contrast >= 21 && high >= 56;
            if (!Visible) return;

            double cut = low + Contrast * .18;
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = Math.Min(1, Math.Max(0, (values[i] - cut) / (high - cut)));
                ink += values[i];
            }
        }

        public double Similarity(Glyph other)
        {
            double shared = 0;
            for (int i = 0; i < values.Length; i++)
                shared += Math.Min(values[i], other.values[i]);
            double total = ink + other.ink;
            if (total == 0) return 0;
            double overlap = shared / (total - shared);
            if (overlap >= .69 || overlap < .46) return overlap;

            // Allow a single sample of misalignment from rounding at different display scales.
            foreach (int[] shift in Shifts)
            {
                shared = 0;
                for (int i = 0; i < shift.Length; i++)
                    if (shift[i] >= 0) shared += Math.Min(values[i], other.values[shift[i]]);
                overlap = Math.Max(overlap, shared / (total - shared) - .025);
            }
            return overlap;
        }
    }

    internal sealed class Candidate
    {
        public int Row, Column, Confirmed, Conflicts, Unknown, Uncertain;
    }

    internal sealed class Matches
    {
        public int TargetCount, BoardCount;
        public readonly List<Candidate> Ranked = new List<Candidate>();
    }

    internal static class Matcher
    {
        public static Matches Find(TargetPattern pattern, Pixels board, Grid boardGrid)
        {
            var result = new Matches { TargetCount = pattern.Cells.Count };
            int boardRows = boardGrid.Rows;
            Glyph[] found = TargetPattern.ReadBoard(board, boardGrid);
            foreach (Glyph glyph in found)
                if (glyph.Visible) result.BoardCount++;
            if (result.TargetCount == 0) return result;

            for (int row = 0; row < boardRows; row++)
            for (int col = pattern.AnchorDown ? 1 : 0; col <= 2 * row; col += 2)
            {
                var candidate = new Candidate { Row = row, Column = col };
                if (Score(candidate, pattern.Cells, found, boardRows))
                    result.Ranked.Add(candidate);
            }
            result.Ranked.Sort(Compare);
            return result;
        }

        private static bool Score(Candidate candidate, List<PatternCell> wanted, Glyph[] found, int boardRows)
        {
            foreach (PatternCell cell in wanted)
            {
                int atRow = candidate.Row + cell.Row;
                int atCol = candidate.Column + cell.Column;
                if (atRow < 0 || atRow >= boardRows || atCol < 0 || atCol > 2 * atRow) return false;
                Glyph actual = found[atRow * atRow + atCol];
                if (!actual.Visible)
                {
                    candidate.Unknown++;
                    continue;
                }
                double similarity = cell.Signature.Similarity(actual);
                if (similarity >= .69) candidate.Confirmed++;
                else if (similarity <= .56) candidate.Conflicts++;
                else candidate.Uncertain++;
            }
            return true;
        }

        private static int Compare(Candidate a, Candidate b)
        {
            int cmp = a.Conflicts.CompareTo(b.Conflicts);
            if (cmp == 0) cmp = b.Confirmed.CompareTo(a.Confirmed);
            if (cmp == 0) cmp = a.Row.CompareTo(b.Row);
            return cmp == 0 ? a.Column.CompareTo(b.Column) : cmp;
        }
    }
}
