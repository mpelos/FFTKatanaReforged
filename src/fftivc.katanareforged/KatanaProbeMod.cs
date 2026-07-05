using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.Enums;
using Reloaded.Hooks.Definitions.X64;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace fftivc.katanareforged;

internal sealed class KatanaProbeMod : ModBase
{
    private static readonly bool DiagnosticLogsEnabled = false; // Set true to re-enable probe logs.
    private const string BattleBasePtrPattern = "0F B7 41 30 66 89 42 0C";
    private const string ProcessSpoilsPattern = "48 8B C4 48 89 58 ?? 48 89 68 ?? 48 89 70 ?? 48 89 78 ?? 41 57 48 83 EC ?? 83 25 ?? ?? ?? ?? ?? 48 8D 3D ?? ?? ?? ?? 48 8B CF E8";
    private const string GetTablePattern = "45 33 C0 89 54 24 ?? 45 8B D0 4C 8B D9 49 B9 ?? ?? ?? ?? ?? ?? ?? ?? 42 0F B6 44 14 ?? 48 B9 ?? ?? ?? ?? ?? ?? ?? ?? 4C 33 C8 49 FF C2 4C 0F AF C9 49 83 FA ?? 72 ?? 49 8B 4B";
    private const string NexSearchRow1KPattern = "48 8B 41 ?? 48 85 C0 74 ?? 48 83 E8 ?? 74 ?? 48 83 F8 ?? 74 ?? 45 33 C9 45 33 C0 E9 ?? ?? ?? ?? 45 33 C0 E9 ?? ?? ?? ?? 80 79 ?? ?? 74 ?? 3B 51";
    private const string NexGetRowDataPattern = "48 8B 01 48 BA ?? ?? ?? ?? ?? ?? ?? ?? 48 23 C2 74 ?? 48 8B 49 ?? 48 83 E8 ?? 74 ?? 48 83 F8 ?? 74 ?? 48 63 41 ?? EB ?? 48 63 41 ?? EB ?? 48 63 41 ?? 48 03 C1 C3";
    private const string NexModulePointerPattern = "48 8B 1D ?? ?? ?? ?? 48 8D 15 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 0F 57 C0 0F 11 44 24";
    private const string CarcassCountsPattern = "39 D7 0F 87 ?? ?? ?? ?? 48 63 C7 48 8D 15 ?? ?? ?? ?? 80 3C 10 ?? 0F 82 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 85 C0 75 ?? 48 8B 0D ?? ?? ?? ?? 48 85 C9 74";

    private const int CalcEntryRva = 0x309A44;
    private const string CalcEntryExpectedBytes = "48 89 5C 24 18 55 56 57";
    private const int UnitTableRva = 0x1853CE0;
    private const int UnitStride = 0x200;
    private const int UnitDumpSize = 0x200;
    private const int UnitBufferCountOffset = UnitDumpSize;
    private const int UnitBufferPtrOffset = UnitBufferCountOffset + 8;
    private const int UnitBufferSize = UnitBufferPtrOffset + 8;
    private const int CalcEntryRingSlots = 64;
    private const int CalcEntryBufferSize = 16 + (CalcEntryRingSlots * 16);
    private const int MaxTrackedBattleUnits = 72;
    private const int PollIntervalMs = 20;
    private const int PoachIdCount = 96;
    private const int PoachTableDumpRetryMs = 2000;

    private static readonly int[] KatanaBoundaryOffsets =
    [
        0x30, 0x31, 0x34, 0x35, 0x41, 0x61,
        0x18C, 0x18D,
        0x1A0, 0x1A1, 0x1A2, 0x1A3, 0x1A8, 0x1A9,
        0x1B8, 0x1B9, 0x1BA, 0x1BB, 0x1BE,
        0x1C0, 0x1C4, 0x1C5, 0x1C6, 0x1C7, 0x1C8, 0x1C9, 0x1CA, 0x1CB,
        0x1D0, 0x1D8, 0x1D9, 0x1DB, 0x1DD,
        0x1E0, 0x1E5, 0x1EF, 0x1F1, 0x1F5,
    ];

    private static readonly int[] PoachNameFieldOffsets = [0x8, 0xC, 0x10, 0x14, 0x18];

    private static readonly Dictionary<int, KatanaInfo> KatanaActions = new()
    {
        [76] = new(38, "Ashura", "Ashura", 87, 1600),
        [77] = new(39, "Kotetsu", "Kotetsu", 88, 3000),
        [78] = new(40, "Bizen Osafune", "Bizen Osafune", 89, 5000),
        [79] = new(41, "Murasame", "Murasame", 90, 7000),
        [80] = new(42, "Ame-no-Murakumo", "Ame-no-Murakumo", 96, 8000),
        [81] = new(43, "Kiyomori", "Kiyomori", 91, 10000),
        [82] = new(44, "Muramasa", "Muramasa", 92, 15000),
        [83] = new(45, "Kiku-ichimonji", "Kiku-ichimonji", 93, 22000),
        [84] = new(46, "Masamune", "Masamune", 95, 10),
        [85] = new(47, "Chirijiraden", "Chirijiraden", 94, 10),
    };

    private readonly IModLoader _modLoader;
    private readonly IReloadedHooks? _hooks;
    private readonly ILogger _logger;
    private readonly IModConfig _modConfig;
    private readonly object _logGate = new();
    private readonly object _stateGate = new();
    private readonly object _poachGate = new();
    private readonly string _logPath;
    private readonly HashSet<nint> _unitRegistry = new();
    private readonly Dictionary<nint, UnitObservation> _unitObservations = new();
    private readonly Dictionary<nint, string> _lastActionState = new();
    private readonly Dictionary<int, int> _patchedPoachSlotItems = new();
    private readonly List<CalcEvent> _recentCalcEvents = new();

