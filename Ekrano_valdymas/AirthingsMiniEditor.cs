using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

internal static class AirthingsMiniEditor
{
    internal static SettingsWindow Create(Window? owner,AirthingsMiniOptions original,Action<AirthingsMiniOptions> save,Action<AirthingsMiniOptions>? applyLive=null)
    {
        var dialog=AppearanceEditorUi.Window("Edit Airthings mini",owner,824);dialog.Width=570;
        var root=new DockPanel();dialog.Content=root;
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(18,4,18,12)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var content=new DockPanel{Margin=new Thickness(18,4,18,8)};root.Children.Add(content);
        var tabs=SettingsEditorTabs.Create("AirthingsMiniTabs");content.Children.Add(tabs);
        var panel=new StackPanel();var colors=new StackPanel();var textPanel=new StackPanel();
        tabs.Items.Add(new TabItem{Header="Cell",Content=MacroUi.Scroll(panel)});tabs.Items.Add(new TabItem{Header="Colors",Content=MacroUi.Scroll(colors)});tabs.Items.Add(new TabItem{Header="Text",Content=MacroUi.Scroll(textPanel)});
        var state=original.SensorSlots==4?original:original with{SensorSlots=4,Reading2=original.Span==1?"__none__":original.Reading2,Reading3="__none__",Reading4="__none__"};bool committed=false;
        var error=AppearanceEditorUi.Help(panel,"");error.Foreground=Brushes.OrangeRed;
        Action? updateVisibility=null;
        var previewTimer=new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background,dialog.Dispatcher){Interval=TimeSpan.FromMilliseconds(40)};
        previewTimer.Tick+=(_,_)=>{previewTimer.Stop();try{applyLive?.Invoke(state);error.Text="";}catch(Exception ex){error.Text=ex.Message;}};
        void Preview(){updateVisibility?.Invoke();if(!previewTimer.IsEnabled)previewTimer.Start();}
        var cells=AppearanceEditorUi.Choice(original.Span.ToString(),("1","One cell"),("2","Two cells"));EditorUi.Field(panel,"Width",cells);
        var rooms=new ComboBox{SelectedValuePath="Tag"};rooms.Items.Add(new ComboBoxItem{Content="First available room",Tag=new AirthingsMiniOptions()});
        foreach(var device in AirthingsService.Shared.Devices)rooms.Items.Add(new ComboBoxItem{Content=$"{device.Home} · {device.Name}",Tag=new AirthingsMiniOptions(device.Account,device.Serial)});
        rooms.SelectedIndex=0;foreach(ComboBoxItem item in rooms.Items)if(item.Tag is AirthingsMiniOptions o&&o.Account==original.Account&&o.Serial==original.Serial)rooms.SelectedItem=item;
        if(original.Serial!=null&&rooms.SelectedIndex==0){rooms.Items.Add(new ComboBoxItem{Content="Saved room · unavailable",Tag=original});rooms.SelectedIndex=rooms.Items.Count-1;}
        EditorUi.Field(panel,"Room",rooms);
        var hideRoom=new CheckBox{Name="AirthingsHideRoom",Content="Hide room name",Foreground=Brushes.White,IsChecked=state.HideRoom,Margin=new Thickness(4,8,4,8)};panel.Children.Add(hideRoom);
        var hideRoomText=new CheckBox{Name="AirthingsHideRoomText",Content="Hide room name",Foreground=Brushes.White,IsChecked=state.HideRoom,Margin=new Thickness(4,8,4,8)};textPanel.Children.Add(hideRoomText);
        void HideRoom(CheckBox source){state=state with{HideRoom=source.IsChecked==true};hideRoom.IsChecked=hideRoomText.IsChecked=state.HideRoom;Preview();}
        hideRoom.Click+=(_,_)=>HideRoom(hideRoom);hideRoomText.Click+=(_,_)=>HideRoom(hideRoomText);
        var iconRows=new CheckBox{Name="AirthingsIconRows",Content="Icon rows",Foreground=Brushes.White,IsChecked=state.IconRows,IsEnabled=state.Span==2,Margin=new Thickness(4,8,4,8)};panel.Children.Add(iconRows);
        iconRows.Click+=(_,_)=>{state=state with{IconRows=iconRows.IsChecked==true};Preview();};
        panel.Children.Add(new TextBlock{Text="Sensors",Margin=new Thickness(4,8,4,5)});
        var selections=Enumerable.Range(0,4).Select(_=>new ComboBox()).ToArray();
        var badges=new ContentControl[4];
        for(int i=0;i<4;i++)
        {
            var row=new Grid{Margin=new Thickness(4,3,4,3)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new GridLength(36)});
            selections[i].Name="AirthingsSensor"+(i+1);row.Children.Add(selections[i]);badges[i]=new ContentControl{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(badges[i],1);row.Children.Add(badges[i]);
            panel.Children.Add(new Border{BorderBrush=new SolidColorBrush(Color.FromRgb(70,66,62)),BorderThickness=new Thickness(0,0,0,i==3?0:1),Padding=new Thickness(0,3,0,5),Child=row});
        }
        bool rebuilding=false;
        string?[] Selected()=>[state.Reading1,state.Reading2,state.Reading3,state.Reading4];
        void Readings()
        {
            rebuilding=true;var selected=Selected();
            for(int i=0;i<4;i++)
            {
                var combo=selections[i];combo.Items.Clear();combo.Items.Add(new ComboBoxItem{Content="Not selected",Tag="__none__"});combo.Items.Add(new ComboBoxItem{Content="Automatic · most urgent",Tag=""});
                var types=(AirthingsMini.Device(state)?.Supported??[]).Concat(string.IsNullOrEmpty(selected[i])||selected[i]=="__none__"?[]:new[]{selected[i]!}).Distinct(StringComparer.OrdinalIgnoreCase);
                foreach(var type in types)combo.Items.Add(new ComboBoxItem{Content=AirthingsMini.SensorLabel(type),Tag=type});
                combo.SelectedItem=combo.Items.Cast<ComboBoxItem>().FirstOrDefault(item=>Equals(item.Tag,selected[i]??""))??combo.Items[0];
                badges[i].Content=selected[i]=="__none__"?null:AirthingsEditorVisuals.SensorIcon(selected[i]);
            }
            rebuilding=false;
        }
        Readings();
        rooms.SelectionChanged+=(_,_)=>{var room=(rooms.SelectedItem as ComboBoxItem)?.Tag as AirthingsMiniOptions??new();state=state with{Account=room.Account,Serial=room.Serial};Readings();Preview();};
        foreach(var combo in selections)combo.SelectionChanged+=(_,_)=>
        {
            if(rebuilding)return;
            var selected=selections.Select(c=>(c.SelectedItem as ComboBoxItem)?.Tag as string??"__none__").ToArray();
            state=state with{Reading1=selected[0],Reading2=selected[1],Reading3=selected[2],Reading4=selected[3],SensorSlots=4};
            for(int i=0;i<4;i++)badges[i].Content=selected[i]=="__none__"?null:AirthingsEditorVisuals.SensorIcon(selected[i]);Preview();
        };        bool reflectingDefaultSize=false;
        Slider Size(string title,double value,double reference,Action<double> set){var slider=new Slider{Minimum=8,Maximum=80,Value=double.IsFinite(value)?Math.Clamp(value,8,80):reference};SliderDefaults.Mark(slider,reference);var label=new TextBlock{Text=$"{title} — {slider.Value:0}",Margin=new Thickness(4,8,4,2)};var group=new StackPanel();group.Children.Add(label);group.Children.Add(slider);textPanel.Children.Add(group);slider.ValueChanged+=(_,_)=>{label.Text=$"{title} — {slider.Value:0}";if(!reflectingDefaultSize){set(slider.Value);Preview();}};return slider;}
        var titleSize=Size("Reading title size",state.LabelSize,11,v=>state=state with{LabelSize=v});
        var valueSize=Size("Reading value size",state.ValueSize??(state.Span==2?25:23),state.Span==2?25:23,v=>state=state with{ValueSize=v});valueSize.Name="AirthingsMiniValueSize";
        var roomSize=Size("Room name size",state.RoomSize,14,v=>state=state with{RoomSize=v});roomSize.Name="AirthingsRoomNameSize";
        var iconSize=Size("Icon height",state.IconSize,24,v=>state=state with{IconSize=v});iconSize.Name="AirthingsIconSize";
        var iconValue=Size("Icon row text size",state.IconValueSize,24,v=>state=state with{IconValueSize=v});iconValue.Name="AirthingsIconValueSize";
        cells.SelectionChanged+=(_,_)=>
        {
            state=state with{Cells=AppearanceEditorUi.Selected(cells)=="2"?2:1};
            iconRows.IsEnabled=state.Span==2;double reference=state.Span==2?25:23;
            // A null size follows the renderer's width-dependent factory size.
            // Reflect it without turning that automatic value into an override.
            reflectingDefaultSize=true;
            try{if(state.ValueSize==null)valueSize.Value=reference;SliderDefaults.Mark(valueSize,reference);}
            finally{reflectingDefaultSize=false;}
            Preview();
        };
        var automatic=new CheckBox{Foreground=Brushes.White,Content="Airthings air quality colors",IsChecked=state.AutomaticColors,Margin=new Thickness(4,10,4,8)};colors.Children.Add(automatic);
        var manual=new StackPanel();colors.Children.Add(manual);var valuePalette=new StackPanel();var labelPalette=new StackPanel();var roomPalette=new StackPanel();manual.Children.Add(labelPalette);manual.Children.Add(roomPalette);
        void Palette(Panel host,string title,string? initial,Action<string?> set){host.Children.Add(new TextBlock{Text=title,Margin=new Thickness(4,8,4,3)});host.Children.Add(ColorSwatchRow.Create(initial,v=>{set(v);Preview();},dialog));}
        Palette(labelPalette,"Reading title",state.LabelColor,v=>state=state with{LabelColor=v});Palette(roomPalette,"Room name",state.RoomColor,v=>state=state with{RoomColor=v});
        manual.Children.Add(valuePalette);Palette(valuePalette,"Reading value",state.ValueColor,v=>state=state with{ValueColor=v});
        updateVisibility=()=>{
            bool icons=state.Span==2&&state.IconRows;
            void Show(UIElement element,bool visible)=>element.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
            Show((UIElement)titleSize.Parent,!icons);Show((UIElement)valueSize.Parent,!icons);
            Show((UIElement)iconSize.Parent,icons);Show((UIElement)iconValue.Parent,icons);
            Show((UIElement)roomSize.Parent,!state.HideRoom);Show(iconRows,state.Span==2);
            Show(manual,!state.AutomaticColors);Show(labelPalette,!icons);Show(roomPalette,!state.HideRoom);
        };
        void Quality(){state=state with{AutomaticColors=automatic.IsChecked==true};manual.IsEnabled=!state.AutomaticColors;manual.Opacity=state.AutomaticColors ? .45 : 1;Preview();}
        automatic.Checked+=(_,_)=>Quality();automatic.Unchecked+=(_,_)=>Quality();Quality();
        AppearanceEditorUi.Help(colors,"Automatic colors follow air quality. Manual choices are retained; missing and old samples stay gray.");
        var restore=MacroUi.Button("Default",(_,_)=>{
            var baseline=new AirthingsMiniOptions(Account:state.Account,Serial:state.Serial,Cells:state.Cells,Reading1:state.Reading1,Reading2:state.Reading2,Reading3:state.Reading3,Reading4:state.Reading4,SensorSlots:state.SensorSlots);
            reflectingDefaultSize=true;
            try{titleSize.Value=baseline.LabelSize;valueSize.Value=baseline.Span==2?25:23;roomSize.Value=baseline.RoomSize;iconSize.Value=baseline.IconSize;iconValue.Value=baseline.IconValueSize;}
            finally{reflectingDefaultSize=false;}
            state=baseline;hideRoom.IsChecked=hideRoomText.IsChecked=false;iconRows.IsChecked=true;automatic.IsChecked=true;
            labelPalette.Children.Clear();roomPalette.Children.Clear();valuePalette.Children.Clear();
            Palette(labelPalette,"Reading title",null,v=>state=state with{LabelColor=v});Palette(roomPalette,"Room name",null,v=>state=state with{RoomColor=v});Palette(valuePalette,"Reading value",null,v=>state=state with{ValueColor=v});
            Preview();
        });restore.Name="AirthingsMiniDefault";restore.ToolTip="Restore appearance; keep the room, sensors and cell width.";footer.Children.Add(restore);
        footer.Children.Add(MacroUi.Button("Save",(_,_)=>{try{previewTimer.Stop();save(state);committed=true;dialog.DialogResult=true;}catch(Exception ex){error.Text=ex.Message;}}));footer.Children.Add(MacroUi.Button("Cancel",(_,_)=>dialog.Close()));
        dialog.Closed+=(_,_)=>{previewTimer.Stop();if(!committed)applyLive?.Invoke(original);};
        AppearanceEditorUi.Frame(dialog);return dialog;
    }
}

