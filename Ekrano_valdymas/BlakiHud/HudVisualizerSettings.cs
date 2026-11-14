using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

// This editor updates the module's own profile and uses the existing dashboard
// settings controls. Both spectral layers share the card's single audio input.
internal static class HudVisualizerSettings
{
    internal static SettingsWindow Create(HudVisualizerCard card,Window? owner)
    {
        var window=MacroUi.Window("BlakiHUD music visualizer",owner,650,730);
        window.Name="HudVisualizerSettings";window.MinWidth=540;window.MinHeight=460;window.ShowInTaskbar=false;
        var original=card.Profile.Copy();bool saved=false;
        var root=new DockPanel();window.Content=root;
        var footer=new DockPanel{LastChildFill=false,Margin=new Thickness(18,8,18,14)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var actions=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(actions,Dock.Right);footer.Children.Add(actions);
        var save=MacroUi.Button("Save",(_,_)=>{});save.Name="HudVisualizerSave";actions.Children.Add(save);
        var close=MacroUi.Button("Cancel",(_,_)=>window.Close());close.Name="HudVisualizerClose";close.IsCancel=true;actions.Children.Add(close);
        var restore=MacroUi.Button("Restore defaults",(_,_)=>{});restore.Name="HudVisualizerRestoreImported";
        restore.ToolTip="Restore the visualizer defaults: mirrored pair, album cover and 100% overall size, including the original motion, colors and advanced settings.";
        DockPanel.SetDock(restore,Dock.Left);footer.Children.Add(restore);
        var error=new TextBlock{Name="HudVisualizerError",Foreground=Brushes.Salmon,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(18,0,18,4)};
        DockPanel.SetDock(error,Dock.Bottom);root.Children.Add(error);
        var tabs=SettingsEditorTabs.Create("HudVisualizerSettingsTabs");tabs.Margin=new Thickness(18,4,18,4);root.Children.Add(tabs);
        var basic=new StackPanel();var colors=new StackPanel();var advanced=new StackPanel();
        tabs.Items.Add(new TabItem{Header="Basic",Content=MacroUi.Scroll(basic)});
        tabs.Items.Add(new TabItem{Header="Colors",Content=MacroUi.Scroll(colors)});
        tabs.Items.Add(new TabItem{Header="Advanced",Content=MacroUi.Scroll(advanced)});
        var imported=HudVisualizerProfile.Imported();bool reflecting=false;
        var refreshers=new List<Action>();
        void Changed(){card.RefreshProfile();error.Text="";}
        OpticallyCenteredText Caption(Panel panel,string text,string? name=null)
        {
            var caption=new OpticallyCenteredText{Text=text,Height=30,TextFontFamily=window.FontFamily,TextFontSize=window.FontSize,TextFontWeight=FontWeights.Normal,TextBrush=window.Foreground,InkAlignment=HorizontalAlignment.Left};
            if(name!=null)caption.Name=name;panel.Children.Add(caption);return caption;
        }
        CheckBox Check(Panel panel,string caption,string name,Func<bool> read,Action<bool> write)
        {
            var check=new CheckBox{Name=name,Content=caption,IsChecked=read(),Foreground=window.Foreground,Margin=new Thickness(0,6,0,8),MinHeight=30};
            check.Click+=(_,_)=>{if(reflecting)return;write(check.IsChecked==true);Changed();Reflect();};panel.Children.Add(check);
            refreshers.Add(()=>check.IsChecked=read());return check;
        }
        Slider Range(Panel panel,string title,string name,double min,double max,Func<double> read,Action<double> write,double reference,bool integer=false,string? captionName=null,Func<double,string>? format=null)
        {
            format??=value=>$"{title} · {value:0.##}%";
            var caption=Caption(panel,format(read()),captionName);
            var slider=new Slider{Name=name,Minimum=min,Maximum=max,Value=read(),Height=28,Margin=new Thickness(0,0,0,8),TickFrequency=1,IsSnapToTickEnabled=integer,SmallChange=1,LargeChange=integer?5:10};
            SliderDefaults.Mark(slider,reference,"Default setting");panel.Children.Add(slider);
            slider.ValueChanged+=(_,_)=>
            {
                caption.Text=format(slider.Value);if(reflecting)return;
                write(slider.Value);Changed();Reflect();
            };
            refreshers.Add(()=>{slider.Value=read();caption.Text=format(read());});return slider;
        }
        void Reflect()
        {
            reflecting=true;try{foreach(var refresh in refreshers)refresh();}finally{reflecting=false;}
        }
        Check(basic,"Enabled","HudVisualizerEnabled",()=>card.Profile.MusicVisualizerEnabled,v=>card.Profile.MusicVisualizerEnabled=v);
        var automatic=Check(basic,"Automatic · hide when music stops","HudVisualizerAutomatic",()=>card.Profile.MusicVisualizerAuto,v=>card.Profile.MusicVisualizerAuto=v);
        automatic.ToolTip="When enabled, hide the visualizer while playback is stopped or paused. When disabled, keep a static visualizer visible. This does not change sensitivity while music plays.";
        Caption(basic,"Composition");
        var compositions=new WrapPanel{Margin=new Thickness(0,0,0,10)};basic.Children.Add(compositions);
        foreach(var (key,label) in new[]{("Contour","Single contour"),("Mirrored","Mirrored pair"),("RadialBars","Radial bars")})
        {
            var button=MacroUi.Button(label,(_,_)=>{card.Profile.Composition=key;card.Profile.MirrorSpectrum=key=="Mirrored";Changed();Reflect();});
            button.Name="HudComposition"+key;compositions.Children.Add(button);
            refreshers.Add(()=>
            {
                bool selected=card.Profile.EffectiveComposition==key;
                button.Background=selected?new SolidColorBrush(Color.FromRgb(76,48,0)):Brushes.Transparent;
                button.BorderBrush=selected?Brushes.Orange:new SolidColorBrush(Color.FromArgb(85,255,255,255));
                System.Windows.Automation.AutomationProperties.SetItemStatus(button,selected?"Selected":"");
            });
        }
        var overall=Range(basic,"Overall size","HudVisualizerOverallSize",75,HudVisualizerProfile.MaximumOverallVisualSize*100,()=>card.Profile.OverallVisualSize*100,v=>card.Profile.OverallVisualSize=v/100,100);
        overall.ToolTip="Enlarge the whole visualizer within the module. The complete effect is kept inside the module at high reaction and glow settings.";
        Caption(basic,"Center");
        var center=new ComboBox{Name="HudVisualizerCenterContent",MinHeight=34,Margin=new Thickness(0,0,0,8)};
        foreach(var (key,label) in new[]{("Music","MUSIC text"),("SourceIcon","Music source icon"),("AlbumCover","Album cover")})center.Items.Add(new ComboBoxItem{Tag=key,Content=label});
        basic.Children.Add(center);
        refreshers.Add(()=>center.SelectedItem=center.Items.OfType<ComboBoxItem>().First(i=>Equals(i.Tag,card.Profile.CenterContent)));
        center.SelectionChanged+=(_,_)=>{if(reflecting||center.SelectedItem is not ComboBoxItem item)return;card.Profile.CenterContent=(string)item.Tag;Changed();};
        Range(basic,"Reaction","HudVisualizerReaction",0,100,()=>card.Profile.MusicVisualizerHeight*100,v=>card.Profile.MusicVisualizerHeight=v/100,imported.MusicVisualizerHeight*100);
        Range(basic,"Smoothing","HudVisualizerSmoothing",0,100,()=>card.Profile.MusicVisualizerSmoothing*100,v=>card.Profile.MusicVisualizerSmoothing=v/100,imported.MusicVisualizerSmoothing*100);
        var presetCaption=Caption(basic,"Motion","HudVisualizerPresetCaption");
        var presets=new WrapPanel{Margin=new Thickness(0,0,0,4)};basic.Children.Add(presets);
        foreach(string key in new[]{"Calm","Soft","Pulse"})
        {
            var button=MacroUi.Button(key,(_,_)=>{card.ApplyPreset(key);Reflect();});button.Name="HudVisualizerPreset"+key;presets.Children.Add(button);
            button.ToolTip=key switch
            {
                "Calm"=>"Restrained movement and a gentle center pulse. Keeps composition and colors.",
                "Soft"=>"Slow, smooth movement with a long decay. Keeps composition and colors.",
                "Pulse"=>"Quick bass pulses with pronounced center movement. Keeps composition and colors.",
                _=>"Wide, lively motion with a clear outline."
            };
            refreshers.Add(()=>
            {
                bool selected=card.SelectedPreset==key;
                button.Background=selected?new SolidColorBrush(Color.FromRgb(76,48,0)):Brushes.Transparent;
                button.BorderBrush=selected?Brushes.Orange:new SolidColorBrush(Color.FromArgb(85,255,255,255));
                System.Windows.Automation.AutomationProperties.SetItemStatus(button,selected?"Selected":"");
            });
        }
        refreshers.Add(()=>presetCaption.Text="Motion · "+(card.SelectedPreset is "Calm" or "Soft" or "Pulse"?card.SelectedPreset:"Custom"));
        tabs.SelectionChanged+=(_,_)=>window.RequestContentFit();

        void ColorChoices(string title,string tag,bool accent)
        {
            Caption(colors,title);
            var row=new WrapPanel{Name=tag,Tag=tag,Orientation=Orientation.Horizontal,Margin=new Thickness(0,2,0,20)};colors.Children.Add(row);
            var choices=new List<(Button Button,int Index)>();
            double Position()=>accent?card.Profile.AccentPosition:card.Profile.TextColorPosition;
            string? Custom()=>accent?card.Profile.CustomAccentColor:card.Profile.CustomTextColor;
            void SetPosition(double value){if(accent)card.Profile.AccentPosition=value;else card.Profile.TextColorPosition=value;}
            void SetCustom(string? value){if(accent)card.Profile.CustomAccentColor=value;else card.Profile.CustomTextColor=value;}
            Color Current()=>CompactColorPicker.Parse(Custom(),HudVisualizerCard.Palette(Position()));
            string[] names=["White","Red","Orange","Yellow","Green","Cyan","Blue","Purple","Black"];
            foreach(int index in Enumerable.Range(0,names.Length))
            {
                var button=MacroUi.Button("",(_,_)=>{SetPosition(index);SetCustom(null);Changed();Reflect();});
                button.Width=40;button.Height=30;button.MinHeight=30;button.MinWidth=0;button.Padding=new Thickness(0);button.Margin=new Thickness(0,2,5,2);button.Tag=index;
                button.Background=new SolidColorBrush(HudVisualizerCard.Palette(index));button.ToolTip=names[index];
                System.Windows.Automation.AutomationProperties.SetName(button,names[index]);choices.Add((button,index));row.Children.Add(button);
            }
            var custom=MacroUi.Button("",(_,_)=>{});custom.Tag="Custom";custom.Width=102;custom.Height=30;custom.MinHeight=30;custom.MinWidth=0;custom.Padding=new Thickness(0);custom.Margin=new Thickness(12,2,0,2);
            var customCaption=new OpticallyCenteredText{Text="Custom",TextFontFamily=window.FontFamily,TextFontSize=14,TextBrush=Brushes.White,Height=24};custom.Content=customCaption;row.Children.Add(custom);
            System.Windows.Automation.AutomationProperties.SetName(custom,"Custom "+title.ToLowerInvariant());
            custom.Click+=(_,_)=>
            {
                string? originalCustom=Custom();double originalPosition=Position();string initial=Current().ToString();bool notified=false;
                var picker=CompactColorPicker.Create(window,initial,value=>{notified=true;SetCustom(value);Changed();Reflect();});
                if(picker.ShowDialog()!=true){SetCustom(originalCustom);SetPosition(originalPosition);Changed();Reflect();}
                else if(!notified){SetCustom(initial);Changed();Reflect();}
            };
            refreshers.Add(()=>
            {
                foreach(var (button,index) in choices)
                {
                    bool selected=Custom()==null&&Position()==index;
                    button.BorderBrush=selected?Brushes.Orange:new SolidColorBrush(Color.FromRgb(100,95,87));button.BorderThickness=new Thickness(selected?2:1);
                    System.Windows.Automation.AutomationProperties.SetItemStatus(button,selected?"Selected":"");
                }
                bool importedMix=Custom()==null&&Position()!=Math.Truncate(Position());
                bool customSelected=Custom()!=null||importedMix;var current=Current();custom.Background=new SolidColorBrush(current);
                custom.BorderBrush=customSelected?Brushes.Orange:new SolidColorBrush(Color.FromRgb(100,95,87));custom.BorderThickness=new Thickness(customSelected?2:1);
                customCaption.TextBrush=(.2126*current.R+.7152*current.G+.0722*current.B)*(current.A/255d)>150?Brushes.Black:Brushes.White;
                custom.ToolTip=$"{(importedMix?"Imported palette mix":"Custom")} · {current}";System.Windows.Automation.AutomationProperties.SetItemStatus(custom,customSelected?"Selected":"");
            });
        }
        ColorChoices("Accent color","HudVisualizerAccentPalette",true);ColorChoices("Text color","HudVisualizerTextPalette",false);

        Range(advanced,"Scale","HudVisualizerScale",60,110,()=>card.Profile.MusicVisualizerScale*100,v=>card.Profile.MusicVisualizerScale=v/100,imported.MusicVisualizerScale*100);
        Range(advanced,"Glow","HudVisualizerGlow",0,100,()=>card.Profile.MusicVisualizerGlow*100,v=>card.Profile.MusicVisualizerGlow=v/100,imported.MusicVisualizerGlow*100);
        Range(advanced,"Center reaction","HudVisualizerCenterReaction",0,100,()=>card.Profile.MusicVisualizerCenterReaction*100,v=>card.Profile.MusicVisualizerCenterReaction=v/100,imported.MusicVisualizerCenterReaction*100);
        advanced.Children.Add(new TextBlock{Text="Frequency range · 18–140 Hz bass band",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,6)});
        static string Hertz(double bin)=>(18+122*bin/41).ToString("0.#",System.Globalization.CultureInfo.CurrentCulture)+" Hz";
        Range(advanced,"Frequency start","HudVisualizerStartBin",0,40,()=>card.Profile.MusicVisualizerStartBin,v=>
        {
            card.Profile.MusicVisualizerStartBin=(int)Math.Round(v);
            if(card.Profile.MusicVisualizerEndBin<=card.Profile.MusicVisualizerStartBin)card.Profile.MusicVisualizerEndBin=card.Profile.MusicVisualizerStartBin+1;
        },imported.MusicVisualizerStartBin,true,"HudVisualizerStartFrequency",v=>"Frequency start · "+Hertz(v));
        Range(advanced,"Frequency end","HudVisualizerEndBin",1,41,()=>card.Profile.MusicVisualizerEndBin,v=>
        {
            card.Profile.MusicVisualizerEndBin=(int)Math.Round(v);
            if(card.Profile.MusicVisualizerStartBin>=card.Profile.MusicVisualizerEndBin)card.Profile.MusicVisualizerStartBin=card.Profile.MusicVisualizerEndBin-1;
        },imported.MusicVisualizerEndBin,true,"HudVisualizerEndFrequency",v=>"Frequency end · "+Hertz(v));
        Check(advanced,"Reverse spectrum","HudVisualizerInvert",()=>card.Profile.MusicVisualizerInvert,v=>card.Profile.MusicVisualizerInvert=v);
        restore.Click+=(_,_)=>{card.RestoreImportedProfile();Reflect();};
        bool Commit()
        {
            try{card.SaveProfile();original=card.Profile.Copy();error.Text="";return true;}
            catch(Exception){error.Text="Settings could not be saved. Check access to your settings folder and try again.";return false;}
        }
        save.Click+=(_,_)=>{if(Commit()){saved=true;window.Close();}};
        window.Closed+=(_,_)=>{if(!saved)card.RestoreProfile(original);};
        Reflect();return window;
    }
}

