using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using AntField.Core;

namespace AntField.Desktop;

internal static class NativeDesktop
{
    [StructLayout(LayoutKind.Sequential)] internal struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out NativePoint point);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] static extern int FrameBounds(IntPtr hwnd, int attribute, out NativeRect rect, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] static extern int WindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    delegate bool MonitorProc(IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorProc callback, IntPtr data);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    internal static List<NativeRect> WorkAreas(bool full = false)
    {
        var result = new List<NativeRect>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data) => {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info)) result.Add(full ? info.Monitor : info.Work);
            return true;
        }, IntPtr.Zero);
        return result;
    }
    internal static void PlaceOverlay(IntPtr hwnd, NativeRect bounds) => SetWindowPos(hwnd, new IntPtr(-1), bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, 0x10);
    internal static NativeRect WorkArea()
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromPoint(new NativePoint(), 1), ref info)) throw new InvalidOperationException("无法读取主屏幕工作区域。");
        return info.Work;
    }
    internal static List<Wall> Windows(IntPtr overlay, NativeRect work, double scaleX, double scaleY)
    {
        var result = new List<Wall>();
        EnumWindows((hwnd, _) =>
        {
            if (hwnd == overlay || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
            const int desktopOverlay = 0x08080080; // No-activate, layered tool windows are overlays, not walls.
            if ((GetWindowLong(hwnd, -20) & desktopOverlay) == desktopOverlay) return true;
            if (WindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            var name = new StringBuilder(128); GetClassName(hwnd, name, 128);
            if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "tooltips_class32") return true;
            if (FrameBounds(hwnd, 9, out var r, Marshal.SizeOf<NativeRect>()) != 0 && !GetWindowRect(hwnd, out r)) return true;
            if (r.Right - r.Left < 80 || r.Bottom - r.Top < 45 || r.Right <= work.Left || r.Left >= work.Right || r.Bottom <= work.Top || r.Top >= work.Bottom) return true;
            result.Add(new Wall(hwnd.ToInt64(), (r.Left - work.Left) / scaleX, (r.Top - work.Top) / scaleY, (r.Right - r.Left) / scaleX, (r.Bottom - r.Top) / scaleY));
            return true;
        }, IntPtr.Zero);
        return result;
    }
    internal static void ClickThrough(IntPtr hwnd, bool enabled)
    {
        const int transparent = 0x20, toolWindow = 0x80, noActivate = 0x08000000;
        int style = GetWindowLong(hwnd, -20) | toolWindow | noActivate;
        int wanted = enabled ? style | transparent : style & ~transparent;
        if (wanted != GetWindowLong(hwnd, -20)) SetWindowLong(hwnd, -20, wanted);
    }
    internal static bool BoundsMatch(IntPtr hwnd, NativeRect expected) => GetWindowRect(hwnd, out var actual) && actual.Left == expected.Left && actual.Top == expected.Top && actual.Right == expected.Right && actual.Bottom == expected.Bottom;
    internal static int ExtendedStyle(IntPtr hwnd) => GetWindowLong(hwnd, -20);
    internal static double DpiScale(Window window) => PresentationSource.FromVisual(window)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
}
