using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;

namespace lumi
{
    internal static class SelfTest
    {
        static void Assert(bool value, string name, StringBuilder log)
        {
            if (!value)
                throw new Exception("FAIL " + name);
            log.AppendLine("PASS " + name);
        }

        public static void Run(string path)
        {
            var log = new StringBuilder();
            try
            {
                Assert(Marshal.SizeOf(typeof(Native.PathInfo)) == 72, "DISPLAYCONFIG_PATH_INFO size=72", log);
                Assert(Marshal.SizeOf(typeof(Native.ModeInfo)) == 64, "DISPLAYCONFIG_MODE_INFO size=64", log);
                Assert(Marshal.SizeOf(typeof(Native.TargetName)) == 420, "target name size=420", log);
                Assert(Marshal.SizeOf(typeof(Native.ColorInfo)) == 36, "HDR info v2 size=36", log);
                Assert(Marshal.SizeOf(typeof(Native.State)) == 24, "HDR state size=24", log);
                Assert(Display.Percent(125, 50, 200) == 50, "nonzero hardware minimum normalization", log);
                Assert(Display.Raw(50, 50, 200) == 125, "nonzero hardware minimum write", log);
                Assert(Display.Raw(-10, 10, 110) == 10 && Display.Raw(150, 10, 110) == 110, "hardware bounds clamp", log);
                using (var b = new Backend())
                {
                    b.Scan();
                    log.AppendLine(b.Report());
                    Assert(b.Displays.Count > 0, "connected targets discovered", log);
                    foreach (var d in b.Displays)
                    {
                        foreach (bool contrast in new[]
                        {
                            false,
                            true
                        }

                        )
                        {
                            if (contrast && !d.ContrastSupported || !contrast && !d.BrightnessSupported)
                            {
                                log.AppendLine("SKIP " + d.Name + (contrast ? " contrast" : " brightness") + " unsupported");
                                continue;
                            }

                            int original = d.Read(contrast);
                            try
                            {
                                int target = original > 50 ? original - 1 : original + 1;
                                d.Set(contrast, target);
                                Thread.Sleep(300);
                                Assert(Math.Abs(d.Read(contrast) - target) <= 1, d.Name + (contrast ? " contrast" : " brightness") + " write/read " + target, log);
                            }
                            finally
                            {
                                d.Set(contrast, original);
                                Thread.Sleep(300);
                                Assert(d.Read(contrast) == original, d.Name + " restore " + original, log);
                            }
                        }
                    }

                    foreach (var d in b.Displays)
                    {
                        d.ReadHdr();
                        if (!d.HdrSupported || d.HdrLimited)
                        {
                            log.AppendLine("SKIP " + d.Name + " HDR unsupported or limited");
                            continue;
                        }

                        bool original = d.HdrEnabled;
                        try
                        {
                            d.SetHdr(!original);
                            Assert(d.HdrEnabled == !original, d.Name + " HDR toggle readback", log);
                            Assert(d.HdrActive == !original, d.Name + " HDR actual active mode", log);
                        }
                        finally
                        {
                            d.SetHdr(original);
                            Assert(d.HdrEnabled == original, d.Name + " HDR restored", log);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                log.AppendLine("FAIL " + e);
                Environment.ExitCode = 1;
            }
            finally
            {
                File.WriteAllText(path, log.ToString(), Encoding.UTF8);
            }
        }
    }
}
