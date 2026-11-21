using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ekrano_valdymas;

internal sealed class ChangesView : StackPanel
{
    private static Style EntryStyle(bool group=false)
    {
    string source="""
    <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Expander">
      <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Expander">
        <StackPanel>
          <ToggleButton MinHeight="44" HorizontalContentAlignment="Stretch" Foreground="White"
              IsChecked="{Binding IsExpanded,Mode=TwoWay,RelativeSource={RelativeSource TemplatedParent}}"
              Content="{TemplateBinding Header}">
            <ToggleButton.Template><ControlTemplate TargetType="ToggleButton">
              <Border x:Name="Surface" Background="#18FFFFFF" CornerRadius="5" Padding="8,5">
                <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="24"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                  <Path x:Name="Arrow" Data="M1,1 L8,8 L15,1" Width="14" Height="8" Stroke="White" StrokeThickness="2" HorizontalAlignment="Left" VerticalAlignment="Center" RenderTransformOrigin="0.5,0.5"><Path.RenderTransform><RotateTransform Angle="-90"/></Path.RenderTransform></Path>
                  <TextBlock Grid.Column="1" Text="{TemplateBinding Content}" TextWrapping="Wrap" VerticalAlignment="Center"/>
                </Grid>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsChecked" Value="True"><Setter TargetName="Arrow" Property="RenderTransform"><Setter.Value><RotateTransform Angle="0"/></Setter.Value></Setter></Trigger>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Background" Value="#28FFFFFF"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></ToggleButton.Template>
          </ToggleButton>
          <ContentPresenter x:Name="Body" ContentSource="Content" Visibility="Collapsed"/>
        </StackPanel>
        <ControlTemplate.Triggers><Trigger Property="IsExpanded" Value="True"><Setter TargetName="Body" Property="Visibility" Value="Visible"/></Trigger></ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    """;
    if(group)source=source.Replace("<ColumnDefinition Width=\"24\"/><ColumnDefinition Width=\"*\"/>","<ColumnDefinition Width=\"Auto\"/><ColumnDefinition Width=\"24\"/><ColumnDefinition Width=\"*\"/>")
        .Replace("<Path x:Name=\"Arrow\"","<Path Grid.Column=\"1\" x:Name=\"Arrow\"")
        .Replace("<TextBlock Grid.Column=\"1\"","<TextBlock Grid.Column=\"0\" Margin=\"0,0,12,0\"");
    return (Style)System.Windows.Markup.XamlReader.Parse(source);
    }
    private sealed record Entry(string Version, string Date, string En, string Lt, string DetailEn, string DetailLt);
    private static readonly Entry[] Entries =
    [
        new("1.0.7","2026-11-21","Settings backups","Nustatymų kopijos",
            "Export your tabs, layouts, macros, shortcuts and artwork from Settings. Import the backup after reinstalling BlakiDeck.",
            "Nustatymuose galite išsaugoti tabus, išdėstymus, makrosus, nuorodas ir jų vaizdus. Perinstaliavę programą importuokite kopiją."),
        new("1.0.7","2026-11-21","OBS replay controls","OBS pakartojimų valdymas",
            "See the replay buffer duration and open the folder containing the last saved replay.","Matykite nustatytą buferio trukmę ir atidarykite paskutinio išsaugoto pakartojimo aplanką."),
        new("1.0.7","2026-11-21","Web thumbnails","Nuorodų miniatiūros",
            "Two rows fit in the module. Scrolling starts at five tiles, including the add button.","Modulyje telpa dvi eilės. Slinkties juosta atsiranda nuo penkto langelio, įskaitant pridėjimo mygtuką."),
        new("1.0.7","2026-11-21","System Monitor views","Sistemos monitoriaus vaizdai",
            "CPU core bars move smoothly. Only the first view is populated by default.","Procesoriaus branduolių stulpeliai juda tolygiai. Pagal numatytuosius nustatymus užpildytas tik pirmas vaizdas."),
        new("1.0.7","2026-11-21","Tabs and dashboard controls","Tabai ir skydelio valdymas",
            "Tabs prepare in the background, the dashboard expands smoothly and the Media module can be used to drag the window. Fit monitor closes its menu.",
            "Tabai paruošiami fone, skydelis sklandžiai išsiskleidžia, o langą galima pertempti už medijos modulio. Pritaikius langą monitoriui meniu užsidaro."),
        new("1.0.7","2026-11-21","Icons and lighting","Ikonos ir apšvietimas",
            "Choose Flat icons in the gallery. Tab icons are larger, clock digits have complete edges, and VU lighting and standby player buttons have been adjusted.",
            "Galerijoje galima rinktis „Flat“ ikonas. Padidintos tabų ikonos, sutvarkyti laikrodžio skaitmenų kraštai, pakoreguotas VU ir grotuvo mygtukų apšvietimas."),
        new("1.0.7","2026-11-21","Microphone channels","Mikrofono kanalai",
            "The microphone display shows the Windows input channel count.","Mikrofono rodmuo rodo „Windows“ įvesties kanalų skaičių."),
        new("1.0.6", "2026-11-16", "Media module", "Medijos modulis",
            "Choose or drop an image or video, optionally loop video playback, and adjust the module background transparency. Settings appear on hover or touch.",
            "Pasirinkite arba nutempkite paveikslėlį ar vaizdo įrašą, pasirinkite jo kartojimą ir modulio fono permatomumą. Nustatymai pasirodo užvedus pelę arba palietus."),
        new("1.0.6", "2026-11-16", "Click feedback", "Paspaudimo grįžtamasis ryšys",
            "Tabs and shortcuts respond visually to hovering and pressing.",
            "Tabai ir nuorodos vizualiai reaguoja užvedus pelę ir paspaudus."),
        new("1.0.6", "2026-11-16", "Application updates", "Programos atnaujinimai",
            "Check for updates in the application and download a verified installer from GitHub. Installation starts when you choose to update.",
            "Programoje galima patikrinti atnaujinimus ir atsisiųsti patikrintą diegiklį iš GitHub. Diegimas prasideda pasirinkus atnaujinti."),
        new("1.0.2", "2026-11-14", "Original web shortcut colors", "Originalios Web nuorodų spalvos",
            "Web shortcuts use their original icon colors by default. Each shortcut can have its own icon effect.",
            "Web nuorodų ikonos pagal nutylėjimą rodomos originalių spalvų. Kiekvienai nuorodai galima pasirinkti atskirą ikonos efektą."),
        new("1.0.2", "2026-11-14", "Weather temperature precision", "Temperatūra su kableliu",
            "Added an optional one-decimal setting for current, feels-like and forecast temperatures. The decimal separator follows the weather language.",
            "Orų nustatymuose galima pasirinkti vieną skaičių po kablelio dabartinei, jutiminei ir prognozuojamai temperatūrai. Skirtukas priklauso nuo orų kalbos."),
        new("1.0.2", "2026-11-14", "Trigger keys for all macros", "Klavišai visiems makrosams",
            "Global keyboard shortcuts can be assigned to all macro types, including microphone controls.",
            "Visų tipų makrosams, įskaitant mikrofono valdymą, galima priskirti bendrąją klavišų kombinaciją."),
        new("1.0.2", "2026-11-14", "Web shortcut context menu", "Web nuorodų meniu",
            "Fixed the right-click menu so existing web shortcuts offer Edit and Remove.",
            "Pataisytas dešiniojo pelės mygtuko meniu: esamoms Web nuorodoms rodomi redagavimo ir pašalinimo veiksmai."),
        new("1.0.2", "2026-11-14", "MIT license and source code", "MIT licencija ir šaltiniai",
            "Published the application source code and build instructions under the MIT License. Third-party components retain their own licenses.",
            "Programos šaltinio kodas ir surinkimo instrukcijos paskelbti pagal MIT licenciją. Trečiųjų šalių komponentams galioja jų licencijos."),
        new("1.0.1", "2026-11-09", "Philips Hue controls", "Philips Hue valdymas",
            "Corrected light availability handling and brightness adjustment while dragging. Unreachable lights remain distinct from lights that are switched off.",
            "Patikslintas šviestuvų pasiekiamumo apdorojimas ir ryškumo keitimas braukiant. Nepasiekiami šviestuvai atskiriami nuo išjungtų."),
        new("1.0.0", "2026-11-08", "First public release", "Pirmoji vieša versija",
            "Published the Windows x64 installer on GitHub and itch.io with dashboard layouts, music, shortcuts, macros, monitoring and optional integrations.",
            "GitHub ir itch.io paskelbtas Windows x64 diegiklis su skydelio išdėstymais, muzika, nuorodomis, makrosais, stebėjimu ir pasirenkamomis integracijomis.")
    ];

