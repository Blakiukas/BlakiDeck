using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Ekrano_valdymas
{
    public partial class HueColorPickerDialog : SettingsWindow
    {
        private const int WheelSize = 360;
        private const double Radius = WheelSize / 2d;
        private bool mouseSelecting;

        public Color SelectedColor { get; private set; }

        public HueColorPickerDialog(Color initialColor,string heading="LIGHT COLOR")
        {
            SelectedColor = initialColor;
            InitializeComponent();
            colorHeading.Text=heading;
            colorWheel.Source = CreateColorWheel();
            Loaded += (_, _) => PositionMarkerForColor(initialColor);
            UpdatePreview();
        }

        private static WriteableBitmap CreateColorWheel()
        {
            int stride = WheelSize * 4;
            byte[] pixels = new byte[stride * WheelSize];
            for (int py = 0; py < WheelSize; py++)
            for (int px = 0; px < WheelSize; px++)
            {
                double dx = px + 0.5 - Radius;
                double dy = py + 0.5 - Radius;
                double saturation = Math.Sqrt(dx * dx + dy * dy) / Radius;
                if (saturation > 1) continue;
                double hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                Color color = HsvToRgb(hue, saturation, 1);
                int index = py * stride + px * 4;
                pixels[index] = color.B;
                pixels[index + 1] = color.G;
                pixels[index + 2] = color.R;
                pixels[index + 3] = 255;
            }
            WriteableBitmap bitmap = new(WheelSize, WheelSize, 96, 96, PixelFormats.Pbgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, WheelSize, WheelSize), pixels, stride, 0);
            return bitmap;
        }

        private void SelectAt(Point point)
        {
            double dx = point.X - Radius;
            double dy = point.Y - Radius;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > Radius)
            {
                dx *= Radius / distance;
                dy *= Radius / distance;
                distance = Radius;
            }
            double hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
            SelectedColor = HsvToRgb(hue, distance / Radius, 1);
            PositionMarker(new Point(Radius + dx, Radius + dy));
            UpdatePreview();
        }

        private void PositionMarkerForColor(Color color)
        {
            (double hue, double saturation) = RgbToHs(color);
            double radians = hue * Math.PI / 180;
            PositionMarker(new Point(Radius + Math.Cos(radians) * saturation * Radius,
                                     Radius + Math.Sin(radians) * saturation * Radius));
        }

        private void PositionMarker(Point point)
        {
            Canvas.SetLeft(selectionMarker, point.X - selectionMarker.Width / 2);
            Canvas.SetTop(selectionMarker, point.Y - selectionMarker.Height / 2);
        }

        private void UpdatePreview() => selectedPreview.Background = new SolidColorBrush(SelectedColor);

        private void ColorWheel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            mouseSelecting = true;
            colorWheel.CaptureMouse();
            SelectAt(e.GetPosition(colorWheel));
        }

        private void ColorWheel_MouseMove(object sender, MouseEventArgs e)
        {
            if (mouseSelecting) SelectAt(e.GetPosition(colorWheel));
        }

        private void ColorWheel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!mouseSelecting) return;
            SelectAt(e.GetPosition(colorWheel));
            mouseSelecting = false;
            colorWheel.ReleaseMouseCapture();
        }

        private void ColorWheel_TouchDown(object sender, TouchEventArgs e)
        {
            colorWheel.CaptureTouch(e.TouchDevice);
            SelectAt(e.GetTouchPoint(colorWheel).Position);
            e.Handled = true;
        }

        private void ColorWheel_TouchMove(object sender, TouchEventArgs e)
        {
            SelectAt(e.GetTouchPoint(colorWheel).Position);
            e.Handled = true;
        }

        private void ColorWheel_TouchUp(object sender, TouchEventArgs e)
        {
            SelectAt(e.GetTouchPoint(colorWheel).Position);
            colorWheel.ReleaseTouchCapture(e.TouchDevice);
            e.Handled = true;
        }

        private static Color HsvToRgb(double hue, double saturation, double value)
        {
            double chroma = value * saturation;
            double component = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
            double match = value - chroma;
            (double r, double g, double b) = hue switch
            {
                < 60 => (chroma, component, 0d), < 120 => (component, chroma, 0d),
                < 180 => (0d, chroma, component), < 240 => (0d, component, chroma),
                < 300 => (component, 0d, chroma), _ => (chroma, 0d, component)
            };
            return Color.FromRgb((byte)Math.Round((r + match) * 255),
                (byte)Math.Round((g + match) * 255), (byte)Math.Round((b + match) * 255));
        }

        private static (double Hue, double Saturation) RgbToHs(Color color)
        {
            double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;
            double hue = delta == 0 ? 0 : max == r ? 60 * (((g - b) / delta) % 6)
                : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
            if (hue < 0) hue += 360;
            return (hue, max == 0 ? 0 : delta / max);
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
        private void Apply_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    }
}
