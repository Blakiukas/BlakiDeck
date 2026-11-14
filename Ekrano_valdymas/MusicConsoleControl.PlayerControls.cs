using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Ekrano_valdymas;

internal sealed partial class MusicConsoleControl
{
    private double vuSensitivity = DefaultVuSensitivity;
    private bool powerOn, powerBusy;
    private ClassicVuMeter.State powerOffState;
    private long powerOffTimestamp;
    private readonly List<RotateTransform> sensitivityRotations = new();
    private Border powerShade = null!;
    private Image powerCap = null!;
    private Button powerButton = null!;
    internal double VuSensitivity => vuSensitivity;
    internal bool PowerOn => powerOn;
    // Keep the original calibration divisor. Old max (4 / 2) is the new base.
    // Old range was two octaves (1..4); new range is three, centred on 4.
    internal const double VuSensitivityCalibration=2;
    internal const double DefaultVuSensitivity=4, MinimumVuSensitivity=1.4142135623730951, MaximumVuSensitivity=11.313708498984761;
    private sealed record SensitivitySetting(int Version,double Value);
    private static double SensitivityAngle(double gain)=>Math.Log2(gain/DefaultVuSensitivity)*90;
    private static string SensitivityPath => System.IO.Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas","vu-sensitivity.json");

    internal void SetVuSensitivity(double value, bool save = false)
    {
        if(save&&!powerOn)return;
        vuSensitivity = Math.Clamp(double.IsFinite(value) ? value : DefaultVuSensitivity, MinimumVuSensitivity, MaximumVuSensitivity);
        foreach(var rotation in sensitivityRotations) rotation.Angle = SensitivityAngle(vuSensitivity);
        if(save && !Diagnostic && !UsesTabSettings) AtomicJson.Write(SensitivityPath,new SensitivitySetting(2,vuSensitivity));
        if(save)SettingsChanged?.Invoke(this,EventArgs.Empty);
    }

