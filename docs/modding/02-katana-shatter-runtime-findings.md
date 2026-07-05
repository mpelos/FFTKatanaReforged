# Katana Shatter Runtime Findings

Date: 2026-07-05

This note records confirmed live probe results for katana shatter during Iaido actions.
Investigation notes, hypotheses, and implementation plans are kept under `work/`.

## Test Context

Probe build:

- `ffttic.katanareforged`
- read-only runtime probe
- log file observed at the game directory:

```text
D:\SteamLibrary\steamapps\common\FINAL FANTASY TACTICS - The Ivalice Chronicles\katana_probe_log.txt
```

The active Reloaded-II module was confirmed in-process as:

```text
C:\Reloaded-II\Mods\ffttic.katanareforged\ffttic.katanareforged.dll
```

The shatter observations below come from probe log lines and direct post-event process memory reads
while the battle was still active.

## Confirmed Shatter Captures

The Samurai/Iaido actor in the confirmed captures was:

```text
ptr=0x141855CE0 id=0x32 job=0x58 team=0
```

| Capture | Katana | Action id | Katana item id | `+0x1C0` | `+0x1D0` u32 | `+0x1E5` | Poach counters |
| --- | --- | ---: | ---: | ---: | --- | ---: | --- |
| `13:30:46` direct read | Masamune | 84 | 46 | 8 | `0x00001000` | 0 | unchanged |
| `13:37:09` probe + direct read | Masamune | 84 | 46 | 8 | `0x00001000` | 0 | unchanged |
| later direct read | Chirijiraden | 85 | 47 | 8 | `0x00001000` | 0 | unchanged |

Representative probe line from the confirmed Masamune capture:

```text
[KATANA-SHATTER-SUSPECT] ability=Masamune itemId=46
reason=mask1D0-bit0x1000 act=84 ail1A8=46 kind1C0=8 mask1D0=0x00001000 f1E5=0
```

Direct memory read after the confirmed Chirijiraden break:

```text
ptr=0x141855CE0 id=0x32 job=0x58 team=0 hp=437/439 mp=0/96 ct=0 equipRW=256
+0x1A2=85 +0x1A8=47 +0x1C0=8 +0x1D0 u32=0x00001000 +0x1E5=0
+0x1B8/+0x1BA/+0x1BB=1/1/1
poach counters nonzero=none
```

## Runtime Fields

The confirmed shatter captures show the broken katana action state on the acting unit.

| Offset | Size | Observed value | Meaning in these captures |
| ---: | --- | --- | --- |
| `+0x1A2` | u16 | `84`, `85` | Iaido action id |
| `+0x1A8` | u16 | `46`, `47` | Katana item id for the Iaido action |
| `+0x1C0` | u8 | `8` | Result-state value present on shatter captures |
| `+0x1D0` | u32 | `0x00001000` | Shatter result mask observed on confirmed breaks |
| `+0x1E5` | u8 | `0` | Was zero on confirmed shatter captures |
| `+0x1B8/+0x1BA/+0x1BB` | u8/u8/u8 | `1/1/1` | Retained post-event action markers in confirmed reads |

Nearby normal Masamune result phases were observed with:

```text
+0x1D0 u32 = 0x00000008
```

Confirmed shatter captures were observed with:

```text
+0x1D0 u32 = 0x00001000
```

## Iaido Mapping Confirmed By Shatter Captures

| Action id | Katana item id | Name |
| ---: | ---: | --- |
| 84 | 46 | Masamune |
| 85 | 47 | Chirijiraden |

## Poach Store Interaction

No Poach Store counter delta was observed automatically during the confirmed katana shatter
captures.

The Poach Store counter array was checked after the confirmed direct reads and remained:

```text
poach counters nonzero=none
```

## Negative Findings

`+0x1E5 = 0x10` is not a confirmed shatter signal. It appeared in earlier action timing markers
before a user-reported break, and it was `0` in the confirmed `+0x1D0 = 0x00001000` shatter
captures.

The actor equipment block is not the consumed-katana surface in these captures. The Samurai unit's
right-hand equipment value remained:

```text
equipRW=256
```

while `+0x1A8` identified the consumed Iaido katana item ids `46` and `47`.

## Current Limits

The `+0x1D0` shatter mask has been confirmed for Masamune and Chirijiraden only.

The captures confirm that the game does not automatically add a shattered katana to the observed
Poach Store counter array. The write path for adding a shattered katana to the Poach Store is not
documented here.
