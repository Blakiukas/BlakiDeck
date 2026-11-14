using System.IO;
using System.Text.Json;
namespace Ekrano_valdymas;
internal sealed record AirthingsDisplay(string View="Overview",string Sensor="radonShortTermAvg",double Days=1,string Pressure="hPa",string Temperature="°C",string Radon="Bq/m³",string Color="#53B5FF")
{
    public string TitleFont {get;init;}="Teko";
    public string ValueFont {get;init;}="Segoe UI";
    public string LabelFont {get;init;}="Segoe UI";
    public string AxisFont {get;init;}="Segoe UI";
    public double TitleSize {get;init;}=27;
    public double ValueSize {get;init;}=24;
    public double LabelSize {get;init;}=11;
    public double AxisSize {get;init;}=11;
    public double IntervalSize {get;init;}=11;
    public string UnitFont {get;init;}="Segoe UI";
    public double UnitSize {get;init;}=14;
    public double FooterSize {get;init;}=10;
    public string ColorMode {get;init;}="Manual";
    internal bool UsesQualityColors=>ColorMode=="Airthings air quality";
    public string? TitleColor {get;init;}
    public string? ValueColor {get;init;}="#00BFFF";
    public string? LabelColor {get;init;}
    public string? AxisColor {get;init;}
    public string? GuideColor {get;init;}
    private static string PathName=>Path.Combine(DiagnosticState.LocalApplicationData,"Ekrano_valdymas","airthings-display.json");
    internal static AirthingsDisplay Load(){try{return JsonSerializer.Deserialize<AirthingsDisplay>(File.ReadAllText(PathName))??new();}catch{return new();}}
    internal static event Action? Changed;
    internal void Save(){AtomicJson.Write(PathName,this);Changed?.Invoke();}
    private static string GaugePath=>Path.Combine(Path.GetDirectoryName(PathName)!,"airthings-gauge-display.json");
    internal static AirthingsDisplay LoadGauges(){try{if(File.Exists(GaugePath))return JsonSerializer.Deserialize<AirthingsDisplay>(File.ReadAllText(GaugePath))??Load();var initial=Load();initial.SaveGauges();return initial;}catch{return Load();}}
    internal void SaveGauges()=>AtomicJson.Write(GaugePath,this);
    internal (double Value,string Unit) Convert(AirthingsReading r)=>Convert(r.SensorType,r.Value??0,r.Unit);
    internal (double Value,string Unit) Convert(string sensor,double value,string unit)
    {
        if(sensor.Equals("pressure",StringComparison.OrdinalIgnoreCase))return Pressure switch{"mmHg"=>(value/1.33322387415,Pressure),"inHg"=>(value/33.8638866667,Pressure),_ =>(value,Pressure)};
        if(sensor is "temp" or "temperature")return Temperature=="°F"?(value*9/5+32,"°F"):(value,"°C");
        if(sensor.StartsWith("radon",StringComparison.OrdinalIgnoreCase))return Radon=="pCi/L"?(value/37,Radon):(value,Radon);
        return(value,AirthingsWidget.DisplayUnit(unit));
    }
}
