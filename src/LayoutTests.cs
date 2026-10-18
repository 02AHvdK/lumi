using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
namespace lumi
{
 internal static class LayoutTests
 {
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
  static StringBuilder log = new StringBuilder();
  static void Check(bool ok,string name) { if(!ok) throw new Exception(name); log.AppendLine("PASS "+name); }
  static Rect Bounds(FrameworkElement e,Window w) { return new Rect(e.TranslatePoint(new Point(),w),e.RenderSize); }
  internal static void Run(string dir)
  {
   Directory.CreateDirectory(dir);
   var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
   try {
    foreach (int dpi in new[] {96,120,144,192}) {
      int gap=8*dpi/96, pw=360*dpi/96, ph=340*dpi/96;
      var bounds=new Desktop.Rect {Left=-1920,Top=0,Right=0,Bottom=1080};
      var work=new Desktop.Rect {Left=-1920,Top=0,Right=0,Bottom=1032};
      var anchor=new Desktop.Rect {Left=-700,Top=1040,Right=-676,Bottom=1064};
      var pos=Desktop.PopupBounds(anchor,bounds,work,pw,ph,gap);
      Check(pos.Left>=work.Left && pos.Right<=work.Right && pos.Bottom==work.Bottom-gap,"bottom taskbar placement at DPI "+dpi);
      Check(Math.Abs((pos.Left+pos.Right)-(anchor.Left+anchor.Right))<=1,"icon center alignment at DPI "+dpi);
    }
    var screen=new Desktop.Rect {Left=0,Top=0,Right=1920,Bottom=1080};
    var left=Desktop.PopupBounds(new Desktop.Rect {Left=8,Top=500,Right=32,Bottom=524},screen,new Desktop.Rect {Left=48,Top=0,Right=1920,Bottom=1080},360,340,8);
    Check(left.Left==56,"left taskbar opens inward");
    var top=Desktop.PopupBounds(new Desktop.Rect {Left=900,Top=8,Right=924,Bottom=32},screen,new Desktop.Rect {Left=0,Top=48,Right=1920,Bottom=1080},360,340,8);
    Check(top.Top==56,"top taskbar opens downward");
    var right=Desktop.PopupBounds(new Desktop.Rect {Left=1880,Top=500,Right=1904,Bottom=524},screen,new Desktop.Rect {Left=0,Top=0,Right=1872,Bottom=1080},360,340,8);
    Check(right.Right==1864,"right taskbar opens inward");
    var service=new DisplayService();
    service.Screens.Add(new ScreenModel(new Display { Name="Lenovo 内置显示屏",Brightness=65,Contrast=50,BrightnessSupported=true,HdrReason="当前屏幕不支持 HDR",Bits=8 },service));
    var main=new MainView(service,null); var w=main.Window; w.Show(); w.UpdateLayout();
    foreach(var name in new[]{"Close","Maximize","Minimize"}) {
     var b=Views.Get<Button>(w,name); var pt=b.PointToScreen(new Point(b.ActualWidth/2,b.ActualHeight/2));
     long packed=((long)(ushort)(int)pt.Y<<16)|(ushort)(int)pt.X;
     Check(SendMessage(new WindowInteropHelper(w).Handle,0x84,IntPtr.Zero,new IntPtr(packed)).ToInt32()==1,name+" native hit is client area");
     Check(!Motion.IsWired(b),name+" has stable hit target");
    }
    foreach(double width in new[]{760.0,620.0}) {
     w.Width=width;w.UpdateLayout();
     Check(Bounds(Views.Get<CheckBox>(w,"Link"),w).Right <= Bounds(Views.Get<Button>(w,"Restore"),w).Left,"link and restore do not overlap at "+width);
     var link=Views.Get<CheckBox>(w,"Link");var hit=w.InputHitTest(link.TranslatePoint(new Point(6,link.ActualHeight/2),w)) as DependencyObject;
     while(hit!=null && hit!=link) hit=VisualTreeHelper.GetParent(hit);
     Check(hit==link,"link receives pointer at "+width);
     Check(Views.Get<Button>(w,"P100").ActualWidth>=48,"preset fits at "+width);
     UiTests.Capture(w,Path.Combine(dir,"main-"+width+".png"));
    }
    main.RenderHardware("Windows 11 / 示例系统版本\r\n显卡  示例显卡\r\n驱动  示例驱动版本");
    main.Page("Info"); w.UpdateLayout();
    Check(Views.Get<StackPanel>(w,"HardwareGroups").Children.Count==service.Screens.Count+1,"hardware grouped by device plus system");
    UiTests.Capture(w,Path.Combine(dir,"hardware-groups.png"));
    Check(((System.Windows.Controls.Border)w.Content).CornerRadius.TopLeft==0,"main shell delegates corners to DWM");
    Views.Get<Button>(w,"Settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));w.UpdateLayout();
    Check(Views.Get<ScrollViewer>(w,"SettingsPage").IsVisible,"settings button opens page");
    Motion.AutoWire(w); Motion.AutoWire(w);
    Check(!Motion.IsWired(Views.Get<Button>(w,"IconOption1")),"icon options have no motion wiring");
    UiTests.Capture(w,Path.Combine(dir,"settings.png"));
    Views.Get<Button>(w,"BackSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(Views.Get<ScrollViewer>(w,"DisplayPage").IsVisible,"return button opens display");
    Views.Get<Button>(w,"Maximize").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(w.WindowState==WindowState.Maximized,"maximize handler");
    Views.Get<Button>(w,"Maximize").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(w.WindowState==WindowState.Normal,"restore handler");
    var q=Views.Load("QuickWindow.xaml"); q.DataContext=service;Desktop.Configure(q,true);q.Height=226;q.Show();q.UpdateLayout();
    Check(((System.Windows.Controls.Border)q.Content).CornerRadius.TopLeft==0,"quick shell delegates corners to DWM");
    int corner;
    Check(Desktop.DwmGetWindowAttribute(new WindowInteropHelper(q).Handle,33,out corner,4)==0 && corner==2,"quick native round-corner preference accepted");
    Check(Desktop.DwmGetWindowAttribute(new WindowInteropHelper(w).Handle,33,out corner,4)==0 && corner==2,"main native round-corner preference accepted");
    UiTests.Capture(q,Path.Combine(dir,"quick-light.png"));
    SystemTheme.Apply(true,Color.FromRgb(80,125,190));w.UpdateLayout();UiTests.Capture(w,Path.Combine(dir,"main-dark.png"));q.UpdateLayout();UiTests.Capture(q,Path.Combine(dir,"quick-dark.png"));
    var slider=Views.Get<Slider>(w,"Brightness");
    slider.Focus();w.UpdateLayout();
    Check(slider.FocusVisualStyle==null && slider.Template.FindName("focus",slider)==null,"slider has no rectangular focus outline");
    UiTests.Capture(w,Path.Combine(dir,"slider-focused.png"));
    var preset=Views.Get<Button>(w,"P50");
    Check(!Motion.IsWired(preset) && preset.RenderTransform.Value.IsIdentity,"preset remains stationary");
    Check(Views.Get<ListBox>(w,"Screens").MaxHeight==double.PositiveInfinity,"screen list expands without height cap");
    Check(ScrollViewer.GetVerticalScrollBarVisibility(Views.Get<ListBox>(w,"Screens"))==ScrollBarVisibility.Disabled,"screen list has no internal scrollbar");
    Check(Views.Get<ScrollViewer>(w,"DisplayPage").VerticalScrollBarVisibility==ScrollBarVisibility.Hidden,"display page has no visible scrollbar");
    Check(SystemTheme.ValidateMode("invalid")=="System" && SystemTheme.ValidateAccent("invalid")=="System","invalid theme preferences fall back to system");
    byte[] saved = File.Exists(SystemTheme.PreferencesPath) ? File.ReadAllBytes(SystemTheme.PreferencesPath) : null;
    string savedMode=SystemTheme.Mode, savedAccent=SystemTheme.AccentChoice;
    try {
      main.Page("Settings");
      Views.Get<Button>(w,"ThemeDark").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
      Check(SystemTheme.Dark && SystemTheme.Mode=="Dark","dark theme button applies immediately");
      Views.Get<Button>(w,"AccentPurple").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
      Check(File.ReadAllLines(SystemTheme.PreferencesPath)[1]=="Purple","accent selection persisted");
      Check(Object.ReferenceEquals(w.FindResource("Accent"),q.FindResource("Accent")),"main and quick share selected theme");
      w.UpdateLayout();UiTests.Capture(w,Path.Combine(dir,"appearance-dark.png"));
      Views.Get<Button>(w,"ThemeLight").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
      Check(!SystemTheme.Dark,"light theme button applies immediately");
      w.UpdateLayout();UiTests.Capture(w,Path.Combine(dir,"appearance-light.png"));
      Views.Get<Button>(w,"ThemeSystem").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
      Check(SystemTheme.Mode=="System","system theme selection");
    } finally {
      if(saved==null) File.Delete(SystemTheme.PreferencesPath); else File.WriteAllBytes(SystemTheme.PreferencesPath,saved);
      SystemTheme.Mode=savedMode;SystemTheme.AccentChoice=savedAccent;SystemTheme.Reload();main.Page("Display");
    }
    for(int i=0;i<5;i++) service.Screens.Add(new ScreenModel(new Display {Name="External display "+i,Brightness=50,BrightnessSupported=true},service));
    q.Height=430;q.UpdateLayout();UiTests.Capture(q,Path.Combine(dir,"quick-six-displays.png"));
    Check(q.ActualHeight==430,"multi-display panel height capped");
    service.Dispose(); // Stop hardware delivery before exercising value-changing UI handlers.
    var model=service.Screens[0];
    int initial=model.Brightness;
    Views.Get<Button>(w,"BrightnessUp").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(model.Brightness==initial+1,"main plus handler increments one percent");
    Views.Get<Button>(w,"BrightnessDown").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(model.Brightness==initial,"main minus handler decrements one percent");
    var step=new Button { Tag="BrightnessUp",DataContext=model };
    Check(QuickView.ApplyStep(step,service) && model.Brightness==initial+1,"quick plus handler updates shared model");
    step.Tag="BrightnessDown";QuickView.ApplyStep(step,service);
    Check(model.Brightness==initial,"quick minus handler updates shared model");
    model.Brightness=100;step.Tag="BrightnessUp";QuickView.ApplyStep(step,service);Check(model.Brightness==100,"quick upper clamp");
    model.Brightness=0;step.Tag="BrightnessDown";QuickView.ApplyStep(step,service);Check(model.Brightness==0,"quick lower clamp");
    model.Brightness=initial;
    w.Height=640;w.UpdateLayout();UiTests.Capture(w,Path.Combine(dir,"main-minimum.png"));
    Check(Views.Get<ScrollViewer>(w,"DisplayPage").ScrollableHeight>0,"minimum window content scrolls");
    Views.Get<ScrollViewer>(w,"DisplayPage").ScrollToBottom();w.UpdateLayout();
    Check(Bounds(Views.Get<Button>(w,"Restore"),w).Bottom < w.ActualHeight,"restore reachable at minimum window height");
    Check(service.WriteCount==0,"zero hardware writes");q.Close();
    Views.Get<Button>(w,"Close").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(!w.IsVisible,"close handler");
   } catch(Exception e) { log.AppendLine("FAIL "+e);Environment.ExitCode=1; }
   File.WriteAllText(Path.Combine(dir,"layout-tests.txt"),log.ToString());app.Shutdown();
  }
 }
}
