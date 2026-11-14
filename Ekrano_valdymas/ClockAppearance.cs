using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

public sealed class ClockAppearance
{
    public string FontFamily {get;set;}="Teko";
    public bool Bold {get;set;}=true;
    public bool Italic {get;set;}
    public double Size {get;set;}=77.33;
    public string Color {get;set;}="";
    public double EffectStrength {get;set;}
    public int Cells {get;set;}=2;
    public bool Shadow {get;set;}=true;
    public bool Skin {get;set;}
    public string? MaterialStyle {get;set;}
    internal string EffectiveMaterial=>IconAppearance.Normalize(MaterialStyle??(Skin?"metal":"original"));
    internal int Span=>Cells==1?1:2;
    internal System.Windows.Media.Effects.Effect? ShadowEffect=>Shadow?new System.Windows.Media.Effects.DropShadowEffect{Color=Colors.Black,BlurRadius=8,ShadowDepth=4,Direction=270,Opacity=1}:null;
    internal ClockAppearance Clone()=>JsonSerializer.Deserialize<ClockAppearance>(JsonSerializer.Serialize(this))!;
    internal FontFamily Face=>FontEditor.Family(FontFamily);
    internal FontWeight Weight=>Bold?FontWeights.Bold:FontWeights.Normal;
    internal FontStyle Style=>Italic?FontStyles.Italic:FontStyles.Normal;
    internal Brush Brush
    {
        get
        {
            if(!string.IsNullOrWhiteSpace(Color))try{return new SolidColorBrush((Color)ColorConverter.ConvertFromString(Color));}catch{}
            return (Brush)Application.Current.FindResource("ClockBrush");
        }
    }
    internal double SafeSize=>double.IsFinite(Size)?Math.Clamp(Size,18,110):77.33;
    internal double CellSize=>SafeSize;
    internal void Apply(OpticallyCenteredText text,double scale=1,bool fitCell=true)
    {
        double size=(fitCell?CellSize:SafeSize)*scale;
        text.TextFontFamily=Face;text.TextFontWeight=Weight;text.TextFontStyle=Style;text.TextFontSize=size;text.LineAdvance=size*1.04;
        text.TextBrush=Brush;text.InsetMetalText=EffectStrength>0;text.InsetStrength=EffectStrength;text.InsetOnBrightMetal=false;text.ClipToBounds=false;
        EcoEffects.SetEffect(text,EffectiveMaterial is "metal" or "inlay"?null:ShadowEffect);
        text.SkinMetalText=EffectiveMaterial!="original";text.SkinTextStyle=EffectiveMaterial;
        text.FitToBox=true;
    }
}

