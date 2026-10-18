using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace lumi
{
    // The UI owns these models. Hardware objects are accessed only through DisplayService.
    internal sealed class ScreenModel : INotifyPropertyChanged
    {
        internal readonly Display Device;
        internal readonly DisplayService Service;
        internal long BrightVersion, ContrastVersion;
        int brightness, contrast;
        bool hdr;
        public event PropertyChangedEventHandler PropertyChanged;
        internal ScreenModel(Display d, DisplayService service)
        {
            Device = d;
            Service = service;
            brightness = d.Brightness;
            contrast = d.Contrast;
            hdr = d.HdrEnabled;
        }

        public string Name
        {
            get
            {
                return Device.Name;
            }
        }

        public string Detail
        {
            get
            {
                return Device.WmiId != null ? "内置屏幕 · 系统背光" : "外接屏幕 · DDC/CI";
            }
        }

        public bool CanBrightness
        {
            get
            {
                return Device.BrightnessSupported;
            }
        }

        public bool CanContrast
        {
            get
            {
                return Device.ContrastSupported;
            }
        }

        public bool CanHdr
        {
            get
            {
                return Device.HdrKnown && Device.HdrSupported && !Device.HdrLimited;
            }
        }

        public string ContrastHint
        {
            get
            {
                return CanContrast ? (Device.HdrActive ? "HDR 模式可能限制硬件对比度" : "调节显示器的明暗对比") : "此屏幕未提供硬件对比度接口";
            }
        }

        public string HdrHint
        {
            get
            {
                return Device.HdrReason;
            }
        }

        public string Mode
        {
            get
            {
                return Device.HdrActive ? "HDR · " + Device.Bits + " BIT" : "SDR · " + Device.Bits + " BIT";
            }
        }

        public int Brightness
        {
            get
            {
                return brightness;
            }

            set
            {
                value = Math.Max(0, Math.Min(100, value));
                if (value == brightness)
                    return;
                if (Service.Busy)
                {
                    Notify("Brightness");
                    return;
                }

                brightness = value;
                Notify("Brightness");
                Service.Request(this, false, value, ++BrightVersion);
            }
        }

        public int Contrast
        {
            get
            {
                return contrast;
            }

            set
            {
                value = Math.Max(0, Math.Min(100, value));
                if (value == contrast)
                    return;
                if (Service.Busy)
                {
                    Notify("Contrast");
                    return;
                }

                contrast = value;
                Notify("Contrast");
                Service.Request(this, true, value, ++ContrastVersion);
            }
        }

        public bool HdrEnabled
        {
            get
            {
                return hdr;
            }
        }

        internal void Accept(bool c, int value, long version)
        {
            if (c)
            {
                Device.Contrast = value;
                if (version == ContrastVersion)
                {
                    contrast = value;
                    Notify("Contrast");
                }
            }
            else
            {
                Device.Brightness = value;
                if (version == BrightVersion)
                {
                    brightness = value;
                    Notify("Brightness");
                }
            }
        }

        internal void UpdateHdr()
        {
            hdr = Device.HdrEnabled;
            Notify("HdrEnabled");
            Notify("CanHdr");
            Notify("HdrHint");
            Notify("Mode");
            Notify("ContrastHint");
        }

        internal void Notify(string name)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }

    internal sealed class PendingChange
    {
        internal ScreenModel Screen;
        internal bool Contrast;
        internal int Value;
        internal long Version;
        internal string Key
        {
            get
            {
                return Screen.Device.Key + (Contrast ? ":c" : ":b");
            }
        }
    }

    internal sealed class DisplayService : INotifyPropertyChanged, IDisposable
    {
        readonly Backend backend = new Backend();
        readonly SemaphoreSlim io = new SemaphoreSlim(1, 1);
        readonly Dictionary<string, PendingChange> pending = new Dictionary<string, PendingChange>();
        readonly DispatcherTimer delay;
        Task pump = Task.FromResult(0);
        bool busy, refreshing, disposed;
        DateTime lastRead = DateTime.MinValue;
        string status = "正在检测显示设备…";
        public ObservableCollection<ScreenModel> Screens { get; private set; }

        public event PropertyChangedEventHandler PropertyChanged;
        public event Action Rebuilt;
        public bool Busy
        {
            get
            {
                return busy;
            }

            private set
            {
                busy = value;
                Notify("Busy");
                Notify("Ready");
            }
        }

        public bool Ready
        {
            get
            {
                return !busy;
            }
        }

        public string Status
        {
            get
            {
                return status;
            }

            private set
            {
                status = value;
                Notify("Status");
            }
        }

        internal int WriteCount;
        internal bool Idle
        {
            get
            {
                return !busy && !refreshing && pump.IsCompleted && pending.Count == 0 && !delay.IsEnabled;
            }
        }

        public DisplayService()
        {
            Screens = new ObservableCollection<ScreenModel>();
            delay = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(70)
            };
            delay.Tick += async delegate
            {
                delay.Stop();
                await Flush();
            };
        }

        void Notify(string name)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(name));
        }

        internal void Message(string value)
        {
            Status = value;
        }

        internal void Request(ScreenModel screen, bool contrast, int value, long version)
        {
            if (disposed || Busy || contrast && !screen.CanContrast || !contrast && !screen.CanBrightness)
                return;
            var c = new PendingChange
            {
                Screen = screen,
                Contrast = contrast,
                Value = value,
                Version = version
            };
            pending[c.Key] = c;
            if (!delay.IsEnabled && pump.IsCompleted)
                delay.Start();
            Status = "正在调节 · " + value + "%";
        }

        internal Task Flush()
        {
            delay.Stop();
            if (pump.IsCompleted)
                pump = Pump();
            return pump;
        }

        async Task Pump()
        {
            bool failed = false;
            while (pending.Count > 0)
            {
                foreach (string key in pending.Keys.ToArray())
                {
                    PendingChange c;
                    if (!pending.TryGetValue(key, out c))
                        continue;
                    pending.Remove(key);
                    await io.WaitAsync();
                    int actual = 0;
                    Exception error = null;
                    try
                    {
                        actual = await Task.Run(() =>
                        {
                            c.Screen.Device.Set(c.Contrast, c.Value);
                            return c.Screen.Device.Read(c.Contrast);
                        });
                        WriteCount++;
                    }
                    catch (Exception e)
                    {
                        error = e;
                    }
                    finally
                    {
                        io.Release();
                    }

                    if (error == null)
                    {
                        c.Screen.Accept(c.Contrast, actual, c.Version);
                        if (Math.Abs(actual - c.Value) > 2)
                        {
                            failed = true;
                            Status = "设备返回 " + actual + "%，当前模式可能限制调节。";
                        }
                    }
                    else
                    {
                        failed = true;
                        AppLog.Write(error);
                        Status = "调节失败：" + error.Message;
                        c.Screen.Accept(c.Contrast, c.Contrast ? c.Screen.Device.Contrast : c.Screen.Device.Brightness, c.Version);
                    }
                }

                if (pending.Count > 0)
                    await Task.Delay(70);
            }

            if (!failed)
                Status = "已应用 · 已确认设备读数";
        }

        public async Task Scan()
        {
            if (disposed || Busy)
                return;
            Busy = true;
            Status = "正在检测显示设备…";
            await Flush();
            await io.WaitAsync();
            try
            {
                await Task.Run(() => backend.Scan());
                Screens.Clear();
                foreach (var d in backend.Displays)
                    Screens.Add(new ScreenModel(d, this));
                lastRead = DateTime.UtcNow;
                Status = Screens.Count + " 块屏幕已连接";
                if (Rebuilt != null)
                    Rebuilt();
            }
            catch (Exception e)
            {
                AppLog.Write(e);
                Screens.Clear();
                if (Rebuilt != null)
                    Rebuilt();
                Status = "检测失败：" + e.Message;
            }
            finally
            {
                io.Release();
                Busy = false;
            }
        }

        public async Task RefreshReadings()
        {
            if (disposed || Busy || refreshing || DateTime.UtcNow - lastRead < TimeSpan.FromSeconds(10))
                return;
            refreshing = true;
            lastRead = DateTime.UtcNow;
            await io.WaitAsync();
            try
            {
                foreach (var s in Screens.ToArray())
                {
                    long bv = s.BrightVersion, cv = s.ContrastVersion;
                    int b = s.Brightness, c = s.Contrast;
                    await Task.Run(() =>
                    {
                        if (s.CanBrightness)
                            b = s.Device.Read(false);
                        if (s.CanContrast)
                            c = s.Device.Read(true);
                        s.Device.ReadHdr();
                    });
                    s.Accept(false, b, bv);
                    s.Accept(true, c, cv);
                    s.UpdateHdr();
                }
            }
            catch (Exception e)
            {
                AppLog.Write(e);
                Status = "读取失败，请重新检测连接。";
            }
            finally
            {
                io.Release();
                refreshing = false;
            }
        }

        public async Task SetHdr(ScreenModel screen, bool enabled)
        {
            if (screen == null || Busy || disposed)
                return;
            Busy = true;
            await Flush();
            await io.WaitAsync();
            try
            {
                Status = "正在切换 HDR，屏幕可能短暂闪黑…";
                await Task.Run(() => screen.Device.SetHdr(enabled));
                Status = screen.Device.HdrActive ? "HDR 已开启并确认生效" : "HDR 已关闭";
            }
            catch (Exception e)
            {
                AppLog.Write(e);
                Status = "HDR：" + e.Message;
            }
            finally
            {
                screen.UpdateHdr();
                io.Release();
                Busy = false;
                lastRead = DateTime.MinValue;
            }
        }

        internal string HardwareSummary { get { return backend.Hardware; } }
        public async Task<string> Diagnostics()
        {
            await io.WaitAsync();
            try
            {
                await Task.Run(() => backend.LoadHardware());
                return backend.Report();
            }
            finally
            {
                io.Release();
            }
        }

        public void Dispose()
        {
            disposed = true;
            delay.Stop();
            backend.Dispose();
            io.Dispose();
        }
    }
}