    private void AddPlayerControls()
    {
        // Legacy scalar settings belong to the old range. Start at the newly
        // requested base; preserve the file until the user changes a screw.
        try
        {
            if(File.Exists(SensitivityPath))
            {
                using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(SensitivityPath));
                if(json.RootElement.ValueKind==System.Text.Json.JsonValueKind.Object&&
                   System.Text.Json.JsonSerializer.Deserialize<SensitivitySetting>(json.RootElement) is {Version:2} saved)SetVuSensitivity(saved.Value);
            }
        }
        catch { }
        // Centre between the physical lower meter frame (338) and housing (384).
        for(int i=0;i<2;i++) AddSensitivityScrew(1094+i*399,(338+384)/2d);
        // Block powered-off controls underneath, but keep this plain panel
        // eligible for the host's mouse and touch window-drag handlers.
        powerShade=new Border{Background=Brushes.Transparent,IsHitTestVisible=false,CornerRadius=new CornerRadius(24)};
        powerShade.Width=1900;powerShade.Height=426;Surface.Children.Add(powerShade);
        powerButton=new Button{Cursor=Cursors.Hand,Tag="NoWindowDrag",ToolTip="Power — pause and dim",Background=Brushes.Transparent,FocusVisualStyle=null};
        // A ContentPresenter containing a non-hit-testable image has no input
        // surface. Keep a transparent, full-size border around the artwork.
        var border=new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty,Brushes.Transparent);
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        powerButton.Template=new ControlTemplate(typeof(Button)){VisualTree=border};
        powerCap=Image("power-push-on.png");
        var powerLayers=new Grid();powerLayers.Children.Add(powerCap);powerLightLayer=new System.Windows.Shapes.Rectangle{Fill=powerLightBrush,IsHitTestVisible=false};powerLayers.Children.Add(powerLightLayer);powerButton.Content=powerLayers;Put(powerButton,1739,308,100,90);
        var powerLabel=new Image{Source=WideBitmap("power-label.png",instanceFinish),Stretch=Stretch.Fill,IsHitTestVisible=false};Put(powerLabel,1749,293,80,26);
        var brand=new Image{Source=WideBitmap("power-brand.png",instanceFinish),Stretch=Stretch.Fill,IsHitTestVisible=false};Put(brand,1700,40,190,100);
        System.Windows.Automation.AutomationProperties.SetName(powerButton,"Player power");
        long suppressClickUntil=0;
        powerButton.Click+=async(_,_)=>{if(Environment.TickCount64>=suppressClickUntil)await SetPowerAsync(!powerOn);};
        // Both finishes use a pushbutton: release toggles once, including touch.
        bool mouse=false;TouchDevice? touch=null;
        powerButton.PreviewMouseLeftButtonDown+=(_,e)=>{if(e.StylusDevice!=null||touch!=null||Environment.TickCount64<suppressClickUntil){e.Handled=true;return;}mouse=true;powerButton.Focus();powerButton.CaptureMouse();e.Handled=true;};
        powerButton.PreviewMouseLeftButtonUp+=async(_,e)=>{if(!mouse)return;bool enabled=!powerOn;mouse=false;powerButton.ReleaseMouseCapture();e.Handled=true;await SetPowerAsync(enabled);};
        powerButton.LostMouseCapture+=(_,_)=>mouse=false;
        powerButton.PreviewTouchDown+=(_,e)=>{e.Handled=true;if(touch==null&&!mouse){suppressClickUntil=Environment.TickCount64+750;touch=e.TouchDevice;powerButton.CaptureTouch(touch);}};
        powerButton.PreviewTouchMove+=(_,e)=>{if(touch==e.TouchDevice)e.Handled=true;};
        powerButton.PreviewTouchUp+=async(_,e)=>{e.Handled=true;if(touch!=e.TouchDevice)return;bool enabled=!powerOn;touch=null;suppressClickUntil=Environment.TickCount64+750;powerButton.ReleaseTouchCapture(e.TouchDevice);await SetPowerAsync(enabled);};
        powerButton.LostTouchCapture+=(_,e)=>{if(touch==e.TouchDevice)touch=null;};
        powerButton.PreviewKeyDown+=async(_,e)=>{if(e.Key is Key.Up or Key.Down){e.Handled=true;await SetPowerAsync(e.Key==Key.Up);}};
        UpdatePowerVisual();
    }

    private void AddSensitivityScrew(double cx,double cy)
    {
        var knob=new Grid{Background=Brushes.Transparent,Cursor=RotaryCursor.Value,Tag="NoWindowDrag",Focusable=true,ToolTip=HardwareTip("VU sensitivity · drag / wheel · double-click to reset")};
        var head=Image("screw-head.png");head.Width=head.Height=38;head.RenderTransformOrigin=new Point(.5,.5);
        var rotation=new RotateTransform(SensitivityAngle(vuSensitivity));sensitivityRotations.Add(rotation);head.RenderTransform=rotation;
        knob.Children.Add(head);Put(knob,cx-24,cy-24,48,48);
        System.Windows.Automation.AutomationProperties.SetName(knob,"VU sensitivity");
        var feedback=new VuAdjustmentFeedback(knob,()=>vuSensitivity);
        double start=0,value=DefaultVuSensitivity;bool mouse=false;TouchDevice? touch=null;
        void Begin(double y){start=y;value=vuSensitivity;knob.Focus();feedback.Begin();}
        // Equal travel means equal dB change; neutral is the former maximum.
        void Move(double y){if(powerOn)SetVuSensitivity(value*Math.Pow(2,(start-y)/60));feedback.Show();}
        void Save(){SetVuSensitivity(vuSensitivity,true);feedback.End();}
        knob.MouseLeftButtonDown+=(_,e)=>{if(e.StylusDevice!=null)return;if(e.ClickCount==2){SetVuSensitivity(DefaultVuSensitivity,true);feedback.Show();e.Handled=true;return;}mouse=true;Begin(e.GetPosition(Surface).Y);knob.CaptureMouse();e.Handled=true;};
        knob.MouseMove+=(_,e)=>{if(mouse)Move(e.GetPosition(Surface).Y);};
        knob.MouseLeftButtonUp+=(_,e)=>{if(!mouse)return;mouse=false;knob.ReleaseMouseCapture();Save();e.Handled=true;};
        knob.LostMouseCapture+=(_,_)=>{if(mouse){mouse=false;Save();}};
        knob.TouchDown+=(_,e)=>{if(touch==null&&!mouse){touch=e.TouchDevice;Begin(e.GetTouchPoint(Surface).Position.Y);knob.CaptureTouch(touch);}e.Handled=true;};
        knob.TouchMove+=(_,e)=>{if(touch==e.TouchDevice)Move(e.GetTouchPoint(Surface).Position.Y);e.Handled=true;};
        knob.TouchUp+=(_,e)=>{if(touch==e.TouchDevice){touch=null;knob.ReleaseTouchCapture(e.TouchDevice);Save();}e.Handled=true;};
        knob.LostTouchCapture+=(_,e)=>{if(touch==e.TouchDevice){touch=null;Save();}};
        knob.MouseWheel+=(_,e)=>{SetVuSensitivity(vuSensitivity*Math.Pow(2,Math.Sign(e.Delta)/20d),true);feedback.Show();e.Handled=true;};
        knob.KeyDown+=(_,e)=>{if(e.Key is Key.Left or Key.Down or Key.Right or Key.Up){SetVuSensitivity(vuSensitivity*Math.Pow(2,(e.Key is Key.Left or Key.Down?-1:1)/20d),true);feedback.Show();e.Handled=true;}else if(e.Key==Key.Home){SetVuSensitivity(DefaultVuSensitivity,true);feedback.Show();e.Handled=true;}};
    }

    internal async Task SetPowerAsync(bool enabled)
    {
        if(powerBusy||enabled==powerOn)return;
        powerBusy=true;powerButton.IsEnabled=false;
        try
        {
            if(!enabled)
            {
                bool accepted=await (Diagnostic ? DiagnosticCommand?.Invoke("pause")??Task.FromResult(true) : player.ConsoleCommandAsync("pause")).WaitAsync(TimeSpan.FromSeconds(5));
                if(!accepted && PlaybackPlaying) throw new InvalidOperationException("The music source did not accept pause.");
            }
            if(!enabled)
            {
                powerOffState=audio.CaptureClassicVuState();
                powerOffTimestamp=System.Diagnostics.Stopwatch.GetTimestamp();
            }
            BeginScreenPower(enabled);powerOn=enabled;UpdateLights(PlaybackPlaying);UpdatePowerVisual();UpdateAudioActivity();UpdateAnimationActivity();SettingsChanged?.Invoke(this,EventArgs.Empty);
        }
        catch(Exception ex){InteractionDiagnostics.RecordFailure("Player power",ex);powerButton.ToolTip=HardwareTip("Could not pause the music source. Try again.");}
        finally{powerBusy=false;powerButton.IsEnabled=true;}
    }

    private static ToolTip HardwareTip(string text)=>new(){Content=text,Background=new SolidColorBrush(Color.FromRgb(23,23,23)),Foreground=new SolidColorBrush(Color.FromRgb(185,175,157)),BorderBrush=new SolidColorBrush(Color.FromRgb(65,65,65)),Padding=new Thickness(10,7,10,7),FontSize=12};

    private void UpdatePowerVisual()
    {
        powerShade.IsHitTestVisible=!powerOn;
        RefreshPowerLight();
        UpdateLights(PlaybackPlaying);
        powerButton.ToolTip=HardwareTip(powerOn?"ON · press to switch off":"OFF · press to switch on");
    }
}



