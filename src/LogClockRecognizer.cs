using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;

namespace GlyphFinder
{
    // Prefers glyphs OCR already read on the same screen.
    internal sealed class LogClockRecognizer
    {
        private const string ClockPattern = "[00:00:00]";
        private const int SampleWidth = 8, SampleHeight = 12;
        private const float DigitDistance = .055f, PunctuationDistance = .025f, MinimumMargin = .014f;
        private static readonly Lazy<List<Glyph[]>> ReferenceDigits = new Lazy<List<Glyph[]>>(DrawReferenceDigits);

        private sealed class Glyph
        {
            public readonly float[] Ink;
            public readonly float Aspect;

            public Glyph(float[] ink, float aspect)
            {
                Ink = ink;
                Aspect = aspect;
            }

            public float At(int x, int y)
            {
                return x >= 0 && x < SampleWidth && y >= 0 && y < SampleHeight ? Ink[y * SampleWidth + x] : 0;
            }
        }

        private readonly Bitmap image;
        private readonly Dictionary<char, List<Glyph>> examples = new Dictionary<char, List<Glyph>>();

        public LogClockRecognizer(Bitmap image)
        {
            this.image = image;
        }

        public void Learn(Rectangle clockArea, string time, Rectangle bodyArea, string body, float clockPitch = 0)
        {
            if (time != null && time.Length == ClockPattern.Length)
                LearnGlyphs(clockArea, time, true, clockPitch);
            LearnGlyphs(bodyArea, string.Concat(body.Where(c => !char.IsWhiteSpace(c))), false);
        }

        private void LearnGlyphs(Rectangle area, string text, bool includePunctuation, float pitch = 0)
        {
            List<Glyph> glyphs = ReadGlyphs(area, pitch);
            if (glyphs.Count != text.Length) return;
            for (int i = 0; i < glyphs.Count; i++)
            {
                char label = text[i];
                if (!char.IsDigit(label) && !includePunctuation) continue;
                List<Glyph> templates;
                if (!examples.TryGetValue(label, out templates))
                {
                    templates = new List<Glyph>();
                    examples.Add(label, templates);
                }
                templates.Add(glyphs[i]);
            }
        }

        public string Read(Rectangle clockArea, float pitch = 0)
        {
            List<Glyph> glyphs = ReadGlyphs(clockArea, pitch);
            if (glyphs.Count != ClockPattern.Length || examples.Count < 2) return null;

            char[] result = new char[ClockPattern.Length];
            for (int i = 0; i < glyphs.Count; i++)
            {
                Glyph glyph = glyphs[i];
                var closest = examples.Select(pair => new { Label = pair.Key, Distance = pair.Value.Min(example => Distance(glyph, example)) })
                    .OrderBy(pair => pair.Distance).Take(2).ToArray();
                char label = closest[0].Label;
                char expected = ClockPattern[i];
                bool digit = expected == '0';
                bool certain = (digit ? char.IsDigit(label) : label == expected) &&
                    closest[0].Distance <= (digit ? DigitDistance : PunctuationDistance) &&
                    closest[1].Distance - closest[0].Distance >= MinimumMargin;
                if (certain) result[i] = label;
                else if (digit && closest[0].Distance > DigitDistance)
                {
                    char? reference = ReadUnseenDigit(glyph, closest[0].Distance);
                    if (!reference.HasValue) return null;
                    result[i] = reference.Value;
                }
                else return null;
            }
            return new string(result);
        }

        // Check the punctuation before reading the six digits in isolation. A screenshot
        // with different punctuation must still be checked against the game's time format.
        public bool HasClockPunctuation(Rectangle area, float pitch)
        {
            List<Glyph> glyphs = ReadGlyphs(area, pitch);
            return glyphs.Count == ClockPattern.Length &&
                IsBracket(glyphs[0], 0) && IsBracket(glyphs[9], SampleWidth - 1) &&
                IsColon(glyphs[3]) && IsColon(glyphs[6]);
        }

        private static bool IsBracket(Glyph glyph, int side)
        {
            if (glyph.Aspect > .58f) return false;
            int other = SampleWidth - 1 - side;
            float spine = Enumerable.Range(2, 8).Average(y => glyph.At(side, y));
            float opening = Enumerable.Range(3, 6).Average(y => glyph.At(other, y));
            float edges = Math.Min(Enumerable.Range(0, 3).Max(y => glyph.At(other, y)),
                Enumerable.Range(SampleHeight - 3, 3).Max(y => glyph.At(other, y)));
            return spine > .25f && opening < spine * .7f && edges > opening + .08f;
        }

