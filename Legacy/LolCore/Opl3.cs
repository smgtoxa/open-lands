// Yamaha YMF262 (OPL3), the chip the game's music is written for.
//
// Transliterated from src/platform/opl3.mjs, itself a port of Nuked OPL3 v1.8 by Nuke.YKT
// (LGPL 2.1+) - a cycle-accurate model, which is why the music sounds like a Sound Blaster and not
// like a synthesiser's idea of one. Only what Lands of Lore uses is kept.
//
// Everything here is integer arithmetic on purpose: the envelope generator, the phase generator and
// the exponential table are the chip's, and a floating-point shortcut anywhere in them changes the
// sound.
namespace LolCore;

public sealed class Opl3
{
    private static readonly ushort[] LogSin =
    {
        0x859, 0x6c3, 0x607, 0x58b, 0x52e, 0x4e4, 0x4a6, 0x471, 0x443, 0x41a, 0x3f5, 0x3d3, 0x3b5, 0x398, 0x37e, 0x365,
        0x34e, 0x339, 0x324, 0x311, 0x2ff, 0x2ed, 0x2dc, 0x2cd, 0x2bd, 0x2af, 0x2a0, 0x293, 0x286, 0x279, 0x26d, 0x261,
        0x256, 0x24b, 0x240, 0x236, 0x22c, 0x222, 0x218, 0x20f, 0x206, 0x1fd, 0x1f5, 0x1ec, 0x1e4, 0x1dc, 0x1d4, 0x1cd,
        0x1c5, 0x1be, 0x1b7, 0x1b0, 0x1a9, 0x1a2, 0x19b, 0x195, 0x18f, 0x188, 0x182, 0x17c, 0x177, 0x171, 0x16b, 0x166,
        0x160, 0x15b, 0x155, 0x150, 0x14b, 0x146, 0x141, 0x13c, 0x137, 0x133, 0x12e, 0x129, 0x125, 0x121, 0x11c, 0x118,
        0x114, 0x10f, 0x10b, 0x107, 0x103, 0x0ff, 0x0fb, 0x0f8, 0x0f4, 0x0f0, 0x0ec, 0x0e9, 0x0e5, 0x0e2, 0x0de, 0x0db,
        0x0d7, 0x0d4, 0x0d1, 0x0cd, 0x0ca, 0x0c7, 0x0c4, 0x0c1, 0x0be, 0x0bb, 0x0b8, 0x0b5, 0x0b2, 0x0af, 0x0ac, 0x0a9,
        0x0a7, 0x0a4, 0x0a1, 0x09f, 0x09c, 0x099, 0x097, 0x094, 0x092, 0x08f, 0x08d, 0x08a, 0x088, 0x086, 0x083, 0x081,
        0x07f, 0x07d, 0x07a, 0x078, 0x076, 0x074, 0x072, 0x070, 0x06e, 0x06c, 0x06a, 0x068, 0x066, 0x064, 0x062, 0x060,
        0x05e, 0x05c, 0x05b, 0x059, 0x057, 0x055, 0x053, 0x052, 0x050, 0x04e, 0x04d, 0x04b, 0x04a, 0x048, 0x046, 0x045,
        0x043, 0x042, 0x040, 0x03f, 0x03e, 0x03c, 0x03b, 0x039, 0x038, 0x037, 0x035, 0x034, 0x033, 0x031, 0x030, 0x02f,
        0x02e, 0x02d, 0x02b, 0x02a, 0x029, 0x028, 0x027, 0x026, 0x025, 0x024, 0x023, 0x022, 0x021, 0x020, 0x01f, 0x01e,
        0x01d, 0x01c, 0x01b, 0x01a, 0x019, 0x018, 0x017, 0x017, 0x016, 0x015, 0x014, 0x014, 0x013, 0x012, 0x011, 0x011,
        0x010, 0x00f, 0x00f, 0x00e, 0x00d, 0x00d, 0x00c, 0x00c, 0x00b, 0x00a, 0x00a, 0x009, 0x009, 0x008, 0x008, 0x007,
        0x007, 0x007, 0x006, 0x006, 0x005, 0x005, 0x005, 0x004, 0x004, 0x004, 0x003, 0x003, 0x003, 0x002, 0x002, 0x002,
        0x002, 0x001, 0x001, 0x001, 0x001, 0x001, 0x001, 0x001, 0x000, 0x000, 0x000, 0x000, 0x000, 0x000, 0x000, 0x000,
    };

