using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace GlyphFinder
{
    internal enum SignalMode { Auto, Morse, Binary }

    internal sealed class SignalResult
    {
        public SignalMode Mode;          // Morse or Binary, or Auto when nothing fits
        public string Code = "";         // one character per digit, '?' for a group that is not a digit
        public SignalMode OtherMode;     // set when the length fits both encodings
        public string OtherCode = "";
        public int Pulses;

        public bool Complete { get { return GroupSize > 0 && Pulses % GroupSize == 0 && Code.Length > 0 && Code.IndexOf('?') < 0; } }
        public int GroupSize { get { return Mode == SignalMode.Morse ? 5 : Mode == SignalMode.Binary ? 4 : 0; } }
    }

    // Signal Decode: each digit is 5 pulses in Morse or 4 pulses in binary (S = 0, L = 1).
    internal static class SignalDecoder
    {
        public static readonly string[] Morse = { "LLLLL", "SLLLL", "SSLLL", "SSSLL", "SSSSL", "SSSSS", "LSSSS", "LLSSS", "LLLSS", "LLLLS" };
        public static readonly string[] Binary = { "SSSS", "SSSL", "SSLS", "SSLL", "SLSS", "SLSL", "SLLS", "SLLL", "LSSS", "LSSL" };

        // Accepts S/L as well as Morse dots and dashes, binary 0/1, and the block characters people paste.
        public static string Normalize(string text)
        {
            var pulses = new StringBuilder();
            foreach (char c in text ?? "")
            {
                if ("Ss.·0□▯○◻".IndexOf(c) >= 0) pulses.Append('S');
                else if ("Ll-_1—–■█▮●◼".IndexOf(c) >= 0) pulses.Append('L');
            }
            return pulses.ToString();
        }

        public static string Decode(string pulses, SignalMode mode)
        {
            string[] table = mode == SignalMode.Morse ? Morse : Binary;
            int size = table[0].Length;
            var code = new StringBuilder();
            for (int i = 0; i + size <= pulses.Length; i += size)
            {
                int digit = Array.IndexOf(table, pulses.Substring(i, size));
                code.Append(digit < 0 ? '?' : (char)('0' + digit));
            }
            return code.ToString();
        }

        public static SignalResult Solve(string pulses, SignalMode mode)
        {
            var result = new SignalResult { Pulses = pulses.Length };
            if (pulses.Length == 0) return result;
            if (mode != SignalMode.Auto)
            {
                result.Mode = mode;
                result.Code = Decode(pulses, mode);
                return result;
            }
            bool morse = pulses.Length % 5 == 0, binary = pulses.Length % 4 == 0;
            if (!morse && !binary) return result;
            string m = morse ? Decode(pulses, SignalMode.Morse) : null;
            string b = binary ? Decode(pulses, SignalMode.Binary) : null;
            bool mValid = m != null && m.IndexOf('?') < 0, bValid = b != null && b.IndexOf('?') < 0;
            // 20 and 40 pulses fit both. Prefer the one that decodes; when both do, the guide has only seen binary at these lengths.
            bool pickMorse = morse && (!binary || (mValid && !bValid));
            result.Mode = pickMorse ? SignalMode.Morse : SignalMode.Binary;
            result.Code = pickMorse ? m : b;
            if (morse && binary && mValid == bValid)
            {
                result.OtherMode = pickMorse ? SignalMode.Binary : SignalMode.Morse;
                result.OtherCode = pickMorse ? b : m;
            }
            return result;
        }

        // Read wrapped lines in screen order. Look for rectangular tiles rather than the brightest section
        // of the screenshot, which can be the replay button or the title above an all-short signal.
        public static string ReadPulses(Bitmap image)
        {
            if (image.Width < 8 || image.Height < 4) return "";
            var rows = new List<List<Rectangle>>();
            foreach (Rectangle mark in PulseMarks(Foreground(image)).OrderBy(mark => mark.Top))
            {
                var row = rows.LastOrDefault(line => Math.Abs(mark.Top - line[0].Top) <= Math.Max(1, line[0].Height / 6) &&
                    SimilarHeight(mark.Height, line[0].Height));
                if (row == null) rows.Add(row = new List<Rectangle>());
                row.Add(mark);
            }
            if (rows.Count == 0) return "";

            var anchor = rows.OrderByDescending(row => row.Count).ThenByDescending(row => row[0].Height).First();
            int height = anchor[0].Height;
            var aligned = rows.Where(row => SimilarHeight(row[0].Height, height)).ToList();
            int first = aligned.IndexOf(anchor), last = first;
            double maxRowGap = height * 2.5;
            while (first > 0 && aligned[first][0].Top - aligned[first - 1][0].Top <= maxRowGap) first--;
            while (last + 1 < aligned.Count && aligned[last + 1][0].Top - aligned[last][0].Top <= maxRowGap) last++;

            // A single cell is about two thirds as wide as it is tall, even when the screenshot has only long pulses.
            double cell = height * (2.0 / 3);
            int hairline = Math.Max(1, (int)Math.Round(cell * .3));
            var pulses = new StringBuilder();
            for (int row = first; row <= last; row++)
            {
                Rectangle[] marks = aligned[row].OrderBy(mark => mark.Left).ToArray();
                for (int i = 0; i < marks.Length; )
                {
                    int start = marks[i].Left, end = marks[i++].Right;
                    while (i < marks.Length && marks[i].Left - end <= hairline)
                        end = Math.Max(end, marks[i++].Right);
                    pulses.Append(end - start >= cell * 1.7 ? 'L' : 'S');
                }
            }
            return pulses.ToString();
        }

        private static bool SimilarHeight(int left, int right)
        {
            return Math.Abs(left - right) <= Math.Max(1, right / 5);
        }

        private static IEnumerable<Rectangle> PulseMarks(bool[,] pixels)
        {
            int width = pixels.GetLength(0), height = pixels.GetLength(1);
            var visited = new bool[width, height];
            var queue = new Queue<Point>();
            var offsets = new[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1) };
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (!pixels[x, y] || visited[x, y]) continue;
                    int left = x, right = x, top = y, bottom = y;
                    queue.Enqueue(new Point(x, y));
                    visited[x, y] = true;
                    while (queue.Count > 0)
                    {
                        Point p = queue.Dequeue();
                        left = Math.Min(left, p.X);
                        right = Math.Max(right, p.X);
                        top = Math.Min(top, p.Y);
                        bottom = Math.Max(bottom, p.Y);
                        foreach (Point offset in offsets)
                        {
                            int nextX = p.X + offset.X, nextY = p.Y + offset.Y;
                            if (nextX < 0 || nextX >= width || nextY < 0 || nextY >= height ||
                                visited[nextX, nextY] || !pixels[nextX, nextY]) continue;
                            visited[nextX, nextY] = true;
                            queue.Enqueue(new Point(nextX, nextY));
                        }
                    }
                    var bounds = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
                    // Discard narrow end markers, the panel border, and wide buttons.
                    if (bounds.Width >= 3 && bounds.Height >= 4 && bounds.Width >= bounds.Height * .36 &&
                        bounds.Width <= bounds.Height * 3 && HasTileEdges(pixels, bounds))
                        yield return bounds;
                }
        }

        private static bool HasTileEdges(bool[,] pixels, Rectangle tile)
        {
            int top = 0, bottom = 0, left = 0, right = 0;
            int lastX = tile.Right - 1, lastY = tile.Bottom - 1;
            for (int x = tile.Left; x <= lastX; x++)
            {
                if (pixels[x, tile.Top] || pixels[x, tile.Top + 1]) top++;
                if (pixels[x, lastY] || pixels[x, lastY - 1]) bottom++;
            }
            for (int y = tile.Top; y <= lastY; y++)
            {
                if (pixels[tile.Left, y] || pixels[tile.Left + 1, y]) left++;
                if (pixels[lastX, y] || pixels[lastX - 1, y]) right++;
            }
            return Math.Min(top, bottom) >= tile.Width * .7 && Math.Min(left, right) >= tile.Height * .7;
        }

        // Marks pixels that stand out from the dark background, whatever their hue.
        private static bool[,] Foreground(Bitmap image)
        {
            int w = image.Width, h = image.Height;
            var on = new bool[w, h];
            var rect = new Rectangle(0, 0, w, h);
            BitmapData data = image.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[data.Stride * h];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                var light = new int[w * h];
                var histogram = new int[256];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * data.Stride + x * 4;
                        int level = Math.Max(bytes[i], Math.Max(bytes[i + 1], bytes[i + 2]));
                        light[y * w + x] = level;
                        histogram[level]++;
                    }
                int background = 0, below = histogram[0];
                while (below <= light.Length / 5) below += histogram[++background];
                // Hollow short pulses can be much dimmer than filled long pulses.
                int threshold = background + Math.Max(28, (255 - background) / 6);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        on[x, y] = light[y * w + x] >= threshold;
            }
            finally { image.UnlockBits(data); }
            return on;
        }
    }

    // The six error types the game offers, in its order, plus Format for text that fits none of them.
    internal enum LogField { Time, Type, MissingSector, MissingStatus, Sector, Status, Format }

    internal sealed class LogIssue
    {
        public readonly LogField Field;
        public readonly string Chinese, English;

        public LogIssue(LogField field, string chinese, string english)
        {
            Field = field;
            Chinese = chinese;
            English = english;
        }
    }

    internal sealed class LogCheck
    {
        public int Number;
        public string Line = "";
        public readonly List<LogIssue> Issues = new List<LogIssue>();
        public bool Ok { get { return Issues.Count == 0; } }
    }

    internal static class LogReader
    {
        private static readonly Regex Event = new Regex(@"(?<!\S)[A-Za-z][A-Za-z0-9-]*(?=\s+(?:/[A-Za-z]|[\[']\s*(?:sector|status)\b))");
        private static readonly Regex Timestamp = new Regex(@"^\[\s*(\d{1,2})\s*:\s*(\d{1,2})\s*:\s*(\d{1,2})\s*\]");
        private static readonly Regex ImportableLine = new Regex(@"^\[[0-9]+(?:[:.\-][0-9]+){2}\]\s+\S");
        private static readonly Regex FieldWithDamagedPunctuation = new Regex(@"(?<=\s)[\['/]\s*(sector|status)\s*[-—=]\s*(?=\w)");

        // Check that OCR captured the timestamp and some log text. The validator checks the timestamp's format and range later.
        public static bool CanImport(string line)
        {
            return ImportableLine.IsMatch(line);
        }

        private sealed class CapturedLine
        {
            public string Text, Body;
            public Rectangle ClockArea;
        }

        public static List<string> Read(Bitmap image)
        {
            var clocks = new LogClockRecognizer(image);
            var rows = WindowsOcr.RecognizeLines(image).OrderBy(line => line.Bounds.Top)
                .Where(line => line.Text.IndexOf("SCAN LOGS OUTPUT", StringComparison.OrdinalIgnoreCase) < 0)
                .Select(line => ReadLine(image, line, clocks)).ToList();

            var result = new List<string>();
            foreach (CapturedLine row in rows)
            {
                string text = row.Text;
                if (!row.ClockArea.IsEmpty)
                {
                    string time = clocks.Read(row.ClockArea);
                    if (time != null) text = time + " " + row.Body;
                }
                result.Add(text);
            }
            return result;
        }

        private static CapturedLine ReadLine(Bitmap image, OcrTextLine line, LogClockRecognizer clocks)
        {
            string text = LogValidator.FixOcr(line.Text);
            Match type = Event.Match(text);
            if (!type.Success || line.Bounds.IsEmpty) return new CapturedLine { Text = text };

            // The game uses monospace log text. The timestamp and the space after it take eleven character positions.
            string body = text.Substring(type.Index);
            float pitch = line.Bounds.Width / (body.Length + (type.Index == 0 ? 0 : 11));
            float start = line.Bounds.Left - (type.Index == 0 ? 11 * pitch : 0);
            int left = Math.Max(0, (int)Math.Round(start - .5f * pitch));
            int top = Math.Max(0, (int)Math.Floor(line.Bounds.Top - .7f * pitch));
            int bottom = Math.Min(image.Height, (int)Math.Ceiling(line.Bounds.Bottom + .6f * pitch));
            var row = Rectangle.FromLTRB(left, top, image.Width, bottom);
            int fieldStart = (int)Math.Round(start + 10.5f * pitch);
            string comparable = FieldWithDamagedPunctuation.Replace(body, "/$1=");
            if (comparable != body && fieldStart >= 0 && fieldStart < row.Right)
            {
                var bodyArea = Rectangle.FromLTRB(fieldStart, top, row.Right, bottom);
                using (Bitmap crop = image.Clone(bodyArea, PixelFormat.Format32bppArgb))
                {
                    if (WindowsOcr.Recognize(crop).Concat(new[] { 1f, 1.25f, 1.5f }.SelectMany(scale => WindowsOcr.RecognizeLogCrop(crop, scale)))
                        .Select(LogValidator.FixOcr).Contains(comparable))
                    {
                        text = text.Substring(0, type.Index) + comparable;
                        body = comparable;
                    }
                }
            }

            Match timestamp = Timestamp.Match(text);
            string time = timestamp.Success ? timestamp.Value : ReadTimestamp(image, row, fieldStart, pitch);
            if (!timestamp.Success && time != null) text = time + " " + body;

            int glyphTop = Math.Max(0, (int)Math.Floor(line.Bounds.Top - .35f * pitch));
            int glyphBottom = Math.Min(image.Height, (int)Math.Ceiling(line.Bounds.Bottom + .3f * pitch));
            int glyphRight = Math.Min(image.Width, (int)Math.Ceiling(line.Bounds.Right + .35f * pitch));
            int split = Math.Min(glyphRight, Math.Max(left, fieldStart));
            var clockArea = Rectangle.FromLTRB(left, glyphTop, split, glyphBottom);
            var bodyBounds = Rectangle.FromLTRB(split, glyphTop, glyphRight, glyphBottom);
            clocks.Learn(clockArea, time, bodyBounds, body);

            return new CapturedLine
            {
                Text = text,
                Body = body,
                ClockArea = time == null && clockArea.Width > 0 ? clockArea : Rectangle.Empty
            };
        }

        private static string ReadTimestamp(Bitmap image, Rectangle row, int fieldStart, float pitch)
        {
            // A narrowly cropped timestamp can be read even when OCR splits the row at the event type.
            int clockHeight = (int)Math.Round(2.7f * pitch);
            int clockTop = Math.Max(row.Top, (row.Top + row.Bottom - clockHeight) / 2);
            var clock = Rectangle.FromLTRB(row.Left, clockTop, Math.Min(row.Right, fieldStart),
                Math.Min(row.Bottom, clockTop + clockHeight));

            string time = ReadTimestamp(image, row, 1f) ?? ReadTimestamp(image, row, 1.25f) ??
                ReadTimestamp(image, clock, 1f) ?? ReadTimestamp(image, clock, 1.25f);
            if (time == null)
            {
                // Removing a little left padding can restore a timestamp that the OCR treated as separate symbols.
                var tightRow = Rectangle.FromLTRB(row.Left + Math.Max(1, (int)Math.Round(.35f * pitch)),
                    Math.Max(0, clock.Top - 1), row.Right, clock.Bottom - 1);
                time = ReadTimestamp(image, tightRow, 1.25f);
            }
            return time;
        }

        private static string ReadTimestamp(Bitmap image, Rectangle area, float scale)
        {
            if (area.Width <= 0 || area.Height <= 0) return null;
            using (Bitmap crop = image.Clone(area, PixelFormat.Format32bppArgb))
            {
                foreach (string line in WindowsOcr.RecognizeLogCrop(crop, scale))
                {
                    Match time = Timestamp.Match(line.TrimStart());
                    if (time.Success)
                        return "[" + time.Groups[1].Value + ":" + time.Groups[2].Value + ":" + time.Groups[3].Value + "]";
                }
            }
            return null;
        }
    }

    // System Log Integrity: [HH:MM:SS] TYPE /sector=1..256 /status=OK|WARN|ERROR
    internal static class LogValidator
    {
        public static readonly string[] Types = { "AUTH", "TEMP-SYNC", "SCAN", "ROUTE-CHECK", "RADIO-TEST", "PING", "SYNC" };
        public static readonly string[] Statuses = { "OK", "WARN", "ERROR" };
        private static readonly Regex Clock = new Regex(@"^(\d{2}):(\d{2}):(\d{2})$");
        private static readonly Regex LooseClock = new Regex(@"^\d{1,2}[:.\-]\d{1,2}[:.\-]\d{1,2}$");

        public static List<LogCheck> Check(string text)
        {
            var checks = new List<LogCheck>();
            foreach (string raw in (text ?? "").Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                string line = Clean(raw);
                if (line.Length == 0) continue;
                LogCheck check = CheckLine(line);
                check.Number = checks.Count + 1;
                checks.Add(check);
            }
            return checks;
        }

        // Drops checkboxes, bullets, and list numbers that come along when lines are copied from the game.
        private static string Clean(string raw)
        {
            string line = raw.Trim().TrimStart('☐', '☑', '☒', '□', '■', '▢', '✓', '✔', '•', '·', '*', '-', '>').Trim();
            return Regex.Replace(line, @"^\d{1,2}[.)]\s+(?=\[)", "");
        }

        public static LogCheck CheckLine(string line)
        {
            var check = new LogCheck { Line = line };
            var tokens = new List<string>(line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));

            // Time
            if (tokens.Count > 0 && tokens[0].StartsWith("["))
            {
                string token = tokens[0];
                tokens.RemoveAt(0);
                if (!token.EndsWith("]") || token.Length < 2)
                    check.Issues.Add(new LogIssue(LogField.Time, "时间缺少右括号：" + token, "Time is missing its closing bracket: " + token));
                else CheckClock(token.Substring(1, token.Length - 2), check);
            }
            else if (tokens.Count > 0 && LooseClock.IsMatch(tokens[0]))
            {
                check.Issues.Add(new LogIssue(LogField.Time, "时间缺少方括号：" + tokens[0], "Time is not in brackets: " + tokens[0]));
                tokens.RemoveAt(0);
            }
            else check.Issues.Add(new LogIssue(LogField.Time, "缺少时间 [HH:MM:SS]", "Time [HH:MM:SS] is missing"));

            // Type
            if (tokens.Count > 0 && !tokens[0].StartsWith("/"))
            {
                string type = tokens[0];
                tokens.RemoveAt(0);
                if (Array.IndexOf(Types, type) < 0)
                {
                    string near = Types.FirstOrDefault(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));
                    check.Issues.Add(near != null
                        ? new LogIssue(LogField.Type, "类型大小写不对：" + type + "（应为 " + near + "）", "Type has the wrong case: " + type + " (should be " + near + ")")
                        : new LogIssue(LogField.Type, "不是有效类型：" + type, "Not a valid type: " + type));
                }
            }
            else check.Issues.Add(new LogIssue(LogField.Type, "缺少事件类型", "Event type is missing"));

            // Fields
            string sector = null, status = null;
            bool sectorSeen = false, statusSeen = false;
            foreach (string token in tokens)
            {
                int eq = token.IndexOf('=');
                string key = token.StartsWith("/") ? (eq > 0 ? token.Substring(1, eq - 1) : token.Substring(1)) : null;
                string value = eq > 0 ? token.Substring(eq + 1) : null;
                if (string.Equals(key, "sector", StringComparison.OrdinalIgnoreCase) && !sectorSeen)
                {
                    sectorSeen = true;
                    sector = value;
                }
                else if (string.Equals(key, "status", StringComparison.OrdinalIgnoreCase) && !statusSeen)
                {
                    statusSeen = true;
                    status = value;
                }
                else check.Issues.Add(new LogIssue(LogField.Format, "多余或拼错的内容：" + token, "Unexpected or misspelled text: " + token));
            }

            if (!sectorSeen) check.Issues.Add(new LogIssue(LogField.MissingSector, "缺少 /sector", "/sector is missing"));
            else
            {
                int number;
                if (string.IsNullOrEmpty(sector) || !int.TryParse(sector, NumberStyles.None, CultureInfo.InvariantCulture, out number))
                    check.Issues.Add(new LogIssue(LogField.Sector, "sector 不是数字：" + (sector ?? ""), "Sector is not a number: " + (sector ?? "")));
                else if (number < 1 || number > 256)
                    check.Issues.Add(new LogIssue(LogField.Sector, "sector " + sector + " 超出 1–256", "Sector " + sector + " is outside 1–256"));
            }

            if (!statusSeen) check.Issues.Add(new LogIssue(LogField.MissingStatus, "缺少 /status", "/status is missing"));
            else if (Array.IndexOf(Statuses, status ?? "") < 0)
                check.Issues.Add(new LogIssue(LogField.Status, "status 无效：" + (status ?? "") + "（应为 OK、WARN 或 ERROR）",
                    "Status is not OK, WARN, or ERROR: " + (status ?? "")));
            return check;
        }

        private static void CheckClock(string clock, LogCheck check)
        {
            Match m = Clock.Match(clock);
            if (!m.Success)
            {
                check.Issues.Add(new LogIssue(LogField.Time, "时间格式应为 HH:MM:SS：" + clock, "Time is not HH:MM:SS: " + clock));
                return;
            }
            int hours = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            int minutes = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            int seconds = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            if (hours > 23) check.Issues.Add(new LogIssue(LogField.Time, "小时 " + m.Groups[1].Value + " 超出 00–23", "Hour " + m.Groups[1].Value + " is outside 00–23"));
            if (minutes > 59) check.Issues.Add(new LogIssue(LogField.Time, "分钟 " + m.Groups[2].Value + " 超出 00–59", "Minute " + m.Groups[2].Value + " is outside 00–59"));
            if (seconds > 59) check.Issues.Add(new LogIssue(LogField.Time, "秒 " + m.Groups[3].Value + " 超出 00–59", "Second " + m.Groups[3].Value + " is outside 00–59"));
        }

        public static string FieldName(LogField field)
        {
            switch (field)
            {
                case LogField.Time: return "Time format is incorrect";
                case LogField.Type: return "Event type is incorrect";
                case LogField.MissingSector: return "Missing /sector parameter";
                case LogField.MissingStatus: return "Missing /status parameter";
                case LogField.Sector: return "/sector parameter is incorrect";
                case LogField.Status: return "/status parameter is incorrect";
                default: return "Check this text";
            }
        }

        private static string MapChars(string text, string from, string to)
        {
            var result = new StringBuilder(text);
            for (int i = 0; i < result.Length; i++)
            {
                int k = from.IndexOf(result[i]);
                if (k >= 0) result[i] = to[k];
            }
            return result.ToString();
        }

        // Undoes the usual OCR mix-ups (O/0, l/1, S/5, misread brackets and slashes, stray spaces) only where the
        // format fixes what a character must be. Real errors in the game, such as 67 minutes or a misspelled type, stay.
        public static string FixOcr(string line)
        {
            line = Regex.Replace(line ?? "", @"\s+", " ").Trim();
            int open = line.IndexOfAny(new[] { '[', '(', '{' });
            if (open > 0 && open <= 4 && open + 1 < line.Length && char.IsLetterOrDigit(line[open + 1])) line = line.Substring(open);

            // Time: [HH:MM:SS] holds only digits and colons.
            Match time = Regex.Match(line, @"^[\[({]\s*([0-9OoDQIl|i!SsBZz]{1,2}\s*[:;]\s*[0-9OoDQIl|i!SsBZz]{1,2}\s*[:;]\s*[0-9OoDQIl|i!SsBZz]{1,2})\s*[\])}J]");
            if (time.Success)
            {
                string clock = MapChars(time.Groups[1].Value.Replace(" ", "").Replace(';', ':'), "OoDQIl|i!SsBZz", "00001111155822");
                line = "[" + clock + "] " + line.Substring(time.Length).TrimStart();
            }

            // Parameters: a slash misread as l, I, | or \, and spaces around the slash or equals sign.
            line = Regex.Replace(line, @"(^|\s)[/|Il\\]\s*(?=(?:s[e3]ct[o0]r|st[a@]tu[s5])\s*=)", "$1/");
            line = Regex.Replace(line, @"/\s+(?=\w+\s*=)", "/");
            line = Regex.Replace(line, @"\s*=\s*", "=");
            line = Regex.Replace(line, @"(?<=\S)/(?=(?:sector|status|s[e3]ct[o0]r|st[a@]tu[s5])=)", " /");
            line = Regex.Replace(line, @"/(s[e3]ct[o0]r|st[a@]tu[s5])=", m => "/" + MapChars(m.Groups[1].Value, "30@5", "eoas") + "=");
            line = Regex.Replace(line, @"(?<=/sector=)[0-9OoDIl|]+(?=\s|$)", m => MapChars(m.Value, "OoDIl|", "000111"));
            line = Regex.Replace(line, @"(?<=/status=)\S+", m => MapChars(m.Value, "0", "O"));

            // Type: letters and hyphens only, so digits inside it are misread letters.
            line = Regex.Replace(line, @"(?<=^\[[^\]]*\] )[A-Za-z0-9-]+(?= |$)", m => Regex.IsMatch(m.Value, "[A-Za-z]") ? MapChars(m.Value, "0158", "OISB") : m.Value);
            return line;
        }
    }
}
