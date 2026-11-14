using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

public partial class MainWindow
{
    private WeatherLanguageSwitch? weatherLanguageChoice;
    private bool fillingWeatherLanguageChoice;
    private MenuItem? weatherDecimalChoice;
    private string WeatherText(string english)=>weatherWidget.Translate(english);
    private void InitializeWeatherTranslations()
    {
        var row=new Grid{Width=468};
        weatherLanguageChoice=new WeatherLanguageSwitch();
        weatherLanguageChoice.SelectionChanged+=(_,_)=>{if(!fillingWeatherLanguageChoice){weatherWidget.SetDisplayLanguage(weatherLanguageChoice.SelectedLanguage);SaveWindowSettings();}};
        row.Children.Add(weatherLanguageChoice);
        void Saved(string code){weatherWidget.SetDisplayLanguage(code);SaveWindowSettings();}
        weatherLanguageChoice.EditRequested+=code=>{weatherOptions!.IsOpen=false;WeatherTranslationEditor.Create(this,code,Saved).ShowDialog();};
        weatherLanguageChoice.AddRequested+=()=>{weatherOptions!.IsOpen=false;WeatherTranslationEditor.CreateNew(this,Saved).ShowDialog();};
        weatherLanguageChoice.RemoveRequested+=code=>
        {
            weatherOptions!.IsOpen=false;
            if(AppMessage.Show(this,"Remove this language from the choices? Its saved translations will be kept.","Weather languages",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            try
            {
                var store=new WeatherTranslationStore();if(!store.RemoveCustomLanguage(code))return;
                if(weatherWidget.DisplayLanguage==code)Saved("lt");else RefreshWeatherLanguageChoices();
            }
            catch(Exception error)when(error is System.IO.IOException or UnauthorizedAccessException)
            {
                InteractionDiagnostics.RecordFailure("Weather language choices",error);
                AppMessage.Show(this,"The language choices could not be saved. Your translations are unchanged.","Weather languages",MessageBoxButton.OK,MessageBoxImage.Warning);
            }
        };
        var menuRow=new MenuItem{Header=row,StaysOpenOnClick=true};StyleWeatherEditorRow(menuRow);weatherOptions!.Items.Insert(0,menuRow);
        weatherDecimalChoice=new MenuItem{Name="WeatherTemperatureDecimals",IsCheckable=true,StaysOpenOnClick=true};
        weatherDecimalChoice.Click+=(_,_)=>{weatherWidget.SetTemperatureDecimals(weatherDecimalChoice.IsChecked);SaveWindowSettings();};
        weatherOptions.Items.Insert(1,weatherDecimalChoice);
        weatherOptions.Opened+=(_,_)=>RefreshWeatherLanguageChoices();
        weatherWidget.DisplayLanguageChanged+=(_,_)=>{RefreshWeatherLanguageChoices();RefreshWeatherLocationTranslations();};RefreshWeatherLanguageChoices();
    }
    private void RefreshWeatherLanguageChoices()
    {
        if(weatherDecimalChoice!=null){weatherDecimalChoice.Header=WeatherText("Temperature: one decimal place");weatherDecimalChoice.IsChecked=weatherWidget.ShowTemperatureDecimals;}
        if(weatherLanguageChoice==null)return;fillingWeatherLanguageChoice=true;
        try
        {
            var store=new WeatherTranslationStore();
            if(weatherWidget.DisplayLanguage!="en")
            {
                try{store.RememberTranslationLanguage(weatherWidget.DisplayLanguage);}
                catch(Exception error)when(error is System.IO.IOException or UnauthorizedAccessException){InteractionDiagnostics.RecordFailure("Weather language preference",error);}
            }
            weatherLanguageChoice.SetChoices(store.SelectableLanguages,weatherWidget.DisplayLanguage);
        }
        finally{fillingWeatherLanguageChoice=false;}
    }
    private void RefreshWeatherLocationTranslations()
    {
        cityMenuLabel.Text=WeatherText("City");coordinatesMenuLabel.Text=WeatherText("Coordinates");
        cityTextBox.ToolTip=WeatherText("Enter a city, then choose Add");coordinatesTextBox.ToolTip=WeatherText("Latitude, longitude");
        cityApplyButton.Content=coordinatesApplyButton.Content=WeatherText("Add");
        if(weatherNameRowGrid==null)return;
        foreach(var label in weatherNameRowGrid.Children.OfType<TextBlock>())label.Text=WeatherText("Display name");
        weatherRenameButton.Content=WeatherText("Save");
        if(weatherRemoveRow.Header is TextBlock heading)heading.Text=WeatherText("SAVED PLACES");
        foreach(var grid in new[]{cityRowGrid,coordinatesRowGrid})foreach(var button in grid.Children.OfType<Button>())if(button.Tag is string key)button.ToolTip=WeatherText(key);
        if(weatherLocationMessage.Tag is string message)weatherLocationMessage.Text=WeatherText(message);
        if(ActiveWeatherLocation is {} place)weatherWidget.SetLocationTranslationTooltip(place);
        RefreshWeatherSavedRows();
    }
}
