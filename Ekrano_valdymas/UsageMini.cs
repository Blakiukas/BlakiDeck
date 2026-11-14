using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

internal sealed record UsageMiniOptions(bool Cpu=true,bool Gpu=true,bool Ram=true,bool Network=false,double LabelSize=16,double ValueSize=16,double TitleSize=10,bool HideTitle=true,bool Web=true)
{
    internal bool HasMetrics=>Cpu||Gpu||Ram||Network||Web;
    internal bool Includes(int index)=>index switch{0=>Cpu,1=>Gpu,2=>Ram,3=>Network,4=>Web,_=>false};
    internal UsageMiniOptions Normalize()=>this with{LabelSize=Size(LabelSize,16),ValueSize=Size(ValueSize,16),TitleSize=Size(TitleSize,10)};
    private static double Size(double value,double fallback)=>double.IsFinite(value)?Math.Clamp(value,8,80):fallback;
}

// Presentation consumers share the main window's existing process-family sampler.
internal static class UsageMiniState
{
    internal static event Action? Changed;
    internal static event Action? DemandChanged;
    internal static int Consumers {get;private set;}
    private static int gpuConsumers;
    internal static bool NeedsGpu=>gpuConsumers>0;
    internal static string[] Values {get;private set;}=["N/A","N/A","N/A","N/A","N/A"];
    internal static void Demand(int delta,bool gpu)
    {
        bool wasActive=Consumers>0,wasGpu=NeedsGpu;
        Consumers=Math.Max(0,Consumers+delta);
        if(gpu)gpuConsumers=Math.Max(0,gpuConsumers+delta);
        if(wasActive!=(Consumers>0)||wasGpu!=NeedsGpu)DemandChanged?.Invoke();
    }
    internal static void GpuDemand(bool previous,bool next)
    {
        if(previous==next)return;
        bool wasGpu=NeedsGpu;gpuConsumers=Math.Max(0,gpuConsumers+(next?1:-1));
        if(wasGpu!=NeedsGpu)DemandChanged?.Invoke();
    }
    internal static void Publish(string cpu,string gpu,string ram,string net)=>PublishMemory(cpu,gpu,ram,net,"N/A");
    internal static void PublishMemory(string cpu,string gpu,string ram,string net,string web)
    {
        if(Values[0]==cpu&&Values[1]==gpu&&Values[2]==ram&&Values[3]==net&&Values[4]==web)return;
        Values=[cpu,gpu,ram,net,web];Changed?.Invoke();
    }
}

