using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;
public partial class MainWindow
{
    private void InitializeMenuRefinements()
    {
        DetachMenu(applicationSettingsMenuItem);
        ContextMenu.Items.Insert(ContextMenu.Items.IndexOf(monitorControlsMenuItem)+1,applicationSettingsMenuItem);
        DetachMenu(alwaysOnTopMenuItem);
        DetachMenu(showModuleTitlesMenuItem);
        var menuSizeItem=(MenuItem)menuSizeRowGrid.Parent;
        DetachMenu(menuSizeItem);
        applicationSettingsMenuItem.Items.Insert(0,alwaysOnTopMenuItem);
        applicationSettingsMenuItem.Items.Insert(1,menuSizeItem);
        applicationSettingsMenuItem.Items.Insert(2,showModuleTitlesMenuItem);
        var tips=new StackPanel{Orientation=Orientation.Horizontal};
        var tipsLabel=new OpticallyCenteredText{Text="Tooltips",CompactLineBox=true,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,16,0)};
        foreach(var pair in new[]{(OpticallyCenteredText.TextFontFamilyProperty,"FontFamily"),(OpticallyCenteredText.TextFontSizeProperty,"FontSize"),(OpticallyCenteredText.TextFontWeightProperty,"FontWeight"),(OpticallyCenteredText.TextBrushProperty,"Foreground")})tipsLabel.SetBinding(pair.Item1,new System.Windows.Data.Binding(pair.Item2){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor,typeof(MenuItem),1)});tips.Children.Add(tipsLabel);
        var delays=new List<CheckBox>();
        foreach(int seconds in new[]{2,3,5,0})
        {
            var tip=new CheckBox{Content=seconds==0?"Off":$"{seconds} s",Tag=seconds,
                Margin=new Thickness(0,0,seconds==0?0:16,0),IsChecked=TooltipPolicy.DelaySeconds==seconds,
                Foreground=Brushes.WhiteSmoke,VerticalAlignment=VerticalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center};
            tip.SetResourceReference(StyleProperty,"DashboardCheckBox");delays.Add(tip);tips.Children.Add(tip);
            tip.Click+=(_,_)=>{TooltipPolicy.SetDelay(seconds);foreach(var choice in delays)choice.IsChecked=ReferenceEquals(choice,tip);};
        }
        applicationSettingsMenuItem.Items.Insert(3,new MenuItem{Header=tips,StaysOpenOnClick=true});
        applicationSettingsMenuItem.Icon=MenuGlyph("\uE713");
        var autoStart=new MenuItem{Header="Start with Windows",IsCheckable=true,StaysOpenOnClick=true};
        ToolTipService.SetShowOnDisabled(autoStart,true);
        int startupReadVersion=0;
        bool changingStartup=false;
        void ApplyStartupStatus(WindowsStartupStatus status)
        {
            autoStart.IsChecked=status.IsEnabled;
            autoStart.IsEnabled=status.CanChange;
            autoStart.ToolTip=status.Message;
        }
        async Task RefreshAutoStartAsync()
        {
            if(changingStartup)return;
            int version=++startupReadVersion;
            autoStart.IsEnabled=false;
            try
            {
                var status=await WindowsAutoStart.GetStatusAsync();
                if(version==startupReadVersion)ApplyStartupStatus(status);
            }
            catch(Exception error)
            {
                InteractionDiagnostics.RecordFailure("Read Windows startup",error);
                if(version==startupReadVersion)
                {
                    autoStart.IsChecked=false;
                    autoStart.ToolTip="Windows startup is unavailable for this installation. "+error.Message;
                }
            }
        }
        autoStart.Click+=async (_,_)=>
        {
            if(changingStartup)return;
            bool requested=autoStart.IsChecked;
            changingStartup=true;
            startupReadVersion++;
            autoStart.IsEnabled=false;
            try
            {
                var status=await WindowsAutoStart.SetEnabledAsync(requested);
                ApplyStartupStatus(status);
                if(status.IsEnabled!=requested)
                {
                    string message=status.Message??"Windows did not change the startup setting. Try again from Windows Settings > Apps > Startup.";
                    if(status.OpenWindowsSettings && requested)
                    {
                        if(AppMessage.Show(this,message+"\n\nOpen Windows startup settings?","BlakiDeck",MessageBoxButton.YesNo,MessageBoxImage.Information)==MessageBoxResult.Yes)
                            using(var settings=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps"){UseShellExecute=true})){}
                    }
                    else AppMessage.Show(this,message,"BlakiDeck",MessageBoxButton.OK,MessageBoxImage.Information);
                }
            }
            catch(Exception error){InteractionDiagnostics.RecordFailure("Windows startup",error);AppMessage.Show(this,"Could not change Windows startup: "+error.Message,"BlakiDeck",MessageBoxButton.OK,MessageBoxImage.Warning);}
            finally{changingStartup=false;await RefreshAutoStartAsync();}
        };
        applicationSettingsMenuItem.SubmenuOpened+=async (_,e)=>{if(ReferenceEquals(e.OriginalSource,applicationSettingsMenuItem))await RefreshAutoStartAsync();};
        applicationSettingsMenuItem.Items.Insert(1,autoStart);
        int textIndex=applicationSettingsMenuItem.Items.IndexOf(menuSizeItem)+1;
        foreach(var textItem in new[]{(MenuItem)menuTextRow.Parent,(MenuItem)usageTextRow.Parent,menuTextDefaultItem})
        {DetachMenu(textItem);applicationSettingsMenuItem.Items.Insert(textIndex++,textItem);}
        if(playerOptionsMenu.Items.OfType<MenuItem>().FirstOrDefault(item=>Equals(item.Header,"Source")) is {} source)
            AddDefaultSourceIconOption(source);
        foreach(var root in ContextMenu.Items.OfType<MenuItem>().Where(i=>i.Header is string))
        {
            root.MinHeight=38;
            root.Margin=new Thickness(0);
        }
        var glyphs=new[]{"\uE7F4","\uE74A","\uE74B","\uE8A7"};
        int actionIndex=0;
        foreach(var button in fitMonitorButtonsGrid.Children.OfType<Button>())
        {
            var content=new StackPanel{Orientation=Orientation.Horizontal};
            var icon=MenuGlyph(glyphs[actionIndex++]);icon.Stretch=Stretch.Uniform;icon.Margin=new Thickness(0,0,8,0);content.Children.Add(icon);
            content.Children.Add(new OpticallyCenteredText{Text=button.Content.ToString()!,TextFontFamily=ContextMenu.FontFamily,TextFontSize=18,TextFontWeight=FontWeights.SemiBold,TextBrush=Brushes.White});button.Content=content;
        }
        foreach(var slider in new[]{menuTextSlider,usageTextSlider})
            slider.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,
                new System.Windows.Controls.Primitives.DragCompletedEventHandler((_,_)=>Dispatcher.BeginInvoke(new Action(ApplyMenuTextSizes))));
        NormalizeMenuGlyphs(ContextMenu);
        InitializeContextActions();
        if(uninstallMenuButton.Parent is Grid helpRow && helpRow.Children.OfType<Button>().FirstOrDefault(b=>b.Content is Grid)?.Content is Grid helpContent && helpContent.Children.OfType<TextBlock>().FirstOrDefault() is {} helpGlyph){int index=helpContent.Children.IndexOf(helpGlyph);helpContent.Children.Remove(helpGlyph);helpContent.Children.Insert(index,MenuGlyph(helpGlyph.Text,helpGlyph.FontFamily));}
    }

    // Normalize the actual glyph outline, rather than its family-dependent em box.
    private static Image MenuGlyph(string text,FontFamily? family=null)
    {
        var outline=new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
            new Typeface(family??new FontFamily("Segoe MDL2 Assets"),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),
            20,Brushes.White,1).BuildGeometry(new Point());
        return MenuOutline(outline);
    }
    private static Image MenuOutline(Geometry outline)
    {
        Rect ink=outline.Bounds;
        if(!ink.IsEmpty&&ink.Height>0&&ink.Width>0)
        {
            // Equal visible height, with a wider slot for monitor and layout glyphs.
            // Keep the original aspect ratio rather than compressing wide symbols.
            double scale=20/ink.Height;
            outline.Transform=new MatrixTransform(scale,0,0,scale,16-(ink.X+ink.Width/2)*scale,12-(ink.Y+ink.Height/2)*scale);
        }
        outline.Freeze();
        var group=new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(new Rect(0,0,32,24))));
        group.Children.Add(new GeometryDrawing(Brushes.White,null,outline));group.Freeze();
        var source=new DrawingImage(group);source.Freeze();
        return new Image{Source=source,Width=32,Height=24,Stretch=Stretch.None,IsHitTestVisible=false};
    }
    private static void NormalizeMenuGlyphs(ItemsControl owner)
    {
        foreach(var item in owner.Items.OfType<MenuItem>())
        {
            if(item.Icon is TextBlock text)item.Icon=MenuGlyph(text.Text,text.FontFamily);
            else if(item.Icon is OpticallyCenteredText optical)item.Icon=MenuGlyph(optical.Text,optical.TextFontFamily);
            else if(item.Icon is System.Windows.Shapes.Path path&&path.Data!=null)
                item.Icon=MenuOutline(path.Stroke!=null?path.Data.GetWidenedPathGeometry(new Pen(path.Stroke,path.StrokeThickness)):path.Data.Clone());
            NormalizeMenuGlyphs(item);
        }
    }
}
