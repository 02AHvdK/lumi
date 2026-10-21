using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace lumi
{
    internal static class AppLog
    {
        internal static void Write(Exception e)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lumi");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "luma.log");
                if (File.Exists(file) && new FileInfo(file).Length > 131072)
                    File.WriteAllText(file, "");
                File.AppendAllText(file, DateTimeOffset.Now.ToString("o") + " " + e + Environment.NewLine);
            }
            catch
            {
            }
        }
    }

    internal sealed class AppHost
    {
        internal readonly DisplayService Service = new DisplayService();
        internal MainView Main;
        internal QuickView Quick;
        internal readonly TrayService Tray;
        readonly DispatcherTimer leaveTimer, refreshTimer;
        bool clicked, quitting;
        DateTime outside = DateTime.MinValue, cooldown = DateTime.MinValue;
        Rect? placement;
        internal AppHost()
        {
            Desktop.LoadIcon();
            Tray = new TrayService();
            Tray.Open += OpenQuick;
            Tray.Leave += delegate
            {
                outside = DateTime.UtcNow;
            };
            Tray.ShowMain += ShowMain;
            Tray.Quit += async delegate
            {
                await Exit();
            };
            leaveTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            leaveTimer.Tick += delegate
            {
                CheckHover();
            };
            refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(1500)
            };
            refreshTimer.Tick += async delegate
            {
                if (!Service.Idle)
                    return;
                refreshTimer.Stop();
                await Service.Scan();
            };
            SystemEvents.DisplaySettingsChanged += DisplayChanged;
            SystemEvents.PowerModeChanged += PowerChanged;
        }

        internal async Task Start(bool tray)
        {
            if (!tray)
                ShowMain();
            await Service.Scan();
        }

        internal void ShowMain()
        {
            if (quitting)
                return;
            HideQuick();
            if (Main == null)
            {
                Main = new MainView(Service, this);
                var w = Main.Window;
                if (placement.HasValue)
                {
                    var r = placement.Value;
                    w.WindowStartupLocation = WindowStartupLocation.Manual;
                    w.Left = r.Left;
                    w.Top = r.Top;
                    w.Width = r.Width;
                    w.Height = r.Height;
                }

                w.Closed += delegate
                {
                    if (w.WindowState == WindowState.Normal)
                        placement = new Rect(w.Left, w.Top, w.Width, w.Height);
                    if (Application.Current.MainWindow == w)
                        Application.Current.MainWindow = null;
                    w.DataContext = null;
                    w.Content = null;
                    Main = null;
                };
                w.Show();
            }
            else
            {
                Main.Window.Show();
                if (Main.Window.WindowState == WindowState.Minimized)
                    Main.Window.WindowState = WindowState.Normal;
            }

            Main.Window.Activate();
            RefreshSoon();
        }

        internal void OpenQuick(bool activate)
        {
            if (quitting || !activate && DateTime.UtcNow < cooldown)
                return;
            if (Quick != null && Quick.Window.IsVisible)
            {
                if (activate)
                {
                    if (clicked)
                    {
                        HideQuick();
                        return;
                    }

                    QuickEngaged();
                    Quick.Window.Activate();
                }

                return;
            }

            clicked = activate;
            outside = DateTime.MinValue;
            Quick = new QuickView(Service, this);
            var w = Quick.Window;
            w.Height = Math.Min(430, 66 + Math.Max(1, Service.Screens.Count) * 80);
            w.ShowActivated = activate;
            new WindowInteropHelper(w).EnsureHandle();
            Desktop.Place(w, Tray.Anchor());
            w.Show();
            Desktop.Place(w, Tray.Anchor());
            if (activate)
            {
                w.Activate();
                Desktop.SetForegroundWindow(new WindowInteropHelper(w).Handle);
            }

            leaveTimer.Start();
            RefreshSoon();
        }

        async void RefreshSoon()
        {
            try
            {
                await Service.RefreshReadings();
            }
            catch (Exception e)
            {
                AppLog.Write(e);
            }
        }

        internal void QuickEngaged()
        {
            clicked = true;
            outside = DateTime.MinValue;
            leaveTimer.Stop();
        }

        internal void QuickDeactivated()
        {
            if (clicked)
                HideQuick();
        }

        internal void HideQuick()
        {
            leaveTimer.Stop();
            outside = DateTime.MinValue;
            cooldown = DateTime.UtcNow.AddMilliseconds(700);
            if (Quick == null)
                return;
            var w = Quick.Window;
            Quick = null;
            w.Close();
            if (Application.Current.MainWindow == w)
                Application.Current.MainWindow = null;
            w.DataContext = null;
            w.Content = null;
            clicked = false;
        }

        internal void CheckHover()
        {
            if (Quick == null)
            {
                leaveTimer.Stop();
                return;
            }

            if (clicked)
            {
                leaveTimer.Stop();
                return;
            }

            Desktop.Point point;
            Desktop.GetCursorPos(out point);
            Desktop.Rect rect;
            Desktop.GetWindowRect(new WindowInteropHelper(Quick.Window).Handle, out rect);
            if (rect.Contains(point, 7) || Tray.Anchor().Contains(point, 6) || Mouse.Captured != null)
            {
                outside = DateTime.MinValue;
                return;
            }

            if (outside == DateTime.MinValue)
                outside = DateTime.UtcNow;
            if (DateTime.UtcNow - outside > TimeSpan.FromMilliseconds(450))
                HideQuick();
        }

        void DisplayChanged(object s, EventArgs e)
        {
            if (quitting)
                return;
            Application.Current.Dispatcher.BeginInvoke(new Action(delegate
            {
                if (!quitting)
                {
                    refreshTimer.Stop();
                    refreshTimer.Start();
                }
            }));
        }

        void PowerChanged(object s, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
                DisplayChanged(s, EventArgs.Empty);
        }

        internal async Task Exit()
        {
            if (quitting)
                return;
            quitting = true;
            leaveTimer.Stop();
            refreshTimer.Stop();
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            SystemEvents.PowerModeChanged -= PowerChanged;
            while (!Service.Idle)
            {
                await Service.Flush();
                await Task.Delay(50);
            }

            HideQuick();
            if (Main != null)
                Main.Window.Close();
            Tray.Dispose();
            Service.Dispose();
            Desktop.ReleaseIcons();
            Application.Current.Shutdown();
        }

        internal bool TimersIdle
        {
            get
            {
                return !leaveTimer.IsEnabled && !refreshTimer.IsEnabled && Service.Idle;
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                Run(args);
            }
            catch (Exception e)
            {
                AppLog.Write(e);
                MessageBox.Show("无法启动 lumi：" + e.Message, "lumi");
                Environment.ExitCode = 1;
            }
        }

        static void Run(string[] args)
        {
            AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);
            if (args.Length > 1 && args[0] == "--layout-test") { LayoutTests.Run(args[1]); return; }
            if (args.Length > 0 && args[0] == "--diagnose")
            {
                using (var b = new Backend())
                {
                    b.Scan();
                    b.LoadHardware();
                    File.WriteAllText(args.Length > 1 ? args[1] : "lumi-diagnostics.txt", b.Report());
                }

                return;
            }

            if (args.Length > 0 && args[0] == "--test")
            {
                SelfTest.Run(args.Length > 1 ? args[1] : "lumi-tests.txt");
                return;
            }

            bool created;
            using (var mutex = new Mutex(true, "Local\\lumi.ScreenControl", out created))
            {
                if (!created)
                {
                    Desktop.PostMessage(new IntPtr(0xffff), TrayService.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                    Desktop.PostMessage(new IntPtr(0xffff), Desktop.RegisterWindowMessage("lumi.Show.2"), IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                var app = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                app.DispatcherUnhandledException += delegate (object s, DispatcherUnhandledExceptionEventArgs e)
                {
                    AppLog.Write(e.Exception);
                    e.Handled = true;
                };
                var host = new AppHost();
                app.Startup += async delegate
                {
                    await host.Start(args.Contains("--tray"));
                    if (args.Length > 1 && args[0] == "--ui-test")
                    {
                        try
                        {
                            await UiTests.Run(host, args[1]);
                        }
                        catch (Exception e)
                        {
                            AppLog.Write(e);
                            Directory.CreateDirectory(args[1]);
                            File.WriteAllText(Path.Combine(args[1], "ui-tests.txt"), "FAIL " + e);
                        }
                    }
                };
                app.Run();
            }
        }
    }
}

