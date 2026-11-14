using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
namespace Ekrano_valdymas;
internal sealed class PlaylistCard : UserControl
{
    private readonly LocalPlayback local=LocalPlayback.Instance;
    internal Canvas Surface {get;}=new(){Width=626,Height=426,Background=Brushes.Transparent};
    private readonly ListBox list=new(){SelectionMode=SelectionMode.Extended,Background=Brushes.Transparent,BorderThickness=new Thickness(0),AllowDrop=true,Tag="PlaylistList"};
    private readonly OpticallyCenteredText status,count;
    private readonly List<OpticallyCenteredText> measuredText=new();
    private bool active,preview,dragged;
    private Point down;
    private int anchor=-1;
    private LocalPlayback.Track? pressed;
    private bool doubleClick;
    internal bool IsEmbedded {get;}
    internal PlaylistSettings Appearance {get;private set;}=new();
    internal void ApplyAppearance(PlaylistSettings value)
    {
        Appearance=value;
        PlaylistAppearance.Apply(list,IsEmbedded,value);
        foreach(var text in measuredText.Where(t=>!Equals(t.Tag,"ModuleTitle")))
            if(value.TextColor is string color)text.TextBrush=(Brush)new BrushConverter().ConvertFromString(color)!;
            else if(IsEmbedded)text.TextBrush=new SolidColorBrush(Color.FromRgb(151,137,112));
            else text.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"PanelTextBrush");
        UpdateStatusColor();
    }
    internal object AppearanceEvidence()=>new{IsEmbedded,Appearance,titleSize=Appearance.Size(IsEmbedded),rowGap=Appearance.Gap,status=status.Text,statusColor=status.TextBrush.ToString()};
    private void UpdateStatusColor()
    {
        if(status==null)return;
        if(status.Text=="Select Local player")status.TextBrush=Brushes.Orange;
        else if(Appearance.TextColor is string color)status.TextBrush=(Brush)new BrushConverter().ConvertFromString(color)!;
        else if(IsEmbedded)status.TextBrush=new SolidColorBrush(Color.FromRgb(151,137,112));
        else status.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"PanelTextBrush");
    }
    internal IReadOnlyList<OpticallyCenteredText> MeasuredText=>measuredText;
    internal IReadOnlyList<LocalPlayback.Track> Tracks=>local.Tracks;
    internal bool Playing=>local.Playing;
    internal double DiagnosticPosition=>local.Position;
    internal PlaylistCard(bool embedded=false)
    {
        IsEmbedded=embedded;
        Background=Brushes.Transparent;Content=new Viewbox{Stretch=Stretch.Uniform,Child=Surface};
        if(!embedded){
        var backing=new Border{CornerRadius=new CornerRadius(18),BorderThickness=new Thickness(1.5),BorderBrush=new SolidColorBrush(Color.FromArgb(53,255,255,255))};backing.SetResourceReference(Border.BackgroundProperty,"PanelMetalBrush");CommonMetal.SetSurface(backing,true);Put(backing,0,0,626,426);
        var mark=Text("♫",28,25,26,32,25);mark.InkAlignment=HorizontalAlignment.Center;
        var header=Text("PLAYLIST",64,25,280,32,25);header.Tag="ModuleTitle";header.TextFontFamily=(FontFamily)FindResource("TekoFont");header.TextFontWeight=FontWeights.Bold;header.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"ModuleTitleBrush");
        Put(new ModuleOptionsButton(),564,24,38,36);
        var rule=new Rectangle();rule.SetResourceReference(Shape.FillProperty,"PanelDividerBrush");Put(rule,28,68,570,1);
        }
        if(embedded){
            Surface.Width=831;Surface.Height=331;
            var face=new FontFamily(new Uri("pack://application:,,,/"),"./Assets/Fonts/#Michroma");
            var heading=Text("PLAYLIST",25,11,300,28,PlaylistAppearance.CapSize(14,face));heading.TextFontFamily=face;heading.TextBrush=new SolidColorBrush(Color.FromRgb(151,137,112));
            var options=new ModuleOptionsButton();((OpticallyCenteredText)options.Content).TextBrush=new SolidColorBrush(Color.FromRgb(151,137,112));Put(options,775,8,32,30);
            var line=new Rectangle{Fill=new SolidColorBrush(Color.FromRgb(55,40,22))};Put(line,22,39,787,1);
        }
        HeaderTool("\uE8B7","Browse music folders",embedded?707:488,()=>new PlaylistFolderBrowser(Window.GetWindow(this),async paths=>{preview=false;await local.AddAsync(paths);}).ShowDialog());
        HeaderTool("\uE74D","Delete playlist — clear list, keep audio files",embedded?741:526,ClearPlaylist);
        status=Text("Select Local player",embedded?22:28,embedded?303:75,embedded?750:300,22,12);
        if(embedded)status.TextBrush=new SolidColorBrush(Color.FromRgb(151,137,112));
        count=Text("",embedded?395:330,embedded?10:75,embedded?296:268,26,14);count.InkAlignment=HorizontalAlignment.Right;
        count.ToolTip="Number of songs in the playlist and their total playing time";
        if(embedded){count.TextFontSize=PlaylistAppearance.CapSize(13);count.TextBrush=new SolidColorBrush(Color.FromRgb(151,137,112));Canvas.SetTop(count,12);Put(list,18,47,795,250);}else Put(list,24,108,578,290);
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        PlaylistAppearance.Apply(list,embedded);
        list.PreviewMouseLeftButtonDown+=(_,e)=>{if(IsScrollControl(e.OriginalSource as DependencyObject))return;pressed=TrackAt(e.OriginalSource as DependencyObject);down=e.GetPosition(list);dragged=false;doubleClick=e.ClickCount==2;if(pressed!=null){list.Focus();e.Handled=true;}else if(e.ClickCount==1){if(Window.GetWindow(this) is MainWindow main)main.DragFromPlaylistBackground(e);else if(e.StylusDevice==null){Window.GetWindow(this)?.DragMove();e.Handled=true;}}};
        list.PreviewMouseMove+=(_,e)=>{
            if(pressed==null||e.LeftButton!=MouseButtonState.Pressed||dragged)return;
            var p=e.GetPosition(list);if(Math.Abs(p.X-down.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(p.Y-down.Y)<SystemParameters.MinimumVerticalDragDistance)return;
            dragged=true;if(!list.SelectedItems.Contains(pressed))list.SelectedItem=pressed;var items=list.SelectedItems.Cast<LocalPlayback.Track>().ToArray();
            DragDrop.DoDragDrop(list,new DataObject("BlakiDeck.PlaylistTracks",items),DragDropEffects.Move);pressed=null;
        };
        list.PreviewMouseLeftButtonUp+=async(_,e)=>{
            var track=pressed;pressed=null;if(track==null||dragged)return;e.Handled=true;Select(track);
            if(doubleClick&&active&&!preview&&Keyboard.Modifiers==ModifierKeys.None)await PlayTrack(track);
        };
        list.PreviewKeyDown+=async(_,e)=>{
            if(e.Key==Key.Delete){local.Remove(list.SelectedItems.Cast<LocalPlayback.Track>().ToArray());e.Handled=true;}
            else if(e.Key==Key.Enter&&active&&list.SelectedItem is LocalPlayback.Track track){await PlayTrack(track);e.Handled=true;}
            else if(e.Key==Key.A&&Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){list.SelectAll();e.Handled=true;}
        };
        AllowDrop=true;DragOver+=(_,e)=>{e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:e.Data.GetDataPresent("BlakiDeck.PlaylistTracks")?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;};
        Drop+=async(_,e)=>{
            e.Handled=true;
            if(e.Data.GetData(DataFormats.FileDrop) is string[] files){preview=false;int first=await local.AddAsync(files);if(active&&first>=0)await PlayTrack(local.Tracks[first]);}
            else if(e.Data.GetData("BlakiDeck.PlaylistTracks") is LocalPlayback.Track[] items){var target=TrackAt(e.OriginalSource as DependencyObject);local.Move(items,target==null?local.Tracks.Count:local.Tracks.IndexOf(target));}
        };
        Loaded+=(_,_)=>{local.Changed+=Render;Render();};Unloaded+=(_,_)=>local.Changed-=Render;
        Render();
    }
    private static bool IsScrollControl(DependencyObject? element){while(element!=null){if(element is System.Windows.Controls.Primitives.ScrollBar)return true;element=VisualTreeHelper.GetParent(element);}return false;}
    private LocalPlayback.Track? TrackAt(DependencyObject? element){while(element!=null&&element!=list){if(element is FrameworkElement{DataContext:LocalPlayback.Track t})return t;element=VisualTreeHelper.GetParent(element);}return null;}
    private void Select(LocalPlayback.Track track)
    {
        int index=list.Items.IndexOf(track);
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)&&anchor>=0){list.SelectedItems.Clear();for(int i=Math.Min(anchor,index);i<=Math.Max(anchor,index);i++)list.SelectedItems.Add(list.Items[i]);}
        else if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){if(list.SelectedItems.Contains(track))list.SelectedItems.Remove(track);else list.SelectedItems.Add(track);anchor=index;}
        else {list.SelectedItem=track;anchor=index;}
    }
    internal void SetActive(bool value){if(active==value)return;active=value;Render();}
    internal string Summary=>count.Text;
    private async Task PlayTrack(LocalPlayback.Track track){try{await local.PlayAsync(local.Tracks.IndexOf(track));}catch(Exception e){local.Fail(e);}}
    internal async void Play(int index){if(active)try{await local.PlayAsync(index);}catch(Exception e){local.Fail(e);}}
    internal void Pause()=>local.Pause();
    internal async void Next(){if(active)try{await local.NextAsync(1);}catch(Exception e){local.Fail(e);}}
    internal ContextMenu Options()
    {
        var menu=ListAppearanceUi.Create(this);menu.PlacementTarget=this;
        void Item(string label,Action action){var item=new MenuItem{Header=label};item.SetResourceReference(StyleProperty,"MiniMenuItemStyle");item.Click+=(_,_)=>action();menu.Items.Add(item);}
        Item("Add files…",async()=>{var dialog=new OpenFileDialog{Multiselect=true,Filter="Audio|*.mp3;*.flac;*.wav;*.m4a;*.aac;*.wma;*.ogg|All files|*.*"};if(dialog.ShowDialog(Window.GetWindow(this))==true){preview=false;await local.AddAsync(dialog.FileNames);}});
        Item("Clear playlist",ClearPlaylist);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Width=300};
        foreach(var entry in menu.Items.OfType<MenuItem>().ToArray()){menu.Items.Remove(entry);var button=new Button{Content=entry.Header,Width=146,Margin=new Thickness(2,0,2,0),Padding=new Thickness(8,3,8,3),Style=menu.TryFindResource("PositionActionButton") as Style};button.Click+=(_,_)=>{menu.IsOpen=false;entry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));};actions.Children.Add(button);}
        var actionRow=new MenuItem{Header=actions,StaysOpenOnClick=true};actionRow.SetResourceReference(StyleProperty,"MiniMenuItemStyle");menu.Items.Add(actionRow);
        menu.Items.Add(new Separator());
        void Change(PlaylistSettings value){if(Window.GetWindow(this) is MainWindow main)main.SavePlaylistAppearance(this,value);else ApplyAppearance(value);}
        menu.Items.Add(new MenuItem{Header="Layout",IsEnabled=false});
        var layouts=new List<MenuItem>();
        foreach(var name in new[]{"Modern","Winamp style"}){var item=new MenuItem{Header=name,IsCheckable=true,IsChecked=Appearance.Layout==name,StaysOpenOnClick=true};item.Click+=(_,_)=>{Change(Appearance with{Layout=name});foreach(var choice in layouts)choice.IsChecked=Equals(choice.Header,name);};layouts.Add(item);menu.Items.Add(item);}
        menu.Items.Add(new Separator());
        ListAppearanceUi.SliderRow(menu,"Text size","PlaylistTitleSize",12,32,Appearance.Size(IsEmbedded),v=>Change(Appearance with{TitleSize=v}),new PlaylistSettings().Size(IsEmbedded));
        ListAppearanceUi.SliderRow(menu,"Spacing","PlaylistRowGap",0,32,Appearance.Gap,v=>Change(Appearance with{RowGap=v}));
        Item("Text color…",()=>{
            var initial=Appearance.TextColor is string color?(Color)ColorConverter.ConvertFromString(color):IsEmbedded?Color.FromRgb(190,169,137):((Application.Current.TryFindResource("PanelTextBrush") as SolidColorBrush)?.Color??Colors.White);
            var original=Appearance;
            bool useDefault=false;var skinDefault=IsEmbedded?Color.FromRgb(190,169,137):((Application.Current.TryFindResource("PanelTextBrush") as SolidColorBrush)?.Color??Colors.White);
            if(ListAppearanceUi.PickColor(Window.GetWindow(this),initial,c=>ApplyAppearance(original with{TextColor=c.ToString()}),out var chosen,skinDefault,value=>{useDefault=value;if(value)ApplyAppearance(original with{TextColor=null});}))Change(original with{TextColor=useDefault?null:chosen.ToString()});else ApplyAppearance(original);
        });
        ListAppearanceUi.Compact(menu);actionRow.Style=menu.TryFindResource("BareMenuRow") as Style;actions.Margin=new Thickness(42,0,0,0);return menu;
    }
    private void ClearPlaylist(){preview=false;local.Remove(local.Tracks.ToArray());Render();}
    private void HeaderTool(string glyph,string hint,double x,Action action)
    {
        var button=new Button{ToolTip=hint,Tag="NoWindowDrag",Content=new OpticallyCenteredText{Text=glyph,TextFontFamily=new FontFamily("Segoe MDL2 Assets"),TextFontSize=IsEmbedded?19:22,TextBrush=IsEmbedded?new SolidColorBrush(Color.FromRgb(151,137,112)):System.Windows.Media.Brushes.Silver}};
        if(glyph=="\uE8B7")button.Content=new Path{Data=Geometry.Parse("M1,5 L8,5 L10,8 L21,8 L21,20 L1,20 Z M1,10 L21,10"),Stroke=IsEmbedded?new SolidColorBrush(Color.FromRgb(151,137,112)):Brushes.Silver,StrokeThickness=1.6,StrokeLineJoin=PenLineJoin.Round,Width=IsEmbedded?20:23,Height=IsEmbedded?16:19,Stretch=Stretch.Uniform};
        button.SetResourceReference(StyleProperty,"ModuleOptionsButtonStyle");button.Click+=(_,e)=>{e.Handled=true;action();};Put(button,x,IsEmbedded?8:24,IsEmbedded?32:38,IsEmbedded?30:36);
    }
    private void Render()
    {
        var selected=list.SelectedItems.Cast<LocalPlayback.Track>().ToArray();list.Items.Clear();
        var tracks=preview?Enumerable.Range(0,6).Select(i=>new LocalPlayback.Track("",new[]{"Easterville — Radio Edit","Lakeside","Evening Drive","Still Water","Afterglow","Homeward"}[i],"Demo Artist",202+i*11)).ToList():local.Tracks;
        status.Text=local.Error.Length>0?local.Error:!active?"Select Local player":preview?"Example layout • files are not saved":"";
        UpdateStatusColor();
        status.ToolTip=status.Text;count.Text=PlaylistAppearance.Summary(tracks);list.Opacity=active?1:.42;
        foreach(var track in tracks)list.Items.Add(track);
        foreach(var t in selected)if(list.Items.Contains(t))list.SelectedItems.Add(t);
    }
    private OpticallyCenteredText Text(string value,double x,double y,double w,double h,double size){var t=new OpticallyCenteredText{Text=value,TextFontFamily=new FontFamily("Segoe UI"),TextFontSize=size,TextFontWeight=FontWeights.Normal,InkAlignment=HorizontalAlignment.Left,Ellipsis=true};t.SetResourceReference(OpticallyCenteredText.TextBrushProperty,"PanelTextBrush");measuredText.Add(t);Put(t,x,y,w,h);return t;}
    private void Put(FrameworkElement e,double x,double y,double w,double h){e.Width=w;e.Height=h;Canvas.SetLeft(e,x);Canvas.SetTop(e,y);Surface.Children.Add(e);}
    internal void SetPreview(){preview=true;Render();}
    internal void SetDiagnosticTrack(string path){if(DiagnosticState.Root==null)throw new InvalidOperationException();preview=false;active=true;local.Remove(local.Tracks.ToArray());local.Tracks.Add(new(path,"Diagnostic audio","",0));local.Volume=0;Render();}
}


