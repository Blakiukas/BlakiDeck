using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.CompilerServices;

namespace Ekrano_valdymas;

// The texture is a window-space material. A moving card reveals its new location
// in that material; neither its size nor its identity changes the grain scale.
public static class CommonMetal
{
    internal static readonly DependencyProperty FullWidthFrameProperty = DependencyProperty.RegisterAttached(
        "FullWidthFrame", typeof(bool), typeof(CommonMetal), new PropertyMetadata(false));
    public static readonly DependencyProperty SurfaceProperty = DependencyProperty.RegisterAttached(
        "Surface", typeof(bool), typeof(CommonMetal), new PropertyMetadata(false, Changed));
    public static bool GetSurface(DependencyObject obj) => (bool)obj.GetValue(SurfaceProperty);
    public static void SetSurface(DependencyObject obj, bool value) => obj.SetValue(SurfaceProperty, value);
    public static readonly DependencyProperty LiftProperty = DependencyProperty.RegisterAttached("Lift", typeof(byte), typeof(CommonMetal), new PropertyMetadata((byte)0));
    public static byte GetLift(DependencyObject obj) => (byte)obj.GetValue(LiftProperty);
    public static void SetLift(DependencyObject obj, byte value) => obj.SetValue(LiftProperty, value);
    private static readonly Dictionary<string, DrawingGroup> fields = new();
    private static readonly Dictionary<string,BitmapSource> bevelFields = new();
    private static readonly LinkedList<string> recentFinishes = new(["dark"]);
    private static ResourceMode resourceMode;
    internal static void ApplyResourceMode(ResourceMode mode)
    {
        resourceMode=mode;
        TrackFinish(finish);
        TrimInactiveResources();
    }
    private static void TrackFinish(string value)
    {
        recentFinishes.Remove(value);recentFinishes.AddFirst(value);
        while(recentFinishes.Count>3)recentFinishes.RemoveLast();
    }
    private static long ImageBytes(BitmapSource image)=>((long)image.PixelWidth*Math.Max(1,image.Format.BitsPerPixel)+7)/8*image.PixelHeight;
    private static long BevelRetentionCost(BitmapSource image)
    {
#if SHARED_MATERIAL_VIEWBOX
        // Keep the same skin-retention policy while eliminating duplicate pixels.
        return image.PixelWidth*(long)Math.Ceiling(559*image.PixelWidth/1900d)*4;
#else
        return ImageBytes(image);
#endif
    }
    private static long CacheRetentionCost=>MusicConsoleControl.ImageCacheRetentionCost+bevelFields.Values.Sum(BevelRetentionCost);
    private static void TrimInactiveResources()
    {
        var retained=recentFinishes.Take(3).ToHashSet(StringComparer.Ordinal);
        long budget=320L*1024*1024;
        // Active render objects retain their sources independently. This bounds
        // inactive static ownership; it is not a whole-process memory ceiling.
        void Prune()
        {
            foreach(var key in fields.Keys.Where(key=>!retained.Contains(key)).ToArray())fields.Remove(key);
            foreach(var key in bevelFields.Keys.Where(key=>!retained.Contains(key)).ToArray())bevelFields.Remove(key);
            MusicConsoleControl.TrimImageCache(retained);
        }
        Prune();
        foreach(var old in recentFinishes.Reverse().Where(value=>value!=finish).ToArray())
        {
            if(CacheRetentionCost<=budget)break;
            retained.Remove(old);Prune();
        }
    }
    internal static object ResourceCacheForAudit()=>new
    {
        activeFinish=finish,mode=resourceMode.ToString(),recent=recentFinishes.ToArray(),
        retainedFinishLimit=3,
        retentionBudgetBytes=320L*1024*1024,
        materialDrawingKeys=fields.Keys.Order().ToArray(),
        bevels=bevelFields.Select(item=>new{key=item.Key,width=item.Value.PixelWidth,height=item.Value.PixelHeight,pixelBytes=ImageBytes(item.Value)}).ToArray(),
        images=MusicConsoleControl.ImageCacheForAudit(),cachePixelBytes=MusicConsoleControl.ImageCachePixelBytesIncluding(bevelFields.Values),retentionCostBytes=CacheRetentionCost,
        limit="Inactive static cache only; active sources, shared hardware and live visual references can exceed the budget."
    };
    private static bool light;
    private static string finish="dark";
    private static int revision;
    private static bool noSkin, deferStartupMaterial;
    internal static bool NoSkin => noSkin || deferStartupMaterial;
    internal static void DeferStartupMaterial(){deferStartupMaterial=true;revision++;}
    internal static bool CompleteStartupMaterial()
    {
        if(!deferStartupMaterial)return false;
        deferStartupMaterial=false;revision++;return true;
    }
    internal static void SetNoSkin(bool value){if(noSkin!=value){noSkin=value;revision++;IconAppearance.Refresh();}}
    private static readonly Brush PlainBackground = new SolidColorBrush(Color.FromRgb(32,32,32));
    private static readonly DependencyProperty RefreshActionProperty=DependencyProperty.RegisterAttached("RefreshAction",typeof(Action),typeof(CommonMetal));
    private static readonly ConditionalWeakTable<Brush,object> generatedDefaultBrushes=new();
    private sealed class SurfaceOwnership
    {
        internal Brush? Brush;
        internal DrawingBrush? Shell;
        internal int Releases;
        internal int InitialDefaultReleases;
        internal Brush Hold(Brush material)
        {
            if(material is not DrawingBrush source)
            {
                if(Shell!=null)Shell.Drawing=null;
                return Brush=material;
            }
            // WPF can retain a former RenderDrawing after its control unloads.
            // Keep that command's brush reference small and severable; only its
            // immutable inner drawing contains the material and bevel rasters.
            var shell=Shell??=new DrawingBrush();
            shell.Opacity=source.Opacity;
            shell.Transform=source.Transform;
            shell.RelativeTransform=source.RelativeTransform;
            shell.AlignmentX=source.AlignmentX;shell.AlignmentY=source.AlignmentY;
            shell.Stretch=source.Stretch;shell.TileMode=source.TileMode;
            shell.ViewboxUnits=source.ViewboxUnits;shell.Viewbox=source.Viewbox;
            shell.ViewportUnits=source.ViewportUnits;shell.Viewport=source.Viewport;
            shell.Drawing=source.Drawing;
            return Brush=shell;
        }
    }
    private static readonly ConditionalWeakTable<FrameworkElement,SurfaceOwnership> surfaceOwnership=new();
    internal static object SurfaceOwnershipForAudit()=>surfaceOwnership.Select(pair=>new
    {
        type=pair.Key.GetType().Name,name=pair.Key.Name,loaded=pair.Key.IsLoaded,painted=IsPainted(pair.Key),
        ownsBrush=pair.Value.Brush!=null,releases=pair.Value.Releases,initialDefaultReleases=pair.Value.InitialDefaultReleases,
        backgroundType=pair.Value.Brush?.GetType().Name,
        hasMutableShell=pair.Value.Shell!=null,shellActive=pair.Value.Shell?.Drawing!=null,
        shellEmpty=pair.Value.Shell!=null&&pair.Value.Shell.Drawing==null,shellFrozen=pair.Value.Shell?.IsFrozen,
        shellDrawingFrozen=pair.Value.Shell?.Drawing?.IsFrozen,
        ownedBrushIsShell=pair.Value.Shell!=null&&ReferenceEquals(pair.Value.Brush,pair.Value.Shell)
    }).ToArray();
    internal static void RefreshVisibleTree(DependencyObject root)
    {
        if(root is QuickLaunchBar bar)bar.RefreshMilitarySticker();
        if(root.GetValue(RefreshActionProperty) is Action refresh)refresh();
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)RefreshVisibleTree(VisualTreeHelper.GetChild(root,i));
    }
    internal static void ReleaseInactiveSurfaces()
    {
        // A resource can change after Initialized while its surface has never
        // loaded or is detached. Such controls are absent from the visible tree.
        foreach(var pair in surfaceOwnership)
        {
            var element=pair.Key;
            if(element.IsLoaded&&IsPainted(element))continue;
            if(element.GetValue(RefreshActionProperty) is Action refresh)refresh();
        }
    }
    internal static bool IsLight => light;
    internal static string FinishName => finish;
    private static readonly DependencyProperty RestingOriginProperty = DependencyProperty.RegisterAttached(
        "RestingOrigin", typeof(Point?), typeof(CommonMetal),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    internal static void SetRestingOrigin(FrameworkElement element, Point origin) => element.SetValue(RestingOriginProperty, origin);

    internal static void SetFinish(bool value) { string next=value==MusicFinish.IsLight?MusicFinish.Name:value?"light":"dark"; if (value != light || finish!=next) { light = value; finish=next;TrackFinish(next); revision++;IconAppearance.Refresh(); } }
    internal static DrawingGroup Field
    {
        get
        {
            if (fields.TryGetValue(finish, out var material)) return material;
            var source = MusicConsoleControl.Bitmap("shared-material.png", finish: finish);
            double scale = source.PixelWidth / 1900d;
            material = new DrawingGroup();
            using (var dc = material.Open())
            {
                // Keep the original pixels and clip the drawing, rather than
                // retaining a second WIC bitmap for the 559-high material crop.
                var clip=new RectangleGeometry(new Rect(0,0,1900,559));clip.Freeze();
                dc.PushClip(clip);
                dc.DrawImage(source,new Rect(0,0,1900,source.PixelHeight/scale));
                dc.Pop();
                // Preserve the Blender material without an added white wash.
            }
            material.Freeze(); fields[finish] = material; return material;
        }
    }

    internal static Rect Viewport(FrameworkElement element)
    {
        if (element.GetValue(MaterialReferenceProperty) is FrameworkElement reference && reference.IsLoaded && element.IsLoaded)
        {
            var top = element.PointFromScreen(reference.PointToScreen(new Point(10,-2)));
            var bottom = element.PointFromScreen(reference.PointToScreen(new Point(1910,557)));
            return new Rect(top,bottom);
        }
        FrameworkElement root = element;
        bool dashboard = false;
        for (DependencyObject? parent = element; parent != null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is FrameworkElement candidate) root = candidate;
            if (parent is FrameworkElement { Name: "dashboardCanvas" } canvas) { root = canvas; dashboard = true; break; }
        }
        double width = dashboard ? 1900 : Math.Max(1, root.ActualWidth);
        var material = dashboard
            ? ((bool)root.GetValue(FullWidthFrameProperty) ? new Rect(0, -2, 1920, 559) : new Rect(10, -2, 1900, 559))
            : new Rect(0, 0, width, width * 559 / 1900);
        // Sliding chrome keeps its original material sample attached to itself;
        // ordinary dashboard surfaces retain the shared window-space material.
        if (element.GetValue(RestingOriginProperty) is Point origin)
        {
            material.Offset(-origin.X, -origin.Y);
            return material;
        }
        return root == element ? material : root.TransformToDescendant(element).TransformBounds(material);
    }

    internal static Brush ForElement(FrameworkElement element) => Brush(Viewport(element));
    private static readonly DependencyProperty MaterialReferenceProperty=DependencyProperty.RegisterAttached("MaterialReference",typeof(FrameworkElement),typeof(CommonMetal));
    internal static void MatchDashboardMaterial(FrameworkElement surface,DependencyObject? owner)
    {
        if(owner==null){surface.ClearValue(MaterialReferenceProperty);return;}
        if(owner is FrameworkElement {Name:"dashboardCanvas"} canvas){surface.SetValue(MaterialReferenceProperty,canvas);return;}
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(owner);i++)MatchDashboardMaterial(surface,VisualTreeHelper.GetChild(owner,i));
    }
    internal static Brush ForBevel(FrameworkElement element)
    {
        if(NoSkin)return PlainBackground;
        if(!bevelFields.TryGetValue(finish,out var image))
        {
            // Reuse the immutable full-resolution source. The brush selects the
            // original material rectangle without allocating a second raster.
#if SHARED_MATERIAL_VIEWBOX
            image=MusicConsoleControl.Bitmap("shared-material.png",finish:finish);
#else
            image=RasterizeMaterial(MusicConsoleControl.Bitmap("shared-material.png",finish:finish),light);
#endif
            bevelFields[finish]=image;
        }
        var brush=new ImageBrush(image){ViewportUnits=BrushMappingMode.Absolute,Viewport=Viewport(element),Stretch=Stretch.Fill,TileMode=TileMode.FlipY};
#if SHARED_MATERIAL_VIEWBOX
        // Sample the same source rectangle without a second full-resolution pixel buffer.
        brush.ViewboxUnits=BrushMappingMode.RelativeToBoundingBox;
        brush.Viewbox=new Rect(0,0,1,Math.Ceiling(559*image.PixelWidth/1900d)/image.PixelHeight);
#endif
        brush.Freeze();return brush;
    }
    internal static BitmapSource RasterizeMaterial(BitmapSource source,bool isLight)
    {
        double scale=source.PixelWidth/1900d;
        int height=(int)Math.Ceiling(559*scale),stride=source.PixelWidth*4;
        var converted=new FormatConvertedBitmap(source,PixelFormats.Pbgra32,null,0);
        var pixels=new byte[stride*height];
        converted.CopyPixels(new Int32Rect(0,0,source.PixelWidth,height),pixels,stride,0);
        // Preserve original Blender color, including in bevel and connected-rail sampling.
        var bitmap=BitmapSource.Create(source.PixelWidth,height,96*scale,96*scale,PixelFormats.Pbgra32,null,pixels,stride);
        bitmap.Freeze();return bitmap;
    }
    private static Brush Brush(Rect viewport, byte lift = 0)
    {
        if(NoSkin)return PlainBackground;
        Drawing drawing = Field;
        // Raised controls keep their bevels, not a white tint over the material.
        var brush = new DrawingBrush(drawing) { ViewportUnits = BrushMappingMode.Absolute, Viewport = viewport, Stretch = Stretch.Fill, TileMode = TileMode.FlipY };
        brush.Freeze(); return brush;
    }
    internal static Brush DefaultBrush
    {
        get
        {
            var brush=Brush(new Rect(0,0,1900,559));
            generatedDefaultBrushes.GetValue(brush,_=>new object());
            return brush;
        }
    }

    private static bool IsPainted(FrameworkElement element)
    {
        if(!element.IsVisible)return false;
        // Player mode preserves module layout behind the player with Opacity=0.
        // WPF IsVisible remains true for those fully transparent ancestors.
        for(DependencyObject? p=element;p!=null;p=VisualTreeHelper.GetParent(p))
            if(p is UIElement visual && visual.Opacity<=0)return false;
        return true;
    }

    private static void Changed(DependencyObject obj, DependencyPropertyChangedEventArgs args)
    {
        if (obj is not FrameworkElement element || !(bool)args.NewValue) return;
        Brush? neutralBorder=null;
        var ownership=surfaceOwnership.GetValue(element,_=>new());
        var property = element is Border ? Border.BackgroundProperty : element is Panel ? Panel.BackgroundProperty : element is Control ? Control.BackgroundProperty : null;
        Rect previous = Rect.Empty; int version = -1;Size previousSize=Size.Empty;CornerRadius previousCorners=new();
        System.Windows.Threading.DispatcherOperation? pending=null, pendingRelease=null;
        void Release()
        {
            // Reparenting can briefly unload a surface which is shown again in
            // the same transition. Do not empty its current render command in
            // that gap. Truly hidden surfaces still release after Loaded/render.
            if(pendingRelease!=null)return;
            pendingRelease=element.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>
            {
                pendingRelease=null;
                if(element.IsLoaded&&IsPainted(element)){Refresh();return;}
                ReleaseNow();
            }));
        }
        void ReleaseNow()
        {
            // Clear the retained drawing command as well as Background. A foreign
            // replacement stays intact, but must not keep our old shell active.
            bool released=ownership.Brush!=null||ownership.Shell?.Drawing!=null;
            if(ownership.Shell!=null)ownership.Shell.Drawing=null;
            if(property!=null&&!BindingOperations.IsDataBound(element,property))
            {
                var current=element.GetValue(property) as Brush;
                if(ownership.Brush!=null&&ReferenceEquals(current,ownership.Brush))element.SetValue(property,Brushes.Transparent);
                else if(ownership.Brush==null&&current!=null&&generatedDefaultBrushes.TryGetValue(current,out _))
                {
                    // A never-loaded XAML surface can already hold the initial
                    // global material. Recognize only our exact generated brush;
                    // unrelated resource expressions and bindings stay intact.
                    element.SetValue(property,Brushes.Transparent);
                    ownership.InitialDefaultReleases++;released=true;
                }
            }
            if(!released)return;
            ownership.Brush=null;ownership.Releases++;
            previous=Rect.Empty;previousSize=Size.Empty;version=-1;
        }
        void Update(object? sender, EventArgs e)
        {
            // A tab transition changes parent, size, finish and opacity in
            // several steps. Render only the final state before composition.
            if(pending!=null)return;
            pending=element.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render,new Action(()=>{pending=null;Refresh();}));
        }
        void Refresh()
        {
        using var callbackTiming = new UiCallbackTiming("CommonMetal.LayoutUpdated");
            // Detached/hidden modules still receive LayoutUpdated. Their old
            // arranged size and new root are not a renderable material state.
            if (!element.IsLoaded || !IsPainted(element)) {Release();return;}
            pendingRelease?.Abort();pendingRelease=null;
            if (!GetSurface(element) || element.ActualWidth <= 0) return;
            Rect viewport = Viewport(element);
            // Neutral legacy chrome duplicates the physical chamfer. Retain its
            // layout thickness and all colored attention/drag outlines.
            if(element is Border legacy)
            {
                if(legacy.BorderBrush is SolidColorBrush neutral &&
                   (neutral.Color==Color.FromArgb(64,255,255,255)||neutral.Color==Color.FromArgb(53,255,255,255)))
                {
                    neutralBorder=legacy.BorderBrush;
                    if(!NoSkin)legacy.SetCurrentValue(Border.BorderBrushProperty,Brushes.Transparent);
                }
                else if(NoSkin&&neutralBorder!=null&&legacy.BorderBrush is SolidColorBrush hidden&&hidden.Color.A==0)
                    legacy.SetCurrentValue(Border.BorderBrushProperty,neutralBorder);
            }
            bool resourceReset = property != null && ReferenceEquals(element.GetValue(property), Application.Current.Resources["PanelMetalBrush"]);
            var corners=element is Border edge?edge.CornerRadius:new CornerRadius();
            if (previous == viewport && version == revision && previousSize==element.RenderSize&&previousCorners==corners&&!resourceReset) return;
            previous = viewport; version = revision;previousSize=element.RenderSize;previousCorners=corners;
            // This component owns the spatial material once loaded. Keeping a
            // DynamicResource expression with SetCurrentValue allows unrelated
            // module-tree changes to restore the global flat brush for a frame.
            if (property != null)
            {
                ownership.Hold(LightMetalBevel.Background(element,Brush(viewport, GetLift(element))));
                element.SetValue(property,ownership.Brush);
            }
            else element.InvalidateVisual();
        }
        element.SetValue(RefreshActionProperty,new Action(()=>{pending?.Abort();pending=null;Refresh();}));
        element.Initialized+=(_,_)=>{if(!element.IsLoaded)Release();};
        element.Loaded += (_, _) => { previous = Rect.Empty; element.LayoutUpdated += Update; Refresh(); };
        element.Unloaded += (_, _) => { element.LayoutUpdated -= Update;pending?.Abort();pending=null;Release(); };
        element.IsVisibleChanged += (_, _) => { if(element.IsVisible) Update(null,EventArgs.Empty);else Release(); };
        if (element.IsLoaded) { element.LayoutUpdated += Update; Update(null, EventArgs.Empty); }
        else Release();
    }
}






