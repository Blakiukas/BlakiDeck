using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Ekrano_valdymas;

internal sealed record AirthingsGaugeScale(string Sensor,string Title,double[] Ticks,double[] Stops)
{
    // All marks, shading anchors and the current pointer use this same map.
    // Explicit tick steps expand narrow threshold intervals without pretending
    // the entire scale has one uniform numerical step.
    internal double Position(double value)
    {
        if(value<=Ticks[0])return 0;
        if(value>=Ticks[^1])return 1;
        for(int i=1;i<Ticks.Length;i++)
            if(value<=Ticks[i])return Stops[i-1]+(Stops[i]-Stops[i-1])*(value-Ticks[i-1])/(Ticks[i]-Ticks[i-1]);
        return 1;
    }
    internal static readonly AirthingsGaugeScale[] All=
    [
        new("radonShortTermAvg","RADON",[0,50,100,125,150,300],[0,.20,.40,.60,.80,1]),
        new("temp","TEMPERATURE",[0,10,15,20,25,30,35],[0,.16666666666666666,.3333333333333333,.5,.6666666666666666,.8333333333333334,1]),
        new("humidity","HUMIDITY",[0,25,27.5,30,60,65,70,100],[0,1d/7,2d/7,3d/7,4d/7,5d/7,6d/7,1]),
        new("pressure","PRESSURE",[950,970,990,1010,1030,1050],[0,.20,.40,.60,.80,1]),
        new("co2","CO₂",[0,400,800,900,1000,2000,5000],[0,1d/6,2d/6,3d/6,4d/6,5d/6,1]),
        new("voc","VOC",[0,250,1125,2000,4000],[0,.25,.50,.75,1]),
        new("mold","MOLD RISK",[0,3,4,5,10],[0,.25,.5,.75,1]),
        new("pm25","PM₂.₅",[0,10,17.5,25,50],[0,.25,.5,.75,1])
    ];
    internal AirthingsGaugeScale WithLimits(AirthingsStatusSensor? options)
    {
        if(options is not {CustomLimits:true}||AirthingsStatusRules.Validate(options with{Included=true})!=null)return this;
        var limits=new[]{options.GoodLow,options.GoodHigh,options.YellowLow,options.YellowHigh,options.OrangeLow,options.OrangeHigh}.OfType<double>().Where(double.IsFinite).ToArray();
        if(limits.Length==0)return this;
        double span=Math.Max(1,limits.Max()-limits.Min()),low=Math.Min(Ticks[0],limits.Min()-span*.2),high=Math.Max(Ticks[^1],limits.Max()+span*.2);
        if(Ticks[0]>=0&&limits.Min()>=0)low=Math.Max(0,low);
        var ticks=new[]{low,high}.Concat(limits).Distinct().Order().ToArray();
        return this with{Ticks=ticks,Stops=Enumerable.Range(0,ticks.Length).Select(i=>i/(double)(ticks.Length-1)).ToArray()};
    }
}

// Only the selected room keeps its current windows. A missing sample retains an
// existing window; it never creates a window around an invented zero reading.
internal sealed class AirthingsGaugeZoom
{
    private readonly Dictionary<string,(double[] FullTicks,AirthingsGaugeScale Window)> windows=new();
    internal void Clear()=>windows.Clear();
    internal int Count=>windows.Count;
    internal AirthingsGaugeScale Resolve(AirthingsGaugeScale full,AirthingsReading reading)
    {
        string sensor=AirthingsStatusRules.Canonical(full.Sensor);
        bool retained=windows.TryGetValue(sensor,out var entry)&&entry.FullTicks.SequenceEqual(full.Ticks);
        double? metric=AirthingsStatusRules.MetricValue(reading);
        if(metric is not double value)return retained?entry.Window:full;
        if(retained&&value>=entry.Window.Ticks[0]&&value<=entry.Window.Ticks[^1])return entry.Window;
        double span=sensor switch
        {
            "radonshorttermavg" or "radonlongtermavg" or "radon"=>100,
            "temp" or "pm25"=>10,"humidity" or "pressure"=>20,
            "co2" or "voc"=>500,"mold"=>2,_=>full.Ticks[^1]-full.Ticks[0]
        };
        // Retain distinct finite ticks even for an unusually large but valid
        // measurement. Ordinary readings use the fixed sensor-unit spans above.
        span=Math.Max(span,Math.Abs(value)*.02);
        double step=span/4,low=Math.Floor((value-span/2)/step)*step,high=low+span;
        if(sensor!="temp"&&low<0){low=0;high=span;}
        double maximum=sensor=="humidity"?100:sensor=="mold"?10:double.PositiveInfinity;
        if(high>maximum){high=maximum;low=maximum-span;}
        var ticks=Enumerable.Range(0,5).Select(i=>low+i*step).ToArray();
        if(ticks.Any(n=>!double.IsFinite(n))||ticks.Zip(ticks.Skip(1)).Any(pair=>pair.First>=pair.Second))return full;
        var window=full with{Ticks=ticks,Stops=[0,.25,.5,.75,1]};
        windows[sensor]=(full.Ticks,window);
        return window;
    }
}

