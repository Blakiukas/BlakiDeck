using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ekrano_valdymas;

[ContentProperty(nameof(Body))]
public sealed class MicrophoneMacroFrame : Grid
{
    public static readonly DependencyProperty TargetProperty=DependencyProperty.Register(nameof(Target),typeof(string),typeof(MicrophoneMacroFrame),new PropertyMetadata(null,(d,_)=>((MicrophoneMacroFrame)d).Refresh()));
    public string? Target{get=>(string?)GetValue(TargetProperty);set=>SetValue(TargetProperty,value);}
    private UIElement? body;
    public UIElement? Body{get=>body;set{if(body!=null)Children.Remove(body);body=value;if(value!=null){SetColumn(value,1);Children.Add(value);}}}
    private readonly ColumnDefinition leftColumn=new(),rightColumn=new();
    private readonly RowDefinition captionRow=new();
    private readonly Meter left=new(),right=new();
    private readonly OpticallyCenteredText caption=new(){TextFontFamily=new FontFamily("Segoe UI"),TextFontWeight=FontWeights.SemiBold,TextFontSize=9,TextBrush=Brushes.LightGray,FitToBox=true,IsHitTestVisible=false};
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(40)};
    private MicrophoneLevels.Lease? lease;private string? device;private bool enabled;
    internal MicrophoneLevels.Reading? DiagnosticReading;
    internal MicrophoneLevels.Reading LiveReading=>lease?.Read()??MicrophoneLevels.Reading.Missing;
    internal string ModeLabel=>caption.Text;
    internal (double Left,double Right) ShownLevels=>(left.Level,right.Level);
    internal object MeasureGeometry()
    {
        var border=Parent as Border;
        Rect Bounds(FrameworkElement e)=>new(e.TranslatePoint(new Point(),this),e.RenderSize);
        Rect Ink(FrameworkElement e)=>e.TransformToAncestor(this).TransformBounds(VisualTreeHelper.GetDrawing(e)?.Bounds??Rect.Empty);
        return new{frame=new Rect(RenderSize),outer=border==null?Rect.Empty:Bounds(border),radius=border?.CornerRadius,borderThickness=border?.BorderThickness,
            left=Bounds(left),right=Bounds(right),leftInk=Ink(left),rightInk=Ink(right),caption=Bounds(caption),captionInk=caption.GetRenderedInkBounds()};
    }
    public MicrophoneMacroFrame()
    {
        ColumnDefinitions.Add(leftColumn);ColumnDefinitions.Add(new());ColumnDefinitions.Add(rightColumn);
        RowDefinitions.Add(new());RowDefinitions.Add(captionRow);Children.Add(left);SetColumn(right,2);Children.Add(right);
        SetRowSpan(left,2);SetRowSpan(right,2);
        SetRow(caption,1);SetColumnSpan(caption,3);Children.Add(caption);
        Loaded+=(_,_)=>{MacroLibrary.Current.Changed+=Changed;Refresh();};
        Unloaded+=(_,_)=>{MacroLibrary.Current.Changed-=Changed;Stop();};
        IsVisibleChanged+=(_,_)=>Refresh();timer.Tick+=(_,_)=>Tick();Refresh();
    }
    private void Changed(object? sender,EventArgs e)=>Refresh();
    private void Stop(){timer.Stop();lease?.Dispose();lease=null;left.Level=right.Level=0;left.InvalidateVisual();right.InvalidateVisual();}
    private void Refresh()
    {
        var macro=MacroLibrary.IsReference(Target)?MacroLibrary.Current.Find(Target):null;
        var step=macro==null?null:MacroStates.Source(macro) is {Kind:"MicMute"} source?source:macro.Steps.Concat(macro.AlternateSteps).FirstOrDefault(s=>s.Kind=="MicMute");
        enabled=step!=null;string? next=step?.DeviceId??"";
        leftColumn.Width=rightColumn.Width=new GridLength(enabled?14:0);captionRow.Height=new GridLength(enabled?14:0);
        left.Visibility=right.Visibility=caption.Visibility=enabled?Visibility.Visible:Visibility.Collapsed;
        if(!enabled||!IsLoaded||!IsVisible){Stop();return;}
        if(lease==null||device!=next){Stop();device=next;if(DiagnosticState.Root==null||DiagnosticState.LiveMicrophone)lease=MicrophoneLevels.Acquire(device);}
        Tick();timer.Start();
    }
    internal void Tick()
    {
        var reading=DiagnosticReading??lease?.Read()??MicrophoneLevels.Reading.Missing;
        caption.Text=!reading.Available?"—":reading.Channels==1?"MONO":"STEREO";
        caption.TextBrush=reading.Muted?Brushes.Orange:Brushes.LightGray;
        ToolTip=!reading.Available?"Microphone unavailable":(reading.Channels==1?"Mono input — both meters show the same signal":"Stereo input — left / right channels")+(reading.Muted?" · muted":"");
        double l=reading.Available&&!reading.Muted?reading.Left:0,r=reading.Available&&!reading.Muted?(reading.Channels==1?reading.Left:reading.Right):0;
        left.Level=Math.Max(l,left.Level*.78);right.Level=reading.Channels==1?left.Level:Math.Max(r,right.Level*.78);
        if(reading.Muted||!reading.Available)left.Level=right.Level=0;
        left.InvalidateVisual();right.InvalidateVisual();
    }
    private sealed class Meter : FrameworkElement
    {
        internal double Level;
        internal Meter(){IsHitTestVisible=false;}
        protected override void OnRender(DrawingContext dc)
        {
            if(ActualHeight<4||ActualWidth<4)return;
            var frame=(MicrophoneMacroFrame)Parent;
            var border=frame.Parent as Border;
            bool isLeft=ReferenceEquals(this,frame.left);
            double top=5,bottom=ActualHeight-5;
            if(border!=null)
            {
                var origin=border.TranslatePoint(new Point(),this);
                double topRadius=isLeft?border.CornerRadius.TopLeft:border.CornerRadius.TopRight;
                double bottomRadius=isLeft?border.CornerRadius.BottomLeft:border.CornerRadius.BottomRight;
                top=origin.Y+topRadius+border.BorderThickness.Top/2;
                bottom=origin.Y+border.ActualHeight-bottomRadius-border.BorderThickness.Bottom/2;
            }
            const int count=12;const double gap=1.5;
            double h=Math.Max(0,(bottom-top-gap*(count-1))/count),w=Math.Min(7,ActualWidth-4),x=(ActualWidth-w)/2+(isLeft?2:-2);
            if(h<=0)return;
            double db=Level>0?20*Math.Log10(Level):-60;int lit=(int)Math.Ceiling(Math.Clamp((db+54)/54,0,1)*count);
            for(int i=0;i<count;i++)
            {
                var brush=i>=lit?new SolidColorBrush(Color.FromArgb(85,91,111,119)):new SolidColorBrush(i>=11?Color.FromRgb(248,70,55):i>=9?Color.FromRgb(255,166,0):Color.FromRgb(35,183,235));
                dc.DrawRoundedRectangle(brush,null,new Rect(x,top+(count-i-1)*(h+gap),w,h),.7,.7);
            }
        }
    }
}
