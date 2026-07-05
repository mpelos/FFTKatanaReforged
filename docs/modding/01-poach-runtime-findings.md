# Poach Runtime Findings

Date: 2026-07-05

This note records the first live probe results for Katana Reforged. The test goal was to understand when the game records a successful monster poach, and whether the Poach Store counter changes during battle or only later during spoils/victory processing.

## Test Context

Probe build:

- `fftivc.katanareforged` at test time; renamed afterward to `ffttic.katanareforged`
- read-only runtime probe
- log file observed at the game directory:

```text
D:\SteamLibrary\steamapps\common\FINAL FANTASY TACTICS - The Ivalice Chronicles\katana_probe_log.txt
```

Installed hooks found in the live process:

```text
[HOOK] calc-entry rva=0x309A44 addr=0x140309A44
[HOOK] battle unit touch addr=0x140226D98
[HOOK] ProcessSpoils addr=0x1402CA00C
[FOUND] GetTable addr=0x1403D32F0
[FOUND] NexSearchRow1K addr=0x1403D3B7C
[FOUND] NexGetRowData addr=0x1403D306C
[FOUND] pNexModule ptrAddr=0x143CD9CD0
[FOUND] CarcassCounts addr=0x1411A7A1A
```

Initial Poach Store counter state:

```text
[POACH-COUNTS initial] nonZero=none
```

## Live Results

The player killed several monsters with Poach active. The probe observed KO events and immediate changes in the carcass counter array.

| Time | Monster | Job | Poach id | Counter delta | Poach item |
| --- | --- | --- | ---: | --- | --- |
| 12:54:04 | Grenade | `0x65` | 27 | `0->1` | Grenade Carcass |
| 12:56:34 | Grenade | `0x65` | 27 | `1->2` | Grenade Carcass |
| 12:56:56 | Bonesnatch | `0x6E` | 33 | `0->1` | Bonesnatch Carcass |
| 12:57:52 | Skeleton | `0x6D` | 32 | `0->1` | Skeleton Carcass+ |
| 12:58:14 | Wisenkin | `0x7F` | 49 | `0->1` | Wisenkin Carcass |

Representative log excerpt:

```text
[KO-MONSTER] ... jobName="Grenade" commonPoach=27 rarePoach=28
[POACH-DELTA poll] poachId=27 0->1 poachName="Grenade Carcass"
```

The `ProcessSpoils` hook fired after the battle ended:

```text
[PROCESS-SPOILS enter]
[PROCESS-SPOILS before] nonZero=27:2,32:1,33:1,49:1
[PROCESS-SPOILS exit]
[PROCESS-SPOILS delta] none
```

## Conclusions

The Poach Store counter changes during battle at or immediately after the monster KO/poach event. It is not deferred until `ProcessSpoils`.

`ProcessSpoils` sees the counters already updated and did not add more Poach Store inventory in this test.

The relevant runtime state is the carcass/poach counter array found by the `CarcassCounts` signature. In this run:

```text
CarcassCounts addr=0x1411A7A1A
```

Each `poachId` appears to map directly to one byte in this array:

```text
CarcassCountsAddress + poachId
```

Monster job table rows expose the normal and rare poach ids:

```text
job +0x24 = commonPoach
job +0x28 = rarePoach
```

Poach item table rows in NEX table `0xEC` expose item display data. The probe successfully resolved names and values during delta logging:

```text
poachId=27 poachName="Grenade Carcass" value=63
poachId=33 poachName="Bonesnatch Carcass" value=50
poachId=32 poachName="Skeleton Carcass+" value=50
poachId=49 poachName="Wisenkin Carcass" value=375
```