public sealed class UsageMini:Grid
{
    internal const string TargetName="blakideck-usage:";
    internal static bool IsTarget(string? target)=>target?.StartsWith(TargetName,StringComparison.Ordinal)==true;
    internal static string Encode(UsageMiniOptions options)
    {
        var normalized=options.Normalize();
        return normalized==new UsageMiniOptions()?TargetName:TargetName+Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(normalized)));
    }
    internal static UsageMiniOptions Decode(string? target)
    {
        try{return IsTarget(target)&&target!.Length>TargetName.Length?(JsonSerializer.Deserialize<UsageMiniOptions>(Encoding.UTF8.GetString(Convert.FromBase64String(target[TargetName.Length..])))??new()).Normalize():new();}
        catch{return new();}
    }
    internal static string Description(string? target)
    {
        var options=Decode(target);
        return "BlakiDeck resource usage · "+string.Join(", ",new[]{"CPU","GPU","BD RAM","network","Web RAM"}.Where((_,i)=>options.Includes(i)))+"\nRAM: BlakiDeck + helper; WEB: BlakiDeck browser processes. Private resident memory; 1 MB = 1,048,576 bytes.\nRight-click to edit";
    }
    public static readonly DependencyProperty TargetProperty=DependencyProperty.Register(nameof(Target),typeof(string),typeof(UsageMini),new PropertyMetadata(null,(d,_)=>((UsageMini)d).TargetChanged()));
    public string? Target{get=>(string?)GetValue(TargetProperty);set=>SetValue(TargetProperty,value);}
    private readonly OpticallyCenteredText?[] readings=new OpticallyCenteredText?[5];
    private static readonly string[] units=["%","%"," MB"," MB/s"," MB"];
    private readonly List<UsageReadingRow> pairs=new();
    private readonly OpticallyCenteredText title=Text("BLAKIDECK USAGE",10);
    private UsageMiniOptions options=new();
    private bool subscribed,gpuSubscribed,configured;
    private Window? host;
    private FrameworkElement? toolTipOwner;
    private object? originalToolTip;
    private ToolTip? forwardedToolTipWindow;
    private string? forwardedToolTip;
    private int layoutMode;
    private Size arrangedSize;
    private readonly List<UIElement> opacitySources=new();
    private readonly System.ComponentModel.DependencyPropertyDescriptor opacityProperty=System.ComponentModel.DependencyPropertyDescriptor.FromProperty(UIElement.OpacityProperty,typeof(UIElement));
    public UsageMini()
    {
        IsHitTestVisible=false;Margin=new Thickness(4,2,4,2);
        Configure(new());
        Loaded+=(_,_)=>{host=Window.GetWindow(this);if(host!=null)host.StateChanged+=HostStateChanged;AttachToolTipOwner();Activity();};
        Unloaded+=(_,_)=>{if(host!=null)host.StateChanged-=HostStateChanged;host=null;Stop();ClearOpacitySources();DetachToolTipOwner();};
        IsVisibleChanged+=(_,_)=>Activity();
    }
    private void TargetChanged(){Configure(Decode(Target));Activity();}
    private void Configure(UsageMiniOptions next)
    {
        if(configured&&next==options)return;
        RestoreToolTip();
        options=next;configured=true;Children.Clear();Children.Add(title);pairs.Clear();Array.Clear(readings);
        title.TextFontSize=options.TitleSize;
        string[] names=["CPU","GPU","RAM","NET","WEB"];
        for(int i=0;i<5;i++)if(options.Includes(i))
        {
            var label=Text(names[i],options.LabelSize);var value=Text("N/A",options.ValueSize);readings[i]=value;
            var pair=new UsageReadingRow(label,value,options.LabelSize,options.ValueSize,i);pairs.Add(pair);Children.Add(pair);
        }
        layoutMode=0;LayoutRows(new Size());Refresh();
    }
    protected override Size MeasureOverride(Size available)
    {
        LayoutRows(available);
        double width=double.IsFinite(available.Width)?available.Width:arrangedSize.Width;
        double height=double.IsFinite(available.Height)?available.Height:arrangedSize.Height;
        double cellWidth=width/Math.Max(1,ColumnDefinitions.Count),cellHeight=height/Math.Max(1,RowDefinitions.Count);
        double scale=1;
        foreach(var pair in pairs)
            scale=Math.Min(scale,pair.ScaleFor(new Size(Math.Max(0,cellWidth-pair.Margin.Left-pair.Margin.Right),Math.Max(0,cellHeight))));
        foreach(var pair in pairs)pair.SetScale(scale);
        return base.MeasureOverride(available);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var result=base.ArrangeOverride(finalSize);
        if(arrangedSize!=finalSize){arrangedSize=finalSize;InvalidateMeasure();}
        return result;
    }
    private void LayoutRows(Size available)
    {
        bool list=false;
        for(DependencyObject? parent=VisualTreeHelper.GetParent(this);parent!=null;parent=VisualTreeHelper.GetParent(parent))
        {
            if(parent is ShortcutsWidget widget){list=widget.ListMode;break;}
            if(parent is MacroLibraryCard library){list=library.IsListPresentation;break;}
        }
        double height=double.IsFinite(available.Height)?available.Height:ActualHeight,width=double.IsFinite(available.Width)&&available.Width>0?available.Width:ActualWidth;
        int mode=list||height>0&&height<65?(width>=450?4:2):1;
        if(layoutMode==mode)return;
        layoutMode=mode;RowDefinitions.Clear();ColumnDefinitions.Clear();
        int columns=Math.Min(mode,Math.Max(1,pairs.Count));
        for(int i=0;i<columns;i++)ColumnDefinitions.Add(new());
        int offset=mode==1&&!options.HideTitle?1:0;
        for(int i=0;i<offset+(pairs.Count+columns-1)/columns;i++)RowDefinitions.Add(new());
        title.Visibility=offset==1?Visibility.Visible:Visibility.Collapsed;Grid.SetColumnSpan(title,columns);
        for(int i=0;i<pairs.Count;i++)
        {
            Grid.SetRow(pairs[i],offset+i/columns);Grid.SetColumn(pairs[i],i%columns);
            pairs[i].Margin=new Thickness(0,0,i%columns<columns-1?10:0,0);
        }
    }
    private static OpticallyCenteredText Text(string value,double size)
    {
        var text=new OpticallyCenteredText{Text=value,TextFontFamily=new FontFamily("Segoe UI Semibold"),TextFontSize=size,CenterCapToBaseline=true,FitToBox=true};
        text.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"UsageBrush");return text;
    }
    private void HostStateChanged(object? sender,EventArgs e)=>Activity();
    private void AttachToolTipOwner()
    {
        DetachToolTipOwner();
        for(DependencyObject? parent=VisualTreeHelper.GetParent(this);parent!=null;parent=VisualTreeHelper.GetParent(parent))
            if(parent is FrameworkElement {ToolTip:not null} element)
            {
                toolTipOwner=element;element.ToolTipOpening+=OwnerToolTipOpening;element.ToolTipClosing+=OwnerToolTipClosing;break;
            }
    }
    private void DetachToolTipOwner()
    {
        RestoreToolTip();
        if(toolTipOwner is {} owner){owner.ToolTipOpening-=OwnerToolTipOpening;owner.ToolTipClosing-=OwnerToolTipClosing;}
        toolTipOwner=null;
    }
    private void OwnerToolTipOpening(object sender,ToolTipEventArgs e)
    {
        if(toolTipOwner is not {} owner||!IsTarget(Target))return;
        RestoreToolTip();
        // Keep an explicit ToolTip attached to its owner. Replacing it with a
        // string makes WPF create a wrapper whose Content binds to owner.ToolTip;
        // restoring the ToolTip during Closing would nest one popup in another.
        forwardedToolTipWindow=owner.ToolTip as ToolTip;
        originalToolTip=forwardedToolTipWindow!=null?forwardedToolTipWindow.Content:owner.ToolTip;
        string heading=Description(Target);
        forwardedToolTip=heading+"\n\n"+string.Join("\n",pairs.Select(pair=>pair.Label.Text+": "+pair.Value.Text));
        if(forwardedToolTipWindow is {} tip)tip.SetCurrentValue(ContentControl.ContentProperty,forwardedToolTip);
        else owner.SetCurrentValue(ToolTipProperty,forwardedToolTip);
    }
    private void OwnerToolTipClosing(object sender,ToolTipEventArgs e)=>RestoreToolTip();
    private void RestoreToolTip()
    {
        // SetCurrentValue retains the owner's binding/resource expression.
        // A foreign update while the tooltip is open always takes precedence.
        var tip=forwardedToolTipWindow;var original=originalToolTip;var forwarded=forwardedToolTip;
        originalToolTip=null;forwardedToolTip=null;forwardedToolTipWindow=null;
        if(tip!=null)
        {
            if(forwarded!=null&&ReferenceEquals(tip.Content,forwarded))tip.SetCurrentValue(ContentControl.ContentProperty,original);
        }
        else if(toolTipOwner is {} owner&&forwarded!=null&&ReferenceEquals(owner.ToolTip,forwarded))owner.SetCurrentValue(ToolTipProperty,original);
    }
    private void OpacityChanged(object? sender,EventArgs e)=>Activity();
    private void ClearOpacitySources(){foreach(var source in opacitySources)opacityProperty.RemoveValueChanged(source,OpacityChanged);opacitySources.Clear();}
    private void Activity()
    {
        bool target=IsTarget(Target);
        if(IsLoaded&&target&&opacitySources.Count==0)
        {
            for(DependencyObject? parent=this;parent!=null;parent=VisualTreeHelper.GetParent(parent))if(parent is UIElement element){opacitySources.Add(element);opacityProperty.AddValueChanged(element,OpacityChanged);}
        }
        else if(!target)ClearOpacitySources();
        if(!IsLoaded||!IsVisible||host?.WindowState==WindowState.Minimized||opacitySources.Any(e=>e.Opacity==0)||!target||!options.HasMetrics){Stop();return;}
        if(!subscribed){subscribed=true;gpuSubscribed=options.Gpu;UsageMiniState.Changed+=Refresh;UsageMiniState.Demand(1,gpuSubscribed);}
        else if(gpuSubscribed!=options.Gpu){UsageMiniState.GpuDemand(gpuSubscribed,options.Gpu);gpuSubscribed=options.Gpu;}
        Refresh();
    }
    private void Stop()
    {
        if(!subscribed)return;
        subscribed=false;UsageMiniState.Changed-=Refresh;UsageMiniState.Demand(-1,gpuSubscribed);gpuSubscribed=false;
    }
    private void Refresh()
    {
        var values=UsageMiniState.Values;
        for(int i=0;i<5;i++)if(readings[i] is {} text)
        {
            string next=values[i]=="N/A"?"N/A":values[i]+units[i];
            if(text.Text!=next)
            {
                text.Text=next;
                // Refresh the actual glyphs inside a fixed format envelope.
                // The fit never depends on this particular sample's digits.
                if(text.Parent is UsageReadingRow pair)pair.InvalidateMeasure();
                InvalidateMeasure();
            }
        }
    }
}