    private static readonly ushort[] Exp =
    {
        0x7fa, 0x7f5, 0x7ef, 0x7ea, 0x7e4, 0x7df, 0x7da, 0x7d4, 0x7cf, 0x7c9, 0x7c4, 0x7bf, 0x7b9, 0x7b4, 0x7ae, 0x7a9,
        0x7a4, 0x79f, 0x799, 0x794, 0x78f, 0x78a, 0x784, 0x77f, 0x77a, 0x775, 0x770, 0x76a, 0x765, 0x760, 0x75b, 0x756,
        0x751, 0x74c, 0x747, 0x742, 0x73d, 0x738, 0x733, 0x72e, 0x729, 0x724, 0x71f, 0x71a, 0x715, 0x710, 0x70b, 0x706,
        0x702, 0x6fd, 0x6f8, 0x6f3, 0x6ee, 0x6e9, 0x6e5, 0x6e0, 0x6db, 0x6d6, 0x6d2, 0x6cd, 0x6c8, 0x6c4, 0x6bf, 0x6ba,
        0x6b5, 0x6b1, 0x6ac, 0x6a8, 0x6a3, 0x69e, 0x69a, 0x695, 0x691, 0x68c, 0x688, 0x683, 0x67f, 0x67a, 0x676, 0x671,
        0x66d, 0x668, 0x664, 0x65f, 0x65b, 0x657, 0x652, 0x64e, 0x649, 0x645, 0x641, 0x63c, 0x638, 0x634, 0x630, 0x62b,
        0x627, 0x623, 0x61e, 0x61a, 0x616, 0x612, 0x60e, 0x609, 0x605, 0x601, 0x5fd, 0x5f9, 0x5f5, 0x5f0, 0x5ec, 0x5e8,
        0x5e4, 0x5e0, 0x5dc, 0x5d8, 0x5d4, 0x5d0, 0x5cc, 0x5c8, 0x5c4, 0x5c0, 0x5bc, 0x5b8, 0x5b4, 0x5b0, 0x5ac, 0x5a8,
        0x5a4, 0x5a0, 0x59c, 0x599, 0x595, 0x591, 0x58d, 0x589, 0x585, 0x581, 0x57e, 0x57a, 0x576, 0x572, 0x56f, 0x56b,
        0x567, 0x563, 0x560, 0x55c, 0x558, 0x554, 0x551, 0x54d, 0x549, 0x546, 0x542, 0x53e, 0x53b, 0x537, 0x534, 0x530,
        0x52c, 0x529, 0x525, 0x522, 0x51e, 0x51b, 0x517, 0x514, 0x510, 0x50c, 0x509, 0x506, 0x502, 0x4ff, 0x4fb, 0x4f8,
        0x4f4, 0x4f1, 0x4ed, 0x4ea, 0x4e7, 0x4e3, 0x4e0, 0x4dc, 0x4d9, 0x4d6, 0x4d2, 0x4cf, 0x4cc, 0x4c8, 0x4c5, 0x4c2,
        0x4be, 0x4bb, 0x4b8, 0x4b5, 0x4b1, 0x4ae, 0x4ab, 0x4a8, 0x4a4, 0x4a1, 0x49e, 0x49b, 0x498, 0x494, 0x491, 0x48e,
        0x48b, 0x488, 0x485, 0x482, 0x47e, 0x47b, 0x478, 0x475, 0x472, 0x46f, 0x46c, 0x469, 0x466, 0x463, 0x460, 0x45d,
        0x45a, 0x457, 0x454, 0x451, 0x44e, 0x44b, 0x448, 0x445, 0x442, 0x43f, 0x43c, 0x439, 0x436, 0x433, 0x430, 0x42d,
        0x42a, 0x428, 0x425, 0x422, 0x41f, 0x41c, 0x419, 0x416, 0x414, 0x411, 0x40e, 0x40b, 0x408, 0x406, 0x403, 0x400,
    };

