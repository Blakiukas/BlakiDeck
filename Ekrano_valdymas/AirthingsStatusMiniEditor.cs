using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Ekrano_valdymas;

internal static class AirthingsStatusMiniEditor
{
    private static AirthingsStatusRoom[] Rooms(AirthingsStatusOptions options)=>options.Rooms?.Where(room=>Device(room) is not AirthingsDevice device||AirthingsStatusRules.HasSensors(device)).ToArray()??AirthingsService.Shared.Devices.Where(AirthingsStatusRules.HasSensors).Take(1).Select(d=>new AirthingsStatusRoom(d.Account,d.Serial)).ToArray();
    private static AirthingsDevice? Device(AirthingsStatusRoom room)=>AirthingsService.Shared.Devices.FirstOrDefault(d=>d.Account==room.Account&&d.Serial==room.Serial);
    private static string ResolveRoomLabel(AirthingsStatusRoom room,AirthingsDevice? device,bool includeHome=false)=>!string.IsNullOrWhiteSpace(room.DisplayLabel)?room.DisplayLabel:device==null?(includeHome?"Saved room · unavailable":"Unavailable"):includeHome?$"{device.Home} · {device.Name}":device.Name;
    internal static SettingsWindow CreateForGaugeLimits(Window? owner,AirthingsStatusOptions original,Action<AirthingsStatusOptions> save,Action<AirthingsStatusOptions>? live=null)
        =>CreateCore(owner,original,save,live,true);
    private static SettingsWindow CreateCore(Window? owner,AirthingsStatusOptions original,Action<AirthingsStatusOptions> save,Action<AirthingsStatusOptions>? live,bool allowRoomLabels)
    {
        var dialog=AppearanceEditorUi.Window("Airthings gauge limits",owner,800);dialog.Width=710;dialog.FontSize=18;dialog.Resources[typeof(Expander)]=AirthingsEditorVisuals.DisclosureStyle();
        var root=new DockPanel();dialog.Content=root;
        var footer=new DockPanel{Margin=new Thickness(18,4,18,12)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(buttons,Dock.Right);footer.Children.Add(buttons);
        var error=new TextBlock{Name="AirthingsStatusError",Foreground=Brushes.OrangeRed,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4,0,8,0)};footer.Children.Add(error);
        var tabs=SettingsEditorTabs.Create("AirthingsStatusEditorTabs");tabs.Margin=new Thickness(18,4,18,8);root.Children.Add(tabs);
        var roomsPanel=new StackPanel();tabs.Items.Add(new TabItem{Header="Rooms and sensors",Content=MacroUi.Scroll(roomsPanel)});
        var state=original.Normalize();bool committed=false;var numericErrors=new HashSet<TextBox>();
        var saved=Rooms(state);
        var available=AirthingsService.Shared.Devices.Where(AirthingsStatusRules.HasSensors).Select(d=>new AirthingsStatusRoom(d.Account,d.Serial)).Concat(saved).GroupBy(r=>(r.Account,r.Serial)).Select(g=>g.First()).ToArray();
        var selected=saved.Select(r=>(r.Account,r.Serial)).ToHashSet();
        var rooms=available.ToDictionary(r=>(r.Account,r.Serial),r=>
        {
            var existing=saved.FirstOrDefault(s=>s.Account==r.Account&&s.Serial==r.Serial)??r;
            var device=Device(existing);
            return existing with{Sensors=device==null?existing.Sensors??[]:AirthingsStatusRules.Configured(device,existing)};
        });
        AirthingsStatusOptions Current()=>state with{Rooms=available.Where(r=>selected.Contains((r.Account,r.Serial))).Select(r=>rooms[(r.Account,r.Serial)]).ToArray()};
        string? Validation()
        {
            if(numericErrors.Count>0)return "Enter valid numbers; invalid input has not been applied.";
            var chosen=Current().Rooms!;if(chosen.Length==0)return "Select at least one room.";
            foreach(var room in chosen)
            {
                var sensors=room.Sensors?.Where(s=>s.Included).ToArray()??[];
                // An offline saved identity may have no cached sensor list yet.
                // Retain it as unknown rather than forcing destructive removal.
                if(sensors.Length==0&&Device(room)==null)continue;
                if(sensors.Length==0)return "Select at least one sensor for each chosen room.";
                foreach(var sensor in sensors)
                {
                    if(!sensor.CustomLimits&&!AirthingsQuality.IsSupported(sensor.Sensor))return AirthingsMini.SensorLabel(sensor.Sensor)+": set custom limits before including this sensor.";
                    if(AirthingsStatusRules.Validate(sensor) is string reason)return AirthingsMini.SensorLabel(sensor.Sensor)+": "+reason;
                }
            }
            return null;
        }
        void Apply(){error.Text=Validation()??"";live?.Invoke(Current().Normalize());}
        CheckBox Check(string caption,bool value,string? name=null)
        {
            var check=new CheckBox{Content=caption,IsChecked=value,Margin=new Thickness(4,5,4,5)};if(name!=null)check.Name=name;
            check.SetBinding(Control.ForegroundProperty,new Binding(nameof(Window.Foreground)){Source=dialog});return check;
        }
        // The checkbox and disclosure title share one header line. Their cap-to-
        // baseline reference is independent of the expanded form's height.
        var sensorHeaderText=new FrameworkElementFactory(typeof(OpticallyCenteredText));
        sensorHeaderText.SetBinding(OpticallyCenteredText.TextProperty,new Binding());
        sensorHeaderText.SetValue(FrameworkElement.HeightProperty,32d);
        sensorHeaderText.SetValue(OpticallyCenteredText.InkAlignmentProperty,HorizontalAlignment.Left);
        sensorHeaderText.SetValue(FrameworkElement.TagProperty,"AirthingsStatusSensorHeaderText");
        sensorHeaderText.SetBinding(OpticallyCenteredText.TextFontFamilyProperty,new Binding(nameof(Window.FontFamily)){Source=dialog});
        sensorHeaderText.SetBinding(OpticallyCenteredText.TextFontSizeProperty,new Binding(nameof(Window.FontSize)){Source=dialog});
        sensorHeaderText.SetBinding(OpticallyCenteredText.TextFontWeightProperty,new Binding(nameof(Window.FontWeight)){Source=dialog});
        sensorHeaderText.SetBinding(OpticallyCenteredText.TextFontStyleProperty,new Binding(nameof(Window.FontStyle)){Source=dialog});
        sensorHeaderText.SetBinding(OpticallyCenteredText.TextBrushProperty,new Binding(nameof(Window.Foreground)){Source=dialog});
        var sensorHeaderTemplate=new DataTemplate{VisualTree=sensorHeaderText};
        var rangeBackground=new SolidColorBrush(Color.FromRgb(37,35,33));rangeBackground.Freeze();
        var rangeBorder=new SolidColorBrush(Color.FromRgb(73,70,64));rangeBorder.Freeze();
        var explanation=new StackPanel();roomsPanel.Children.Add(new Expander{Header="About gauge limits",Content=explanation,Margin=new Thickness(0,0,0,8)});
        AppearanceEditorUi.Help(explanation,"Each gauge uses the limits for its own measurement. Gray means missing, old or unclassified data. Sensor checkboxes enable classification; disabling one does not hide its measurement.");
        AppearanceEditorUi.Help(explanation,"Green/red limits follow Airthings. Yellow/orange split the intermediate range at its midpoint; this subdivision belongs to BlakiDeck. Custom ranges are your own settings.");
        AppearanceEditorUi.Help(explanation,"Samples use the shared cloud synchronization (at least 5 minutes), not a separate polling timer. Bluetooth-only devices update when synchronized by the Airthings app.");
        if(available.Length==0)AppearanceEditorUi.Help(roomsPanel,"No rooms available. Connect Airthings in its module setup, then reopen this editor.");
        foreach(var availableRoom in available)
        {
            var key=(availableRoom.Account,availableRoom.Serial);var device=Device(availableRoom);
            string RoomLabel()=>ResolveRoomLabel(rooms[key],device,true);
            var roomCheck=Check(RoomLabel(),selected.Contains(key));roomCheck.Tag=availableRoom;
            var roomHeader=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};roomHeader.Children.Add(roomCheck);
            if(allowRoomLabels)
            {
                bool editing=false;
                var labelInput=new TextBox{Tag="AirthingsRoomLabelInput",DataContext=availableRoom,Width=340,MinHeight=32,VerticalContentAlignment=VerticalAlignment.Center,ToolTip="Room name in BlakiDeck only. Blank restores the Airthings room name. Press Enter to finish."};
                var pencil=MacroUi.Button("",(_,_)=>{});pencil.Tag="AirthingsRoomLabelEdit";pencil.DataContext=availableRoom;pencil.Width=30;pencil.Height=30;pencil.MinWidth=0;pencil.MinHeight=0;pencil.Padding=new Thickness(4);pencil.Margin=new Thickness(4,0,0,0);pencil.VerticalAlignment=VerticalAlignment.Center;pencil.ToolTip="Edit displayed room name";
                pencil.Content=new OpticallyCenteredText{Text="\uE70F",TextFontFamily=new FontFamily("Segoe MDL2 Assets"),TextFontSize=20,TextFontWeight=FontWeights.Normal,TextBrush=AdaptiveSensorColors.PencilBrush,FitToBox=true};
                System.Windows.Automation.AutomationProperties.SetName(pencil,"Edit displayed room name · "+RoomLabel());
                roomHeader.Children.Add(pencil);
                void Finish(){if(!editing)return;editing=false;roomCheck.Content=RoomLabel();roomCheck.ToolTip=RoomLabel();}
                pencil.Click+=(_,_)=>
                {
                    labelInput.Text=rooms[key].DisplayLabel??(device==null?"":$"{device.Home} · {device.Name}");editing=true;roomCheck.Content=labelInput;labelInput.Focus();labelInput.SelectAll();
                    dialog.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,new Action(()=>{if(editing){labelInput.Focus();labelInput.SelectAll();}}));
                };
                labelInput.TextChanged+=(_,_)=>
                {
                    if(!editing)return;
                    rooms[key]=rooms[key] with{DisplayLabel=string.IsNullOrWhiteSpace(labelInput.Text)?null:labelInput.Text.Trim()};Apply();
                };
                labelInput.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;Finish();pencil.Focus();}};
                labelInput.LostKeyboardFocus+=(_,_)=>Finish();
            }
            var body=new StackPanel{Margin=new Thickness(12,0,0,8)};
            var expander=new Expander{Header=roomHeader,Content=body,IsExpanded=selected.Contains(key),Margin=new Thickness(0,6,0,4),HorizontalContentAlignment=HorizontalAlignment.Stretch};roomsPanel.Children.Add(expander);
            expander.SetBinding(Control.ForegroundProperty,new Binding(nameof(Window.Foreground)){Source=dialog});
            roomCheck.Checked+=(_,_)=>{selected.Add(key);Apply();};roomCheck.Unchecked+=(_,_)=>{selected.Remove(key);Apply();};
            if(device==null)AppearanceEditorUi.Help(body,"Saved sensor choices are retained. Measurements stay unavailable until the room is available again.");
            foreach(var initial in rooms[key].Sensors??[])
            {
                string canonical=AirthingsStatusRules.Canonical(initial.Sensor);
                AirthingsStatusSensor Sensor()=>rooms[key].Sensors!.First(s=>AirthingsStatusRules.Canonical(s.Sensor)==canonical);
                void Set(AirthingsStatusSensor next){rooms[key]=rooms[key] with{Sensors=rooms[key].Sensors!.Select(s=>AirthingsStatusRules.Canonical(s.Sensor)==canonical?next:s).ToArray()};Apply();}
                var sensorPanel=new DockPanel{Margin=new Thickness(0,2,0,2)};
                var included=Check(AirthingsMini.SensorLabel(initial.Sensor),initial.Included);included.Tag=initial.Sensor;included.ContentTemplate=sensorHeaderTemplate;included.MinHeight=32;included.Margin=new Thickness(4,0,4,0);included.VerticalAlignment=VerticalAlignment.Top;DockPanel.SetDock(included,Dock.Left);sensorPanel.Children.Add(included);
                var rangePanel=new StackPanel();
                var rangeSurface=new Border{Tag="AirthingsStatusLimitsSurface",Background=rangeBackground,BorderBrush=rangeBorder,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Padding=new Thickness(10,6,10,6),Margin=new Thickness(0,3,0,3),Child=rangePanel};
                // The range surface owns its border/padding. Theme chrome around
                // the Expander must not inset its shared header below the checkbox.
                var limits=new Expander{Header="Limits · "+AirthingsStatusRules.Unit(initial.Sensor),HeaderTemplate=sensorHeaderTemplate,Content=rangeSurface,BorderThickness=new Thickness(0),Padding=new Thickness(0),Margin=new Thickness(12,0,0,0),VerticalAlignment=VerticalAlignment.Top,HorizontalContentAlignment=HorizontalAlignment.Stretch};sensorPanel.Children.Add(limits);body.Children.Add(sensorPanel);
                limits.Loaded+=(_,_)=>
                {
                    limits.ApplyTemplate();
                    // The standard WPF HeaderSite has a one-DIP theme margin.
                    // The shared header row supplies its spacing instead.
                    if(limits.Template?.FindName("HeaderSite",limits) is FrameworkElement headerSite)headerSite.Margin=new Thickness(0);
                };
                limits.SetBinding(Control.ForegroundProperty,new Binding(nameof(Window.Foreground)){Source=dialog});
                var custom=Check("Use my limits",initial.CustomLimits);rangePanel.Children.Add(custom);
                var grid=new Grid{Margin=new Thickness(4,2,4,4)};grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto,MinWidth=76});grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new());
                for(int row=0;row<4;row++)grid.RowDefinitions.Add(new(){Height=GridLength.Auto});rangePanel.Children.Add(grid);
                void Label(string value,int row,int col)
                {
                    FrameworkElement label=new TextBlock{Text=value,Margin=new Thickness(3),VerticalAlignment=VerticalAlignment.Center};
                    if(col==0&&row>0)
                    {
                        var line=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
                        line.Children.Add(new Border{Width=14,Height=12,Background=AirthingsStatusRules.Brush((AirthingsStatusLevel)(row-1)),BorderBrush=Brushes.DimGray,BorderThickness=new Thickness(1),ToolTip=value,Margin=new Thickness(3,0,3,0)});line.Children.Add(label);label=line;
                    }
                    grid.Children.Add(label);Grid.SetRow(label,row);Grid.SetColumn(label,col);
                }
                Label("",0,0);Label("Minimum",0,1);Label("Maximum",0,2);Label("Green",1,0);Label("Yellow",2,0);Label("Orange",3,0);
                var seed=AirthingsStatusRules.SuggestedCustom(initial);
                void Input(double? initialValue,int row,int col,Func<AirthingsStatusSensor,double?,AirthingsStatusSensor> update)
                {
                    var box=new TextBox{Text=initialValue?.ToString("0.#####",CultureInfo.CurrentCulture)??"",MinWidth=65,Margin=new Thickness(3),ToolTip="Blank means no limit on this side"};grid.Children.Add(box);Grid.SetRow(box,row);Grid.SetColumn(box,col);
                    box.TextChanged+=(_,_)=>
                    {
                        double? value=null;
                        if(!string.IsNullOrWhiteSpace(box.Text))
                        {
                            if(!(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out double parsed)||double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out parsed))||!double.IsFinite(parsed)){numericErrors.Add(box);box.BorderBrush=Brushes.OrangeRed;error.Text=Validation()??"";return;}
                            value=parsed;
                        }
                        numericErrors.Remove(box);box.ClearValue(Control.BorderBrushProperty);Set(update(Sensor(),value));
                    };
                }
                Input(seed.GoodLow,1,1,(s,v)=>s with{GoodLow=v});Input(seed.GoodHigh,1,2,(s,v)=>s with{GoodHigh=v});Input(seed.YellowLow,2,1,(s,v)=>s with{YellowLow=v});Input(seed.YellowHigh,2,2,(s,v)=>s with{YellowHigh=v});Input(seed.OrangeLow,3,1,(s,v)=>s with{OrangeLow=v});Input(seed.OrangeHigh,3,2,(s,v)=>s with{OrangeHigh=v});
                void EnableCustom(){var next=Sensor();if(custom.IsChecked==true)next=AirthingsStatusRules.SuggestedCustom(next);Set(next with{CustomLimits=custom.IsChecked==true});grid.IsEnabled=custom.IsChecked==true;grid.Opacity=grid.IsEnabled?1:.45;}
                grid.IsEnabled=initial.CustomLimits;grid.Opacity=grid.IsEnabled?1:.45;custom.Checked+=(_,_)=>EnableCustom();custom.Unchecked+=(_,_)=>EnableCustom();
                AppearanceEditorUi.Help(rangePanel,"Ranges are nested: green inside yellow inside orange. Outside orange is red. Minimum is included; maximum starts the next band. Blank leaves that side open.");
                included.Checked+=(_,_)=>Set(Sensor() with{Included=true});included.Unchecked+=(_,_)=>Set(Sensor() with{Included=false});
            }
        }
        buttons.Children.Add(MacroUi.Button("Save",(_,_)=>{if(Validation() is string reason){error.Text=reason;return;}try{save(Current().Normalize());committed=true;dialog.DialogResult=true;}catch(Exception ex){error.Text=ex.Message;}}));buttons.Children.Add(MacroUi.Button("Cancel",(_,_)=>dialog.Close()));
        dialog.Closed+=(_,_)=>{if(!committed)live?.Invoke(original);};
        AppearanceEditorUi.Frame(dialog);return dialog;
    }
}
