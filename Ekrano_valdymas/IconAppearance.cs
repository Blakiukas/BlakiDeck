using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows;
namespace Ekrano_valdymas;

// Prepared bitmap variants only: no animation, shader, timer or disk exports.
internal static class IconAppearance
{
    internal static string Style { get; private set; } = "original";
    internal static int Revision { get; private set; }
    internal static event Action? Changed;
    private sealed class Identity { internal readonly int Id=++nextId; }
    private static int nextId;
    private static readonly ConditionalWeakTable<BitmapSource,Identity> identities=new();
    private static readonly Dictionary<(int,string,bool,bool),BitmapSource> cache=new();
    private static readonly Queue<(int,string,bool,bool)> order=new();
    private static readonly Dictionary<bool,byte[]> grains=new();
    internal static long CacheBytes {get;private set;}
    internal static int Computations {get;private set;}
    internal static double ProcessingMilliseconds {get;private set;}
    // User's 2026-10-04 player-display samples and DARK BlakiDeck lettering.
    // Keep the subtle RGB tint; these are not neutral black/white endpoints.
    internal static Color DarkTone(bool light)=>light?Color.FromRgb(5,6,6):Color.FromRgb(6,7,7);
    internal static Color LightTone(bool light)=>light?Color.FromRgb(203,203,203):Color.FromRgb(180,180,178);
    internal static string Normalize(string? value)=>value=="metal-c"?"metal":value is "mono" or "metal" or "inlay"?value:"original";
    internal static void SetStyle(string? value)
    {
        value=Normalize(value);
        if(Style==value)return;Style=value;Refresh();
    }
    internal static void Refresh(){Revision++;Changed?.Invoke();}
    internal static BitmapSource Apply(BitmapSource source,bool light,bool useIconRelief=true,string? style=null)
    {
        style??=Style;
        if(style=="original")return source;
        var key=(identities.GetValue(source,_=>new()).Id,style,light,useIconRelief);
        if(cache.TryGetValue(key,out var found))return found;
        var watch=Stopwatch.StartNew();
        var input=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);
        int w=input.PixelWidth,h=input.PixelHeight,stride=w*4;
        var pixels=new byte[stride*h];input.CopyPixels(pixels,stride,0);
        var original=(byte[])pixels.Clone();var grain=style=="metal"?Grain(light):null;
        double L(int x,int y){int i=(Math.Clamp(y,0,h-1)*w+Math.Clamp(x,0,w-1))*4;return (.2126*original[i+2]+.7152*original[i+1]+.0722*original[i])*original[i+3]/255;}
        if(light&&style=="metal"&&useIconRelief) LightIconRelief.Render(pixels,w,h,grain!);
        else for(int y=0;y<h;y++)for(int x=0;x<w;x++)
        {
            int i=y*stride+x*4;if(pixels[i+3]==0)continue;
            double luminance=.2126*original[i+2]+.7152*original[i+1]+.0722*original[i];
            // Saturated red/blue marks must not disappear just because their
            // luminance is lower than green. Retain detail with a value/luma mix.
            double l=.35*luminance+.65*Math.Max(original[i],Math.Max(original[i+1],original[i+2]));
            // Keep internal tonal structure, not just the outer alpha silhouette.
            double monoContrast=Math.Clamp((l-128)*1.5+128,0,255);
            double tonePosition=monoContrast/255;
            if(grain!=null)
            {
                // Satin metal: broad, restrained reflection and upper-left relief,
                // using the same brushed grain as the current chassis.
                double contrast=Math.Clamp((l-128)*1.3+128,0,255);
                tonePosition=contrast/255;
                double reflection=1-(x/(double)Math.Max(1,w-1)+y/(double)Math.Max(1,h-1));
                double relief=reflection*(light?9:14);
                relief+=(grain[(y%128)*128+x%128]-128)*.40;
                int edge=Math.Max(1,(int)Math.Round(Math.Min(w,h)/64d));
                relief+=Math.Clamp((L(x+edge,y+edge)-L(x-edge,y-edge))*.22,-25,25);
                // Texture belongs to midtones. Black/white anchors must keep
                // the sampled player colors, even at relief highlights/edges.
                tonePosition+=relief/255*(4*tonePosition*(1-tonePosition));
            }
            tonePosition=Math.Clamp(tonePosition,0,1);
            var dark=DarkTone(light);var bright=LightTone(light);
            byte Mix(byte a,byte b)=>(byte)Math.Round(a+(b-a)*tonePosition);
            pixels[i]=Mix(dark.B,bright.B);pixels[i+1]=Mix(dark.G,bright.G);pixels[i+2]=Mix(dark.R,bright.R);
        }
        var result=BitmapSource.Create(w,h,source.DpiX,source.DpiY,PixelFormats.Bgra32,null,pixels,stride);result.Freeze();
        long bytes=(long)stride*h;
        while(order.Count>0&&(CacheBytes+bytes>16*1024*1024||cache.Count>=128)){var old=order.Dequeue();if(cache.Remove(old,out var bitmap))CacheBytes-=(long)bitmap.PixelWidth*bitmap.PixelHeight*4;}
        if(bytes<=16*1024*1024){cache[key]=result;order.Enqueue(key);CacheBytes+=bytes;}
        IconBitmap.RegisterDerived(result,source);Computations++;ProcessingMilliseconds+=watch.Elapsed.TotalMilliseconds;return result;
    }
    private static byte[] Grain(bool light)
    {
        if(grains.TryGetValue(light,out var found))return found;
        var source=MusicConsoleControl.Bitmap("shared-material.png",finish:light?"light":"dark");
        var crop=new CroppedBitmap(source,new Int32Rect(Math.Min(900,source.PixelWidth-128),Math.Min(100,source.PixelHeight-128),128,128));
        var image=new FormatConvertedBitmap(crop,PixelFormats.Bgra32,null,0);var rgba=new byte[128*128*4];image.CopyPixels(rgba,512,0);
        var values=new double[128*128];double mean=0;
        for(int i=0;i<values.Length;i++){values[i]=.2126*rgba[i*4+2]+.7152*rgba[i*4+1]+.0722*rgba[i*4];mean+=values[i];}mean/=values.Length;
        double variance=values.Sum(x=>(x-mean)*(x-mean))/values.Length,scale=20/Math.Max(1,Math.Sqrt(variance));
        var result=values.Select(x=>(byte)Math.Clamp(Math.Round(128+(x-mean)*scale),0,255)).ToArray();grains[light]=result;return result;
    }
}