    private static readonly int[] Mt = { 1, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 20, 24, 24, 30, 30 };
    private static readonly int[] Ksl = { 0, 32, 40, 45, 48, 51, 53, 55, 56, 58, 59, 60, 61, 62, 63, 64 };
    private static readonly int[] KslShift = { 8, 1, 2, 0 };
    private static readonly int[][] EgIncStep =
    {
        new[] { 0, 0, 0, 0 }, new[] { 1, 0, 0, 0 }, new[] { 1, 0, 1, 0 }, new[] { 1, 1, 1, 0 },
    };
    private static readonly int[] AdSlot =
    {
        0, 1, 2, 3, 4, 5, -1, -1, 6, 7, 8, 9, 10, 11, -1, -1,
        12, 13, 14, 15, 16, 17, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
    };
    private static readonly int[] ChSlot = { 0, 1, 2, 6, 7, 8, 12, 13, 14, 18, 19, 20, 24, 25, 26, 30, 31, 32 };

    private const int Ch2Op = 0, Ch4Op = 1, Ch4Op2 = 2, ChDrum = 3;
    private const int EgkNorm = 1, EgkDrum = 2;
    private const int EgAttack = 0, EgDecay = 1, EgSustain = 2, EgRelease = 3;

    private static int S16(int v) => (short)v;

    private static int CalcExp(int level)
    {
        if (level > 0x1fff) level = 0x1fff;
        return (Exp[level & 0xff] << 1) >> (level >> 8);
    }

    /// <summary>The eight waveforms, as the chip derives them from one quarter of a log-sine table.</summary>
    private static int Wave(int wf, int phase, int env)
    {
        phase &= 0x3ff;
        switch (wf)
        {
            case 0:
            {
                int neg = (phase & 0x200) != 0 ? 0xffff : 0;
                int outv = (phase & 0x100) != 0 ? LogSin[(phase & 0xff) ^ 0xff] : LogSin[phase & 0xff];
                return S16(CalcExp(outv + (env << 3)) ^ neg);
            }
            case 1:
            {
                int outv = (phase & 0x200) != 0 ? 0x1000 : (phase & 0x100) != 0 ? LogSin[(phase & 0xff) ^ 0xff] : LogSin[phase & 0xff];
                return CalcExp(outv + (env << 3));
            }
            case 2:
            {
                int outv = (phase & 0x100) != 0 ? LogSin[(phase & 0xff) ^ 0xff] : LogSin[phase & 0xff];
                return CalcExp(outv + (env << 3));
            }
            case 3:
            {
                int outv = (phase & 0x100) != 0 ? 0x1000 : LogSin[phase & 0xff];
                return CalcExp(outv + (env << 3));
            }
            case 4:
            {
                int neg = (phase & 0x300) == 0x100 ? 0xffff : 0;
                int outv = (phase & 0x200) != 0 ? 0x1000
                    : (phase & 0x80) != 0 ? LogSin[((phase ^ 0xff) << 1) & 0xff] : LogSin[(phase << 1) & 0xff];
                return S16(CalcExp(outv + (env << 3)) ^ neg);
            }
            case 5:
            {
                int outv = (phase & 0x200) != 0 ? 0x1000
                    : (phase & 0x80) != 0 ? LogSin[((phase ^ 0xff) << 1) & 0xff] : LogSin[(phase << 1) & 0xff];
                return CalcExp(outv + (env << 3));
            }
            case 6:
            {
                int neg = (phase & 0x200) != 0 ? 0xffff : 0;
                return S16(CalcExp(env << 3) ^ neg);
            }
            default:
            {
                int neg = 0;
                if ((phase & 0x200) != 0) { neg = 0xffff; phase = (phase & 0x1ff) ^ 0x1ff; }
                return S16(CalcExp((phase << 3) + (env << 3)) ^ neg);
            }
        }
    }

    /// <summary>One operator. Its output is read by whatever the channel's algorithm points at it.</summary>
    private sealed class Slot
    {
        public Opl3 Chip;
        public int SlotNum;
        public Channel Channel;
        public int Out, Fbmod, Prout;
        public Slot ModSrc;          // null stands for the chip's zeromod
        public bool ModFb;
        public int EgRout = 0x1ff, EgOut = 0x1ff, EgGen = EgRelease, EgKsl;
        public bool TremOn;
        public int RegVib, RegType, RegKsr, RegMult, RegKsl, RegTl, RegAr, RegDr, RegSl, RegRr, RegWf;
        public int Key, PgReset;
        public uint PgPhase;
        public int PgPhaseOut;

        public void UpdateKsl()
        {
            int ksl = (Opl3.Ksl[Channel.FNum >> 6] << 2) - ((8 - Channel.Block) << 5);
            EgKsl = ksl < 0 ? 0 : ksl;
        }

