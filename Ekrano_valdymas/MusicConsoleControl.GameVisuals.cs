using System.Windows;
using System.Windows.Media.Animation;

namespace Ekrano_valdymas;
internal sealed partial class MusicConsoleControl
{
    private readonly OpticallyCenteredText gameModeText;
    private readonly System.Windows.Controls.Border gameModeFrame;
    private bool gameVisualActive,gamePulseRunning;
    private ClassicVuMeter.State gameReleaseState;
    private double gameVisualStart=-10,gameVoltageFrom=1;
    private const double GameReleaseSlowdown=3,GameReleaseSeconds=6;
    private const double VuUnlitShade=.88;
    private bool GameNeedlesSettling=>gameVisualActive&&clock.Elapsed.TotalSeconds-gameVisualStart<GameReleaseSeconds;
    private bool GameShadeTransition=>clock.Elapsed.TotalSeconds-gameVisualStart<(gameVisualActive?2:.6);
    // A normalized incandescent-lamp approximation: light falls much faster
    // than voltage. This is a visual response, not a measured electrical model.
    private double GameLampVoltage
    {
        get{double t=Math.Clamp((clock.Elapsed.TotalSeconds-gameVisualStart)/(gameVisualActive?2:.6),0,1);t=t*t*(3-2*t);return gameVoltageFrom+((gameVisualActive?.36:1)-gameVoltageFrom)*t;}
    }
    // Keep the unlit dial's ambient reflection. Only the lamp contribution
    // fades, so Game can never black out the dial beyond its power-off state.
    private double GameVuShade=>VuUnlitShade*(1-Math.Pow(GameLampVoltage,3.4));
    private double GameVuWarmth=>.25*(1-GameLampVoltage);
    private void UpdateGameVisualState()
    {
        if(gameVisualActive==ResourcePolicy.IsGame)return;
        gameVoltageFrom=GameLampVoltage;gameVisualStart=clock.Elapsed.TotalSeconds;
        gameVisualActive=ResourcePolicy.IsGame;
        if(gameVisualActive)
        {
            // Preserve the actual displayed deflection on entry (also when power
            // is already off). Release the moving coil without further audio input.
            double gain=vuSensitivity/VuSensitivityCalibration;
            double Amplitude(double angle)=>Math.Max(0,angle+50)/100*Math.Pow(10,3d/20)/Math.Max(gain,.000001);
            gameReleaseState=new(Amplitude(needles[0].Angle),Amplitude(needles[1].Angle),0,0);
        }
    }
    private void AdvanceGameNeedles()
    {
        var level=GameNeedlesSettling?gameReleaseState.AfterSilence((clock.Elapsed.TotalSeconds-gameVisualStart)/GameReleaseSlowdown):(0d,0d);
        SetNeedles(level.Item1,level.Item2);
    }
    private void UpdateGamePulse(bool active)
    {
        if(!active){StopGamePulse();return;}
        if(gamePulseRunning)return;
        gameModeText.Visibility=gameModeFrame.Visibility=Visibility.Visible;gamePulseRunning=true;
        var pulse=new DoubleAnimation(.35,.9,TimeSpan.FromSeconds(2.5)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase{EasingMode=EasingMode.EaseInOut}};
        Timeline.SetDesiredFrameRate(pulse,20);
        gameModeText.BeginAnimation(OpacityProperty,pulse);
    }
    private void StopGamePulse()
    {
        if(gamePulseRunning)gameModeText.BeginAnimation(OpacityProperty,null);
        gamePulseRunning=false;gameModeText.Visibility=gameModeFrame.Visibility=Visibility.Collapsed;
    }
}
