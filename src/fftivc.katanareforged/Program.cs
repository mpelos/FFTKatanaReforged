using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;

namespace fftivc.katanareforged;

/// <summary>
/// Reloaded-II entry point. Keep loader-specific plumbing here and the probe logic in KatanaProbeMod.
/// </summary>
public sealed class Program : IMod
{
    private ModBase _mod = new();

    public void StartEx(IModLoaderV1 loaderApi, IModConfigV1 modConfigV1)
    {
        var modLoader = (IModLoader)loaderApi;
        var modConfig = (IModConfig)modConfigV1;
        var logger = (ILogger)modLoader.GetLogger();

        IReloadedHooks? hooks = null;
        modLoader.GetController<IReloadedHooks>()?.TryGetTarget(out hooks);

        _mod = new KatanaProbeMod(new ModContext
        {
            Logger = logger,
            Hooks = hooks,
            ModLoader = modLoader,
            ModConfig = modConfig,
            Owner = this,
        });
    }

    public void Suspend() => _mod.Suspend();
    public void Resume() => _mod.Resume();
    public void Unload() => _mod.Unload();
    public bool CanUnload() => _mod.CanUnload();
    public bool CanSuspend() => _mod.CanSuspend();
    public Action Disposing => () => _mod.Disposing();
}
