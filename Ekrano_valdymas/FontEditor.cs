using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

internal sealed class FontEditor
{
    private readonly ComboBox family;
    private readonly CheckBox bold,italic;
    private readonly Slider strength;
    private ComboBox? effectChoice; internal string EffectMode=>effectChoice?.SelectedItem as string??"Outline";
    internal event Action? Changed;
    internal string SelectedFamily=>family.SelectedItem as string??"Segoe UI";
    internal bool Bold=>bold.IsChecked==true;
    internal bool Italic=>italic.IsChecked==true;
    internal double Strength=>strength.Value;
    internal void Reset(string face,bool weight,bool slant,double effect,string mode="None"){family.SelectedItem=face;bold.IsChecked=weight;italic.IsChecked=slant;strength.Value=effect;if(effectChoice!=null)effectChoice.SelectedItem=mode;Changed?.Invoke();}
    internal void ResetShortcut()=>Reset("Segoe UI",true,false,1);
    internal static FontFamily Family(string? name)
    {
        if(name=="Teko")return (FontFamily)Application.Current.FindResource("TekoFont");
        if(name=="Michroma")return new FontFamily(new Uri("pack://application:,,,/"),"./Assets/Fonts/#Michroma");
        try{return new FontFamily(string.IsNullOrWhiteSpace(name)?"Segoe UI":name);}catch{return new FontFamily("Segoe UI");}
    }
    internal FontEditor(Panel panel,string selected,bool isBold,bool isItalic,double effect,string effectLabel="Inset effect strength (0 = off, 1 = default)",bool includeEffect=true,string? effectMode=null)
    {
        var names=Fonts.SystemFontFamilies.Select(f=>f.Source).Append("Teko").Append("Michroma").Append(selected).Distinct().OrderBy(n=>n).ToArray();
        family=new ComboBox{Name="FontFamilyChoice",ItemsSource=names,SelectedItem=selected};EditorUi.Field(panel,"Font family",family);
        var styles=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(4,4,4,2)};
        bold=new CheckBox{Content="Bold",IsChecked=isBold,Foreground=Brushes.White,Margin=new Thickness(0,0,24,0)};
        italic=new CheckBox{Content="Italic",IsChecked=isItalic,Foreground=Brushes.White};styles.Children.Add(bold);styles.Children.Add(italic);panel.Children.Add(styles);
        strength=new Slider{Name="TextEffectStrength",Minimum=0,Maximum=2,Value=double.IsFinite(effect)?Math.Clamp(effect,0,2):1,TickFrequency=.1,IsSnapToTickEnabled=true};
        if(includeEffect){if(effectMode!=null){effectChoice=new ComboBox{Name="TextEffectMode",ItemsSource=new[]{"None","Soft shadow","Outline"},SelectedItem=effectMode};EditorUi.Field(panel,"Text effect",effectChoice);effectChoice.SelectionChanged+=(_,_)=>{strength.IsEnabled=EffectMode!="None";if(strength.IsEnabled&&strength.Value==0)strength.Value=1;Changed?.Invoke();};strength.IsEnabled=EffectMode!="None";}EditorUi.Field(panel,effectMode!=null?"Strength":effectLabel,strength);}
        family.SelectionChanged+=(_,_)=>Changed?.Invoke();bold.Checked+=(_,_)=>Changed?.Invoke();bold.Unchecked+=(_,_)=>Changed?.Invoke();italic.Checked+=(_,_)=>Changed?.Invoke();italic.Unchecked+=(_,_)=>Changed?.Invoke();strength.ValueChanged+=(_,_)=>Changed?.Invoke();
    }
}
