# Katana Reforged

Reloaded-II mod for **FINAL FANTASY TACTICS - The Ivalice Chronicles**.

Goal: whenever a katana breaks in battle, make that katana become available in the Poach Store.

## Project Layout

```text
src/fftivc.katanareforged/
  fftivc.katanareforged.csproj
  ModConfig.json
  Program.cs
  ModBase.cs
  ModContext.cs
  KatanaProbeMod.cs
  UnitSnapshot.cs
  ActionProbeState.cs
```

## Local Build

Deploy directly to Reloaded-II:

```powershell
$env:RELOADEDIIMODS='C:\Reloaded-II\Mods'
dotnet build .\src\fftivc.katanareforged\fftivc.katanareforged.csproj -c Release
```

Output:

```text
C:\Reloaded-II\Mods\ffttic.katanareforged
```

## Notes

The current build is a runtime probe plus guarded first implementation. It logs:

- Iaido/katana action candidates (`76-85`);
- battle unit HP/MP/CT changes and monster KO candidates;
- `ProcessSpoils` entry/exit;
- Poach Store carcass counter deltas;
- Poach NEX table `0xEC` rows when the table functions are available.

On a confirmed katana shatter mask, it also rewrites a reserved PoachItem row to point at the
shattered katana and increments that Poach Store counter. See `work/` for current validation notes.

Runtime log:

```text
katana_probe_log.txt
```

The log is written next to the deployed mod DLL.
