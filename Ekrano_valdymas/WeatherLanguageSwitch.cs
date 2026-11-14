using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

// Weather-only selection/edit actions; the About/global language switch is independent.
internal sealed class WeatherLanguageSwitch:Border
{
    private readonly WrapPanel row=new(){Orientation=Orientation.Horizontal};
    private readonly Dictionary<string,Button> selectors=new(StringComparer.Ordinal);
    internal string SelectedLanguage {get;private set;}="en";
    internal event EventHandler? SelectionChanged;
    internal event Action<string>? EditRequested;
    internal event Action<string>? RemoveRequested;
    internal event Action? AddRequested;
    internal WeatherLanguageSwitch()
    {
        Name="WeatherLanguageChoice";Background=new SolidColorBrush(Color.FromRgb(23,25,27));
        BorderBrush=new SolidColorBrush(Color.FromRgb(59,62,64));BorderThickness=new Thickness(1);
        CornerRadius=new CornerRadius(8);Padding=new Thickness(3);MinHeight=44;
        HorizontalAlignment=HorizontalAlignment.Stretch;Child=row;
    }
    private static Button MakeButton(string name,string text,FontFamily? family=null)
    {
        var label=new OpticallyCenteredText{Text=text,TextFontFamily=family??new FontFamily("Segoe UI"),TextFontWeight=FontWeights.Normal,TextFontSize=13,
            TextBrush=Brushes.White,InkAlignment=HorizontalAlignment.Center,FitToBox=false,Ellipsis=true};
        var button=new Button{Name=name,Content=label,Width=Math.Clamp(Math.Ceiling(label.GetNaturalMetrics(13).Width)+24,52,360),MinWidth=52,MaxWidth=360,Height=36,BorderThickness=new Thickness(0),Padding=new Thickness(12,0,12,0),Tag="NoWindowDrag",Background=Brushes.Transparent};
        var frame=new FrameworkElementFactory(typeof(Border));frame.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));
        frame.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Stretch);
        content.SetBinding(ContentPresenter.MarginProperty,new System.Windows.Data.Binding("Padding"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});frame.AppendChild(content);
        var template=new ControlTemplate(typeof(Button)){VisualTree=frame};
        var disabled=new Trigger{Property=IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(OpacityProperty,.45));template.Triggers.Add(disabled);
        button.Template=template;return button;
    }
    internal void SetChoices(IReadOnlyList<WeatherLanguageDictionary> languages,string selected)
    {
        row.Children.Clear();selectors.Clear();
        foreach(var language in languages)
        {
            string code=language.Code;var cell=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(1)};
            var select=MakeButton("WeatherSelect_"+code.Replace('-','_'),code is "en" or "lt"?code.ToUpperInvariant():language.Name);select.Tag=code;select.ToolTip=language.Name+" ("+code.ToUpperInvariant()+")";
            System.Windows.Automation.AutomationProperties.SetName(select,"Select "+language.Name);
            select.Click+=(_,e)=>{e.Handled=true;SetLanguage(code);SelectionChanged?.Invoke(this,EventArgs.Empty);};selectors.Add(code,select);cell.Children.Add(select);
            if(code!="en")
            {
                var edit=MakeButton("WeatherEdit_"+code.Replace('-','_'),"\uE70F",new FontFamily("Segoe MDL2 Assets"));edit.MinWidth=28;edit.Width=28;edit.Padding=new Thickness(4,0,4,0);edit.Tag=code;
                ((OpticallyCenteredText)edit.Content).TextBrush=AdaptiveSensorColors.PencilBrush;
                edit.ToolTip="Edit "+language.Name+" ("+code.ToUpperInvariant()+")";System.Windows.Automation.AutomationProperties.SetName(edit,"Edit "+language.Name);
                edit.Click+=(_,e)=>{e.Handled=true;EditRequested?.Invoke(code);};cell.Children.Add(edit);
                if(code!="lt")
                {
                    var menu=new ContextMenu();menu.SetResourceReference(StyleProperty,"MiniContextMenuStyle");
                    var remove=new MenuItem{Header="Remove from choices…"};remove.SetResourceReference(StyleProperty,"MiniMenuItemStyle");remove.Click+=(_,_)=>RemoveRequested?.Invoke(code);menu.Items.Add(remove);cell.ContextMenu=menu;
                }
            }
            row.Children.Add(cell);
        }
        var add=MakeButton("WeatherAddLanguage","+");add.MinWidth=36;add.Width=36;add.Padding=new Thickness(0);add.Margin=new Thickness(1);
        add.IsEnabled=languages.Count(language=>language.Code is not "en" and not "lt")<WeatherTranslationStore.MaximumCustomLanguages;
        add.ToolTip=add.IsEnabled?"Add a weather language":"Up to three additional languages. Right-click a custom language to remove it from the choices; its translations are kept.";
        ToolTipService.SetShowOnDisabled(add,true);System.Windows.Automation.AutomationProperties.SetName(add,"Add a weather language");
        add.Click+=(_,e)=>{e.Handled=true;AddRequested?.Invoke();};row.Children.Add(add);SetLanguage(selected);
    }
    private void SetLanguage(string code)
    {
        SelectedLanguage=selectors.ContainsKey(code)?code:"en";
        foreach(var pair in selectors)pair.Value.Background=pair.Key==SelectedLanguage?new SolidColorBrush(Color.FromRgb(68,71,74)):Brushes.Transparent;
    }
}