    private nint _moduleBase;
    private int _moduleSize;
    private nint _unitBuffer;
    private nint _calcEntryBuffer;
    private nint _nexModulePointerAddress;
    private nint _carcassCountsAddress;
    private byte[]? _lastPoachCounts;
    private int _lastUnitHookCount;
    private int _lastCalcEntryCount;
    private int _poachTableDumpAttempts;
    private long _lastPoachTableDumpAttemptTick;
    private bool _running = true;
    private bool _poachTableDumped;
    private Thread? _poller;
    private IAsmHook? _battleUnitHook;
    private IAsmHook? _calcEntryHook;
    private IHook<ProcessSpoils>? _processSpoilsHook;
    private GetTable? _getTable;
    private NexSearchRow1K? _nexSearchRow1K;
    private NexGetRowData? _nexGetRowData;

    [Function(CallingConventions.Microsoft)]
    private delegate void ProcessSpoils();

    [Function(CallingConventions.Microsoft)]
    private delegate ulong GetTable(ulong nexModule, int tableId);

    [Function(CallingConventions.Microsoft)]
    private delegate ulong NexSearchRow1K(ulong table, int key);

    [Function(CallingConventions.Microsoft)]
    private delegate ulong NexGetRowData(ulong row);

    public KatanaProbeMod()
    {
        _modLoader = null!;
        _hooks = null;
        _logger = null!;
        _modConfig = null!;
        _logPath = "";
    }

    public KatanaProbeMod(ModContext context)
    {
        _modLoader = context.ModLoader;
        _hooks = context.Hooks;
        _logger = context.Logger;
        _modConfig = context.ModConfig;
        _logPath = Path.Combine(AppContext.BaseDirectory, "katana_probe_log.txt");

        Line("==== Katana Reforged Probe ====");
        Line($"log path: {_logPath}");

        try
        {
            var module = Process.GetCurrentProcess().MainModule;
            _moduleBase = module?.BaseAddress ?? 0;
            _moduleSize = module?.ModuleMemorySize ?? 0;
            Line($"module base=0x{_moduleBase:X} size=0x{_moduleSize:X}");

            if (_hooks is null)
            {
                Line("[ERROR] IReloadedHooks unavailable.");
                return;
            }

            var scannerController = _modLoader.GetController<IStartupScanner>();
            if (scannerController is null || !scannerController.TryGetTarget(out var scanner) || scanner is null)
            {
                Line("[ERROR] IStartupScanner unavailable. Enable Reloaded.Memory.SigScan.ReloadedII.");
                return;
            }

            InstallCalcEntryProbe();
            InstallScannedHooks(scanner);
            StartPoller();
        }
        catch (Exception ex)
        {
            Line("[INIT-ERROR] " + ex);
        }
    }

    public override void Disposing()
    {
        _running = false;
        try { _poller?.Join(250); } catch { }
        try { _battleUnitHook?.Disable(); } catch { }
        try { _calcEntryHook?.Disable(); } catch { }
        try { _processSpoilsHook?.Disable(); } catch { }
        if (_unitBuffer != 0)
        {
            try { Marshal.FreeHGlobal(_unitBuffer); } catch { }
            _unitBuffer = 0;
        }
        if (_calcEntryBuffer != 0)
        {
            try { Marshal.FreeHGlobal(_calcEntryBuffer); } catch { }
            _calcEntryBuffer = 0;
        }

        Line("==== Katana Reforged Probe disposed ====");
    }

    private void InstallScannedHooks(IStartupScanner scanner)
    {
        scanner.AddMainModuleScan(BattleBasePtrPattern, result =>
        {
            if (!result.Found)
            {
                Line("[NOTFOUND] battle unit touch hook");
                return;
            }

            InstallBattleUnitHook(_moduleBase + result.Offset);
        });

        scanner.AddMainModuleScan(ProcessSpoilsPattern, result =>
        {
            if (!result.Found)
            {
                Line("[NOTFOUND] ProcessSpoils");
                return;
            }

            InstallProcessSpoilsHook(_moduleBase + result.Offset);
        });

        scanner.AddMainModuleScan(GetTablePattern, result =>
        {
            if (!result.Found)
            {
                Line("[NOTFOUND] GetTable");
                return;
            }

            var address = _moduleBase + result.Offset;
            _getTable = _hooks!.CreateWrapper<GetTable>(address, out _);
            Line($"[FOUND] GetTable addr=0x{address:X}");
            TryDumpPoachTableOnce();
        });

        scanner.AddMainModuleScan(NexSearchRow1KPattern, result =>
        {
            if (!result.Found)
            {
                Line("[NOTFOUND] NexSearchRow1K");
                return;
            }

            var address = _moduleBase + result.Offset;
            _nexSearchRow1K = _hooks!.CreateWrapper<NexSearchRow1K>(address, out _);
            Line($"[FOUND] NexSearchRow1K addr=0x{address:X}");
            TryDumpPoachTableOnce();
        });

        scanner.AddMainModuleScan(NexGetRowDataPattern, result =>
        {
            if (!result.Found)
            {
                Line("[NOTFOUND] NexGetRowData");
                return;
            }

            var address = _moduleBase + result.Offset;
            _nexGetRowData = _hooks!.CreateWrapper<NexGetRowData>(address, out _);
            Line($"[FOUND] NexGetRowData addr=0x{address:X}");
            TryDumpPoachTableOnce();
        });

        scanner.AddMainModuleScan(NexModulePointerPattern, result =>
        {
            if (!result.Found)
            {
                Line("[NOTFOUND] pNexModule");
                return;
            }

            var aobBase = _moduleBase + result.Offset;
            int rel = Marshal.ReadInt32(aobBase + 3);
            _nexModulePointerAddress = aobBase + 7 + rel;
            Line($"[FOUND] pNexModule aob=0x{aobBase:X} ptrAddr=0x{_nexModulePointerAddress:X}");
            TryDumpPoachTableOnce();
        });

        scanner.AddMainModuleScan(CarcassCountsPattern, result =>
        {
            if (!result.Found)
            {
                Line("[NOTFOUND] CarcassCounts");
                return;
            }

            var aobBase = _moduleBase + result.Offset;
            int rel = Marshal.ReadInt32(aobBase + 0xE);
            _carcassCountsAddress = aobBase + 0x12 + rel;
            Line($"[FOUND] CarcassCounts aob=0x{aobBase:X} addr=0x{_carcassCountsAddress:X}");
            lock (_poachGate)
            {
                _lastPoachCounts = SnapshotPoachCounts();
                LogPoachCounts("[POACH-COUNTS initial]", _lastPoachCounts);
            }
            TryDumpPoachTableOnce();
        });
    }