        public void EnvelopeCalc()
        {
            var chip = Chip;
            EgOut = (EgRout + (RegTl << 2) + (EgKsl >> KslShift[RegKsl]) + (TremOn ? chip.Tremolo : 0)) & 0xffff;
            int regRate = 0;
            int reset = 0;
            if (Key != 0 && EgGen == EgRelease)
            {
                reset = 1;
                regRate = RegAr;
            }
            else
            {
                switch (EgGen)
                {
                    case EgAttack: regRate = RegAr; break;
                    case EgDecay: regRate = RegDr; break;
                    case EgSustain: if (RegType == 0) regRate = RegRr; break;
                    default: regRate = RegRr; break;
                }
            }
            PgReset = reset;
            int ks = Channel.Ksv >> ((RegKsr ^ 1) << 1);
            bool nonzero = regRate != 0;
            int rate = ks + (regRate << 2);
            int rateHi = rate >> 2;
            int rateLo = rate & 3;
            if ((rateHi & 0x10) != 0) rateHi = 0x0f;
            int egShift = rateHi + chip.EgAdd;
            int shift = 0;
            if (nonzero)
            {
                if (rateHi < 12)
                {
                    if (chip.EgState != 0)
                    {
                        if (egShift == 12) shift = 1;
                        else if (egShift == 13) shift = (rateLo >> 1) & 1;
                        else if (egShift == 14) shift = rateLo & 1;
                    }
                }
                else
                {
                    shift = (rateHi & 3) + EgIncStep[rateLo][chip.EgTimerLo];
                    if ((shift & 4) != 0) shift = 3;
                    if (shift == 0) shift = chip.EgState;
                }
            }
            int egRout = EgRout;
            int egInc = 0;
            int egOff = 0;
            if (reset != 0 && rateHi == 0x0f) egRout = 0;
            if ((EgRout & 0x1f8) == 0x1f8) egOff = 1;
            if (EgGen != EgAttack && reset == 0 && egOff != 0) egRout = 0x1ff;
            switch (EgGen)
            {
                case EgAttack:
                    if (EgRout == 0) EgGen = EgDecay;
                    else if (Key != 0 && shift > 0 && rateHi != 0x0f) egInc = (~EgRout) >> (4 - shift);
                    break;
                case EgDecay:
                    if ((EgRout >> 4) == RegSl) EgGen = EgSustain;
                    else if (egOff == 0 && reset == 0 && shift > 0) egInc = 1 << (shift - 1);
                    break;
                default:
                    if (egOff == 0 && reset == 0 && shift > 0) egInc = 1 << (shift - 1);
                    break;
            }
            EgRout = (egRout + egInc) & 0x1ff;
            if (reset != 0) EgGen = EgAttack;
            if (Key == 0) EgGen = EgRelease;
        }

        public void PhaseGenerate()
        {
            var chip = Chip;
            int fNum = Channel.FNum;
            if (RegVib != 0)
            {
                int range = (fNum >> 7) & 7;
                int vibpos = chip.Vibpos;
                if ((vibpos & 3) == 0) range = 0;
                else if ((vibpos & 1) != 0) range >>= 1;
                range >>= chip.Vibshift;
                if ((vibpos & 4) != 0) range = -range;
                fNum += range;
            }
            int basefreq = (fNum << Channel.Block) >> 1;
            int phase = (int)((PgPhase >> 9) & 0xffff);
            if (PgReset != 0) PgPhase = 0;
            PgPhase = (uint)(PgPhase + (uint)((basefreq * Mt[RegMult]) >> 1));
            uint noise = chip.Noise;
            PgPhaseOut = phase;
            if (SlotNum == 13)
            {
                chip.RmHhBit2 = (phase >> 2) & 1;
                chip.RmHhBit3 = (phase >> 3) & 1;
                chip.RmHhBit7 = (phase >> 7) & 1;
                chip.RmHhBit8 = (phase >> 8) & 1;
            }
            if (SlotNum == 17 && (chip.Rhy & 0x20) != 0)
            {
                chip.RmTcBit3 = (phase >> 3) & 1;
                chip.RmTcBit5 = (phase >> 5) & 1;
            }
            if ((chip.Rhy & 0x20) != 0)
            {
                int rmXor = (chip.RmHhBit2 ^ chip.RmHhBit7) | (chip.RmHhBit3 ^ chip.RmTcBit5) | (chip.RmTcBit3 ^ chip.RmTcBit5);
                switch (SlotNum)
                {
                    case 13:
                        PgPhaseOut = rmXor << 9;
                        PgPhaseOut |= (rmXor ^ (int)(noise & 1)) != 0 ? 0xd0 : 0x34;
                        break;
                    case 16:
                        PgPhaseOut = (chip.RmHhBit8 << 9) | ((chip.RmHhBit8 ^ (int)(noise & 1)) << 8);
                        break;
                    case 17:
                        PgPhaseOut = (rmXor << 9) | 0x80;
                        break;
                }
            }
            uint nBit = ((noise >> 14) ^ noise) & 1;
            chip.Noise = (noise >> 1) | (nBit << 22);
        }

