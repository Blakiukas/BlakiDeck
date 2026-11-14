using System.Windows.Media;

namespace Ekrano_valdymas;

internal enum AirthingsStatusLevel { Unknown=-1, Good, Yellow, Orange, Red }
internal sealed record AirthingsStatusSensor(string Sensor,bool Included=true,bool CustomLimits=false,double? GoodLow=null,double? GoodHigh=null,double? YellowLow=null,double? YellowHigh=null,double? OrangeLow=null,double? OrangeHigh=null);
internal sealed record AirthingsStatusRoom(string Account,string Serial,AirthingsStatusSensor[]? Sensors=null,string? DisplayLabel=null);
internal sealed record AirthingsStatusOptions(AirthingsStatusRoom[]? Rooms=null,bool HideRoomNames=false,double RoomSize=11,double StatusSize=10,double IndicatorSize=32)
{
    internal AirthingsStatusOptions Normalize()=>this with
    {
        Rooms=Rooms?.Where(r=>r!=null&&!string.IsNullOrWhiteSpace(r.Serial)).GroupBy(r=>(r.Account,r.Serial)).Select(g=>g.First() with{Sensors=g.First().Sensors?.Where(s=>s!=null&&!string.IsNullOrWhiteSpace(s.Sensor)).GroupBy(s=>AirthingsStatusRules.Canonical(s.Sensor)).Select(x=>x.First()).ToArray(),DisplayLabel=g.First().DisplayLabel?.Trim() is {Length:>0} label?label:null}).ToArray(),
        RoomSize=Size(RoomSize,11,8,80),StatusSize=Size(StatusSize,10,8,80),IndicatorSize=Size(IndicatorSize,32,12,80)
    };
    private static double Size(double value,double fallback,double min,double max)=>double.IsFinite(value)?Math.Clamp(value,min,max):fallback;
}

