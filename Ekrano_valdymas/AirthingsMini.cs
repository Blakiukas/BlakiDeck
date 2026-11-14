using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Ekrano_valdymas;

internal sealed record AirthingsMiniOptions(string? Account=null,string? Serial=null,int Cells=1,string? Reading1=null,string? Reading2=null,double LabelSize=11,double? ValueSize=null,double RoomSize=14,double StatusSize=10,bool AutomaticColors=true,string? LabelColor=null,string? ValueColor=null,string? RoomColor=null,string? StatusColor=null,bool HideRoom=false,string? Reading3=null,string? Reading4=null,int SensorSlots=0,bool IconRows=true,double IconSize=24,double IconValueSize=24)
{
    public int Span=>Cells==2?2:1;
}

// Targets retain the room and width inside the existing per-placement shortcut storage.
public sealed class AirthingsMini : UserControl
{
    internal const string Prefix="airthings-mini:";
    internal const string ReservedTarget="airthings-mini-reserved";
    public static readonly DependencyProperty TargetProperty=DependencyProperty.Register(nameof(Target),typeof(string),typeof(AirthingsMini),new PropertyMetadata(null,(d,e)=>((AirthingsMini)d).TargetChanged(e.OldValue as string,e.NewValue as string)));
    public string? Target {get=>(string?)GetValue(TargetProperty);set=>SetValue(TargetProperty,value);}
    internal static bool IsTarget(string? target)=>target?.StartsWith(Prefix,StringComparison.Ordinal)==true;
    internal static string Encode(AirthingsMiniOptions options)=>Prefix+Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(options with{Cells=options.Span})));
    internal static AirthingsMiniOptions Decode(string? target)
    {try{return IsTarget(target)?JsonSerializer.Deserialize<AirthingsMiniOptions>(Encoding.UTF8.GetString(Convert.FromBase64String(target![Prefix.Length..])))??new():new();}catch{return new();}}
    internal static int Span(string? target)=>IsTarget(target)?Decode(target).Span:1;
    internal static AirthingsDevice? Device(AirthingsMiniOptions options)=>options.Serial==null?AirthingsService.Shared.Devices.FirstOrDefault(d=>d.Supported.Any(AirthingsQuality.IsSupported))??AirthingsService.Shared.Devices.FirstOrDefault():AirthingsService.Shared.Devices.FirstOrDefault(d=>d.Serial==options.Serial&&(options.Account==null||d.Account==options.Account));
    internal static bool TryActivate(string? target,Window? owner)
    {return IsTarget(target);}

    private readonly OpticallyCenteredText room=Text(11),footer=Text(10);
    private readonly Grid samples=new();
    private readonly Grid root=new(){Margin=new Thickness(2,1,2,1)};
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(30)};
    private bool subscribed;
    private bool? compactLayout;
    internal long RefreshCountForProbe {get;private set;}
    internal long SizeChangesForProbe {get;private set;}
    public AirthingsMini()
    {
        IsHitTestVisible=false;
        // Short metadata rows have their own cap/baseline budget; FitToBox's
        // general padding would shrink an 11 DIP room caption to 4 DIP of ink.
        room.FitToBox=footer.FitToBox=false;room.CompactLineBox=footer.CompactLineBox=true;
        root.RowDefinitions.Add(new(){Height=new GridLength(15)});root.RowDefinitions.Add(new());root.RowDefinitions.Add(new(){Height=new GridLength(13)});
        room.Tag="AirthingsMiniRoom";footer.Tag="AirthingsMiniStatus";root.Children.Add(room);root.Children.Add(samples);Grid.SetRow(samples,1);root.Children.Add(footer);Grid.SetRow(footer,2);Content=root;
        timer.Tick+=(_,_)=>Refresh();
        SizeChanged+=(_,_)=>
        {
            SizeChangesForProbe++;
            if(compactLayout!=IsCompactLayout)Refresh();
            else FitMetadataLayout(Decode(Target));
        };
        Loaded+=(_,_)=>UpdateActivity();IsVisibleChanged+=(_,_)=>UpdateActivity();
        Unloaded+=(_,_)=>StopActivity();
    }
    private void TargetChanged(string? oldTarget,string? newTarget)
    {
        var before=Decode(oldTarget);var after=Decode(newTarget);
        if(IsLoaded&&IsVisible&&IsTarget(oldTarget)&&IsTarget(newTarget)&&after with{RoomSize=before.RoomSize,HideRoom=before.HideRoom}==before){FitMetadataLayout(after);return;}
        UpdateActivity();
    }
    private void StopActivity(){if(subscribed){AirthingsService.Shared.Changed-=Changed;AirthingsDisplay.Changed-=Changed;subscribed=false;}timer.Stop();}
    private void UpdateActivity()
    {
        if(!IsLoaded||!IsVisible||!IsTarget(Target)){StopActivity();return;}
        if(!subscribed){AirthingsService.Shared.Changed+=Changed;AirthingsDisplay.Changed+=Changed;subscribed=true;AirthingsService.Shared.Start();}timer.Start();Refresh();
    }
    internal bool IsActiveForProbe=>subscribed&&timer.IsEnabled;
    private static OpticallyCenteredText Text(double size)=>new(){TextFontFamily=new FontFamily("Segoe UI"),TextFontWeight=FontWeights.SemiBold,TextFontSize=size,TextBrush=Brushes.White,CenterCapToBaseline=true,FitToBox=true,Ellipsis=true,ClipToBounds=false};
    private void Changed(){if(Dispatcher.CheckAccess())Refresh();else Dispatcher.BeginInvoke(new Action(Refresh));}
    internal static string SensorLabel(string type)=>type.ToLowerInvariant() switch{"radonshorttermavg"=>"RADON · 24H","radonlongtermavg"=>"RADON · LONG","co2"=>"CO₂","pm25" or "pm2.5"=>"PM2.5","humidity"=>"HUMIDITY","mold" or "moldrisk"=>"MOLD RISK",_=>type.ToUpperInvariant()};
    private static int Priority(string type)=>type.ToLowerInvariant() switch{"radonshorttermavg"=>0,"co2"=>1,"pm25" or "pm2.5"=>2,"voc"=>3,"humidity"=>4,"radonlongtermavg"=>5,_=>6};
    internal static AirthingsReading[] SelectReadings(AirthingsDevice device,int count)
    {
        var readings=device.Supported.Where(AirthingsQuality.IsSupported).Select(type=>device.Readings.FirstOrDefault(r=>r.SensorType.Equals(type,StringComparison.OrdinalIgnoreCase))??new AirthingsReading(type,null,""));
        return readings.OrderByDescending(r=>(int)AirthingsQuality.Evaluate(r)).ThenBy(r=>Priority(r.SensorType)).Take(Math.Clamp(count,1,4)).ToArray();
    }
    internal static AirthingsReading[] SelectReadings(AirthingsDevice device,AirthingsMiniOptions options)
    {
        var automatic=SelectReadings(device,4);var selected=new List<AirthingsReading>();
        foreach(var type in new[]{options.Reading1,options.Reading2,options.Reading3,options.Reading4}.Take(options.SensorSlots==4?4:options.Span)){
            if(type=="__none__")continue;
            var reading=string.IsNullOrWhiteSpace(type)?automatic.FirstOrDefault(r=>selected.All(s=>s.SensorType!=r.SensorType)):device.Readings.FirstOrDefault(r=>r.SensorType.Equals(type,StringComparison.OrdinalIgnoreCase))??new AirthingsReading(type,null,"");
            if(reading!=null)selected.Add(reading);
        }
        return selected.ToArray();
    }
    private static double SafeSize(double size,double fallback)=>double.IsFinite(size)?Math.Clamp(size,8,80):fallback;
    private static void FitMetadata(OpticallyCenteredText text,double requested,double width,double height)
    {
        var face=new Typeface(text.TextFontFamily,text.TextFontStyle,text.TextFontWeight,FontStretches.Normal);
        FormattedText Format(string content)=>new(content,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,face,requested,Brushes.White,VisualTreeHelper.GetDpi(text).PixelsPerDip);
        var actual=Format(text.Text);var cap=Format("H");var ink=actual.BuildGeometry(new Point()).Bounds;double top=cap.BuildGeometry(new Point()).Bounds.Top;
        double overflow=ink.IsEmpty?0:Math.Max(0,Math.Max(top-ink.Top,ink.Bottom-cap.Baseline));
        double drawHeight=cap.Baseline-top+overflow*2+2;
        text.TextFontSize=requested*Math.Clamp(Math.Min(Math.Max(0,width-4)/Math.Max(1,actual.WidthIncludingTrailingWhitespace),Math.Max(0,height)/Math.Max(1,drawHeight)),.01,1);
    }
    private static Brush Manual(string? color,Brush fallback)=>color==null?fallback:new SolidColorBrush(CompactColorPicker.Parse(color,Colors.White));
    internal static string ValueText(AirthingsReading reading)
    {if(reading.Value is not double value||!double.IsFinite(value))return "N/A";if(reading.SensorType.StartsWith("radon",StringComparison.OrdinalIgnoreCase)&&reading.Unit.Equals("pCi/L",StringComparison.OrdinalIgnoreCase))reading=reading with{Value=value*37,Unit="bq"};var converted=AirthingsDisplay.Load().Convert(reading);return converted.Value.ToString("0.##",CultureInfo.CurrentCulture)+" "+converted.Unit;}
    internal static string Age(DateTimeOffset? recorded,DateTimeOffset? now=null)
    {if(recorded==null)return "No sample";var age=(now??DateTimeOffset.UtcNow)-recorded.Value;if(age<TimeSpan.Zero)age=TimeSpan.Zero;return age.TotalDays>=1?$"{(int)age.TotalDays}d":age.TotalHours>=1?$"{(int)age.TotalHours}h":$"{(int)age.TotalMinutes}m";}
    internal static string Description(string? target)
    {var device=Device(Decode(target));return device==null?"Airthings · selected room unavailable\nRight-click to edit":$"{device.Home} · {device.Name}\n"+string.Join("\n",device.Readings.Select(r=>$"{SensorLabel(r.SensorType)} {ValueText(r)}"))+"\nSample: "+(device.Recorded?.ToLocalTime().ToString("g")??"unknown")+(AirthingsQuality.IsStale(device.Recorded)?" · old data":"")+"\nRight-click to edit";}
    private bool IsCompactLayout=>ActualHeight>0&&ActualHeight<65;
    private void FitMetadataLayout(AirthingsMiniOptions options)
    {
        bool compact=IsCompactLayout;
        double requested=SafeSize(options.RoomSize,14);
        double roomHeight=options.HideRoom?0:Math.Min(Math.Max(compact?12:15,requested*1.2),Math.Max(1,ActualHeight*.24));
        double statusHeight=0;
        root.RowDefinitions[0].Height=new GridLength(roomHeight);root.RowDefinitions[2].Height=new GridLength(statusHeight);
        room.Visibility=options.HideRoom?Visibility.Collapsed:Visibility.Visible;footer.Visibility=Visibility.Collapsed;
        if(!options.HideRoom)FitMetadata(room,requested,ActualWidth-4,roomHeight);
    }
    internal void Refresh()
    {
        if(!IsLoaded||!IsVisible||!IsTarget(Target))return;
        RefreshCountForProbe++;
        var options=Decode(Target);var device=Device(options);samples.Children.Clear();samples.ColumnDefinitions.Clear();samples.RowDefinitions.Clear();
        bool compact=IsCompactLayout;compactLayout=compact;
        room.Text=device==null?"AIRTHINGS":string.IsNullOrWhiteSpace(device.Name)?device.Home:device.Name;room.TextBrush=Brushes.White;
        if(device==null){room.TextBrush=Brushes.Gray;var unavailable=Text(22);unavailable.Text="N/A";unavailable.TextBrush=Brushes.Gray;samples.Children.Add(unavailable);footer.Text=AirthingsService.Shared.HasCredentials?"Unavailable":"Setup required";footer.TextBrush=Brushes.Gray;FitMetadataLayout(options);SetToolTip("Airthings · "+AirthingsService.Shared.Status);return;}
        bool stale=AirthingsQuality.IsStale(device.Recorded);room.TextBrush=stale?Brushes.Gray:options.AutomaticColors?Brushes.White:Manual(options.RoomColor,Brushes.White);var readings=SelectReadings(device,options);
        if(readings.Length==0){readings=[new("air quality",null,"")];}
        bool iconRows=options.IconRows&&options.Span==2;
        int columns=iconRows?2:readings.Length>2?2:readings.Length;
        for(int col=0;col<columns;col++)samples.ColumnDefinitions.Add(new());
        for(int row=0;row<(iconRows?2:(readings.Length+columns-1)/columns);row++)samples.RowDefinitions.Add(new());
        var iconGrid=iconRows?new AirthingsIconReadings(SafeSize(options.IconValueSize,24),SafeSize(options.IconSize,24)):null;
        if(iconGrid!=null){samples.Children.Add(iconGrid);Grid.SetColumnSpan(iconGrid,2);Grid.SetRowSpan(iconGrid,2);}
        for(int i=0;i<readings.Length;i++)
        {

            var label=Text(SafeSize(options.LabelSize,11));label.Text=SensorLabel(readings[i].SensorType);label.TextBrush=stale?Brushes.Gray:options.AutomaticColors?Brushes.White:Manual(options.LabelColor,Brushes.White);label.Tag="AirthingsMiniLabel";
            var value=Text(SafeSize(options.ValueSize??(compact?19:options.Span==2?25:23),23));value.Text=ValueText(readings[i]);value.Tag="AirthingsMiniValue";var level=AirthingsQuality.Evaluate(readings[i],stale);value.TextBrush=stale||readings[i].Value is not double number||!double.IsFinite(number)?Brushes.Gray:options.AutomaticColors?AirthingsQuality.BrushFor(level):Manual(options.ValueColor,Brushes.White);
            // The surrounding shared fit already contains the full glyph ink.
            // Trimming against advance widths would remove otherwise fitting units.
            label.Ellipsis=value.Ellipsis=false;
            if(iconRows)
            {
                iconGrid!.Add(readings[i].SensorType,value);
                continue;
            }            if(compact){var inline=new AirthingsReadingRow(label,value,SafeSize(options.LabelSize,11),SafeSize(options.ValueSize??(compact?19:options.Span==2?25:23),23)){Margin=new Thickness(i==0?0:8,0,0,0)};samples.Children.Add(inline);Grid.SetColumn(inline,i%columns);Grid.SetRow(inline,i/columns);}
            else{var stack=new AirthingsReadingStack(label,value,SafeSize(options.LabelSize,11),SafeSize(options.ValueSize??(options.Span==2?25:23),23),15){Margin=new Thickness(i==0?0:6,0,0,0)};samples.Children.Add(stack);Grid.SetColumn(stack,i%columns);Grid.SetRow(stack,i/columns);}
        }
        bool incomplete=device.Supported.Where(AirthingsQuality.IsSupported).Any(type=>AirthingsQuality.Evaluate(device.Readings.FirstOrDefault(r=>r.SensorType.Equals(type,StringComparison.OrdinalIgnoreCase))??new(type,null,""))==AirthingsQualityLevel.Unknown);
        var quality=stale?AirthingsQualityLevel.Unknown:readings.Select(r=>AirthingsQuality.Evaluate(r)).Max();
        footer.Text=$"{(stale?"Old":incomplete?"Partial":quality==AirthingsQualityLevel.Unknown?(readings.Any(r=>r.Value is double n&&double.IsFinite(n))?"Unrated":"No data"):AirthingsQuality.Name(quality))} · {Age(device.Recorded)}";footer.TextBrush=stale||incomplete?Brushes.Gray:options.AutomaticColors?AirthingsQuality.BrushFor(quality):Manual(options.StatusColor,Brushes.White);
        if(compact)room.Text+=" · "+(stale?"Old ":"")+Age(device.Recorded);
        FitMetadataLayout(options);
        SetToolTip($"{device.Home} · {device.Name}\n"+string.Join("\n",device.Readings.Select(r=>$"{SensorLabel(r.SensorType)} {ValueText(r)}"))+"\nSample: "+(device.Recorded?.ToLocalTime().ToString("g")??"unknown")+(stale?" · old data":"")+"\nRight-click to edit");
    }
    private void SetToolTip(string text)
    {ToolTip=text;for(DependencyObject? parent=VisualTreeHelper.GetParent(this);parent!=null;parent=VisualTreeHelper.GetParent(parent))if(parent is Border {ToolTip:ToolTip tip} border&&(border.DataContext is QuickLaunchBar.QuickLaunchSlot||border.DataContext is ShortcutsWidget.Slot)){tip.SetCurrentValue(ContentControl.ContentProperty,text);break;}}
}