        public void Generate()
        {
            int mod = ModSrc == null ? 0 : ModFb ? ModSrc.Fbmod : ModSrc.Out;
            Out = Wave(RegWf, (PgPhaseOut + mod) & 0xffff, EgOut);
        }

        public void CalcFb()
        {
            Fbmod = Channel.Fb != 0 ? S16((Prout + Out) >> (9 - Channel.Fb)) : 0;
            Prout = Out;
        }

        public void Process()
        {
            CalcFb();
            EnvelopeCalc();
            PhaseGenerate();
            Generate();
        }
    }

    /// <summary>A channel: two operators, and the algorithm that wires them together.</summary>
    private sealed class Channel
    {
        public Opl3 Chip;
        public int ChNum;
        public readonly Slot[] Slots = new Slot[2];
        public Channel Pair;
        public Slot[] Out = new Slot[4];      // null stands for the chip's zeromod
        public int Chtype = Ch2Op;
        public int FNum, FNumReg, Block, BlockReg, Fb, Con, Alg, Ksv;
        public int Cha = 0xffff, Chb = 0xffff, Chc, Chd;

        public void UpdateFrequency()
        {
            Ksv = (Block << 1) | ((FNum >> (9 - Chip.Nts)) & 1);
            Slots[0].UpdateKsl();
            Slots[1].UpdateKsl();
        }

        public void RestoreFrequency()
        {
            FNum = FNumReg;
            Block = BlockReg;
            UpdateFrequency();
        }

        public void Sync4Op()
        {
            Pair.FNum = FNum;
            Pair.Block = Block;
            Pair.UpdateFrequency();
        }

        public void WriteA0(int data)
        {
            FNumReg = (FNumReg & 0x300) | data;
            if (Chtype == Ch4Op2) return;
            RestoreFrequency();
            if (Chtype == Ch4Op) Sync4Op();
        }

        public void WriteB0(int data)
        {
            FNumReg = (FNumReg & 0xff) | ((data & 3) << 8);
            BlockReg = (data >> 2) & 7;
            if (Chtype == Ch4Op2) return;
            RestoreFrequency();
            if (Chtype == Ch4Op) Sync4Op();
        }

        private static void SetMod(Slot slot, Slot src, bool fb)
        {
            slot.ModSrc = src;
            slot.ModFb = fb;
        }

        public void SetupAlg()
        {
            var s0 = Slots[0];
            var s1 = Slots[1];
            if (Chtype == ChDrum)
            {
                if (ChNum == 7 || ChNum == 8)
                {
                    SetMod(s0, null, false);
                    SetMod(s1, null, false);
                    return;
                }
                SetMod(s0, s0, true);
                SetMod(s1, (Alg & 1) != 0 ? null : s0, false);
                return;
            }
            if ((Alg & 8) != 0) return;
            if ((Alg & 4) != 0)
            {
                var pair = Pair;
                var p0 = pair.Slots[0];
                var p1 = pair.Slots[1];
                pair.Out = new Slot[4];
                switch (Alg & 3)
                {
                    case 0:
                        SetMod(p0, p0, true); SetMod(p1, p0, false); SetMod(s0, p1, false); SetMod(s1, s0, false);
                        Out = new[] { s1, null, null, null };
                        break;
                    case 1:
                        SetMod(p0, p0, true); SetMod(p1, p0, false); SetMod(s0, null, false); SetMod(s1, s0, false);
                        Out = new[] { p1, s1, null, null };
                        break;
                    case 2:
                        SetMod(p0, p0, true); SetMod(p1, null, false); SetMod(s0, p1, false); SetMod(s1, s0, false);
                        Out = new[] { p0, s1, null, null };
                        break;
                    default:
                        SetMod(p0, p0, true); SetMod(p1, null, false); SetMod(s0, p1, false); SetMod(s1, null, false);
                        Out = new[] { p0, s0, s1, null };
                        break;
                }
            }
            else if ((Alg & 1) != 0)
            {
                SetMod(s0, s0, true); SetMod(s1, null, false);
                Out = new[] { s0, s1, null, null };
            }
            else
            {
                SetMod(s0, s0, true); SetMod(s1, s0, false);
                Out = new[] { s1, null, null, null };
            }
        }

