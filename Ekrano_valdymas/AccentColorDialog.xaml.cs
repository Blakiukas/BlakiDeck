using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Ekrano_valdymas
{
    public partial class AccentColorDialog : SettingsWindow
    {
        public enum ColorTarget { System, Clock, SystemMonitor, Base, Text, Music, Titles, Labels, Knob, Power }
        private Color titlesColor, labelsColor;
        private double titlesIntensity=100, labelsIntensity=100;
        internal void ConfigurePanelColors(Color titles,Color labels) { titlesColor=titles; labelsColor=labels; titlesIntensity=titles.A/255d*100; labelsIntensity=labels.A/255d*100;UpdateTargetSwatches(); }
        private Color systemColor;
        private Color clockColor;
        private Color systemMonitorColor;
        private Color baseColor;
        private Color textColor;
        private Color musicColor;
        private Color knobColor, powerColor;
        private double knobIntensity=50, powerIntensity=100;
        public Color KnobColor=>knobColor; public Color PowerColor=>powerColor;
        public double KnobIntensity=>knobIntensity; public double PowerIntensity=>powerIntensity;
        internal void ConfigurePlayerLights(Color knob,double knobLevel,Color power,double powerLevel){knobColor=knob;knobIntensity=knobLevel;powerColor=power;powerIntensity=powerLevel;UpdateTargetSwatches();}
        private bool IsPlayerLight=>SelectedTarget is ColorTarget.Music or ColorTarget.Knob or ColorTarget.Power;
        private double musicIntensity;
        public Color MusicColor => musicColor;
        public double MusicIntensity => musicIntensity;
        private double systemIntensity;
        private double clockIntensity;
        private double systemMonitorIntensity;
        private double textIntensity;
        private double baseOpacity;
        private Action<string?>? refreshPicker;
        private Button applyButton=null!;private bool editingPicker;
        private bool updatingIntensity = true;
        private readonly ColorEditHistory<string> editHistory=new();
        private bool historyReady;
        private record ColorState(string[] Colors,double[] Levels);
        private string CaptureColors()=>System.Text.Json.JsonSerializer.Serialize(new ColorState(
            new[]{systemColor,clockColor,systemMonitorColor,baseColor,textColor,musicColor,knobColor,powerColor,titlesColor,labelsColor}.Select(c=>c.ToString()).ToArray(),
            new[]{systemIntensity,clockIntensity,systemMonitorIntensity,baseOpacity,textIntensity,musicIntensity,knobIntensity,powerIntensity,titlesIntensity,labelsIntensity}));
        private void RestoreColors(string json)
        {
            var state=System.Text.Json.JsonSerializer.Deserialize<ColorState>(json)!;
            var c=state.Colors.Select(v=>(Color)ColorConverter.ConvertFromString(v)).ToArray();var l=state.Levels;
            (systemColor,clockColor,systemMonitorColor,baseColor,textColor,musicColor,knobColor,powerColor,titlesColor,labelsColor)=(c[0],c[1],c[2],c[3],c[4],c[5],c[6],c[7],c[8],c[9]);
            (systemIntensity,clockIntensity,systemMonitorIntensity,baseOpacity,textIntensity,musicIntensity,knobIntensity,powerIntensity,titlesIntensity,labelsIntensity)=(l[0],l[1],l[2],l[3],l[4],l[5],l[6],l[7],l[8],l[9]);
            ColorTarget[] targets={ColorTarget.System,ColorTarget.Clock,ColorTarget.SystemMonitor,ColorTarget.Base,ColorTarget.Text,ColorTarget.Music,ColorTarget.Knob,ColorTarget.Power,ColorTarget.Titles,ColorTarget.Labels};
            for(int i=0;i<targets.Length;i++)PreviewChanged?.Invoke(targets[i],c[i],l[i]);
            ColorTarget_Checked(this,new RoutedEventArgs());
        }
        private bool updatingTone = true;
        private ScreenColorSamplerWindow? colorSampler;
        private readonly Color[] savedColors = new Color[4];
        public Color SelectedColor { get; private set; }
        public ColorTarget SelectedTarget { get; private set; } = ColorTarget.System;
        public Color DashboardColor => systemColor;
        public Color ClockColor => clockColor;
        public Color SystemMonitorColor => systemMonitorColor;
        public Color BaseColor => baseColor;
        public Color TextColor => textColor;
        public double DashboardIntensity => systemIntensity;
        public double ClockIntensity => clockIntensity;
        public double SystemMonitorIntensity => systemMonitorIntensity;
        public double TextIntensity => textIntensity;
        public double BaseOpacity => baseOpacity;
        public event Action<ColorTarget, Color, double>? PreviewChanged;

        public AccentColorDialog(
            Color initialSystem,
            Color initialClock,
            Color initialSystemMonitor,
            Color initialBase,
            Color initialText,
            double initialSystemIntensity,
            double initialClockIntensity,
            double initialSystemMonitorIntensity,
            double initialTextIntensity,
            double initialBaseOpacity,
            string[]? initialSavedColors = null, Color? initialMusic = null, double initialMusicIntensity = 100)
        {
            musicColor = initialMusic ?? Color.FromRgb(238,183,100);
            musicIntensity = initialMusicIntensity;
            systemColor = initialSystem;
            clockColor = initialClock;
            systemMonitorColor = initialSystemMonitor;
            baseColor = initialBase;
            textColor = initialText;
            systemIntensity = initialSystemIntensity;
            clockIntensity = initialClockIntensity;
            systemMonitorIntensity = Math.Clamp(initialSystemMonitorIntensity, 35, 100);
            textIntensity = Math.Clamp(initialTextIntensity, 35, 100);
            baseOpacity = initialBaseOpacity;
            SelectedColor = systemColor;
            InitializeComponent();
            MacroUi.ApplyTheme(this);
            sampleColorButton.Content=PipetteCursorFactory.Icon();
            var root=(FrameworkElement)Content;Content=null;
            var dock=new DockPanel();var footer=new Grid{Margin=new Thickness(16,0,16,16)};footer.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);
            var defaults=MacroUi.Button("Default",(_,_)=>ResetSelectedColor());defaults.Width=90;defaults.HorizontalAlignment=HorizontalAlignment.Left;footer.Children.Add(editHistory.Buttons(defaults,RestoreColors));
            var cancel=MacroUi.Button("Cancel",Cancel_Click);cancel.Width=90;Grid.SetColumn(cancel,1);footer.Children.Add(cancel);
            applyButton=MacroUi.Button("Save",Apply_Click);applyButton.Name="applyButton";applyButton.Width=90;Grid.SetColumn(applyButton,2);footer.Children.Add(applyButton);
            var scroll=MacroUi.Scroll(root);AutoFitScroll=scroll;dock.Children.Add(scroll);Content=dock;
            pickerHost.Content=CompactColorPicker.Surface(this,SelectedColor.ToString(),value=>{
                var color=CompactColorPicker.Parse(value,SelectedColor);editingPicker=true;try{SetSelectedColor(Color.FromRgb(color.R,color.G,color.B));double level=color.A*100d/255;intensitySlider.Value=SelectedTarget==ColorTarget.Base||IsPlayerLight?level:100-level;}finally{editingPicker=false;}UpdatePreview();
            },out refreshPicker,valid=>applyButton.IsEnabled=valid,alphaLabel:()=>IsPlayerLight?"Brightness":"Opacity");
            updatingIntensity = false;
            updatingTone = false;
            InitializeSavedColors(initialSavedColors);
            systemTargetRadio.Checked += ColorTarget_Checked;
            clockTargetRadio.Checked += ColorTarget_Checked;
            systemMonitorTargetRadio.Checked += ColorTarget_Checked;
            baseTargetRadio.Checked += ColorTarget_Checked;
            textTargetRadio.Checked += ColorTarget_Checked;
            musicTargetRadio.Checked += ColorTarget_Checked;
            knobTargetRadio.Checked += ColorTarget_Checked;powerTargetRadio.Checked += ColorTarget_Checked;
            titlesTargetRadio.Checked += ColorTarget_Checked; labelsTargetRadio.Checked += ColorTarget_Checked;
            UpdateIntensityState();
            UpdateToneState();
            UpdatePreview();
            Closed += (_, _) => EndScreenColorSampling();
            Loaded+=(_,_)=>{historyReady=true;editHistory.Record(CaptureColors());};
        }

        internal void SelectTarget(ColorTarget target)
        {
            var radio=target switch{ColorTarget.Titles=>titlesTargetRadio,ColorTarget.Labels=>labelsTargetRadio,ColorTarget.SystemMonitor=>systemMonitorTargetRadio,ColorTarget.Clock=>clockTargetRadio,ColorTarget.Base=>baseTargetRadio,ColorTarget.Text=>textTargetRadio,ColorTarget.Knob=>knobTargetRadio,ColorTarget.Power=>powerTargetRadio,ColorTarget.Music=>musicTargetRadio,_=>systemTargetRadio};
            radio.IsChecked=true;
        }

        internal void ConfigureAvailability(Func<ColorTarget,bool> available)
        {
            var radios=new[]{systemTargetRadio,clockTargetRadio,systemMonitorTargetRadio,baseTargetRadio,textTargetRadio,musicTargetRadio,titlesTargetRadio,labelsTargetRadio,knobTargetRadio,powerTargetRadio};
            foreach(ColorTarget target in Enum.GetValues<ColorTarget>())
            {
                var radio=radios[(int)target];radio.IsEnabled=available(target);
                radio.ToolTip=radio.IsEnabled?null:"Not used in the current view. Your saved color is retained.";
                ToolTipService.SetShowOnDisabled(radio,true);
            }
            if(!radios[(int)SelectedTarget].IsEnabled)
                SelectTarget(Enum.GetValues<ColorTarget>().First(t=>radios[(int)t].IsEnabled));
        }

        internal void ConfigureClockOnly()
        {
            SelectTarget(ColorTarget.Clock);Title="Clock color";dialogHeading.Text="CLOCK COLOR";
            ((FrameworkElement)((FrameworkElement)clockTargetRadio.Parent).Parent).Visibility=Visibility.Collapsed;
            intensityPanel.Visibility=Visibility.Collapsed;Height=480;
            DialogWorkArea.Attach(this);
        }

        internal void ConfigureKnobOnly()
        {
            SelectTarget(ColorTarget.Music);Title="Knob backlight color";dialogHeading.Text="KNOB BACKLIGHT COLOR";
            targetDescription.Text="Knob backlight for this player only. Overrides the default for all players.";
            ((FrameworkElement)((FrameworkElement)musicTargetRadio.Parent).Parent).Visibility=Visibility.Collapsed;
            intensityPanel.Visibility=Visibility.Collapsed;Height=480;
            DialogWorkArea.Attach(this);
        }

        private void ColorTarget_Checked(object sender, RoutedEventArgs e)
        {
            if (preview == null) return;
            SelectedTarget = knobTargetRadio?.IsChecked==true ? ColorTarget.Knob : powerTargetRadio?.IsChecked==true ? ColorTarget.Power : titlesTargetRadio?.IsChecked==true ? ColorTarget.Titles : labelsTargetRadio?.IsChecked==true ? ColorTarget.Labels : musicTargetRadio?.IsChecked == true ? ColorTarget.Music : textTargetRadio?.IsChecked == true ? ColorTarget.Text
                : baseTargetRadio?.IsChecked == true ? ColorTarget.Base
                : systemMonitorTargetRadio?.IsChecked == true ? ColorTarget.SystemMonitor
                : clockTargetRadio?.IsChecked == true ? ColorTarget.Clock
                : ColorTarget.System;
            SelectedColor = SelectedTarget switch
            {
                ColorTarget.Titles => titlesColor, ColorTarget.Labels => labelsColor,
                ColorTarget.Clock => clockColor,
                ColorTarget.SystemMonitor => systemMonitorColor,
                ColorTarget.Base => baseColor,
                ColorTarget.Knob=>knobColor, ColorTarget.Power=>powerColor,
                ColorTarget.Music => musicColor,
                ColorTarget.Text => textColor,
                _ => systemColor
            };
            UpdateIntensityState();
            UpdateToneState();
            UpdatePreview();
        }

        private void IntensitySlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (intensityText != null) intensityText.Text = $"{e.NewValue:0}%";
            if (updatingIntensity) return;
            if (SelectedTarget == ColorTarget.Base)
            {
                baseOpacity = e.NewValue;
                UpdatePreview();
                NotifyPreviewChanged();
                return;
            }
            double opacity = IsPlayerLight ? e.NewValue : 100 - e.NewValue;
            if (SelectedTarget == ColorTarget.Titles) titlesIntensity=opacity;
            else if (SelectedTarget == ColorTarget.Labels) labelsIntensity=opacity;
            else if (SelectedTarget == ColorTarget.Clock) clockIntensity = opacity;
            else if (SelectedTarget == ColorTarget.SystemMonitor) systemMonitorIntensity = opacity;
            else if (SelectedTarget == ColorTarget.Text) textIntensity = opacity;
            else if (SelectedTarget == ColorTarget.Knob) knobIntensity=opacity;
            else if (SelectedTarget == ColorTarget.Power) powerIntensity=opacity;
            else if (SelectedTarget == ColorTarget.Music) musicIntensity = opacity;
            else systemIntensity = opacity;
            UpdatePreview();
            NotifyPreviewChanged();
        }

        private void UpdateIntensityState()
        {
            if (intensityPanel == null) return;
            targetDescription.Text=SelectedTarget switch { ColorTarget.Titles=>"This tab: module headings — System Monitor, Macros, Weather.", ColorTarget.Labels=>"This tab: sensor names, weather text and neutral panel labels.", ColorTarget.Text=>"Other interface text and playback controls; excludes panel labels, module titles and clocks.", ColorTarget.SystemMonitor=>"Sensor readings and graphs — CPU, GPU, RAM values.", ColorTarget.Clock=>"Clock digits.", ColorTarget.Music=>"This player: PLAY, PREV and NEXT illumination. NEXT and PREV light while pressed.", ColorTarget.Knob=>"This player: volume marker and knob backlight.", ColorTarget.Power=>"This player: POWER ring and symbol illumination.", ColorTarget.Base=>"Module background color and opacity.", _=>"Dashboard accents and selection highlights." };
            bool isBase = SelectedTarget == ColorTarget.Base;
            intensityPanel.IsEnabled = true;
            intensityPanel.Opacity = 1;
            intensityLabel.Text = IsPlayerLight ? "BRIGHTNESS" : isBase ? "OPACITY" : "TRANSPARENCY";
            double value = SelectedTarget switch
            {
                ColorTarget.Titles => titlesIntensity, ColorTarget.Labels => labelsIntensity,
                ColorTarget.Clock => clockIntensity,
                ColorTarget.SystemMonitor => systemMonitorIntensity,
                ColorTarget.Knob=>knobIntensity, ColorTarget.Power=>powerIntensity,
                ColorTarget.Music => musicIntensity,
                ColorTarget.Text => textIntensity,
                ColorTarget.Base => baseOpacity,
                _ => systemIntensity
            };
            updatingIntensity = true;
            intensitySlider.Minimum = SelectedTarget == ColorTarget.Text ? 0 : 0;
            intensitySlider.Maximum = SelectedTarget is ColorTarget.Text or ColorTarget.SystemMonitor
                ? 65 : 100;
            intensitySlider.Value = isBase || IsPlayerLight ? value : 100 - value;
            updatingIntensity = false;
            intensityText.Text = $"{intensitySlider.Value:0}%";
        }

        private void PaletteColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string value } && TrySetColor(value))
                Keyboard.ClearFocus();
        }

        private void ToneSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (updatingTone) return;
            SetSelectedColor(SpectrumColorAt(e.NewValue / 100d), synchronizeTone: false);
        }

        private void UpdateToneState()
        {
            if (toneSlider == null) return;
            (double hue, double saturation, double lightness) = RgbToHsl(SelectedColor);
            double position = lightness < 0.02 ? 0.96
                : saturation < 0.02 && lightness > 0.98 ? 0
                : 0.10 + hue * 0.80;
            updatingTone = true;
            toneSlider.Value = position * 100;
            updatingTone = false;
        }

        private bool TrySetColor(string? value, bool remember = false)
        {
            try
            {
                string normalized = (value ?? string.Empty).Trim();
                if (!normalized.StartsWith('#')) normalized = $"#{normalized}";
                if (normalized.Length != 7) return false;
                Color color = (Color)ColorConverter.ConvertFromString(normalized);
                SetSelectedColor(color);
                if (remember) RememberColor(color);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SetSelectedColor(Color color, bool synchronizeTone = true)
        {
            SelectedColor = color;
            if (SelectedTarget == ColorTarget.Titles) titlesColor=color;
            else if (SelectedTarget == ColorTarget.Labels) labelsColor=color;
            else if (SelectedTarget == ColorTarget.Clock) clockColor = color;
            else if (SelectedTarget == ColorTarget.SystemMonitor) systemMonitorColor = color;
            else if (SelectedTarget == ColorTarget.Base) baseColor = color;
            else if (SelectedTarget == ColorTarget.Text) textColor = color;
            else if (SelectedTarget == ColorTarget.Knob) knobColor=color;
            else if (SelectedTarget == ColorTarget.Power) powerColor=color;
            else if (SelectedTarget == ColorTarget.Music) musicColor = color;
            else systemColor = color;
            if (synchronizeTone) UpdateToneState();
            UpdatePreview();
            NotifyPreviewChanged();
        }

        private static (double Hue, double Saturation, double Lightness) RgbToHsl(Color color)
        {
            double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double lightness = (max + min) / 2;
            if (Math.Abs(max - min) < 0.000001) return (0, 0, lightness);
            double delta = max - min;
            double saturation = lightness > 0.5
                ? delta / (2 - max - min)
                : delta / (max + min);
            double hue = max == r
                ? (g - b) / delta + (g < b ? 6 : 0)
                : max == g ? (b - r) / delta + 2
                : (r - g) / delta + 4;
            return (hue / 6, saturation, lightness);
        }

        private static Color SpectrumColorAt(double position)
        {
            position = Math.Clamp(position, 0, 1);
            (double Offset, Color Color)[] stops =
            {
                (0.00, Colors.White), (0.08, Colors.White),
                (0.12, Color.FromRgb(255,48,48)), (0.18, Color.FromRgb(255,48,48)),
                (0.22, Color.FromRgb(255,152,0)), (0.28, Color.FromRgb(255,152,0)),
                (0.32, Color.FromRgb(255,255,50)), (0.38, Color.FromRgb(255,255,50)),
                (0.42, Color.FromRgb(50,215,95)), (0.48, Color.FromRgb(50,215,95)),
                (0.52, Color.FromRgb(32,215,229)), (0.58, Color.FromRgb(32,215,229)),
                (0.62, Color.FromRgb(49,140,231)), (0.68, Color.FromRgb(49,140,231)),
                (0.72, Color.FromRgb(118,80,216)), (0.78, Color.FromRgb(118,80,216)),
                (0.82, Color.FromRgb(213,42,167)), (0.88, Color.FromRgb(213,42,167)),
                (0.92, Colors.Black), (1.00, Colors.Black)
            };
            for (int index = 1; index < stops.Length; index++)
            {
                if (position > stops[index].Offset) continue;
                var left = stops[index - 1];
                var right = stops[index];
                double amount = (position - left.Offset) / (right.Offset - left.Offset);
                byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
                return Color.FromRgb(Mix(left.Color.R, right.Color.R),
                    Mix(left.Color.G, right.Color.G), Mix(left.Color.B, right.Color.B));
            }
            return Colors.Black;
        }

        private void UpdateTargetSwatches()
        {
            void Set(RadioButton radio,Color color,double level){if(radio!=null)radio.Background=new SolidColorBrush(Color.FromArgb((byte)Math.Round(Math.Clamp(level,0,100)*255/100),color.R,color.G,color.B));}
            Set(systemTargetRadio,systemColor,systemIntensity);Set(clockTargetRadio,clockColor,clockIntensity);Set(systemMonitorTargetRadio,systemMonitorColor,systemMonitorIntensity);Set(baseTargetRadio,baseColor,baseOpacity);Set(textTargetRadio,textColor,textIntensity);Set(titlesTargetRadio,titlesColor,titlesIntensity);Set(labelsTargetRadio,labelsColor,labelsIntensity);Set(musicTargetRadio,musicColor,musicIntensity);Set(knobTargetRadio,knobColor,knobIntensity);Set(powerTargetRadio,powerColor,powerIntensity);
        }
        private void ResetSelectedColor()
        {
            string value=ProductColorDefaults.Color(SelectedTarget);
            double level=ProductColorDefaults.Intensity(SelectedTarget);
            var lights=PlayerSkinLights.Defaults(CommonMetal.NoSkin?"none":MusicFinish.Name);
            if(SelectedTarget==ColorTarget.Music){value=lights.Buttons;level=lights.ButtonsIntensity;}
            else if(SelectedTarget==ColorTarget.Knob){value=lights.Knob??lights.Buttons;level=lights.KnobIntensity;}
            else if(SelectedTarget==ColorTarget.Power){value=lights.Power??MusicConsoleControl.DefaultPowerColor(MusicFinish.Name).ToString();level=lights.PowerIntensity;}
            SetSelectedColor((Color)ColorConverter.ConvertFromString(value));intensitySlider.Value=SelectedTarget==ColorTarget.Base||IsPlayerLight?level:100-level;UpdatePreview();
        }
        private void UpdatePreview()
        {
            double opacity = SelectedTarget switch
            {
                ColorTarget.Titles => titlesIntensity, ColorTarget.Labels => labelsIntensity,
                ColorTarget.Clock => clockIntensity,
                ColorTarget.SystemMonitor => systemMonitorIntensity,
                ColorTarget.Knob=>knobIntensity, ColorTarget.Power=>powerIntensity,
                ColorTarget.Music => musicIntensity,
                ColorTarget.Text => textIntensity,
                ColorTarget.Base => baseOpacity,
                _ => systemIntensity
            };
            preview.Background = new SolidColorBrush(Color.FromArgb(
                (byte)Math.Round(255 * Math.Clamp(opacity, 0, 100) / 100d),
                SelectedColor.R, SelectedColor.G, SelectedColor.B));
            hexText.Text = $"#{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";
            if(!editingPicker)refreshPicker?.Invoke(((SolidColorBrush)preview.Background).Color.ToString());
            UpdateTargetSwatches();
            UpdatePaletteSelection();
            if(historyReady&&!editingPicker)editHistory.Record(CaptureColors());
        }

        private void UpdatePaletteSelection()
        {
            if (paletteGrid == null) return;
            Button? closest = null;
            int closestDistance = int.MaxValue;
            foreach (object child in paletteGrid.Children)
            {
                if (child is not Button button) continue;
                try
                {
                    Color candidate = (Color)ColorConverter.ConvertFromString(button.Tag as string);
                    int red = candidate.R - SelectedColor.R;
                    int green = candidate.G - SelectedColor.G;
                    int blue = candidate.B - SelectedColor.B;
                    int distance = red * red + green * green + blue * blue;
                    if (distance < closestDistance) { closestDistance = distance; closest = button; }
                }
                catch { }
            }
            foreach (object child in paletteGrid.Children)
            {
                if (child is not Button button) continue;
                bool selected = closestDistance == 0 && ReferenceEquals(button, closest);
                bool saved = ReferenceEquals(button, savedColor1) || ReferenceEquals(button, savedColor2) ||
                             ReferenceEquals(button, savedColor3) || ReferenceEquals(button, savedColor4);
                button.Content = selected ? "✓" : null;
                button.FontSize = 27;
                button.FontWeight = FontWeights.Bold;
                button.BorderThickness = selected ? new Thickness(3) : new Thickness(saved ? 2 : 1);
                if (saved)
                    button.SetResourceReference(Button.BorderBrushProperty, "AccentBrush");
                else
                    button.BorderBrush = selected
                        ? new SolidColorBrush(SelectedColor.R + SelectedColor.G + SelectedColor.B > 430
                            ? Colors.Black : Colors.White)
                        : new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
                button.Foreground = new SolidColorBrush(
                    SelectedColor.R + SelectedColor.G + SelectedColor.B > 430 ? Colors.Black : Colors.White);
            }
        }

        public string[] SavedColors => savedColors
            .Select(color => $"#{color.R:X2}{color.G:X2}{color.B:X2}").ToArray();

        private void InitializeSavedColors(string[]? values)
        {
            string[] defaults = { "#263238", "#795548", "#607D8B", "#000000" };
            for (int index = 0; index < savedColors.Length; index++)
            {
                string value = values != null && index < values.Length ? values[index] : defaults[index];
                try { savedColors[index] = (Color)ColorConverter.ConvertFromString(value); }
                catch { savedColors[index] = (Color)ColorConverter.ConvertFromString(defaults[index]); }
            }
            UpdateSavedColorButtons();
        }

        private void RememberColor(Color color)
        {
            if (savedColors[^1] == color)
            {
                UpdateSavedColorButtons();
                UpdatePaletteSelection();
                return;
            }
            for (int index = 0; index < savedColors.Length - 1; index++)
                savedColors[index] = savedColors[index + 1];
            savedColors[^1] = color;
            UpdateSavedColorButtons();
            if(!editingPicker)refreshPicker?.Invoke(((SolidColorBrush)preview.Background).Color.ToString());
            UpdateTargetSwatches();
            UpdatePaletteSelection();
        }

        private void UpdateSavedColorButtons()
        {
            Button[] buttons = { savedColor1, savedColor2, savedColor3, savedColor4 };
            for (int index = 0; index < buttons.Length; index++)
            {
                Color color = savedColors[index];
                string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                buttons[index].Tag = hex;
                buttons[index].Background = new SolidColorBrush(color);
                buttons[index].ToolTip = hex;
            }
        }

        private void NotifyPreviewChanged()
        {
            double intensity = SelectedTarget switch
            {
                ColorTarget.Titles => titlesIntensity, ColorTarget.Labels => labelsIntensity,
                ColorTarget.Clock => clockIntensity,
                ColorTarget.SystemMonitor => systemMonitorIntensity,
                ColorTarget.Knob=>knobIntensity, ColorTarget.Power=>powerIntensity,
                ColorTarget.Music => musicIntensity,
                ColorTarget.Text => textIntensity,
                ColorTarget.Base => baseOpacity,
                _ => systemIntensity
            };
            PreviewChanged?.Invoke(SelectedTarget, SelectedColor, intensity);
        }

        private void HexText_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!TrySetColor(hexText.Text, remember: true)) UpdatePreview();
        }

        private void HexText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (!TrySetColor(hexText.Text, remember: true)) UpdatePreview();
            Keyboard.ClearFocus();
            e.Handled = true;
        }

        private void SampleColorButton_Click(object sender, RoutedEventArgs e)
        {
            EndScreenColorSampling();
            colorSampler = new ScreenColorSamplerWindow();
            colorSampler.Owner = this;
            colorSampler.ColorSampled += ColorSampler_ColorSampled;
            colorSampler.SamplingEnded += ColorSampler_SamplingEnded;
            sampleColorButton.ToolTip = "Left-click samples repeatedly; right-click or Esc closes";
            colorSampler.Show();
            colorSampler.Activate();
            e.Handled = true;
        }

        private void ColorSampler_ColorSampled(Color color)
        {
            SetSelectedColor(color);
            RememberColor(color);
        }

        private void ColorSampler_SamplingEnded() => EndScreenColorSampling();

        private void EndScreenColorSampling()
        {
            ScreenColorSamplerWindow? sampler = colorSampler;
            colorSampler = null;
            if (sampler != null)
            {
                sampler.ColorSampled -= ColorSampler_ColorSampled;
                sampler.SamplingEnded -= ColorSampler_SamplingEnded;
                sampler.Close();
            }
            if (sampleColorButton != null)
                sampleColorButton.ToolTip = "Pick a color from the screen";
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
        private void DialogSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source) return;
            for (DependencyObject? current = source; current != null;
                 current = VisualTreeHelper.GetParent(current))
            {
                if (current is ButtonBase or Slider or TextBox or ColorPlane) return;
                if (ReferenceEquals(current, this)) break;
            }
            e.Handled = true;
            DragMove();
        }
        private void Cancel_Click(object sender, RoutedEventArgs e) { EndScreenColorSampling(); DialogResult = false; Close(); }
        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (!TrySetColor(hexText.Text)) UpdatePreview();
            RecentColors.Save(new[]{((SolidColorBrush)preview.Background).Color.ToString()});
            DialogResult = true;
            Close();
        }

    }
}
