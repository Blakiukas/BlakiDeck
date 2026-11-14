using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace Ekrano_valdymas;

public partial class MainWindow
{
    private sealed record ResourceModeSettings(ResourceMode Mode=ResourceMode.Normal,bool GameAuto=true);
    private ResourceModeSettings resourceSettings=new();
    private readonly GameApplicationDetector gameDetector=new();
    private readonly DispatcherTimer resourceModeTimer=new(DispatcherPriority.Background){Interval=TimeSpan.FromSeconds(2)};
    private readonly Dictionary<ResourceMode,MenuItem> resourceModeItems=new();
    private CheckBox? resourceAutoItem;
    private string? detectedGame;
    private GameApplicationDetector.GameProcess? detectedGameProcess;
    private ImageSource? detectedGameIcon;
    private readonly GameSessionClock gameSessionClock=new();
    private bool gameElapsedOverridden;
    private TimeSpan? gameElapsedForProbe;
    private bool gameScanRunning;
    private int gameScanRevision;
    private string ResourceSettingsPath=>Path.Combine(settingsFolder,"resource-mode.json");
    internal ResourceMode EffectiveResourceMode=>ResourcePolicy.Mode;
    internal bool AutomaticGameMode=>resourceSettings.GameAuto;
    private bool NeedsGameDetection=>resourceSettings.GameAuto||resourceSettings.Mode==ResourceMode.Game;
    private void InitializeResourceModes()
    {
        try{if(File.Exists(ResourceSettingsPath))resourceSettings=JsonSerializer.Deserialize<ResourceModeSettings>(File.ReadAllText(ResourceSettingsPath))??new();}catch{}
        if(!Enum.IsDefined(resourceSettings.Mode))resourceSettings=resourceSettings with{Mode=ResourceMode.Normal};
        // Retired manual Game selection becomes automatic, not a hidden forced mode.
        if(resourceSettings.Mode==ResourceMode.Game)resourceSettings=resourceSettings with{Mode=ResourceMode.Normal,GameAuto=true};
        var row=new Grid();
        row.SetBinding(FrameworkElement.WidthProperty,new System.Windows.Data.Binding("ActualWidth"){Source=menuSizeRowGrid});
        row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var label=new OpticallyCenteredText{Text="Resource mode",CompactLineBox=true,InkAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Stretch};
        foreach(var pair in new[]{(OpticallyCenteredText.TextFontFamilyProperty,"FontFamily"),(OpticallyCenteredText.TextFontSizeProperty,"FontSize"),(OpticallyCenteredText.TextFontWeightProperty,"FontWeight"),(OpticallyCenteredText.TextBrushProperty,"Foreground")})
            label.SetBinding(pair.Item1,new System.Windows.Data.Binding(pair.Item2){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor,typeof(MenuItem),1)});
        row.Children.Add(label);
        resourceAutoItem=new CheckBox{Content="Game mode",IsChecked=resourceSettings.GameAuto,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,0,0),Style=(Style)FindResource("DashboardCheckBox")};
        resourceAutoItem.SetBinding(Control.ForegroundProperty,new System.Windows.Data.Binding("Foreground"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor,typeof(MenuItem),1)});
        Grid.SetColumn(resourceAutoItem,1);row.Children.Add(resourceAutoItem);
        var menu=new MenuItem{Header=row,StaysOpenOnClick=true};
        resourceAutoItem.Click+=(_,_)=>
        {
            resourceSettings=resourceSettings with{GameAuto=resourceAutoItem.IsChecked==true};
            if(!NeedsGameDetection){gameScanRevision++;ClearGameDetection();}
            UpdateGameDetectionTimer();
            SaveResourceMode();ApplyResourceMode();_=ScanGameAsync();
        };

        applicationSettingsMenuItem.Items.Insert(0,menu);
        resourceModeTimer.Tick+=async(_,_)=>{UpdateGameElapsed();await ScanGameAsync();};
        Loaded+=(_,_)=>{ApplyResourceMode();UpdateGameDetectionTimer();};
        Closed+=(_,_)=>resourceModeTimer.Stop();
    }
    internal void SelectResourceMode(ResourceMode mode)
    {
        if(!Enum.IsDefined(mode))mode=ResourceMode.Normal;
        resourceSettings=resourceSettings with{Mode=mode};
        if(!NeedsGameDetection){gameScanRevision++;ClearGameDetection();}
        UpdateGameDetectionTimer();SaveResourceMode();ApplyResourceMode();
        if(DiagnosticState.Root==null)_=ScanGameAsync();
    }
    private void UpdateGameDetectionTimer()
    {
        if(NeedsGameDetection&&DiagnosticState.Root==null)resourceModeTimer.Start();else resourceModeTimer.Stop();
    }
    private void SaveResourceMode()
    {
        try{Directory.CreateDirectory(settingsFolder);AtomicJson.Write(ResourceSettingsPath,resourceSettings);}
        catch(Exception error){InteractionDiagnostics.RecordFailure("Resource mode settings",error);}
    }
    private void ClearGameDetection()
    {
        detectedGame=null;detectedGameProcess=null;detectedGameIcon=null;
        gameSessionClock.Reset();gameElapsedOverridden=false;gameElapsedForProbe=null;
    }
    private TimeSpan? CurrentGameElapsed=>gameElapsedOverridden?gameElapsedForProbe:gameSessionClock.Elapsed;
    private void UpdateGameElapsed()
    {
        bool display=NeedsGameDetection&&(gameSessionClock.Process!=null||detectedGame!=null);
        quickLaunchBar.SetGameElapsedRunning(gameElapsedOverridden||gameSessionClock.IsRunning);
        quickLaunchBar.SetGameModeIndicator(display,display?detectedGameIcon:null,display?CurrentGameElapsed:null);
    }
    private async Task ScanGameAsync()
    {
        if(!NeedsGameDetection||gameScanRunning)return;
        int revision=gameScanRevision;
        gameScanRunning=true;
        try
        {
            var game=await Task.Run(gameDetector.ReadProcess);
            if(applicationClosing||!NeedsGameDetection||revision!=gameScanRevision)return;
            if(game==detectedGameProcess)return;
            var previous=gameSessionClock.Process;
            gameSessionClock.Start(game);gameElapsedOverridden=false;gameElapsedForProbe=null;
            detectedGameProcess=game;detectedGame=game is {IsSuspended:false}?game.Application:null;
            bool sameApplication=GameSessionClock.SameGame(previous,game);
            if(game!=null&&!sameApplication)detectedGameIcon=null;
            ApplyResourceMode();
            if(game==null||(sameApplication&&detectedGameIcon!=null))return;
            // Extract once on a worker for this exact process. A late result
            // must not restore an icon after Auto is disabled or the game exits.
            var gameIcon=await Task.Run(()=>GameApplicationIcon.ReadForProcess(game.Application,game.ProcessId));
            if(applicationClosing||!NeedsGameDetection||revision!=gameScanRevision||game!=detectedGameProcess)return;
            detectedGameIcon=gameIcon;ApplyResourceMode();
        }
        finally{gameScanRunning=false;}
    }
    private void ApplyResourceMode()
    {
        var mode=resourceSettings.GameAuto&&detectedGame!=null?ResourceMode.Game:resourceSettings.Mode;
        ResourcePolicy.SetMode(mode);
        foreach(var pair in resourceModeItems)pair.Value.IsChecked=pair.Key==mode;
        if(resourceAutoItem!=null)resourceAutoItem.ToolTip=detectedGame==null?"Automatically use Game while a supported game is running":$"Detected: {detectedGame}";
        UpdateGameElapsed();
        activeBrowser?.SetResourceMode(mode);
        CommonMetal.ApplyResourceMode(mode);
        LightMetalBevel.ApplyResourceMode();
        TrimTabConsoles();
        UpdateBackgroundModuleActivity();
        hueWidget?.RefreshResourceMode();
    }
    // Deterministic state input for the non-persistent probe; never starts a game.
    internal void SetGameDetectionForProbe(string? game,bool auto)
    {
        if(DiagnosticState.Root==null)throw new InvalidOperationException("Isolated probe only.");
        gameScanRevision++;ClearGameDetection();
        resourceSettings=resourceSettings with{GameAuto=auto};detectedGame=game;ApplyResourceMode();
    }
    internal void SetGameIconForProbe(ImageSource? icon)
    {
        if(DiagnosticState.Root==null)throw new InvalidOperationException("Isolated probe only.");
        detectedGameIcon=icon;ApplyResourceMode();
    }
    internal void SetGameProcessForProbe(GameApplicationDetector.GameProcess? game)
    {
        if(DiagnosticState.Root==null)throw new InvalidOperationException("Isolated probe only.");
        gameScanRevision++;
        if(game!=null&&!GameSessionClock.SameGame(gameSessionClock.Process,game))detectedGameIcon=null;
        gameSessionClock.Start(game);gameElapsedOverridden=false;gameElapsedForProbe=null;
        detectedGameProcess=game;detectedGame=game is {IsSuspended:false}?game.Application:null;ApplyResourceMode();
    }
    internal void SetGameElapsedForProbe(TimeSpan? elapsed)
    {
        if(DiagnosticState.Root==null)throw new InvalidOperationException("Isolated probe only.");
        gameElapsedOverridden=true;gameElapsedForProbe=elapsed is { } value&&value<TimeSpan.Zero?TimeSpan.Zero:elapsed;
        UpdateGameElapsed();
    }
}

