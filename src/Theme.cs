// Visual system, following Windows 11 (Fluent) guidance:
//  - neutral surfaces & text tokens from WinUI theme resources (acrylic-fallback flyout colours)
//  - accent used sparingly (next waktu label, progress, ticks, current-row tint, primary button)
//  - all text >= 4.5:1 and tick outline >= 3:1 (WCAG 2.2 AA), verified for every accent in both modes
//  - Segoe UI Variable on Windows 11, Segoe UI on Windows 10
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using Microsoft.Win32;

namespace WaktuSolat
{
    static class Theme
    {
        public enum Mode { Auto = 0, Dark = 1, Light = 2 }

        public sealed class AccentDef
        {
            public string Name; public Color Dark, Light;
            public AccentDef(string n, int dark, int light) { Name = n; Dark = C(dark); Light = C(light); }
        }

        // Muted, Windows-like accents (dark-mode / light-mode variants).
        public static readonly AccentDef[] Accents =
        {
            new AccentDef("Zamrud",            0x5CD08C, 0x127A4A),
            new AccentDef("Firus",             0x3CCBD3, 0x0A6F80),
            new AccentDef("Biru Negara",       0x7AB4FF, 0x1F5FCC),
            new AccentDef("Ungu Senja",        0xC4A0FF, 0x7139C6),
            new AccentDef("Merah Jambu Putra", 0xF59AC0, 0xB02E6A),
        };

        public static bool IsLight { get; private set; }
        static int _accent;

        public static Color Bg, Fg, Dim, Stroke, Track, Accent, OnAccent, Plate, Hover, Warn, ButtonFill, ButtonStroke;

        static Theme() { Apply(Mode.Auto, 0); }

        public static void Apply(Mode mode, int accent)
        {
            IsLight = mode == Mode.Light || (mode == Mode.Auto && ReadPersonalize("AppsUseLightTheme"));
            _accent = Math.Max(0, Math.Min(accent, Accents.Length - 1));
            var a = Accents[_accent];
            Bg           = IsLight ? C(0xF9F9F9) : C(0x2C2C2C);   // flyout/toast surface
            Fg           = IsLight ? C(0x1A1A1A) : C(0xFFFFFF);   // text primary
            Dim          = IsLight ? C(0x5F5F5F) : C(0xCFCFCF);   // text secondary
            Stroke       = IsLight ? C(0x8A8A8A) : C(0x9F9F9F);   // control strong stroke (tick outline)
            Track        = IsLight ? C(0xEAEAEA) : C(0x3D3D3D);   // divider / progress track
            Warn         = IsLight ? C(0xA15C00) : C(0xF5B83D);   // caution (offline estimate)
            Accent       = IsLight ? a.Light : a.Dark;
            OnAccent     = IsLight ? Color.White : Color.Black;
            Plate        = Mix(Accent, Bg, 0.14);                  // current-waktu backplate
            Hover        = Mix(Fg, Bg, 0.06);                      // subtle hover fill
            ButtonFill   = IsLight ? C(0xFDFDFD) : Mix(Color.White, Bg, 0.06);
            ButtonStroke = IsLight ? C(0xE0E0E0) : Mix(Color.White, Bg, 0.10);
        }

        static Color C(int rgb) => Color.FromArgb(255, Color.FromArgb(rgb));

        static Color Mix(Color f, Color b, double a) => Color.FromArgb(255,
            (int)Math.Round(f.R * a + b.R * (1 - a)), (int)Math.Round(f.G * a + b.G * (1 - a)), (int)Math.Round(f.B * a + b.B * (1 - a)));

        // AppsUseLightTheme = app windows; SystemUsesLightTheme = taskbar/tray (often dark even in light mode on Windows 10).
        static bool ReadPersonalize(string name)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return (k?.GetValue(name) as int?) == 1;
            }
            catch { return false; }
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// Crescent icon in the accent colour (variant chosen for the taskbar's own theme), drawn at runtime.
        public static Icon MakeIcon(int size)
        {
            var a = Accents[_accent];
            using (var bmp = Crescent(size, ReadPersonalize("SystemUsesLightTheme") ? a.Light : a.Dark))
            {
                IntPtr h = bmp.GetHicon();
                var icon = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                return icon;
            }
        }

        public static Bitmap Crescent(int size, Color color)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float pad = size * 0.06f, d = size - 2 * pad;
                using (var b = new SolidBrush(color)) g.FillEllipse(b, pad, pad, d, d);
                g.CompositingMode = CompositingMode.SourceCopy;
                using (var b = new SolidBrush(Color.Transparent))
                    g.FillEllipse(b, pad + d * 0.30f, pad - d * 0.12f, d * 0.92f, d * 0.92f);
            }
            return bmp;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    }

    /// Windows 11 type ramp (px): Caption 12, Body 14, Body Strong 14 semibold, Body Large 18, Title Large 40.
    static class Type
    {
        static readonly string[] Installed = SafeFamilies();
        static string[] SafeFamilies() { try { return new InstalledFontCollection().Families.Select(f => f.Name).ToArray(); } catch { return new string[0]; } }
        static string Pick(params string[] names) => names.FirstOrDefault(n => Installed.Contains(n, StringComparer.OrdinalIgnoreCase)) ?? "Segoe UI";

        static readonly string Small = Pick("Segoe UI Variable Small", "Segoe UI");
        static readonly string Text = Pick("Segoe UI Variable Text", "Segoe UI");
        static readonly string TextSemibold = Pick("Segoe UI Variable Text Semibold", "Segoe UI Semibold");
        static readonly string DisplaySemilight = Pick("Segoe UI Variable Display Semil", "Segoe UI Variable Display Semilight", "Segoe UI Semilight", "Segoe UI Light");
        public static readonly string Icons = Installed.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons"
                                            : Installed.Contains("Segoe MDL2 Assets") ? "Segoe MDL2 Assets" : null;

        public static Font Caption(float px = 12) => new Font(Small, px, GraphicsUnit.Pixel);
        public static Font Body(float px = 14) => new Font(Text, px, GraphicsUnit.Pixel);
        public static Font BodyStrong(float px = 14) => new Font(TextSemibold, px, GraphicsUnit.Pixel);
        public static Font Display(float px = 40) => new Font(DisplaySemilight, px, GraphicsUnit.Pixel);
        public static Font Icon(float px = 14) => Icons == null ? null : new Font(Icons, px, GraphicsUnit.Pixel);

        // Segoe Fluent Icons / MDL2 glyphs
        public const string GlyphLocation = "", GlyphMinimize = "", GlyphCheck = "", GlyphClose = "";
    }
}
