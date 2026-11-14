namespace Ekrano_valdymas;

// Preserve the persisted Game value; retired Eco (1) migrates to Normal.
internal enum ResourceMode { Normal = 0, Game = 2 }

// Shared policy state; presentation changes never imply suspension of commands
// or music playback. Observers must detach with their owning control/window.
internal static class ResourcePolicy
{
    internal static ResourceMode Mode { get; private set; }
    internal static bool IsSaving => Mode != ResourceMode.Normal;
    internal static bool IsGame => Mode == ResourceMode.Game;
    internal static event Action? Changed;
    internal static void SetMode(ResourceMode mode)
    {
        if(Mode==mode){ResourceModeDiagnostics.Record(mode);return;}
        Mode=mode;
        ResourceModeDiagnostics.Record(mode);
        Changed?.Invoke();
    }
    internal static TimeSpan PassiveInterval => Mode switch
    {
        ResourceMode.Game => TimeSpan.FromSeconds(5),
        _ => TimeSpan.FromSeconds(1)
    };
}