    private void InstallBattleUnitHook(nint address)
    {
        try
        {
            _unitBuffer = Marshal.AllocHGlobal(UnitBufferSize);
            Zero(_unitBuffer, UnitBufferSize);

            string buf = $"0{_unitBuffer:X}h";
            var asm = new List<string>
            {
                "use64",
                "push rax",
                "push r8",
                "pushfq",
                $"mov rax, {buf}",
                $"mov [rax+{UnitBufferPtrOffset}], rcx",
            };
            for (int offset = 0; offset < UnitDumpSize; offset += 4)
            {
                asm.Add($"mov r8d, [rcx+{offset}]");
                asm.Add($"mov [rax+{offset}], r8d");
            }
            asm.Add($"mov r8d, [rax+{UnitBufferCountOffset}]");
            asm.Add("add r8d, 1");
            asm.Add($"mov [rax+{UnitBufferCountOffset}], r8d");
            asm.AddRange(new[] { "popfq", "pop r8", "pop rax" });

            _battleUnitHook = _hooks!.CreateAsmHook(asm.ToArray(), address, AsmHookBehaviour.ExecuteFirst).Activate();
            Line($"[HOOK] battle unit touch addr=0x{address:X} buf=0x{_unitBuffer:X}");
        }
        catch (Exception ex)
        {
            Line("[HOOK-FAILED] battle unit touch " + ex);
        }
    }

    private void InstallCalcEntryProbe()
    {
        nint address = _moduleBase + CalcEntryRva;
        if (!ValidateExpectedBytes(address, CalcEntryExpectedBytes, out string byteError))
        {
            Line($"[CALC-SKIP] rva=0x{CalcEntryRva:X} {byteError}");
            return;
        }

        try
        {
            _calcEntryBuffer = Marshal.AllocHGlobal(CalcEntryBufferSize);
            Zero(_calcEntryBuffer, CalcEntryBufferSize);

            string buf = $"0{_calcEntryBuffer:X}h";
            string[] asm =
            [
                "use64",
                "push rax",
                "push rbx",
                "push rsi",
                "pushfq",
                $"mov rax, {buf}",
                "mov ebx, dword [rax]",
                "and ebx, 0x3F",
                "shl ebx, 4",
                "lea rsi, [rax + rbx + 16]",
                "mov qword [rsi], rcx",
                "mov dword [rsi+8], edx",
                "mov ebx, dword [rcx]",
                "mov dword [rsi+12], ebx",
                "add dword [rax], 1",
                "popfq",
                "pop rsi",
                "pop rbx",
                "pop rax",
            ];

            _calcEntryHook = _hooks!.CreateAsmHook(asm, address, AsmHookBehaviour.ExecuteFirst).Activate();
            Line($"[HOOK] calc-entry rva=0x{CalcEntryRva:X} addr=0x{address:X} buf=0x{_calcEntryBuffer:X}");
        }
        catch (Exception ex)
        {
            Line("[CALC-HOOK-FAILED] " + ex);
        }
    }

    private void InstallProcessSpoilsHook(nint address)
    {
        try
        {
            _processSpoilsHook = _hooks!.CreateHook<ProcessSpoils>(ProcessSpoilsReplacement, address).Activate();
            Line($"[HOOK] ProcessSpoils addr=0x{address:X}");
        }
        catch (Exception ex)
        {
            Line("[HOOK-FAILED] ProcessSpoils " + ex);
        }
    }

    private void ProcessSpoilsReplacement()
    {
        byte[]? before = null;
        try
        {
            before = SnapshotPoachCounts();
            Line("[PROCESS-SPOILS enter]");
            LogPoachCounts("[PROCESS-SPOILS before]", before);
        }
        catch (Exception ex)
        {
            Line("[PROCESS-SPOILS before-error] " + ex.Message);
        }

        try
        {
            _processSpoilsHook!.OriginalFunction();
        }
        finally
        {
            try
            {
                var after = SnapshotPoachCounts();
                Line("[PROCESS-SPOILS exit]");
                LogPoachDeltas("[PROCESS-SPOILS delta]", before, after);
                LogRecentMonsterKos();
                lock (_poachGate)
                    _lastPoachCounts = after;
            }
            catch (Exception ex)
            {
                Line("[PROCESS-SPOILS after-error] " + ex.Message);
            }
        }
    }

    private void StartPoller()
    {
        _poller = new Thread(Poll)
        {
            IsBackground = true,
            Name = "KatanaReforgedProbe",
        };
        _poller.Start();
        Line($"[POLL] started interval={PollIntervalMs}ms");
    }

    private void Poll()
    {
        while (_running)
        {
            try
            {
                long now = Stopwatch.GetTimestamp();
                CaptureUnitHookObservation(now);
                CaptureCalcEntryEvents(now);
                PollRegisteredUnits(now);
                CapturePoachCounterChanges();
                TryDumpPoachTableOnce();
            }
            catch (Exception ex)
            {
                Line("[POLL-ERROR] " + ex.Message);
            }

            Thread.Sleep(PollIntervalMs);
        }
    }

    private void CaptureUnitHookObservation(long now)
    {
        if (_unitBuffer == 0)
            return;

        int count = Marshal.ReadInt32(_unitBuffer + UnitBufferCountOffset);
        if (count == _lastUnitHookCount)
            return;

        _lastUnitHookCount = count;
        nint unitPtr = Marshal.ReadIntPtr(_unitBuffer + UnitBufferPtrOffset);
        var raw = new byte[UnitDumpSize];
        Marshal.Copy(_unitBuffer, raw, 0, raw.Length);

        if (!TryCreateUnitSnapshot(unitPtr, raw, out var unit))
            return;

        RegisterNearbyBattleUnits(unit.Ptr);
        ProcessObservedUnit(unit, now, fromHook: true);
    }

