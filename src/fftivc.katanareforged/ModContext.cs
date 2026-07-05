using Reloaded.Mod.Interfaces;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace fftivc.katanareforged;

public sealed class ModContext
{
    public IModLoader ModLoader { get; init; } = null!;
    public IReloadedHooks? Hooks { get; init; }
    public ILogger Logger { get; init; } = null!;
    public IModConfig ModConfig { get; init; } = null!;
    public IMod Owner { get; init; } = null!;
}
