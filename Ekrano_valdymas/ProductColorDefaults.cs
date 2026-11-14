namespace Ekrano_valdymas;

// User-approved Colors and lights palette, 2026-10-31.
internal static class ProductColorDefaults
{
    internal const string Dash="#1ED760", Clock="#FF8000", Sensors="#0253DB",
        Base="#FF5722", Text="#BDBDBD", Titles="#757575", Labels="#BDBDBD",
        Buttons="#FF9800", Knob="#FF9800", Power="#57BE40";
    internal static string Color(AccentColorDialog.ColorTarget target)=>target switch
    {
        AccentColorDialog.ColorTarget.System=>Dash, AccentColorDialog.ColorTarget.Clock=>Clock,
        AccentColorDialog.ColorTarget.SystemMonitor=>Sensors, AccentColorDialog.ColorTarget.Base=>Base,
        AccentColorDialog.ColorTarget.Text=>Text, AccentColorDialog.ColorTarget.Titles=>Titles,
        AccentColorDialog.ColorTarget.Labels=>Labels, AccentColorDialog.ColorTarget.Music=>Buttons,
        AccentColorDialog.ColorTarget.Knob=>Knob, AccentColorDialog.ColorTarget.Power=>Power, _=>Dash
    };
    internal static double Intensity(AccentColorDialog.ColorTarget target)=>target switch
    { AccentColorDialog.ColorTarget.Base=>70, AccentColorDialog.ColorTarget.Knob=>50, _=>100 };
}
