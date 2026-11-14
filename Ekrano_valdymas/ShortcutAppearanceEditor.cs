using System.Windows;
using System.Windows.Controls;

namespace Ekrano_valdymas;

internal static class ShortcutAppearanceEditor
{
    internal static string? Normalize(string? value) => value is "Text" or "Icon" or "IconText" ? value : null;

    internal static SettingsWindow Create(Window? owner,string name,string target,string presentation,Action<string,string,string,string?> save,bool web=false,Action<string,string,string>? preview=null,Action? clearPreview=null,string? iconPath=null,Action<string?>? previewIcon=null,Action<Panel>? textEditor=null,Action? resetText=null)
    {
        var window=AppearanceEditorUi.Window(web?"Web shortcut":"Shortcut",owner,640);
        var root=new DockPanel();window.Content=root;
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(18,4,18,14)};
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var panel=new StackPanel{Margin=new Thickness(18,0,18,8)};
        StackPanel? textPanel=null;
        if(textEditor==null)root.Children.Add(MacroUi.Scroll(panel));
        else
        {
            window.Width=820;
            var columns=new Grid();columns.ColumnDefinitions.Add(new());columns.ColumnDefinitions.Add(new());columns.RowDefinitions.Add(new(){Height=GridLength.Auto});columns.RowDefinitions.Add(new(){Height=GridLength.Auto});
            textPanel=new StackPanel{Margin=new Thickness(8,0,18,8)};Grid.SetColumn(textPanel,1);columns.Children.Add(panel);columns.Children.Add(textPanel);root.Children.Add(MacroUi.Scroll(columns));
            columns.SizeChanged+=(_,_)=>{bool narrow=columns.ActualWidth<680;Grid.SetColumn(textPanel,narrow?0:1);Grid.SetRow(textPanel,narrow?1:0);Grid.SetColumnSpan(panel,narrow?2:1);Grid.SetColumnSpan(textPanel,narrow?2:1);};
        }
        var nameBox=new TextBox{Text=name,MaxLength=120,Padding=new Thickness(10,6,10,6)};
        var nameFrame=AppearanceEditorUi.AttentionField(panel,"Name",nameBox);
        var targetBox=new TextBox{Text=target,IsReadOnly=!web,Padding=new Thickness(10,6,10,6)};
        var targetFrame=AppearanceEditorUi.AttentionField(panel,web?"Web address":"Opens",targetBox);
        var iconBox=new TextBox{Text=iconPath??"",Padding=new Thickness(10,6,10,6)};
        // Assign the menu itself: the native text editor can handle opening
        // before the implicit style's ContextMenuOpening handler is reached.
        foreach(var input in new[]{nameBox,targetBox,iconBox})
            input.ContextMenu=TextEditMenu.Create(input);
        bool isMacro=MacroLibrary.IsReference(target);
        if(!isMacro)
        {
            AppearanceEditorUi.AttentionField(panel,"Icon image (empty uses default)",iconBox);
            var buttons=new WrapPanel();
                buttons.Children.Add(MacroUi.Button("Choose file…",(_,_)=>{if(DefaultIconCatalog.ChooseImage(window) is string path)iconBox.Text=path;}));
            buttons.Children.Add(MacroUi.Button("Built-in icons…",(_,_)=>{if(BuiltInIconGallery.Choose(window) is string path)iconBox.Text=path;}));
            buttons.Children.Add(MacroUi.Button("Reset icon",(_,_)=>iconBox.Text=""));panel.Children.Add(buttons);
            iconBox.TextChanged+=(_,_)=>previewIcon?.Invoke(iconBox.Text.Trim());
        }
        string selected=presentation is "Text" or "Icon" or "IconText"?presentation:web&&presentation=="Original"?"Original":"IconText";
        var viewGroup=new StackPanel{Margin=new Thickness(4,6,4,8)};
        viewGroup.Children.Add(new TextBlock{Name="ShortcutViewHeading",Text="View",Margin=new Thickness(0,0,0,8)});
        var view=new StackPanel{Name="ShortcutView",Orientation=Orientation.Horizontal};
        viewGroup.Children.Add(view);
        var choices=new List<CheckBox>();
        void Select(string key){selected=key;foreach(var item in choices)item.IsChecked=Equals(item.Tag,key);}
        void AddView(string key,string label)
        {
            var check=new CheckBox{Content=label,Tag=key,Foreground=System.Windows.Media.Brushes.White,IsChecked=selected==key,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)};
            check.Click+=(_,_)=>{Select(key);if(web&&key=="Original")iconBox.Text="";UpdatePreview();};choices.Add(check);view.Children.Add(check);
        }
        if(web)AddView("Original","Original");
        AddView("Text","Text");AddView("Icon","Icon");AddView("IconText","Icon + text");panel.Children.Insert(2,new Border{BorderBrush=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(104,100,94)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Background=System.Windows.Media.Brushes.Transparent,Padding=new Thickness(4),Margin=new Thickness(4,8,4,8),Child=viewGroup});
        var iconEffect=ShortcutIconEffect.Editor(viewGroup,target,web);
        iconEffect.SelectionChanged+=(_,_)=>ShortcutIconEffect.Preview(target,AppearanceEditorUi.Selected(iconEffect));
        AppearanceEditorUi.Help(panel,web?"Original uses the saved page preview or website icon; a link symbol is shown when neither is available.":"This choice applies only to this shortcut and follows it when moved.");
        AppearanceEditorUi.Help(panel,"Changes appear immediately on the dashboard. Save keeps them; Cancel restores the previous appearance.");
        if(textPanel!=null)textEditor!(textPanel);
        var error=AppearanceEditorUi.Help(panel,"");
        void UpdatePreview()=>preview?.Invoke(nameBox.Text.Trim(),targetBox.Text.Trim(),selected);
        nameBox.TextChanged+=(_,_)=>UpdatePreview();targetBox.TextChanged+=(_,_)=>UpdatePreview();
        window.Closed+=(_,_)=>{ShortcutIconEffect.Preview(target,null);clearPreview?.Invoke();};
        footer.Children.Add(MacroUi.Button("Default",(_,_)=>{Select(web?"Original":"IconText");iconBox.Text="";iconEffect.SelectedIndex=0;resetText?.Invoke();UpdatePreview();}));
        footer.Children.Add(MacroUi.Button("Save",(_,_)=>
        {
            if(string.IsNullOrWhiteSpace(nameBox.Text)){AppearanceEditorUi.Pulse(nameFrame);nameBox.Focus();return;}
            string destination=targetBox.Text.Trim();
            if(web)
            {
                if(!destination.Contains("://",StringComparison.Ordinal))destination="https://"+destination;
                if(!Uri.TryCreate(destination,UriKind.Absolute,out var uri)||uri.Scheme is not ("https" or "http"))
                {error.Text="Enter an http or https web address.";AppearanceEditorUi.Pulse(targetFrame);return;}
                destination=uri.AbsoluteUri;
            }
            string chosen=iconBox.Text.Trim();
            if(!isMacro&&chosen.Length>0&&ShortcutIcon.Load(chosen)==null){error.Text="Choose a readable image file.";return;}
            try { save(nameBox.Text.Trim(),destination,selected,isMacro?null:chosen);ShortcutIconEffect.Save(destination,AppearanceEditorUi.Selected(iconEffect));window.DialogResult=true; }
            catch(Exception exception){error.Text="Could not save: "+exception.Message;}
        }));
        var cancel=MacroUi.Button("Cancel",(_,_)=>window.DialogResult=false);cancel.IsCancel=true;footer.Children.Add(cancel);
        AppearanceEditorUi.Frame(window);return window;
    }
}