// A row still aligns its two cap-to-baseline bottoms, but UsageMini chooses one
// common scale for every row. A wider RAM value cannot shrink only its own row.
internal sealed class UsageReadingRow:Panel
{
    internal OpticallyCenteredText Label {get;}
    internal OpticallyCenteredText Value {get;}
    internal double LabelSize {get;}
    internal double ValueSize {get;}
    internal double RenderedScale {get;private set;}=1;
    private const double Gap=8;
    private readonly record struct Metrics(double Width,double CapHeight,double Overflow);
    private readonly record struct Key(string Text,double Size,string Face,FontWeight Weight,FontStyle Style,double Dpi,string Culture);
    private (Key Key,Metrics Metrics)? labelCache,valueCache,envelopeCache;
    private readonly int metricIndex;
    private Metrics measuredLabel,measuredValue;
    internal UsageReadingRow(OpticallyCenteredText label,OpticallyCenteredText value,double labelSize,double valueSize,int metricIndex)
    {
        Label=label;Value=value;LabelSize=labelSize;ValueSize=valueSize;this.metricIndex=metricIndex;
        UseLayoutRounding=false;
        foreach(var text in new[]{Label,Value}){text.UseLayoutRounding=false;text.FitToBox=false;text.CompactLineBox=true;text.CenterCapToBaseline=true;Children.Add(text);}
    }
    private Key Inputs(OpticallyCenteredText text,double size,string content)=>new(content,size,text.TextFontFamily.Source,text.TextFontWeight,text.TextFontStyle,VisualTreeHelper.GetDpi(this).PixelsPerDip,CultureInfo.CurrentUICulture.Name+"|"+CultureInfo.CurrentCulture.Name+"|"+CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
    private Metrics Natural(OpticallyCenteredText text,double size,ref (Key Key,Metrics Metrics)? cache)
    {
        var key=Inputs(text,size,text.Text??"");
        if(cache is {} old&&old.Key==key)return old.Metrics;
        var result=Measure(text,size,key.Text,key.Dpi);cache=(key,result);return result;
    }
    private Metrics Measure(OpticallyCenteredText text,double size,string content,double dpi)
    {
        var face=new Typeface(text.TextFontFamily,text.TextFontStyle,text.TextFontWeight,FontStretches.Normal);
        var actual=new FormattedText(content,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,face,size,text.TextBrush,dpi);
        var cap=new FormattedText("H",CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,face,size,text.TextBrush,dpi);
        Rect ink=actual.BuildGeometry(new Point()).Bounds,reference=cap.BuildGeometry(new Point()).Bounds;
        double top=reference.IsEmpty?cap.Baseline-size:reference.Top;
        double overflow=ink.IsEmpty?0:Math.Max(0,Math.Max(top-ink.Top,ink.Bottom-cap.Baseline));
        return new(ink.IsEmpty?0:ink.Width,cap.Baseline-top,overflow+1);
    }
    private Metrics Envelope()
    {
        var key=Inputs(Value,ValueSize,"format:"+metricIndex);
        if(envelopeCache is {} old&&old.Key==key)return old.Metrics;
        var face=new Typeface(Value.TextFontFamily,Value.TextFontStyle,Value.TextFontWeight,FontStretches.Normal);
        FormattedText Format(string content)=>new(content,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,face,ValueSize,Value.TextBrush,key.Dpi);
        double Advance(string content)=>Format(content).WidthIncludingTrailingWhitespace;
        double digit=0,leftOverhang=0,rightOverhang=0;
        void IncludeOverhang(string content)
        {
            var text=Format(content);var ink=text.BuildGeometry(new Point()).Bounds;
            if(ink.IsEmpty)return;
            leftOverhang=Math.Max(leftOverhang,Math.Max(0,-ink.Left));
            rightOverhang=Math.Max(rightOverhang,Math.Max(0,ink.Right-text.WidthIncludingTrailingWhitespace));
        }
        foreach(char number in "0123456789")
        {
            string content=number.ToString();double advance=Advance(content);
            digit=Math.Max(digit,advance);IncludeOverhang(content);
        }
        // The sampler caps CPU/GPU at100.0 and RAM at99999MB. NET is not
        // capped: reserve five integer digits plus its decimal, then ellipsize
        // exceptional wider values locally while retaining the complete tooltip.
        int digits=metricIndex<2?4:metricIndex is 2 or 4?5:6;
        string separator=CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        string literal=metricIndex<2?separator+"%":metricIndex is 2 or 4?" MB":separator+" MB/s";
        IncludeOverhang(literal);
        var reserve=Measure(Value,ValueSize,"0123456789"+literal+"N/A",key.Dpi);
        var unavailable=Measure(Value,ValueSize,"N/A",key.Dpi);
        double width=Math.Max(digits*digit+Advance(literal)+leftOverhang+rightOverhang+2,Math.Max(Advance("N/A"),unavailable.Width));
        var result=reserve with{Width=width};envelopeCache=(key,result);return result;
    }
    internal double ScaleFor(Size available)
    {
        if(available.Width<=0||available.Height<=0)return 1;
        var label=Natural(Label,LabelSize,ref labelCache);var value=Envelope();
        double height=Math.Max(label.CapHeight,value.CapHeight)+2*(Math.Max(label.Overflow,value.Overflow)-1);
        return Math.Clamp(Math.Min(1,Math.Min(Math.Max(0,available.Width-Gap-4)/Math.Max(1,label.Width+value.Width),Math.Max(0,available.Height-2)/Math.Max(1,height))),.0001,1);
    }
    internal void SetScale(double scale)
    {
        if(RenderedScale==scale)return;RenderedScale=scale;InvalidateMeasure();
    }
    protected override Size MeasureOverride(Size available)
    {
        Label.TextFontSize=LabelSize*RenderedScale;Value.TextFontSize=ValueSize*RenderedScale;
        // Scaling linear outlines preserves their metrics; the common safety
        // pixel stays unscaled so descenders retain drawing room at every fit.
        Metrics Scale(Metrics m)=>new(m.Width*RenderedScale,m.CapHeight*RenderedScale,(m.Overflow-1)*RenderedScale+1);
        var natural=Natural(Value,ValueSize,ref valueCache);var envelope=Envelope();
        measuredLabel=Scale(Natural(Label,LabelSize,ref labelCache));measuredValue=Scale(natural with{Width=Math.Min(natural.Width,envelope.Width)});
        Value.Ellipsis=natural.Width>envelope.Width;
        Value.ToolTip=natural.Width>envelope.Width?Value.Text:null;
        Label.Measure(TextSize(measuredLabel));Value.Measure(TextSize(measuredValue));
        var reserve=Scale(envelope);
        double width=measuredLabel.Width+reserve.Width+Gap+4,height=Math.Max(measuredLabel.CapHeight,reserve.CapHeight)+2*Math.Max(measuredLabel.Overflow,reserve.Overflow);
        return new Size(double.IsFinite(available.Width)?Math.Min(width,available.Width):width,double.IsFinite(available.Height)?Math.Min(height,available.Height):height);
    }
    private static Size TextSize(Metrics m)=>new(m.Width+2,m.CapHeight+2*m.Overflow);
    protected override Size ArrangeOverride(Size size)
    {
        double cap=Math.Max(measuredLabel.CapHeight,measuredValue.CapHeight),baseline=(size.Height-cap)/2+cap;
        void Place(OpticallyCenteredText text,Metrics metrics,double x)=>text.Arrange(new Rect(new Point(x,baseline-metrics.CapHeight-metrics.Overflow),TextSize(metrics)));
        Place(Label,measuredLabel,0);Place(Value,measuredValue,Math.Max(measuredLabel.Width+Gap,size.Width-measuredValue.Width-2));
        return size;
    }
}

