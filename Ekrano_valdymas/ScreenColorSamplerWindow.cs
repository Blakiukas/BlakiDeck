using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Ekrano_valdymas
{
    internal sealed class ScreenColorSamplerWindow : Window
    {
        private readonly Canvas surface;
        private bool ended;

        public event Action<Color>? ColorSampled;
        public event Action? SamplingEnded;

        public ScreenColorSamplerWindow()
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
            ShowInTaskbar = false;
            Topmost = true;
            Cursor = PipetteCursorFactory.Create();
            Left = 0;
            Top = 0;
            Width = 1;
            Height = 1;

            surface = new Canvas
            {
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                Focusable = true
            };
            Content = surface;

            PreviewMouseLeftButtonDown += OnMouseLeftButtonDown;
            PreviewMouseRightButtonDown += OnMouseRightButtonDown;
            PreviewKeyDown += OnPreviewKeyDown;
            SourceInitialized += OnSourceInitialized;
            Loaded += (_, _) => { OnSourceInitialized(this, EventArgs.Empty); Activate(); Keyboard.Focus(surface); };
            Closed += (_, _) => End();
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            int left = GetSystemMetrics(76);
            int top = GetSystemMetrics(77);
            int width = GetSystemMetrics(78);
            int height = GetSystemMetrics(79);
            SetWindowPos(handle, new IntPtr(-1), left, top, width, height, 0x0040);
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            ShowWindow(handle, 0);
            DwmFlush();
            if (TrySampleScreenPixel(out Color color)) ColorSampled?.Invoke(color);
            ShowWindow(handle, 5);
            SetWindowPos(handle, new IntPtr(-1), GetSystemMetrics(76), GetSystemMetrics(77),
                GetSystemMetrics(78), GetSystemMetrics(79), 0x0040);
            Activate();
            Keyboard.Focus(surface);
            e.Handled = true;
        }

        private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            End();
            e.Handled = true;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            End();
            e.Handled = true;
        }

        private void End()
        {
            if (ended) return;
            ended = true;
            SamplingEnded?.Invoke();
            if (IsVisible) Close();
        }

        private static bool TrySampleScreenPixel(out Color color)
        {
            color = Colors.Transparent;
            if (!GetCursorPos(out NativePoint point)) return false;
            IntPtr desktop = GetDC(IntPtr.Zero);
            if (desktop == IntPtr.Zero) return false;
            try
            {
                uint value = GetPixel(desktop, point.X, point.Y);
                if (value == 0xFFFFFFFF) return false;
                color = Color.FromRgb((byte)(value & 0xFF),
                    (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));
                return true;
            }
            finally { ReleaseDC(IntPtr.Zero, desktop); }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr dc, int x, int y);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
            int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    }
}
