using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Ekrano_valdymas;

internal static class WeatherTranslationEditor
{
    internal static SettingsWindow Create(Window? owner,string language,Action<string> saved)=>CreateCore(owner,language,saved,false);
    internal static SettingsWindow CreateNew(Window? owner,Action<string> saved)=>CreateCore(owner,"",saved,true);
    private static SettingsWindow CreateCore(Window? owner,string language,Action<string> saved,bool newLanguage)
    {
        var store=new WeatherTranslationStore();string T(string text)=>store.Text(language,text);var window=AppearanceEditorUi.Window(T("Weather translations"),owner,900);
        window.Width=980;window.MinWidth=620;window.MinHeight=420;
        var root=new Grid{Margin=new Thickness(14,4,14,12)};
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new());root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var fields=new Grid{Margin=new Thickness(4,4,4,8)};
        fields.ColumnDefinitions.Add(new(){Width=new GridLength(180)});fields.ColumnDefinitions.Add(new());fields.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        fields.RowDefinitions.Add(new(){Height=new GridLength(24)});fields.RowDefinitions.Add(new(){Height=GridLength.Auto,MinHeight=38});
        OpticallyCenteredText Label(string text,double size=16)=>new(){Text=text,TextFontFamily=new FontFamily("Segoe UI"),TextFontWeight=FontWeights.Normal,TextFontSize=size,TextBrush=Brushes.White,InkAlignment=HorizontalAlignment.Left,Ellipsis=true};
        var code=new TextBox{Name="WeatherLanguageCode",MaxLength=35,IsReadOnly=!newLanguage,Margin=new Thickness(0,0,12,0),VerticalContentAlignment=VerticalAlignment.Center};
        var name=new TextBox{Name="WeatherLanguageName",MaxLength=80,VerticalContentAlignment=VerticalAlignment.Center};
        fields.Children.Add(Label(T("Language code")));var nameLabel=Label(T("Language name"));Grid.SetColumn(nameLabel,1);fields.Children.Add(nameLabel);
        Grid.SetRow(code,1);fields.Children.Add(code);Grid.SetRow(name,1);Grid.SetColumn(name,1);fields.Children.Add(name);
        var load=MacroUi.Button(T("Load"),(_,_)=>{});load.Name="WeatherTranslationLoad";load.Visibility=newLanguage?Visibility.Visible:Visibility.Collapsed;load.MinWidth=70;Grid.SetColumn(load,2);Grid.SetRow(load,1);fields.Children.Add(load);root.Children.Add(fields);
        var filters=new DockPanel{Margin=new Thickness(4,2,4,8)};
        var completion=new TextBlock{Name="WeatherTranslationCompletion",VerticalAlignment=VerticalAlignment.Center,Foreground=Brushes.LightSteelBlue};DockPanel.SetDock(completion,Dock.Right);filters.Children.Add(completion);
        var missingOnly=new CheckBox{Name="WeatherTranslationMissingOnly",Content=T("Only missing translations"),Foreground=window.Foreground,MinHeight=32,VerticalContentAlignment=VerticalAlignment.Center,Style=(Style)Application.Current.FindResource("DashboardCheckBox")};filters.Children.Add(missingOnly);Grid.SetRow(filters,1);root.Children.Add(filters);
        var columns=new Grid{Margin=new Thickness(4,0,22,5)};columns.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});columns.ColumnDefinitions.Add(new(){Width=new GridLength(1.35,GridUnitType.Star)});
        columns.Children.Add(Label(T("English original (read-only)")));var translationTitle=Label(T("Translation · blank uses English"));Grid.SetColumn(translationTitle,1);columns.Children.Add(translationTitle);Grid.SetRow(columns,2);root.Children.Add(columns);
        var list=new StackPanel();var scroll=MacroUi.Scroll(list);scroll.Name="WeatherTranslationScroll";Grid.SetRow(scroll,3);root.Children.Add(scroll);window.AutoFitScroll=scroll;
        var inputs=new Dictionary<string,TextBox>(StringComparer.Ordinal);var originals=new List<OpticallyCenteredText>();
        var rowContainers=new Dictionary<string,Grid>(StringComparer.Ordinal);var groupLabels=new Dictionary<string,OpticallyCenteredText>(StringComparer.Ordinal);var groupEntries=new Dictionary<string,List<string>>(StringComparer.Ordinal);bool changing=false;
        var noMissing=new TextBlock{Name="WeatherTranslationNoMissing",Text=T("No missing translations."),Foreground=Brushes.LightSteelBlue,Margin=new Thickness(4,12,4,12),Visibility=Visibility.Collapsed};
        string? group=null;
        foreach(var entry in WeatherTranslationStore.Entries)
        {
            if(group!=entry.Group){group=entry.Group;var heading=Label(group,15);heading.Height=30;heading.TextBrush=Brushes.LightSteelBlue;heading.Margin=new Thickness(4,6,4,0);list.Children.Add(heading);groupLabels.Add(group,heading);groupEntries.Add(group,new());}
            var row=new Grid{MinHeight=37,Margin=new Thickness(4,1,4,1)};row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1.35,GridUnitType.Star)});
            var original=Label(entry.English,15);original.ToolTip=entry.English;original.Margin=new Thickness(0,0,12,0);original.Name="WeatherEnglishOriginal";originals.Add(original);row.Children.Add(original);
            var input=new TextBox{Tag=entry.English,MaxLength=500,MinHeight=34,VerticalContentAlignment=VerticalAlignment.Center,ToolTip=entry.English,Padding=new Thickness(7,3,7,3)};Grid.SetColumn(input,1);row.Children.Add(input);inputs.Add(entry.English,input);list.Children.Add(row);rowContainers.Add(entry.English,row);groupEntries[group!].Add(entry.English);
            input.TextChanged+=(_,_)=>{if(!changing)RefreshFilter();};input.LostKeyboardFocus+=(_,_)=>window.Dispatcher.BeginInvoke(new Action(RefreshFilter),System.Windows.Threading.DispatcherPriority.Input);
        }
        list.Children.Add(noMissing);
        void RefreshFilter()
        {
            int missing=inputs.Values.Count(input=>string.IsNullOrWhiteSpace(input.Text));completion.Text=T("Translated")+": "+(inputs.Count-missing)+"/"+inputs.Count+" · "+T("Missing")+": "+missing;
            foreach(var pair in inputs)rowContainers[pair.Key].Visibility=missingOnly.IsChecked!=true||string.IsNullOrWhiteSpace(pair.Value.Text)||pair.Value.IsKeyboardFocusWithin?Visibility.Visible:Visibility.Collapsed;
            foreach(var pair in groupEntries)groupLabels[pair.Key].Visibility=pair.Value.Any(key=>rowContainers[key].Visibility==Visibility.Visible)?Visibility.Visible:Visibility.Collapsed;
            noMissing.Visibility=missingOnly.IsChecked==true&&missing==0?Visibility.Visible:Visibility.Collapsed;
        }
        missingOnly.Checked+=(_,_)=>RefreshFilter();missingOnly.Unchecked+=(_,_)=>RefreshFilter();
        var footer=new Grid{Margin=new Thickness(0,8,0,0)};footer.RowDefinitions.Add(new(){Height=GridLength.Auto});footer.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var message=new TextBlock{Name="WeatherTranslationMessage",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.Salmon,Margin=new Thickness(4,0,4,5)};footer.Children.Add(message);
        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};Grid.SetRow(buttons,1);footer.Children.Add(buttons);Grid.SetRow(footer,4);root.Children.Add(footer);window.Content=root;
        string current="";var drafts=new Dictionary<string,WeatherLanguageDictionary>(StringComparer.Ordinal);var hiddenTranslations=new Dictionary<string,string>(StringComparer.Ordinal);
        WeatherLanguageDictionary Draft()=>new(){Code=current,Name=name.Text,Translations=hiddenTranslations.Concat(inputs.Select(p=>new KeyValuePair<string,string>(p.Key,p.Value.Text))).ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal)};
        void Remember(){if(current.Length>0)drafts[current]=Draft();}
        void Show(WeatherLanguageDictionary dictionary)
        {
            changing=true;current=dictionary.Code;code.Text=current;name.Text=dictionary.Name;
            hiddenTranslations=dictionary.Translations.Where(p=>!inputs.ContainsKey(p.Key)).ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal);
            foreach(var pair in inputs)pair.Value.Text=dictionary.Translations.GetValueOrDefault(pair.Key)??"";
            changing=false;message.Text=current=="en"?T("English is the original. Choose another code to save a translation."):"";RefreshFilter();
        }
        bool LoadCode()
        {
            if(changing)return true;
            try
            {
                string requested=WeatherTranslationStore.NormalizeCode(code.Text);if(newLanguage&&(requested is "en" or "lt"))throw new ArgumentException("English is read-only. Use the Lithuanian pencil to edit Lithuanian.");
                if(requested==current){message.Text="";return true;}
                // A new dictionary may already contain pasted fields before
                // its code loses focus. Assign its identity without erasing it.
                if(newLanguage&&current.Length==0&&(!string.IsNullOrWhiteSpace(name.Text)||inputs.Values.Any(input=>!string.IsNullOrWhiteSpace(input.Text))))
                {current=requested;code.Text=current;message.Text="";RefreshFilter();return true;}
                Remember();Show(drafts.TryGetValue(requested,out var draft)?draft:store.LoadLanguage(requested));return true;
            }
            catch(ArgumentException exception){message.Text=T(exception.Message);return false;}
        }
        load.Click+=(_,_)=>LoadCode();code.LostKeyboardFocus+=(_,_)=>LoadCode();code.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;LoadCode();}};
        var import=MacroUi.Button(T("Import JSON…"),(_,_)=>
        {
            var dialog=new OpenFileDialog{Title=T("Import weather translation"),Filter="JSON (*.json)|*.json",CheckFileExists=true};
            if(dialog.ShowDialog(window)!=true)return;
            try{if(new FileInfo(dialog.FileName).Length>1_000_000)throw new ArgumentException("The translation file is too large.");var dictionary=WeatherTranslationStore.Import(File.ReadAllText(dialog.FileName));if(!newLanguage&&dictionary.Code!=current)throw new ArgumentException("The imported language code must match the language being edited.");if(newLanguage&&(dictionary.Code is "en" or "lt"))throw new ArgumentException("Use the Lithuanian pencil to edit Lithuanian.");Remember();drafts[dictionary.Code]=dictionary;Show(dictionary);}
            catch(Exception exception)when(exception is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException){message.Text=T(exception.Message);}
        });
        var export=MacroUi.Button(T("Export JSON…"),(_,_)=>
        {
            if(!LoadCode())return;var dialog=new SaveFileDialog{Title=T("Export weather translation"),Filter="JSON (*.json)|*.json",FileName="weather-"+current+".json",DefaultExt=".json",AddExtension=true};
            if(dialog.ShowDialog(window)!=true)return;
            try{File.WriteAllText(dialog.FileName,WeatherTranslationStore.Export(Draft()));message.Text=T("Exported.");}
            catch(Exception exception)when(exception is IOException or UnauthorizedAccessException or ArgumentException){message.Text=T(exception.Message);}
        });
        var save=MacroUi.Button(T("Save and use"),(_,_)=>
        {
            if(!LoadCode())return;
            try{store.SaveLanguage(Draft());saved(current);window.Close();}
            catch(Exception exception)when(exception is IOException or UnauthorizedAccessException or ArgumentException){message.Text=T(exception.Message);}
        });save.Name="WeatherTranslationSaveAndUse";save.IsDefault=true;save.SetResourceReference(Control.BackgroundProperty,"ActionFillBrush");save.SetResourceReference(Control.BorderBrushProperty,"ActionAccentBrush");
        var cancel=MacroUi.Button(T("Cancel"),(_,_)=>window.Close());cancel.Name="WeatherTranslationCancel";cancel.IsCancel=true;
        buttons.Children.Add(import);buttons.Children.Add(export);buttons.Children.Add(cancel);buttons.Children.Add(save);
        if(newLanguage)Show(new(){Code="",Name="",Translations=WeatherTranslationStore.Entries.ToDictionary(entry=>entry.English,_=>"",StringComparer.Ordinal)});
        else try{Show(store.LoadLanguage(language));}catch(ArgumentException){Show(store.LoadLanguage("lt"));}
        // English is a source column, never an editable input. The editable
        // dictionary is isolated per code, including unsaved in-window drafts.
        foreach(var original in originals)original.Focusable=false;
        AppearanceEditorUi.Frame(window,420,620,compact:false);DialogWorkArea.Attach(window,620,420);return window;
    }
}
