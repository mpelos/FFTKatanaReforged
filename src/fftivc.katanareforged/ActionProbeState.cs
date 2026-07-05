namespace fftivc.katanareforged;

internal sealed record ActionProbeState(
    int PendingFlag,
    int PendingTimer,
    int Boundary0,
    int Boundary1,
    int ActionId,
    int StagedAilment,
    int StagedResultPresent,
    int ResultKind,
    int ForecastDamage,
    int ForecastCredit,
    int StagedMpDebit,
    int StagedMpCredit,
    uint ApplyMask,
    int ForecastCharge,
    int ForecastFlag,
    int PendingFlag2,
    int ActiveMarker,
    int ActiveMarker2,
    int PhaseMarker)
{
    private const int PendingActionBit = 0x08;

    public static ActionProbeState From(UnitSnapshot unit)
        => new(
            unit.ReadByte(0x61),
            unit.ReadByte(0x18D),
            unit.ReadByte(0x1A0),
            unit.ReadByte(0x1A1),
            unit.ReadUInt16(0x1A2),
            unit.ReadUInt16(0x1A8),
            unit.ReadByte(0x1BE),
            unit.ReadByte(0x1C0),
            unit.ReadUInt16(0x1C4),
            unit.ReadUInt16(0x1C6),
            unit.ReadUInt16(0x1C8),
            unit.ReadUInt16(0x1CA),
            unit.ReadUInt32(0x1D0),
            unit.ReadByte(0x1D8),
            unit.ReadByte(0x1E5),
            unit.ReadByte(0x1EF),
            unit.ReadByte(0x1B8),
            unit.ReadByte(0x1BA),
            unit.ReadByte(0x1BB));

    public bool HasPrimaryPendingFlag => (PendingFlag & PendingActionBit) != 0;
    public bool HasSecondaryPendingFlag => (PendingFlag2 & PendingActionBit) != 0;
    public bool IsActiveSourceMarker => ActiveMarker2 == 1;
    public bool HasShatterMask => (ApplyMask & 0x1000) != 0;

    public bool LooksRelevant =>
        PendingFlag != 0 ||
        PendingTimer != 0xFF ||
        Boundary0 != 0 ||
        Boundary1 != 0 ||
        ActionId != 0 ||
        StagedAilment != 0 ||
        StagedResultPresent != 0 ||
        ResultKind != 0 ||
        ForecastDamage != 0 ||
        ForecastCredit != 0 ||
        StagedMpDebit != 0 ||
        StagedMpCredit != 0 ||
        ApplyMask != 0 ||
        ForecastCharge != 0 ||
        ForecastFlag != 0 ||
        PendingFlag2 != 0 ||
        ActiveMarker != 0 ||
        ActiveMarker2 != 0 ||
        PhaseMarker != 0;

    public bool IsClearedPendingAction(int actionId)
        => actionId > 0 &&
           ActionId == actionId &&
           !HasPrimaryPendingFlag &&
           !HasSecondaryPendingFlag &&
           PendingTimer == 0xFF;

    public string Key => string.Join('/',
        PendingFlag,
        PendingTimer,
        Boundary0,
        Boundary1,
        ActionId,
        StagedAilment,
        StagedResultPresent,
        ResultKind,
        ForecastDamage,
        ForecastCredit,
        StagedMpDebit,
        StagedMpCredit,
        ApplyMask,
        ForecastCharge,
        ForecastFlag,
        PendingFlag2,
        ActiveMarker,
        ActiveMarker2,
        PhaseMarker);

    public string AllFields =>
        $"s61={PendingFlag}/t18D={PendingTimer}/a0={Boundary0}/a1={Boundary1}/act={ActionId}" +
        $"/ail1A8={StagedAilment}/be={StagedResultPresent}/kind1C0={ResultKind}" +
        $"/dmg1C4={ForecastDamage}/cred1C6={ForecastCredit}/mpD1C8={StagedMpDebit}/mpC1CA={StagedMpCredit}" +
        $"/mask1D0=0x{ApplyMask:X8}/chg1D8={ForecastCharge}/f1E5={ForecastFlag}/f1EF={PendingFlag2}" +
        $"/b8={ActiveMarker}/ba={ActiveMarker2}/bb={PhaseMarker}";
}
