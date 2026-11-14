using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
namespace Ekrano_valdymas;
public partial class MainWindow
{
    private readonly List<WeatherLocation> weatherLocations = new();
    private string? activeWeatherLocationId, editingWeatherId;
    private bool weatherLookupBusy, fillingWeatherFields, weatherNameEdited;
    private int weatherEditRevision;
    private WeatherLocation? weatherDraft;
    private readonly TextBox weatherNameEditor = new() { Height=32,FontSize=17,Padding=new Thickness(10,2,10,2),VerticalContentAlignment=VerticalAlignment.Center,MaxLength=80 };
    private readonly TextBlock weatherLocationMessage = new() { MaxWidth=468,TextWrapping=TextWrapping.Wrap,FontSize=14,FontWeight=FontWeights.Normal,Foreground=Brushes.LightGreen };
    private MenuItem weatherRenameRow=null!,weatherRemoveRow=null!,weatherMessageRow=null!;
    private Grid weatherNameRowGrid=null!;
    private Button weatherRenameButton=null!;
    private readonly List<MenuItem> weatherSavedRows=new();
    private WeatherLocation? ActiveWeatherLocation=>weatherLocations.FirstOrDefault(p=>p.Id==activeWeatherLocationId);
    private static Brush WeatherMarkerBrush(WeatherLocation place)=>place.NameEdited?AdaptiveSensorColors.PencilBrush:Brushes.DeepSkyBlue;

