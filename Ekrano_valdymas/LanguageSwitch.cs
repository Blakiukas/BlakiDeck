using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

internal sealed class LanguageSwitch : Border
{
    // Shared by About, Help and the owned legal pages for this application session.
    internal static string CurrentLanguage {get;private set;}="en";
    private static event EventHandler? LanguageChanged;
    private readonly Dictionary<string,Button> buttons=new();
    internal string SelectedLanguage {get;private set;}="en";
    internal event EventHandler? SelectionChanged;
    internal LanguageSwitch(string? language=null)
    {
        Background=new SolidColorBrush(Color.FromRgb(23,25,27));
        BorderBrush=new SolidColorBrush(Color.FromRgb(59,62,64));BorderThickness=new Thickness(1);
        CornerRadius=new CornerRadius(8);Padding=new Thickness(3);Height=44;Width=112;
        var row=new StackPanel{Orientation=Orientation.Horizontal};Child=row;
        foreach(string code in new[]{"en","lt"})
        {
            var label=new OpticallyCenteredText{Text=code.ToUpperInvariant(),TextFontFamily=new FontFamily("Segoe UI"),TextFontWeight=FontWeights.Normal,TextFontSize=13,TextBrush=Brushes.White,InkAlignment=HorizontalAlignment.Center};
            var button=new Button{Content=label,Width=52,Height=36,BorderThickness=new Thickness(0),Padding=new Thickness(0),Tag="NoWindowDrag"};
            var frame=new FrameworkElementFactory(typeof(Border));frame.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));
            frame.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
            var content=new FrameworkElementFactory(typeof(ContentPresenter));frame.AppendChild(content);
            button.Template=new ControlTemplate(typeof(Button)){VisualTree=frame};
            System.Windows.Automation.AutomationProperties.SetName(button,code=="en"?"English":"Lietuvių");
            button.Click+=(_,e)=>{e.Handled=true;SetLanguage(code);SelectionChanged?.Invoke(this,EventArgs.Empty);};
            buttons.Add(code,button);row.Children.Add(button);
        }
        SetLanguage(language??CurrentLanguage);
        Loaded+=(_,_)=>{LanguageChanged+=FollowLanguage;FollowLanguage(null,EventArgs.Empty);};
        Unloaded+=(_,_)=>LanguageChanged-=FollowLanguage;
    }
    private void FollowLanguage(object? sender,EventArgs e)
    {
        if(SelectedLanguage==CurrentLanguage)return;
        SetLanguage(CurrentLanguage);SelectionChanged?.Invoke(this,EventArgs.Empty);
    }
    internal void SetLanguage(string language)
    {
        SelectedLanguage=language=="lt"?"lt":"en";
        foreach(var pair in buttons)pair.Value.Background=pair.Key==SelectedLanguage?new SolidColorBrush(Color.FromRgb(68,71,74)):Brushes.Transparent;
        if(CurrentLanguage!=SelectedLanguage){CurrentLanguage=SelectedLanguage;LanguageChanged?.Invoke(this,EventArgs.Empty);}
    }
}
