using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GlyphFinder
{
    internal sealed class OcrUnavailableException : Exception
    {
        public OcrUnavailableException(string message, Exception inner = null) : base(message, inner) { }
    }

    internal sealed class OcrTextLine
    {
        public readonly string Text;
        public readonly RectangleF Bounds;

        public OcrTextLine(string text, RectangleF bounds)
        {
            Text = text;
            Bounds = bounds;
        }
    }

    // Text recognition with the OCR engine built into Windows 10 and 11 (Windows.Media.Ocr).
    // The WinRT types are bound at run time, so the build needs no Windows SDK and other systems get a clear message.
    internal static class WindowsOcr
    {
        private const int MaxDimension = 2600;
        private static object engine;
        private static Type engineType, resultType, lineType, bitmapType, formatType;
        private static MethodInfo asBuffer, asTask;

        private static Type WinRT(string name)
        {
            foreach (string container in new[] { "Windows.Foundation", "Windows", "Windows.Media", "Windows.Graphics", "Windows.Globalization" })
            {
                try
                {
                    Type type = Type.GetType(name + ", " + container + ", ContentType=WindowsRuntime");
                    if (type != null) return type;
                }
                catch (Exception ex)
                {
                    if (!(ex is TypeLoadException || ex is System.IO.IOException || ex is ArgumentException || ex is BadImageFormatException)) throw;
                }
            }
            return null;
        }

        private static void Initialize()
        {
            if (engine != null) return;
            if (Environment.OSVersion.Platform != PlatformID.Win32NT || Environment.OSVersion.Version.Major < 10)
                throw new OcrUnavailableException("Windows 10 or 11 is needed for text recognition.");
            engineType = WinRT("Windows.Media.Ocr.OcrEngine");
            resultType = WinRT("Windows.Media.Ocr.OcrResult");
            lineType = WinRT("Windows.Media.Ocr.OcrLine");
            bitmapType = WinRT("Windows.Graphics.Imaging.SoftwareBitmap");
            formatType = WinRT("Windows.Graphics.Imaging.BitmapPixelFormat");
            Type languageType = WinRT("Windows.Globalization.Language");
            if (engineType == null || resultType == null || lineType == null || bitmapType == null || formatType == null || languageType == null)
                throw new OcrUnavailableException("Windows text recognition (Windows.Media.Ocr) was not found.");

            Assembly runtime = Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
            asBuffer = runtime.GetType("System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions", true)
                .GetMethod("AsBuffer", new[] { typeof(byte[]) });
            asTask = runtime.GetType("System.WindowsRuntimeSystemExtensions", true).GetMethods()
                .First(m => m.Name == "AsTask" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1")
                .MakeGenericMethod(resultType);

            // The logs are English, so prefer an English engine and fall back to the user's languages.
            object created = null;
            foreach (string tag in new[] { "en-US", "en-GB", "en" })
            {
                object language = Activator.CreateInstance(languageType, tag);
                if ((bool)engineType.GetMethod("IsLanguageSupported").Invoke(null, new[] { language }))
                {
                    created = engineType.GetMethod("TryCreateFromLanguage").Invoke(null, new[] { language });
                    if (created != null) break;
                }
            }
            if (created == null) created = engineType.GetMethod("TryCreateFromUserProfileLanguages").Invoke(null, null);
            if (created == null)
                throw new OcrUnavailableException("No text recognition language is installed. Add English in Windows Settings › Time & language › Language.");
            engine = created;
        }

        public static List<string> Recognize(Bitmap image)
        {
            return Recognize(image, (line, xScale, yScale) => (string)lineType.GetProperty("Text").GetValue(line, null));
        }

        internal static List<string> RecognizeLogCrop(Bitmap image, float scale)
        {
            return Recognize(image, (line, xScale, yScale) => (string)lineType.GetProperty("Text").GetValue(line, null),
                source => PrepareLogCrop(source, scale));
        }

        public static List<OcrTextLine> RecognizeLines(Bitmap image)
        {
            Type wordType = WinRT("Windows.Media.Ocr.OcrWord");
            if (wordType == null) throw new OcrUnavailableException("Windows text recognition word positions were not found.");
            PropertyInfo boundingRect = wordType.GetProperty("BoundingRect");
            return Recognize(image, (line, xScale, yScale) =>
            {
                RectangleF bounds = RectangleF.Empty;
                foreach (object word in (IEnumerable)lineType.GetProperty("Words").GetValue(line, null))
                {
                    object rect = boundingRect.GetValue(word, null);
                    Type type = rect.GetType();
                    var area = new RectangleF(
                        Convert.ToSingle(type.GetProperty("X").GetValue(rect, null)) * xScale,
                        Convert.ToSingle(type.GetProperty("Y").GetValue(rect, null)) * yScale,
                        Convert.ToSingle(type.GetProperty("Width").GetValue(rect, null)) * xScale,
                        Convert.ToSingle(type.GetProperty("Height").GetValue(rect, null)) * yScale);
                    bounds = bounds.IsEmpty ? area : RectangleF.Union(bounds, area);
                }
                return new OcrTextLine((string)lineType.GetProperty("Text").GetValue(line, null), bounds);
            });
        }

        private static List<T> Recognize<T>(Bitmap image, Func<object, float, float, T> readLine, Func<Bitmap, Bitmap> prepare = null)
        {
            try
            {
                Initialize();
                using (Bitmap prepared = (prepare ?? Prepare)(image))
                {
                    var rect = new Rectangle(Point.Empty, prepared.Size);
                    BitmapData data = prepared.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    var bytes = new byte[prepared.Width * prepared.Height * 4];
                    try
                    {
                        for (int y = 0; y < prepared.Height; y++)
                            Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * prepared.Width * 4, prepared.Width * 4);
                    }
                    finally { prepared.UnlockBits(data); }

                    object buffer = asBuffer.Invoke(null, new object[] { bytes });
                    MethodInfo create = bitmapType.GetMethods().First(m => m.Name == "CreateCopyFromBuffer" && m.GetParameters().Length == 4);
                    object bitmap = create.Invoke(null, new[] { buffer, Enum.ToObject(formatType, 87), prepared.Width, prepared.Height });   // Bgra8
                    using (var disposable = bitmap as IDisposable)
                    {
                        object operation = engineType.GetMethod("RecognizeAsync").Invoke(engine, new[] { bitmap });
                        var task = (Task)asTask.Invoke(null, new[] { operation });
                        if (!task.Wait(15000)) throw new OcrUnavailableException("Text recognition timed out.");
                        object result = task.GetType().GetProperty("Result").GetValue(task, null);
                        float xScale = (float)image.Width / prepared.Width;
                        float yScale = (float)image.Height / prepared.Height;
                        var lines = new List<T>();
                        foreach (object line in (IEnumerable)resultType.GetProperty("Lines").GetValue(result, null))
                            lines.Add(readLine(line, xScale, yScale));
                        return lines;
                    }
                }
            }
            catch (TargetInvocationException ex)
            {
                throw new OcrUnavailableException("Windows text recognition failed: " + ex.GetBaseException().Message, ex);
            }
            catch (AggregateException ex)
            {
                throw new OcrUnavailableException("Windows text recognition failed: " + ex.GetBaseException().Message, ex);
            }
            catch (Exception ex)
            {
                if (ex is OcrUnavailableException) throw;
                if (ex is TypeLoadException || ex is System.IO.IOException || ex is MissingMethodException || ex is InvalidCastException ||
                    ex is InvalidOperationException || ex is COMException || ex is NotSupportedException || ex is ArgumentException)
                    throw new OcrUnavailableException("Windows text recognition is not available: " + ex.Message, ex);
                throw;
            }
        }

        // The regular enlargement can make Windows OCR skip punctuation and digits in a small monospace timestamp.
        private static Bitmap PrepareLogCrop(Bitmap source, float scale)
        {
            const int margin = 8;
            scale = Math.Min(scale, Math.Min((float)(MaxDimension - 2 * margin) / source.Width,
                (float)(MaxDimension - 2 * margin) / source.Height));
            int width = Math.Max(1, (int)(source.Width * scale)), height = Math.Max(1, (int)(source.Height * scale));
            var padded = new Bitmap(width + 2 * margin, height + 2 * margin, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(padded))
            {
                g.Clear(Color.Black);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(margin, margin, width, height));
            }
            return padded;
        }

        // Dark text on white, with the game's scanline background removed and small text enlarged.
        public static Bitmap Prepare(Bitmap source)
        {
            int w = source.Width, h = source.Height;
            var light = new byte[w * h];
            var rect = new Rectangle(0, 0, w, h);
            using (Bitmap copy = source.Clone(rect, PixelFormat.Format32bppArgb))
            {
                BitmapData data = copy.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new byte[w * 4];
                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                        for (int x = 0; x < w; x++)
                            light[y * w + x] = Math.Max(row[x * 4], Math.Max(row[x * 4 + 1], row[x * 4 + 2]));
                    }
                }
                finally { copy.UnlockBits(data); }
            }
            byte[] sorted = (byte[])light.Clone();
            Array.Sort(sorted);
            int background = sorted[sorted.Length / 2];
            int top = Math.Max(background + 40, sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * .995))]);
            int floor = background + 18;

            var flat = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData output = flat.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[w * 4];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int v = light[y * w + x];
                        int ink = v <= floor ? 0 : Math.Min(255, (v - floor) * 255 / Math.Max(1, top - floor));
                        byte shade = (byte)(255 - ink);
                        row[x * 4] = row[x * 4 + 1] = row[x * 4 + 2] = shade;
                        row[x * 4 + 3] = 255;
                    }
                    Marshal.Copy(row, 0, output.Scan0 + y * output.Stride, row.Length);
                }
            }
            finally { flat.UnlockBits(output); }

            float scale = Math.Min(3f, Math.Min((float)MaxDimension / w, (float)MaxDimension / h));
            scale = Math.Min(scale, h < 900 ? 2.5f : 1.5f);
            if (scale >= 1f && scale <= 1.05f) return flat;
            var large = new Bitmap(Math.Max(1, (int)(w * scale)), Math.Max(1, (int)(h * scale)), PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(large))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(flat, new Rectangle(0, 0, large.Width, large.Height));
            }
            flat.Dispose();
            return large;
        }
    }
}
