using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace lumi
{
    // Native notification icon. No WinForms UI runtime, global mouse hook or resident polling.
    internal sealed class TrayService : IDisposable
    {
        const int Callback = 0x8001;
        readonly HwndSource source;
        readonly Guid id = new Guid("6F93D1B5-0475-4C37-8CB3-62D0D642CDDA");
        readonly uint taskbarCreated;
        readonly DispatcherTimer hover;
        Desktop.Rect lastAnchor;
        internal event Action<bool> Open;
        internal event Action Leave;
        internal event Action ShowMain;
        internal event Action Quit;
        internal static readonly uint ShowMessage = Desktop.RegisterWindowMessage("lumi.Show.3");
        internal static readonly uint QuickMessage = Desktop.RegisterWindowMessage("lumi.Quick.3");
        internal static readonly uint ExitMessage = Desktop.RegisterWindowMessage("lumi.Exit.3");
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct Data
        {
            internal uint Size;
            internal IntPtr Hwnd;
            internal uint Id, Flags, Message;
            internal IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            internal string Tip;
            internal uint State, StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            internal string Info;
            internal uint Version;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            internal string Title;
            internal uint InfoFlags;
            internal Guid Guid;
            internal IntPtr Balloon;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Identifier
        {
            internal uint Size;
            internal IntPtr Hwnd;
            internal uint Id;
            internal Guid Guid;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern bool Shell_NotifyIcon(uint action, ref Data data);
        [DllImport("shell32.dll")]
        static extern int Shell_NotifyIconGetRect(ref Identifier id, out Desktop.Rect rect);
        internal TrayService()
        {
            source = new HwndSource(new HwndSourceParameters("lumi Tray Host") { Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x80 });
            source.AddHook(Hook);
            taskbarCreated = Desktop.RegisterWindowMessage("TaskbarCreated");
            hover = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            hover.Tick += delegate
            {
                hover.Stop();
                Desktop.Point p;
                Desktop.GetCursorPos(out p);
                if (Anchor().Contains(p, 2) && Open != null)
                    Open(false);
            };
            try
            {
                if (System.IO.File.Exists(TrayIconSettings.FilePath))
                {
                    byte[] bytes = TrayIconSettings.Read(TrayIconSettings.FilePath);
                    var icon = Desktop.CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, 0x30000, 32, 32, 0);
                    if (icon != IntPtr.Zero) { Desktop.DestroyIcon(Desktop.TrayIcon); Desktop.TrayIcon = icon; }
                }
            }
            catch (Exception e) { AppLog.Write(e); }
            Add();
        }

        Data Packet()
        {
            return new Data
            {
                Size = (uint)Marshal.SizeOf(typeof(Data)),
                Hwnd = source.Handle,
                Id = 1,
                Flags = 1 | 2 | 4 | 0x20,
                Message = Callback,
                Icon = Desktop.TrayIcon,
                Tip = "lumi · 悬停或点击调节亮度",
                Guid = id
            };
        }

        void Add()
        {
            var d = Packet();
            if (!Shell_NotifyIcon(0, ref d))
                AppLog.Write(new Exception("无法创建系统托盘图标"));
            d.Version = 4;
            Shell_NotifyIcon(4, ref d);
        }

        internal Desktop.Rect Anchor()
        {
            var identifier = new Identifier
            {
                Size = (uint)Marshal.SizeOf(typeof(Identifier)),
                Hwnd = source.Handle,
                Id = 1,
                Guid = id
            };
            Desktop.Rect rect;
            if (Shell_NotifyIconGetRect(ref identifier, out rect) == 0)
            {
                lastAnchor = rect;
                return rect;
            }

            Desktop.Point p;
            Desktop.GetCursorPos(out p);
            lastAnchor = new Desktop.Rect
            {
                Left = p.X - 10,
                Right = p.X + 10,
                Top = p.Y - 10,
                Bottom = p.Y + 10
            };
            return lastAnchor;
        }

        IntPtr Hook(IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled)
        {
            if (message == 0x001A || message == 0x0320) SystemTheme.Reload();
            if (message == taskbarCreated)
            {
                Add();
                return IntPtr.Zero;
            }

            if (message == ShowMessage)
            {
                if (ShowMain != null)
                    ShowMain();
            }

            if (message == QuickMessage)
            {
                if (Open != null)
                    Open(w != IntPtr.Zero);
            }

            if (message == ExitMessage)
            {
                if (Quit != null)
                    Quit();
            }

            if (message != Callback)
                return IntPtr.Zero;
            int evt = (int)((long)l & 0xffff);
            if (evt == 0x400 || evt == 0x401)
            {
                hover.Stop();
                if (Open != null)
                    Open(true);
            }
            else if (evt == 0x406)
            {
                hover.Stop();
                if (Open != null)
                    Open(false);
            }
            else if (evt == 0x407)
            {
                hover.Stop();
                if (Leave != null)
                    Leave();
            }
            else if (evt == 0x200)
            {
                if (!hover.IsEnabled)
                    hover.Start();
            }
            else if (evt == 0x7B || evt == 0x205)
            {
                hover.Stop();
                var menu = new ContextMenu();
                var main = new MenuItem
                {
                    Header = "打开显示控制台"
                };
                main.Click += delegate
                {
                    if (ShowMain != null)
                        ShowMain();
                };
                var exit = new MenuItem
                {
                    Header = "退出 lumi"
                };
                exit.Click += delegate
                {
                    if (Quit != null)
                        Quit();
                };
                menu.Items.Add(main);
                menu.Items.Add(new Separator());
                menu.Items.Add(exit);
                Desktop.SetForegroundWindow(source.Handle);
                menu.IsOpen = true;
            }

            handled = true;
            return IntPtr.Zero;
        }

        internal void ChangeIcon(string path)
        {
            byte[] bytes;
            if (path != null) bytes = TrayIconSettings.Read(path);
            else
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("tray.png"))
                using (var data = new System.IO.MemoryStream()) { stream.CopyTo(data); bytes = data.ToArray(); }
            ChangeIconBytes(bytes, path == null);
        }

        internal void ChangeIconBytes(byte[] bytes, bool reset)
        {
            var next = Desktop.CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, 0x30000, 32, 32, 0);
            if (next == IntPtr.Zero) throw new InvalidOperationException("无法读取此图标，请选择其他图片。");
            var previous = Desktop.TrayIcon;
            Desktop.TrayIcon = next;
            try
            {
                var packet = Packet();
                if (!Shell_NotifyIcon(1, ref packet)) throw new InvalidOperationException("托盘更新失败，请稍后重试。");
                if (reset) System.IO.File.Delete(TrayIconSettings.FilePath);
                else TrayIconSettings.Save(bytes);
            }
            catch
            {
                Desktop.TrayIcon = previous;
                var packet = Packet(); Shell_NotifyIcon(1, ref packet);
                Desktop.DestroyIcon(next);
                throw;
            }
            Desktop.DestroyIcon(previous);
        }

        public void Dispose()
        {
            hover.Stop();
            var d = Packet();
            Shell_NotifyIcon(2, ref d);
            source.Dispose();
        }
    }
}