// Published green/red boundaries are retained. The published intermediate range
// is split at its midpoint for the app's yellow/orange distinction. This is an
// application severity subdivision, not a new medical/exposure recommendation.
internal static class AirthingsStatusRules
{
    internal static string Canonical(string sensor)=>sensor.Trim().ToLowerInvariant() switch{"temperature"=>"temp","tvoc"=>"voc","pm2.5" or "pm2_5"=>"pm25","moldrisk"=>"mold",var s=>s};
    internal static string[] Sensors(AirthingsDevice device)=>(device.Supported??[]).Concat(device.Readings.Select(r=>r.SensorType)).Where(s=>!string.IsNullOrWhiteSpace(s)).GroupBy(Canonical).Select(g=>g.First()).ToArray();
    internal static bool HasSensors(AirthingsDevice device)=>Sensors(device).Length>0;
    internal static AirthingsStatusSensor[] Defaults(AirthingsDevice device)=>Sensors(device).Select(s=>new AirthingsStatusSensor(s,AirthingsQuality.IsSupported(s))).ToArray();
    internal static AirthingsStatusSensor[] Configured(AirthingsDevice device,AirthingsStatusRoom room)
    {
        var saved=room.Sensors??[];
        return Sensors(device).Concat(saved.Select(s=>s.Sensor)).GroupBy(Canonical).Select(g=>saved.FirstOrDefault(s=>Canonical(s.Sensor)==g.Key)??new AirthingsStatusSensor(g.First(),room.Sensors==null&&AirthingsQuality.IsSupported(g.First()))).ToArray();
    }
    internal static string Unit(string sensor)=>Canonical(sensor) switch{"temp"=>"°C","pressure"=>"hPa","humidity"=>"%","radonshorttermavg" or "radonlongtermavg" or "radon"=>"Bq/m³","co2"=>"ppm","voc"=>"ppb","pm25"=>"µg/m³","mold"=>"index",_=>"source unit"};
    private static string UnitKey(string unit)=>unit.Trim().ToLowerInvariant().Replace("³","3").Replace("μ","u").Replace("µ","u").Replace(" ","");
    internal static double? MetricValue(AirthingsReading reading)
    {
        if(reading.Value is not double value||!double.IsFinite(value))return null;
        string sensor=Canonical(reading.SensorType),unit=UnitKey(reading.Unit);
        return sensor switch
        {
            "temp"=>unit is "c" or "°c" or "celsius"?value:unit is "f" or "°f" or "fahrenheit"?(value-32)*5/9:null,
            "pressure"=>value<0?null:unit is "mbar" or "hpa"?value:unit=="pa"?value/100:unit=="kpa"?value*10:unit=="mmhg"?value*1.33322387415:unit=="inhg"?value*33.8638866667:null,
            "radonshorttermavg" or "radonlongtermavg" or "radon"=>value<0?null:unit is "pci/l" or "pci/liter"?value*37:unit is "bq" or "bq/m3" or "bqm3"?value:null,
            "humidity"=>value is <0 or >100?null:unit is "%" or "pct" or "percent"?value:null,
            "co2"=>value>=0&&unit=="ppm"?value:null,
            "voc"=>value>=0&&unit=="ppb"?value:null,
            "pm25"=>value>=0&&(unit is "ug/m3" or "ugm3")?value:null,
            "mold"=>value is >=0 and <=10&&(unit is "" or "%" or "pct" or "index" or "risk" or "riskindex" or "1")?value:null,
            _=>value
        };
    }
    internal static string? Validate(AirthingsStatusSensor sensor)
    {
        if(!sensor.Included||!sensor.CustomLimits)return null;
        double?[] values=[sensor.GoodLow,sensor.GoodHigh,sensor.YellowLow,sensor.YellowHigh,sensor.OrangeLow,sensor.OrangeHigh];
        if(values.Any(v=>v is double n&&!double.IsFinite(n)))return "Limits must be finite numbers.";
        if(values.All(v=>v==null))return "Enter at least one lower or upper limit.";
        double Lower(double? n)=>n??double.NegativeInfinity;double Upper(double? n)=>n??double.PositiveInfinity;
        if(Lower(sensor.GoodLow)>=Upper(sensor.GoodHigh)||Lower(sensor.YellowLow)>=Upper(sensor.YellowHigh)||Lower(sensor.OrangeLow)>=Upper(sensor.OrangeHigh))return "Each lower limit must be smaller than its upper limit.";
        if(Lower(sensor.OrangeLow)>Lower(sensor.YellowLow)||Lower(sensor.YellowLow)>Lower(sensor.GoodLow)||Upper(sensor.GoodHigh)>Upper(sensor.YellowHigh)||Upper(sensor.YellowHigh)>Upper(sensor.OrangeHigh))return "Green must fit inside yellow, and yellow inside orange.";
        return null;
    }
    internal static AirthingsStatusLevel Evaluate(AirthingsReading reading,AirthingsStatusSensor options)
    {
        if(!options.Included)return AirthingsStatusLevel.Unknown;
        if(!options.CustomLimits)
        {
            var original=AirthingsQuality.Evaluate(reading);
            if(original!=AirthingsQualityLevel.Fair)return original switch{AirthingsQualityLevel.Good=>AirthingsStatusLevel.Good,AirthingsQualityLevel.Poor=>AirthingsStatusLevel.Red,_=>AirthingsStatusLevel.Unknown};
            if(MetricValue(reading) is not double metric)return AirthingsStatusLevel.Unknown;
            return Canonical(reading.SensorType) switch
            {
                "radonshorttermavg" or "radonlongtermavg" or "radon"=>metric>=125?AirthingsStatusLevel.Orange:AirthingsStatusLevel.Yellow,
                "co2"=>metric>=900?AirthingsStatusLevel.Orange:AirthingsStatusLevel.Yellow,
                "voc"=>metric>=1125?AirthingsStatusLevel.Orange:AirthingsStatusLevel.Yellow,
                "pm25"=>metric>=17.5?AirthingsStatusLevel.Orange:AirthingsStatusLevel.Yellow,
                "mold"=>metric>=4?AirthingsStatusLevel.Orange:AirthingsStatusLevel.Yellow,
                "humidity"=>metric<27.5||metric>=65?AirthingsStatusLevel.Orange:AirthingsStatusLevel.Yellow,
                _=>AirthingsStatusLevel.Yellow
            };
        }
        if(Validate(options)!=null||MetricValue(reading) is not double value)return AirthingsStatusLevel.Unknown;
        bool In(double? low,double? high)=>value>=(low??double.NegativeInfinity)&&value<(high??double.PositiveInfinity);
        return In(options.GoodLow,options.GoodHigh)?AirthingsStatusLevel.Good:In(options.YellowLow,options.YellowHigh)?AirthingsStatusLevel.Yellow:In(options.OrangeLow,options.OrangeHigh)?AirthingsStatusLevel.Orange:AirthingsStatusLevel.Red;
    }
    internal static AirthingsStatusSensor SuggestedCustom(AirthingsStatusSensor sensor)
    {
        if(new[]{sensor.GoodLow,sensor.GoodHigh,sensor.YellowLow,sensor.YellowHigh,sensor.OrangeLow,sensor.OrangeHigh}.Any(v=>v!=null))return sensor;
        AirthingsStatusSensor Upper(double good,double yellow,double orange)=>sensor with{GoodHigh=good,YellowHigh=yellow,OrangeHigh=orange};
        return Canonical(sensor.Sensor) switch
        {
            "radonshorttermavg" or "radonlongtermavg" or "radon"=>Upper(100,125,150),"co2"=>Upper(800,900,1000),"voc"=>Upper(250,1125,2000),"pm25"=>Upper(10,17.5,25),"mold"=>Upper(3,4,5),
            "humidity"=>sensor with{GoodLow=30,GoodHigh=60,YellowLow=27.5,YellowHigh=65,OrangeLow=25,OrangeHigh=70},_=>sensor
        };
    }
    internal static string Name(AirthingsStatusLevel level)=>level switch{AirthingsStatusLevel.Good=>"Good",AirthingsStatusLevel.Yellow=>"Attention",AirthingsStatusLevel.Orange=>"Warning",AirthingsStatusLevel.Red=>"Poor",_=>"Unknown"};
    internal static Brush Brush(AirthingsStatusLevel level)=>level switch{AirthingsStatusLevel.Good=>Brushes.LimeGreen,AirthingsStatusLevel.Yellow=>Brushes.Gold,AirthingsStatusLevel.Orange=>Brushes.DarkOrange,AirthingsStatusLevel.Red=>AirthingsQuality.BrushFor(AirthingsQualityLevel.Poor),_=>Brushes.Gray};
}

