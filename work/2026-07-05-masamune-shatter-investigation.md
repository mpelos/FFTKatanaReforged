# Katana Shatter Investigation - 2026-07-05

This is a working investigation note, not canonical modding documentation. Move only confirmed,
stable findings to `docs/modding`.

## Live Context

- Game log: `D:\SteamLibrary\steamapps\common\FINAL FANTASY TACTICS - The Ivalice Chronicles\katana_probe_log.txt`
- The running process still logged as `fftivc.katanareforged`.
- Process module inspection confirmed that the game had loaded
  `C:\Reloaded-II\Mods\fftivc.katanareforged\fftivc.katanareforged.dll`.
- The enhanced `ffttic.katanareforged` probe had been built, but it was not active in this live
  session.
- User reported that a Masamune had just broken during the live test.
- In the follow-up live session, process module inspection confirmed the active DLL was
  `C:\Reloaded-II\Mods\ffttic.katanareforged\ffttic.katanareforged.dll`.
- User reported another Masamune break in that `ffttic` session.
- User later reported another break; a direct memory read identified the retained action state as
  Chirijiraden rather than Masamune.

## Observed Facts

- The probe captured repeated Iaido `Masamune` actions.
- The Samurai/Iaido actor was `ptr=0x141855CE0`, `job=0x58`, `team=0`.
- `[CALC] type=0x13 abilityId=84 katanaAbility=Masamune itemId=46` appeared for the Masamune actions.
- `[KATANA-ACTION]` captured the action lifecycle.
- In the 13:19 session, the reported break aligned with:
  - `13:19:56` `[CALC] type=0x13 abilityId=84 katanaAbility=Masamune itemId=46`.
  - `13:20:02` `[KATANA-ACTION] ... act=84 ... f1E5=16 ... bb=2`.
  - `13:20:02` `[HPMP] ... mp=96/96 prevMp=95`.
  - `13:20:04` `[KATANA-ACTION] ... act=84 ... cred1C6=54 ... f1E5=64`.
- No explicit `Broken`, `break`, or `shatter` text appeared in the current probe log.
- No enhanced `[KATANA-BOUNDARY]`, `[KATANA-RAW]`, or `[KATANA-SHATTER-SUSPECT]` lines appeared,
  because the active DLL was still the old `fftivc` build.
- No Poach Store counter delta was observed for the Masamune event in the current probe log.
- After the reported break, a direct memory read still showed `+0x1A2 act = 84`.
- After the reported break, the same direct memory read showed `+0x1A8 = 46`, matching the
  Masamune item id from the Iaido mapping.
- Direct post-break memory read of the Samurai unit:

```text
ptr=0x141855CE0 id=0x32 job=0x58 team=0 hp=439/439 mp=96/96 ct=10
equip head=155 body=183 acc=214 rWeapon=256 rShield=255 lWeapon=255 lShield=255
+0x18D=255 +0x1A0=16 +0x1A1=19 +0x1A2=84 +0x1A8=46
+0x1B8/+0x1BA/+0x1BB=1/1/1 +0x1BE=1 +0x1C0=8
+0x1C4=0 +0x1C6=0 +0x1C8=0 +0x1CA=0 +0x1D0=0 +0x1D8=0
+0x1E5=0 +0x1EF=0 +0x1F5=255
poach counters nonzero=none
```

- In the `ffttic` session, normal Masamune result phases logged the same raw non-zero offsets as
  the break phase except for the `+0x1D0` dword:
  - Normal observed result phase examples: `+0x1D0 u32 = 0x00000008`.
  - User-reported break at `13:30:46`: `+0x1D0 u32 = 0x00001000`.
- A direct memory read immediately after the `13:30:46` user-reported break confirmed:

```text
ptr=0x141855CE0 id=0x32 job=0x58 team=0 hp=439/439 mp=96/96 ct=10
+0x1A2=84 +0x1A8=46 +0x1C0=8 +0x1D0 byte=0x00 +0x1D0 u16=0x1000 +0x1D0 u32=0x00001000
+0x1E5=0 +0x1B8/+0x1BA/+0x1BB=1/1/1
poach counters nonzero=none
```
- The `ffttic` break capture did not show `+0x1E5 = 0x10`; that earlier suspect is now weaker than
  the `+0x1D0 u32 = 0x00001000` candidate.
- In the next `ffttic` session, the user reported another Masamune break. The probe emitted:

```text
13:37:09.528 [KATANA-SHATTER-SUSPECT] ability=Masamune itemId=46
reason=mask1D0-bit0x1000 act=84 ail1A8=46 kind1C0=8 mask1D0=0x00001000 f1E5=0
```

- A direct memory read immediately after the `13:37:09` report confirmed the same retained state:

