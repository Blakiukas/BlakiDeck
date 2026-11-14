using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Text.Json.Serialization;

namespace Ekrano_valdymas;

public sealed record LabelTypography
{
    public string FontFamily {get;set;}="Segoe UI";
    public double Size {get;set;}=18;
    public bool Bold {get;set;}
    public bool Italic {get;set;}
    public string? LightEffectMode {get;set;}
    public string? DarkEffectMode {get;set;}
    public string ModeFor(bool light)=>NormalizeEffect((light?LightEffectMode:DarkEffectMode)??(EffectFor(light)>0?"Outline":"None"));
    private static string NormalizeEffect(string mode)=>mode=="Engraved"?"Outline":mode;
    public double? LightEffectStrength {get;set;}
    public double? DarkEffectStrength {get;set;}
    public double EffectFor(bool light,double fallback=0){double value=(light?LightEffectStrength:DarkEffectStrength)??fallback;return double.IsFinite(value)?Math.Clamp(value,0,2):fallback;}
    public static readonly DependencyProperty TextEffectProperty=DependencyProperty.RegisterAttached("TextEffect",typeof(LabelTypography),typeof(LabelTypography),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public static LabelTypography? GetTextEffect(DependencyObject target)=>(LabelTypography?)target.GetValue(TextEffectProperty);
    public static void SetTextEffect(DependencyObject target,LabelTypography? value)=>target.SetValue(TextEffectProperty,value);
    [JsonIgnore] public double SafeSize=>double.IsFinite(Size)?Math.Clamp(Size,10,96):18;
    [JsonIgnore] public FontFamily Face=>FontEditor.Family(FontFamily);
    [JsonIgnore] public FontWeight Weight=>Bold?FontWeights.Bold:FontWeights.Normal;
    [JsonIgnore] public FontStyle Style=>Italic?FontStyles.Italic:FontStyles.Normal;
    internal void Apply(OpticallyCenteredText text)
    {text.TextFontFamily=Face;text.TextFontSize=SafeSize;text.TextFontWeight=Weight;text.TextFontStyle=Style;}
    internal static void Editor(Panel panel,LabelTypography initial,Action<LabelTypography> changed,string sizeName="TextSizeChoice",bool includeEffect=false,LabelTypography? defaults=null)
    {
        bool light=MusicFinish.IsLight;var font=new FontEditor(panel,initial.FontFamily,initial.Bold,initial.Italic,initial.EffectFor(light),light?"Light skin effect (0 = off, 1 = default)":"Dark skin effect (0 = off, 1 = default)",includeEffect:includeEffect,effectMode:includeEffect?initial.ModeFor(light):null);
        var size=new ComboBox{Name=sizeName,ItemsSource=new double[]{10,12,14,16,18,19,20,22,24,27,28,32,36,42,48,60,72,96}.Append(initial.SafeSize).Distinct().Order().ToArray(),SelectedItem=initial.SafeSize};
        EditorUi.Field(panel,"Text size",size);
        void Update()=>changed(initial with{LightEffectMode=includeEffect&&light?font.EffectMode:initial.LightEffectMode,DarkEffectMode=includeEffect&&!light?font.EffectMode:initial.DarkEffectMode,FontFamily=font.SelectedFamily,Size=size.SelectedItem is double value?value:initial.SafeSize,Bold=font.Bold,Italic=font.Italic,LightEffectStrength=includeEffect&&light?font.Strength:initial.LightEffectStrength,DarkEffectStrength=includeEffect&&!light?font.Strength:initial.DarkEffectStrength});
        size.SelectionChanged+=(_,_)=>Update();font.Changed+=Update;
        if(defaults!=null)panel.Children.Add(MacroUi.Button("Default text",(_,_)=>{initial=defaults with{};font.Reset(initial.FontFamily,initial.Bold,initial.Italic,initial.EffectFor(light),initial.ModeFor(light));size.SelectedItem=initial.SafeSize;Update();}));
    }
}
