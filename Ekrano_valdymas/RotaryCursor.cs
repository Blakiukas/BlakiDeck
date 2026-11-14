using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ekrano_valdymas;
internal static class RotaryCursor
{
    internal static Cursor Value { get; } = Create();
    private static Cursor Create()
    {
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())
        {
            // Two circular arcs with tangent arrowheads; no stretched SVG arc.
            foreach(double start in new[]{-85d,95d})
            {
                Point P(double degrees){double a=degrees*Math.PI/180;return new Point(16+9*Math.Cos(a),16+9*Math.Sin(a));}
                var arc=new StreamGeometry();using(var g=arc.Open()){g.BeginFigure(P(start),false,false);for(int i=1;i<=28;i++)g.LineTo(P(start+i*5),true,false);}
                dc.DrawGeometry(null,new Pen(Brushes.Black,4){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},arc);
                dc.DrawGeometry(null,new Pen(Brushes.White,1.6){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round},arc);
                double end=(start+140)*Math.PI/180;var tangent=new Vector(-Math.Sin(end),Math.Cos(end));var normal=new Vector(Math.Cos(end),Math.Sin(end));
                Point tip=P(start+140)+tangent*2,back=tip-tangent*5;
                var arrow=new StreamGeometry();using(var g=arrow.Open()){g.BeginFigure(tip,true,true);g.LineTo(back+normal*3,true,false);g.LineTo(back-normal*3,true,false);}
                dc.DrawGeometry(Brushes.White,new Pen(Brushes.Black,1),arrow);
            }
        }
        var bitmap=new RenderTargetBitmap(32,32,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png=new MemoryStream();encoder.Save(png);
        using var cur=new MemoryStream();using(var writer=new BinaryWriter(cur,System.Text.Encoding.UTF8,true))
        {
            writer.Write((ushort)0);writer.Write((ushort)2);writer.Write((ushort)1);
            writer.Write((byte)32);writer.Write((byte)32);writer.Write((ushort)0);
            writer.Write((ushort)16);writer.Write((ushort)16);writer.Write((uint)png.Length);writer.Write((uint)22);writer.Write(png.ToArray());
        }
        cur.Position=0;return new Cursor(cur);
    }
}