    internal ChangesView(string language) { SetLanguage(language); }
    internal void SetLanguage(string language)
    {
        bool initial=Children.Count==0,lt=language=="lt";
        var expandedGroups=Children.OfType<Expander>().Where(e=>e.IsExpanded).Select(e=>e.Tag).ToHashSet();
        var expandedEntries=Children.OfType<Expander>().SelectMany(e=>((StackPanel)e.Content).Children.OfType<Expander>()).Where(e=>e.IsExpanded).Select(e=>e.Tag).ToHashSet();
        Children.Clear();var style=EntryStyle();var groupStyle=EntryStyle(true);
        foreach(var group in Entries.GroupBy(e=>e.Version))
        {
            var list=new StackPanel {Margin=new Thickness(8,4,0,10)};
            foreach(var entry in group)
            {
                var detail=new TextBox {Text=lt?entry.DetailLt:entry.DetailEn,IsReadOnly=true,
                    TextWrapping=TextWrapping.Wrap,BorderThickness=new Thickness(0),Background=Brushes.Transparent,
                    Foreground=Brushes.White,Padding=new Thickness(0),Margin=new Thickness(28,6,8,12),
                    VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
                list.Children.Add(new Expander {Tag=entry,Header=lt?entry.Lt:entry.En,Content=detail,
                    Style=style,FontSize=16,Foreground=Brushes.White,Margin=new Thickness(0,2,0,4),IsExpanded=expandedEntries.Contains(entry)});
            }
            Children.Add(new Expander {Name="VersionGroup",Tag=group.Key,Header=$"{group.Key} · {group.First().Date}",Content=list,
                Style=groupStyle,Foreground=Brushes.White,FontSize=18,Margin=new Thickness(0,4,0,8),
                IsExpanded=initial?group.Key==Entries[0].Version:expandedGroups.Contains(group.Key)});
        }
    }
}