internal sealed class AirthingsGaugeView:UniformGrid
{
    internal AirthingsGaugeColumn[] Gauges {get;}
    internal AirthingsGaugeView(AirthingsDevice device,AirthingsDisplay display,AirthingsStatusRoom? limits=null,AirthingsGaugeZoom? zoom=null)
    {
        Columns=6;Rows=1;Margin=new Thickness(0,1,0,0);
        bool stale=AirthingsQuality.IsStale(device.Recorded);
        var supported=AirthingsStatusRules.Sensors(device).Select(AirthingsStatusRules.Canonical).ToHashSet();
        Gauges=AirthingsGaugeScale.All.Where(scale=>supported.Contains(AirthingsStatusRules.Canonical(scale.Sensor))||
            scale.Sensor=="radonShortTermAvg"&&(supported.Contains("radon")||supported.Contains("radonlongtermavg"))).Select(scale=>
        {
            string key=AirthingsStatusRules.Canonical(scale.Sensor);
            var reading=device.Readings.FirstOrDefault(r=>AirthingsStatusRules.Canonical(r.SensorType)==key);
            if(reading==null&&key=="radonshorttermavg")
                reading=device.Readings.FirstOrDefault(r=>AirthingsStatusRules.Canonical(r.SensorType) is "radon" or "radonlongtermavg");
            reading??=new AirthingsReading(scale.Sensor,null,AirthingsStatusRules.Unit(scale.Sensor));
            var configured=limits?.Sensors?.FirstOrDefault(s=>AirthingsStatusRules.Canonical(s.Sensor)==AirthingsStatusRules.Canonical(reading.SensorType));
            var full=scale.WithLimits(configured);
            return new AirthingsGaugeColumn(zoom?.Resolve(full,reading)??full,reading,display,stale,configured){Margin=new Thickness(2,0,2,0)};
        }).ToArray();
        foreach(var gauge in Gauges)Children.Add(gauge);
    }
    internal FrameworkElement Viewport()
    {
        if(Gauges.Length<=6)return this;
        // Keep the established one-sixth cell width even on seven-sensor
        // devices. Overflow is scrollable instead of shrinking every gauge.
        Columns=Gauges.Length;
        var scroll=new ScrollViewer{Content=this,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalContentAlignment=HorizontalAlignment.Left};
        scroll.SizeChanged+=(_,_)=>Width=Math.Max(0,scroll.ActualWidth)*Gauges.Length/6d;
        return scroll;
    }
}