    private void PollRegisteredUnits(long now)
    {
        nint[] units;
        lock (_stateGate)
            units = _unitRegistry.ToArray();

        foreach (nint ptr in units)
        {
            if (TryReadLiveUnitSnapshot(ptr, out var unit))
                ProcessObservedUnit(unit, now, fromHook: false);
        }
    }

    private void ProcessObservedUnit(UnitSnapshot unit, long now, bool fromHook)
    {
        lock (_stateGate)
        {
            if (!_unitRegistry.Contains(unit.Ptr))
            {
                if (_unitRegistry.Count >= MaxTrackedBattleUnits)
                    return;

                _unitRegistry.Add(unit.Ptr);
                Line($"[UNIT-NEW source={(fromHook ? "hook" : "scan")}] {unit.UnitLine} {unit.EquipmentLine}");
            }

            _unitObservations.TryGetValue(unit.Ptr, out var previous);
            var state = ActionProbeState.From(unit);
            LogActionStateIfInteresting(unit, state, previous);

            if (previous is not null)
            {
                if (unit.Hp != previous.Unit.Hp || unit.Mp != previous.Unit.Mp)
                    Line($"[HPMP] {unit.UnitLine} prevHp={previous.Unit.Hp} prevMp={previous.Unit.Mp}");

                if (previous.Unit.Hp > 0 && unit.Hp <= 0)
                {
                    string monsterTag = unit.IsMonsterJob ? "KO-MONSTER" : "KO-UNIT";
                    string recent = FormatRecentCalcForTarget(unit, now);
                    string jobPoach = unit.IsMonsterJob ? FormatJobPoachInfo(unit.Job) : "jobPoach=n/a";
                    Line($"[{monsterTag}] {unit.UnitLine} {jobPoach} recentCalc={recent}");
                }

                if (previous.Unit.Ct > unit.Ct)
                    Line($"[CT-DROP] {unit.UnitLine} prevCt={previous.Unit.Ct} drop={previous.Unit.Ct - unit.Ct}");
            }

            _unitObservations[unit.Ptr] = new UnitObservation(unit, state, now);
        }
    }

    private void LogActionStateIfInteresting(UnitSnapshot unit, ActionProbeState state, UnitObservation? previousObservation)
    {
        var previous = previousObservation?.ActionState;
        if (!state.LooksRelevant && previous is null)
            return;

        if (_lastActionState.TryGetValue(unit.Ptr, out var lastKey) && lastKey == state.Key)
            return;

        _lastActionState[unit.Ptr] = state.Key;

        bool wasKatana = previous is not null && KatanaActions.ContainsKey(previous.ActionId);
        bool resolvedKatana = previous is not null && previous.ActionId != 0 && KatanaActions.ContainsKey(previous.ActionId) && state.IsClearedPendingAction(previous.ActionId);

        if (!KatanaActions.TryGetValue(state.ActionId, out var katana) && !wasKatana && !resolvedKatana)
            return;

        if (katana is not null)
        {
            Line($"[KATANA-ACTION] {unit.UnitLine} action={state.AllFields} ability={katana.AbilityName} itemId={katana.ItemId} katana={katana.KatanaName} {unit.EquipmentLine}");
            LogKatanaBoundary(unit, state, previousObservation, katana);
            return;
        }

        if (resolvedKatana && previous is not null && KatanaActions.TryGetValue(previous.ActionId, out var resolved))
        {
            Line($"[KATANA-RESOLVE-CANDIDATE] {unit.UnitLine} previousAction={previous.AllFields} currentAction={state.AllFields} ability={resolved.AbilityName} itemId={resolved.ItemId} katana={resolved.KatanaName} {unit.EquipmentLine}");
            LogKatanaBoundary(unit, state, previousObservation, resolved);
            return;
        }

        if (wasKatana && previous is not null && KatanaActions.TryGetValue(previous.ActionId, out var old))
        {
            Line($"[KATANA-ACTION-EXIT] {unit.UnitLine} previousAction={previous.AllFields} currentAction={state.AllFields} ability={old.AbilityName} itemId={old.ItemId} katana={old.KatanaName}");
            LogKatanaBoundary(unit, state, previousObservation, old);
        }
    }

    private void LogKatanaBoundary(UnitSnapshot unit, ActionProbeState state, UnitObservation? previous, KatanaInfo katana)
    {
        string previousFields = previous is null ? "none" : KatanaBoundaryFields(previous.Unit.Raw);
        string currentFields = KatanaBoundaryFields(unit.Raw);
        string diff = previous is null
            ? "no-baseline"
            : string.Join(" ", KatanaBoundaryDiffs(previous.Unit.Raw, unit.Raw, 48));
        if (string.IsNullOrWhiteSpace(diff))
            diff = "none";

        Line($"[KATANA-BOUNDARY] {unit.UnitLine} ability={katana.AbilityName} itemId={katana.ItemId} katana={katana.KatanaName} prev={previousFields} curr={currentFields} diff={diff}");

        bool dumpRaw = previous is null ||
                       previous.ActionState.ForecastFlag != state.ForecastFlag ||
                       previous.ActionState.ApplyMask != state.ApplyMask ||
                       previous.ActionState.StagedResultPresent != state.StagedResultPresent ||
                       previous.ActionState.ResultKind != state.ResultKind ||
                       previous.ActionState.PhaseMarker != state.PhaseMarker;
        if (dumpRaw)
            Line($"[KATANA-RAW] ptr=0x{unit.Ptr:X} ability={katana.AbilityName} itemId={katana.ItemId} katana={katana.KatanaName} raw180_1FF={FormatByteRange(unit.Raw, 0x180, 0x80)}");

        if (state.HasShatterMask)
        {
            Line($"[KATANA-SHATTER] {unit.UnitLine} ability={katana.AbilityName} itemId={katana.ItemId} katana={katana.KatanaName} reason=mask1D0-bit0x1000 action={state.AllFields} raw180_1FF={FormatByteRange(unit.Raw, 0x180, 0x80)}");

            if (previous?.ActionState.HasShatterMask != true)
                TryAddShatteredKatanaToPoachStore(unit, state, katana);
        }

        if ((state.ForecastFlag & 0x10) != 0)
        {
            Line($"[KATANA-F1E5-MARKER] {unit.UnitLine} ability={katana.AbilityName} itemId={katana.ItemId} katana={katana.KatanaName} reason=f1E5-bit0x10 action={state.AllFields} raw180_1FF={FormatByteRange(unit.Raw, 0x180, 0x80)}");
        }
    }