        public void UpdateAlg()
        {
            Alg = Con;
            if (Chtype == Ch4Op)
            {
                Pair.Alg = 4 | (Con << 1) | Pair.Con;
                Alg = 8;
                Pair.SetupAlg();
            }
            else if (Chtype == Ch4Op2)
            {
                Alg = 4 | (Pair.Con << 1) | Con;
                Pair.Alg = 8;
                SetupAlg();
            }
            else SetupAlg();
        }

        public void WriteC0(int data)
        {
            Fb = (data & 0x0e) >> 1;
            Con = data & 1;
            UpdateAlg();
            if (Chip.Newm != 0)
            {
                Cha = ((data >> 4) & 1) != 0 ? 0xffff : 0;
                Chb = ((data >> 5) & 1) != 0 ? 0xffff : 0;
                Chc = ((data >> 6) & 1) != 0 ? 0xffff : 0;
                Chd = ((data >> 7) & 1) != 0 ? 0xffff : 0;
            }
            else
            {
                Cha = Chb = 0xffff;
                Chc = Chd = 0;
            }
        }

        public void KeyOn()
        {
            if (Chtype == Ch4Op)
            {
                Slots[0].Key |= EgkNorm; Slots[1].Key |= EgkNorm;
                Pair.Slots[0].Key |= EgkNorm; Pair.Slots[1].Key |= EgkNorm;
            }
            else if (Chtype == Ch2Op || Chtype == ChDrum)
            {
                Slots[0].Key |= EgkNorm; Slots[1].Key |= EgkNorm;
            }
        }

        public void KeyOff()
        {
            if (Chtype == Ch4Op)
            {
                Slots[0].Key &= ~EgkNorm; Slots[1].Key &= ~EgkNorm;
                Pair.Slots[0].Key &= ~EgkNorm; Pair.Slots[1].Key &= ~EgkNorm;
            }
            else if (Chtype == Ch2Op || Chtype == ChDrum)
            {
                Slots[0].Key &= ~EgkNorm; Slots[1].Key &= ~EgkNorm;
            }
        }
    }

    private readonly Slot[] _slot = new Slot[36];
    private readonly Channel[] _channel = new Channel[18];

    private int Timer;
    private long EgTimer;
    private int EgTimerRem, EgState, EgAdd, EgTimerLo;
    private int Newm, Nts, Rhy;
    private int Vibpos, Vibshift = 1;
    private int Tremolo, Tremolopos, Tremoloshift = 4;
    private uint Noise = 1;
    private readonly int[] _mixbuff = new int[2];
    private int RmHhBit2, RmHhBit3, RmHhBit7, RmHhBit8, RmTcBit3, RmTcBit5;

    private readonly int _rateratio;
    private int _samplecnt;
    private readonly int[] _oldsamples = new int[2];
    private readonly int[] _samples = new int[2];

    public Opl3(int sampleRate)
    {
        for (int i = 0; i < 36; i += 1) _slot[i] = new Slot { Chip = this, SlotNum = i };
        for (int i = 0; i < 18; i += 1) _channel[i] = new Channel { Chip = this, ChNum = i };
        for (int i = 0; i < 18; i += 1)
        {
            var ch = _channel[i];
            int slotBase = ChSlot[i];
            ch.Slots[0] = _slot[slotBase];
            ch.Slots[1] = _slot[slotBase + 3];
            _slot[slotBase].Channel = ch;
            _slot[slotBase + 3].Channel = ch;
            if (i % 9 < 3) ch.Pair = _channel[i + 3];
            else if (i % 9 < 6) ch.Pair = _channel[i - 3];
            ch.SetupAlg();
        }
        _rateratio = (sampleRate << 10) / 49716;
    }