```text
ptr=0x141855CE0 id=0x32 job=0x58 team=0 hp=441/441 mp=96/97 ct=10
+0x1A2=84 +0x1A8=46 +0x1C0=8 +0x1D0 u32=0x00001000 +0x1E5=0
+0x1B8/+0x1BA/+0x1BB=1/1/1
poach counters nonzero=none
```

- A later direct memory read after another user-reported break showed the same shatter mask with
  Chirijiraden:

```text
ptr=0x141855CE0 id=0x32 job=0x58 team=0 hp=437/439 mp=0/96 ct=0 equipRW=256
+0x1A2=85 +0x1A8=47 +0x1C0=8 +0x1D0 u32=0x00001000 +0x1E5=0
+0x1B8/+0x1BA/+0x1BB=1/1/1
poach counters nonzero=none
```

- The same session also produced `f1E5-bit0x10` markers before the user-reported break, so
  `f1E5=0x10` is noisy and should not be treated as the main shatter signal.
- The post-event pending/action state read:

```text
+0x18D pending timer = 255
+0x1E5 result flag = 0
+0x1EF pending/status master = 0
+0x1B8/+0x1BA/+0x1BB = 1/1/1
```

- The unit equipment block showed right weapon `256`, so the consumed Masamune was not visible as
  the unit's equipped right-hand weapon.
- GenericChronicle documents that `+0x1A2` can remain as historical last-action state after
  resolution; therefore waiting for `act` to clear is not a valid shatter-resolution signal.

## Hypotheses / Suspects

- `f1E5=16` appeared in earlier reported-break-adjacent sequences, but later emitted before a
  user-reported break. Treat it as an auxiliary timing marker only.
- `+0x1D0` interpreted as a 32-bit little-endian mask is now the strongest shatter suspect. In the
  `13:30:46`, `13:37:09`, and later Chirijiraden captures, user-reported breaks had bit `0x1000`
  set while nearby normal Masamune result phases used `0x00000008`.
- `+0x1A8` is a strong candidate for the action's katana item-id surface. It matched Masamune
  (`46`) and Chirijiraden (`47`) in post-break retained action state.
- Iaido likely consumes inventory stock rather than the actor's equipment slot, so equipment alone
  is the wrong confirmation surface.

## Next Probe Changes

- Log broader katana action-boundary state:
  `+0x1A0/+0x1A1/+0x1A2/+0x1A8/+0x1BE/+0x1C0/+0x1C4/+0x1C6/+0x1C8/+0x1CA/+0x1D0/+0x1D8/+0x1E5/+0x1EF/+0x1B8/+0x1BA/+0x1BB`.
- Emit `[KATANA-BOUNDARY]` diffs for katana action state changes.
- Emit `[KATANA-RAW]` for `0x180-0x1FF` around state transitions.
- Emit `[KATANA-SHATTER-SUSPECT]` only when `+0x1D0` as a u32 contains bit `0x1000`.
- Emit `+0x1E5 & 0x10` as an auxiliary marker, not as `KATANA-SHATTER-SUSPECT`.
- Find or infer an inventory count surface for item ids `38-47`.

## Probe Changes After 13:30 Capture

- Source updated so `ActionProbeState.ApplyMask` reads `+0x1D0` as a 32-bit value.
- Boundary summaries now render `mask1D0` as hex, e.g. `0x00001000`.
- `[KATANA-SHATTER-SUSPECT]` now fires on `mask1D0-bit0x1000`.
- Source was then adjusted so `+0x1E5 & 0x10` logs as `[KATANA-F1E5-MARKER]` instead of
  `[KATANA-SHATTER-SUSPECT]`.
- Build validation for that adjustment succeeded to
  `D:\Projects\FFTKatanaReforged\artifacts\verify-build\ffttic.katanareforged`.
- Build validation succeeded to `D:\Projects\FFTKatanaReforged\artifacts\verify-build\ffttic.katanareforged`.
- Deploy to `C:\Reloaded-II\Mods\ffttic.katanareforged` could not complete while the game process
  held the loaded DLL open.
- After the game was closed, the normal Release build deployed successfully to
  `C:\Reloaded-II\Mods\ffttic.katanareforged\ffttic.katanareforged.dll` at `2026-07-05 13:33:04`.

## Iaido Mapping Under Test

| Action id | Katana item id | Name |
| ---: | ---: | --- |
| 76 | 38 | Ashura |
| 77 | 39 | Kotetsu |
| 78 | 40 | Bizen Osafune |
| 79 | 41 | Murasame |
| 80 | 42 | Ame-no-Murakumo |
| 81 | 43 | Kiyomori |
| 82 | 44 | Muramasa |
| 83 | 45 | Kiku-ichimonji |
| 84 | 46 | Masamune |
| 85 | 47 | Chirijiraden |