    private void CaptureCalcEntryEvents(long now)
    {
        if (_calcEntryBuffer == 0)
            return;

        int count = Marshal.ReadInt32(_calcEntryBuffer);
        if (count == _lastCalcEntryCount)
            return;

        int fresh = count - _lastCalcEntryCount;
        int start = fresh < 0
            ? Math.Max(0, count - Math.Min(count, CalcEntryRingSlots))
            : Math.Max(_lastCalcEntryCount, count - Math.Min(fresh, CalcEntryRingSlots));

        long unitTable = _moduleBase.ToInt64() + UnitTableRva;
        for (int n = start; n < count; n++)
        {
            nint slot = _calcEntryBuffer + 16 + ((n & (CalcEntryRingSlots - 1)) * 16);
            long recordPtr = Marshal.ReadInt64(slot);
            int targetIdx = Marshal.ReadInt32(slot + 8) & 0xFF;
            int packed = Marshal.ReadInt32(slot + 12);
            int casterIdx = packed & 0xFF;
            int actionType = (packed >> 8) & 0xFF;
            int abilityId = (packed >> 16) & 0xFFFF;
            nint targetPtr = (nint)(unitTable + (targetIdx * UnitStride));
            nint casterPtr = (nint)(unitTable + (casterIdx * UnitStride));
            TryReadLiveUnitSnapshot(targetPtr, out var target);
            TryReadLiveUnitSnapshot(casterPtr, out var caster);

            var calcEvent = new CalcEvent(now, n, recordPtr, casterIdx, targetIdx, actionType, abilityId, caster, target);
            lock (_stateGate)
                _recentCalcEvents.Add(calcEvent);

            KatanaActions.TryGetValue(abilityId, out var katanaInfo);
            bool targetMonster = target?.IsMonsterJob == true;
            if (katanaInfo is not null || targetMonster)
            {
                string katanaText = katanaInfo is not null
                    ? $" katanaAbility={katanaInfo.AbilityName} itemId={katanaInfo.ItemId} katana={katanaInfo.KatanaName}"
                    : "";
                string casterText = caster is null ? $"casterIdx={casterIdx}" : $"caster={caster.UnitLine}";
                string targetText = target is null ? $"targetIdx={targetIdx}" : $"target={target.UnitLine}";
                Line($"[CALC] n={n} rec=0x{recordPtr:X} type=0x{actionType:X2} abilityId={abilityId}{katanaText} {casterText} {targetText}");
            }
        }

        _lastCalcEntryCount = count;
        PruneRecentCalcEvents(now);
    }

    private void CapturePoachCounterChanges()
    {
        if (_carcassCountsAddress == 0)
            return;

        lock (_poachGate)
        {
            var current = SnapshotPoachCounts();
            if (_lastPoachCounts is null)
            {
                _lastPoachCounts = current;
                return;
            }

            if (current is not null && HasPoachDelta(_lastPoachCounts, current))
            {
                LogPoachDeltas("[POACH-DELTA poll]", _lastPoachCounts, current);
                _lastPoachCounts = current;
            }
        }
    }

    private void RegisterNearbyBattleUnits(nint anchor)
    {
        if (anchor == 0)
            return;

        for (int delta = -16; delta <= 16; delta++)
        {
            nint ptr = anchor + (delta * UnitStride);
            if (TryReadLiveUnitSnapshot(ptr, out var unit))
            {
                lock (_stateGate)
                {
                    if (_unitRegistry.Contains(unit.Ptr))
                        continue;
                    if (_unitRegistry.Count >= MaxTrackedBattleUnits)
                        return;
                    _unitRegistry.Add(unit.Ptr);
                    Line($"[UNIT-NEW source=nearby-scan] {unit.UnitLine} {unit.EquipmentLine}");
                }
            }
        }
    }

    private string FormatRecentCalcForTarget(UnitSnapshot target, long now)
    {
        PruneRecentCalcEvents(now);

        CalcEvent[] matches;
        lock (_stateGate)
        {
            matches = _recentCalcEvents
                .Where(e => e.Target?.Ptr == target.Ptr || e.Target?.CharId == target.CharId)
                .OrderByDescending(e => e.Tick)
                .Take(3)
                .ToArray();
        }

        return matches.Length == 0
            ? "none"
            : string.Join(" | ", matches.Select(FormatCalcEventBrief));
    }

    private static string FormatCalcEventBrief(CalcEvent e)
    {
        string ability = KatanaActions.TryGetValue(e.AbilityId, out var katana)
            ? $"{e.AbilityId}/{katana.AbilityName}/item{katana.ItemId}"
            : e.AbilityId.ToString();
        string caster = e.Caster is null ? $"idx{e.CasterIdx}" : $"id=0x{e.Caster.CharId:X2}/job=0x{e.Caster.Job:X2}/team={e.Caster.Team}";
        return $"calc#{e.Sequence}:type=0x{e.ActionType:X2}/ability={ability}/caster={caster}";
    }

