using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace GlyphFinder
{
    internal enum UiLanguage { Chinese, English }

    internal static class UiText
    {
        public static string Text(this UiLanguage language, string chinese, string english)
        {
            return language == UiLanguage.English ? english : chinese;
        }
    }

    internal sealed class LanguagePreference
    {
        private readonly string path;

        public LanguagePreference(string path = null)
        {
            this.path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TriangleGlyphFinder", "language.txt");
        }

        public UiLanguage Load()
        {
            try
            {
                if (File.ReadAllText(path).Trim() == "zh") return UiLanguage.Chinese;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return UiLanguage.English;
        }

        public void Save(UiLanguage language)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, language == UiLanguage.English ? "en" : "zh");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal sealed class WindowSizePreference
    {
        private readonly string directory;

        public WindowSizePreference(string directory = null)
        {
            this.directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TriangleGlyphFinder");
        }

        public void Bind(Form window, string name, Func<bool> normalMode = null)
        {
            Size saved = Load(name);
            if (!saved.IsEmpty) window.ClientSize = saved;
            window.ResizeEnd += delegate
            {
                if (window.WindowState == FormWindowState.Normal && (normalMode == null || normalMode()))
                    Save(name, window.ClientSize);
            };
        }

        internal Size Load(string name)
        {
            try
            {
                var parts = File.ReadAllText(Path.Combine(directory, name + "-size.txt")).Split(',');
                int width, height;
                if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out width) &&
                    int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out height) &&
                    width >= 200 && height >= 150 && width <= 16000 && height <= 16000)
                    return new Size(width, height);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return Size.Empty;
        }

        internal void Save(string name, Size size)
        {
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, name + "-size.txt"),
                    string.Format(CultureInfo.InvariantCulture, "{0},{1}", size.Width, size.Height));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
