using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Ekrano_valdymas;

// Clock glyphs enter the exact same prepared-pixel pipeline as icons.
internal static class SkinTextMaterial
{
    private static readonly Dictionary<string,BitmapSource> materials=new();
    internal static int MaterialComputations {get;private set;}
    internal static double MaterialMilliseconds {get;private set;}
    internal static long MaterialBytes {get;private set;}
    internal static void Draw(DrawingContext dc,FrameworkElement owner,Geometry glyph,string style)
    {
        var bounds=glyph.Bounds;double sampling=LightMetalBevel.Sampling(owner);
        string shape=PathGeometry.CreateFromGeometry(glyph).ToString(System.Globalization.CultureInfo.InvariantCulture);
        string key=$"{style}|{CommonMetal.FinishName}|{CommonMetal.NoSkin}|{CommonMetal.Viewport(owner)}|{sampling}|{shape}";
        bool relief=style is "metal" or "inlay";
        double side=Math.Max(bounds.Width,bounds.Height);
        if(!materials.TryGetValue(key,out var result))
        {
            var timer=System.Diagnostics.Stopwatch.StartNew();
            var visual=new DrawingVisual();using(var draw=visual.RenderOpen()){
                draw.PushTransform(new TranslateTransform(-bounds.X,-bounds.Y));draw.DrawGeometry(Brushes.White,null,glyph);draw.Pop();
            }
            var mask=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(bounds.Width*sampling)),Math.Max(1,(int)Math.Ceiling(bounds.Height*sampling)),96*sampling,96*sampling,PixelFormats.Pbgra32);mask.Render(visual);mask.Freeze();
            if(relief){
                // Proxy has the exact ink rectangle; the square renderer's unused area stays transparent.
                var proxy=new FrameworkElement{Width=bounds.Width,Height=bounds.Height};proxy.Measure(new Size(bounds.Width,bounds.Height));proxy.Arrange(new Rect(0,0,bounds.Width,bounds.Height));
                var material=CommonMetal.ForBevel(owner).Clone();material.Transform=new TranslateTransform(-bounds.X,-bounds.Y);
                result=InstalledIconMaterials.Render(proxy,mask,false,style,sampling,material,glyph:true);
            }else result=IconAppearance.Apply(mask,CommonMetal.IsLight,useIconRelief:false,style:style);
            long bytes=(long)result.PixelWidth*result.PixelHeight*4;
            if(MaterialBytes+bytes>16*1024*1024||materials.Count>=16){materials.Clear();MaterialBytes=0;}
            if(bytes<=16*1024*1024){materials[key]=result;MaterialBytes+=bytes;}
            MaterialComputations++;MaterialMilliseconds+=timer.Elapsed.TotalMilliseconds;
        }
        dc.DrawImage(result,relief?new Rect(bounds.X,bounds.Y,side,side):bounds);
    }
    private static readonly Dictionary<string,BitmapSource> cache=new();
    internal static BitmapSource Image(Geometry glyph,bool light,double dpi)
    {
        // GeometryGroup.ToString() returns only its type, not the changing clock digits.
        string shape=PathGeometry.CreateFromGeometry(glyph).ToString(System.Globalization.CultureInfo.InvariantCulture);
        string key=$"{light}|{IconAppearance.Style}|{dpi}|{shape}";
        if(cache.TryGetValue(key,out var found))return found;
        var bounds=glyph.Bounds;double scale=Math.Max(1,dpi);
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(-bounds.X,-bounds.Y));
            dc.DrawGeometry(Brushes.White,null,glyph);dc.Pop();
        }
        var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(bounds.Width*scale)),Math.Max(1,(int)Math.Ceiling(bounds.Height*scale)),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();
        var result=IconAppearance.Apply(bitmap,light,useIconRelief:false);
        if(cache.Count>=16)cache.Clear();cache[key]=result;return result;
    }
}


