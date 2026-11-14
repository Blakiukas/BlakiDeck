using System.Windows.Media;

namespace Ekrano_valdymas;

internal enum AirthingsQualityLevel { Unknown, Good, Fair, Poor }

/// <summary>Airthings' published metric thresholds, evaluated before display-unit conversion.</summary>
internal static class AirthingsQuality
{
    private static readonly Brush PoorBrush=CreatePoorBrush();
    private static Brush CreatePoorBrush(){var brush=new SolidColorBrush(Color.FromRgb(255,91,91));brush.Freeze();return brush;}
    // https://www.airthings.com/sv/view-plus-two-pack (metric thresholds)
    // https://www.airthings.com/wave-mini and help.airthings.com/en/articles/4419641
    // Temperature/pressure do not contribute to the View monitor's overall air-quality light.
    internal static bool IsStale(DateTimeOffset? recorded,DateTimeOffset? now=null)
        =>recorded==null||(now??DateTimeOffset.UtcNow)-recorded.Value>TimeSpan.FromMinutes(60);

    internal static bool IsSupported(string sensor)=>sensor.ToLowerInvariant() is
        "radonshorttermavg" or "radonlongtermavg" or "radon" or "co2" or "voc" or "tvoc" or
        "humidity" or "pm25" or "pm2.5" or "pm2_5" or "mold" or "moldrisk";

    internal static AirthingsQualityLevel Evaluate(AirthingsReading reading,bool stale=false)
    {
        if(stale||reading.Value is not {} value||!double.IsFinite(value)||value<0)return AirthingsQualityLevel.Unknown;
        string sensor=reading.SensorType.ToLowerInvariant();
        string unit=reading.Unit.Trim().ToLowerInvariant().Replace("³","3").Replace("μ","u").Replace("µ","u").Replace(" ","");
        AirthingsQualityLevel Upper(double fair,double poor)=>value>=poor?AirthingsQualityLevel.Poor:value>=fair?AirthingsQualityLevel.Fair:AirthingsQualityLevel.Good;
        switch(sensor)
        {
            case "radonshorttermavg":case "radonlongtermavg":case "radon":
                if(unit is "pci/l" or "pci/liter")value*=37;
                else if(unit is not ("bq" or "bq/m3" or "bqm3"))return AirthingsQualityLevel.Unknown;
                return Upper(100,150);
            case "co2":return unit=="ppm"?Upper(800,1000):AirthingsQualityLevel.Unknown;
            case "voc":case "tvoc":return unit=="ppb"?Upper(250,2000):AirthingsQualityLevel.Unknown;
            case "humidity":
                if(unit is not ("%" or "pct" or "percent")||value>100)return AirthingsQualityLevel.Unknown;
                return value<25||value>=70?AirthingsQualityLevel.Poor:value<30||value>=60?AirthingsQualityLevel.Fair:AirthingsQualityLevel.Good;
            case "pm25":case "pm2.5":case "pm2_5":
                return unit is "ug/m3" or "ugm3"?Upper(10,25):AirthingsQualityLevel.Unknown;
            case "mold":case "moldrisk":
                // The published ranges share endpoints; the higher category starts at 3 and 5.
                return value<=10&&(unit is "" or "%" or "pct" or "index" or "risk" or "riskindex" or "1")?Upper(3,5):AirthingsQualityLevel.Unknown;
            default:return AirthingsQualityLevel.Unknown;
        }
    }

    internal static Brush BrushFor(AirthingsQualityLevel level)=>level switch
    {
        AirthingsQualityLevel.Good=>Brushes.LimeGreen,
        AirthingsQualityLevel.Fair=>Brushes.Gold,
        AirthingsQualityLevel.Poor=>PoorBrush,
        _=>Brushes.Gray
    };
    internal static string Name(AirthingsQualityLevel level)=>level switch
    {
        AirthingsQualityLevel.Good=>"Good",AirthingsQualityLevel.Fair=>"Fair",AirthingsQualityLevel.Poor=>"Poor",_=>"Unavailable / unclassified"
    };
}
