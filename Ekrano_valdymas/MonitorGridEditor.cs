using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Ekrano_valdymas;

internal sealed class MonitorGridEditor:SettingsWindow
{
    private readonly SystemMonitorWidget monitor;
    private MonitorGridLayout draft;
    private readonly Dictionary<int,SystemMonitorWidget.MetricKind?> pendingSources=new();
    private readonly Canvas canvas=new(){Background=new SolidColorBrush(Color.FromRgb(25,25,25)),ClipToBounds=true,Focusable=true};
    private readonly Dictionary<int,Border> cards=new();
    private readonly ComboBox hidden=new(){MinWidth=200,MaxWidth=300,Margin=new Thickness(6)};
    private readonly TextBlock status=new(){Foreground=Brushes.Silver,Margin=new Thickness(6),TextWrapping=TextWrapping.Wrap};
    private int selected=-1;
    private bool dragging;
    internal MonitorGridLayout Draft=>draft;
    internal MonitorGridEditor(SystemMonitorWidget monitor)
    {
        this.monitor=monitor;draft=monitor.CaptureGridLayout();
        Owner=Window.GetWindow(monitor);Title="System Monitor — Edit layout";ShowInTaskbar=false;
        Width=1100;Height=660;MinWidth=600;MinHeight=440;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        MacroUi.ApplyTheme(this);
        var root=new DockPanel{Margin=new Thickness(18)};Content=root;
        var footer=new DockPanel{Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(buttons,Dock.Right);footer.Children.Add(buttons);
        buttons.Children.Add(MacroUi.Button("Cancel",(_,_)=>Close()));
        var apply=MacroUi.Button("Apply",(_,_)=>{try{monitor.ApplyEditorLayout(draft,pendingSources);Close();}catch(Exception ex){status.Text="Could not save layout: "+ex.Message;}});apply.Name="ApplyMonitorGrid";buttons.Children.Add(apply);footer.Children.Add(status);
        var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        header.Children.Add(new TextBlock{Text="Drag cells to move · drag any edge or the corner grip to resize",Foreground=Brushes.WhiteSmoke,FontSize=18,Margin=new Thickness(6)});
        header.Children.Add(new TextBlock{Text="Choose a sensor and add a cell. Drag to arrange. Apply saves; Cancel discards changes.",Foreground=Brushes.Silver,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(6)});
        var toolbar=new WrapPanel();header.Children.Add(toolbar);toolbar.Children.Add(hidden);
        toolbar.Children.Add(MacroUi.Button("Add cell",(_,_)=>AddSelected()));

        var layoutActions=new WrapPanel{Margin=new Thickness(0,8,0,0)};DockPanel.SetDock(layoutActions,Dock.Bottom);root.Children.Add(layoutActions);
        layoutActions.Children.Add(MacroUi.Button("Delete cell",(_,_)=>HideSelected()));
        layoutActions.Children.Add(MacroUi.Button("Revert changes",(_,_)=>{draft=monitor.CaptureGridLayout();pendingSources.Clear();selected=-1;Rebuild();}));
        layoutActions.Children.Add(MacroUi.Button("Default",(_,_)=>{draft=monitor.DefaultGridLayout();selected=-1;Rebuild();}));
        layoutActions.Children.Add(MacroUi.Button("Clear layout",(_,_)=>{draft=draft with{Cells=[]};selected=-1;Rebuild();status.Text="Layout cleared. Add cells to start again; Apply saves, Cancel keeps the previous layout.";}));
        var preview=new Grid{MinHeight=300,Margin=new Thickness(6)};
        var frame=new Border{BorderBrush=Brushes.DimGray,BorderThickness=new Thickness(1),Child=canvas,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};preview.Children.Add(frame);root.Children.Add(preview);
        var panel=FindPanel(monitor);double baseWidth=panel?.ActualWidth??draft.Columns*100,baseHeight=panel?.ActualHeight??330;
        if(baseWidth<=0||baseHeight<=0){baseWidth=draft.Columns*100;baseHeight=330;}
        preview.SizeChanged+=(_,_)=>{double scale=Math.Min(preview.ActualWidth/baseWidth,preview.ActualHeight/baseHeight);frame.Width=Math.Max(1,baseWidth*scale);frame.Height=Math.Max(1,baseHeight*scale);};
        canvas.SizeChanged+=(_,_)=>{if(!dragging)Rebuild();};canvas.PreviewKeyDown+=KeyboardMove;
        Loaded+=(_,_)=>Rebuild();DialogWorkArea.Attach(this);
    }
    private static MonitorSlotsPanel? FindPanel(DependencyObject root){if(root is MonitorSlotsPanel panel)return panel;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)if(FindPanel(VisualTreeHelper.GetChild(root,i)) is {} found)return found;return null;}
    private string Label(int slot)=>$"{slot+1}. {(pendingSources.TryGetValue(slot,out var kind)?kind.HasValue?SystemMonitorWidget.GetMetricLabel(kind.Value):"Empty cell":string.IsNullOrWhiteSpace(monitor.Slots[slot].Label)?"Empty cell":monitor.Slots[slot].Label)}";
    private static Thumb Handle(bool resize)
    {
        var factory=new FrameworkElementFactory(typeof(Grid));factory.SetValue(Panel.BackgroundProperty,Brushes.Transparent);
        if(resize){var grip=new FrameworkElementFactory(typeof(Path));grip.SetValue(Path.DataProperty,Geometry.Parse("M 2,16 L 16,2 M 8,16 L 16,8 M 14,16 L 16,14"));grip.SetValue(Path.StrokeProperty,Brushes.Silver);grip.SetValue(Path.StrokeThicknessProperty,1.5);grip.SetValue(Path.IsHitTestVisibleProperty,false);factory.AppendChild(grip);}
        return new Thumb{Template=new ControlTemplate(typeof(Thumb)){VisualTree=factory},Cursor=resize?Cursors.SizeNWSE:Cursors.SizeAll,Focusable=false};
    }
    private void Rebuild()
    {
        if(canvas.ActualWidth<=0||canvas.ActualHeight<=0)return;
        canvas.Children.Clear();cards.Clear();
        for(int x=0;x<=draft.Columns;x++)canvas.Children.Add(new Line{X1=x*canvas.ActualWidth/draft.Columns,X2=x*canvas.ActualWidth/draft.Columns,Y2=canvas.ActualHeight,Stroke=new SolidColorBrush(Color.FromRgb(48,48,48)),IsHitTestVisible=false});
        for(int y=0;y<=draft.Rows;y++)canvas.Children.Add(new Line{Y1=y*canvas.ActualHeight/draft.Rows,Y2=y*canvas.ActualHeight/draft.Rows,X2=canvas.ActualWidth,Stroke=new SolidColorBrush(Color.FromRgb(48,48,48)),IsHitTestVisible=false});
        foreach(var cell in draft.Cells)
        {
            var body=new Grid();var card=new Border{Child=body,Background=new SolidColorBrush(Color.FromRgb(46,46,46)),BorderThickness=new Thickness(2),CornerRadius=new CornerRadius(6)};
            var cellMenu=ListAppearanceUi.Create(this);var delete=new MenuItem{Header="Delete cell"};
            delete.Click+=(_,_)=>{selected=cell.Slot;HideSelected();};cellMenu.Items.Add(delete);card.ContextMenu=cellMenu;
            card.PreviewMouseRightButtonDown+=(_,e)=>{selected=cell.Slot;PositionCards();e.Handled=true;cellMenu.PlacementTarget=card;cellMenu.IsOpen=true;};
            var text=new TextBlock{Text=Label(cell.Slot),Foreground=Brushes.WhiteSmoke,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(6),FontSize=14,IsHitTestVisible=false};body.Children.Add(text);
            var move=Handle(false);move.ToolTip="Move "+Label(cell.Slot);body.Children.Add(move);
            foreach(string edge in new[]{"Left","Right","Top","Bottom"}){
                bool horizontal=edge is "Left" or "Right";var handle=Handle(false);handle.Cursor=horizontal?Cursors.SizeWE:Cursors.SizeNS;handle.ToolTip="Resize "+Label(cell.Slot);
                if(horizontal){handle.Width=12;handle.HorizontalAlignment=edge=="Left"?HorizontalAlignment.Left:HorizontalAlignment.Right;}
                else{handle.Height=12;handle.VerticalAlignment=edge=="Top"?VerticalAlignment.Top:VerticalAlignment.Bottom;}
                body.Children.Add(handle);BindDrag(handle,cell.Slot,true,edge);
            }
            var resize=Handle(true);resize.Width=resize.Height=18;resize.HorizontalAlignment=HorizontalAlignment.Right;resize.VerticalAlignment=VerticalAlignment.Bottom;resize.ToolTip="Resize "+Label(cell.Slot);body.Children.Add(resize);
            BindDrag(move,cell.Slot,false);BindDrag(resize,cell.Slot,true);canvas.Children.Add(card);cards[cell.Slot]=card;
        }
        if(hidden.Items.Count==0){hidden.Items.Add(new ComboBoxItem{Content="Empty cell"});foreach(var kind in SystemMonitorWidget.SelectableMetrics)hidden.Items.Add(new ComboBoxItem{Content=SystemMonitorWidget.GetMetricLabel(kind),Tag=kind});hidden.SelectedIndex=0;}
        foreach(ComboBoxItem item in hidden.Items)
        {
            if(item.Tag is not SystemMonitorWidget.MetricKind kind)continue;
            int count=draft.Cells.Count(cell=>
            {
                var source=pendingSources.TryGetValue(cell.Slot,out var pending)?pending:
                    monitor.Slots[cell.Slot].IsShortcut?null:(SystemMonitorWidget.MetricKind?)monitor.Slots[cell.Slot].Kind;
                return source.HasValue&&SystemMonitorWidget.NormalizeMetric(source.Value)==SystemMonitorWidget.NormalizeMetric(kind);
            });
            var caption=new TextBlock{Foreground=Brushes.WhiteSmoke};caption.Inlines.Add(SystemMonitorWidget.GetMetricLabel(kind));
            if(count>0)caption.Inlines.Add(new System.Windows.Documents.Run("  "+string.Join(" ",Enumerable.Repeat("●",Math.Min(count,3)))){Foreground=Brushes.DarkOrange});
            item.Content=caption;
        }
        PositionCards();
    }
    private void PositionCards()
    {
        foreach(var cell in draft.Cells){var card=cards[cell.Slot];Canvas.SetLeft(card,cell.X*canvas.ActualWidth/draft.Columns+3);Canvas.SetTop(card,cell.Y*canvas.ActualHeight/draft.Rows+3);card.Width=Math.Max(1,cell.Width*canvas.ActualWidth/draft.Columns-6);card.Height=Math.Max(1,cell.Height*canvas.ActualHeight/draft.Rows-6);card.BorderBrush=cell.Slot==selected?Brushes.DarkOrange:Brushes.DimGray;}
    }
    private void BindDrag(Thumb thumb,int slot,bool resize,string edge="Corner")
    {
        MonitorGridCell? start=null;double dx=0,dy=0;Point pointerStart=default;
        thumb.DragStarted+=(_,e)=>{selected=slot;start=draft.Cells.Single(c=>c.Slot==slot);dx=dy=0;pointerStart=Mouse.GetPosition(canvas);dragging=true;canvas.Focus();PositionCards();e.Handled=true;};
        thumb.DragDelta+=(_,e)=>{
            if(start==null)return;
            if(thumb.IsDragging){var pointer=Mouse.GetPosition(canvas);dx=pointer.X-pointerStart.X;dy=pointer.Y-pointerStart.Y;}else{dx+=e.HorizontalChange;dy+=e.VerticalChange;}
            int x=(int)Math.Round(dx*draft.Columns/canvas.ActualWidth),y=(int)Math.Round(dy*draft.Rows/canvas.ActualHeight);
            var next=resize?ResizeCell(start,x,y,edge,draft.Columns,draft.Rows):start with{X=Math.Clamp(start.X+x,0,draft.Columns-start.Width),Y=Math.Clamp(start.Y+y,0,draft.Rows-start.Height)};
            if(!resize&&!draft.Fits(next,slot))
            {
                // A narrow gap should not require an exact pointer position.
                // Search only adjacent grid positions, never jump across the layout.
                var nearby=(from oy in Enumerable.Range(-1,3)
                            from ox in Enumerable.Range(-1,3)
                            let candidate=next with{X=next.X+ox,Y=next.Y+oy}
                            where draft.Fits(candidate,slot)
                            orderby ox*ox+oy*oy
                            select candidate).FirstOrDefault();
                if(nearby!=null)next=nearby;
            }
            bool placed=TryPlace(next);
            if(!resize&&!placed)
            {
                // Keep the captured thumb under the pointer while crossing occupied
                // cells; only valid placements are committed to the draft.
                var card=cards[slot];Panel.SetZIndex(card,1);card.Opacity=.65;
                Canvas.SetLeft(card,Math.Clamp(start.X*canvas.ActualWidth/draft.Columns+dx,0,Math.Max(0,canvas.ActualWidth-card.Width))+3);
                Canvas.SetTop(card,Math.Clamp(start.Y*canvas.ActualHeight/draft.Rows+dy,0,Math.Max(0,canvas.ActualHeight-card.Height))+3);
            }
            else {cards[slot].Opacity=1;Panel.SetZIndex(cards[slot],0);}
            e.Handled=true;
        };
        thumb.DragCompleted+=(_,e)=>{start=null;dragging=false;cards[slot].Opacity=1;Panel.SetZIndex(cards[slot],0);PositionCards();e.Handled=true;};
    }
    internal static MonitorGridCell ResizeCell(MonitorGridCell c,int dx,int dy,string edge,int columns,int rows)
    {
        int left=c.X,top=c.Y,right=c.X+c.Width,bottom=c.Y+c.Height;
        if(edge=="Left")left=Math.Clamp(left+dx,0,right-1);
        if(edge=="Top")top=Math.Clamp(top+dy,0,bottom-2);
        if(edge is "Right" or "Corner")right=Math.Clamp(right+dx,left+1,columns);
        if(edge is "Bottom" or "Corner")bottom=Math.Clamp(bottom+dy,top+2,rows);
        return c with{X=left,Y=top,Width=right-left,Height=bottom-top};
    }
    internal bool TryPlace(MonitorGridCell cell)
    {
        if(!draft.Cells.Any(c=>c.Slot==cell.Slot)||!draft.Fits(cell,cell.Slot)){status.Text="That space is occupied or outside the grid.";return false;}
        draft=draft.Move(cell);PositionCards();status.Text=Label(cell.Slot);return true;
    }
    private void HideSelected()
    {
        if(selected<0){status.Text="Select a cell first.";return;}
        pendingSources[selected]=null;draft=draft with{Cells=draft.Cells.Where(c=>c.Slot!=selected).ToArray()};selected=-1;Rebuild();
    }
    private void AddSelected()
    {
        if(hidden.SelectedItem is not ComboBoxItem choice)return;
        SystemMonitorWidget.MetricKind? kind=choice.Tag is SystemMonitorWidget.MetricKind metric?metric:null;
        int slot=Enumerable.Range(0,monitor.Slots.Count).Except(draft.Cells.Select(c=>c.Slot)).DefaultIfEmpty(-1).First();
        if(slot<0){status.Text="The grid is full. Delete a cell first.";return;}
        foreach(var size in SystemMonitorWidget.NewSensorSizes(kind))
        for(int y=0;y<=draft.Rows-size.H;y++)for(int x=0;x<=draft.Columns-size.W;x++){var cell=new MonitorGridCell(slot,x,y,size.W,size.H);if(!draft.Fits(cell,slot))continue;draft=draft with{Cells=[..draft.Cells,cell]};pendingSources[slot]=kind;selected=slot;Rebuild();return;}
        status.Text="Make room by shrinking or deleting a cell first.";
    }
    private void KeyboardMove(object sender,KeyEventArgs e)
    {
        if(selected<0||e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))return;
        var cell=draft.Cells.Single(c=>c.Slot==selected);int x=e.Key==Key.Left?-1:e.Key==Key.Right?1:0,y=e.Key==Key.Up?-1:e.Key==Key.Down?1:0;
        TryPlace(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?cell with{Width=cell.Width+x,Height=cell.Height+y}:cell with{X=cell.X+x,Y=cell.Y+y});e.Handled=true;
    }
}
