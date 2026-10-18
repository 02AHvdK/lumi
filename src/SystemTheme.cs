using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace lumi
{
    internal static class SystemTheme
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmGetColorizationColor(out uint color, out bool opaque);
        internal static bool Dark;
        internal static string Mode = "System", AccentChoice = "System";
        internal static readonly string PreferencesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lumi", "appearance.txt");
        static bool preferencesLoaded;
        internal static string ValidateMode(string value) { return value == "Light" || value == "Dark" ? value : "System"; }
        internal static string ValidateAccent(string value) { return value == "Blue" || value == "Purple" || value == "Green" || value == "Orange" ? value : "System"; }
        internal static void Choose(string mode, string accent)
        {
            mode = ValidateMode(mode); accent = ValidateAccent(accent);
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath));
            File.WriteAllLines(PreferencesPath, new[] { mode, accent });
            Mode = mode; AccentChoice = accent; preferencesLoaded = true; Reload();
        }

        internal static void Reload()
        {
            if (!preferencesLoaded) {
                preferencesLoaded = true;
                try { if (File.Exists(PreferencesPath)) { var lines = File.ReadAllLines(PreferencesPath); if (lines.Length > 0) Mode = ValidateMode(lines[0]); if (lines.Length > 1) AccentChoice = ValidateAccent(lines[1]); } }
                catch (Exception e) { AppLog.Write(e); }
            }
            bool dark = false;
            uint accent = 0xff537fbd;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    dark = k != null && Convert.ToInt32(k.GetValue("AppsUseLightTheme", 1)) == 0;
                bool opaque;
                uint native;
                if (DwmGetColorizationColor(out native, out opaque) == 0) accent = native;
                // Explorer's menu accent is independent of the optional title-bar tint.
                // Registry COLORREF is AABBGGRR, unlike DWM's AARRGGBB.
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                {
                    object value = k == null ? null : k.GetValue("AccentColorMenu");
                    if (value is int)
                    {
                        uint c = unchecked((uint)(int)value);
                        accent = 0xff000000 | ((c & 255) << 16) | (c & 0xff00) | ((c >> 16) & 255);
                    }
                }
            }
            catch (Exception e) { AppLog.Write(e); }
            if (Mode != "System") dark = Mode == "Dark";
            Color chosen = Color.FromRgb((byte)(accent >> 16), (byte)(accent >> 8), (byte)accent);
            if (AccentChoice == "Blue") chosen = Color.FromRgb(88,125,201);
            if (AccentChoice == "Purple") chosen = Color.FromRgb(138,102,194);
            if (AccentChoice == "Green") chosen = Color.FromRgb(37,131,110);
            if (AccentChoice == "Orange") chosen = Color.FromRgb(189,105,43);
            Apply(dark, chosen);
        }

        internal static void Apply(bool dark, Color accent)
        {
            Dark = dark;
            // Material 3 semantic surface roles, tinted from the user's accent.
            Color seed = accent;
            Color surface = Mix(seed, dark ? Color.FromRgb(18,18,22) : Color.FromRgb(252,250,255), dark ? .94 : .96);
            Color container = Mix(seed, dark ? Color.FromRgb(34,32,39) : Color.FromRgb(241,237,247), .94);
            Color high = Mix(seed, dark ? Color.FromRgb(49,46,56) : Color.FromRgb(232,226,240), .92);
            Color primary = Mix(seed, dark ? Colors.White : Colors.Black, dark ? .52 : .22);
            Put("Ink", dark ? Color.FromRgb(235,230,240) : Color.FromRgb(29,27,32));
            Put("Muted", dark ? Color.FromRgb(202,194,209) : Color.FromRgb(76,69,84));
            Put("Accent", primary);
            Put("WindowTint", surface); Put("Sidebar", surface); Put("Fallback", surface);
            Put("CardFace", container); Put("CardFaceSoft", container);
            Put("Surface", high); Put("Inset", container);
            Put("Edge", dark ? Color.FromRgb(76,69,84) : Color.FromRgb(204,196,214));
            Put("Hover", Mix(primary, high, .88));
            Put("Track", dark ? Color.FromRgb(79,71,88) : Color.FromRgb(214,205,225));
            Put("Knob", primary);
            Put("Selected", Mix(seed, dark ? Color.FromRgb(58,48,76) : Color.FromRgb(231,220,250), .78));
            Put("OnPrimary", dark ? Color.FromRgb(24,19,31) : Colors.White);
            // 底色自带一层极淡的强调色晕，替代原来靠桌面壁纸透出来的层次
            var glow = new RadialGradientBrush { GradientOrigin = new Point(0.86, 0.06), Center = new Point(0.86, 0.06), RadiusX = 0.95, RadiusY = 1.0 };
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1));
            glow.Freeze();
            Application.Current.Resources["PageGlow"] = glow;
            foreach (Window w in Application.Current.Windows)
                if (new System.Windows.Interop.WindowInteropHelper(w).Handle != IntPtr.Zero) Desktop.Apply(w);
        }
        static Color Mix(Color a, Color b, double t)
        {
            return Color.FromRgb((byte)(a.R*(1-t)+b.R*t), (byte)(a.G*(1-t)+b.G*t), (byte)(a.B*(1-t)+b.B*t));
        }
        static void Set(string key, string color) { Put(key, (Color)ColorConverter.ConvertFromString(color)); }
        static void Put(string key, Color c)
        {
            var old = Application.Current.Resources[key] as SolidColorBrush;
            if (old != null && old.Color == c) return;
            var brush = new SolidColorBrush(c); brush.Freeze();
            Application.Current.Resources[key] = brush;
        }
    }
}

