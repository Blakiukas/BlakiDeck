using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;
internal sealed record PlayerTypography
{
    public LabelTypography? Title {get;set;}
    public LabelTypography? Artist {get;set;}
    public LabelTypography? Time {get;set;}
    internal static LabelTypography Default(string category,bool wide)=>new(){FontFamily=wide?"Michroma":"Segoe UI",Size=category=="Title"?27:category=="Artist"?19:16};
    internal LabelTypography? Get(string category)=>category=="Title"?Title:category=="Artist"?Artist:Time;
    internal PlayerTypography With(string category,LabelTypography? style)=>category=="Title"?this with{Title=style}:category=="Artist"?this with{Artist=style}:this with{Time=style};
    internal void Apply(OpticallyCenteredText title,OpticallyCenteredText artist,OpticallyCenteredText time,OpticallyCenteredText total,bool wide)
    {(Title??Default("Title",wide)).Apply(title);(Artist??Default("Artist",wide)).Apply(artist);(Time??Default("Time",wide)).Apply(time);(Time??Default("Time",wide)).Apply(total);}
}
internal sealed partial class MusicConsoleControl
{
    internal void ApplyTypography(PlayerTypography value)=>value.Apply(title,artist,time,total,true);
}
internal sealed partial class MusicPlayerCard
{
    internal void ApplyTypography(PlayerTypography value)=>value.Apply(title,artist,time,total,false);
}
public partial class SpotifyWidget
{
    private readonly Dictionary<TextBlock,(FontFamily Face,double Size,FontWeight Weight,FontStyle Style)> originalTypography=new();
    internal void ApplyTypography(PlayerTypography value)
    {
        void Apply(TextBlock text,LabelTypography? style)
        {
            if(style==null&&!originalTypography.ContainsKey(text))return;
            if(!originalTypography.TryGetValue(text,out var original))originalTypography[text]=original=(text.FontFamily,text.FontSize,text.FontWeight,text.FontStyle);
            text.SetCurrentValue(TextBlock.FontFamilyProperty,style?.Face??original.Face);text.SetCurrentValue(TextBlock.FontSizeProperty,style?.SafeSize??original.Size);
            text.SetCurrentValue(TextBlock.FontWeightProperty,style?.Weight??original.Weight);text.SetCurrentValue(TextBlock.FontStyleProperty,style?.Style??original.Style);
            if(style==null)originalTypography.Remove(text);
        }
        Apply(txtTitle,value.Title);Apply(txtArtist,value.Artist);Apply(txtTimeCurrent,value.Time);Apply(txtTimeTotal,value.Time);
    }
}
public partial class MainWindow
{
    private void ApplyPlayerTypography(PlayerTypography value)
    {
        selectedPlayerSettings.Typography=value;
        musicConsole?.ApplyTypography(value);musicPlayerCard?.ApplyTypography(value);spotifyWidget.ApplyTypography(value);
    }
    private void InitializePlayerTextMenu()
    {
        var menu=new MenuItem{Header="Song title, artist & time text…",ToolTip="Edit the font, size and style of the song title, artist and time numbers on this tab."};playerOptionsMenu.Items.Add(menu);
        menu.Click+=(_,_)=>CreatePlayerTextEditor().ShowDialog();
    }
    internal SettingsWindow CreatePlayerTextEditor(string category="All")
    {
        var original=selectedPlayerSettings.Typography??new();
        var dialog=AppearanceEditorUi.Window("Song title, artist & time text",this,590);
        var root=new DockPanel{Margin=new Thickness(18)};dialog.Content=root;
        var footer=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var panel=new StackPanel();root.Children.Add(MacroUi.Scroll(panel));
        var draft=original;bool committed=false;
        var choices=new[]{("All","All"),("Title","Song title"),("Artist","Artist"),("Time","Time numbers")};
        var selector=new ComboBox{Name="PlayerTextCategory",ItemsSource=choices.Select(x=>x.Item2).ToArray(),SelectedIndex=Array.FindIndex(choices,x=>x.Item1==category)};
        EditorUi.Field(panel,"Text element",selector);
        var fields=new StackPanel();panel.Children.Add(fields);
        void ShowFields(){fields.Children.Clear();string key=choices[Math.Max(0,selector.SelectedIndex)].Item1;
            var previous=draft.Get(key=="All"?"Title":key)??PlayerTypography.Default(key=="All"?"Title":key,musicConsoleEnabled);
            LabelTypography.Editor(fields,previous,value=>{
                foreach(string target in key=="All"?new[]{"Title","Artist","Time"}:new[]{key}){
                    var current=draft.Get(target)??PlayerTypography.Default(target,musicConsoleEnabled);
                    var changed=key!="All"?value:current with{
                        FontFamily=value.FontFamily!=previous.FontFamily?value.FontFamily:current.FontFamily,
                        Size=value.Size!=previous.Size?value.Size:current.Size,
                        Bold=value.Bold!=previous.Bold?value.Bold:current.Bold,
                        Italic=value.Italic!=previous.Italic?value.Italic:current.Italic};
                    draft=draft.With(target,changed);
                }
                previous=value;ApplyPlayerTypography(draft);
            },"PlayerTextSize");}
        selector.SelectionChanged+=(_,_)=>ShowFields();ShowFields();
        AppearanceEditorUi.Help(panel,"All applies each change to all three text elements on this tab. Save keeps changes; Cancel restores them.");
        var error=new TextBlock{Foreground=Brushes.OrangeRed,TextWrapping=TextWrapping.Wrap};panel.Children.Add(error);
        footer.Children.Add(MacroUi.Button("Default",(_,_)=>{string key=choices[Math.Max(0,selector.SelectedIndex)].Item1;foreach(string target in key=="All"?new[]{"Title","Artist","Time"}:new[]{key})draft=draft.With(target,null);ApplyPlayerTypography(draft);ShowFields();}));
        footer.Children.Add(MacroUi.Button("Save",(_,_)=>{try{ApplyPlayerTypography(draft);SaveWindowSettings();committed=true;dialog.DialogResult=true;}catch(Exception ex){error.Text=ex.Message;}}));
        footer.Children.Add(MacroUi.Button("Cancel",(_,_)=>dialog.Close()));
        dialog.Closed+=(_,_)=>{if(!committed)ApplyPlayerTypography(original);};AppearanceEditorUi.Frame(dialog);return dialog;
    }
}