    public void UpdateRhythm(int data)
    {
        Rhy = data & 0x3f;
        var c6 = _channel[6];
        var c7 = _channel[7];
        var c8 = _channel[8];
        if ((Rhy & 0x20) != 0)
        {
            c6.Out = new[] { c6.Slots[1], c6.Slots[1], null, null };
            c7.Out = new[] { c7.Slots[0], c7.Slots[0], c7.Slots[1], c7.Slots[1] };
            c8.Out = new[] { c8.Slots[0], c8.Slots[0], c8.Slots[1], c8.Slots[1] };
            foreach (var ch in new[] { c6, c7, c8 }) { ch.Chtype = ChDrum; ch.SetupAlg(); }
            void Key(Slot slot, bool on) { if (on) slot.Key |= EgkDrum; else slot.Key &= ~EgkDrum; }
            Key(c7.Slots[0], (Rhy & 1) != 0);    // hh
            Key(c8.Slots[1], (Rhy & 2) != 0);    // tc
            Key(c8.Slots[0], (Rhy & 4) != 0);    // tom
            Key(c7.Slots[1], (Rhy & 8) != 0);    // sd
            Key(c6.Slots[0], (Rhy & 16) != 0);
            Key(c6.Slots[1], (Rhy & 16) != 0);   // bd
        }
        else
        {
            foreach (var ch in new[] { c6, c7, c8 })
            {
                ch.Chtype = Ch2Op;
                ch.SetupAlg();
                ch.Slots[0].Key &= ~EgkDrum;
                ch.Slots[1].Key &= ~EgkDrum;
            }
        }
    }

    public void Set4Op(int data)
    {
        for (int bit = 0; bit < 6; bit += 1)
        {
            int chnum = bit < 3 ? bit : bit + 6;
            var ch = _channel[chnum];
            var ch2 = _channel[chnum + 3];
            if (((data >> bit) & 1) != 0)
            {
                ch.Chtype = Ch4Op;
                ch2.Chtype = Ch4Op2;
                ch.Sync4Op();
                ch.UpdateAlg();
            }
            else
            {
                ch.Chtype = Ch2Op;
                ch2.Chtype = Ch2Op;
                ch2.RestoreFrequency();
                ch.UpdateAlg();
                ch2.UpdateAlg();
            }
        }
    }

    /// <summary>A register write, exactly as the driver makes it.</summary>
    public void WriteReg(int reg, int v)
    {
        int high = (reg >> 8) & 1;
        int regm = reg & 0xff;
        Slot SlotOf()
        {
            int s = AdSlot[regm & 0x1f];
            return s >= 0 ? _slot[18 * high + s] : null;
        }
        switch (regm & 0xf0)
        {
            case 0x00:
                if (high != 0)
                {
                    if ((regm & 0x0f) == 4) Set4Op(v);
                    else if ((regm & 0x0f) == 5) Newm = v & 1;
                }
                else if ((regm & 0x0f) == 8) Nts = (v >> 6) & 1;
                break;
            case 0x20: case 0x30:
            {
                var s = SlotOf();
                if (s == null) break;
                s.TremOn = ((v >> 7) & 1) != 0;
                s.RegVib = (v >> 6) & 1;
                s.RegType = (v >> 5) & 1;
                s.RegKsr = (v >> 4) & 1;
                s.RegMult = v & 0x0f;
                break;
            }
            case 0x40: case 0x50:
            {
                var s = SlotOf();
                if (s == null) break;
                s.RegKsl = (v >> 6) & 3;
                s.RegTl = v & 0x3f;
                s.UpdateKsl();
                break;
            }
            case 0x60: case 0x70:
            {
                var s = SlotOf();
                if (s == null) break;
                s.RegAr = (v >> 4) & 0x0f;
                s.RegDr = v & 0x0f;
                break;
            }
            case 0x80: case 0x90:
            {
                var s = SlotOf();
                if (s == null) break;
                s.RegSl = (v >> 4) & 0x0f;
                if (s.RegSl == 0x0f) s.RegSl = 0x1f;
                s.RegRr = v & 0x0f;
                break;
            }
            case 0xe0: case 0xf0:
            {
                var s = SlotOf();
                if (s == null) break;
                s.RegWf = v & 7;
                if (Newm == 0) s.RegWf &= 3;
                break;
            }
            case 0xa0:
                if ((regm & 0x0f) < 9) _channel[9 * high + (regm & 0x0f)].WriteA0(v);
                break;
            case 0xb0:
                if (regm == 0xbd && high == 0)
                {
                    Tremoloshift = ((((v >> 7) ^ 1) << 1) + 2);
                    Vibshift = ((v >> 6) & 1) ^ 1;
                    UpdateRhythm(v);
                }
                else if ((regm & 0x0f) < 9)
                {
                    var ch = _channel[9 * high + (regm & 0x0f)];
                    ch.WriteB0(v);
                    if ((v & 0x20) != 0) ch.KeyOn(); else ch.KeyOff();
                }
                break;
            case 0xc0:
                if ((regm & 0x0f) < 9) _channel[9 * high + (regm & 0x0f)].WriteC0(v);
                break;
        }
    }

