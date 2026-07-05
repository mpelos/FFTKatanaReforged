namespace fftivc.katanareforged;

internal sealed record UnitSnapshot(nint Ptr, byte[] Raw)
{
    public int CharId => ReadByte(0x00);
    public int Job => ReadByte(0x03);
    public int Team => ReadByte(0x04);
    public int Flags06 => ReadByte(0x06);
    public int Hp => ReadUInt16(0x30);
    public int MaxHp => ReadUInt16(0x32);
    public int Mp => ReadUInt16(0x34);
    public int MaxMp => ReadUInt16(0x36);
    public int Ct => ReadByte(0x41);
    public int Status61 => ReadByte(0x61);
    public int Status1Ef => ReadByte(0x1EF);
    public bool IsMonsterJob => Job is >= 94 and <= 141;

    public int ReadByte(int offset)
        => offset >= 0 && offset < Raw.Length ? Raw[offset] : 0;

    public int ReadUInt16(int offset)
        => offset >= 0 && offset + 1 < Raw.Length
            ? Raw[offset] | (Raw[offset + 1] << 8)
            : 0;

    public uint ReadUInt32(int offset)
        => offset >= 0 && offset + 3 < Raw.Length
            ? (uint)(Raw[offset] |
                     (Raw[offset + 1] << 8) |
                     (Raw[offset + 2] << 16) |
                     (Raw[offset + 3] << 24))
            : 0;

    public string UnitLine =>
        $"ptr=0x{Ptr:X}/id=0x{CharId:X2}/job=0x{Job:X2}/team={Team}/hp={Hp}/{MaxHp}/mp={Mp}/{MaxMp}/ct={Ct}/st61=0x{Status61:X2}/st1EF=0x{Status1Ef:X2}/flags06=0x{Flags06:X2}";

    public string EquipmentLine
    {
        get
        {
            const int baseOffset = 0x1A;
            return $"equip[+0x{baseOffset:X}]=[head={ReadUInt16(baseOffset)} body={ReadUInt16(baseOffset + 2)} acc={ReadUInt16(baseOffset + 4)} " +
                   $"rWeapon={ReadUInt16(baseOffset + 6)} rShield={ReadUInt16(baseOffset + 8)} lWeapon={ReadUInt16(baseOffset + 10)} lShield={ReadUInt16(baseOffset + 12)}]";
        }
    }
}
