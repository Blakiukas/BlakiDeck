using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Ekrano_valdymas;
internal static class IconBitmap
{
    private sealed record Origin(string Path);
    private static readonly ConditionalWeakTable<ImageSource,Origin> origins=new();
    private static readonly ConditionalWeakTable<BitmapSource,BitmapSource> dimmed=new();
    internal static BitmapSource Dim(BitmapSource source)
    {
        return dimmed.GetValue(source,original=>{
            var converted=new FormatConvertedBitmap(original,PixelFormats.Bgra32,null,0);
            int stride=converted.PixelWidth*4;var pixels=new byte[stride*converted.PixelHeight];converted.CopyPixels(pixels,stride,0);
            for(int i=0;i<pixels.Length;i+=4)for(int c=0;c<3;c++)pixels[i+c]=(byte)Math.Round(pixels[i+c]*.52);
            var result=BitmapSource.Create(converted.PixelWidth,converted.PixelHeight,original.DpiX,original.DpiY,PixelFormats.Bgra32,null,pixels,stride);result.Freeze();
            if(PathFor(original) is string path)origins.Add(result,new(path));return result;
        });
    }
    private static readonly Dictionary<string,BitmapSource> cache=new();
    private static readonly Queue<string> order=new();
    private static long bytes;
    private static readonly object cacheLock=new();
    internal static string? PathFor(ImageSource? source)=>source!=null&&origins.TryGetValue(source,out var o)?o.Path:null;
    internal static void RegisterDerived(ImageSource result,ImageSource original){if(PathFor(original) is string path)origins.GetValue(result,_=>new(path));}
    internal static BitmapSource? Load(string? path,int pixels=128)
    {
        using var callbackTiming = new UiCallbackTiming("IconBitmap.Load");
        path=PackagedIconPath.Resolve(path);
        if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))return null;
        try {
            path=Path.GetFullPath(path);pixels=Math.Clamp(((pixels+63)/64)*64,64,2048);
            string key=path+"|"+File.GetLastWriteTimeUtc(path).Ticks+"|"+pixels;
            lock(cacheLock)if(cache.TryGetValue(key,out var found))return found;
            if(Path.GetExtension(path).Equals(".ico",StringComparison.OrdinalIgnoreCase))
            {
                // ICO files often begin with a16px frame. Select a real frame
                // large enough for the requested display instead of scaling it.
                using var iconStream=File.OpenRead(path);
                var frames=BitmapDecoder.Create(iconStream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames;
                var frame=frames.OrderBy(f=>Math.Max(f.PixelWidth,f.PixelHeight)>=pixels?0:1)
                    .ThenBy(f=>Math.Max(f.PixelWidth,f.PixelHeight)>=pixels?Math.Max(f.PixelWidth,f.PixelHeight):-Math.Max(f.PixelWidth,f.PixelHeight))
                    .ThenByDescending(f=>f.Format.BitsPerPixel).First();
                frame.Freeze();origins.Add(frame,new(path));return Remember(key,frame);
            }
            int width,height;
            using(var stream=File.OpenRead(path)) {
                var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);
                width=decoder.Frames[0].PixelWidth;height=decoder.Frames[0].PixelHeight;
            }
            var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
            bitmap.UriSource=new Uri(path);bitmap.CreateOptions=BitmapCreateOptions.IgnoreImageCache;
            if(Math.Max(width,height)>pixels){if(width>=height)bitmap.DecodePixelWidth=pixels;else bitmap.DecodePixelHeight=pixels;}
            bitmap.EndInit();bitmap.Freeze();origins.Add(bitmap,new(path));
            return Remember(key,bitmap);
        }catch{return null;}
    }
    private static BitmapSource Remember(string key,BitmapSource bitmap)
    {
            long size=(long)bitmap.PixelWidth*bitmap.PixelHeight*4;
            lock(cacheLock) {
            if(cache.TryGetValue(key,out var existing))return existing;
            while(order.Count>0&&(bytes+size>32*1024*1024||cache.Count>=128)){
                var old=order.Dequeue();if(cache.Remove(old,out var removed))bytes-=(long)removed.PixelWidth*removed.PixelHeight*4;
            }
            cache[key]=bitmap;order.Enqueue(key);bytes+=size;return bitmap;
            }
    }
}
// Keeps normal Source bindings and layout; only file-backed user icons opt into decoding.
public sealed class AdaptiveIconImage:Image
{
    public static readonly DependencyProperty SkinIconProperty=DependencyProperty.Register(nameof(SkinIcon),typeof(bool),typeof(AdaptiveIconImage),new PropertyMetadata(false,(d,_)=>((AdaptiveIconImage)d).AppearanceChanged()));
    public bool SkinIcon {get=>(bool)GetValue(SkinIconProperty);set=>SetValue(SkinIconProperty,value);}
    public static readonly DependencyProperty ShortcutTargetProperty=DependencyProperty.Register(nameof(ShortcutTarget),typeof(string),typeof(AdaptiveIconImage),new PropertyMetadata(null,(d,_)=>((AdaptiveIconImage)d).AppearanceChanged()));
    public string? ShortcutTarget {get=>(string?)GetValue(ShortcutTargetProperty);set=>SetValue(ShortcutTargetProperty,value);}
    private bool FollowsHostStyle => SkinIcon && !ShortcutIconEffect.IndependentDefault(ShortcutTarget);
    private string EffectiveStyle => ShortcutIconEffect.Get(ShortcutTarget) is string value && value!="inherit" ? value : FollowsHostStyle?IconAppearance.Style:"original";
    private bool UsesSkin => FollowsHostStyle || ShortcutIconEffect.Get(ShortcutTarget)!="inherit";
    private bool? skinLightOverride;
    internal bool? SkinLightOverride {get=>skinLightOverride;set{if(skinLightOverride==value)return;skinLightOverride=value;AppearanceChanged();}}
    private ImageSource? originalSource;
    private BitmapSource? vectorRaster;
    private int vectorPixels;
    private int lastAppearance=-1;
    internal bool Dimmed {get=>dimmed;set{if(dimmed==value)return;dimmed=value;lastPixels=0;Refresh();}}
    private bool dimmed;
    private bool updating;
    private int lastPixels; private string? lastPath;
    private bool PressedMetal => UsesSkin && (EffectiveStyle is "metal" or "metal-c" or "inlay") && !CommonMetal.NoSkin;
    static AdaptiveIconImage(){EffectProperty.OverrideMetadata(typeof(AdaptiveIconImage),new FrameworkPropertyMetadata(null,null,(d,value)=>((AdaptiveIconImage)d).PressedMetal?null:value));}
    private BitmapSource? reliefImage; private ImageSource? reliefSource;private string? reliefKey;
    internal BitmapSource? RenderedRelief => PressedMetal?reliefImage:null;
    protected override void OnRender(DrawingContext dc){
        if(!PressedMetal||Source==null){base.OnRender(dc);return;}
        string key=$"{EffectiveStyle}|{CommonMetal.FinishName}|{RenderSize}|{CommonMetal.Viewport(this)}|{LightMetalBevel.Sampling(this)}|{dimmed}";
        if(reliefImage==null||reliefSource!=Source||reliefKey!=key){reliefImage=InstalledIconMaterials.Render(this,Source,dimmed,EffectiveStyle);reliefSource=Source;reliefKey=key;}
        RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
        double padding=EffectiveStyle is "metal" or "metal-c"?PressedMetalRelief.IconPadding:0;
        dc.DrawImage(reliefImage,new Rect(-padding,-padding,reliefImage.Width,reliefImage.Height));
    }
    public AdaptiveIconImage(){Loaded+=(_,_)=>{LayoutUpdated+=LayoutReady;IconAppearance.Changed+=AppearanceChanged;Refresh();};Unloaded+=(_,_)=>{LayoutUpdated-=LayoutReady;IconAppearance.Changed-=AppearanceChanged;};}
    private void AppearanceChanged(){CoerceValue(EffectProperty);InvalidateVisual();lastPixels=0;lastAppearance=-1;Refresh();}
    private void LayoutReady(object? sender,EventArgs e){if(PressedMetal){string key=$"{EffectiveStyle}|{CommonMetal.FinishName}|{RenderSize}|{CommonMetal.Viewport(this)}|{LightMetalBevel.Sampling(this)}|{dimmed}";if(key!=reliefKey)InvalidateVisual();}Refresh();}
    protected override void OnRenderSizeChanged(SizeChangedInfo info){base.OnRenderSizeChanged(info);Refresh();}
    protected override void OnDpiChanged(DpiScale oldDpi,DpiScale newDpi){base.OnDpiChanged(oldDpi,newDpi);Refresh();}
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e){base.OnPropertyChanged(e);if(e.Property==SourceProperty&&!updating){originalSource=e.NewValue as ImageSource;vectorRaster=null;lastPixels=0;Refresh();}}
    private void Refresh()
    {
        using var callbackTiming = new UiCallbackTiming("AdaptiveIconImage.Refresh");
        if(updating||ActualWidth<=0||ActualHeight<=0)return;
        var path=IconBitmap.PathFor(originalSource??Source);
        if(path==null&&!UsesSkin&&originalSource!=null){updating=true;try{SetCurrentValue(SourceProperty,originalSource);}finally{updating=false;}return;}
        if(path==null&&originalSource==null)return;
        double width=ActualWidth,height=ActualHeight;
        if(PresentationSource.FromVisual(this)?.RootVisual is Visual root&&root!=this){try{var rect=TransformToAncestor(root).TransformBounds(new Rect(RenderSize));width=rect.Width;height=rect.Height;}catch(InvalidOperationException){}}
        var dpi=VisualTreeHelper.GetDpi(this);
        // Extra sampling headroom also covers active-tab growth between layout passes.
        int pixels=(int)Math.Ceiling(Math.Max(width*dpi.DpiScaleX,height*dpi.DpiScaleY)*1.25);
        pixels=Math.Clamp(((pixels+63)/64)*64,64,2048);
        if(lastPixels==pixels&&lastPath==path&&lastAppearance==IconAppearance.Revision)return;lastPixels=pixels;lastPath=path;lastAppearance=IconAppearance.Revision;
        if(path==null&&originalSource is not BitmapSource){
            if(EffectiveStyle=="original"){updating=true;try{SetCurrentValue(SourceProperty,originalSource);}finally{updating=false;}return;}
            if(vectorRaster==null||vectorPixels!=pixels){
                vectorPixels=pixels;double ratio=originalSource!.Width/Math.Max(1,originalSource.Height);int rw=ratio>=1?pixels:Math.Max(1,(int)(pixels*ratio)),rh=ratio>=1?Math.Max(1,(int)(pixels/ratio)):pixels;
                var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawImage(originalSource,new Rect(0,0,rw,rh));
                var raster=new RenderTargetBitmap(rw,rh,96,96,PixelFormats.Pbgra32);raster.Render(visual);raster.Freeze();vectorRaster=raster;
            }
        }
        var next=path!=null?IconBitmap.Load(path,pixels):originalSource as BitmapSource??vectorRaster;if(next==null)return;
        if(UsesSkin)
        {
            // Inactive LIGHT tabs used the DARK palette as a dimming shortcut.
            // A shared LIGHT relief must shade every tab with the same material.
            bool light=dimmed&&CommonMetal.IsLight&&(EffectiveStyle is "metal" or "metal-c" or "inlay")
                ?true:skinLightOverride??CommonMetal.IsLight;
            if(!PressedMetal)next=IconAppearance.Apply(next,light,style:EffectiveStyle);
        }
        if(dimmed&&(!UsesSkin||EffectiveStyle=="original"))next=IconBitmap.Dim(next);if(ReferenceEquals(Source,next))return;
        updating=true;try{SetCurrentValue(SourceProperty,next);}finally{updating=false;}
    }
}


