using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace Ekrano_valdymas;
internal static class MacroEditor
{
    internal static List<CameraDevice> MicrophoneDevices(MacroStep? saved)
    {
        var choices=new List<CameraDevice>{new("","Windows default (communications)")};
        try{using var enumerator=new NAudio.CoreAudioApi.MMDeviceEnumerator();foreach(var device in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture,NAudio.CoreAudioApi.DeviceState.Active)){using(device)choices.Add(new(device.ID,device.FriendlyName));}}catch{}
        if(saved is {DeviceId.Length:>0}&&!choices.Any(d=>d.Id==saved.DeviceId))
            choices.Add(new(saved.DeviceId,$"Unavailable — {(string.IsNullOrWhiteSpace(saved.DeviceName)?saved.DeviceId:saved.DeviceName)}"));
        return choices;
    }
    internal static string DefaultIconDirectory=>DefaultIconCatalog.PackagedDirectory;
    internal static Microsoft.Win32.OpenFileDialog DefaultIconPicker(IEnumerable<MacroStep> steps)
    {
        var step=steps.Count()==1?steps.First():null;
        string suffix=step?.Kind switch { "Restart"=>"restart", "Shutdown"=>"shutdown", "Sleep"=>step.SleepTarget=="Monitors"?"sleep-monitor":"sleep-pc", _=>"" };
        return new Microsoft.Win32.OpenFileDialog {Title="Default icons",InitialDirectory=DefaultIconDirectory,ClientGuid=Guid.NewGuid(),AddToRecent=false,Filter="Default icons|*.png;*.ico"};
    }

    internal static void Show(Window? owner,MacroDefinition? original=null)=>Create(owner,original).ShowDialog();
    internal static SettingsWindow Create(Window? owner,MacroDefinition? original=null,Func<Window,bool>? confirmDiscard=null)
    {
        var draft=original?.Clone()??new MacroDefinition();MacroStates.UpgradeEditor(draft);var steps=new ObservableCollection<MacroStep>(draft.Steps);var alternate=new ObservableCollection<MacroStep>(draft.AlternateSteps);
        var dialog=AppearanceEditorUi.Window(original==null?"Add macro":"Edit macro",owner,608);dialog.Width=1040;
        MacroStateEditorUi.StyleTabs(dialog);
        var root=new DockPanel{Margin=new Thickness(16)};dialog.Content=root;
        var footer=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=System.Windows.Media.Brushes.OrangeRed,Margin=new Thickness(4)};DockPanel.SetDock(error,Dock.Bottom);root.Children.Add(error);
        var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(3,GridUnitType.Star)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(2,GridUnitType.Star)});body.RowDefinitions.Add(new RowDefinition());body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.Children.Add(body);
        var right=new StackPanel{Margin=new Thickness(16,0,0,0)};var rightViewport=MacroUi.Scroll(right);Grid.SetColumn(rightViewport,1);body.Children.Add(rightViewport);
        var fields=new StackPanel();var settingsTabs=new TabControl{VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,0,0,8)};settingsTabs.Items.Add(new TabItem{Header="General",Content=MacroUi.Scroll(fields)});body.Children.Add(settingsTabs);
        var name=new TextBox{Name="macroName",Text=draft.Name,MaxLength=80,FontWeight=FontWeights.SemiBold,Foreground=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(244,205,142))};var icon=new TextBox{Text=draft.Icon};
        EditorUi.Field(fields,"Macro name",name);EditorUi.Field(fields,"Icon symbol or image path",icon);
        var hotkey=new TextBox{Name="macroHotkey",Text=draft.Hotkey,IsReadOnly=true,ToolTip="Click here and press the keys to assign."};
        EditorUi.Field(fields,"Trigger keys (global shortcut)",hotkey);
        hotkey.PreviewKeyDown+=(_,e)=>{
            e.Handled=true;var key=e.Key==System.Windows.Input.Key.System?e.SystemKey:e.Key;
            if(key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin)return;
            var modifiers=System.Windows.Input.Keyboard.Modifiers;
            string prefix=(modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)?"Ctrl+":"")+(modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)?"Alt+":"")+(modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)?"Shift+":"")+(modifiers.HasFlag(System.Windows.Input.ModifierKeys.Windows)?"Win+":"");
            try{hotkey.Text=MacroHotkeys.Normalize(prefix+key);error.Text="";}catch(Exception ex){error.Text=ex.Message;}
        };
        fields.Children.Add(MacroUi.Button("Clear trigger keys",(_,_)=>{hotkey.Text="";error.Text="";}));
        AppearanceEditorUi.Help(fields,"Click the field and press a combination. Works while BlakiDeck is running, even in another app. Paused while editing settings.");
        error.Text=MacroHotkeys.Current?.Error(draft.Id)??"";
        var presentation=AppearanceEditorUi.Presentation(draft.Presentation??"IconText");
        EditorUi.Field(fields,"Show macro: text / icon / icon and text",presentation);
        AppearanceEditorUi.Help(fields,"This choice applies to this macro in the library and on its shortcuts.");
        var iconButtons=new WrapPanel();
        iconButtons.Children.Add(MacroUi.Button("Choose file…",(_,_)=>{if(DefaultIconCatalog.ChooseImage(dialog) is string path)icon.Text=MacroDefaultIcons.Portable(path);}));
        var defaults=MacroUi.Button("Built-in icons…",(_,_)=>{if(BuiltInIconGallery.Choose(dialog,"Macros") is string path)icon.Text=MacroDefaultIcons.Portable(path);});iconButtons.Children.Add(defaults);fields.Children.Add(iconButtons);
        foreach(FrameworkElement field in fields.Children)
            field.Margin=field is TextBlock?new Thickness(4,4,4,2):new Thickness(4,2,4,2);
        string effectTarget=MacroLibrary.Prefix+draft.Id;
        var iconEffect=ShortcutIconEffect.Editor(fields,effectTarget);
        iconEffect.SelectionChanged+=(_,_)=>ShortcutIconEffect.Preview(effectTarget,AppearanceEditorUi.Selected(iconEffect));
        var branch=AppearanceEditorUi.Choice("A",("A","Action A — steps"),("B","Action B — steps"));branch.Name="actionBranch";
        right.Children.Add(new TextBlock{Text="Actions",FontSize=20,Foreground=System.Windows.Media.Brushes.White,Margin=new Thickness(4,8,4,8)});right.Children.Add(branch);
        ObservableCollection<MacroStep> ActiveSteps()=>AppearanceEditorUi.Selected(branch)=="B"?alternate:steps;
        void ModeChanged(){branch.Visibility=draft.IconMode=="Alternate"?Visibility.Visible:Visibility.Collapsed;if(draft.IconMode!="Alternate")branch.SelectedIndex=0;}
        Action? refreshTextPreview=null;
        var states=MacroStateEditorUi.Create(dialog,draft,steps,ModeChanged,()=>refreshTextPreview?.Invoke());
        settingsTabs.Items.Add(new TabItem{Header="States & icons",Content=MacroUi.Scroll(states)});ModeChanged();
        var textFields=new StackPanel();
        LabelTypography.Editor(textFields,draft.Typography??new(){Size=16},value=>{draft.Typography=value;refreshTextPreview?.Invoke();},"MacroTextSize",includeEffect:true,defaults:new(){Size=16});
        AppearanceEditorUi.Help(textFields,"Text settings follow this macro in the library and on its shortcuts. Changes are saved only with Save.");
        settingsTabs.Items.Add(new TabItem{Header="Text",Content=MacroUi.Scroll(textFields)});
        var list=new ListBox{Name="actionSteps",ItemsSource=steps,MinHeight=56,MaxHeight=240,HorizontalContentAlignment=HorizontalAlignment.Stretch};ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Auto);branch.SelectionChanged+=(_,_)=>list.ItemsSource=ActiveSteps();
        list.ItemTemplate=(DataTemplate)System.Windows.Markup.XamlReader.Parse("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><StackPanel Margin="5,6"><TextBlock Text="{Binding DisplayTitle}" TextWrapping="Wrap" FontWeight="SemiBold"/><TextBlock Text="{Binding DisplayDetail}" TextWrapping="Wrap" FontSize="13" Opacity="0.7"/></StackPanel></DataTemplate>
        """);
        var actions=new WrapPanel();right.Children.Add(list);right.Children.Add(actions);
        actions.Children.Add(MacroUi.Button("Add step",(_,_)=>{var step=EditStep(dialog,null);if(step!=null){ActiveSteps().Add(step);list.SelectedItem=step;}}));
        void ReplaceStep(MacroStep selected,MacroStep replacement)
        {
            int index=ActiveSteps().IndexOf(selected);if(index<0)return;
            if(ReferenceEquals(ActiveSteps(),steps)&&draft.StateSourceKey==MacroStates.Key(selected))draft.StateSourceKey=MacroStates.Key(replacement);
            ActiveSteps()[index]=replacement;list.SelectedIndex=index;
        }
        void EditSelectedStep(){if(list.SelectedItem is MacroStep selected){var step=EditStep(dialog,selected);if(step!=null)ReplaceStep(selected,step);}}
        actions.Children.Add(MacroUi.Button("Edit step",(_,_)=>EditSelectedStep()));
        list.MouseDoubleClick+=(_,e)=>{if(e.ChangedButton==System.Windows.Input.MouseButton.Left&&ItemsControl.ContainerFromElement(list,e.OriginalSource as DependencyObject) is ListBoxItem){e.Handled=true;EditSelectedStep();}};
        actions.Children.Add(MacroUi.Button("Remove step",(_,_)=>{if(list.SelectedItem is MacroStep selected)ActiveSteps().Remove(selected);}));
        void Move(int delta){int i=list.SelectedIndex;if(i<0||i+delta<0||i+delta>=ActiveSteps().Count)return;ActiveSteps().Move(i,i+delta);list.SelectedIndex=i+delta;}
        actions.Children.Add(MacroUi.Button("↑",(_,_)=>Move(-1)));actions.Children.Add(MacroUi.Button("↓",(_,_)=>Move(1)));foreach(Button action in actions.Children){action.Margin=new Thickness(2);action.Padding=new Thickness(8,5,8,5);}
        var microphoneFields=new StackPanel{Margin=new Thickness(0,8,0,0)};right.Children.Add(microphoneFields);
        var microphoneDevice=new ComboBox{Name="MicrophoneDevice",DisplayMemberPath="Name",SelectedValuePath="Id"};
        System.Windows.Automation.AutomationProperties.SetName(microphoneDevice,"Microphone device");
        EditorUi.Field(microphoneFields,"Microphone device",microphoneDevice);
        AppearanceEditorUi.Help(microphoneFields,"For the selected microphone action. Used for mute, state and VU. Saved only with Save.");
        bool updatingMicrophone=false;
        void UpdateMicrophone()
        {
            updatingMicrophone=true;
            try{
                var selected=list.SelectedItem as MacroStep;microphoneFields.Visibility=selected?.Kind=="MicMute"?Visibility.Visible:Visibility.Collapsed;
                microphoneDevice.ItemsSource=selected?.Kind=="MicMute"?MicrophoneDevices(selected):null;
                microphoneDevice.SelectedValue=selected?.DeviceId;
            }finally{updatingMicrophone=false;}
        }
        microphoneDevice.SelectionChanged+=(_,_)=>{
            if(updatingMicrophone||list.SelectedItem is not MacroStep selected||selected.Kind!="MicMute"||microphoneDevice.SelectedItem is not CameraDevice device||selected.DeviceId==device.Id)return;
            var replacement=System.Text.Json.JsonSerializer.Deserialize<MacroStep>(System.Text.Json.JsonSerializer.Serialize(selected))!;
            replacement.DeviceId=device.Id;replacement.DeviceName=device.Name;ReplaceStep(selected,replacement);
        };
        list.SelectionChanged+=(_,_)=>UpdateMicrophone();
        branch.SelectionChanged+=(_,_)=>{list.SelectedIndex=ActiveSteps().Count>0?0:-1;UpdateMicrophone();};
        list.SelectedIndex=steps.Count>0?0:-1;UpdateMicrophone();
        void SyncDraft(){draft.Hotkey=hotkey.Text;draft.Name=name.Text.Trim();draft.Icon=icon.Text.Trim();draft.Presentation=AppearanceEditorUi.Selected(presentation);draft.Steps=steps.ToList();draft.AlternateSteps=alternate.ToList();}
        string Snapshot(){SyncDraft();return System.Text.Json.JsonSerializer.Serialize(draft)+"|"+AppearanceEditorUi.Selected(iconEffect);}
        string baseline=Snapshot();bool explicitClose=false;
        void RefreshPlacedAppearance()
        {
            SyncDraft();
            if(original!=null)MacroLibrary.Current.PreviewAppearance(draft.Id,draft);
        }
        refreshTextPreview=RefreshPlacedAppearance;
        name.TextChanged+=(_,_)=>RefreshPlacedAppearance();icon.TextChanged+=(_,_)=>RefreshPlacedAppearance();presentation.SelectionChanged+=(_,_)=>RefreshPlacedAppearance();
        steps.CollectionChanged+=(_,_)=>RefreshPlacedAppearance();alternate.CollectionChanged+=(_,_)=>RefreshPlacedAppearance();
        dialog.Loaded+=(_,_)=>RefreshPlacedAppearance();
        dialog.Closed+=(_,_)=>{ShortcutIconEffect.Preview(effectTarget,null);if(original!=null)MacroLibrary.Current.PreviewAppearance(draft.Id,null);};
        dialog.Closing+=(_,e)=>{if(!explicitClose&&Snapshot()!=baseline&&!(confirmDiscard?.Invoke(dialog)??AppearanceEditorUi.ConfirmDiscard(dialog)))e.Cancel=true;};
        void AlignActions()
        {
            bool narrow=body.ActualWidth<760;
            double contentTop=36;
            if(!narrow&&settingsTabs.SelectedContent is FrameworkElement selected&&selected.IsLoaded)
                contentTop=selected.TranslatePoint(new Point(),body).Y;
            right.Margin=new Thickness(narrow?0:16,narrow?0:Math.Max(0,contentTop),0,0);
            // Reserve the heading, branch selector and action buttons, then use
            // the remaining viewport for steps instead of an arbitrary 240 px cap.
            double otherHeight=right.Children.OfType<FrameworkElement>()
                .Where(child=>child!=list&&child.Visibility!=Visibility.Collapsed)
                .Sum(child=>child.DesiredSize.Height);
            if(rightViewport.ActualHeight>0)
                list.MaxHeight=Math.Max(list.MinHeight,rightViewport.ActualHeight-right.Margin.Top-otherHeight-4);
        }
        body.SizeChanged+=(_,_)=>{bool narrow=body.ActualWidth<760;Grid.SetColumn(rightViewport,narrow?0:1);Grid.SetRow(rightViewport,narrow?1:0);body.RowDefinitions[1].Height=narrow?new GridLength(1,GridUnitType.Star):GridLength.Auto;Grid.SetColumnSpan(settingsTabs,narrow?2:1);Grid.SetColumnSpan(rightViewport,narrow?2:1);settingsTabs.Height=narrow?280:double.NaN;settingsTabs.MaxHeight=Math.Max(100,body.ActualHeight);AlignActions();};
        rightViewport.SizeChanged+=(_,_)=>AlignActions();
        actions.SizeChanged+=(_,_)=>AlignActions();
        microphoneFields.SizeChanged+=(_,_)=>AlignActions();
        branch.IsVisibleChanged+=(_,_)=>dialog.Dispatcher.BeginInvoke(new Action(AlignActions));
        settingsTabs.SizeChanged+=(_,_)=>AlignActions();dialog.Loaded+=(_,_)=>AlignActions();
        // A tab's content finishes loading after the TabControl size event.
        // Align again when its actual arranged content origin is available.
        foreach(TabItem tab in settingsTabs.Items)
            ((FrameworkElement)tab.Content).Loaded+=(_,_)=>AlignActions();
        dialog.ContentRendered+=(_,_)=>AlignActions();
        footer.Children.Add(MacroUi.Button("Save",(_,_)=>{try{if(MacroRunner.Current.IsRunning)throw new InvalidOperationException("Wait for the running macro to finish before saving.");SyncDraft();MacroLibrary.Current.Save(draft);ShortcutIconEffect.Save(effectTarget,AppearanceEditorUi.Selected(iconEffect));explicitClose=true;dialog.DialogResult=true;}catch(Exception ex){error.Text=ex.Message;}}));
        footer.Children.Add(MacroUi.Button("Cancel",(_,_)=>{explicitClose=true;dialog.DialogResult=false;}));AppearanceEditorUi.Frame(dialog);return dialog;
    }
    private static MacroStep? EditStep(Window owner,MacroStep? original)
    {
        MacroStep? result=null;var dialog=CreateStep(owner,original,step=>result=step);dialog.ShowDialog();return result;
    }
    internal static SettingsWindow CreateStep(Window? owner,MacroStep? original,Action<MacroStep> accept)
    {
        var dialog=AppearanceEditorUi.Window("Macro action",owner,560);var panel=new StackPanel{Margin=new Thickness(18,4,18,12)};var root=new DockPanel();var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(18,8,18,12)};DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);var scroll=MacroUi.Scroll(panel);root.Children.Add(scroll);dialog.Content=root;dialog.AutoFitScroll=scroll;
        var kind=new ComboBox{ItemsSource=new[]{"Launch","Wait","Keys","MicMute","Camera","Sleep","Shutdown","Restart","OBS","Hue"},SelectedItem=original?.Kind??"Launch"};
        var deviceHelp=AppearanceEditorUi.Help(panel,"MicMute controls the selected microphone or Windows default. Camera controls the selected camera system-wide. Neither needs a key combination.");
        var value=new TextBox{Text=original?.Value??""};var wait=new TextBox{Text=(original?.Milliseconds??1000).ToString()};
        var countdown=new TextBox{Text=(original?.CountdownSeconds??10).ToString()}; var target=new ComboBox{ItemsSource=new[]{"Computer","Monitors"},SelectedItem=original?.SleepTarget??"Computer"};
        EditorUi.Field(panel,"Action",kind);EditorUi.Field(panel,"Program / target or key combination (Ctrl+Shift+S)",value);
        var deviceFields=new StackPanel();panel.Children.Add(deviceFields);
        var devices=new ComboBox();EditorUi.Field(deviceFields,"Device",devices);
        var operation=AppearanceEditorUi.Choice(original?.Operation??"Toggle",("Toggle","Toggle"),("On","On — enable camera / microphone"),("Off","Off — disable camera / mute microphone"));EditorUi.Field(deviceFields,"Device action",operation);
        var cameraHelp=AppearanceEditorUi.Help(deviceFields,"Camera Off disables this device system-wide. Windows may request administrator approval. On allows use; it does not start video.");
        string? listedKind=null;
        void UpdateDevices()
        {
            string selected=(string)kind.SelectedItem;deviceFields.Visibility=selected is "Camera" or "MicMute"?Visibility.Visible:Visibility.Collapsed;deviceHelp.Visibility=deviceFields.Visibility;
            cameraHelp.Visibility=selected=="Camera"?Visibility.Visible:Visibility.Collapsed;
            if(listedKind==selected)return;listedKind=selected;var choices=new List<CameraDevice>();
            if(selected=="Camera")choices=CameraControl.Devices();
            if(selected=="MicMute")
            {
                choices=MicrophoneDevices(original?.Kind=="MicMute"?original:null);
            }
            if(original!=null&&original.Kind==selected&&!choices.Any(d=>d.Id==original.DeviceId)&&original.DeviceId.Length>0)choices.Add(new(original.DeviceId,"Unavailable saved device"));
            devices.ItemsSource=choices;devices.SelectedItem=choices.FirstOrDefault(d=>d.Id==original?.DeviceId)??choices.FirstOrDefault();
        }
        var browse=MacroUi.Button("Choose program or file…",(_,_)=>{var file=new Microsoft.Win32.OpenFileDialog();if(file.ShowDialog(dialog)==true)value.Text=file.FileName;});panel.Children.Add(browse);
        EditorUi.Field(panel,"Wait in milliseconds (0–3600000)",wait);EditorUi.Field(panel,"Sleep target for this action",target);
        EditorUi.Field(panel,"Power action countdown in seconds (0–3600)",countdown);
        var obsAction=AppearanceEditorUi.Choice(original?.Value??"Record",("Record","Recording — start / stop"),("ReplayBuffer","Replay buffer — start / stop"),("SaveReplay","Save replay"));EditorUi.Field(panel,"OBS action (uses your local OBS WebSocket settings)",obsAction);
        var huePanel=new StackPanel();panel.Children.Add(huePanel);
        var hueRooms=new ComboBox{Name="hueRooms",DisplayMemberPath="Name",SelectedValuePath="GroupedLightId"};EditorUi.Field(huePanel,"Hue room / group",hueRooms);
        var hueOperation=AppearanceEditorUi.Choice(original?.Operation??"Toggle",("Toggle","Switch on / off"),("On","Turn on"),("Off","Turn off"),("Brightness","Set brightness"));EditorUi.Field(huePanel,"Hue action",hueOperation);
        var hueBrightness=new Slider{Minimum=0,Maximum=100,Value=original?.Brightness??50,TickFrequency=1,IsSnapToTickEnabled=true};hueBrightness.SetResourceReference(FrameworkElement.StyleProperty,"DashboardSlider");
        EditorUi.Field(huePanel,"Brightness (0% turns lights off)",hueBrightness);
        var hueValue=AppearanceEditorUi.Help(huePanel,$"{hueBrightness.Value:0}%");hueBrightness.ValueChanged+=(_,_)=>hueValue.Text=$"{hueBrightness.Value:0}%";
        var hueStatus=AppearanceEditorUi.Help(huePanel,"Uses the bridge connected in Philips Hue.");
        var hueLoad=new CancellationTokenSource();dialog.Closed+=(_,_)=>{hueLoad.Cancel();hueLoad.Dispose();};
        if(original?.Kind=="Hue"){var savedRoom=new HueRoom(original.DeviceName,original.DeviceId,false,original.Brightness,null,null,false,false,null);hueRooms.ItemsSource=new[]{savedRoom};hueRooms.SelectedIndex=0;}
        async Task LoadHue()
        {
            try{hueStatus.Text="Loading Hue rooms…";using var timeout=CancellationTokenSource.CreateLinkedTokenSource(hueLoad.Token);timeout.CancelAfter(TimeSpan.FromSeconds(8));
                string selected=hueRooms.SelectedValue as string??original?.DeviceId??"";var rooms=await HueMacroAction.RoomsAsync(timeout.Token);
                var choices=rooms.ToList();if(original?.Kind=="Hue"&&!choices.Any(r=>r.GroupedLightId==original.DeviceId))choices.Add(new HueRoom(original.DeviceName,original.DeviceId,false,original.Brightness,null,null,false,false,null));
                hueRooms.ItemsSource=choices;hueRooms.SelectedValue=selected;if(hueRooms.SelectedIndex<0&&rooms.Count>0)hueRooms.SelectedIndex=0;
                hueStatus.Text=rooms.Count==0?"No rooms available. Connect Hue and retry.":"Uses your existing Hue connection.";
            }catch(OperationCanceledException){if(!hueLoad.IsCancellationRequested)hueStatus.Text="Hue connection timed out. Retry when the bridge is available.";}
            catch(Exception ex){hueStatus.Text=ex.Message;}
        }
        huePanel.Children.Add(MacroUi.Button("Refresh rooms",async(_,_)=>await LoadHue()));
        foreach(FrameworkElement field in huePanel.Children)field.Margin=field is TextBlock?new Thickness(4,3,4,1):new Thickness(4,2,4,2);
        bool hueLoaded=false;
        void UpdateHue(){bool visible=kind.SelectedItem is "Hue";huePanel.Visibility=visible?Visibility.Visible:Visibility.Collapsed;hueBrightness.IsEnabled=AppearanceEditorUi.Selected(hueOperation)=="Brightness";if(visible&&!hueLoaded){hueLoaded=true;_=LoadHue();}}
        kind.SelectionChanged+=(_,_)=>UpdateHue();hueOperation.SelectionChanged+=(_,_)=>UpdateHue();UpdateHue();
        void ShowField(FrameworkElement control,bool show){control.Visibility=show?Visibility.Visible:Visibility.Collapsed;int index=panel.Children.IndexOf(control);if(index>0&&panel.Children[index-1] is TextBlock label)label.Visibility=control.Visibility;}
        void Update(){ShowField(obsAction,kind.SelectedItem is "OBS");ShowField(countdown,kind.SelectedItem is "Shutdown" or "Restart" or "Sleep");ShowField(value,kind.SelectedItem is "Launch" or "Keys");browse.Visibility=kind.SelectedItem is "Launch"?Visibility.Visible:Visibility.Collapsed;ShowField(wait,kind.SelectedItem is "Wait");ShowField(target,kind.SelectedItem is "Sleep");}
        kind.SelectionChanged+=(_,_)=>{Update();UpdateDevices();};Update();UpdateDevices();
        var error=new TextBlock{Foreground=System.Windows.Media.Brushes.OrangeRed,TextWrapping=TextWrapping.Wrap};panel.Children.Add(error);
        actions.Children.Add(MacroUi.Button("Save action",(_,_)=>{try{int milliseconds=1000;if(kind.SelectedItem is "Wait"&&!int.TryParse(wait.Text,out milliseconds))throw new ArgumentException("Enter a whole number of milliseconds.");
            int seconds=10;if(kind.SelectedItem is "Shutdown" or "Restart" or "Sleep"&&!int.TryParse(countdown.Text,out seconds))throw new ArgumentException("Enter a whole number of seconds."); var step=new MacroStep{DeviceName=(devices.SelectedItem as CameraDevice)?.Name??"",DeviceId=(devices.SelectedItem as CameraDevice)?.Id??"",Operation=AppearanceEditorUi.Selected(operation),CountdownSeconds=seconds,Kind=(string)kind.SelectedItem,Value=value.Text.Trim(),Milliseconds=milliseconds,SleepTarget=(string)target.SelectedItem};
            if(original?.Kind=="MicMute"&&step.Kind=="MicMute"&&step.DeviceId==original.DeviceId&&step.DeviceName.StartsWith("Unavailable — "))step.DeviceName=original.DeviceName;
            if(step.Kind=="OBS")step.Value=AppearanceEditorUi.Selected(obsAction);
            if(step.Kind=="Hue"){if(hueRooms.SelectedItem is not HueRoom room)throw new ArgumentException("Choose a Hue room / group.");step.DeviceId=room.GroupedLightId;step.DeviceName=room.Name;step.Operation=AppearanceEditorUi.Selected(hueOperation);step.Brightness=hueBrightness.Value;}
            MacroLibrary.Validate(new(){Steps=new(){step}});accept(step);dialog.DialogResult=true;}catch(Exception ex){error.Text=ex.Message;}}));
        actions.Children.Add(MacroUi.Button("Cancel",(_,_)=>dialog.DialogResult=false));AppearanceEditorUi.Frame(dialog);kind.SelectionChanged+=(_,_)=>dialog.RequestContentFit();return dialog;
    }
}