    private static int SlotOut(Slot s) => s?.Out ?? 0;

    /// <summary>One chip sample at 49716 Hz. Returns the left mix; the right lags one sample.</summary>
    private int GenerateOne()
    {
        for (int i = 0; i < 15; i += 1) _slot[i].Process();
        int mixL = 0;
        for (int i = 0; i < 18; i += 1)
        {
            var ch = _channel[i];
            int accm = S16(SlotOut(ch.Out[0]) + SlotOut(ch.Out[1]) + SlotOut(ch.Out[2]) + SlotOut(ch.Out[3]));
            mixL += S16(accm & ch.Cha);
        }
        _mixbuff[0] = mixL;
        for (int i = 15; i < 18; i += 1) _slot[i].Process();
        int outL = _mixbuff[0];
        for (int i = 18; i < 33; i += 1) _slot[i].Process();
        int mixR = 0;
        for (int i = 0; i < 18; i += 1)
        {
            var ch = _channel[i];
            int accm = S16(SlotOut(ch.Out[0]) + SlotOut(ch.Out[1]) + SlotOut(ch.Out[2]) + SlotOut(ch.Out[3]));
            mixR += S16(accm & ch.Chb);
        }
        _mixbuff[1] = mixR;
        for (int i = 33; i < 36; i += 1) _slot[i].Process();

        if ((Timer & 0x3f) == 0x3f) Tremolopos = (Tremolopos + 1) % 210;
        Tremolo = Tremolopos < 105 ? Tremolopos >> Tremoloshift : (210 - Tremolopos) >> Tremoloshift;
        if ((Timer & 0x3ff) == 0x3ff) Vibpos = (Vibpos + 1) & 7;
        Timer = (Timer + 1) & 0xffff;
        if (EgState != 0)
        {
            int shift = 0;
            while (shift < 13 && ((EgTimer >> shift) & 1) == 0) shift += 1;
            EgAdd = shift > 12 ? 0 : shift + 1;
            EgTimerLo = (int)(EgTimer & 3);
        }
        if (EgTimerRem != 0 || EgState != 0)
        {
            if (EgTimer == 0xfffffffffL)
            {
                EgTimer = 0;
                EgTimerRem = 1;
            }
            else
            {
                EgTimer += 1;
                EgTimerRem = 0;
            }
        }
        EgState ^= 1;
        return outL;
    }

    private static int Clip(int v) => v > 32767 ? 32767 : v < -32768 ? -32768 : v;

    /// <summary>Fills `left` (and `right`, if given) with `n` frames at the chosen sample rate.</summary>
    public void Generate(float[] left, float[] right, int n)
    {
        int rr = _rateratio;
        for (int i = 0; i < n; i += 1)
        {
            while (_samplecnt >= rr)
            {
                _oldsamples[0] = _samples[0];
                _oldsamples[1] = _samples[1];
                int prevRight = _mixbuff[1];   // the chip puts the right side out one sample late
                GenerateOne();
                _samples[0] = Clip(_mixbuff[0]);
                _samples[1] = Clip(prevRight);
                _samplecnt -= rr;
            }
            int l = (_oldsamples[0] * (rr - _samplecnt) + _samples[0] * _samplecnt) / rr;
            int r = (_oldsamples[1] * (rr - _samplecnt) + _samples[1] * _samplecnt) / rr;
            left[i] = l / 32768f;
            if (right != null) right[i] = r / 32768f;
            _samplecnt += 1 << 10;
        }
    }
}
