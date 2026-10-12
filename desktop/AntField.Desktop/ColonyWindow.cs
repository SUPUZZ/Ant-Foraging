using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AntField.Core;

namespace AntField.Desktop;

internal sealed class ColonyWindow : Window
{
    readonly DesktopColony colony;
    readonly TextBlock stats, status, chamberDetail;
    readonly NestView nestView;
    readonly Func<bool> isPaused;
    readonly Action togglePause;
    readonly Button pause;
    public Action<double>? SpeedChanged;
    public Action<bool>? TrailsChanged, WallsChanged;
    public Action? ResetRequested;
    bool exiting;
    public ColonyWindow(DesktopColony colony, Func<bool> isPaused, Action togglePause)
    {
        this.colony = colony; this.isPaused = isPaused; this.togglePause = togglePause;
        Title = "桌面蚁境 · 蚁窝内部"; Width = 920; Height = 800; MinWidth = 700; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = AntPainter.ColorBrush("#16211c"); Foreground = AntPainter.ColorBrush("#dce5cf"); FontFamily = new FontFamily("Microsoft YaHei UI");
        var stack = new StackPanel { Margin = new Thickness(25, 20, 25, 18) };
        var header = new DockPanel(); var exit = Button("退出蚁境", () => { exiting = true; Application.Current.Shutdown(); }); DockPanel.SetDock(exit, Dock.Right); header.Children.Add(exit);
        header.Children.Add(new TextBlock { Text = "蚁窝内部", FontSize = 26, FontWeight = FontWeights.SemiBold }); stack.Children.Add(header);
        status = new TextBlock { Text = "工蚁正在桌面空地探索", FontSize = 12, Foreground = AntPainter.ColorBrush("#a8b99b"), Margin = new Thickness(0, 10, 0, 15), TextWrapping = TextWrapping.Wrap }; stack.Children.Add(status);
        stats = new TextBlock { FontSize = 16, Foreground = AntPainter.ColorBrush("#d6e5b7"), Margin = new Thickness(0, 0, 0, 14), TextWrapping = TextWrapping.Wrap }; stack.Children.Add(stats);
        nestView = new NestView(colony); stack.Children.Add(nestView);
        chamberDetail = new TextBlock { FontSize = 12, Foreground = AntPainter.ColorBrush("#b7c5a6"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 15) };
        nestView.ChamberSelected += _ => RefreshDetail(); stack.Children.Add(chamberDetail);
        var buttons = new WrapPanel();
        pause = Button("暂停", togglePause); buttons.Children.Add(pause);
        buttons.Children.Add(Button("补充随机食物", () => { colony.SpawnFood(); Refresh(); }));
        buttons.Children.Add(Button("收起到桌面", Hide));
        buttons.Children.Add(Button("重建蚁群", () => ResetRequested?.Invoke()));
        stack.Children.Add(buttons);
        var options = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        options.Children.Add(Slider("时间速度", .5, 4, 1, .5, value => SpeedChanged?.Invoke(value), v => $"{v:0.0}×"));
        options.Children.Add(Slider("食物出现间隔", 5, 40, colony.SpawnInterval, 1, value => colony.SpawnInterval = value, v => $"{v:0}秒"));
        stack.Children.Add(options);
        var checks = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var trails = new CheckBox { Content = "显示信息素", Foreground = Foreground, Margin = new Thickness(0, 0, 24, 0) }; trails.Checked += (_, _) => TrailsChanged?.Invoke(true); trails.Unchecked += (_, _) => TrailsChanged?.Invoke(false); checks.Children.Add(trails);
        var walls = new CheckBox { Content = "显示窗口墙边界", Foreground = Foreground }; walls.Checked += (_, _) => WallsChanged?.Invoke(true); walls.Unchecked += (_, _) => WallsChanged?.Invoke(false); checks.Children.Add(walls); stack.Children.Add(checks);
        stack.Children.Add(new TextBlock { Text = "点击剖面中的房间查看详情。关闭此窗口仅收起；托盘菜单可暂停或退出。", FontSize = 11, Foreground = AntPainter.ColorBrush("#8fa08c"), Margin = new Thickness(0, 16, 0, 0), TextWrapping = TextWrapping.Wrap });
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Closing += (_, e) => { if (!exiting && !App.IsExiting) { e.Cancel = true; Hide(); } };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };
        Refresh();
    }
    static Button Button(string text, Action click)
    {
        var button = new Button { Content = text, Background = AntPainter.ColorBrush("#32412e"), Foreground = AntPainter.ColorBrush("#e0e9ce"), BorderBrush = AntPainter.ColorBrush("#5b6c4c"), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 10, 0), Cursor = Cursors.Hand };
        button.Click += (_, _) => click(); return button;
    }
    FrameworkElement Slider(string name, double min, double max, double value, double tick, Action<double> change, Func<double, string> format)
    {
        var container = new StackPanel { Width = 265, Margin = new Thickness(0, 0, 12, 0) };
        var text = new TextBlock { Text = $"{name}  {format(value)}", FontSize = 11, Foreground = Foreground };
        var slider = new System.Windows.Controls.Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = tick, IsSnapToTickEnabled = true, Margin = new Thickness(0, 7, 0, 0) };
        slider.ValueChanged += (_, _) => { text.Text = $"{name}  {format(slider.Value)}"; change(slider.Value); }; container.Children.Add(text); container.Children.Add(slider); return container;
    }
    void RefreshDetail()
    {
        chamberDetail.Text = nestView.Selected switch {
            0 => "蚁后室：蚁后由巢内工蚁照料。有足够储粮时每 40 秒产卵；缺粮会暂停育幼。",
            1 => $"育幼室：{colony.Nursery.Count} 个育幼个体。卵 → 幼虫 → 蛹 → 工蚁；阶段分别需 55 / 70 / 85 秒仿真时间，发育消耗储粮。",
            2 => $"粮仓：现有 {colony.Stored} 粒储粮，累计运回 {colony.Delivered} 粒。工蚁恢复体力、蚁后和育幼共同消耗粮食。",
            _ => $"工蚁休息区：{colony.Workers.Count(a => a.State == AntState.InNest)} 只在巢内恢复体力，{colony.Workers.Count(a => a.State != AntState.InNest)} 只在外活动。累计羽化 {colony.Births} 只新工蚁。"
        };
    }
    public void Refresh()
    {
        stats.Text = $"工蚁  {colony.Workers.Count}       搬运  {colony.Carrying}       已运回  {colony.Delivered}       储粮  {colony.Stored}       育幼  {colony.Nursery.Count}       窗下  {colony.Workers.Count(a => a.State == AntState.UnderWindow)}";
        status.Text = $"{(isPaused() ? "已暂停" : "运行中")} · 识别到 {colony.Walls.Count} 个窗口墙 · {colony.Foods.Count} 处食物 · 工蚁在底层沿窗口墙绕行" + (colony.FoodSpaceAvailable ? "" : " · 暂无可达桌面空地，等待窗口布局改变");
        pause.Content = isPaused() ? "继续" : "暂停"; RefreshDetail(); nestView.InvalidateVisual();
    }
}
