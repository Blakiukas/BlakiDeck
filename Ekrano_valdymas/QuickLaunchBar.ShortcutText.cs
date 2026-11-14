using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;
public partial class QuickLaunchBar
{
    private TabPresentation? shortcutTextPreview;
    private string? shortcutTextPreviewTarget;
    private static readonly TabPresentation DefaultShortcutText=new(){TextSize=14,Bold=true,LightEffectMode="None",DarkEffectMode="None"};
    private TabPresentation ShortcutTextFor(string? target)
    {
        if(target==null)return DefaultShortcutText;
        if(string.Equals(target,shortcutTextPreviewTarget,StringComparison.OrdinalIgnoreCase)&&shortcutTextPreview!=null)return shortcutTextPreview;
        barAppearance.ShortcutText??=new(StringComparer.OrdinalIgnoreCase);
        return barAppearance.ShortcutText.TryGetValue(target,out var saved)?saved:DefaultShortcutText;
    }
    private void RefreshShortcutText(bool metal)
    {
        foreach(var slot in _slots)slot.ConfigureText(()=>ShortcutTextFor(slot.Target),metal,MusicFinish.IsLight);
    }
    internal SettingsWindow CreateShortcutEditor(QuickLaunchSlot slot)
    {
        string originalTarget=slot.Target!;
        var draft=JsonSerializer.Deserialize<TabPresentation>(JsonSerializer.Serialize(ShortcutTextFor(originalTarget)))!;
        bool light=MusicFinish.IsLight; Action? reset=null;
        return ShortcutAppearanceEditor.Create(Window.GetWindow(this),slot.DisplayName,originalTarget,slot.Presentation??(slot.ShowName?"IconText":"Icon"),(name,target,presentation,chosenIcon)=>
        {
            var candidate=JsonSerializer.Deserialize<BarAppearance>(JsonSerializer.Serialize(barAppearance))!;
            candidate.ShortcutText[target]=draft;
            AtomicJson.Write(appearanceFile,candidate);barAppearance=candidate;
            slot.SetTarget(target,name);slot.Presentation=presentation;slot.IconPath=chosenIcon;SaveShortcuts(true);
        },preview:slot.PreviewAppearance,clearPreview:()=>
        {
            shortcutTextPreview=null;shortcutTextPreviewTarget=null;slot.ClearAppearancePreview();ApplyBarMaterial();
        },iconPath:slot.IconPath,previewIcon:slot.PreviewIcon,textEditor:panel=>
        {
            var size=new ComboBox{Name="ShortcutTextSize",ItemsSource=new double[]{10,12,14,18,22,28,32,36,42,48,60,72,96},SelectedItem=draft.TextSize};
            EditorUi.Field(panel,"Text size",size);
            var font=new FontEditor(panel,draft.FontFamily,draft.Bold,draft.Italic,draft.EffectFor(light),light?"Light skin effect (0 = off, 1 = default)":"Dark skin effect (0 = off, 1 = default)",effectMode:draft.ModeFor(light));
            AppearanceEditorUi.Help(panel,"Text settings follow this application across the top-bar shortcut cells. Light and dark skin effects are saved separately.");
            void Preview()
            {
                draft.TextSize=size.SelectedItem is double value?value:14;draft.FontFamily=font.SelectedFamily;draft.Bold=font.Bold;draft.Italic=font.Italic;
                if(light){draft.LightEffectStrength=font.Strength;draft.LightEffectMode=font.EffectMode;}else{draft.DarkEffectStrength=font.Strength;draft.DarkEffectMode=font.EffectMode;}
                shortcutTextPreviewTarget=originalTarget;shortcutTextPreview=draft;ApplyBarMaterial();
            }
            reset=()=>{draft.LightEffectMode=draft.DarkEffectMode="None";size.SelectedItem=14d;font.ResetShortcut();Preview();};
            size.SelectionChanged+=(_,_)=>Preview();font.Changed+=Preview;
        },resetText:()=>reset?.Invoke());
    }
}
