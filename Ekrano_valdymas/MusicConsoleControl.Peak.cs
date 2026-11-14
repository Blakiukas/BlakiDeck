using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Ekrano_valdymas;
internal sealed partial class MusicConsoleControl
{
    private readonly Ellipse?[] peakLights=new Ellipse?[2];
    private readonly double[] peakUntil=new double[2];
    private static readonly double PeakAngle=ClassicVuMeter.Angle(Math.Pow(10,1d/20));
    private bool PeakHolding=>peakUntil.Any(t=>t>clock.Elapsed.TotalSeconds);
    internal bool[] PeakStates=>peakLights.Select(l=>l?.Opacity>0).ToArray();
    private void AddPeakIndicator(int channel,double cx,double cy)
    {
        var lens=new Ellipse{Tag="PeakLens",IsHitTestVisible=false,
            Fill=new RadialGradientBrush{GradientOrigin=new Point(.32,.25),GradientStops=new GradientStopCollection{new(Color.FromRgb(47,3,2),0),new(Color.FromRgb(29,1,0),.65),new(Color.FromRgb(17,0,0),1)}}};
        Put(lens,cx-5.5,cy-5.5,11,11);
        var lit=new Ellipse{IsHitTestVisible=false,Opacity=0,Fill=new RadialGradientBrush{GradientOrigin=new Point(.35,.3),GradientStops=new GradientStopCollection{new(Color.FromRgb(255,75,35),0),new(Color.FromRgb(220,12,3),.45),new(Color.FromRgb(104,2,0),1)}}};
        Put(lit,cx-5.5,cy-5.5,11,11);peakLights[channel]=lit;
    }
    private void RefreshPeakIndicators()
    {
        double now=clock.Elapsed.TotalSeconds;
        for(int i=0;i<2;i++)
        {
            if(!powerOn)peakUntil[i]=0;
            else if(needles[i].Angle>PeakAngle)peakUntil[i]=now+.220;
            if(peakLights[i] is {} light)light.Opacity=powerOn&&now<peakUntil[i]?1:0;
        }
    }
}

