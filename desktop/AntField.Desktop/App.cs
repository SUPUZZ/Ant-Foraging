using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
    bool paused, smoke, frameTest, persistenceTest;
    int smokeSeconds = 3;
    readonly Stopwatch clock = new();
    readonly Stopwatch maintenanceClock = new();
    TimeSpan lastRenderingTime = TimeSpan.MinValue;
    Task pendingSave = Task.CompletedTask;
    readonly List<double> frameIntervals = new();
    string stateFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AntField");
    ProgressStore progressStore = null!;
    string runId = Guid.NewGuid().ToString("N"), saveStatus = "尚未保存", historyText = "暂无历史进度。";
    bool restoredProgress;
    SavedProgress? loadedProgress;
    internal DesktopColony CurrentColony => colony;
    internal ColonyWindow CurrentPanel => panel;
    Mutex? singleInstance;
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown, smoke = args.Contains("--smoke-test") };
        if (args.Contains("--frame-test")) { app.smoke = true; app.frameTest = true; app.smokeSeconds = 8; }
        if (args.Contains("--persistence-test"))
        {
            app.smoke = app.frameTest = app.persistenceTest = true; app.smokeSeconds = 2;
            app.stateFolder = Path.Combine(AppContext.BaseDirectory, "smoke-results", "persistence");
        }
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
        colony = new DesktopColony(initial.Width, initial.Height, frameTest ? 120 : 48);
        progressStore = new ProgressStore(stateFolder);
        if (!smoke || persistenceTest)
        {
            loadedProgress = progressStore.Load();
            if (loadedProgress != null)
                {
                    var saved = loadedProgress.Colony; restoredProgress = true; runId = loadedProgress.RunId;
                    speed = loadedProgress.Speed; paused = loadedProgress.Paused;
                    saveStatus = $"{(progressStore.RecoveredBackup ? "已恢复备份" : "已恢复进度")} · {loadedProgress.SavedAt.ToLocalTime():MM-dd HH:mm:ss}";
                    bool multi = saved.Screens is { Length: > 0 } && saved.Home != null;
                    colony = DesktopColony.Restore(saved, multi ? saved.Width : initial.Width, multi ? saved.Height : initial.Height);
                    if (multi)
                    {
                        double initialScale = (work.Right - work.Left) / initial.Width;
                        work.Left = (int)Math.Round((work.Left + work.Right) / 2.0 - colony.Home.X * initialScale);
                        work.Top = (int)Math.Round(work.Bottom - (colony.Home.Y + 24) * initialScale);
                    }
                }
            else if (progressStore.LoadFailed) saveStatus = "存档和备份无法读取，已启动新蚁群";
            RefreshHistory();
        }
        surface = new OverlaySurface(colony) { ShowTrails = loadedProgress?.ShowTrails ?? false, ShowWalls = loadedProgress?.ShowWalls ?? false };
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
        ScanWindows(); if (!restoredProgress) colony.SpawnFood();
        if (!smoke && !restoredProgress && !progressStore.LoadFailed) Save(true);
        clock.Start();
        CompositionTarget.Rendering += RenderFrame;
        maintenanceClock.Start();
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += MaintainDesktop; timer.Start();
        if (smoke) { if (!frameTest) { panel.ShowActivated = false; panel.Show(); } }
        else OpenPanel();
        if (smoke)
        {
            var smokeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(smokeSeconds) };
            smokeTimer.Tick += (_, _) => { smokeTimer.Stop(); if (frameTest) SaveFrameTiming(); else SaveSmoke(); Shutdown(); }; smokeTimer.Start();
        }
    }
    void CreatePanel()
    {
        panel = new ColonyWindow(colony, () => paused, TogglePause, speed, surface.ShowTrails, surface.ShowWalls);
        panel.SpeedChanged = value => speed = value;
        panel.TrailsChanged = value => surface.ShowTrails = value;
        panel.WallsChanged = value => surface.ShowWalls = value;
        panel.SaveRequested = () => Save(true);
        panel.ProgressText = () => $"累计仿真时间 {TimeSpan.FromSeconds(colony.Time):d\\天\\ hh\\:mm\\:ss} · 累计羽化 {colony.Births} 只 · {saveStatus}";
        panel.HistoryText = () => historyText;
        panel.Refresh();
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
    void TogglePause() { paused = !paused; clock.Restart(); panel.Refresh(); surface.InvalidateVisual(); }
    void UpdateBounds()
    {
        var areas = NativeDesktop.WorkAreas(true);
        if (areas.Count == 0) areas.Add(NativeDesktop.WorkArea());
        var bounds = new NativeDesktop.NativeRect { Left = areas.Min(a => a.Left), Top = areas.Min(a => a.Top), Right = areas.Max(a => a.Right), Bottom = areas.Max(a => a.Bottom) };
        double previousScale = scale; scale = NativeDesktop.DpiScale(overlay);
        var screens = areas.Select((a, i) => new Wall(i, (a.Left - bounds.Left) / scale, (a.Top - bounds.Top) / scale, (a.Right - a.Left) / scale, (a.Bottom - a.Top) / scale)).ToArray();
        var previousFoodAreas = colony.FoodAreas;
        var foodAreas = NativeDesktop.WorkAreas().Select((a, i) => new Wall(i, (a.Left - bounds.Left) / scale, (a.Top - bounds.Top) / scale, (a.Right - a.Left) / scale, (a.Bottom - a.Top) / scale)).ToArray();
        if (!previousFoodAreas.SequenceEqual(foodAreas)) colony.FoodAreas = foodAreas;
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
        bool changed = work.Left != bounds.Left || work.Top != bounds.Top || work.Right != bounds.Right || work.Bottom != bounds.Bottom || Math.Abs(previousScale - scale) > .001;
        work = bounds;
        if (changed || !NativeDesktop.BoundsMatch(overlayHandle, bounds))
        {
            overlay.Width = colony.Width; overlay.Height = colony.Height;
            NativeDesktop.PlaceOverlay(overlayHandle, bounds);
        }
    }
    void ScanWindows()
    {
        var walls = colony.Walls; var areas = colony.FoodAreas;
        double width = colony.Width, height = colony.Height;
        UpdateBounds(); colony.UpdateWalls(NativeDesktop.Windows(overlayHandle, work, scale, scale));
        if (!ReferenceEquals(walls, colony.Walls) || !ReferenceEquals(areas, colony.FoodAreas) || width != colony.Width || height != colony.Height) surface.InvalidateVisual();
    }
    void AdvanceFrame()
    {
        double dt = Math.Min(.12, clock.Elapsed.TotalSeconds); clock.Restart();
        if (!paused) colony.Step(dt * speed);
        if (!paused && overlay.IsVisible) surface.UpdateFrame();
    }
    void RenderFrame(object? sender, EventArgs e)
    {
        // WPF can issue multiple callbacks for one composition frame.
        if (e is RenderingEventArgs frame)
        {
            if (frame.RenderingTime == lastRenderingTime) return;
            lastRenderingTime = frame.RenderingTime;
        }
        if (smoke) frameIntervals.Add(clock.Elapsed.TotalMilliseconds);
        AdvanceFrame();
    }
    void MaintainDesktop(object? sender, EventArgs e)
    {
        double dt = maintenanceClock.Elapsed.TotalSeconds; maintenanceClock.Restart();
        scanClock += dt; refreshClock += dt; saveClock += dt;
        if (scanClock > .1) { ScanWindows(); scanClock = 0; }
        if (NativeDesktop.GetCursorPos(out var cursor))
        {
            var point = new Point((cursor.X - work.Left) / scale, (cursor.Y - work.Top) / scale);
            NativeDesktop.ClickThrough(overlayHandle, !surface.NestHitBox.Contains(point));
        }
        // Keep the colony alive when no visible WPF window produces frames.
        if (!overlay.IsVisible && !panel.IsVisible && clock.Elapsed.TotalSeconds >= .1) AdvanceFrame();
        if (refreshClock > .2) { if (panel.IsVisible) panel.Refresh(); refreshClock = 0; }
        if (saveClock > 30 && !smoke) { Save(true); saveClock = 0; }
    }
    SavedProgress CaptureProgress() => new(runId, DateTimeOffset.UtcNow, colony.Snapshot(), speed, paused, surface.ShowTrails, surface.ShowWalls);
    void RefreshHistory()
    {
        try
        {
            var records = progressStore.History();
            historyText = records.Length == 0 ? "同一蚁群持续观察，退出时保存，下次接续。" : string.Join("\n", records.Select(r => $"{r.SavedAt.ToLocalTime():yyyy-MM-dd HH:mm} · 累计 {TimeSpan.FromSeconds(r.Colony.Time).TotalMinutes:0.0} 分钟 · 工蚁 {r.Colony.Workers.Length} · 运回 {r.Colony.Delivered} · 羽化 {r.Colony.Births}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { historyText = "暂时无法读取历史记录。"; }
    }
    void Save(bool background = false)
    {
        if (smoke && !persistenceTest) return;
        if (background && !pendingSave.IsCompleted) return;
        if (!background) pendingSave.GetAwaiter().GetResult();
        var snapshot = CaptureProgress();
        if (background)
        {
            saveStatus = "正在保存…";
            pendingSave = Task.Run(() => {
                bool saved = WriteSnapshot(snapshot);
                if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(() => { if (runId == snapshot.RunId) { saveStatus = saved ? $"已保存 · {snapshot.SavedAt.ToLocalTime():HH:mm:ss}" : "保存失败，请重试"; panel.Refresh(); } }));
            });
        }
        else if (!WriteSnapshot(snapshot)) System.Windows.MessageBox.Show("退出前保存失败，请检查存储空间和文件权限。上一次成功保存的进度仍然保留。", "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
    }
    bool WriteSnapshot(SavedProgress snapshot)
    {
        try { progressStore.Save(snapshot); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
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
        RenderingChecks.LargeFood(output);
        RenderingChecks.LongTermObservation(this, output);
        File.WriteAllText(Path.Combine(output, "native-check.json"), JsonSerializer.Serialize(checks));
        File.WriteAllText(Path.Combine(output, "nest-cutaway-check.json"), JsonSerializer.Serialize(new { nestCutaway }));
        SaveFrameTiming();
        SaveDesktopDemo(output);
        if (!checks.desktopBoundsMatch || !checks.drawingSizeMatches || !checks.windowOcclusion || !checks.transparent || !checks.borderless || !checks.noActivate || !checks.hitPolicyMatchesCursor || !checks.foodOnFloor) throw new InvalidOperationException("原生桌面检查失败。");
    }
    void SaveFrameTiming()
    {
        if (frameIntervals.Count > 0)
        {
            string output = Path.Combine(AppContext.BaseDirectory, "smoke-results"); Directory.CreateDirectory(output);
            var sorted = frameIntervals.OrderBy(value => value).ToArray();
            File.WriteAllText(Path.Combine(output, "frame-timing.json"), JsonSerializer.Serialize(new { desktopOnly = frameTest, renderTier = RenderCapability.Tier >> 16, workers = colony.Workers.Count, frames = sorted.Length, averageMs = sorted.Average(), p95Ms = sorted[(int)((sorted.Length - 1) * .95)], maxMs = sorted[^1] }));
        }
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
        IsExiting = true; CompositionTarget.Rendering -= RenderFrame; timer?.Stop(); if ((!smoke || persistenceTest) && colony != null) Save();
        if (tray != null) { tray.Visible = false; tray.Dispose(); }
        trayIcon?.Dispose();
        singleInstance?.ReleaseMutex(); singleInstance?.Dispose(); base.OnExit(e);
    }
}
