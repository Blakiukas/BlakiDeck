using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas
{
    internal sealed class WebShortcutDialog : SettingsWindow
    {
        private readonly TextBox name = new(), url = new();
        public string ShortcutName => name.Text;
        public string Url => url.Text;
        public WebShortcutDialog(string initialName, string initialUrl)
        {
            Title = "Web shortcut"; Width = 560; Height = 300; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            Foreground = Brushes.White; ResizeMode = ResizeMode.NoResize;
            name.Text = initialName; url.Text = initialUrl;
            ConfigureTextBox(name); ConfigureTextBox(url);
            Border shell = new() { Background = new SolidColorBrush(Color.FromArgb(250,44,44,44)), BorderBrush = new SolidColorBrush(Color.FromArgb(60,255,255,255)), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(14), Padding = new Thickness(22) };
            Grid layout = new(); layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) }); layout.RowDefinitions.Add(new RowDefinition());
            DockPanel header = new() { Cursor = System.Windows.Input.Cursors.SizeAll };
            header.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
            StackPanel actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button cancel = ActionButton("CANCEL"), apply = ActionButton("APPLY"); cancel.Margin = new Thickness(0,0,8,0);
            cancel.Click += (_,_) => DialogResult = false; apply.Click += (_,_) => DialogResult = true; actions.Children.Add(cancel); actions.Children.Add(apply);
            DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions); header.Children.Add(new TextBlock { Text = "WEB SHORTCUT", FontFamily = (FontFamily)Application.Current.FindResource("TekoFont"), FontSize = 29, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            layout.Children.Add(header);
            StackPanel body = new() { Margin = new Thickness(0,18,0,0) }; Grid.SetRow(body, 1);
            body.Children.Add(Label("NAME")); body.Children.Add(name); body.Children.Add(Label("ADDRESS", 15)); body.Children.Add(url);
            layout.Children.Add(body); shell.Child = layout; Content = shell;
        }

        private static TextBlock Label(string text, double top = 0) => new() { Text = text, FontFamily = (FontFamily)Application.Current.FindResource("TekoFont"), FontSize = 21, FontWeight = FontWeights.Bold, Margin = new Thickness(2,top,0,4) };
        private static void ConfigureTextBox(TextBox box)
        {
            box.Height = 42; box.FontSize = 18; box.Foreground = Brushes.White;
            box.Background = new SolidColorBrush(Color.FromRgb(30,30,30));
            box.BorderBrush = new SolidColorBrush(Color.FromArgb(85,255,255,255));
            box.BorderThickness = new Thickness(1); box.Padding = new Thickness(10,7,10,5);
        }
        private static Button ActionButton(string text)
        {
            Button button = new() { Content = text, Width = 105, Height = 36 };
            if (Application.Current.TryFindResource("DashboardDialogButton") is Style style) button.Style = style;
            return button;
        }
    }
}
