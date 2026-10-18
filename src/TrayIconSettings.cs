using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace lumi
{
    internal static class TrayIconSettings
    {
        internal static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lumi", "tray-icon.png");
        internal static byte[] Option(int index)
        {
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("option" + index + ".png"))
            using (var data = new MemoryStream()) { stream.CopyTo(data); return data.ToArray(); }
        }
        internal static byte[] Read(string path)
        {
            if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new InvalidOperationException("请选择小于 20 MB 的图标文件。");
            BitmapSource source;
            using (var stream = File.OpenRead(path))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                source = decoder.Frames[0];
                foreach (var frame in decoder.Frames)
                    if (frame.PixelWidth >= 32 && (source.PixelWidth < 32 || frame.PixelWidth < source.PixelWidth)) source = frame;
                if (source.PixelWidth > 8192 || source.PixelHeight > 8192) throw new InvalidOperationException("图片尺寸过大，请选择不超过 8192 像素的图片。");
                source.Freeze();
            }
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            double scale = 32.0 / Math.Max(source.PixelWidth, source.PixelHeight);
            double width = source.PixelWidth * scale, height = source.PixelHeight * scale;
            using (var drawing = visual.RenderOpen()) drawing.DrawImage(source, new Rect((32-width)/2, (32-height)/2, width, height));
            var bitmap = new RenderTargetBitmap(32,32,96,96,PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = new MemoryStream()) { png.Save(output); return output.ToArray(); }
        }
        internal static ImageSource Preview()
        {
            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(Desktop.TrayIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        internal static void Save(byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temp = FilePath + ".tmp";
            try
            {
                File.WriteAllBytes(temp, data);
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
                else File.Move(temp, FilePath);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
