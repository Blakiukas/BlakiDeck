using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ekrano_valdymas;

internal static class CopyFeedback
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button,Action> resets=new();
    internal static void Reset(Button button){if(resets.TryGetValue(button,out var reset))reset();}
    internal static Button Create(Func<string> text, Action<bool>? highlight=null, Action<string>? write=null)
    {
        var copyShape=Geometry.Parse("M 4,15 L 1,15 L 1,1 L 12,1 L 12,4 M 5,5 L 16,5 L 16,19 L 5,19 Z");
        var checkShape=Geometry.Parse("M 2,10 L 7,15 L 18,3");
        var glyph=new System.Windows.Shapes.Path{Data=copyShape,StrokeThickness=1.5,StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Stretch=Stretch.Uniform,Width=18,Height=18,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
        var button=MacroUi.Button("",(_,_)=>{});button.Content=glyph;button.Width=36;button.MinWidth=0;button.MinHeight=0;button.Height=30;button.Padding=new Thickness(4);button.Margin=new Thickness(2);button.ToolTip="Copy";
        glyph.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new System.Windows.Data.Binding(nameof(Control.Foreground)){Source=button});
        button.SizeChanged+=(_,_)=>{double size=Math.Min(24,Math.Min(button.ActualWidth,button.ActualHeight)*.6);glyph.Width=size;glyph.Height=size;};
        System.Windows.Automation.AutomationProperties.SetName(button,"Copy");
        var reset=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};
        void Restore(){reset.Stop();glyph.Data=copyShape;button.ToolTip="Copy";highlight?.Invoke(false);}
        resets.Add(button,Restore);
        reset.Tick+=(_,_)=>Restore();button.Unloaded+=(_,_)=>Restore();
        button.IsVisibleChanged+=(_,_)=>{if(!button.IsVisible)Restore();};
        button.Click+=(_,_)=>
        {
            try
            {
                string value=text();if(string.IsNullOrWhiteSpace(value)){button.ToolTip="No value to copy";return;}
                (write??Clipboard.SetText)(value);glyph.Data=checkShape;button.ToolTip="Copied";highlight?.Invoke(true);reset.Stop();reset.Start();
            }
            catch(System.Runtime.InteropServices.ExternalException){button.ToolTip="Clipboard is busy. Try again.";}
        };
        return button;
    }
}