        private static bool IsColon(Glyph glyph)
        {
            if (glyph.Aspect > .65f) return false;
            var rows = Enumerable.Range(0, SampleHeight)
                .Select(y => Enumerable.Range(0, SampleWidth).Max(x => glyph.At(x, y))).ToArray();
            return rows.Take(4).Max() > .45f && rows.Skip(8).Max() > .45f &&
                rows.Skip(4).Take(4).Min() < .35f;
        }

        private char? ReadUnseenDigit(Glyph glyph, float screenDistance)
        {
            // Only accept an unseen digit if two different fonts agree and both are closer than any screen sample.
            if (ReferenceDigits.Value.Count < 2) return null;
            char? match = null;
            foreach (Glyph[] set in ReferenceDigits.Value)
            {
                var closest = set.Select((sample, digit) => new { Label = (char)('0' + digit), Distance = Distance(glyph, sample) })
                    .OrderBy(pair => pair.Distance).Take(2).ToArray();
                char label = closest[0].Label;
                if (examples.ContainsKey(label) || closest[0].Distance > .09f ||
                    closest[0].Distance >= screenDistance ||
                    closest[1].Distance - closest[0].Distance < .025f || (match.HasValue && match.Value != label)) return null;
                match = label;
            }
            return match;
        }

        private static List<Glyph[]> DrawReferenceDigits()
        {
            const int cell = 48;
            var sets = new List<Glyph[]>();
            foreach (string family in new[] { "Segoe UI", "Microsoft Sans Serif" })
            using (var font = new Font(family, 24, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                if (font.Name != family) continue;
                using (var bitmap = new Bitmap(10 * cell, cell))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                using (StringFormat format = StringFormat.GenericTypographic)
                {
                    graphics.Clear(Color.FromArgb(16, 22, 26));
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    for (int digit = 0; digit < 10; digit++)
                        graphics.DrawString(digit.ToString(CultureInfo.InvariantCulture), font, Brushes.LightGray, digit * cell + 4, 4, format);
                    graphics.Flush();
                    var sampler = new LogClockRecognizer(bitmap);
                    Glyph[] set = Enumerable.Range(0, 10)
                        .Select(digit => sampler.ReadGlyphs(new Rectangle(digit * cell, 0, cell, cell)).FirstOrDefault()).ToArray();
                    if (set.All(glyph => glyph != null)) sets.Add(set);
                }
            }
            return sets;
        }

        private List<Glyph> ReadGlyphs(Rectangle area, float pitch = 0)
        {
            var glyphs = new List<Glyph>();
            if (area.Width <= 0 || area.Height <= 0) return glyphs;

            var pixels = new byte[area.Height, area.Width];
            var histogram = new int[256];
            var rowBackground = new int[area.Height];
            for (int y = 0; y < area.Height; y++)
            {
                int darkest = 255;
                for (int x = 0; x < area.Width; x++)
                {
                    Color pixel = image.GetPixel(area.X + x, area.Y + y);
                    byte brightness = Math.Max(pixel.B, Math.Max(pixel.R, pixel.G));
                    pixels[y, x] = brightness;
                    histogram[brightness]++;
                    darkest = Math.Min(darkest, brightness);
                }
                rowBackground[y] = darkest;
            }
            int count = area.Width * area.Height;
            int background = Percentile(histogram, count / 2);
            int bright = Percentile(histogram, count * 95 / 100);
            int threshold = background + (pitch > 0 ? 18 : Math.Max(14, (bright - background) / 4));

            // Keep a bright scanline from joining adjacent characters along the entire row.
            for (int y = 0; y < area.Height; y++)
            {
                int reduction = rowBackground[y] + 14 - threshold;
                if (reduction <= 0) continue;
                for (int x = 0; x < area.Width; x++)
                    pixels[y, x] = (byte)Math.Max(0, pixels[y, x] - reduction);
            }

            if (pitch > 0)
            {
                // At small sizes, antialiased edges sometimes touch between characters.
                var boundary = new int[ClockPattern.Length + 1];
                for (int index = 0; index < boundary.Length; index++)
                {
                    int nominal = Math.Min(area.Width, (int)Math.Round(index * pitch));
                    boundary[index] = nominal;
                    if (index == 0 || index == ClockPattern.Length) continue;
                    int bestInk = int.MaxValue;
                    for (int x = Math.Max(1, nominal - 1); x <= Math.Min(area.Width - 1, nominal + 2); x++)
                    {
                        int ink = 0;
                        for (int y = 0; y < area.Height; y++)
                            ink += Math.Max(0, pixels[y, x - 1] - threshold) + Math.Max(0, pixels[y, x] - threshold);
                        if (ink > bestInk || (ink == bestInk && Math.Abs(x - nominal) >= Math.Abs(boundary[index] - nominal))) continue;
                        bestInk = ink;
                        boundary[index] = x;
                    }
                }
                for (int index = 0; index < ClockPattern.Length; index++)
                {
                    int start = boundary[index], end = boundary[index + 1];
                    int x0 = end, x1 = start, y0 = area.Height, y1 = -1;
                    for (int y = 0; y < area.Height; y++)
                    for (int x = start; x < end; x++)
                    {
                        if (pixels[y, x] < threshold) continue;
                        x0 = Math.Min(x0, x);
                        x1 = Math.Max(x1, x);
                        y0 = Math.Min(y0, y);
                        y1 = Math.Max(y1, y);
                    }
                    if (y1 < y0) break;
                    glyphs.Add(Sample(pixels, threshold, Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1)));
                }
                return glyphs;
            }

            // The monospace characters have empty columns between them, even inside a timestamp.
            int left = -1, top = area.Height, bottom = -1;
            for (int x = 0; x <= area.Width; x++)
            {
                bool hasInk = false;
                if (x < area.Width)
                for (int y = 0; y < area.Height; y++)
                {
                    if (pixels[y, x] < threshold) continue;
                    hasInk = true;
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
                if (hasInk && left < 0) left = x;
                if (!hasInk && left >= 0)
                {
                    var bounds = Rectangle.FromLTRB(left, top, x, bottom + 1);
                    glyphs.Add(Sample(pixels, threshold, bounds));
                    left = -1;
                    top = area.Height;
                    bottom = -1;
                }
            }
            return glyphs;
        }

        private static int Percentile(int[] histogram, int rank)
        {
            for (int value = 0; value < histogram.Length; value++)
            {
                rank -= histogram[value];
                if (rank < 0) return Math.Max(0, value - 1);
            }
            return histogram.Length - 1;
        }

        private static Glyph Sample(byte[,] pixels, int threshold, Rectangle bounds)
        {
            int peak = 0;
            for (int y = bounds.Top; y < bounds.Bottom; y++)
            for (int x = bounds.Left; x < bounds.Right; x++)
                peak = Math.Max(peak, pixels[y, x]);

            int floor = threshold - 12;
            var ink = new float[SampleWidth * SampleHeight];
            for (int y = 0; y < SampleHeight; y++)
            for (int x = 0; x < SampleWidth; x++)
            {
                float sum = 0;
                for (int subY = 0; subY < 4; subY++)
                for (int subX = 0; subX < 4; subX++)
                {
                    int sourceY = bounds.Top + (8 * y + 2 * subY + 1) * bounds.Height / (8 * SampleHeight);
                    int sourceX = bounds.Left + (8 * x + 2 * subX + 1) * bounds.Width / (8 * SampleWidth);
                    float level = (pixels[sourceY, sourceX] - floor) / (float)Math.Max(1, peak - floor);
                    sum += Math.Max(0, Math.Min(1, level));
                }
                ink[y * SampleWidth + x] = sum / 16;
            }
            return new Glyph(ink, (float)bounds.Width / bounds.Height);
        }

        private static float Distance(Glyph observed, Glyph example)
        {
            float best = float.MaxValue;
            // Browser rendering shifts otherwise identical glyphs by a fraction of a pixel.
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int offsetX = (int)Math.Floor(dx / 2f), offsetY = (int)Math.Floor(dy / 2f);
                float fractionX = (dx & 1) * .5f, fractionY = (dy & 1) * .5f;
                float sum = 0;
                for (int y = -1; y <= SampleHeight; y++)
                for (int x = -1; x <= SampleWidth; x++)
                {
                    int gx = x + offsetX, gy = y + offsetY;
                    float above = (1 - fractionX) * example.At(gx, gy) + fractionX * example.At(gx + 1, gy);
                    float below = (1 - fractionX) * example.At(gx, gy + 1) + fractionX * example.At(gx + 1, gy + 1);
                    float difference = observed.At(x, y) - ((1 - fractionY) * above + fractionY * below);
                    sum += difference * difference;
                }
                float distance = sum / (SampleWidth * SampleHeight) + .007f * Math.Abs(dx) + .010f * Math.Abs(dy);
                best = Math.Min(best, distance);
            }
            return best + .06f * Math.Abs(observed.Aspect - example.Aspect);
        }
    }
}
