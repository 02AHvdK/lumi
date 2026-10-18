using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace lumi
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Luid
        {
            public uint Low;
            public int High;
            public override string ToString()
            {
                return High + ":" + Low;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Source
        {
            public Luid Adapter;
            public uint Id, Mode, Status;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Target
        {
            public Luid Adapter;
            public uint Id, Mode, Technology, Rotation, Scaling, RefreshN, RefreshD, Scan;
            public int Available;
            public uint Status;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PathInfo
        {
            public Source Source;
            public Target Target;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Explicit, Size = 64)]
        internal struct ModeInfo
        {
            [FieldOffset(0)]
            public uint Type;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Header
        {
            public uint Type, Size;
            public Luid Adapter;
            public uint Id;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct SourceName
        {
            public Header Header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string Name;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct TargetName
        {
            public Header Header;
            public uint Flags, Technology;
            public ushort Maker, Product;
            public uint Connector;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DevicePath;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ColorInfo
        {
            public Header Header;
            public uint Flags, Encoding, Bits, ActiveMode;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct State
        {
            public Header Header;
            public uint Value;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct Physical
        {
            public IntPtr Handle;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Name;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MonitorInfo
        {
            public uint Size;
            public Rect Monitor, Work;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string Device;
        }

        internal delegate bool MonitorCallback(IntPtr h, IntPtr dc, IntPtr rect, IntPtr data);
        [DllImport("user32.dll")]
        internal static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")]
        internal static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] PathInfo[] path, ref uint modes, [Out] ModeInfo[] mode, IntPtr topology);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        internal static extern int GetSource(ref SourceName value);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        internal static extern int GetTarget(ref TargetName value);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        internal static extern int GetColor(ref ColorInfo value);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")]
        internal static extern int SetState(ref State value);
        [DllImport("user32.dll")]
        internal static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr rect, MonitorCallback callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern bool GetMonitorInfo(IntPtr handle, ref MonitorInfo info);
        [DllImport("dxva2.dll")]
        internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr handle, out uint count);
        [DllImport("dxva2.dll", CharSet = CharSet.Unicode)]
        internal static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr handle, uint count, [Out] Physical[] monitors);
        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint current, out uint max);
        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool SetMonitorBrightness(IntPtr h, uint value);
        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool GetMonitorContrast(IntPtr h, out uint min, out uint current, out uint max);
        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool SetMonitorContrast(IntPtr h, uint value);
        [DllImport("dxva2.dll")]
        internal static extern bool DestroyPhysicalMonitor(IntPtr handle);
        internal static Header Head(uint type, int size, Luid adapter, uint id)
        {
            return new Header
            {
                Type = type,
                Size = (uint)size,
                Adapter = adapter,
                Id = id
            };
        }
    }

    internal sealed class Display
    {
        public string Name, Key, Gdi, DevicePath, WmiId, Note = "", HdrReason = "";
        public Native.Luid Adapter;
        public uint TargetId;
        public IntPtr Handle;
        public bool BrightnessSupported, ContrastSupported, HdrKnown, HdrSupported, HdrEnabled, HdrActive, HdrLimited, HdrModern;
        public uint BrightMin, BrightMax, ContrastMin, ContrastMax, Bits;
        public int Brightness, Contrast, InitialBrightness, InitialContrast;
        public bool InitialHdr;
        public string HdrError = "";
        public string Interface
        {
            get
            {
                return (WmiId != null ? "WMI" : Handle != IntPtr.Zero ? "DDC/CI" : "系统显示接口");
            }
        }

        public static int Percent(uint value, uint min, uint max)
        {
            return max > min ? (int)Math.Round((value - min) * 100.0 / (max - min)) : 0;
        }

        public static uint Raw(int value, uint min, uint max)
        {
            return min + (uint)Math.Round(Math.Max(0, Math.Min(100, value)) * (max - min) / 100.0);
        }

        public int Read(bool contrast)
        {
            if (!contrast && WmiId != null)
            {
                using (var search = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM WmiMonitorBrightness WHERE Active=True"))
                using (var results = search.Get())
                    foreach (ManagementObject m in results)
                        using (m)
                            if ((string)m["InstanceName"] == WmiId)
                                return Convert.ToInt32(m["CurrentBrightness"]);
                throw new Exception("屏幕已断开，请重新检测。");
            }

            uint min, current, max;
            bool ok = contrast ? Native.GetMonitorContrast(Handle, out min, out current, out max) : Native.GetMonitorBrightness(Handle, out min, out current, out max);
            if (!ok || max <= min)
                throw new Exception("无法读取" + (contrast ? "对比度" : "亮度") + "，请重新检测连接。");
            return Percent(current, min, max);
        }

        public void Set(bool contrast, int value)
        {
            if (contrast && !ContrastSupported || !contrast && !BrightnessSupported)
                throw new Exception("此屏幕未提供该控制接口。");
            if (!contrast && WmiId != null)
            {
                using (var search = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=True"))
                using (var results = search.Get())
                    foreach (ManagementObject m in results)
                        using (m)
                            if ((string)m["InstanceName"] == WmiId)
                            {
                                object code = m.InvokeMethod("WmiSetBrightness", new object[] { (uint)0, (byte)Math.Max(0, Math.Min(100, value)) });
                                if (Convert.ToUInt32(code) != 0)
                                    throw new Exception("系统拒绝亮度设置。");
                                return;
                            }

                throw new Exception("屏幕已断开，请重新检测。");
            }

            bool ok = contrast ? Native.SetMonitorContrast(Handle, Raw(value, ContrastMin, ContrastMax)) : Native.SetMonitorBrightness(Handle, Raw(value, BrightMin, BrightMax));
            if (!ok)
                throw new Exception("显示器拒绝设置，请检查 DDC/CI 或 HDR 模式。");
        }

        public void ReadHdr()
        {
            var info = new Native.ColorInfo
            {
                Header = Native.Head(15, 36, Adapter, TargetId)
            };
            int code = Native.GetColor(ref info);
            HdrModern = code == 0;
            if (code == 0)
            {
                HdrKnown = true;
                HdrSupported = (info.Flags & 16) != 0;
                HdrEnabled = (info.Flags & 32) != 0;
                HdrActive = info.ActiveMode == 2;
                HdrLimited = (info.Flags & 8) != 0;
                Bits = info.Bits;
            }
            else
            {
                HdrKnown = false;
                HdrSupported = false;
                HdrEnabled = false;
                HdrActive = false;
                HdrReason = "系统未提供可区分 HDR 的接口，请使用 Windows 显示设置。";
                return;
            }

            HdrReason = !HdrSupported ? "此屏幕或当前连接未报告 HDR 支持。" : HdrLimited ? "HDR 受 Windows 电源或显示策略限制。" : HdrEnabled && !HdrActive ? "已请求 HDR，但当前未生效；请检查电源与系统显示设置。" : HdrActive ? "HDR 正在使用 · " + Bits + " bit" : "高动态范围 · " + Bits + " bit";
        }

        public void SetHdr(bool enabled)
        {
            ReadHdr();
            if (!HdrKnown || !HdrSupported || HdrLimited)
                throw new Exception(HdrReason);
            var packet = new Native.State
            {
                Header = Native.Head(16, 24, Adapter, TargetId),
                Value = enabled ? 1u : 0u
            };
            int code = Native.SetState(ref packet);
            if (code != 0)
                throw new Win32Exception(code, "Windows 无法切换 HDR（" + code + "）。");
            System.Threading.Thread.Sleep(900);
            ReadHdr();
            if (HdrEnabled != enabled)
                throw new Exception("HDR 请求未生效，请检查 Windows 显示设置。");
        }
    }

    internal sealed class Backend : IDisposable
    {
        public List<Display> Displays = new List<Display>();
        public string Hardware = "", Notes = "";
        static string Normalize(string path)
        {
            if (String.IsNullOrEmpty(path))
                return "";
            return path.Replace("\\\\?\\", "").Replace('#', '\\').Split('{')[0].TrimEnd('\\');
        }

        public void Scan()
        {
            Dispose();
            Displays.Clear();
            Notes = "";
            Native.PathInfo[] paths = null;
            uint count = 0;
            int result = 0;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                uint modes;
                result = Native.GetDisplayConfigBufferSizes(2, out count, out modes);
                if (result != 0)
                    throw new Win32Exception(result);
                paths = new Native.PathInfo[count];
                var mm = new Native.ModeInfo[modes];
                result = Native.QueryDisplayConfig(2, ref count, paths, ref modes, mm, IntPtr.Zero);
                if (result == 0)
                    break;
                if (result != 122)
                    throw new Win32Exception(result);
            }

            if (result != 0)
                throw new Exception("显示设备正在变化，请重新检测。");
            for (int i = 0; i < count; i++)
            {
                var p = paths[i];
                var target = new Native.TargetName
                {
                    Header = Native.Head(2, Marshal.SizeOf(typeof(Native.TargetName)), p.Target.Adapter, p.Target.Id)
                };
                var source = new Native.SourceName
                {
                    Header = Native.Head(1, Marshal.SizeOf(typeof(Native.SourceName)), p.Source.Adapter, p.Source.Id)
                };
                Native.GetTarget(ref target);
                Native.GetSource(ref source);
                var d = new Display
                {
                    Adapter = p.Target.Adapter,
                    TargetId = p.Target.Id,
                    Key = p.Target.Adapter + ":" + p.Target.Id,
                    Name = String.IsNullOrEmpty(target.Name) ? "显示器 " + (i + 1) : target.Name,
                    Gdi = source.Name,
                    DevicePath = target.DevicePath
                };
                d.ReadHdr();
                d.InitialHdr = d.HdrEnabled;
                Displays.Add(d);
            }

            try
            {
                using (var search = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM WmiMonitorBrightness WHERE Active=True"))
                using (var results = search.Get())
                    foreach (ManagementObject m in results)
                        using (m)
                        {
                            string id = (string)m["InstanceName"];
                            var matches = Displays.Where(d => !String.IsNullOrEmpty(d.DevicePath) && id.StartsWith(Normalize(d.DevicePath), StringComparison.OrdinalIgnoreCase)).ToList();
                            if (matches.Count == 1)
                            {
                                var d = matches[0];
                                d.WmiId = id;
                                if (d.Name.StartsWith("显示器 "))
                                    d.Name = "内置屏幕 · " + id.Split('\\')[1];
                                d.BrightnessSupported = true;
                                d.Brightness = Convert.ToInt32(m["CurrentBrightness"]);
                            }
                        }
            }
            catch (Exception e)
            {
                Notes += "WMI：" + e.Message + "\r\n";
            }

            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate (IntPtr h, IntPtr dc, IntPtr r, IntPtr data)
            {
                var mi = new Native.MonitorInfo
                {
                    Size = (uint)Marshal.SizeOf(typeof(Native.MonitorInfo))
                };
                if (!Native.GetMonitorInfo(h, ref mi))
                    return true;
                uint n;
                if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(h, out n) || n == 0)
                    return true;
                var physical = new Native.Physical[n];
                if (!Native.GetPhysicalMonitorsFromHMONITOR(h, n, physical))
                    return true;
                var matches = Displays.Where(d => d.Gdi == mi.Device).ToList();
                foreach (var pm in physical)
                {
                    bool owned = false;
                    try
                    {
                        if (matches.Count != 1 || n != 1)
                        {
                            Notes += "镜像或多目标连接无法可靠映射 DDC/CI，请使用扩展模式。\r\n";
                            continue;
                        }

                        var d = matches[0];
                        d.Handle = pm.Handle;
                        owned = true;
                        uint a, b, c;
                        if (d.WmiId == null && Native.GetMonitorBrightness(pm.Handle, out a, out b, out c) && c > a)
                        {
                            d.BrightnessSupported = true;
                            d.BrightMin = a;
                            d.BrightMax = c;
                            d.Brightness = Display.Percent(b, a, c);
                        }

                        if (Native.GetMonitorContrast(pm.Handle, out a, out b, out c) && c > a)
                        {
                            d.ContrastSupported = true;
                            d.ContrastMin = a;
                            d.ContrastMax = c;
                            d.Contrast = Display.Percent(b, a, c);
                        }
                    }
                    finally
                    {
                        if (!owned)
                            Native.DestroyPhysicalMonitor(pm.Handle);
                    }
                }

                return true;
            }, IntPtr.Zero);
            foreach (var d in Displays)
            {
                d.InitialBrightness = d.Brightness;
                d.InitialContrast = d.Contrast;
            }
        }

        public void LoadHardware()
        {
            if (Hardware == "")
            {
                var b = new StringBuilder();
                foreach (string cls in new[]
                {
                    "Win32_OperatingSystem",
                    "Win32_VideoController"
                }

                )
                    try
                    {
                        using (var q = new ManagementObjectSearcher("SELECT * FROM " + cls))
                        using (var results = q.Get())
                            foreach (ManagementObject m in results)
                                using (m)
                                {
                                    if (cls == "Win32_OperatingSystem")
                                        b.AppendLine(m["Caption"] + " / " + m["Version"]);
                                    else
                                        b.AppendLine("\r\n显卡  " + m["Name"] + "\r\n驱动  " + m["DriverVersion"] + "\r\n模式  " + m["VideoModeDescription"]);
                                }
                    }
                    catch (Exception e)
                    {
                        b.AppendLine(e.Message);
                    }

                Hardware = b.ToString();
            }
        }

        public string Report()
        {
            var b = new StringBuilder("lumi 3 · 本机显示诊断\r\n" + DateTimeOffset.Now.ToString("o") + "\r\n\r\n" + Hardware + "\r\n");
            foreach (var d in Displays)
                b.AppendLine(d.Name + " / " + d.Gdi + "\r\n设备路径  " + d.DevicePath + "\r\n控制接口  " + d.Interface + "\r\n亮度  " + (d.BrightnessSupported ? d.Brightness + "%" : "不支持") + "\r\n对比度  " + (d.ContrastSupported ? d.Contrast + "%" : "不支持") + "\r\nHDR  支持=" + d.HdrSupported + ", 用户开启=" + d.HdrEnabled + ", 实际生效=" + d.HdrActive + "\r\n" + d.HdrReason + "\r\n");
            return b.ToString() + Notes;
        }

        public void Dispose()
        {
            foreach (var d in Displays)
                if (d.Handle != IntPtr.Zero)
                {
                    Native.DestroyPhysicalMonitor(d.Handle);
                    d.Handle = IntPtr.Zero;
                }
        }
    }
}
