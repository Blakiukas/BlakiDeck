using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Ekrano_valdymas;
public partial class WebShortcutsWidget
{
    private bool synchronizingView,suppressViewEvents;
    public static readonly DependencyProperty ViewModeProperty=DependencyProperty.Register(nameof(ViewMode),typeof(string),typeof(WebShortcutsWidget),
        new PropertyMetadata("List",(d,_)=>((WebShortcutsWidget)d).ModeChanged(),(_,value)=>NormalizeViewMode(value as string,false)));
    public string ViewMode {get=>(string)GetValue(ViewModeProperty);set=>SetValue(ViewModeProperty,value);}
    public static readonly DependencyProperty IconSizeProperty=DependencyProperty.Register(nameof(IconSize),typeof(double),typeof(WebShortcutsWidget),
        new PropertyMetadata(48d,(d,e)=>{var widget=(WebShortcutsWidget)d;widget.SetValue(IconTileExtentProperty,(double)e.NewValue+24);widget.SetValue(IconFallbackSizeProperty,(double)e.NewValue*.65);if(!widget.suppressViewEvents)widget.IconSizeChanged?.Invoke((double)e.NewValue);},(_,value)=>NormalizeIconSize((double)value)));
    public double IconSize {get=>(double)GetValue(IconSizeProperty);set=>SetValue(IconSizeProperty,value);}
    public static readonly DependencyProperty IconTileExtentProperty=DependencyProperty.Register(nameof(IconTileExtent),typeof(double),typeof(WebShortcutsWidget),new PropertyMetadata(72d));
    public double IconTileExtent=>(double)GetValue(IconTileExtentProperty);
    public static readonly DependencyProperty IconFallbackSizeProperty=DependencyProperty.Register(nameof(IconFallbackSize),typeof(double),typeof(WebShortcutsWidget),new PropertyMetadata(31.2d));
    public double IconFallbackSize=>(double)GetValue(IconFallbackSizeProperty);
    public event Action<string>? ViewModeChanged;
    public event Action<double>? IconSizeChanged;
    internal static string NormalizeViewMode(string? mode,bool legacyImagesOnly)=>mode is "List" or "Thumbs" or "Icons"?mode:legacyImagesOnly?"Thumbs":"List";
    internal static double NormalizeIconSize(double size)=>Math.Clamp(double.IsFinite(size)?size:48,32,128);
    internal void RestoreViewSettings(string mode,double size)
    {suppressViewEvents=true;try{ViewMode=mode;IconSize=size;}finally{suppressViewEvents=false;}}
    private void LegacyViewChanged(bool thumbnails){if(!synchronizingView)ViewMode=thumbnails?"Thumbs":"List";QueuePreviewDemand();}
    private void ModeChanged()
    {
        synchronizingView=true;try{SetCurrentValue(ImagesOnlyProperty,ViewMode=="Thumbs");}finally{synchronizingView=false;}
        if(webModeButton!=null){webModeButton.ToolTip="View: "+ViewMode+" · switch List / Thumbs / Icons";System.Windows.Automation.AutomationProperties.SetName(webModeButton,(string)webModeButton.ToolTip);}
        QueuePreviewDemand();if(!suppressViewEvents){ViewChanged?.Invoke(ImagesOnly);ViewModeChanged?.Invoke(ViewMode);}
    }
    private void AddViewChoices(ContextMenu menu)
    {
        foreach(string mode in new[]{"List","Thumbs","Icons"})
        {var item=new MenuItem{Name="WebView"+mode,Header=mode,IsCheckable=true,IsChecked=ViewMode==mode};item.SetResourceReference(StyleProperty,"MiniMenuItemStyle");item.Click+=(_,_)=>ViewMode=mode;menu.Items.Add(item);}
        menu.Items.Add(new Separator());
    }
    public sealed partial class WebShortcut
    {
        // Icons mode never reads PreviewImage: custom artwork or favicon only.
        [System.Text.Json.Serialization.JsonIgnore] public ImageSource? IconOnlyImage=>ShortcutIcon.Load(previewIconPath??IconPath)??(IsMacro?MacroLibrary.Current.IconFor(Url):Favicon);
        [System.Text.Json.Serialization.JsonIgnore] public ImageSource? ThumbnailImage=>IsMacro?PreviewImage:previewImage??IconOnlyImage;
        // Captured page content is a photograph, not an icon material.
        [System.Text.Json.Serialization.JsonIgnore] public string? PreviewEffectTarget=>previewImage!=null&&ReferenceEquals(PreviewImage,previewImage)?null:Url;
        [System.Text.Json.Serialization.JsonIgnore] public string? ThumbnailEffectTarget=>!IsMacro&&previewImage!=null?null:Url;
        [System.Text.Json.Serialization.JsonIgnore] public Stretch ThumbnailStretch=>previewImage!=null?Stretch.UniformToFill:Stretch.Uniform;
    }
}
