namespace Ekrano_valdymas;

// Absolute position from one gesture origin; asynchronous source updates cannot
// feed back into the next target. The first 400 ms distinguish a click from a hold.
internal sealed class HeldSeek
{
    private double started,origin,duration;
    private int direction;
    internal void Begin(double now,double position,double length,int sign)
    {started=now;origin=position;duration=Math.Max(0,length);direction=sign;}
    internal double Position(double now)=>Math.Clamp(origin+direction*10*Math.Max(0,now-started-.4),0,duration);
}
