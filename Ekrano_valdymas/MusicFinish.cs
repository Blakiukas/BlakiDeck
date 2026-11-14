namespace Ekrano_valdymas;
internal static class MusicFinish {
 internal const string DefaultName="powder-black";
 internal static string ProfileDefaultName=>InstallationDefaults.IsNewInstall?DefaultName:"dark";
 internal static string Name {get;private set;}=DefaultName;
 internal static string Normalize(string? value)=>value is "dark" or "light" or "brushed-light" or "powder-black" or "rust-iron" or "carbon-fiber" or "militarity"?value:ProfileDefaultName;
 internal static bool IsNewSkin(string? value)=>value is "carbon-fiber" or "militarity";
 internal static void Set(string? value)=>Name=Normalize(value);
 internal static bool IsLightFinish(string? value)=>value is "light" or "brushed-light";
 internal static string HardwareFinish(string? value)=>IsLightFinish(value)?"light":"dark";
 internal static bool IsLight {get=>IsLightFinish(Name);set=>Set(value?"light":"dark");}
}

