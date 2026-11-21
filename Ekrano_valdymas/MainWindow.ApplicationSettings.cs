using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

public partial class MainWindow
{
    private bool exportingSettings;
    private async void ExportSettings_Click(object sender,RoutedEventArgs e)
    {
        ContextMenu.IsOpen=false;
        if(exportingSettings)return;
        var dialog=new Microsoft.Win32.SaveFileDialog {Filter="BlakiDeck backup (*.bdbackup)|*.bdbackup",FileName=$"BlakiDeck-{DateTime.Today:yyyy-MM-dd}.bdbackup"};
        if(dialog.ShowDialog(this)!=true)return;
        exportingSettings=true;
        try
        {
            SaveWindowSettings();
            await Task.Run(()=>SettingsBackup.Export(settingsFolder,dialog.FileName));
            AppMessage.Show(this,"Settings backup saved.","Export",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        catch(Exception ex){AppMessage.Show(this,ex.Message,"Export",MessageBoxButton.OK,MessageBoxImage.Error);}
        finally{exportingSettings=false;}
    }
    private void ImportSettings_Click(object sender,RoutedEventArgs e)
    {
        ContextMenu.IsOpen=false;
        if(exportingSettings)return;
        var dialog=new Microsoft.Win32.OpenFileDialog {Filter="BlakiDeck backup (*.bdbackup)|*.bdbackup"};
        if(dialog.ShowDialog(this)!=true)return;
        if(AppMessage.Show(this,"Replace the current settings and restart BlakiDeck? Only import backups you trust. They can include macros, shortcuts and their trigger keys.","Import settings",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        try
        {
            using var replacement=Process.Start(ApplicationProfileReset.CreateRestart(ProfileResetScope.Import,dialog.FileName)) ?? throw new InvalidOperationException("Could not restart BlakiDeck.");
            Application.Current.Shutdown();
        }
        catch(Exception ex){AppMessage.Show(this,ex.Message,"Import",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
    private void ResetLayout_Click(object sender,RoutedEventArgs e)=>ConfirmProfileReset(ProfileResetScope.Layout);
    private void FactoryReset_Click(object sender,RoutedEventArgs e)=>ConfirmProfileReset(ProfileResetScope.Full);

    internal SettingsWindow CreateResetConfirmation(bool full)
    {
        var dialog=AppearanceEditorUi.Window(full?"Full factory reset":"Reset layout",this,340);
        dialog.Width=610;dialog.MinWidth=420;dialog.MinHeight=260;
        var panel=new StackPanel { Margin=new Thickness(20) };
        panel.Children.Add(new TextBlock { Text=full
            ? "Delete all BlakiDeck saved data?\n\nThis removes all tabs, layouts, shortcuts, settings, saved sign-ins and local weather translations. The application will restart with 4 tabs."
            : "Reset the layout of all tabs?\n\nThis clears module arrangements and assigned shortcuts, then restores 4 tabs with three empty shortcut modules and a wide clock. Accounts, weather translations and other general settings are kept.",
            FontFamily=new FontFamily("Segoe UI"),FontSize=18,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.White });
        var buttons=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0) };
        var cancel=MacroUi.Button("Cancel",(_,_)=>dialog.DialogResult=false);cancel.IsDefault=true;cancel.IsCancel=true;
        buttons.Children.Add(cancel);buttons.Children.Add(MacroUi.Button(full?"Delete all data and restart":"Reset layout and restart",(_,_)=>dialog.DialogResult=true));
        panel.Children.Add(buttons);dialog.Content=panel;AppearanceEditorUi.Frame(dialog,260,420);return dialog;
    }

    private void ConfirmProfileReset(ProfileResetScope scope)
    {
        if(CreateResetConfirmation(scope==ProfileResetScope.Full).ShowDialog()!=true)return;
        if(DiagnosticState.Root!=null)return; // Isolated previews never restart or reset a real profile.
        try
        {
            var start=ApplicationProfileReset.CreateRestart(scope);
            using var replacement=Process.Start(start) ?? throw new InvalidOperationException("Could not restart BlakiDeck.");
            Application.Current.Shutdown();
        }
        catch(Exception error)
        {
            InteractionDiagnostics.RecordFailure("Profile reset restart",error);
            MessageBox.Show(this,error.Message,"Reset could not start",MessageBoxButton.OK,MessageBoxImage.Error);
        }
    }
}