internal static class ClockEditor
{
    internal static SettingsWindow Create(Window? owner,ClockAppearance original,Action<ClockAppearance> preview,Action<ClockAppearance> save,double defaultSize=77.33,Action<string>? saveDefault=null)
    {
        var dialog=AppearanceEditorUi.Window("Edit clock",owner,740);
        dialog.Resources[typeof(CheckBox)]=Application.Current.FindResource("DashboardCheckBox");
        var root=new DockPanel();dialog.Content=root;
        var footer=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(18,4,18,12)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var panel=new StackPanel{Margin=new Thickness(18,0,18,6)};root.Children.Add(MacroUi.Scroll(panel));
        var cells=AppearanceEditorUi.Choice(original.Span.ToString(),("1","One cell"),("2","Two cells"));EditorUi.Field(panel,"Clock width",cells);
        var material=AppearanceEditorUi.Choice(original.EffectiveMaterial,("original","Original"),("mono","Monochrome"),("metal","Metal"),("inlay","Inlay"));
        material.Name="ClockStyleChoice";var styleRow=new DockPanel();bool useDefault=false;Button? allTabsButton=null;
        if(saveDefault!=null){var defaultButton=MacroUi.Button("Use for all tabs",(sender,_)=>{useDefault=true;if(sender is Button button)button.Content="All tabs ✓";});allTabsButton=defaultButton;defaultButton.Name="ClockStyleDefault";defaultButton.ToolTip="Use this style for all tabs when saved. You can change each tab separately afterwards.";DockPanel.SetDock(defaultButton,Dock.Right);styleRow.Children.Add(defaultButton);}
        styleRow.Children.Add(material);EditorUi.Field(panel,"Clock style",styleRow);
        var font=new FontEditor(panel,original.FontFamily,original.Bold,original.Italic,original.EffectStrength);
        var size=new Slider{Minimum=18,Maximum=110,Value=original.SafeSize,IsSnapToTickEnabled=false};SliderDefaults.Mark(size,defaultSize);EditorUi.Field(panel,"Text size",size);
        var color=new TextBox{Name="ClockColorHex",Text=original.Color,VerticalContentAlignment=VerticalAlignment.Center};
        var colorRow=new DockPanel();var selectColor=MacroUi.Button("Select color…",(_,_)=>
        {
            string previous=color.Text;Color initial=((SolidColorBrush)original.Brush).Color;
            try{if(!string.IsNullOrWhiteSpace(previous))initial=(Color)ColorConverter.ConvertFromString(previous);}catch{}
            var picker=new AccentColorDialog(initial,initial,initial,initial,initial,100,100,100,100,100){Owner=dialog,ShowInTaskbar=false};picker.ConfigureClockOnly();
            picker.PreviewChanged+=(_,value,_)=>color.Text=$"#{value.R:X2}{value.G:X2}{value.B:X2}";
            if(picker.ShowDialog()==true){var value=picker.ClockColor;color.Text=$"#{value.R:X2}{value.G:X2}{value.B:X2}";}else color.Text=previous;
        });selectColor.Name="SelectClockColor";DockPanel.SetDock(selectColor,Dock.Right);colorRow.Children.Add(selectColor);
        var copyColor=CopyFeedback.Create(()=>color.Text,copied=>{if(copied)color.BorderBrush=new SolidColorBrush(Color.FromRgb(59,107,245));else color.ClearValue(Control.BorderBrushProperty);});copyColor.Name="CopyColorCode";DockPanel.SetDock(copyColor,Dock.Right);colorRow.Children.Add(copyColor);
        copyColor.Height=double.NaN;copyColor.Margin=selectColor.Margin;copyColor.VerticalAlignment=VerticalAlignment.Stretch;
        colorRow.Children.Add(color);EditorUi.Field(panel,"Color (#RRGGBB; blank follows clock theme)",colorRow);
        var shadow=new CheckBox{Content="Text shadow",Foreground=Brushes.White,IsChecked=original.Shadow,Margin=new Thickness(4,10,4,4)};panel.Children.Add(shadow);
        var error=AppearanceEditorUi.Help(panel,"");var draft=original.Clone();bool committed=false;
        bool Update()
        {
            try
            {
                var next=new ClockAppearance{Skin=original.Skin,MaterialStyle=AppearanceEditorUi.Selected(material),Cells=AppearanceEditorUi.Selected(cells)=="1"?1:2,Shadow=shadow.IsChecked==true,FontFamily=font.SelectedFamily,Bold=font.Bold,Italic=font.Italic,EffectStrength=font.Strength,Size=size.Value,Color=color.Text.Trim()};
                if(next.Color.Length>0)_=(Color)ColorConverter.ConvertFromString(next.Color);draft=next;preview(draft);error.Text="";return true;
            }
            catch{error.Text="Enter a color such as #FFFFFF, or leave it blank.";return false;}
        }
        font.Changed+=()=>Update();size.ValueChanged+=(_,_)=>Update();color.TextChanged+=(_,_)=>Update();
        material.SelectionChanged+=(_,_)=>Update();
        cells.SelectionChanged+=(_,_)=>Update();shadow.Checked+=(_,_)=>Update();shadow.Unchecked+=(_,_)=>Update();
        footer.Children.Add(MacroUi.Button("Default",(_,_)=>{useDefault=false;if(allTabsButton!=null)allTabsButton.Content="Use for all tabs";font.Reset("Teko",true,false,0);size.Value=defaultSize;color.Text="";shadow.IsChecked=true;material.SelectedIndex=0;Update();}));
        footer.Children.Add(MacroUi.Button("Save",(_,_)=>{if(!Update())return;try{save(draft.Clone());if(useDefault)saveDefault?.Invoke(draft.EffectiveMaterial);committed=true;dialog.DialogResult=true;}catch(Exception ex){error.Text="Could not save: "+ex.Message;}}));
        footer.Children.Add(MacroUi.Button("Cancel",(_,_)=>dialog.Close()));
        dialog.Closed+=(_,_)=>{if(!committed)preview(original);};AppearanceEditorUi.Frame(dialog);
        // Frame compacts text buttons; match its final margins and height for
        // the adjacent icon button too, rather than its pre-frame defaults.
        copyColor.Margin=selectColor.Margin;copyColor.MinHeight=selectColor.MinHeight;
        return dialog;
    }
}
