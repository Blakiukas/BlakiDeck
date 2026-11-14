using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

internal static class UsageMiniEditor
{
    internal static SettingsWindow Create(Window? owner,UsageMiniOptions original,Action<UsageMiniOptions> save,Action<UsageMiniOptions>? applyLive=null)
    {
        var dialog=AppearanceEditorUi.Window("Edit BlakiDeck Usage",owner,550);
        var root=new DockPanel();dialog.Content=root;
        var footer=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(18,4,18,12)};
        var footerHost=new DockPanel();DockPanel.SetDock(footerHost,Dock.Bottom);root.Children.Add(footerHost);DockPanel.SetDock(footer,Dock.Right);footerHost.Children.Add(footer);
        var panel=new StackPanel{Margin=new Thickness(18,4,18,8)};root.Children.Add(MacroUi.Scroll(panel));
        var state=original.Normalize();bool committed=false,refreshing=false;var sliders=new List<Slider>();
        panel.Children.Add(new TextBlock{Text="Show metrics",Margin=new Thickness(4,12,4,6)});
        var choices=new WrapPanel();panel.Children.Add(choices);
        var error=AppearanceEditorUi.Help(panel,"");error.Foreground=Brushes.OrangeRed;
        void Preview(){if(refreshing)return;applyLive?.Invoke(state.Normalize());error.Text=state.HasMetrics?"":"Select at least one metric.";}
        void Metric(string name,bool selected,Action<bool> set)
        {
            string id=name=="BD RAM"?"RAM":name=="Web RAM"?"Web":name;
            var check=new CheckBox{Content=name,Name="Usage"+id,IsChecked=selected,Margin=new Thickness(4,4,18,8)};
            check.SetBinding(Control.ForegroundProperty,new System.Windows.Data.Binding(nameof(Window.Foreground)){Source=dialog});
            choices.Children.Add(check);check.Checked+=(_,_)=>{set(true);Preview();};check.Unchecked+=(_,_)=>{set(false);Preview();};
        }
        Metric("CPU",state.Cpu,v=>state=state with{Cpu=v});Metric("GPU",state.Gpu,v=>state=state with{Gpu=v});
        Metric("BD RAM",state.Ram,v=>state=state with{Ram=v});Metric("Network",state.Network,v=>state=state with{Network=v});Metric("Web RAM",state.Web,v=>state=state with{Web=v});
        var hide=new CheckBox{Name="UsageHideTitle",Content="Hide module title",IsChecked=state.HideTitle,Margin=new Thickness(4,8,4,8)};
        hide.SetBinding(Control.ForegroundProperty,new System.Windows.Data.Binding(nameof(Window.Foreground)){Source=dialog});panel.Children.Add(hide);
        hide.Click+=(_,_)=>{state=state with{HideTitle=hide.IsChecked==true};Preview();};
        void Size(string title,string name,double value,double reference,Action<double> set)
        {
            var label=new TextBlock{Text=$"{title} — {value:0}",Margin=new Thickness(4,8,4,2)};
            var slider=new Slider{Name=name,Minimum=8,Maximum=80,Value=value};SliderDefaults.Mark(slider,reference);
            sliders.Add(slider);
            panel.Children.Add(label);panel.Children.Add(slider);
            slider.ValueChanged+=(_,_)=>{set(slider.Value);label.Text=$"{title} — {slider.Value:0}";Preview();};
        }
        Size("Heading size","UsageHeadingSize",state.TitleSize,10,v=>state=state with{TitleSize=v});
        Size("Metric title size","UsageLabelSize",state.LabelSize,16,v=>state=state with{LabelSize=v});
        Size("Value size","UsageValueSize",state.ValueSize,16,v=>state=state with{ValueSize=v});
        var defaults=MacroUi.Button("Default",(_,_)=>
        {
            refreshing=true;state=new UsageMiniOptions();
            foreach(var check in choices.Children.OfType<CheckBox>())check.IsChecked=check.Name!="UsageNetwork";
            hide.IsChecked=state.HideTitle;sliders[0].Value=state.TitleSize;sliders[1].Value=state.LabelSize;sliders[2].Value=state.ValueSize;
            refreshing=false;Preview();
        });defaults.Name="UsageDefault";defaults.Width=90;
        var defaultRow=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(18,4,0,12)};defaultRow.Children.Add(defaults);footerHost.Children.Add(defaultRow);
        footer.Children.Add(MacroUi.Button("Save",(_,_)=>
        {
            if(!state.HasMetrics){error.Text="Select at least one metric.";return;}
            try{save(state.Normalize());committed=true;dialog.DialogResult=true;}catch(Exception ex){error.Text=ex.Message;}
        }));
        footer.Children.Add(MacroUi.Button("Cancel",(_,_)=>dialog.Close()));
        dialog.Closed+=(_,_)=>{if(!committed)applyLive?.Invoke(original);};
        AppearanceEditorUi.Frame(dialog);return dialog;
    }
}

public partial class QuickLaunchBar
{
    internal SettingsWindow CreateUsageMiniEditor(QuickLaunchSlot slot)
    {
        string? target=slot.Target,presentation=slot.Presentation,icon=slot.IconPath;string name=slot.DisplayName;var original=UsageMini.Decode(target);
        void Apply(UsageMiniOptions options){slot.SetTarget(options==original?target:UsageMini.Encode(options),name);slot.Presentation=presentation;slot.IconPath=icon;}
        return UsageMiniEditor.Create(Window.GetWindow(this),original,options=>{Apply(options);SaveShortcuts();},Apply);
    }
}

public partial class ShortcutsWidget
{
    internal SettingsWindow CreateUsageMiniEditor(Slot slot)
    {
        string? target=slot.Target,presentation=slot.Presentation,icon=slot.IconPath,name=slot.CustomName;var original=UsageMini.Decode(target);
        void Apply(UsageMiniOptions options){slot.SetTarget(options==original?target:UsageMini.Encode(options),name);slot.Presentation=presentation;slot.IconPath=icon;}
        return UsageMiniEditor.Create(Window.GetWindow(this),original,options=>{Apply(options);SaveSlots();},Apply);
    }
}

