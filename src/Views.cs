using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace lumi
{
    internal static class Views
    {
        static bool loaded;
        internal static Window Load(string resource)
        {
            if (!loaded)
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Theme.xaml"))
                    Application.Current.Resources = (ResourceDictionary)XamlReader.Load(s);
                loaded = true;
                SystemTheme.Reload();
            }

            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                return (Window)XamlReader.Load(s);
        }

        internal static T Get<T>(Window w, string name)
            where T : FrameworkElement
        {
            return (T)w.FindName(name);
        }

        internal static void Click(Window w, string name, Action action)
        {
            Get<Button>(w, name).Click += delegate
            {
                action();
            };
        }

        internal static void Wheel(object sender, MouseWheelEventArgs e)
        {
            var slider = sender as Slider;
            if (slider != null && slider.IsEnabled)
            {
                slider.Value = Math.Max(0, Math.Min(100, slider.Value + (e.Delta > 0 ? 1 : -1)));
                e.Handled = true;
            }
        }

        internal static void WheelSliders(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var slider = child as Slider;
                if (slider != null)
                {
                    slider.PreviewMouseWheel -= Wheel;
                    slider.PreviewMouseWheel += Wheel;
                }
                else
                    WheelSliders(child);
            }
        }

        internal static void SetIcon(Window w)
        {
            var logo = Get<Image>(w, "Logo");
            if (logo == null) return;
            if (w.Icon != null) { logo.Source = w.Icon; return; }
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("option2.png")) {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); logo.Source = bitmap;
            }
        }
    }

    internal sealed class MainView
    {
        internal readonly Window Window;
        readonly DisplayService service;
        bool changing;
        internal MainView(DisplayService service, AppHost host)
        {
            this.service = service;
            Window = Views.Load("MainWindow.xaml");
            Window.DataContext = service;
            Desktop.Configure(Window, false);
            Views.SetIcon(Window);
            var screens = Views.Get<ListBox>(Window, "Screens");
            screens.SelectedIndex = 0;
            Action rebuilt = delegate
            {
                screens.SelectedIndex = 0;
            };
            service.Rebuilt += rebuilt;
            Window.Closed += delegate
            {
                service.Rebuilt -= rebuilt;
            };
            Window.Loaded += delegate
            {
                Views.WheelSliders(Window);
            };
            Views.Click(Window, "Close", () => Window.Close());
            Views.Click(Window, "Minimize", () => Window.WindowState = WindowState.Minimized);
            Views.Click(Window, "Maximize", () => Window.WindowState = Window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
            Views.Click(Window, "Quick", () => host.OpenQuick(true));
            Views.Click(Window, "Refresh", async () => await service.Scan());
            Views.Click(Window, "Info", async delegate
            {
                Page("Info");
                Views.Get<TextBlock>(Window, "HardwareStatus").Text = "正在读取系统与驱动信息…";
                RenderHardware("正在读取系统与驱动信息…");
                try
                {
                    Views.Get<TextBox>(Window, "Report").Text = await service.Diagnostics();
                    RenderHardware(service.HardwareSummary);
                }
                catch (Exception e)
                {
                    Views.Get<TextBlock>(Window, "HardwareStatus").Text = "读取失败，可重新打开此页重试";
                    service.Message(e.Message);
                }
            });
            Views.Click(Window, "BackInfo", () => Page("Display"));
            Views.Click(Window, "BackSettings", () => Page("Display"));
            Views.Click(Window, "Settings", () => Page("Settings"));
            Views.Click(Window, "DisplayNav", () => Page("Display"));
            foreach (string control in new[] { "Brightness", "Contrast" })
                foreach (int delta in new[] { -1, 1 })
                {
                    string channel = control; int step = delta;
                    Views.Click(Window, control + (delta < 0 ? "Down" : "Up"), delegate {
                        var selected = screens.SelectedItem as ScreenModel;
                        if (selected == null || service.Busy) return;
                        if (channel == "Brightness" && selected.CanBrightness) SetBrightness(Math.Max(0, Math.Min(100, selected.Brightness + step)));
                        if (channel == "Contrast" && selected.CanContrast) selected.Contrast = Math.Max(0, Math.Min(100, selected.Contrast + step));
                    });
                }
            Window.Loaded += delegate { Page("Display"); };
            Action refreshAppearance = delegate {
                foreach (string mode in new[] { "System", "Light", "Dark" }) {
                    bool selected = mode == SystemTheme.Mode;
                    var b = Views.Get<Button>(Window, "Theme" + mode);
                    b.SetResourceReference(Control.BorderBrushProperty, selected ? "Accent" : "Edge");
                    b.SetResourceReference(Control.BackgroundProperty, selected ? "Selected" : "Surface");
                    Views.Get<TextBlock>(Window, "ThemeState" + mode).Text = selected ? "已选用" : "选择";
                }
                foreach (string color in new[] { "System", "Blue", "Purple", "Green", "Orange" }) {
                    bool selected = color == SystemTheme.AccentChoice;
                    Views.Get<Button>(Window, "Accent" + color).SetResourceReference(Control.BorderBrushProperty, selected ? "Accent" : "Edge");
                    Views.Get<TextBlock>(Window, "AccentState" + color).Text = selected ? "已选用" : " ";
                }
            };
            foreach (string mode in new[] { "System", "Light", "Dark" }) {
                string choice = mode;
                Views.Click(Window, "Theme" + mode, delegate { try { SystemTheme.Choose(choice, SystemTheme.AccentChoice); refreshAppearance(); } catch(Exception e) { service.Message("主题保存失败：" + e.Message); } });
            }
            foreach (string color in new[] { "System", "Blue", "Purple", "Green", "Orange" }) {
                string choice = color;
                Views.Click(Window, "Accent" + color, delegate { try { SystemTheme.Choose(SystemTheme.Mode, choice); refreshAppearance(); } catch(Exception e) { service.Message("主题保存失败：" + e.Message); } });
            }
            refreshAppearance();
            Action refreshIcons = delegate
            {
                byte[] saved = File.Exists(TrayIconSettings.FilePath) ? File.ReadAllBytes(TrayIconSettings.FilePath) : null;
                for (int i = 1; i <= 3; i++)
                {
                    byte[] icon = TrayIconSettings.Option(i);
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    using (var input = new MemoryStream(icon)) { bitmap.BeginInit(); bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bitmap.StreamSource = input; bitmap.EndInit(); bitmap.Freeze(); }
                    Views.Get<Image>(Window, "OptionImage" + i).Source = bitmap;
                    bool selected = saved == null ? i == 3 : Convert.ToBase64String(saved) == Convert.ToBase64String(icon);
                    var button = Views.Get<Button>(Window, "IconOption" + i);
                    button.SetResourceReference(Control.BackgroundProperty, selected ? "Selected" : "Surface");
                    button.SetResourceReference(Control.BorderBrushProperty, selected ? "Accent" : "Edge");
                    Views.Get<TextBlock>(Window, "OptionState" + i).Text = selected ? "已选用" : "选择";
                }
            };
            refreshIcons();
            for (int i = 1; i <= 3; i++)
            {
                int option = i;
                Views.Click(Window, "IconOption" + i, delegate
                {
                    try { host.Tray.ChangeIconBytes(TrayIconSettings.Option(option), false); refreshIcons(); service.Message("托盘图标已更新并保存。"); }
                    catch (Exception e) { service.Message(e.Message); }
                });
            }
            Views.Click(Window, "Windows", delegate
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:display") { UseShellExecute = true });
                }
                catch (Exception e)
                {
                    service.Message(e.Message);
                }
            });
            Views.Click(Window, "Export", delegate
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "文本文件 (*.txt)|*.txt",
                    FileName = "lumi-诊断.txt"
                };
                if (dialog.ShowDialog(Window) == true)
                    try
                    {
                        File.WriteAllText(dialog.FileName, Views.Get<TextBox>(Window, "Report").Text);
                        service.Message("诊断已保存。");
                    }
                    catch (Exception e)
                    {
                        service.Message(e.Message);
                    }
            });
            foreach (int value in new[]
            {
                25,
                50,
                75,
                100
            }

            )
            {
                int n = value;
                Views.Click(Window, "P" + n, () => SetBrightness(n));
            }

            Views.Get<Slider>(Window, "Brightness").ValueChanged += delegate (object sender, RoutedPropertyChangedEventArgs<double> e)
            {
                if (changing || service.Busy || Views.Get<CheckBox>(Window, "Link").IsChecked != true || !Views.Get<Slider>(Window, "Brightness").IsKeyboardFocusWithin && !Views.Get<Slider>(Window, "Brightness").IsMouseCaptureWithin)
                    return;
                changing = true;
                foreach (var s in service.Screens)
                    if (s.CanBrightness)
                        s.Brightness = (int)e.NewValue;
                changing = false;
            };
            Views.Get<CheckBox>(Window, "Hdr").Click += async delegate
            {
                var selected = screens.SelectedItem as ScreenModel;
                bool enabled = Views.Get<CheckBox>(Window, "Hdr").IsChecked == true;
                await service.SetHdr(selected, enabled);
                if (selected != null)
                    selected.Notify("HdrEnabled");
            };
            Views.Click(Window, "Restore", delegate
            {
                var selected = screens.SelectedItem as ScreenModel;
                if (selected == null)
                    return;
                Views.Get<CheckBox>(Window, "Link").IsChecked = false;
                if (selected.CanBrightness)
                    selected.Brightness = selected.Device.InitialBrightness;
                if (selected.CanContrast)
                    selected.Contrast = selected.Device.InitialContrast;
            });
            var startup = Views.Get<CheckBox>(Window, "Startup");
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    startup.IsChecked = key != null && key.GetValue("lumi") != null;
            }
            catch (Exception e)
            {
                AppLog.Write(e);
            }

            startup.Click += delegate
            {
                bool desired = startup.IsChecked == true;
                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    {
                        if (desired)
                            key.SetValue("lumi", "\"" + Assembly.GetExecutingAssembly().Location + "\" --tray");
                        else
                            key.DeleteValue("lumi", false);
                    }

                    service.Message("登录启动设置已保存。");
                }
                catch (Exception e)
                {
                    startup.IsChecked = !desired;
                    service.Message(e.Message);
                }
            };
        }

        internal void RenderHardware(string system)
        {
            Views.Get<TextBlock>(Window, "HardwareStatus").Text = service.Screens.Count + " 块屏幕 · 按设备查看能力与连接信息";
            var groups = Views.Get<StackPanel>(Window, "HardwareGroups"); groups.Children.Clear();
            foreach (var screen in service.Screens) {
                var body = new StackPanel();
                body.Children.Add(HardwareText(screen.Name, 18, true, false));
                var subtitle=HardwareText(screen.Detail + " · " + screen.Mode,12,false,true);subtitle.Margin=new Thickness(0,5,0,16);body.Children.Add(subtitle);
                body.Children.Add(HardwareRow("亮度控制",screen.CanBrightness ? screen.Brightness + "% · 可调节" : "硬件未提供接口"));
                body.Children.Add(HardwareRow("对比度",screen.CanContrast ? screen.Contrast + "% · 可调节" : "硬件未提供接口"));
                body.Children.Add(HardwareRow("HDR 状态",screen.Device.HdrActive ? "已生效" : screen.CanHdr ? "可用 · 未开启" : "当前不可用"));
                if (!String.IsNullOrWhiteSpace(screen.HdrHint)) { var hint=HardwareText(screen.HdrHint,12,false,true);hint.Margin=new Thickness(0,8,0,0);body.Children.Add(hint); }
                var details=new StackPanel();details.Children.Add(HardwareRow("控制接口",screen.Device.Interface));details.Children.Add(HardwareRow("显示标识",screen.Device.Gdi));details.Children.Add(HardwareRow("设备路径",screen.Device.DevicePath));
                var expander=new Expander { Header="连接详情", Content=details, Margin=new Thickness(0,14,0,0), FontSize=13 }; expander.SetResourceReference(Control.ForegroundProperty,"Ink");body.Children.Add(expander);
                var card=new Border { Style=(Style)Window.FindResource("Card"), Child=body, Margin=new Thickness(0,0,0,12) };groups.Children.Add(card);
            }
            var systemBody=new StackPanel(); systemBody.Children.Add(HardwareText("系统与显卡",18,true,false));
            foreach(string raw in system.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)) {
                string line=raw.Trim();int split=line.IndexOf("  ",StringComparison.Ordinal);
                systemBody.Children.Add(split>0 ? (UIElement)HardwareRow(line.Substring(0,split),line.Substring(split).Trim()) : HardwareText(line,13,false,true));
            }
            groups.Children.Add(new Border { Style=(Style)Window.FindResource("Card"), Child=systemBody, Margin=new Thickness(0,0,0,12) });
        }
        static TextBlock HardwareText(string text,double size,bool strong,bool muted)
        {
            var block=new TextBlock { Text=text ?? "未提供", FontSize=size, FontWeight=strong ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping=TextWrapping.Wrap, LineHeight=size*1.5 };
            block.SetResourceReference(TextBlock.ForegroundProperty,muted ? "Muted" : "Ink");return block;
        }
        static Grid HardwareRow(string label,string value)
        {
            var row=new Grid { Margin=new Thickness(0,6,0,6) };row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(104) });row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(HardwareText(label,12,false,true));var text=HardwareText(String.IsNullOrWhiteSpace(value) ? "未提供" : value,13,false,false);Grid.SetColumn(text,1);row.Children.Add(text);return row;
        }
        internal void Page(string name)
        {
            Views.Get<ScrollViewer>(Window, "DisplayPage").Visibility = name == "Display" ? Visibility.Visible : Visibility.Collapsed;
            Views.Get<Grid>(Window, "InfoPage").Visibility = name == "Info" ? Visibility.Visible : Visibility.Collapsed;
            Views.Get<ScrollViewer>(Window, "SettingsPage").Visibility = name == "Settings" ? Visibility.Visible : Visibility.Collapsed;
            Window.UpdateLayout();

            foreach (string nav in new[] { "DisplayNav", "Info", "Settings" })
                Views.Get<Button>(Window, nav).SetResourceReference(Control.BackgroundProperty, (nav == name || nav == "DisplayNav" && name == "Display") ? "Selected" : "Inset");
        }

        void SetBrightness(int value)
        {
            if (service.Busy)
                return;
            if (Views.Get<CheckBox>(Window, "Link").IsChecked == true)
            {
                foreach (var s in service.Screens)
                    if (s.CanBrightness)
                        s.Brightness = value;
            }
            else
            {
                var s = Views.Get<ListBox>(Window, "Screens").SelectedItem as ScreenModel;
                if (s != null && s.CanBrightness)
                    s.Brightness = value;
            }
        }
    }

    internal sealed class QuickView
    {
        internal readonly Window Window;
        internal static bool ApplyStep(Button button, DisplayService service)
        {
            var model = button == null ? null : button.DataContext as ScreenModel;
            if (model == null || !model.CanBrightness || service.Busy) return false;
            string tag = button.Tag as string;
            if (tag != "BrightnessDown" && tag != "BrightnessUp") return false;
            model.Brightness = Math.Max(0, Math.Min(100, model.Brightness + (tag == "BrightnessUp" ? 1 : -1)));
            return true;
        }
        internal QuickView(DisplayService service, AppHost host)
        {
            Window = Views.Load("QuickWindow.xaml");
            Window.DataContext = service;
            Desktop.Configure(Window, true);
            Views.SetIcon(Window);
            Views.Click(Window, "Dismiss", () => host.HideQuick());
            Window.AddHandler(Button.ClickEvent, new RoutedEventHandler(delegate(object sender, RoutedEventArgs e) {
                if (ApplyStep(e.OriginalSource as Button, service)) e.Handled = true;
            }));
            Views.Click(Window, "OpenMain", () => host.ShowMain());
            Action resize = delegate
            {
                Window.Height = Math.Min(430, 66 + Math.Max(1, service.Screens.Count) * 80);
                if (Window.IsVisible) Desktop.Place(Window, host.Tray.Anchor());
                Views.WheelSliders(Window);
            };
            service.Rebuilt += resize;
            Window.Closed += delegate { service.Rebuilt -= resize; };
            Window.Loaded += delegate
            {
                Views.WheelSliders(Window);
            };
            Window.PreviewKeyDown += delegate (object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    host.HideQuick();
                    e.Handled = true;
                }
            };
            Window.PreviewMouseDown += delegate
            {
                host.QuickEngaged();
            };
            Window.Deactivated += delegate
            {
                host.QuickDeactivated();
            };
        }
    }
}



