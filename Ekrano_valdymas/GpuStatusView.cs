using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static Ekrano_valdymas.SystemMonitorWidget;
namespace Ekrano_valdymas;

internal sealed class GpuStatusView:Grid
{
    private readonly AdaptiveSensorColors adaptiveColors=new();
    private readonly OpticallyCenteredText title=Text("GPU STATUS",FontWeights.Bold);
    private readonly OpticallyCenteredText device=Text("",FontWeights.Normal);
    private readonly OpticallyCenteredText[] labels=new OpticallyCenteredText[6],values=new OpticallyCenteredText[6];
    private static OpticallyCenteredText Text(string text,FontWeight weight)=>new(){Text=text,TextFontWeight=weight,TextBrush=Brushes.WhiteSmoke,FitToBox=true,Margin=new Thickness(5,2,5,2)};
    private static readonly string CpuDeviceName=ReadCpuName();
    private static string ReadCpuName(){try{using var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");return (key?.GetValue("ProcessorNameString") as string)?.Trim()??"CPU unavailable";}catch{return "CPU unavailable";}}
    internal GpuStatusView()
    {
        IsHitTestVisible=false;
        ColumnDefinitions.Add(new(){Width=new GridLength(42,GridUnitType.Star)});ColumnDefinitions.Add(new(){Width=new GridLength(58,GridUnitType.Star)});
        for(int i=0;i<8;i++)RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        Children.Add(title);SetColumnSpan(title,2);
        for(int i=0;i<6;i++){labels[i]=Text("",FontWeights.SemiBold);values[i]=Text("",FontWeights.SemiBold);labels[i].InkAlignment=HorizontalAlignment.Left;values[i].InkAlignment=HorizontalAlignment.Right;Children.Add(labels[i]);Children.Add(values[i]);SetRow(labels[i],i+1);SetRow(values[i],i+1);SetColumn(values[i],1);}
        Children.Add(device);SetRow(device,7);SetColumnSpan(device,2);
    }
    internal void Refresh(SystemMonitorWidget source,MetricPresentation appearance)
        =>RefreshStatus(source,appearance,MetricKind.GpuStatus);
    internal void RefreshStatus(SystemMonitorWidget source,MetricPresentation appearance,MetricKind kind)
    {
        var gpu=source.GpuInfo;var rows=source.StatusRows(kind,appearance);title.Text=GetMetricLabel(kind);
        title.Visibility=appearance.HideTitle?Visibility.Collapsed:Visibility.Visible;RowDefinitions[0].Height=appearance.HideTitle?new GridLength(0):new GridLength(1,GridUnitType.Star);
        title.TextFontSize=appearance.TitleSize;title.FitToBox=appearance.AutoFit;
        var accent=Brushes.DeepSkyBlue as Brush;
        try{if(appearance.Color!=null)accent=(Brush)new BrushConverter().ConvertFromString(appearance.Color)!;}catch{}
        Brush heading=Brushes.WhiteSmoke;
        try{if(appearance.TitleColor!=null)heading=(Brush)new BrushConverter().ConvertFromString(appearance.TitleColor)!;}catch{}
        title.TextBrush=heading;foreach(var label in labels)label.TextBrush=heading;
        Brush clock=accent;try{if(appearance.GpuClockColor!=null)clock=(Brush)new BrushConverter().ConvertFromString(appearance.GpuClockColor)!;}catch{}
        for(int i=0;i<6;i++){labels[i].Text=rows[i].Label;values[i].Text=rows[i].Value;labels[i].TextFontSize=appearance.TitleSize;values[i].TextFontSize=appearance.ValueSize;labels[i].FitToBox=values[i].FitToBox=appearance.AutoFit;values[i].TextBrush=kind==MetricKind.GpuStatus&&i==3?clock:kind==MetricKind.GpuStatus&&i==4&&gpu.Reasons is {} reasons&&(reasons&0xfcUL)!=0?Brushes.Orange:accent;}
        if(kind==MetricKind.DiskStatus){string?[] colors=[appearance.DiskReadColor,appearance.DiskWriteColor,appearance.DiskTemperatureColor];for(int i=0;i<3;i++)try{if(colors[i] is {} color)values[i].TextBrush=(Brush)new BrushConverter().ConvertFromString(color)!;}catch{}}
        if(kind==MetricKind.CpuStatus){string?[] colors=[appearance.CpuLoadColor,appearance.CpuTemperatureColor,appearance.CpuPowerColor];for(int i=0;i<3;i++)try{if(colors[i] is {} color)values[i].TextBrush=(Brush)new BrushConverter().ConvertFromString(color)!;}catch{}}
        if(kind==MetricKind.GpuStatus&&appearance.GpuPowerColor is {} power)try{values[0].TextBrush=(Brush)new BrushConverter().ConvertFromString(power)!;}catch{}
        var channels=AdaptiveSensorColors.Channels(kind);
        foreach(var channel in channels)
        {
            int index=kind==MetricKind.GpuStatus?(channel==MetricKind.GpuPower?0:3):Array.IndexOf(channels,channel);
            var color=adaptiveColors.Resolve(channel+":"+appearance.DiskId,AdaptiveSensorColors.Reading(source,channel,appearance),values[index].TextBrush.ToString(),appearance.AdaptiveRules?.GetValueOrDefault(channel.ToString()),appearance.AdaptiveColors);
            try{if(color!=null)values[index].TextBrush=(Brush)new BrushConverter().ConvertFromString(color)!;}catch{}
        }
        device.Text=kind==MetricKind.GpuStatus?GpuTelemetrySelection.Current?.Name??"GPU unavailable":kind==MetricKind.DiskStatus?source.SelectedDisk(appearance.DiskId)?.Description??"Disk unavailable":CpuDeviceName;device.TextFontSize=Math.Min(appearance.TitleSize,12);device.TextBrush=Brushes.Silver;
    }
}
