using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace lumi
{
    internal static class UiTests
    {
        static void Check(bool condition, string name, StringBuilder log)
        {
            if (!condition)
                throw new Exception(name);
            log.AppendLine("PASS " + name);
        }

        internal static void Capture(Window w, string path)
        {
            w.UpdateLayout();
            var image = new RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            image.Render(w);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(image));
            using (var file = File.Create(path))
                png.Save(file);
        }

        internal static async Task Run(AppHost host, string dir)
        {
            Directory.CreateDirectory(dir);
            var log = new StringBuilder();
            var service = host.Service;
            host.ShowMain();
            await Task.Delay(250);
            Check(service.Screens.Count > 0, "active displays discovered", log);
            var w = host.Main.Window;
            int material, corner;
            var handle = new WindowInteropHelper(w).Handle;
            Check(Desktop.DwmGetWindowAttribute(handle, 38, out material, 4) == 0 && material == 3, "native Acrylic backdrop accepted", log);
            Check(Desktop.DwmGetWindowAttribute(handle, 33, out corner, 4) == 0 && corner == 2, "native rounded corners accepted", log);
            Check(!w.AllowsTransparency, "non-layered hardware-composited window", log);
            Check(TextOptions.GetTextFormattingMode(w) == TextFormattingMode.Display, "pixel-aligned Display text formatting", log);
            var glyph = Views.Get<System.Windows.Shapes.Path>(w, "MaxGlyph");
            string normalGlyph = glyph.Data.ToString();
            Views.Get<Button>(w, "Maximize").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(180);
            Check(w.WindowState == WindowState.Maximized && glyph.Data.ToString() != normalGlyph, "maximize button switches to vector restore icon", log);
            Views.Get<Button>(w, "Maximize").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(100);
            Check(w.WindowState == WindowState.Normal && glyph.Data.ToString() == normalGlyph, "restore button restores normal window and icon", log);
            Views.Get<Button>(w, "Minimize").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(w.WindowState == WindowState.Minimized, "minimize button works", log);
            w.WindowState = WindowState.Normal;
            w.Activate();
            foreach (string button in new[] { "Minimize", "Maximize", "Close" })
                Check(Views.Get<Button>(w, button).ActualWidth == 46 && Views.Get<Button>(w, button).ActualHeight == 40, button + " has consistent caption hit area", log);
            var model = service.Screens.First(x => x.CanBrightness);
            Views.Get<ListBox>(w, "Screens").SelectedItem = model;
            var slider = Views.Get<Slider>(w, "Brightness");
            int initial = model.Brightness;
            int writes = service.WriteCount;
            int ticks = 0;
            double gap = 0;
            var watch = Stopwatch.StartNew();
            long last = 0;
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            timer.Tick += delegate
            {
                long now = watch.ElapsedMilliseconds;
                gap = Math.Max(gap, now - last);
                last = now;
                ticks++;
            };
            timer.Start();
            Exception failure = null;
            try
            {
                for (int i = 0; i < 80; i++)
                {
                    slider.Value = i % 2 == 0 ? Math.Max(0, initial - 1) : initial;
                    await Task.Delay(10);
                }

                await service.Flush();
                Check(Object.ReferenceEquals(slider, Views.Get<Slider>(w, "Brightness")), "stable slider instance during writes", log);
                Check(ticks > 10 && gap < 300, "responsive UI: max dispatcher gap=" + gap + "ms", log);
            }
            catch (Exception e)
            {
                failure = e;
            }
            finally
            {
                timer.Stop();
            }

            model.Brightness = initial;
            await service.Flush();
            Check(await Task.Run(() => model.Device.Read(false)) == initial, "last requested brightness confirmed and restored", log);
            if (failure != null)
                throw failure;
            log.AppendLine("80 inputs -> " + (service.WriteCount - writes) + " hardware writes");
            var unsupported = service.Screens.FirstOrDefault(x => !x.CanContrast);
            if (unsupported != null)
            {
                Views.Get<ListBox>(w, "Screens").SelectedItem = unsupported;
                await Task.Delay(50);
                Check(!Views.Get<Slider>(w, "Contrast").IsEnabled, "unsupported contrast remains disabled", log);
            }

            Views.Get<ListBox>(w, "Screens").SelectedItem = model;
            host.Main.Page("Settings");
            Check(Views.Get<ScrollViewer>(w, "SettingsPage").Visibility == Visibility.Visible, "settings navigation", log);
            host.Main.Page("Display");
            Capture(w, Path.Combine(dir, "main-layout.png"));
            host.OpenQuick(true);
            await Task.Delay(200);
            Check(host.Quick != null && host.Quick.Window.IsVisible, "click opens quick panel", log);
            Check(Object.ReferenceEquals(w.DataContext, host.Quick.Window.DataContext), "main and quick panel share a single state service", log);
            Check(host.Quick.Window.Width == 310 && host.Quick.Window.Height == Math.Min(430, 66 + Math.Max(1, service.Screens.Count) * 80), "compact panel adapts to screen count (" + host.Quick.Window.Width + "x" + host.Quick.Window.Height + ")", log);
            Check(host.Quick.Window.FindName("P25") == null, "quick panel contains no preset clutter", log);
            try
            {
                SystemTheme.Apply(true, Color.FromRgb(190, 80, 150));
                await Task.Delay(120);
                int dark;
                Check(Desktop.DwmGetWindowAttribute(handle, 20, out dark, 4) == 0 && dark == 1, "dark palette updates native backdrop", log);
                Check(((SolidColorBrush)Views.Get<Button>(w, "Minimize").Foreground).Color.R == 240, "caption foreground follows dark theme", log);
                Check(Object.ReferenceEquals(w.FindResource("Accent"), host.Quick.Window.FindResource("Accent")), "both windows share live accent brush", log);
                Capture(host.Quick.Window, Path.Combine(dir, "quick-dark.png"));
                SystemTheme.Apply(false, Color.FromRgb(40, 130, 100));
                await Task.Delay(120);
                Check(((SolidColorBrush)Views.Get<Button>(w, "Minimize").Foreground).Color.R == 37, "caption foreground follows light theme", log);
                Capture(host.Quick.Window, Path.Combine(dir, "quick-light.png"));
            }
            finally { SystemTheme.Reload(); }
            Desktop.Rect rect;
            Desktop.GetWindowRect(new WindowInteropHelper(host.Quick.Window).Handle, out rect);
            Check(rect.Right > rect.Left && rect.Bottom > rect.Top, "quick panel has valid native placement", log);
            Capture(host.Quick.Window, Path.Combine(dir, "quick-layout.png"));
            host.HideQuick();
            Check(host.Quick == null, "dismiss releases quick window", log);
            Views.Get<Button>(w, "Close").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(host.Main == null, "closing main window releases view reference", log);
            await Task.Delay(800);
            host.OpenQuick(false);
            Check(host.Quick != null && !host.Quick.Window.ShowActivated, "hover opens without activation", log);
            host.HideQuick();
            await Task.Delay(300);
            Check(host.TimersIdle, "no running timers or hardware polling when hidden", log);
            host.ShowMain();
            Check(host.Main != null, "main window lazily recreates", log);
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "System.Windows.Forms"), "WinForms runtime not loaded", log);
            File.WriteAllText(Path.Combine(dir, "ui-tests.txt"), log.ToString());
            service.Message("交互测试通过 · 所有测试亮度已恢复");
        }
    }
}


