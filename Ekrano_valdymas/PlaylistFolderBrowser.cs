using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Ekrano_valdymas;

internal sealed class PlaylistFolderBrowser : SettingsWindow
{
    private readonly TreeView folders=new(){Background=Brushes.Transparent,BorderBrush=Brushes.DimGray,Foreground=Brushes.White};
    private readonly ListBox files=new(){SelectionMode=SelectionMode.Extended,DisplayMemberPath="Name",Background=Brushes.Transparent,Foreground=Brushes.White,BorderBrush=Brushes.DimGray};
    private readonly TextBlock status=new(){Foreground=Brushes.Silver,TextWrapping=TextWrapping.Wrap};
    private string[] directFiles=[];
    private int selectionVersion;
    private bool closed;
    internal int DirectFileCount=>directFiles.Length;
    internal void SelectDiagnosticFolder(string path){if(DiagnosticState.Root==null)throw new InvalidOperationException();var node=Node(path,System.IO.Path.GetFileName(path));folders.Items.Insert(0,node);node.IsSelected=true;node.IsExpanded=true;}
    private sealed record AudioFile(string Path){public string Name=>System.IO.Path.GetFileName(Path);}

    internal PlaylistFolderBrowser(Window owner,Func<string[],Task> add)
    {
        Owner=owner;Title="Music folders";Width=780;Height=560;MinWidth=560;MinHeight=380;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;WindowStyle=WindowStyle.None;
        Background=new SolidColorBrush(Color.FromRgb(39,39,39));Foreground=Brushes.White;FontFamily=new FontFamily("Segoe UI");FontSize=16;
        var root=new DockPanel{Margin=new Thickness(16)};Content=root;
        var heading=new TextBlock{Text="Music folders",FontSize=22,Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
        heading.MouseLeftButtonDown+=(_,e)=>{if(e.ButtonState==System.Windows.Input.MouseButtonState.Pressed)DragMove();};
        var bottom=new StackPanel{Margin=new Thickness(0,10,0,0)};DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);bottom.Children.Add(status);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};bottom.Children.Add(actions);
        async Task Add(string[] paths){if(paths.Length==0)return;try{await add(paths);status.Text=$"Added {paths.Length} file(s). Files on disk are unchanged.";}catch(Exception ex){status.Text=ex.Message;}}
        actions.Children.Add(MacroUi.Button("Add selected",async(_,_)=>await Add(files.SelectedItems.Cast<AudioFile>().Select(f=>f.Path).ToArray())));
        actions.Children.Add(MacroUi.Button("Add folder",async(_,_)=>await Add(directFiles)));
        actions.Children.Add(MacroUi.Button("Close",(_,_)=>Close()));
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(2,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(3,GridUnitType.Star)});root.Children.Add(grid);
        folders.Margin=new Thickness(0,0,10,0);grid.Children.Add(folders);Grid.SetColumn(files,1);grid.Children.Add(files);
        PlaylistAppearance.ApplyChrome(files);
        folders.Resources[SystemColors.HighlightBrushKey]=new SolidColorBrush(Color.FromRgb(90,67,35));
        folders.Resources[SystemColors.HighlightTextBrushKey]=Brushes.White;
        folders.Resources[SystemColors.InactiveSelectionHighlightBrushKey]=new SolidColorBrush(Color.FromRgb(70,57,39));
        folders.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey]=Brushes.White;
        folders.Resources[typeof(ScrollBar)]=files.Resources[typeof(ScrollBar)];
        folders.SelectedItemChanged+=async(_,_)=>{
            int version=++selectionVersion;directFiles=[];files.ItemsSource=null;
            if(folders.SelectedItem is not TreeViewItem{Tag:string path})return;
            status.Text=path;
            try{var paths=await Task.Run(()=>LocalPlayback.ExpandAudioPaths([path]));if(closed||version!=selectionVersion)return;directFiles=paths;files.ItemsSource=paths.Select(p=>new AudioFile(p)).ToArray();status.Text=$"{paths.Length} audio file(s) in this folder. Subfolders are not included.";}catch(Exception ex){if(version==selectionVersion)status.Text=ex.Message;}
        };
        files.MouseDoubleClick+=async(_,e)=>{if(files.SelectedItem is AudioFile file){e.Handled=true;await Add([file.Path]);}};
        string music=Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if(Directory.Exists(music)){var node=Node(music,"Music");folders.Items.Add(node);node.IsSelected=true;}
        foreach(var drive in DriveInfo.GetDrives())folders.Items.Add(Node(drive.RootDirectory.FullName,drive.Name));
        Closed+=(_,_)=>{closed=true;selectionVersion++;};
    }
    private TreeViewItem Node(string path,string name)
    {
        var item=new TreeViewItem{Header=name,Tag=path,Foreground=Brushes.White};item.Items.Add(new TreeViewItem{Header="…"});
        item.Expanded+=async(_,e)=>{
            if(!ReferenceEquals(e.OriginalSource,item)||item.Items.Count!=1||item.Items[0] is not TreeViewItem{Tag:null})return;
            e.Handled=true;item.Items.Clear();
            try{var children=await Task.Run(()=>Directory.GetDirectories(path));if(closed)return;foreach(string child in children.OrderBy(p=>p,StringComparer.CurrentCultureIgnoreCase))item.Items.Add(Node(child,System.IO.Path.GetFileName(child)));}
            catch(Exception ex){status.Text=ex.Message;}
        };return item;
    }
}
