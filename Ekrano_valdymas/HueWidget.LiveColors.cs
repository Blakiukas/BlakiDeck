using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Ekrano_valdymas;
public partial class HueWidget
{
    private readonly Dictionary<string,HuePoint[]> lightColorDraft = [];
    private readonly Dictionary<string,List<Button>> lightColorButtons = [];
    private static Color LightColor(HueLight light)
    {
        if (light.Mirek is int mirek)
        {
            // Planckian white is a screen approximation; the bridge's mirek remains authoritative.
            double t = Math.Clamp(1000000d / mirek, 1000, 40000) / 100;
            double r = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -.1332047592);
            double g = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661 : 288.1221695283 * Math.Pow(t - 60, -.0755148492);
            double b = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
            return Color.FromRgb((byte)Math.Clamp(r,0,255),(byte)Math.Clamp(g,0,255),(byte)Math.Clamp(b,0,255));
        }
        return XyToColor(light.Color?.X,light.Color?.Y);
    }
    private static Color[] LightColors(HueLight light) => light.Mirek == null && light.Gradient.Length >= 2
        ? light.Gradient.Select(p=>XyToColor(p.X,p.Y)).ToArray() : [LightColor(light)];
    private static Color[] RoomLiveColors(HueRoom room) => room.Lights.Length > 0
        ? room.Lights.SelectMany(LightColors).ToArray() : [XyToColor(room.ColorX,room.ColorY)];
    private static Color RoomLiveColor(HueRoom room) => RoomLiveColors(room)[0];
    private static Brush RoomLiveBrush(HueRoom room,bool on)
    {
        if(!on)return Brushes.Transparent;
        var colors=RoomLiveColors(room);
        if(colors.Length==1)return RoomBrush(true,colors[0]);
        var brush=new LinearGradientBrush {StartPoint=new Point(0,0),EndPoint=new Point(1,0)};
        for(int i=0;i<colors.Length;i++)brush.GradientStops.Add(new(Color.FromArgb(165,colors[i].R,colors[i].G,colors[i].B),(double)i/(colors.Length-1)));
        return brush;
    }
    private void AddLightColorRows(HueRoom room,Grid row)
    {
        var swatches=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
        var fit=new Viewbox{Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,Child=swatches,MaxWidth=280,VerticalAlignment=VerticalAlignment.Center};
        Grid.SetColumn(fit,1);row.Children.Add(fit);
        foreach(var light in room.Lights)
        {            int count=light.ColorChoiceCount;
            var buttons=new List<Button>();lightColorButtons[light.Id]=buttons;
            for(int i=0;i<count;i++)
            {
                int point=i;
                var button=new Button {Width=42,Height=36,Margin=new Thickness(3,1,0,1),Padding=new Thickness(0),BorderBrush=Brushes.Gray};
                button.SetResourceReference(StyleProperty,"HueColorSwatch");
                ToolTipService.SetShowOnDisabled(button,true);
                button.Click+=(_,_)=>EditLightColor(light.Id,point);
                buttons.Add(button);swatches.Children.Add(button);
            }

        }
        RefreshLightColorButtons();
    }
    private void RefreshLightColorButtons()
    {
        foreach(var light in currentRooms.SelectMany(r=>r.Lights))
        {
            if(!lightColorButtons.TryGetValue(light.Id,out var buttons))continue;
            var colors=lightColorDraft.TryGetValue(light.Id,out var draft)?draft.Select(p=>XyToColor(p.X,p.Y)).ToArray():LightColors(light);
            for(int i=0;i<buttons.Count;i++)
            {
                buttons[i].Background=new SolidColorBrush(colors[Math.Min(i,colors.Length-1)]);
                buttons[i].IsEnabled=light.SupportsColor&&light.Reachable!=false&&selectionActions.IsEnabled;
                buttons[i].ToolTip=light.Reachable==false?$"{light.Name}: disconnected; last reported color":light.Color==null&&light.Mirek==null&&light.Gradient.Length==0?$"{light.Name}: color unavailable":$"{light.Name} — color {i+1}";
            }
        }
    }
    private void EditLightColor(string id,int index)
    {
        var light=currentRooms.SelectMany(r=>r.Lights).FirstOrDefault(l=>l.Id==id);
        if(light==null||light.Reachable==false||!light.SupportsColor)return;
        var original=lightColorDraft.GetValueOrDefault(id)??(light.Mirek==null&&light.Gradient.Length>=2?light.Gradient:[]);
        int count=light.ColorChoiceCount;
        var initial=original.Length>index?XyToColor(original[index].X,original[index].Y):LightColor(light);
        Color selected=initial;
        var dialog=CompactColorPicker.CreateOpaque(Window.GetWindow(this),initial.ToString(),s=>selected=CompactColorPicker.Parse(s,Colors.White));
        if(dialog.ShowDialog()!=true)return;
        var xy=RgbToXy(selected);
        var fallback=light.Color??new HuePoint(xy.X,xy.Y);
        var points=Enumerable.Range(0,count).Select(i=>i<original.Length?original[i]:fallback).ToArray();
        points[index]=new HuePoint(xy.X,xy.Y);
        lightColorDraft[id]=points;RefreshLightColorButtons();
    }
    private async Task SaveLightColorDraftAsync()
    {
        if(lightColorDraft.Count==0)return;
        if(currentBridge==null||applicationKey==null)throw new InvalidOperationException("Hue is not connected.");
        using var client=new HueBridgeClient(currentBridge);
        // Re-read capabilities and connectivity before sending edits, never write colors during GET/polling.
        currentRooms=await client.GetRoomsAsync(applicationKey);
        foreach(var pair in lightColorDraft.ToArray())
        {
            var light=currentRooms.SelectMany(r=>r.Lights).FirstOrDefault(l=>l.Id==pair.Key)??throw new InvalidOperationException("Light no longer available.");
            if(light.Reachable==false)throw new InvalidOperationException("Light is unavailable; color was not sent.");
            await client.SetLightColorsAsync(applicationKey,light,pair.Value);
            lightColorDraft.Remove(pair.Key);
        }
        currentRooms=await client.GetRoomsAsync(applicationKey);
    }
}