    private static void StyleWeatherEditorRow(MenuItem row)
    {
        // Form rows must not inherit the checkmark gutter or a whole-row hover surface.
        var border=new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.PaddingProperty,new Thickness(8,0,8,0));
        var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(ContentPresenter.ContentSourceProperty,"Header");border.AppendChild(content);
        row.Template=new ControlTemplate(typeof(MenuItem)){VisualTree=border};
    }
    private Button WeatherDeleteButton(string tooltip)
    {
        var button=new Button{Width=28,Height=28,Padding=new Thickness(0),Style=cityApplyButton.Style,
            Background=Brushes.Transparent,ToolTip=WeatherText(tooltip),Tag=tooltip};
        button.Content=new OpticallyCenteredText{Text="\uE74D",TextFontFamily=new FontFamily("Segoe MDL2 Assets"),TextFontWeight=FontWeights.Normal,TextFontSize=14,TextBrush=Brushes.Salmon,Width=18,Height=18};
        return button;
    }
    private void ApplyWeatherEditorMetrics()
    {
        if(weatherNameRowGrid==null)return;
        double factor=1.56*menuTextPercent/100; double font=19.6*factor; double height=Application.Current.TryFindResource("UnifiedMenuRowHeight") is double h?h:40;
        double label=126*factor,input=204*factor,action=80*factor,clear=34;
        double width=label+input+action+clear;
        foreach(var row in new[]{cityRowGrid,coordinatesRowGrid,weatherNameRowGrid}){
            row.Width=width;
            double[] widths=[label,input,action,clear];
            for(int i=0;i<4;i++)row.ColumnDefinitions[i].Width=new GridLength(widths[i]);
            foreach(var text in row.Children.OfType<TextBlock>())text.FontSize=font;
        }
        foreach(var inputBox in new[]{cityTextBox,coordinatesTextBox,weatherNameEditor}){inputBox.Height=height-2;inputBox.FontSize=font;}
        foreach(var button in new[]{cityApplyButton,coordinatesApplyButton,weatherRenameButton}){button.Height=height-2;button.MinWidth=0;button.FontSize=font;button.Padding=new Thickness(6,0,6,0);}
        foreach(var row in weatherSavedRows){
            if(row.Header is Grid grid){grid.Width=width;grid.ColumnDefinitions[1].Width=new GridLength(clear);}
        }
    }

    private void InitializeWeatherLocations()
    {
        weatherNameEditor.Background=cityTextBox.Background;weatherNameEditor.Foreground=cityTextBox.Foreground;
        weatherNameEditor.BorderBrush=cityTextBox.BorderBrush;weatherNameEditor.BorderThickness=cityTextBox.BorderThickness;
        weatherRenameButton=new Button { Content="Save",Style=cityApplyButton.Style,Height=32,Margin=new Thickness(8,0,0,0),Background=(Brush)FindResource("ActionFillBrush"),BorderBrush=(Brush)FindResource("ActionAccentBrush"),BorderThickness=new Thickness(1) };
        weatherRenameButton.Click+=async(_,e)=>{e.Handled=true;await SaveWeatherDraftAsync();};
        weatherNameEditor.KeyDown+=async(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;await SaveWeatherDraftAsync();}};
        weatherNameRowGrid=new Grid {Width=508};
        foreach(double width in new[]{170d,220,78,40})weatherNameRowGrid.ColumnDefinitions.Add(new(){Width=new GridLength(width)});
        weatherNameRowGrid.Children.Add(new TextBlock{Text="Display name",VerticalAlignment=VerticalAlignment.Center});
        Grid.SetColumn(weatherNameEditor,1);weatherNameRowGrid.Children.Add(weatherNameEditor);
        Grid.SetColumn(weatherRenameButton,2);weatherNameRowGrid.Children.Add(weatherRenameButton);
        weatherRenameRow=new MenuItem{Header=weatherNameRowGrid,StaysOpenOnClick=true};
        weatherOptions!.Items.Add(new Separator());weatherOptions.Items.Add(weatherRenameRow);
        weatherMessageRow=new MenuItem{Header=weatherLocationMessage,IsHitTestVisible=false,Focusable=false,Visibility=Visibility.Collapsed};weatherOptions.Items.Add(weatherMessageRow);
        weatherOptions.Items.Add(new Separator());
        weatherRemoveRow=new MenuItem{Header=new TextBlock{Text="SAVED PLACES",FontSize=14,FontWeight=FontWeights.SemiBold,Foreground=Brushes.Silver},IsHitTestVisible=false,Focusable=false};
        weatherOptions.Items.Add(weatherRemoveRow);
        foreach(var pair in new[]{(cityRowGrid,cityTextBox),(coordinatesRowGrid,coordinatesTextBox)})
        {
            pair.Item1.ColumnDefinitions.Add(new(){Width=new GridLength(40)});
            var clear=WeatherDeleteButton("Clear input");
            Grid.SetColumn(clear,3);pair.Item1.Children.Add(clear);
            clear.Click+=(_,e)=>{e.Handled=true;pair.Item2.Clear();};
        }
        foreach(var button in new[]{cityApplyButton,coordinatesApplyButton,weatherRenameButton}){button.SetResourceReference(Control.BackgroundProperty,"ActionFillBrush");button.SetResourceReference(Control.BorderBrushProperty,"ActionAccentBrush");button.BorderThickness=new Thickness(1);}
        cityTextBox.TextChanged+=(_,_)=>{
            if(fillingWeatherFields)return;
            weatherEditRevision++;weatherDraft=null;
            fillingWeatherFields=true;coordinatesTextBox.Clear();fillingWeatherFields=false;
        };
        coordinatesTextBox.TextChanged+=async(_,_)=>{
            if(fillingWeatherFields)return;
            int revision=++weatherEditRevision;weatherDraft=null;
            string input=coordinatesTextBox.Text;
            if(!WeatherLocation.TryCoordinates(input,out _,out _))return;
            await Task.Delay(650);if(revision!=weatherEditRevision)return;
            try{var place=await ResolveWeatherDraftAsync(true,input);if(revision!=weatherEditRevision)return;
                weatherDraft=place;FillWeatherResolvedName(place);WeatherMessage("");}
            catch{if(revision==weatherEditRevision)WeatherMessage("Could not find the place. Check coordinates or try Add again.",true);}
        };
        weatherNameEditor.TextChanged+=(_,_)=>{if(!fillingWeatherFields)weatherNameEdited=true;};
        weatherOptions.Opened+=(_,_)=>{RefreshWeatherLocationTranslations();ResetWeatherEditor();};
        weatherOptions.Closed+=(_,_)=>{weatherEditRevision++;};
        weatherWidget.ClearSavedLocation();
        foreach(var row in new[]{cityLocationMenuItem,coordinatesLocationMenuItem,weatherRenameRow,weatherMessageRow,weatherRemoveRow})StyleWeatherEditorRow(row);
        weatherRemoveRow.Margin=new Thickness(0,8,0,4);
        UpdateWeatherSourceStatus();RefreshWeatherSavedRows();ApplyWeatherEditorMetrics();
    }
    private void WeatherMessage(string text,bool error=false)
    {weatherLocationMessage.Tag=text;weatherLocationMessage.Text=WeatherText(text);weatherLocationMessage.Foreground=error?Brushes.Salmon:Brushes.LightGreen;weatherMessageRow.Visibility=text.Length==0?Visibility.Collapsed:Visibility.Visible;}
    private void ResetWeatherEditor()
    {
        weatherEditRevision++;editingWeatherId=null;weatherDraft=null;weatherNameEdited=false;
        fillingWeatherFields=true;cityTextBox.Clear();coordinatesTextBox.Clear();weatherNameEditor.Clear();fillingWeatherFields=false;
        WeatherMessage("");RefreshWeatherSavedRows();UpdateWeatherSourceStatus();
    }
    private void FillWeatherResolvedName(WeatherLocation place)
    {
        fillingWeatherFields=true;cityTextBox.Text=place.AutomaticName;
        if(!weatherNameEdited)weatherNameEditor.Text=place.Name;
        fillingWeatherFields=false;
    }
    private void EditWeatherPlace(WeatherLocation place)
    {
        weatherEditRevision++;editingWeatherId=place.Id;weatherDraft=CloneWeatherPlace(place);weatherNameEdited=place.NameEdited;
        fillingWeatherFields=true;cityTextBox.Text=place.AutomaticName;coordinatesTextBox.Text=place.FromCoordinates?place.Coordinates:"";weatherNameEditor.Text=place.Name;fillingWeatherFields=false;
        SelectWeatherLocation(place);WeatherMessage("");
    }
    private static WeatherLocation CloneWeatherPlace(WeatherLocation place)=>System.Text.Json.JsonSerializer.Deserialize<WeatherLocation>(System.Text.Json.JsonSerializer.Serialize(place))!;
    private Task<WeatherLocation> ResolveWeatherDraftAsync(bool coordinates,string input)
    {
        var saved=coordinates&&WeatherLocation.TryCoordinates(input,out double lat,out double lon)
            ?weatherLocations.FirstOrDefault(p=>p.FromCoordinates&&p.Latitude==lat&&p.Longitude==lon)
            :!coordinates?weatherLocations.FirstOrDefault(p=>!p.FromCoordinates&&string.Equals(p.AutomaticName,input.Trim(),StringComparison.OrdinalIgnoreCase)):null;
        return saved==null?WeatherLocationLookup.Resolve(input,coordinates,weatherWidget.DisplayLanguage):Task.FromResult(CloneWeatherPlace(saved));
    }
    private Task ApplyWeatherLocationAsync(bool useCoordinates)=>CommitWeatherDraftAsync(useCoordinates||!string.IsNullOrWhiteSpace(coordinatesTextBox.Text),false);
    private Task SaveWeatherDraftAsync()=>CommitWeatherDraftAsync(!string.IsNullOrWhiteSpace(coordinatesTextBox.Text),true);
    private async void RenameWeatherLocation()=>await SaveWeatherDraftAsync();
    private async Task CommitWeatherDraftAsync(bool coordinates,bool edit)
    {
        if(weatherLookupBusy)return;
        if(edit&&string.IsNullOrWhiteSpace(weatherNameEditor.Text)){WeatherMessage("Enter a display name.",true);return;}
        int revision=weatherEditRevision;weatherLookupBusy=true;UpdateWeatherSourceStatus();WeatherMessage("Finding place…");
        try {
            string input=coordinates?coordinatesTextBox.Text:cityTextBox.Text;
            var place=weatherDraft!=null?CloneWeatherPlace(weatherDraft):await ResolveWeatherDraftAsync(coordinates,input);
            if(revision!=weatherEditRevision)return;
            place.FromCoordinates=coordinates;
            if(weatherNameEdited&&!string.IsNullOrWhiteSpace(weatherNameEditor.Text))place.Rename(weatherNameEditor.Text);
            var previous=edit?weatherLocations.FirstOrDefault(p=>p.Id==editingWeatherId):null;
            previous??=weatherLocations.FirstOrDefault(p=>p.Latitude==place.Latitude&&p.Longitude==place.Longitude&&p.FromCoordinates==place.FromCoordinates);
            if(previous!=null){place.Id=previous.Id;weatherLocations[weatherLocations.IndexOf(previous)]=place;}
            else{place.Id=Guid.NewGuid().ToString("N");weatherLocations.Add(place);}
            editingWeatherId=place.Id;weatherDraft=CloneWeatherPlace(place);SelectWeatherLocation(place);
            fillingWeatherFields=true;cityTextBox.Text=place.AutomaticName;coordinatesTextBox.Text=coordinates?place.Coordinates:"";weatherNameEditor.Text=place.Name;fillingWeatherFields=false;
            WeatherMessage("Saved");RefreshWeatherSavedRows();
        }catch(ArgumentException ex){WeatherMessage(ex.Message,true);}catch{WeatherMessage("Could not find the place. Check your connection and try again.",true);}
        finally{weatherLookupBusy=false;UpdateWeatherSourceStatus();}
    }
    private void DeleteWeatherPlace(WeatherLocation place)
    {
        if(weatherLookupBusy)return;
        weatherLocations.Remove(place);
        if(activeWeatherLocationId==place.Id){activeWeatherLocationId=weatherLocations.FirstOrDefault()?.Id;if(ActiveWeatherLocation is {} next)weatherWidget.SetSavedLocation(next);else weatherWidget.ClearSavedLocation();}
        if(editingWeatherId==place.Id)ResetWeatherEditor();
        RefreshWeatherSavedRows();SaveWindowSettings();
    }
    private void RefreshWeatherSavedRows()
    {
        foreach(var row in weatherSavedRows)weatherOptions!.Items.Remove(row);weatherSavedRows.Clear();
        foreach(var place in weatherLocations){
            var grid=new Grid{Width=508};grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=new GridLength(40)});
            var header=WeatherPlaceHeader(place);
            var select=new Button{Content=header,Style=cityApplyButton.Style,HorizontalContentAlignment=HorizontalAlignment.Left,Background=Brushes.Transparent,Height=40,FontSize=18,ToolTip=place.DescriptionFor(weatherWidget.DisplayLanguage)};
            // The shared action template centers content, so explicitly bind the header to its available row width.
            header.SetBinding(FrameworkElement.WidthProperty,new System.Windows.Data.Binding("ActualWidth"){Source=select});
            select.Click+=(_,e)=>{e.Handled=true;EditWeatherPlace(place);};grid.Children.Add(select);
            var remove=WeatherDeleteButton("Delete saved place");
            remove.Click+=(_,e)=>{e.Handled=true;DeleteWeatherPlace(place);};Grid.SetColumn(remove,1);grid.Children.Add(remove);
            var row=new MenuItem{Header=grid,StaysOpenOnClick=true};StyleWeatherEditorRow(row);weatherSavedRows.Add(row);weatherOptions!.Items.Add(row);
        }
        var attribution=new MenuItem{Header=new TextBlock{Text=WeatherText("Place names")+": © OpenStreetMap contributors · Photon / Open-Meteo",FontSize=12,FontWeight=FontWeights.Normal,Foreground=Brushes.Gray},Margin=new Thickness(0,8,0,2),IsHitTestVisible=false,Focusable=false};
        StyleWeatherEditorRow(attribution);weatherSavedRows.Add(attribution);weatherOptions!.Items.Add(attribution);ApplyWeatherEditorMetrics();
    }
    private static FrameworkElement WeatherPlaceHeader(WeatherLocation place)
    {
        var panel=new StackPanel{Orientation=Orientation.Horizontal};
        if(place.Icon.Length>0)panel.Children.Add(new OpticallyCenteredText{Text=place.Icon,TextBrush=WeatherMarkerBrush(place),TextFontFamily=new FontFamily("Segoe MDL2 Assets"),TextFontSize=16,Width=20,Height=24,Margin=new Thickness(0,0,8,0)});
        panel.Children.Add(new TextBlock{Text=place.Name,MaxWidth=400,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center});return panel;
    }
    private void RestoreWeatherLocations(WindowSettings settings)
    {
        weatherLocations.Clear();
        if(settings.WeatherLocations!=null)weatherLocations.AddRange(settings.WeatherLocations.Where(p=>!string.IsNullOrWhiteSpace(p.Name)&&(p.Latitude==null&&p.Longitude==null||p.Latitude is >=-90 and <=90&&p.Longitude is >=-180 and <=180)).DistinctBy(p=>p.Id));
        else if(!string.IsNullOrWhiteSpace(settings.City)) {
            string name=settings.City.Trim();var legacy=new WeatherLocation{Name=name,AutomaticName=name};
            if(WeatherLocation.TryCoordinates(settings.Coordinates,out double lat,out double lon)){legacy.Latitude=lat;legacy.Longitude=lon;legacy.FromCoordinates=true;legacy.NameEdited=true;}weatherLocations.Add(legacy);
        }
        activeWeatherLocationId=weatherLocations.FirstOrDefault(p=>p.Id==settings.ActiveWeatherLocationId)?.Id??weatherLocations.FirstOrDefault()?.Id;
        if(ActiveWeatherLocation is {} selected)weatherWidget.SetSavedLocation(selected);else weatherWidget.ClearSavedLocation();
        ResetWeatherEditor();
    }
    private void SelectWeatherLocation(WeatherLocation place)
    {activeWeatherLocationId=place.Id;weatherWidget.SetSavedLocation(place);SaveWindowSettings();}
    private void UpdateWeatherSourceStatus()
    {
        cityLocationMenuItem.IsChecked=coordinatesLocationMenuItem.IsChecked=false;
        cityApplyButton.Content=coordinatesApplyButton.Content=WeatherText("Add");
        cityApplyButton.IsEnabled=coordinatesApplyButton.IsEnabled=!weatherLookupBusy;
        if(weatherRenameRow!=null)weatherRenameRow.IsEnabled=!weatherLookupBusy;
    }
    internal void OpenWeatherLocations(FrameworkElement anchor)
    {
        if(weatherLocations.Count==0)
        {
            OpenModuleOptions(weatherWidget,anchor);
            return;
        }
        var menu=NewModuleMenu();foreach(var place in weatherLocations){var item=new MenuItem{Header=WeatherPlaceHeader(place),IsCheckable=true,IsChecked=place.Id==activeWeatherLocationId,ToolTip=place.DescriptionFor(weatherWidget.DisplayLanguage)};item.Click+=(_,_)=>SelectWeatherLocation(place);menu.Items.Add(item);}
        anchor.ContextMenu=menu;menu.PlacementTarget=anchor;menu.Placement=PlacementMode.Bottom;menu.IsOpen=menu.Items.Count>0;
    }
}
