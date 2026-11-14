using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Ekrano_valdymas;
internal sealed class UsageTelemetryView:Grid
{
    private readonly OpticallyCenteredText[] labels=new OpticallyCenteredText[5],values=new OpticallyCenteredText[5];
    private readonly double[] widths=new double[5],labelWidths=new double[5];
    internal OpticallyCenteredText[] GeometryTextElements=>labels.Zip(values).SelectMany(p=>new[]{p.First,p.Second}).ToArray();
    internal double NaturalWidth {get;private set;}
    public bool SingleRow {get;set;}
    public UsageTelemetryView(){var names=new[]{"CPU:","GPU:","BD RAM:","NET:","WEB:"};for(int i=0;i<5;i++){labels[i]=Text(names[i]);values[i]=Text("N/A");Children.Add(labels[i]);Children.Add(values[i]);}SetValues("0.0","N/A","0","0");}
    private static OpticallyCenteredText Text(string value){var text=new OpticallyCenteredText{Text=value,TextFontFamily=new FontFamily("Segoe UI Semibold"),TextFontSize=16,InkAlignment=HorizontalAlignment.Left};text.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"UsageBrush");return text;}
    private double WidthOf(string text)=>new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe UI Semibold"),16,Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace+2;
    protected override Size MeasureOverride(Size available)
    {
        var reserves=new[]{"100.0%","100.0%","99999 MB","999.9 MB/s","99999 MB"};
        for(int i=0;i<5;i++){labelWidths[i]=WidthOf(labels[i].Text);widths[i]=labelWidths[i]+5+Math.Max(WidthOf(reserves[i]),WidthOf(values[i].Text));}
        NaturalWidth=widths.Sum()+40;
        double limit=SingleRow?NaturalWidth:Math.Min(available.Width,NaturalWidth),x=0,height=24;
        foreach(double width in widths){if(x>0&&x+width>limit+1){x=0;height+=24;}x+=width+10;}
        foreach(var child in GeometryTextElements)child.Measure(new Size(double.PositiveInfinity,24));
        return new Size(limit,height);
    }
    protected override Size ArrangeOverride(Size size)
    {
        double x=0,y=0;
        for(int i=0;i<5;i++){if(!SingleRow&&x>0&&x+widths[i]>size.Width+1){x=0;y+=24;}labels[i].Arrange(new Rect(x,y,labelWidths[i],24));values[i].Arrange(new Rect(x+labelWidths[i]+5,y,widths[i]-labelWidths[i]-5,24));x+=widths[i]+10;}
        return size;
    }
    internal void SetValues(string cpu,string gpu,string ram,string net)=>SetMemoryValues(cpu,gpu,ram,net,"N/A");
    internal void SetMemoryValues(string cpu,string gpu,string ram,string net,string web){values[0].Text=cpu.Equals("N/A",StringComparison.OrdinalIgnoreCase)?"N/A":Strip(cpu,"%")+"%";values[1].Text=gpu.Equals("N/A",StringComparison.OrdinalIgnoreCase)?"N/A":Strip(gpu,"%")+"%";values[2].Text=ram.Equals("N/A",StringComparison.OrdinalIgnoreCase)?"N/A":Strip(ram,"MB")+" MB";values[3].Text=net.Equals("N/A",StringComparison.OrdinalIgnoreCase)?"N/A":Strip(net,"MB/s")+" MB/s";values[4].Text=web=="N/A"?"N/A":Strip(web,"MB")+" MB";InvalidateMeasure();}
    private static string Strip(string value,string unit)=>Regex.Replace(value.Trim(),Regex.Escape(unit)+"$","",RegexOptions.IgnoreCase).Trim();
    internal void SetCanonicalText(string text){var matches=Regex.Matches(text,@"(?:CPU|GPU|RAM|NET|WEB):\s*.*?(?=\s{2,}(?:CPU|GPU|RAM|NET|WEB):|$)",RegexOptions.IgnoreCase);if(matches.Count is not (4 or 5))return;values[4].Text="N/A";for(int i=0;i<matches.Count;i++)values[i].Text=Regex.Replace(matches[i].Value,@"^[^:]+:\s*","",RegexOptions.IgnoreCase).Trim();InvalidateMeasure();}
    internal static bool VerifyStableGeometry(){var view=new UsageTelemetryView();view.Measure(new Size(700,48));view.Arrange(new Rect(0,0,700,48));var before=view.GeometryTextElements.Select(t=>t.TranslatePoint(new Point(),view)).ToArray();view.SetValues("100.0","100.0","99999","999.9");view.Measure(new Size(700,48));view.Arrange(new Rect(0,0,700,48));return before.SequenceEqual(view.GeometryTextElements.Select(t=>t.TranslatePoint(new Point(),view)));}
}


