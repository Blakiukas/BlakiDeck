using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Ekrano_valdymas
{
    internal sealed class UsageOverlayWindow : Window
    {
        private readonly TextBlock valueText;
        private readonly Window dashboard;
        private readonly Border dimOverlay;

        public UsageOverlayWindow(Window owner)
        {
            dashboard = owner;
            Owner = owner;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;
            Height = 32;

            valueText = new TextBlock
            {
                Text = "CPU: 0.0%    GPU: N/A    RAM: 0 MB    NET: 0.0 MB/s    WEB: 0 MB",
                Foreground = Application.Current.TryFindResource("UsageBrush") as Brush ?? Brushes.Orange,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Border surface = new()
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(9),
                Child = valueText
            };
            surface.SetResourceReference(Border.BackgroundProperty, "ModuleBackgroundBrush");
            dimOverlay = new Border
            {
                Background = Brushes.Black,
                Opacity = 0,
                IsHitTestVisible = false
            };
            Grid root = new();
            root.Children.Add(surface);
            root.Children.Add(dimOverlay);
            Content = root;
        }

        public void UpdateText(string text) => valueText.Text = text;
        public void SetAutoDimmed(bool dimmed) => SetAutoDimOpacity(dimmed ? 0.70 : 1.0);

        public void SetAutoDimOpacity(double opacity)
            => SetAutoDimAmount(1d - Math.Clamp(opacity, 0, 1));

        public void SetUiOpacity(double opacity)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = Math.Clamp(opacity, 0.60, 1.0);
        }

        public void SetAutoDimAmount(double amount)
        {
            DoubleAnimation animation = new(Math.Clamp(amount, 0, 0.80),
                TimeSpan.FromMilliseconds(650))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            dimOverlay.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        public void AlignToDashboard()
        {
            if (!IsVisible || dashboard.WindowState == WindowState.Minimized) return;
            Rect bounds = CalculateBounds(new Rect(
                dashboard.Left, dashboard.Top, dashboard.ActualWidth, dashboard.ActualHeight), Height);
            Width = bounds.Width;
            Left = bounds.Left;
            Top = bounds.Top;
            Topmost = dashboard.Topmost;
        }

        internal static Rect CalculateBounds(Rect dashboardBounds, double overlayHeight = 32)
        {
            double edgeGap = Math.Max(8, dashboardBounds.Width * 0.008);
            double width = Math.Max(300, dashboardBounds.Width * 0.30);
            return new Rect(
                dashboardBounds.Right - width - edgeGap,
                dashboardBounds.Top - overlayHeight - 5,
                width,
                overlayHeight);
        }

        internal static bool VerifyGeometry()
        {
            Rect dashboardBounds = new(100, 200, 1745, 560);
            Rect overlay = CalculateBounds(dashboardBounds);
            return Math.Abs(overlay.Width - 523.5) < 0.01 &&
                   Math.Abs(dashboardBounds.Right - overlay.Right - 13.96) < 0.01 &&
                   Math.Abs(overlay.Bottom - (dashboardBounds.Top - 5)) < 0.01;
        }
    }
}

