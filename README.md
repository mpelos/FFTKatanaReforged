# Katana Reforged

Reloaded-II mod for **FINAL FANTASY TACTICS - The Ivalice Chronicles**.

Goal: whenever a katana breaks in battle, make that katana become available in the Poach Store.

## Project Layout

```text
src/fftivc.katanareforged/
  fftivc.katanareforged.csproj
  ModConfig.json
  Program.cs
```

## Local Build

Deploy directly to Reloaded-II:

```powershell
$env:RELOADEDIIMODS='C:\Reloaded-II\Mods'
dotnet build .\src\fftivc.katanareforged\fftivc.katanareforged.csproj -c Release
```

Output:

```text
C:\Reloaded-II\Mods\fftivc.katanareforged
```

## Notes

This scaffold intentionally contains no gameplay hook yet. The next step is to identify where the
game records katana breakage and where the Poach Store inventory is assembled.

