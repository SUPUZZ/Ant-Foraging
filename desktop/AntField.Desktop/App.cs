using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AntField.Core;
using Forms = System.Windows.Forms;

namespace AntField.Desktop;

internal sealed class App : Application
{
    public static bool IsExiting { get; private set; }
    DesktopColony colony = null!;
    Window overlay = null!;
    OverlaySurface surface = null!;
    ColonyWindow panel = null!;
    Forms.NotifyIcon tray = null!;
    System.Drawing.Icon? trayIcon;
    internal static Uri IconUri => new("pack://application:,,,/Assets/ant-icon.ico", UriKind.Absolute);
    DispatcherTimer timer = null!;
    IntPtr overlayHandle;
    NativeDesktop.NativeRect work;
    double scale = 1, speed = 1, scanClock, refreshClock, saveClock;
    bool paused, smoke;
    int smokeSeconds = 3;
    readonly Stopwatch clock = new();
    readonly string stateFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AntField");
    Mutex? singleInstance;
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown, smoke = args.Contains("--smoke-test") };
        if (args.Contains("--ui-test")) { app.smoke = true; app.smokeSeconds = 60; }
        app.singleInstance = new Mutex(true, app.smoke ? "Local\\AntFieldSmokeTest" : "Local\\AntFieldDesktop", out bool created);
        if (!created) { System.Windows.MessageBox.Show("桌面蚁境已经在运行，请从系统托盘打开蚁窝。", "桌面蚁境"); app.singleInstance.Dispose(); return; }
        app.DispatcherUnhandledException += (_, e) => {
            Directory.CreateDirectory(app.stateFolder); File.WriteAllText(Path.Combine(app.stateFolder, "error.log"), e.Exception.ToString());
            System.Windows.MessageBox.Show("桌面蚁境遇到错误，详情已保存到本地 error.log。", "桌面蚁境"); e.Handled = true; app.Shutdown(1);
        };
        app.Run();
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); work = NativeDesktop.WorkArea();
        var initial = SystemParameters.WorkArea;
        colony = new DesktopColony(initial.Width, initial.Height);
        if (!smoke)
        {
            try { var saved = JsonSerializer.Deserialize<ColonySnapshot>(File.ReadAllText(Path.Combine(stateFolder, "colony.json"))); if (saved != null)
                {
                    bool multi = saved.Screens is { Length: > 0 } && saved.Home != null;
                    colony = DesktopColony.Restore(saved, multi ? saved.Width : initial.Width, multi ? saved.Height : initial.Height);
                    if (multi)
                    {
                        double initialScale = (work.Right - work.Left) / initial.Width;
                        work.Left = (int)Math.Round((work.Left + work.Right) / 2.0 - colony.Home.X * initialScale);
                        work.Top = (int)Math.Round(work.Bottom - (colony.Home.Y + 24) * initialScale);
                    }
                } }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NullReferenceException) { /* A missing or damaged save starts a fresh colony. */ }
        }
        surface = new OverlaySurface(colony);
        overlay = new Window { Title = "桌面蚁境 · 透明桌面层", WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true, Left = initial.Left, Top = initial.Top, Width = initial.Width, Height = initial.Height, Content = surface };
        overlay.SourceInitialized += (_, _) => {
            overlayHandle = new WindowInteropHelper(overlay).Handle; scale = NativeDesktop.DpiScale(overlay); UpdateBounds(); NativeDesktop.ClickThrough(overlayHandle, true);
            HwndSource.FromHwnd(overlayHandle)?.AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) => {
                if (msg == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
                return IntPtr.Zero;
            });
        };
        surface.MouseLeftButtonDown += (_, args) => { if (surface.NestHitBox.Contains(args.GetPosition(surface))) OpenPanel(); };
        overlay.Icon = BitmapFrame.Create(IconUri);
        overlay.Show();
        CreatePanel(); CreateTray();
        ScanWindows(); for (int i = 0; i < 5; i++) colony.SpawnFood();
        clock.Start();
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += Tick; timer.Start();
        if (smoke) { panel.ShowActivated = false; panel.Show(); }
        else OpenPanel();
        if (smoke)
        {
            var smokeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(smokeSeconds) };
            smokeTimer.Tick += (_, _) => { smokeTimer.Stop(); SaveSmoke(); Shutdown(); }; smokeTimer.Start();
        }
    }
    void CreatePanel()
    {
        panel = new ColonyWindow(colony, () => paused, TogglePause);
        panel.SpeedChanged = value => speed = value;
        panel.TrailsChanged = value => surface.ShowTrails = value;
        panel.WallsChanged = value => surface.ShowWalls = value;
        panel.ResetRequested = Reset;
    }
    void CreateTray()
    {
        using (var stream = GetResourceStream(IconUri).Stream)
        using (var icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize))
            trayIcon = (System.Drawing.Icon)icon.Clone();
        tray = new Forms.NotifyIcon { Text = "桌面蚁境 · 点击查看蚁窝", Icon = trayIcon, Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("查看蚁窝", null, (_, _) => Dispatcher.Invoke(OpenPanel));
        menu.Items.Add("暂停 / 继续", null, (_, _) => Dispatcher.Invoke(TogglePause));
        menu.Items.Add("隐藏 / 显示桌面蚁群", null, (_, _) => Dispatcher.Invoke(() => { if (overlay.IsVisible) overlay.Hide(); else overlay.Show(); }));
        menu.Items.Add("退出桌面蚁境", null, (_, _) => Dispatcher.Invoke(() => Shutdown()));
        tray.ContextMenuStrip = menu; tray.MouseClick += (_, args) => { if (args.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(OpenPanel); };
    }
    void OpenPanel() { panel.Show(); panel.Activate(); panel.Refresh(); }
    void TogglePause() { paused = !paused; panel.Refresh(); }
    void UpdateBounds()
    {
        var areas = NativeDesktop.WorkAreas(true);
        if (areas.Count == 0) areas.Add(NativeDesktop.WorkArea());
        var bounds = new NativeDesktop.NativeRect { Left = areas.Min(a => a.Left), Top = areas.Min(a => a.Top), Right = areas.Max(a => a.Right), Bottom = areas.Max(a => a.Bottom) };
        double previousScale = scale; scale = NativeDesktop.DpiScale(overlay);
        var screens = areas.Select((a, i) => new Wall(i, (a.Left - bounds.Left) / scale, (a.Top - bounds.Top) / scale, (a.Right - a.Left) / scale, (a.Bottom - a.Top) / scale)).ToArray();
        var previousFoodAreas = colony.FoodAreas;
        colony.FoodAreas = NativeDesktop.WorkAreas().Select((a, i) => new Wall(i, (a.Left - bounds.Left) / scale, (a.Top - bounds.Top) / scale, (a.Right - a.Left) / scale, (a.Bottom - a.Top) / scale)).ToArray();
        var primary = NativeDesktop.WorkArea();
        var nest = new Vec(((primary.Left + primary.Right) / 2.0 - bounds.Left) / scale, (primary.Bottom - bounds.Top) / scale - 24);
        if (!colony.Screens.SequenceEqual(screens) || !previousFoodAreas.SequenceEqual(colony.FoodAreas) || colony.Home != nest)
        {
            if (Math.Abs(previousScale - scale) > .001)
            {
                foreach (var ant in colony.Workers) { ant.Position *= previousScale / scale; ant.LastFloor *= previousScale / scale; }
                foreach (var food in colony.Foods) food.Position *= previousScale / scale;
            }
            colony.ConfigureScreens((bounds.Right - bounds.Left) / scale, (bounds.Bottom - bounds.Top) / scale, screens, nest, new Vec((work.Left - bounds.Left) / scale, (work.Top - bounds.Top) / scale));
        }
        work = bounds;
        overlay.Width = colony.Width; overlay.Height = colony.Height;
        NativeDesktop.PlaceOverlay(overlayHandle, bounds);
    }
    void ScanWindows() { UpdateBounds(); colony.UpdateWalls(NativeDesktop.Windows(overlayHandle, work, scale, scale)); }
    void Tick(object? sender, EventArgs e)
    {
        double dt = Math.Min(.12, clock.Elapsed.TotalSeconds); clock.Restart();
        scanClock += dt; refreshClock += dt; saveClock += dt;
        if (scanClock > .1) { ScanWindows(); scanClock = 0; }
        if (NativeDesktop.GetCursorPos(out var cursor))
        {
            var point = new Point((cursor.X - work.Left) / scale, (cursor.Y - work.Top) / scale);
            NativeDesktop.ClickThrough(overlayHandle, !surface.NestHitBox.Contains(point));
        }
        if (!paused) colony.Step(dt * speed);
        if (overlay.IsVisible) surface.InvalidateVisual();
        if (refreshClock > .2) { if (panel.IsVisible) panel.Refresh(); refreshClock = 0; }
        if (saveClock > 30 && !smoke) { Save(); saveClock = 0; }
    }
    void Reset()
    {
        var screens = colony.Screens; var nest = colony.Home;
        colony = new DesktopColony(overlay.Width, overlay.Height);
        colony.ConfigureScreens(overlay.Width, overlay.Height, screens, nest);
        surface = new OverlaySurface(colony) { ShowTrails = surface.ShowTrails, ShowWalls = surface.ShowWalls };
        overlay.Content = surface; surface.MouseLeftButtonDown += (_, args) => { if (surface.NestHitBox.Contains(args.GetPosition(surface))) OpenPanel(); };
        ScanWindows(); for (int i = 0; i < 5; i++) colony.SpawnFood();
        panel.Hide(); IsExiting = true; panel.Close(); IsExiting = false;
        CreatePanel(); OpenPanel(); speed = 1;
    }
    void Save()
    {
        try {
            Directory.CreateDirectory(stateFolder); string target = Path.Combine(stateFolder, "colony.json"), temporary = target + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(colony.Snapshot())); File.Move(temporary, target, true);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Simulation continues if the local save is unavailable. */ }
    }
    void SaveSmoke()
    {
        string output = Path.Combine(AppContext.BaseDirectory, "smoke-results"); Directory.CreateDirectory(output);
        var visual = (FrameworkElement)panel.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var dc = background.RenderOpen()) dc.DrawRectangle(panel.Background, null, new Rect(0, 0, visual.ActualWidth, visual.ActualHeight));
        bitmap.Render(background); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(output, "nest-interior.png"))) encoder.Save(file);
        int style = NativeDesktop.ExtendedStyle(overlayHandle);
        bool outsideNest = !NativeDesktop.GetCursorPos(out var cursor) || !surface.NestHitBox.Contains(new Point((cursor.X - work.Left) / scale, (cursor.Y - work.Top) / scale));
        bool hitPolicyMatchesCursor = ((style & 0x20) != 0) == outsideNest;
        var checks = new { screens = colony.Screens.Count, desktopBoundsMatch = NativeDesktop.BoundsMatch(overlayHandle, work), drawingSizeMatches = Math.Abs(surface.ActualWidth - colony.Width) < 1 && Math.Abs(surface.ActualHeight - colony.Height) < 1, windows = colony.Walls.Count, time = colony.Time, width = colony.Width, height = colony.Height, dpi = scale, transparent = overlay.AllowsTransparency, borderless = overlay.WindowStyle == WindowStyle.None, noActivate = (style & 0x08000000) != 0, hitPolicyMatchesCursor, foodOnFloor = colony.Foods.All(f => colony.CanWalk(f.Position)), workers = colony.Workers.Count, windowOcclusion = RenderingChecks.WindowOcclusion() };
        bool nestCutaway = RenderingChecks.NestCutaway(output);
        File.WriteAllText(Path.Combine(output, "native-check.json"), JsonSerializer.Serialize(checks));
        File.WriteAllText(Path.Combine(output, "nest-cutaway-check.json"), JsonSerializer.Serialize(new { nestCutaway }));
        SaveDesktopDemo(output);
        if (!checks.desktopBoundsMatch || !checks.drawingSizeMatches || !checks.windowOcclusion || !checks.transparent || !checks.borderless || !checks.noActivate || !checks.hitPolicyMatchesCursor || !checks.foodOnFloor) throw new InvalidOperationException("原生桌面检查失败。");
    }
    static void SaveDesktopDemo(string output)
    {
        // This is a simulated desktop illustration, never a capture of user windows.
        var demo = new DesktopColony(1280, 720);
        demo.UpdateWalls([new Wall(1, 300, 90, 580, 500), new Wall(2, 960, 160, 255, 360)]);
        for (int i = 0; i < 5; i++) demo.SpawnFood(); demo.Step(150);
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
        {
            dc.DrawRectangle(AntPainter.ColorBrush("#617364"), null, new Rect(0, 0, 1280, 720));
            AntPainter.Text(dc, "桌面蚁境", new Point(30, 25), 24, AntPainter.ColorBrush("#e6ecd3"));
            AntPainter.Text(dc, "桌面空地觅食 · 窗口视作高墙 · 底部巢穴归巢", new Point(30, 59), 13, AntPainter.ColorBrush("#c6d3ba"));
            foreach (var wall in demo.Walls)
            {
                dc.DrawRoundedRectangle(AntPainter.ColorBrush("#29342e"), new Pen(AntPainter.ColorBrush("#96ac8c"), 1), new Rect(wall.X, wall.Y, wall.Width, wall.Height), 8, 8);
                dc.DrawRectangle(AntPainter.ColorBrush("#39463c"), null, new Rect(wall.X + 1, wall.Y + 1, wall.Width - 2, 34));
                AntPainter.Text(dc, "工作窗口 · 高墙区域", new Point(wall.X + 14, wall.Y + 9), 12, AntPainter.ColorBrush("#d7e2c8"));
                AntPainter.Text(dc, "工蚁主要沿窗口边缘绕行", new Point(wall.X + 22, wall.Y + 70), 15, AntPainter.ColorBrush("#a4b49a"));
                AntPainter.Text(dc, "窗口遮住工蚁时，它们继续在底层爬行", new Point(wall.X + 22, wall.Y + 102), 11, AntPainter.ColorBrush("#889b81"));
            }
            AntPainter.Text(dc, $"工蚁 {demo.Workers.Count}   已运回 {demo.Delivered}   窗口示意 / 非真实桌面截图", new Point(30, 681), 12, AntPainter.ColorBrush("#e3ecd3"));
        }
        var demoSurface = new OverlaySurface(demo) { ShowTrails = true, Width = 1280, Height = 720 };
        demoSurface.Measure(new Size(1280, 720)); demoSurface.Arrange(new Rect(0, 0, 1280, 720)); demoSurface.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32); bitmap.Render(background); bitmap.Render(demoSurface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(output, "desktop-demo.png")); encoder.Save(file);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        IsExiting = true; timer?.Stop(); if (!smoke && colony != null) Save();
        if (tray != null) { tray.Visible = false; tray.Dispose(); }
        trayIcon?.Dispose();
        singleInstance?.ReleaseMutex(); singleInstance?.Dispose(); base.OnExit(e);
    }
}
