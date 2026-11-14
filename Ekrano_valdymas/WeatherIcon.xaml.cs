using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Ekrano_valdymas
{
    public partial class WeatherIcon : UserControl
    {
        private static readonly Brush SunBrush = new SolidColorBrush(Color.FromRgb(255, 204, 36));
        private static readonly Brush CloudBrush = new LinearGradientBrush(
            Color.FromRgb(248, 250, 252),
            Color.FromRgb(163, 174, 188),
            90);
        private static readonly Brush DarkCloudBrush = new LinearGradientBrush(
            Color.FromRgb(145, 157, 173),
            Color.FromRgb(79, 91, 108),
            90);
        private static readonly Brush RainBrush = new SolidColorBrush(Color.FromRgb(73, 176, 255));
        private static readonly Brush SnowBrush = new SolidColorBrush(Color.FromRgb(210, 239, 255));

        public static readonly DependencyProperty WeatherCodeProperty =
            DependencyProperty.Register(
                nameof(WeatherCode),
                typeof(int),
                typeof(WeatherIcon),
                new PropertyMetadata(2, OnWeatherCodeChanged));

        public WeatherIcon()
        {
            InitializeComponent();
            Loaded += (_, _) => DrawIcon();
        }

        public int WeatherCode
        {
            get => (int)GetValue(WeatherCodeProperty);
            set => SetValue(WeatherCodeProperty, value);
        }

        private static void OnWeatherCodeChanged(
            DependencyObject dependencyObject,
            DependencyPropertyChangedEventArgs e)
        {
            if (dependencyObject is WeatherIcon icon && icon.IsLoaded)
                icon.DrawIcon();
        }

        private void DrawIcon()
        {
        using var callbackTiming = new UiCallbackTiming("WeatherIcon.DrawIcon");
            IconCanvas.Children.Clear();

            switch (WeatherCode)
            {
                case 0:
                    DrawSun(50, 50, 23);
                    break;
                case 1:
                case 2:
                    DrawSun(35, 34, 18);
                    DrawCloud(16, 39, false);
                    break;
                case 3:
                    DrawCloud(12, 35, false);
                    break;
                case 45:
                case 48:
                    DrawCloud(12, 27, true);
                    DrawFog();
                    break;
                case 51:
                case 53:
                case 55:
                case 61:
                case 63:
                case 65:
                case 80:
                case 81:
                case 82:
                    DrawCloud(12, 26, WeatherCode >= 63);
                    DrawRain(WeatherCode >= 63 ? 4 : 3);
                    break;
                case 71:
                case 73:
                case 75:
                case 77:
                    DrawCloud(12, 24, true);
                    DrawSnow();
                    break;
                case 95:
                case 96:
                case 99:
                    DrawCloud(12, 23, true);
                    DrawLightning();
                    DrawRain(2);
                    break;
                default:
                    DrawCloud(12, 35, false);
                    break;
            }
        }

        private void DrawSun(double centerX, double centerY, double radius)
        {
            for (int ray = 0; ray < 8; ray++)
            {
                double angle = ray * System.Math.PI / 4;
                double startRadius = radius + 6;
                double endRadius = radius + 16;

                IconCanvas.Children.Add(new Line
                {
                    X1 = centerX + System.Math.Cos(angle) * startRadius,
                    Y1 = centerY + System.Math.Sin(angle) * startRadius,
                    X2 = centerX + System.Math.Cos(angle) * endRadius,
                    Y2 = centerY + System.Math.Sin(angle) * endRadius,
                    Stroke = SunBrush,
                    StrokeThickness = 5,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
            }

            AddEllipse(centerX - radius, centerY - radius, radius * 2, radius * 2, SunBrush);
        }

        private void DrawCloud(double left, double top, bool dark)
        {
            Brush brush = dark ? DarkCloudBrush : CloudBrush;

            Path cloud = new()
            {
                Data = Geometry.Parse(
                    "M12,42 C12,31 20,23 31,23 C35,10 47,4 59,9 C68,12 73,20 74,29 " +
                    "C87,29 96,39 96,52 C96,65 86,74 73,74 L34,74 C21,74 12,62 12,49 Z"),
                Fill = brush,
                Stretch = Stretch.Fill,
                Width = 82,
                Height = 64
            };

            Canvas.SetLeft(cloud, left);
            Canvas.SetTop(cloud, top);
            IconCanvas.Children.Add(cloud);
        }

        private void DrawRain(int drops)
        {
            for (int drop = 0; drop < drops; drop++)
            {
                double x = 30 + drop * 15;
                IconCanvas.Children.Add(new Line
                {
                    X1 = x,
                    Y1 = 72,
                    X2 = x - 5,
                    Y2 = 88,
                    Stroke = RainBrush,
                    StrokeThickness = 5,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
            }
        }

        private void DrawSnow()
        {
            foreach (double x in new[] { 30d, 50d, 70d })
            {
                AddSnowflake(x, 82);
            }
        }

        private void AddSnowflake(double x, double y)
        {
            for (int arm = 0; arm < 3; arm++)
            {
                double angle = arm * System.Math.PI / 3;
                IconCanvas.Children.Add(new Line
                {
                    X1 = x - System.Math.Cos(angle) * 7,
                    Y1 = y - System.Math.Sin(angle) * 7,
                    X2 = x + System.Math.Cos(angle) * 7,
                    Y2 = y + System.Math.Sin(angle) * 7,
                    Stroke = SnowBrush,
                    StrokeThickness = 2.5,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
            }
        }

        private void DrawFog()
        {
            foreach (double y in new[] { 72d, 82d, 92d })
            {
                Line fogLine = new()
                {
                    X1 = y == 82 ? 24 : 34,
                    Y1 = y,
                    X2 = y == 82 ? 78 : 68,
                    Y2 = y,
                    Stroke = CloudBrush,
                    StrokeThickness = 4,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                IconCanvas.Children.Add(fogLine);
            }
        }

        private void DrawLightning()
        {
            Path lightning = new()
            {
                Data = Geometry.Parse("M51,62 L40,80 L51,79 L44,96 L66,72 L54,73 L62,62 Z"),
                Fill = SunBrush
            };
            IconCanvas.Children.Add(lightning);
        }

        private void AddEllipse(
            double left,
            double top,
            double width,
            double height,
            Brush fill)
        {
            Ellipse ellipse = new()
            {
                Width = width,
                Height = height,
                Fill = fill
            };

            Canvas.SetLeft(ellipse, left);
            Canvas.SetTop(ellipse, top);
            IconCanvas.Children.Add(ellipse);
        }
    }
}
