using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace GlyphFinder
{
    internal sealed class PatternCell
    {
        public int Row, Column;
        public Glyph Signature;
    }

    internal sealed class TargetPattern
    {
        public bool AnchorDown;
        public readonly List<PatternCell> Cells = new List<PatternCell>();

        private sealed class Tile
        {
            public Rectangle Box;
            public bool Down;
            public double X { get { return Box.Left + (Box.Width - 1) * .5; } }
            public double LatticeTop(double height) { return Box.Top + (Down ? .01 : -.078) * height; }
        }

        private sealed class Placement
        {
            public Tile Tile;
            public int Y, X;
            public double Error;
        }

        private sealed class AxisFit
        {
            public double Origin, Pitch;
        }

        public static TargetPattern Read(Pixels image, double tileAspect)
        {
            int background = BackgroundLevel(image);
            int size = image.Width * image.Height;
            var mask = new byte[size];
            var queue = new int[size];
            var best = new TargetPattern();
            double bestCoverage = 0;
            foreach (int increment in new[] { 13, 24, 39, 62 })
            {
                var tiles = Components(image, background + increment, tileAspect, mask, queue);
                foreach (Tile seed in tiles)
                {
                    // Rounded game borders occupy about 84% of the width and 87% of the row pitch.
                    double hw = seed.Box.Width / 1.68;
                    double rh = seed.Box.Height / .87;
                    var positions = new Dictionary<Point, Placement>();
                    foreach (Tile tile in tiles)
                    {
                        if (Math.Abs(tile.Box.Width - seed.Box.Width) > Math.Max(3, .14 * seed.Box.Width) ||
                            Math.Abs(tile.Box.Height - seed.Box.Height) > Math.Max(3, .14 * seed.Box.Height))
                            continue;
                        double x = (tile.X - seed.X) / hw;
                        double y = (tile.LatticeTop(rh) - seed.LatticeTop(rh)) / rh;
                        int dx = (int)Math.Round(x);
                        int dy = (int)Math.Round(y);
                        double error = Math.Abs(x - dx) + Math.Abs(y - dy);
                        bool down = seed.Down ^ (((dx + dy) & 1) != 0);
                        if (error > .36 || down != tile.Down) continue;
                        var key = new Point(dx, dy);
                        Placement previous;
                        if (positions.TryGetValue(key, out previous) && previous.Error <= error) continue;
                        positions[key] = new Placement { Tile = tile, X = dx, Y = dy, Error = error };
                    }
                    double coverage = seed.Box.Width * seed.Box.Height * (double)positions.Count;
                    if (coverage <= bestCoverage) continue;
                    var pattern = BuildPattern(image, positions.Values.ToList(), hw, rh);
                    double usableCoverage = seed.Box.Width * seed.Box.Height * (double)pattern.Cells.Count;
                    if (usableCoverage <= bestCoverage) continue;
                    best = pattern;
                    bestCoverage = usableCoverage;
                }
            }
            return best;
        }

        public static Glyph[] ReadBoard(Pixels image, Grid grid)
        {
            int size = image.Width * image.Height;
            double hw = grid.HalfWidth, rh = grid.RowHeight;
            var tiles = Components(image, BackgroundLevel(image) + 24, 2 * hw / rh, new byte[size], new int[size]);
            var glyphs = new Glyph[grid.Rows * grid.Rows];
            foreach (Tile tile in tiles)
            {
                if (tile.Box.Width < 1.40 * hw || tile.Box.Width > 2.0 * hw ||
                    tile.Box.Height < .70 * rh || tile.Box.Height > 1.04 * rh) continue;
                double y = (tile.LatticeTop(rh) - grid.Bounds.Top) / rh;
                int row = (int)Math.Round(y);
                if (row < 0 || row >= grid.Rows || Math.Abs(y - row) > .35) continue;
                double x = (tile.X - grid.CenterAtTop(row, 0).X) / hw;
                int col = (int)Math.Round(x);
                if (col < 0 || col > 2 * row || Math.Abs(x - col) > .40 || ((col & 1) != 0) != tile.Down) continue;
                Glyph signature = Signature(image, tile, hw, rh);
                if (signature.Visible) glyphs[row * row + col] = signature;
            }
            for (int row = 0; row < grid.Rows; row++)
            for (int col = 0; col <= row * 2; col++)
            {
                int index = row * row + col;
                if (glyphs[index] == null) glyphs[index] = new Glyph(image, grid, row, col);
            }
            return glyphs;
        }

        public static Grid FitBoardGrid(Pixels image, Grid seed)
        {
            int size = image.Width * image.Height;
            double hw = seed.HalfWidth, rh = seed.RowHeight;
            List<Tile> tiles = Components(image, BackgroundLevel(image) + 24, 2 * hw / rh,
                new byte[size], new int[size]).Where(tile =>
                    tile.Box.Width >= 1.25 * hw && tile.Box.Width <= 2.10 * hw &&
                    tile.Box.Height >= .61 * rh && tile.Box.Height <= 1.10 * rh).ToList();
            if (tiles.Count < 6) return null;

            AxisFit vertical = FitBoardAxis(tiles, seed.Rows, seed.Bounds.Top, rh, null);
            if (vertical == null) return null;
            AxisFit horizontal = FitBoardAxis(tiles, seed.Rows, seed.CenterAtTop(0, 0).X, hw, vertical);
            if (horizontal == null) return null;
            float half = (float)horizontal.Pitch * seed.Rows;
            return new Grid(new RectangleF((float)horizontal.Origin - half, (float)vertical.Origin,
                2 * half, (float)vertical.Pitch * seed.Rows), seed.Rows);
        }

        private static AxisFit FitBoardAxis(List<Tile> tiles, int rows, double origin, double nominalPitch, AxisFit vertical)
        {
            var best = new AxisFit { Origin = origin, Pitch = nominalPitch };
            double bestScore = 0;
            bool horizontal = vertical != null;
            for (int scale = -22; scale <= 13; scale++)
            for (int shift = -18; shift <= 18; shift++)
            {
                double pitch = nominalPitch * (1 + .008 * scale);
                double offset = origin + nominalPitch * .046 * shift;
                double score = 0;
                foreach (Tile tile in tiles)
                {
                    double index = BoardIndex(tile, rows, offset, pitch, vertical);
                    score += Math.Max(0, 1 - Math.Abs(index - Math.Round(index)) / .23);
                }
                score -= tiles.Count * (Math.Abs(shift) * .003 + Math.Abs(scale) * .002);
                if (score <= bestScore) continue;
                bestScore = score;
                best.Origin = offset;
                best.Pitch = pitch;
            }

            var indices = new List<double>();
            var positions = new List<double>();
            foreach (Tile tile in tiles)
            {
                double index = BoardIndex(tile, rows, best.Origin, best.Pitch, vertical);
                double snapped = Math.Round(index);
                if (Math.Abs(index - snapped) > .18) continue;
                indices.Add(snapped);
                positions.Add(horizontal ? tile.X : tile.LatticeTop(best.Pitch));
            }
            if (indices.Count < Math.Max(6, tiles.Count * .70) ||
                indices.Max() - indices.Min() < (horizontal ? 4 : 3)) return null;
            best.Pitch = FitPitch(indices, positions, best.Pitch);
            best.Origin = positions.Average() - indices.Average() * best.Pitch;
            return best;
        }

        private static double BoardIndex(Tile tile, int rows, double offset, double pitch, AxisFit vertical)
        {
            if (vertical == null)
            {
                double index = (tile.LatticeTop(pitch) - offset) / pitch;
                return index >= -.23 && index <= rows - 1 + .23 ? index : .5;
            }
            double rowPosition = (tile.LatticeTop(vertical.Pitch) - vertical.Origin) / vertical.Pitch;
            int row = (int)Math.Round(rowPosition);
            if (row < 0 || row >= rows || Math.Abs(rowPosition - row) > .23) return .5;
            double position = (tile.X - offset) / pitch;
            int column = row + (int)Math.Round(position);
            return column >= 0 && column <= 2 * row && ((column & 1) != 0) == tile.Down ? position : .5;
        }

        private static TargetPattern BuildPattern(Pixels image, List<Placement> tiles, double hw, double rh)
        {
            hw = FitPitch(tiles.Select(p => (double)p.X), tiles.Select(p => p.Tile.X), hw);
            rh = FitPitch(tiles.Select(p => (double)p.Y), tiles.Select(p => p.Tile.LatticeTop(rh)), rh);
            var pattern = new TargetPattern();
            int anchorX = 0, anchorY = 0;
            foreach (Placement placed in tiles.OrderBy(p => p.Y).ThenBy(p => p.X))
            {
                Tile tile = placed.Tile;
                Glyph glyph = Signature(image, tile, hw, rh);
                if (!glyph.Visible) continue;
                if (pattern.Cells.Count == 0)
                {
                    anchorX = placed.X;
                    anchorY = placed.Y;
                    pattern.AnchorDown = tile.Down;
                }
                int row = placed.Y - anchorY;
                pattern.Cells.Add(new PatternCell
                {
                    Row = row,
                    Column = placed.X - anchorX + row,
                    Signature = glyph
                });
            }
            return pattern;
        }

        private static Glyph Signature(Pixels image, Tile tile, double hw, double rh)
        {
            var top = new PointF((float)tile.X, (float)tile.LatticeTop(rh));
            return new Glyph(image, top, hw, rh, tile.Down);
        }

        private static double FitPitch(IEnumerable<double> indices, IEnumerable<double> coordinates, double seed)
        {
            double[] index = indices.ToArray();
            double[] position = coordinates.ToArray();
            double meanIndex = index.Average();
            double meanPosition = position.Average();
            double product = 0, square = 0;
            for (int i = 0; i < index.Length; i++)
            {
                double offset = index[i] - meanIndex;
                product += offset * (position[i] - meanPosition);
                square += offset * offset;
            }
            return square == 0 ? seed : Math.Max(seed * .9, Math.Min(seed * 1.10, product / square));
        }

        private static int BackgroundLevel(Pixels image)
        {
            var histogram = new int[256];
            for (int y = 0; y < image.Height; y += 2)
            for (int x = 0; x < image.Width; x += 2)
                histogram[image.Green(x, y)]++;
            int quantile = ((image.Width + 1) / 2) * ((image.Height + 1) / 2) / 5;
            int total = 0;
            for (int i = 0; i < histogram.Length; i++)
            {
                total += histogram[i];
                if (total > quantile) return i;
            }
            return 0;
        }

        private static List<Tile> Components(Pixels image, int threshold, double aspect, byte[] mask, int[] queue)
        {
            int w = image.Width, h = image.Height;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                mask[y * w + x] = image.Green(x, y) >= threshold ? (byte)1 : (byte)0;

            var tiles = new List<Tile>();
            for (int p = 0; p < mask.Length; p++)
            {
                if (mask[p] == 0) continue;
                int head = 0, tail = 0;
                Add(mask, queue, p, ref tail);
                int left = w, right = -1, top = h, bottom = -1;
                while (head < tail)
                {
                    int pos = queue[head++];
                    int x = pos % w, y = pos / w;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                    if (x > 0) Add(mask, queue, pos - 1, ref tail);
                    if (x + 1 < w) Add(mask, queue, pos + 1, ref tail);
                    if (y > 0) Add(mask, queue, pos - w, ref tail);
                    if (y + 1 < h) Add(mask, queue, pos + w, ref tail);
                }
                var box = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
                if (box.Width < 12 || box.Height < 11) continue;
                double ratio = box.Width / (double)box.Height;
                double density = tail / (double)(box.Width * box.Height);
                if (ratio < aspect * .70 || ratio > aspect * 1.35 || density < .09 || density > .74) continue;
                int narrowLeft = w, narrowRight = -1, broadLeft = w, broadRight = -1;
                int band = Math.Max(2, (int)(box.Height * .23));
                for (int i = 0; i < tail; i++)
                {
                    int x = queue[i] % w, y = queue[i] / w;
                    if (y < top + band)
                    {
                        narrowLeft = Math.Min(narrowLeft, x);
                        narrowRight = Math.Max(narrowRight, x);
                    }
                    if (y > bottom - band)
                    {
                        broadLeft = Math.Min(broadLeft, x);
                        broadRight = Math.Max(broadRight, x);
                    }
                }
                double topSpan = (narrowRight - narrowLeft + 1) / (double)box.Width;
                double bottomSpan = (broadRight - broadLeft + 1) / (double)box.Width;
                if (Math.Max(topSpan, bottomSpan) < .75 || Math.Min(topSpan, bottomSpan) > .57 ||
                    Math.Abs(topSpan - bottomSpan) < .30) continue;
                tiles.Add(new Tile { Box = box, Down = topSpan > bottomSpan });
            }
            return tiles;
        }

        private static void Add(byte[] mask, int[] queue, int p, ref int tail)
        {
            if (mask[p] == 0) return;
            mask[p] = 0;
            queue[tail++] = p;
        }
    }
}