internal sealed class AirthingsGaugeColumn:Panel
{
    private readonly AirthingsGaugeScale scale;
    private readonly AirthingsReading reading;
    private readonly AirthingsDisplay display;
    private readonly AirthingsStatusSensor sensorOptions;
    private readonly bool stale;
    private readonly double? metric;
    private readonly OpticallyCenteredText heading,period,unit,current,currentUnit;
    private readonly OpticallyCenteredText[] ticks;
    private readonly Canvas drawingLayer=new(){IsHitTestVisible=false,ClipToBounds=true};
    private readonly Border outerFrame,barShape,bubbleShape;
    private readonly Line pointerShape;
    private readonly Line[] tickMarks;
    private readonly AirthingsReadingLayoutSlot layoutSlot=new();
    private readonly Dictionary<UIElement,Rect> childBounds=new();
    private Size measuredSize;
    private int measurePasses,arrangePasses;
    private Brush scaleBrush=Brushes.Gray;
    private Rect bar,labelBox;
    private double pointerY;
    private static readonly Brush Frame=Frozen(Color.FromArgb(50,255,255,255));
    private static readonly Brush LabelFrame=Frozen(Color.FromArgb(80,255,255,255));
    private static readonly Brush LabelBackground=Frozen(Color.FromArgb(110,0,0,0));
    private static Brush Frozen(Color color){var brush=new SolidColorBrush(color);brush.Freeze();return brush;}
    internal IReadOnlyList<OpticallyCenteredText> TextsForProbe=>Children.OfType<OpticallyCenteredText>().ToArray();
    internal object GradientForProbe=>new{smooth=scaleBrush is LinearGradientBrush,frozen=scaleBrush.IsFrozen,
        anchors=scaleBrush is LinearGradientBrush gradient?gradient.GradientStops.Select(stop=>new{position=stop.Offset,color=stop.Color.ToString()}).ToArray():null};
    internal object GeometryForProbe=>new{sensor=reading.SensorType,value=reading.Value,metric,stale,bar,pointerY,
        position=metric is double value?scale.Position(value):(double?)null,offScale=metric is double n&&(n<scale.Ticks[0]||n>scale.Ticks[^1]),
        ticks=scale.Ticks.Select((value,index)=>new{value,position=scale.Stops[index],y=bar.Bottom-scale.Stops[index]*bar.Height}).ToArray(),labelBox};
    internal object LayoutForProbe=>new{measurePasses,arrangePasses};
    internal AirthingsGaugeColumn(AirthingsGaugeScale scale,AirthingsReading reading,AirthingsDisplay display,bool stale,AirthingsStatusSensor? custom=null)
    {
        this.scale=scale;this.reading=reading;this.display=display;this.stale=stale;metric=AirthingsStatusRules.MetricValue(reading);
        sensorOptions=custom is {CustomLimits:true}&&AirthingsStatusRules.Validate(custom with{Included=true})==null?custom:custom is {CustomLimits:false}?custom:new(reading.SensorType);
        ClipToBounds=true;UseLayoutRounding=false;drawingLayer.UseLayoutRounding=false;
        Children.Add(drawingLayer);
        outerFrame=new Border{BorderBrush=Frame,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Tag="AirthingsGaugeCardFrame"};drawingLayer.Children.Add(outerFrame);
        barShape=new Border{BorderBrush=LabelFrame,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4),Tag="AirthingsGaugeBar"};drawingLayer.Children.Add(barShape);
        bubbleShape=new Border{Background=LabelBackground,BorderBrush=LabelFrame,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4),Tag="AirthingsGaugeValueFrame"};drawingLayer.Children.Add(bubbleShape);
        pointerShape=new Line{Stroke=stale?Brushes.Gray:Brushes.White,StrokeThickness=3,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Tag="AirthingsGaugePointer",Visibility=metric.HasValue?Visibility.Visible:Visibility.Collapsed};drawingLayer.Children.Add(pointerShape);
        FontFamily Face(string name)=>name=="Teko"?(FontFamily)Application.Current.FindResource("TekoFont"):new FontFamily(name);
        Brush Color(string? value,string fallback)=>value==null?(Brush)Application.Current.FindResource(fallback):Frozen(CompactColorPicker.Parse(value,Colors.White));
        OpticallyCenteredText Text(string text,double size,string font,Brush color,FontWeight weight)
        {
            var result=new OpticallyCenteredText{Text=text,TextFontFamily=Face(font),TextFontSize=size,TextFontWeight=weight,
                TextBrush=color,FitToBox=true,CompactLineBox=true,CenterCapToBaseline=true,ClipToBounds=true,Ellipsis=false,UseLayoutRounding=false};Children.Add(result);return result;
        }
        var labelColor=Color(display.UsesQualityColors?null:display.LabelColor,"ModuleTitleBrush");
        var axisColor=Color(display.UsesQualityColors?null:display.AxisColor,"PanelTextBrush");
        heading=Text(scale.Title,Math.Clamp(display.LabelSize,8,80),display.LabelFont,labelColor,FontWeights.SemiBold);
        string periodText=reading.SensorType.ToLowerInvariant() switch{"radonshorttermavg"=>"24H AVG","radonlongtermavg"=>"LONG TERM",_=>""};
        period=Text(periodText,Math.Clamp(display.IntervalSize,8,32),display.AxisFont,axisColor,FontWeights.Normal);
        string canonical=AirthingsStatusRules.Canonical(reading.SensorType);
        var converted=display.Convert(canonical,metric??0,AirthingsStatusRules.Unit(canonical));
        unit=Text(converted.Unit,Math.Clamp(display.UnitSize,8,80),display.UnitFont,axisColor,FontWeights.Normal);
        bool available=metric.HasValue;
        var valueColor=!available||stale?Brushes.Gray:display.UsesQualityColors?AirthingsStatusRules.Brush(AirthingsStatusRules.Evaluate(reading,sensorOptions)):Color(display.ValueColor,"PanelTextBrush");
        current=Text(available?converted.Value.ToString("0.##",CultureInfo.CurrentCulture):"N/A",Math.Clamp(display.ValueSize,8,80),display.ValueFont,
            valueColor,FontWeights.SemiBold);
        currentUnit=Text(available?converted.Unit:"",Math.Clamp(display.UnitSize,8,80),display.UnitFont,
            valueColor,FontWeights.Normal);
        ticks=scale.Ticks.Select(value=>Text(display.Convert(canonical,value,AirthingsStatusRules.Unit(canonical)).Value.ToString("0.##",CultureInfo.CurrentCulture),
            Math.Clamp(display.AxisSize,8,24),display.AxisFont,axisColor,FontWeights.Normal)).ToArray();
        foreach(var tick in ticks)tick.InkAlignment=HorizontalAlignment.Right;
        tickMarks=ticks.Select(tick=>new Line{Stroke=tick.TextBrush,StrokeThickness=1,Tag="AirthingsGaugeTick"}).ToArray();foreach(var mark in tickMarks)drawingLayer.Children.Add(mark);
        BuildBands();
        barShape.Background=scaleBrush;
        string state=!available?"No valid current measurement.":stale?"Older sample — current air quality is not classified.":"Current device measurement.";
        string classification=!available||stale?"":AirthingsStatusRules.Name(AirthingsStatusRules.Evaluate(reading,sensorOptions));
        ToolTip=state+(classification.Length>0?" "+classification+".":"")+"\nTick labels define the numerical scale. Values beyond the displayed range keep their actual number; the pointer stops at the end."
            +"\nShort color transitions blend at the existing category limits; they do not change those limits."
            +(periodText.Length>0?"\nRadon uses the reported "+periodText.ToLowerInvariant()+" averaging period.":"")
            +(!AirthingsQuality.IsSupported(reading.SensorType)&&!sensorOptions.CustomLimits?"\nNo air-quality limits are configured for this parameter.":"");
        System.Windows.Automation.AutomationProperties.SetName(this,scale.Title+" · "+current.Text+" "+converted.Unit+(stale?" · older sample":""));
    }
    private void BuildBands()
    {
        var boundaries=new List<double>{scale.Ticks[0],scale.Ticks[^1]};
        var options=sensorOptions.CustomLimits?sensorOptions:AirthingsStatusRules.SuggestedCustom(new(reading.SensorType,sensorOptions.Included));
        foreach(var limit in new[]{options.GoodLow,options.GoodHigh,options.YellowLow,options.YellowHigh,options.OrangeLow,options.OrangeHigh})
            if(limit is double n&&n>scale.Ticks[0]&&n<scale.Ticks[^1])boundaries.Add(n);
        // Custom limits may extend the displayed scale beyond a sensor's valid
        // metric range. Keep those unclassified spans out of the color bands.
        string sensor=AirthingsStatusRules.Canonical(reading.SensorType);
        if(sensor is "radonshorttermavg" or "radonlongtermavg" or "radon" or "pressure" or "co2" or "voc" or "pm25" or "humidity" or "mold")
            foreach(double edge in sensor=="humidity"?new[]{0d,100d}:sensor=="mold"?new[]{0d,10d}:new[]{0d})
                if(edge>scale.Ticks[0]&&edge<scale.Ticks[^1])boundaries.Add(edge);
        var sorted=boundaries.Distinct().Order().ToArray();
        var gradient=new LinearGradientBrush{StartPoint=new Point(0,1),EndPoint=new Point(0,0)};
        Color ColorAt(double value)
        {
            var metricReading=reading with{Value=value,Unit=AirthingsStatusRules.Unit(reading.SensorType)};
            var level=stale||metric==null?AirthingsStatusLevel.Unknown:AirthingsStatusRules.Evaluate(metricReading,sensorOptions);
            return ((SolidColorBrush)AirthingsStatusRules.Brush(level)).Color;
        }
        var positions=sorted.Select(scale.Position).ToArray();
        var colors=Enumerable.Range(0,sorted.Length-1).Select(i=>ColorAt(sorted[i]/2+sorted[i+1]/2)).ToArray();
        gradient.GradientStops.Add(new GradientStop(colors[0],0));
        for(int i=1;i<sorted.Length-1;i++)
        {
            if(colors[i-1]==colors[i])continue;
            // Keep each category's color across its band. Blend only around
            // the actual boundary, using the same nonuniform map as the ticks
            // and pointer. The quarter-band cap keeps narrow custom bands distinct.
            double halfWidth=Math.Min(.015,Math.Min(positions[i]-positions[i-1],positions[i+1]-positions[i])/4);
            gradient.GradientStops.Add(new GradientStop(colors[i-1],positions[i]-halfWidth));
            gradient.GradientStops.Add(new GradientStop(colors[i],positions[i]+halfWidth));
        }
        gradient.GradientStops.Add(new GradientStop(colors[^1],1));
        if(gradient.GradientStops.All(stop=>stop.Color==Colors.Gray))
        {gradient.GradientStops.Clear();gradient.GradientStops.Add(new GradientStop(Color.FromRgb(82,86,90),0));gradient.GradientStops.Add(new GradientStop(Color.FromRgb(160,164,168),1));}
        gradient.Freeze();scaleBrush=gradient;
    }
    protected override Size MeasureOverride(Size available)
    {
        measurePasses++;
        var desired=new Size(double.IsFinite(available.Width)?available.Width:96,double.IsFinite(available.Height)?available.Height:230);
        measuredSize=layoutSlot.MeasureSize(available,desired);
        MeasureGeometry(measuredSize);
        foreach(var (child,bounds) in childBounds)child.Measure(bounds.Size);
        // Canvas must measure after its shapes have their actual dimensions and
        // coordinates. Measuring the initially empty shapes and growing them in
        // Arrange leaves WPF's tiny natural-size layout clips on the paint.
        drawingLayer.Measure(measuredSize);
        return desired;
    }
    private void MeasureGeometry(Size size)
    {
        childBounds.Clear();
        void PlaceText(UIElement child,Rect bounds)=>childBounds.Add(child,bounds);
        double width=Math.Max(1,size.Width),height=Math.Max(1,size.Height);
        double HeaderHeight(OpticallyCenteredText text,string reference)
        {
            var metrics=text.GetValueRowMetrics(text.TextFontSize,reference);
            return metrics.Reference+2*metrics.BottomOverflow+8;
        }
        // Reserve the same rows across all gauges, including the blank radon
        // period row, so unit descenders never move the aligned bar tops.
        double headingHeight=HeaderHeight(heading,"H"),periodHeight=HeaderHeight(period,"H"),unitHeight=Math.Max(HeaderHeight(current,"0123456789"),HeaderHeight(currentUnit,"Bq/m³ ppm µg/m³ °C"));
        double headerScale=Math.Min(1,Math.Max(1,height*.45-8)/Math.Max(1,headingHeight+periodHeight+unitHeight));
        headingHeight*=headerScale;periodHeight*=headerScale;unitHeight*=headerScale;
        double head=8+headingHeight+periodHeight+unitHeight;
        PlaceText(heading,new Rect(4,4,Math.Max(1,width-8),headingHeight));
        PlaceText(period,new Rect(4,4+headingHeight,Math.Max(1,width-8),periodHeight));
        // One stable readout above the bar. Value and unit share the visible
        // bottom, and use one fit factor when the fixed-width cell is narrow.
        unit.Visibility=Visibility.Collapsed;
        var valueMetrics=current.GetNaturalMetrics(current.TextFontSize);
        var unitMetrics=currentUnit.GetNaturalMetrics(currentUnit.TextFontSize);
        double gap=string.IsNullOrEmpty(currentUnit.Text)?0:3;
        double rowWidth=Math.Max(1,width-8);
        double fit=Math.Min(headerScale,Math.Min(1,rowWidth/Math.Max(1,valueMetrics.Width+unitMetrics.Width+gap+16)));
        double valueWidth=valueMetrics.Width*fit+8,unitWidth=unitMetrics.Width*fit+8;
        double rowX=(width-valueWidth-unitWidth-gap)/2,rowY=4+headingHeight+periodHeight;
        double reference=Math.Max(valueMetrics.Reference,unitMetrics.Reference)*fit;
        double baseline=rowY+(unitHeight-reference)/2+reference;
        double valueExtent=valueMetrics.Extent*fit+8,unitExtent=unitMetrics.Extent*fit+8;
        labelBox=new Rect(4,rowY,rowWidth,unitHeight);
        PlaceText(current,new Rect(rowX,baseline-(valueMetrics.Reference+valueMetrics.BottomOverflow)*fit-4,valueWidth,valueExtent));
        PlaceText(currentUnit,new Rect(rowX+valueWidth+gap,baseline-(unitMetrics.Reference+unitMetrics.BottomOverflow)*fit-4,unitWidth,unitExtent));
        double left=Math.Max(24,width*.34),barWidth=Math.Clamp(width*.15,10,18),top=head+8,bottom=Math.Max(top+1,height-14);
        bar=new Rect(left,top,barWidth,bottom-top);
        double tickHeight=Math.Min(20,Math.Max(12,(bottom-top)/Math.Max(1,ticks.Length-1)*.85));
        for(int i=0;i<ticks.Length;i++)PlaceText(ticks[i],new Rect(2,bar.Bottom-scale.Stops[i]*bar.Height-tickHeight/2,Math.Max(1,left-8),tickHeight));
        pointerY=metric is double value?bar.Bottom-scale.Position(value)*bar.Height:bar.Top+bar.Height/2;
        bubbleShape.Visibility=Visibility.Collapsed;
        Place(outerFrame,new Rect(.5,.5,Math.Max(0,width-1),Math.Max(0,height-1)));Place(barShape,bar);Place(bubbleShape,labelBox);
        pointerShape.X1=bar.Left-1;pointerShape.X2=bar.Right+2;pointerShape.Y1=pointerShape.Y2=pointerY;
        for(int i=0;i<tickMarks.Length;i++){double y=bar.Bottom-scale.Stops[i]*bar.Height;tickMarks[i].X1=bar.Left-5;tickMarks[i].X2=bar.Left-2;tickMarks[i].Y1=tickMarks[i].Y2=y;}
        drawingLayer.InvalidateMeasure();
    }
    protected override Size ArrangeOverride(Size size)
    {
        arrangePasses++;
        // No child measurement or geometry/property changes during Arrange:
        // an unchanged slot remains measure-valid instead of restarting layout.
        foreach(var (child,bounds) in childBounds)child.Arrange(bounds);
        drawingLayer.Arrange(new Rect(new Point(),measuredSize));
        if(layoutSlot.ObserveArrange(size))InvalidateMeasure();
        return size;
    }
    private static void Place(FrameworkElement shape,Rect bounds)
    {
        Canvas.SetLeft(shape,bounds.X);Canvas.SetTop(shape,bounds.Y);shape.Width=bounds.Width;shape.Height=bounds.Height;
    }
}