    private void LogRecentMonsterKos()
    {
        UnitObservation[] observations;
        lock (_stateGate)
            observations = _unitObservations.Values.ToArray();

        var recentKos = observations
            .Where(o => o.Unit.IsMonsterJob && o.Unit.Hp <= 0)
            .OrderBy(o => o.Unit.Ptr)
            .Select(o => $"{o.Unit.UnitLine} {FormatJobPoachInfo(o.Unit.Job)}")
            .ToArray();

        if (recentKos.Length > 0)
            Line("[RECENT-MONSTER-KOS] " + string.Join(" || ", recentKos));
    }

    private byte[]? SnapshotPoachCounts()
    {
        if (_carcassCountsAddress == 0)
            return null;

        var bytes = new byte[PoachIdCount + 1];
        Marshal.Copy(_carcassCountsAddress, bytes, 0, bytes.Length);
        return bytes;
    }

    private static bool HasPoachDelta(byte[] before, byte[] after)
    {
        int max = Math.Min(before.Length, after.Length);
        for (int i = 1; i < max; i++)
        {
            if (before[i] != after[i])
                return true;
        }

        return false;
    }

    private void LogPoachDeltas(string prefix, byte[]? before, byte[]? after)
    {
        if (before is null || after is null)
        {
            Line($"{prefix} unavailable");
            return;
        }

        bool any = false;
        int max = Math.Min(Math.Min(before.Length, after.Length), PoachIdCount + 1);
        for (int poachId = 1; poachId < max; poachId++)
        {
            if (before[poachId] == after[poachId])
                continue;

            any = true;
            Line($"{prefix} poachId={poachId} {before[poachId]}->{after[poachId]} {FormatPoachItemInfo(poachId)}");
        }

        if (!any)
            Line($"{prefix} none");
    }

    private void LogPoachCounts(string prefix, byte[]? counts)
    {
        if (counts is null)
        {
            Line($"{prefix} unavailable");
            return;
        }

        var nonZero = new List<string>();
        for (int poachId = 1; poachId < counts.Length && poachId <= PoachIdCount; poachId++)
        {
            if (counts[poachId] > 0)
                nonZero.Add($"{poachId}:{counts[poachId]}");
        }

        Line($"{prefix} nonZero={(nonZero.Count == 0 ? "none" : string.Join(",", nonZero))}");
    }

    private void TryAddShatteredKatanaToPoachStore(UnitSnapshot unit, ActionProbeState state, KatanaInfo katana)
    {
        if (state.StagedAilment != katana.ItemId)
        {
            Line($"[KATANA-POACH-SKIP] reason=item-mismatch actionItem={state.StagedAilment} mappedItem={katana.ItemId} ability={katana.AbilityName} unit={unit.UnitLine}");
            return;
        }

        if (_carcassCountsAddress == 0)
        {
            Line($"[KATANA-POACH-SKIP] reason=carcass-counts-unavailable itemId={katana.ItemId} katana={katana.KatanaName}");
            return;
        }

        lock (_poachGate)
        {
            if (!TryPatchKatanaPoachRow(katana, out string patchDetails))
            {
                Line($"[KATANA-POACH-SKIP] reason=row-patch-failed itemId={katana.ItemId} katana={katana.KatanaName} poachId={katana.PoachId} {patchDetails}");
                return;
            }

            nint countPtr = _carcassCountsAddress + katana.PoachId;
            int before = Marshal.ReadByte(countPtr);
            int after = before >= 99 ? before : before + 1;
            if (after != before)
                Marshal.WriteByte(countPtr, (byte)after);

            Line($"[KATANA-POACH-ADD] unit={unit.UnitLine} itemId={katana.ItemId} katana={katana.KatanaName} poachId={katana.PoachId} count={before}->{after} {patchDetails}");
        }
    }

