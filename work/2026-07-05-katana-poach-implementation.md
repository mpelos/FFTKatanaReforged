# Katana Shatter to Poach Store Implementation - 2026-07-05

This is an implementation journal. It is not canonical modding documentation.

## Implemented Path

- `ActionProbeState.HasShatterMask` remains the detection gate:

```text
(unit + 0x1D0 u32) & 0x1000
```

- The mod now acts only on the rising edge of that mask:

```text
previous shatter mask != true
current shatter mask == true
```

- The staged item id at `+0x1A8` must match the katana action mapping. Mismatch skips the write and
  logs `[KATANA-POACH-SKIP]`.

- On a guarded shatter event, the mod:
  - patches one reserved `PoachItem` row;
  - writes `Cost` at `PoachItem +0x24`;
  - writes `SellPrice` at `PoachItem +0x28`;
  - writes the sold item id at `PoachItem +0x2C`;
  - writes the rare byte at `PoachItem +0x34`;
  - attempts to overwrite existing relative strings at `+0x8/+0xC/+0x10/+0x14/+0x18` with the
    katana name, only when the existing string buffer is long enough;
  - increments `CarcassCounts[poachId]`, saturated at `99`.

## Reserved PoachItem Slots

These slots are high-end monster poach rows in vanilla. This is a first implementation path for live
validation, not the final data-design decision.

| Katana item id | Katana | PoachItem id |
| ---: | --- | ---: |
| 38 | Ashura | 87 |
| 39 | Kotetsu | 88 |
| 40 | Bizen Osafune | 89 |
| 41 | Murasame | 90 |
| 42 | Ame-no-Murakumo | 96 |
| 43 | Kiyomori | 91 |
| 44 | Muramasa | 92 |
| 45 | Kiku-ichimonji | 93 |
| 46 | Masamune | 95 |
| 47 | Chirijiraden | 94 |

## Expected Log Lines

Successful flow should produce:

```text
[KATANA-SHATTER] ...
[KATANA-POACH-ADD] ... itemId=<katana id> ... poachId=<reserved slot> count=0->1 ...
[POACH-DELTA poll] poachId=<reserved slot> ... itemId=<katana id> ...
```

Failure modes should log:

```text
[KATANA-POACH-SKIP] reason=item-mismatch ...
[KATANA-POACH-SKIP] reason=carcass-counts-unavailable ...
[KATANA-POACH-SKIP] reason=row-patch-failed ...
```

## Needs Live Validation

- Confirm the patched row appears in the Poach Store list after battle.
- Confirm the displayed row name is the katana name in the active UI language.
- Confirm buying the patched row gives the katana item id, not the original monster reward.
- Confirm the saved post-battle store state does not duplicate entries after repeated polling.
