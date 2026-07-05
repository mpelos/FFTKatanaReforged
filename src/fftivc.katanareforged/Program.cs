using System;

using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;

namespace fftivc.katanareforged;

/// <summary>
/// Katana Reforged entry point.
///
/// Planned behavior: detect katanas broken in battle and make them available in the Poach Store.
/// This initial scaffold only verifies that the mod loads cleanly under Reloaded-II.
/// </summary>
public sealed class Program : IMod
{
    private ILogger _logger = null!;
    private IModLoader _modLoader = null!;
    private IModConfig _modConfig = null!;

    public void StartEx(IModLoaderV1 loaderApi, IModConfigV1 modConfigV1)
    {
        _modLoader = (IModLoader)loaderApi;
        _modConfig = (IModConfig)modConfigV1;
        _logger = (ILogger)_modLoader.GetLogger();

        Log("Katana Reforged loaded. No gameplay hooks installed yet.");
    }

    public void Suspend() { }
    public void Resume() { }
    public void Unload() { }
    public bool CanUnload() => false;
    public bool CanSuspend() => false;

    public Action? Disposing { get; }

    private void Log(string message)
    {
        _logger.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{_modConfig.ModId}] {message}");
    }
}
