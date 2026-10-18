using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace lumi
{
    internal static class Desktop
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            internal int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            internal int Left, Top, Right, Bottom;
            internal bool Contains(Point p, int pad)
            {
                return p.X >= Left - pad && p.X <= Right + pad && p.Y >= Top - pad && p.Y <= Bottom + pad;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Monitor
        {
            internal int Size;
            internal Rect Bounds, Work;
            internal uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Margins
        {
            public int Left, Right, Top, Bottom;
        }

        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
        [DllImport("dwmapi.dll")]
        static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref Margins margins);
        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr h, out Rect rect);
        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromPoint(Point p, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool GetMonitorInfo(IntPtr monitor, ref Monitor info);
        [DllImport("user32.dll")]
        internal static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")]
        internal static extern bool DestroyIcon(IntPtr icon);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll")]
        internal static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
        [DllImport("user32.dll")]
        internal static extern IntPtr CreateIconFromResourceEx(byte[] data, uint size, bool icon, uint version, int width, int height, uint flags);
        internal static IntPtr Icon, SmallIcon, TrayIcon;
        internal static void LoadIcon()
        {
            ExtractIconEx(Assembly.GetExecutingAssembly().Location, 0, out Icon, out SmallIcon, 1);
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("tray.png"))
            using (var data = new System.IO.MemoryStream())
            {
                stream.CopyTo(data);
                byte[] bytes = data.ToArray();
                TrayIcon = CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, 0x30000, 32, 32, 0);
                if (TrayIcon == IntPtr.Zero) throw new InvalidOperationException("无法加载托盘图标");
            }
        }

        internal static void ReleaseIcons()
        {
            if (TrayIcon != IntPtr.Zero) DestroyIcon(TrayIcon);
            if (Icon != IntPtr.Zero)
                DestroyIcon(Icon);
            if (SmallIcon != IntPtr.Zero)
                DestroyIcon(SmallIcon);
        }

        internal static void Configure(Window w, bool compact)
        {
            // Keep a normal composited HWND: no layered WPF window or bitmap-shadow visual trees.
            WindowChrome.SetWindowChrome(w, new WindowChrome { CaptionHeight = compact ? 0 : 64, ResizeBorderThickness = new Thickness(compact ? 0 : 6), CornerRadius = new CornerRadius(0), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);
            RenderOptions.SetClearTypeHint(w, ClearTypeHint.Enabled);
            TextOptions.SetTextRenderingMode(w, TextRenderingMode.ClearType);
            TextOptions.SetTextHintingMode(w, TextHintingMode.Fixed);
            w.UseLayoutRounding = true;
            w.SnapsToDevicePixels = true;
            if (Icon != IntPtr.Zero)
                w.Icon = Imaging.CreateBitmapSourceFromHIcon(Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            w.SourceInitialized += delegate
            {
                Apply(w);
            };
        }

        internal static int Apply(Window w)
        {
            IntPtr h = new WindowInteropHelper(w).Handle;
            var source = HwndSource.FromHwnd(h);
            if (source != null)
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
            var margins = new Margins
            {
                Left = 0,
                Right = 0,
                Top = 0,
                Bottom = 0
            };
            DwmExtendFrameIntoClientArea(h, ref margins);
            int corner = 2, light = SystemTheme.Dark ? 1 : 0, backdrop = 3, border = unchecked((int)0xFFFFFFFF);
            DwmSetWindowAttribute(h, 33, ref corner, 4);
            DwmSetWindowAttribute(h, 20, ref light, 4);
            DwmSetWindowAttribute(h, 34, ref border, 4);
            int result = DwmSetWindowAttribute(h, 38, ref backdrop, 4);
            if (result != 0)
                w.SetResourceReference(Window.BackgroundProperty, "Fallback");
            return result;
        }

        [DllImport("shcore.dll")]
        static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);

        // All rectangles are physical pixels; alignment follows the actual tray icon.
        internal static Rect PopupBounds(Rect anchor, Rect bounds, Rect work, int width, int height, int gap)
        {
            width = Math.Min(width, Math.Max(1, work.Right - work.Left - 2 * gap));
            height = Math.Min(height, Math.Max(1, work.Bottom - work.Top - 2 * gap));
            int x = (anchor.Left + anchor.Right - width) / 2;
            int y = anchor.Top - height - gap;
            if (anchor.Left >= work.Right && work.Right < bounds.Right) {
                x = work.Right - width - gap; y = (anchor.Top + anchor.Bottom - height) / 2;
            } else if (anchor.Right <= work.Left && work.Left > bounds.Left) {
                x = work.Left + gap; y = (anchor.Top + anchor.Bottom - height) / 2;
            } else if (anchor.Bottom <= work.Top && work.Top > bounds.Top) {
                y = work.Top + gap;
            } else if (anchor.Top >= work.Bottom && work.Bottom < bounds.Bottom) {
                y = work.Bottom - height - gap;
            } else if (y < work.Top + gap) {
                y = anchor.Bottom + gap;
            }
            x = Math.Max(work.Left + gap, Math.Min(x, work.Right - width - gap));
            y = Math.Max(work.Top + gap, Math.Min(y, work.Bottom - height - gap));
            return new Rect { Left=x, Top=y, Right=x+width, Bottom=y+height };
        }
        internal static void Place(Window w, Rect anchor)
        {
            var point = new Point { X=(anchor.Left+anchor.Right)/2, Y=(anchor.Top+anchor.Bottom)/2 };
            IntPtr monitor = MonitorFromPoint(point, 2);
            var info = new Monitor { Size=Marshal.SizeOf(typeof(Monitor)) };
            if (!GetMonitorInfo(monitor, ref info)) return;
            IntPtr h = new WindowInteropHelper(w).Handle;
            uint dx, dy; double scale;
            try { scale = GetDpiForMonitor(monitor, 0, out dx, out dy) == 0 ? dx / 96.0 : GetDpiForWindow(h) / 96.0; }
            catch (EntryPointNotFoundException) { scale=GetDpiForWindow(h)/96.0; }
            if (scale <= 0) scale=1;
            int gap=(int)Math.Round(8*scale);
            Rect r=PopupBounds(anchor, info.Bounds, info.Work, (int)Math.Ceiling(w.Width*scale), (int)Math.Ceiling(w.Height*scale), gap);
            // No intermediate jump to the monitor's top-left corner.
            SetWindowPos(h, new IntPtr(-1), r.Left, r.Top, r.Right-r.Left, r.Bottom-r.Top, 0x10);
        }
    }
}