    private bool TryPatchKatanaPoachRow(KatanaInfo katana, out string details)
    {
        details = "";
        try
        {
            ulong row = GetRowData(0xEC, katana.PoachId);
            if (row == 0)
            {
                details = "row=unavailable";
                return false;
            }

            string oldName = ReadRelativeAnsiString(row, 0x8).Replace("<Icon=103>", "+");
            int oldCost = Marshal.ReadInt32((nint)(row + 0x24));
            int oldSell = Marshal.ReadInt32((nint)(row + 0x28));
            int oldItem = Marshal.ReadInt32((nint)(row + 0x2C));
            bool rowAlreadyPatched =
                oldItem == katana.ItemId &&
                _patchedPoachSlotItems.TryGetValue(katana.PoachId, out int patchedItem) &&
                patchedItem == katana.ItemId;

            int cost = Math.Max(1, katana.Price);
            int sellPrice = Math.Max(1, cost / 2);
            if (!rowAlreadyPatched)
            {
                Marshal.WriteInt32((nint)(row + 0x24), cost);
                Marshal.WriteInt32((nint)(row + 0x28), sellPrice);
                Marshal.WriteInt32((nint)(row + 0x2C), katana.ItemId);
                Marshal.WriteByte((nint)(row + 0x34), (byte)(katana.ItemId >= 46 ? 1 : 0));

                var namePatches = new List<string>();
                foreach (int offset in PoachNameFieldOffsets)
                {
                    if (TryOverwriteRelativeAnsiString(row, offset, katana.KatanaName, out string namePatch))
                        namePatches.Add($"+0x{offset:X}:{namePatch}");
                }

                _patchedPoachSlotItems[katana.PoachId] = katana.ItemId;
                string patchedName = ReadRelativeAnsiString(row, 0x8).Replace("<Icon=103>", "+");
                details = $"row=0x{row:X} oldName=\"{oldName}\" newName=\"{patchedName}\" oldItem={oldItem} newItem={katana.ItemId} oldCost={oldCost} newCost={cost} oldSell={oldSell} newSell={sellPrice} namePatches={string.Join(",", namePatches)}";
                return true;
            }

            details = $"row=0x{row:X} alreadyPatched=1 name=\"{oldName}\" itemId={oldItem} cost={oldCost} sell={oldSell}";
            return true;
        }
        catch (Exception ex)
        {
            details = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private string FormatJobPoachInfo(int jobId)
    {
        try
        {
            ulong row = GetRowData(0x2E, jobId);
            if (row == 0)
                return "jobPoach=unavailable";

            string name = ReadRelativeAnsiString(row, 0x8).Replace("<Icon=103>", "+");
            int common = Marshal.ReadInt32((nint)(row + 0x24));
            int rare = Marshal.ReadInt32((nint)(row + 0x28));
            return $"jobName=\"{name}\" commonPoach={common} rarePoach={rare}";
        }
        catch
        {
            return "jobPoach=unavailable";
        }
    }

    private string FormatPoachItemInfo(int poachId)
    {
        try
        {
            ulong row = GetRowData(0xEC, poachId);
            if (row == 0)
                return "poachRow=unavailable";

            string name = ReadRelativeAnsiString(row, 0x8).Replace("<Icon=103>", "+");
            int cost = Marshal.ReadInt32((nint)(row + 0x24));
            int sellPrice = Marshal.ReadInt32((nint)(row + 0x28));
            int itemId = Marshal.ReadInt32((nint)(row + 0x2C));
            int iconId = Marshal.ReadInt32((nint)(row + 0x30));
            int word20 = Marshal.ReadInt32((nint)(row + 0x20));
            return $"poachName=\"{name}\" cost={cost} sell={sellPrice} itemId={itemId} iconId={iconId} row=0x{row:X} w20={word20}";
        }
        catch (Exception ex)
        {
            return $"poachRow=error:{ex.GetType().Name}";
        }
    }

    private void TryDumpPoachTableOnce()
    {
        if (_poachTableDumped || _getTable is null || _nexSearchRow1K is null || _nexGetRowData is null || _nexModulePointerAddress == 0)
            return;

        long now = Stopwatch.GetTimestamp();
        if (_lastPoachTableDumpAttemptTick != 0 &&
            (now - _lastPoachTableDumpAttemptTick) * 1000 / Stopwatch.Frequency < PoachTableDumpRetryMs)
            return;

        _lastPoachTableDumpAttemptTick = now;
        _poachTableDumpAttempts++;

        try
        {
            ulong nexModule = (ulong)Marshal.ReadInt64(_nexModulePointerAddress);
            if (nexModule == 0)
                return;

            int rows = 0;
            Line($"[POACH-TABLE-DUMP] attempt={_poachTableDumpAttempts} nexModule=0x{nexModule:X}");
            for (int poachId = 1; poachId <= PoachIdCount; poachId++)
            {
                ulong row = GetRowData(0xEC, poachId);
                if (row == 0)
                    continue;

                rows++;
                string name = ReadRelativeAnsiString(row, 0x8).Replace("<Icon=103>", "+");
                int value = Marshal.ReadInt32((nint)(row + 0x28));
                Line($"[POACH-ROW] id={poachId} row=0x{row:X} name=\"{name}\" value={value} raw={ReadRowWords(row, 0x40)}");
            }

            _poachTableDumped = rows > 0;
            if (!_poachTableDumped)
                Line($"[POACH-TABLE-DUMP pending] attempt={_poachTableDumpAttempts} rows=0");
        }
        catch (Exception ex)
        {
            _poachTableDumped = false;
            Line("[POACH-TABLE-DUMP error] " + ex.Message);
        }
    }

    private ulong GetRowData(int tableId, int key)
    {
        if (_getTable is null || _nexSearchRow1K is null || _nexGetRowData is null || _nexModulePointerAddress == 0)
            return 0;

        ulong nexModule = (ulong)Marshal.ReadInt64(_nexModulePointerAddress);
        if (nexModule == 0)
            return 0;

        ulong table = _getTable(nexModule, tableId);
        if (table == 0)
            return 0;

        ulong row = _nexSearchRow1K(table, key);
        return row == 0 ? 0 : _nexGetRowData(row);
    }

    private static string ReadRelativeAnsiString(ulong row, int fieldOffset)
    {
        nint strPtr = GetRelativeAnsiStringPointer(row, fieldOffset);
        return Marshal.PtrToStringAnsi(strPtr) ?? "";
    }

    private static nint GetRelativeAnsiStringPointer(ulong row, int fieldOffset)
    {
        long fieldAddress = unchecked((long)(row + (uint)fieldOffset));
        int rel = Marshal.ReadInt32((nint)fieldAddress);
        return (nint)(fieldAddress + rel);
    }

    private static bool TryOverwriteRelativeAnsiString(ulong row, int fieldOffset, string value, out string result)
    {
        result = "";
        try
        {
            nint strPtr = GetRelativeAnsiStringPointer(row, fieldOffset);
            string old = Marshal.PtrToStringAnsi(strPtr) ?? "";
            int oldLength = Encoding.ASCII.GetByteCount(old);
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            if (bytes.Length > oldLength)
            {
                result = $"skip oldLen={oldLength} newLen={bytes.Length}";
                return false;
            }

            Marshal.Copy(bytes, 0, strPtr, bytes.Length);
            for (int i = bytes.Length; i <= oldLength; i++)
                Marshal.WriteByte(strPtr, i, 0);

            result = $"ok old=\"{old}\"";
            return true;
        }
        catch (Exception ex)
        {
            result = $"error:{ex.GetType().Name}";
            return false;
        }
    }

    private static string ReadRowWords(ulong row, int byteCount)
    {
        var parts = new List<string>();
        for (int offset = 0; offset < byteCount; offset += 4)
        {
            int value = Marshal.ReadInt32((nint)(row + (uint)offset));
            parts.Add($"+0x{offset:X2}={value}");
        }

        return string.Join(" ", parts);
    }

    private static List<string> KatanaBoundaryDiffs(byte[] previousRaw, byte[] currentRaw, int max)
    {
        var diffs = new List<string>();
        int limit = Math.Min(previousRaw.Length, currentRaw.Length);
        if (limit == 0 || max <= 0)
            return diffs;

        foreach (int offset in KatanaBoundaryOffsets)
        {
            if (offset < 0 || offset >= limit)
                continue;
            if (previousRaw[offset] == currentRaw[offset])
                continue;
            diffs.Add($"+0x{offset:X3}:{previousRaw[offset]:X2}->{currentRaw[offset]:X2}");
            if (diffs.Count >= max)
                break;
        }

        return diffs;
    }

    private static string KatanaBoundaryFields(byte[] raw)
        => $"hp={ReadUInt16Raw(raw, 0x30)}/mp={ReadUInt16Raw(raw, 0x34)}/ct={ReadByteRaw(raw, 0x41)}" +
           $"/s61={ReadByteRaw(raw, 0x61)}/t18D={ReadByteRaw(raw, 0x18D)}" +
           $"/a0={ReadByteRaw(raw, 0x1A0)}/a1={ReadByteRaw(raw, 0x1A1)}/act={ReadUInt16Raw(raw, 0x1A2)}" +
           $"/ail1A8={ReadUInt16Raw(raw, 0x1A8)}/be={ReadByteRaw(raw, 0x1BE)}/kind1C0={ReadByteRaw(raw, 0x1C0)}" +
           $"/dmg1C4={ReadUInt16Raw(raw, 0x1C4)}/cred1C6={ReadUInt16Raw(raw, 0x1C6)}" +
           $"/mpD1C8={ReadUInt16Raw(raw, 0x1C8)}/mpC1CA={ReadUInt16Raw(raw, 0x1CA)}" +
           $"/mask1D0=0x{ReadUInt32Raw(raw, 0x1D0):X8}/chg1D8={ReadUInt16Raw(raw, 0x1D8)}" +
           $"/f1E5={ReadByteRaw(raw, 0x1E5)}/f1EF={ReadByteRaw(raw, 0x1EF)}" +
           $"/b8={ReadByteRaw(raw, 0x1B8)}/ba={ReadByteRaw(raw, 0x1BA)}/bb={ReadByteRaw(raw, 0x1BB)}";

    private static string FormatByteRange(byte[] raw, int start, int length)
    {
        if (raw.Length == 0 || length <= 0)
            return "";

        start = Math.Clamp(start, 0, raw.Length - 1);
        int end = Math.Min(raw.Length, start + length);
        var bytes = new string[end - start];
        for (int i = start; i < end; i++)
            bytes[i - start] = raw[i].ToString("X2");

        return string.Join(" ", bytes);
    }

    private static int ReadByteRaw(byte[] raw, int offset)
        => offset >= 0 && offset < raw.Length ? raw[offset] : 0;

    private static int ReadUInt16Raw(byte[] raw, int offset)
        => ReadByteRaw(raw, offset) | (ReadByteRaw(raw, offset + 1) << 8);

    private static uint ReadUInt32Raw(byte[] raw, int offset)
        => (uint)(ReadByteRaw(raw, offset) |
                  (ReadByteRaw(raw, offset + 1) << 8) |
                  (ReadByteRaw(raw, offset + 2) << 16) |
                  (ReadByteRaw(raw, offset + 3) << 24));

    private bool TryReadLiveUnitSnapshot(nint ptr, out UnitSnapshot unit)
    {
        unit = null!;
        if (ptr == 0)
            return false;

        try
        {
            var raw = new byte[UnitDumpSize];
            Marshal.Copy(ptr, raw, 0, raw.Length);
            return TryCreateUnitSnapshot(ptr, raw, out unit);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCreateUnitSnapshot(nint ptr, byte[] raw, out UnitSnapshot unit)
    {
        unit = null!;
        if (ptr == 0 || raw.Length < UnitDumpSize)
            return false;

        var candidate = new UnitSnapshot(ptr, raw);
        if (candidate.CharId == 0xFF)
            return false;
        if (candidate.MaxHp <= 0 || candidate.MaxHp > 9999)
            return false;
        if (candidate.Hp > 9999)
            return false;
        if (candidate.Job > 250)
            return false;
        if (candidate.Team > 8)
            return false;

        unit = candidate;
        return true;
    }

    private void PruneRecentCalcEvents(long now)
    {
        long maxAge = Stopwatch.Frequency * 8;
        lock (_stateGate)
            _recentCalcEvents.RemoveAll(e => now - e.Tick > maxAge);
    }

    private static bool ValidateExpectedBytes(nint address, string expectedBytes, out string error)
    {
        error = "";
        try
        {
            byte?[] expected = expectedBytes
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part == "??" ? (byte?)null : Convert.ToByte(part, 16))
                .ToArray();
            var actual = new byte[expected.Length];
            Marshal.Copy(address, actual, 0, actual.Length);

            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i].HasValue && actual[i] != expected[i]!.Value)
                {
                    error = $"expected {expectedBytes}, actual {BitConverter.ToString(actual).Replace('-', ' ')}";
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void Zero(nint ptr, int length)
    {
        for (int i = 0; i < length; i++)
            Marshal.WriteByte(ptr, i, 0);
    }

    private void Line(string message)
    {
        if (!DiagnosticLogsEnabled)
            return;

        string line = $"[{DateTime.Now:HH:mm:ss.fff}] [{_modConfig?.ModId ?? "ffttic.katanareforged"}] {message}";
        lock (_logGate)
        {
            try
            {
                File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Probe logging must never destabilize the game.
            }

            try
            {
                _logger?.WriteLine(line);
            }
            catch
            {
                // Same: keep hooks observational and resilient.
            }
        }
    }

    private sealed record UnitObservation(UnitSnapshot Unit, ActionProbeState ActionState, long Tick);

    private sealed record KatanaInfo(
        int ItemId,
        string AbilityName,
        string KatanaName,
        int PoachId,
        int Price);

    private sealed record CalcEvent(
        long Tick,
        int Sequence,
        long RecordPtr,
        int CasterIdx,
        int TargetIdx,
        int ActionType,
        int AbilityId,
        UnitSnapshot? Caster,
        UnitSnapshot? Target);
}
