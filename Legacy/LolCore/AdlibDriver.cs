// The Westwood AdLib driver: what turns an .ADL file into register writes on the OPL chip.
//
// Transliterated from src/platform/adlib-driver.mjs, itself a port of ScummVM's Kyra AdLibDriver
// (GPLv3), version 4 - the Lands of Lore CD build. An .ADL file is a 500-byte track table followed
// by programs and instruments addressed through a 16-bit offset table, and the driver's callback
// runs 72 times a second whatever the sample rate is.
//
// The parser is a little byte-code machine with its own stack, its own tempo per channel, and two
// effect slots (a pitch slide or vibrato, plus a register sweep). All of it is 8- and 16-bit
// arithmetic that wraps, so every step is masked the way the original does.
namespace LolCore;

public sealed class AdLibDriver
{
    public const int CallbacksPerSecond = 72;

    private static readonly int[] RegOffset = { 0x00, 0x01, 0x02, 0x08, 0x09, 0x0a, 0x10, 0x11, 0x12 };
    private static readonly int[] FreqTable = { 0x0134, 0x0147, 0x015a, 0x016f, 0x0184, 0x019c, 0x01b4, 0x01ce, 0x01e9, 0x0207, 0x0225, 0x0246 };

    private static readonly int[] Table21 = BuildTable21();
    private static readonly int[] Table22 = BuildTable22();
    private static readonly int[] Table23 = BuildTable23();
    private static readonly int[][] Table2 = { Table21, Table22, Table21, Table22, Table23, Table22 };

    private static int[] BuildTable21()
    {
        // 0x50 down to 0x10, each value twice.
        var t = new int[130];
        for (int i = 0; i < 130; i += 1) t[i] = 0x50 - (i >> 1);
        return t;
    }

    private static int[] BuildTable22()
    {
        var t = new int[128];
        for (int i = 0; i < 128; i += 1) t[i] = i == 0x5f ? 0x6f : i;
        return t;
    }

    private static int[] BuildTable23()
    {
        // 0x40 down to 0x15, each value three times.
        var t = new int[130];
        for (int i = 0; i < 130; i += 1) t[i] = 0x40 - (i / 3);
        return t;
    }

    private static readonly int[][] PitchBend =
    {
        new[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20, 0x21 },
        new[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x06, 0x07, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20, 0x22, 0x24 },
        new[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x06, 0x08, 0x09, 0x0a, 0x0c, 0x0d, 0x0e, 0x0f, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x19, 0x1a, 0x1c, 0x1d, 0x1e, 0x1f, 0x20, 0x21, 0x22, 0x24, 0x25, 0x26 },
        new[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x06, 0x08, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x1a, 0x1c, 0x1d, 0x1e, 0x1f, 0x20, 0x21, 0x23, 0x25, 0x27, 0x28 },
        new[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x06, 0x08, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x11, 0x13, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1b, 0x1d, 0x1f, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x28, 0x2a },
        new[] { 0x00, 0x01, 0x02, 0x03, 0x05, 0x07, 0x09, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x13, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1b, 0x1d, 0x1f, 0x20, 0x21, 0x22, 0x23, 0x25, 0x27, 0x29, 0x2b, 0x2d },
        new[] { 0x00, 0x01, 0x02, 0x03, 0x05, 0x07, 0x09, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x13, 0x15, 0x16, 0x17, 0x18, 0x1a, 0x1c, 0x1e, 0x21, 0x24, 0x25, 0x26, 0x27, 0x29, 0x2b, 0x2d, 0x2f, 0x30 },
        new[] { 0x00, 0x01, 0x02, 0x04, 0x06, 0x08, 0x0a, 0x0c, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x13, 0x15, 0x18, 0x19, 0x1a, 0x1c, 0x1d, 0x1f, 0x21, 0x23, 0x25, 0x26, 0x27, 0x29, 0x2b, 0x2d, 0x2f, 0x30, 0x32 },
        new[] { 0x00, 0x01, 0x02, 0x04, 0x06, 0x08, 0x0a, 0x0d, 0x0e, 0x0f, 0x10, 0x11, 0x12, 0x14, 0x17, 0x1a, 0x19, 0x1a, 0x1c, 0x1e, 0x20, 0x22, 0x25, 0x28, 0x29, 0x2a, 0x2b, 0x2d, 0x2f, 0x31, 0x33, 0x35 },
        new[] { 0x00, 0x01, 0x03, 0x05, 0x07, 0x09, 0x0b, 0x0e, 0x0f, 0x10, 0x12, 0x14, 0x16, 0x18, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x20, 0x22, 0x24, 0x26, 0x29, 0x2a, 0x2c, 0x2e, 0x30, 0x32, 0x34, 0x36, 0x39 },
        new[] { 0x00, 0x01, 0x03, 0x05, 0x07, 0x09, 0x0b, 0x0e, 0x0f, 0x10, 0x12, 0x14, 0x16, 0x19, 0x1b, 0x1e, 0x1f, 0x21, 0x23, 0x25, 0x27, 0x29, 0x2b, 0x2d, 0x2e, 0x2f, 0x31, 0x32, 0x34, 0x36, 0x39, 0x3c },
        new[] { 0x00, 0x01, 0x03, 0x05, 0x07, 0x0a, 0x0c, 0x0f, 0x10, 0x11, 0x13, 0x15, 0x17, 0x19, 0x1b, 0x1e, 0x1f, 0x20, 0x22, 0x24, 0x26, 0x28, 0x2b, 0x2e, 0x2f, 0x30, 0x32, 0x34, 0x36, 0x39, 0x3c, 0x3f },
        new[] { 0x00, 0x02, 0x04, 0x06, 0x08, 0x0b, 0x0d, 0x10, 0x11, 0x12, 0x14, 0x16, 0x18, 0x1b, 0x1e, 0x21, 0x22, 0x23, 0x25, 0x27, 0x29, 0x2c, 0x2f, 0x32, 0x33, 0x34, 0x36, 0x38, 0x3b, 0x34, 0x41, 0x44 },
        new[] { 0x00, 0x02, 0x04, 0x06, 0x08, 0x0b, 0x0d, 0x11, 0x12, 0x13, 0x15, 0x17, 0x1a, 0x1d, 0x20, 0x23, 0x24, 0x25, 0x27, 0x29, 0x2c, 0x2f, 0x32, 0x35, 0x36, 0x37, 0x39, 0x3b, 0x3e, 0x41, 0x44, 0x47 },
    };

    private static int Clip(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    private static int U8(int v) => v & 0xff;
    private static int I8(int v) => (sbyte)v;
    private static int I16(int v) => (short)v;

    private enum Effect { None, Slide, Vibrato, Secondary1 }

    private sealed class Chan
    {
        public bool Lock;
        public int OpExtraLevel2;
        public int Dataptr = -1;
        public int Duration, RepeatCounter, BaseOctave, Priority;
        public readonly int[] DataptrStack = new int[4];
        public int DataptrStackPos, BaseNote, SlideTempo, SlideTimer, SlideStep;
        public int VibratoStep, VibratoStepRange, VibratoStepsCountdown, VibratoNumSteps, VibratoDelay;
        public int VibratoTempo, VibratoTimer, VibratoDelayCountdown, OpExtraLevel1, Spacing2, BaseFreq;
        public int Tempo = 0xff, Timer, RegAx, RegBx;
        public Effect PrimaryEffect, SecondaryEffect;
        public int FractionalSpacing, OpLevel1, OpLevel2, OpExtraLevel3, TwoChan, Unk39, Unk40;
        public int Spacing1 = 1, DurationRandomness;
        public int SecondaryEffectTempo, SecondaryEffectTimer, SecondaryEffectSize, SecondaryEffectPos;
        public int SecondaryEffectRegbase, SecondaryEffectData, TempoReset, RawNote, PitchBend, VolumeModifier;

        /// <summary>initChannel: everything but the extra level the engine keeps across a reset.</summary>
        public void Reset()
        {
            int backup = OpExtraLevel2;
            Lock = false;
            OpExtraLevel2 = 0;
            Dataptr = -1;
            Duration = RepeatCounter = BaseOctave = Priority = 0;
            Array.Clear(DataptrStack, 0, DataptrStack.Length);
            DataptrStackPos = BaseNote = SlideTempo = SlideTimer = SlideStep = 0;
            VibratoStep = VibratoStepRange = VibratoStepsCountdown = VibratoNumSteps = VibratoDelay = 0;
            VibratoTempo = VibratoTimer = VibratoDelayCountdown = OpExtraLevel1 = Spacing2 = BaseFreq = 0;
            Tempo = 0xff;
            Timer = RegAx = RegBx = 0;
            PrimaryEffect = SecondaryEffect = Effect.None;
            FractionalSpacing = OpLevel1 = OpLevel2 = OpExtraLevel3 = TwoChan = Unk39 = Unk40 = 0;
            Spacing1 = 1;
            DurationRandomness = 0;
            SecondaryEffectTempo = SecondaryEffectTimer = SecondaryEffectSize = SecondaryEffectPos = 0;
            SecondaryEffectRegbase = SecondaryEffectData = TempoReset = RawNote = PitchBend = VolumeModifier = 0;
            OpExtraLevel2 = backup;
        }
    }

    private sealed class QueueEntry { public int Data = -1; public int Id; public int Volume; }

    private readonly Opl3 _opl;
    private readonly int _version;
    private readonly int _numPrograms;
    private byte[] _soundData;
    private int _soundDataSize;
    private readonly Chan[] _channels = Enumerable.Range(0, 10).Select(_ => new Chan()).ToArray();
    private int _curChannel;
    private int _soundTrigger;
    private int _rnd = 0x1234;
    private int _beatDivider, _beatDivCnt, _beatCounter, _beatWaiting;
    private int _callbackTimer = 0xff;
    private int _opLevelBD, _opLevelHH, _opLevelSD, _opLevelTT, _opLevelCY;
    private int _opExtraLevel1HH, _opExtraLevel2HH, _opExtraLevel1CY, _opExtraLevel2CY;
    private int _opExtraLevel2TT, _opExtraLevel1TT, _opExtraLevel1SD, _opExtraLevel2SD;
    private int _opExtraLevel1BD, _opExtraLevel2BD;
    private readonly QueueEntry[] _programQueue = Enumerable.Range(0, 16).Select(_ => new QueueEntry()).ToArray();
    private int _programStartTimeout, _programQueueStart, _programQueueEnd;
    private bool _retrySounds;
    private int _sfxPointer = -1, _sfxPriority, _sfxVelocity;
    private int _vibratoAndAMDepthBits, _rhythmSectionBits, _curRegOffset, _tempo;
    private int[] _tablePtr1, _tablePtr2;
    private int _syncJumpMask;
    private int _musicVolume, _sfxVolume;

    public AdLibDriver(Opl3 opl, int version = 4)
    {
        _opl = opl;
        _version = version;
        _numPrograms = version == 1 ? 150 : version == 4 ? 500 : 250;
    }

    private void WriteOpl(int reg, int val) => _opl.WriteReg(reg & 0xff, val & 0xff);

    // ---- data access: offsets into the sound data, -1 for null ----
    private int CheckDataOffset(int ptr, int n)
    {
        if (ptr < 0) return -1;
        if (n >= -ptr && n <= _soundDataSize - ptr) return ptr + n;
        return -1;
    }

    private int GetProgram(int progId)
    {
        if (_soundData == null || progId < 0 || progId >= (_soundDataSize >> 1)) return -1;
        int offset = _soundData[2 * progId] | (_soundData[2 * progId + 1] << 8);
        if (offset == 0 || offset >= _soundDataSize) return -1;
        return offset;
    }

    private int GetInstrument(int id) => GetProgram(_numPrograms + id);

    // ---- public API ----
    public void InitDriver() => ResetAdLibState();

    public void SetSoundData(byte[] data)
    {
        _programQueueStart = _programQueueEnd = 0;
        foreach (var e in _programQueue) { e.Data = -1; e.Id = 0; e.Volume = 0; }
        _sfxPointer = -1;
        _soundData = data;
        _soundDataSize = data?.Length ?? 0;
    }

    public void StartSound(int track, int volume)
    {
        int trackData = GetProgram(track);
        if (trackData < 0) return;
        var e = _programQueue[_programQueueEnd];
        e.Data = trackData;
        e.Id = track;
        e.Volume = volume;
        _programQueueEnd = (_programQueueEnd + 1) & 15;
    }

    public bool IsChannelPlaying(int channel) => _channels[channel].Dataptr >= 0;

    public void StopAllChannels()
    {
        for (int channel = 0; channel <= 9; channel += 1)
        {
            _curChannel = channel;
            var chan = _channels[channel];
            chan.Priority = 0;
            chan.Dataptr = -1;
            if (channel != 9) NoteOff(chan);
        }
        _retrySounds = false;
        _programQueueStart = _programQueueEnd = 0;
        _programQueue[0].Data = -1;
        _programStartTimeout = 0;
    }

    public void SetMusicVolume(int volume)
    {
        _musicVolume = volume;
        for (int i = 0; i < 6; i += 1)
        {
            var chan = _channels[i];
            chan.VolumeModifier = volume;
            int regOffset = RegOffset[i];
            WriteOpl(0x40 + regOffset, CalculateOpLevel1(chan));
            WriteOpl(0x43 + regOffset, CalculateOpLevel2(chan));
        }
        if (_version < 4) SetSfxVolume(volume);
    }

    public void SetSfxVolume(int volume)
    {
        _sfxVolume = volume;
        for (int i = 6; i < 9; i += 1)
        {
            var chan = _channels[i];
            chan.VolumeModifier = volume;
            int regOffset = RegOffset[i];
            WriteOpl(0x40 + regOffset, CalculateOpLevel1(chan));
            WriteOpl(0x43 + regOffset, CalculateOpLevel2(chan));
        }
    }

    // ---- the 72 Hz callback ----
    public void Callback()
    {
        if (_programStartTimeout != 0) _programStartTimeout -= 1;
        else SetupPrograms();
        ExecutePrograms();
        int old = _callbackTimer;
        _callbackTimer = U8(old + _tempo);
        if (_callbackTimer < old)
        {
            _beatDivCnt = U8(_beatDivCnt - 1);
            if (_beatDivCnt == 0)
            {
                _beatDivCnt = _beatDivider;
                _beatCounter = U8(_beatCounter + 1);
            }
        }
    }

    private void SetupPrograms()
    {
        var entry = _programQueue[_programQueueStart];
        int ptr = entry.Data;
        if (_programQueueStart == _programQueueEnd && ptr < 0) return;
        (int Id, int Volume)? retrySound = null;
        if (entry.Id == 0) _retrySounds = true;
        else if (_retrySounds) retrySound = (entry.Id, entry.Volume);
        entry.Data = -1;
        _programQueueStart = (_programQueueStart + 1) & 15;
        if (CheckDataOffset(ptr, 2) < 0) return;
        int chanNum = _soundData[ptr];
        if (chanNum > 9 || (chanNum < 9 && CheckDataOffset(ptr, 4) < 0)) return;
        var channel = _channels[chanNum];
        AdjustSfxData(ptr, entry.Volume);
        ptr += 1;
        int priority = _soundData[ptr];
        ptr += 1;
        if (priority >= channel.Priority)
        {
            channel.Reset();
            channel.Priority = priority;
            channel.Dataptr = ptr;
            channel.Tempo = 0xff;
            channel.Timer = 0xff;
            channel.Duration = 1;
            channel.VolumeModifier = chanNum <= 5 ? _musicVolume : _sfxVolume;
            InitAdlibChannel(chanNum);
            _programStartTimeout = 2;
            retrySound = null;
        }
        if (retrySound.HasValue) StartSound(retrySound.Value.Id, retrySound.Value.Volume);
    }

    private void AdjustSfxData(int ptr, int volume)
    {
        var d = _soundData;
        if (_sfxPointer >= 0)
        {
            d[_sfxPointer + 1] = (byte)_sfxPriority;
            d[_sfxPointer + 3] = (byte)_sfxVelocity;
            _sfxPointer = -1;
        }
        if (d[ptr] == 9) return;   // a music track, not an effect
        _sfxPointer = ptr;
        _sfxPriority = d[ptr + 1];
        _sfxVelocity = d[ptr + 3];
        if (volume != 0xff)
        {
            if (_version >= 3)
            {
                int newVal = (((d[ptr + 3] + 63) * volume) >> 8) & 0xff;
                d[ptr + 3] = (byte)U8(-newVal + 63);
                d[ptr + 1] = (byte)(((d[ptr + 1] * volume) >> 8) & 0xff);
            }
            else
            {
                int newVal = ((_sfxVelocity << 2) ^ 0xff) * volume;
                d[ptr + 3] = (byte)U8((newVal >> 10) ^ 0x3f);
                d[ptr + 1] = (byte)U8(newVal >> 11);
            }
        }
    }

    private void ExecutePrograms()
    {
        if (_syncJumpMask != 0)
        {
            int c;
            for (c = 9; c >= 0; c -= 1)
                if ((_syncJumpMask & (1 << c)) != 0 && _channels[c].Dataptr >= 0 && !_channels[c].Lock) break;
            if (c < 0) for (c = 9; c >= 0; c -= 1) if ((_syncJumpMask & (1 << c)) != 0) _channels[c].Lock = false;
        }
        for (_curChannel = 9; _curChannel >= 0; _curChannel -= 1)
        {
            var channel = _channels[_curChannel];
            if (channel.Dataptr < 0) continue;
            if (channel.Lock && (_syncJumpMask & (1 << _curChannel)) != 0) continue;
            _curRegOffset = _curChannel == 9 ? 0 : RegOffset[_curChannel];
            if (channel.TempoReset != 0) channel.Tempo = _tempo;
            int result = 1;
            int old = channel.Timer;
            channel.Timer = U8(old + channel.Tempo);
            if (channel.Timer < old)
            {
                channel.Duration = U8(channel.Duration - 1);
                if (channel.Duration != 0)
                {
                    if (channel.Duration == channel.Spacing2) NoteOff(channel);
                    if (channel.Duration == channel.Spacing1 && _curChannel != 9) NoteOff(channel);
                }
                else result = 0;
            }
            while (result == 0 && channel.Dataptr >= 0)
            {
                int opcode = 0xff;
                if (CheckDataOffset(channel.Dataptr, 1) >= 0) opcode = _soundData[channel.Dataptr++];
                if ((opcode & 0x80) != 0)
                {
                    opcode = Clip(opcode & 0x7f, 0, OpcodeArgs.Length - 1);
                    int values = OpcodeArgs[opcode];
                    if (CheckDataOffset(channel.Dataptr, values) < 0)
                    {
                        result = UpdateStopChannel(channel);
                        break;
                    }
                    channel.Dataptr += values;
                    result = RunOpcode(opcode, channel, channel.Dataptr - values);
                }
                else
                {
                    if (CheckDataOffset(channel.Dataptr, 1) < 0)
                    {
                        result = UpdateStopChannel(channel);
                        break;
                    }
                    int duration = _soundData[channel.Dataptr++];
                    SetupNote(opcode, channel);
                    NoteOn(channel);
                    SetupDuration(duration, channel);
                    result = duration != 0 ? 1 : 0;
                }
            }
            if (result == 1)
            {
                if (channel.PrimaryEffect == Effect.Slide) PrimaryEffectSlide(channel);
                else if (channel.PrimaryEffect == Effect.Vibrato) PrimaryEffectVibrato(channel);
                if (channel.SecondaryEffect == Effect.Secondary1) SecondaryEffect1(channel);
            }
        }
    }

    private void ResetAdLibState()
    {
        _rnd = 0x1234;
        WriteOpl(0x01, 0x20);
        WriteOpl(0x08, 0x00);
        WriteOpl(0xbd, 0x00);
        _channels[9].Reset();
        for (int loop = 8; loop >= 0; loop -= 1)
        {
            WriteOpl(0x40 + RegOffset[loop], 0x3f);
            WriteOpl(0x43 + RegOffset[loop], 0x3f);
            _channels[loop].Reset();
        }
    }

    private void NoteOff(Chan channel)
    {
        if (_curChannel >= 9) return;
        if (_rhythmSectionBits != 0 && _curChannel >= 6) return;
        channel.RegBx &= 0xdf;
        WriteOpl(0xb0 + _curChannel, channel.RegBx);
    }

    private void InitAdlibChannel(int chan)
    {
        if (chan >= 9) return;
        if (_rhythmSectionBits != 0 && chan >= 6) return;
        int offset = RegOffset[chan];
        WriteOpl(0x60 + offset, 0xff);
        WriteOpl(0x63 + offset, 0xff);
        WriteOpl(0x80 + offset, 0xff);
        WriteOpl(0x83 + offset, 0xff);
        WriteOpl(0xb0 + chan, 0x00);
        WriteOpl(0xb0 + chan, 0x20);
    }

    private int GetRandomNr()
    {
        _rnd = (_rnd + 0x9248) & 0xffff;
        int lowBits = _rnd & 7;
        _rnd >>= 3;
        _rnd |= lowBits << 13;
        return _rnd;
    }

    private void SetupDuration(int duration, Chan channel)
    {
        if (channel.DurationRandomness != 0)
        {
            channel.Duration = U8(duration + (GetRandomNr() & channel.DurationRandomness));
            return;
        }
        if (channel.FractionalSpacing != 0) channel.Spacing2 = U8((duration >> 3) * channel.FractionalSpacing);
        channel.Duration = duration;
    }

    private void SetupNote(int rawNote, Chan channel, bool flag = false)
    {
        if (_curChannel >= 9) return;
        channel.RawNote = rawNote;
        int note = I8((rawNote & 0x0f) + channel.BaseNote);
        int octave = I8(((rawNote + channel.BaseOctave) >> 4) & 0x0f);
        if (note >= 12)
        {
            octave = I8(octave + note / 12);
            note %= 12;
        }
        else if (note < 0)
        {
            int octaves = -(note + 1) / 12 + 1;
            octave = I8(octave - octaves);
            note += 12 * octaves;
        }
        int freq = (FreqTable[note] + channel.BaseFreq) & 0xffff;
        if (channel.PitchBend != 0 || flag)
        {
            int indexNote = Clip(rawNote & 0x0f, 0, 11);
            if (channel.PitchBend >= 0) freq += PitchBend[indexNote + 2][Clip(channel.PitchBend, 0, 31)];
            else freq -= PitchBend[indexNote][Clip(-channel.PitchBend, 0, 31)];
            freq &= 0xffff;
        }
        channel.RegAx = freq & 0xff;
        channel.RegBx = U8((channel.RegBx & 0x20) | (octave << 2) | ((freq >> 8) & 0x03));
        WriteOpl(0xa0 + _curChannel, channel.RegAx);
        WriteOpl(0xb0 + _curChannel, channel.RegBx);
    }

    private void SetupInstrument(int regOffset, int dataptr, Chan channel)
    {
        if (_curChannel >= 9) return;
        if (CheckDataOffset(dataptr, 11) < 0) return;
        var d = _soundData;
        WriteOpl(0x20 + regOffset, d[dataptr++]);
        WriteOpl(0x23 + regOffset, d[dataptr++]);
        int temp = d[dataptr++];
        WriteOpl(0xc0 + _curChannel, temp);
        channel.TwoChan = temp & 1;
        WriteOpl(0xe0 + regOffset, d[dataptr++]);
        WriteOpl(0xe3 + regOffset, d[dataptr++]);
        channel.OpLevel1 = d[dataptr++];
        channel.OpLevel2 = d[dataptr++];
        WriteOpl(0x40 + regOffset, CalculateOpLevel1(channel));
        WriteOpl(0x43 + regOffset, CalculateOpLevel2(channel));
        WriteOpl(0x60 + regOffset, d[dataptr++]);
        WriteOpl(0x63 + regOffset, d[dataptr++]);
        WriteOpl(0x80 + regOffset, d[dataptr++]);
        WriteOpl(0x83 + regOffset, d[dataptr++]);
    }

    private void NoteOn(Chan channel)
    {
        if (_curChannel >= 9) return;
        channel.RegBx |= 0x20;
        WriteOpl(0xb0 + _curChannel, channel.RegBx);
        int shift = 9 - Clip(channel.VibratoStepRange, 0, 9);
        int freq = ((channel.RegBx << 8) | channel.RegAx) & 0x3ff;
        channel.VibratoStep = (freq >> shift) & 0xff;
        channel.VibratoDelayCountdown = channel.VibratoDelay;
    }

    private void AdjustVolume(Chan channel)
    {
        if (_curChannel >= 9) return;
        WriteOpl(0x43 + RegOffset[_curChannel], CalculateOpLevel2(channel));
        if (channel.TwoChan != 0) WriteOpl(0x40 + RegOffset[_curChannel], CalculateOpLevel1(channel));
    }

    private void PrimaryEffectSlide(Chan channel)
    {
        if (_curChannel >= 9) return;
        int old = channel.SlideTimer;
        channel.SlideTimer = U8(old + channel.SlideTempo);
        if (channel.SlideTimer >= old) return;
        int freq = I16(((channel.RegBx & 0x03) << 8) | channel.RegAx);
        int octave = channel.RegBx & 0x1c;
        int noteOn = channel.RegBx & 0x20;
        freq = I16(freq + Clip(channel.SlideStep, -0x3ff, 0x3ff));
        if (channel.SlideStep >= 0 && freq >= 734)
        {
            freq >>= 1;
            if ((freq & 0x3ff) == 0) freq += 1;
            octave = U8(octave + 4);
        }
        else if (channel.SlideStep < 0 && freq < 388)
        {
            if (freq < 0) freq = 0;
            freq = I16(freq << 1);
            if ((freq & 0x3ff) == 0) freq -= 1;
            octave = U8(octave - 4);
        }
        channel.RegAx = freq & 0xff;
        channel.RegBx = U8(noteOn | (octave & 0x1c) | ((freq >> 8) & 0x03));
        WriteOpl(0xa0 + _curChannel, channel.RegAx);
        WriteOpl(0xb0 + _curChannel, channel.RegBx);
    }

    private void PrimaryEffectVibrato(Chan channel)
    {
        if (_curChannel >= 9) return;
        if (channel.VibratoDelayCountdown != 0)
        {
            channel.VibratoDelayCountdown -= 1;
            return;
        }
        int old = channel.VibratoTimer;
        channel.VibratoTimer = U8(old + channel.VibratoTempo);
        if (channel.VibratoTimer < old)
        {
            channel.VibratoStepsCountdown = U8(channel.VibratoStepsCountdown - 1);
            if (channel.VibratoStepsCountdown == 0)
            {
                channel.VibratoStep = I16(-channel.VibratoStep);
                channel.VibratoStepsCountdown = channel.VibratoNumSteps;
            }
            int freq = ((channel.RegBx << 8) | channel.RegAx) & 0x3ff;
            freq = (freq + channel.VibratoStep) & 0xffff;
            channel.RegAx = freq & 0xff;
            channel.RegBx = U8((channel.RegBx & 0xfc) | (freq >> 8));
            WriteOpl(0xa0 + _curChannel, channel.RegAx);
            WriteOpl(0xb0 + _curChannel, channel.RegBx);
        }
    }

    private void SecondaryEffect1(Chan channel)
    {
        if (_curChannel >= 9) return;
        int old = channel.SecondaryEffectTimer;
        channel.SecondaryEffectTimer = U8(old + channel.SecondaryEffectTempo);
        if (channel.SecondaryEffectTimer < old)
        {
            channel.SecondaryEffectPos = I8(channel.SecondaryEffectPos - 1);
            if (channel.SecondaryEffectPos < 0) channel.SecondaryEffectPos = channel.SecondaryEffectSize;
            int at = channel.SecondaryEffectData + channel.SecondaryEffectPos;
            int value = at >= 0 && at < _soundDataSize ? _soundData[at] : 0;
            WriteOpl(channel.SecondaryEffectRegbase + _curRegOffset, value);
        }
    }

    private int CalculateOpLevel1(Chan channel)
    {
        int value = channel.OpLevel1 & 0x3f;
        if (channel.TwoChan != 0)
        {
            value += channel.OpExtraLevel1;
            value += channel.OpExtraLevel2;
            int level3 = ((channel.OpExtraLevel3 ^ 0x3f) * channel.VolumeModifier) & 0xffff;
            if (level3 != 0)
            {
                level3 += 0x3f;
                level3 >>= 8;
            }
            value += level3 ^ 0x3f;
        }
        value = Clip(value & 0xff, 0, 0x3f);
        if (channel.VolumeModifier == 0) value = 0x3f;
        return value | (channel.OpLevel1 & 0xc0);
    }

    private int CalculateOpLevel2(Chan channel)
    {
        int value = channel.OpLevel2 & 0x3f;
        value += channel.OpExtraLevel1;
        value += channel.OpExtraLevel2;
        int level3 = ((channel.OpExtraLevel3 ^ 0x3f) * channel.VolumeModifier) & 0xffff;
        if (level3 != 0)
        {
            level3 += 0x3f;
            level3 >>= 8;
        }
        value += level3 ^ 0x3f;
        value = Clip(value & 0xff, 0, 0x3f);
        if (channel.VolumeModifier == 0) value = 0x3f;
        return value | (channel.OpLevel2 & 0xc0);
    }

    // ---- the parser's opcodes; `values` is the offset of the first argument byte ----
    private int V(int values, int i) => _soundData[values + i];
    private int Le16(int values) => I16(_soundData[values] | (_soundData[values + 1] << 8));
    private int Be16(int values) => (_soundData[values] << 8) | _soundData[values + 1];

    /// <summary>How many argument bytes each opcode takes, indexed by opcode &amp; 0x7f.</summary>
    private static readonly int[] OpcodeArgs =
    {
        1, 2, 1, 1, 2, 2, 0, 1, 0, 1, 2, 2, 1, 5, 1, 1,
        1, 3, 0, 1, 0, 4, 0, 0, 0, 0, 1, 0, 1, 1, 1, 0,
        1, 1, 0, 0, 1, 0, 1, 0, 0, 1, 0, 1, 2, 2, 1, 1,
        1, 0, 0, 1, 0, 2, 0, 0, 0, 1, 0, 0, 1, 1, 0, 2,
        0, 9, 1, 0, 2, 2, 2, 1, 1, 2, 0,
    };

    private int RunOpcode(int opcode, Chan channel, int values)
    {
        switch (opcode)
        {
            case 0: channel.RepeatCounter = V(values, 0); return 0;
            case 1:
                channel.RepeatCounter = U8(channel.RepeatCounter - 1);
                if (channel.RepeatCounter != 0)
                {
                    int p = CheckDataOffset(channel.Dataptr, Le16(values));
                    if (p >= 0) channel.Dataptr = p;
                }
                return 0;
            case 2: return UpdateSetupProgram(channel, values);
            case 3: channel.Spacing1 = V(values, 0); return 0;
            case 4: return UpdateJump(channel, values);
            case 5: return UpdateJumpToSubroutine(channel, values);
            case 6:
                if (channel.DataptrStackPos == 0) return UpdateStopChannel(channel);
                channel.Dataptr = channel.DataptrStack[--channel.DataptrStackPos];
                return 0;
            case 7: channel.BaseOctave = I8(V(values, 0)); return 0;
            case 9:
                SetupDuration(V(values, 0), channel);
                NoteOff(channel);
                return V(values, 0) != 0 ? 1 : 0;
            case 10: WriteOpl(V(values, 0), V(values, 1)); return 0;
            case 11:
                SetupNote(V(values, 0), channel);
                SetupDuration(V(values, 1), channel);
                return V(values, 1) != 0 ? 1 : 0;
            case 12: channel.BaseNote = I8(V(values, 0)); return 0;
            case 13: return UpdateSetupSecondaryEffect1(channel, values);
            case 14: return UpdateStopOtherChannel(values);
            case 15: return UpdateWaitForEndOfProgram(channel, values);
            case 16:
            {
                int instrument = GetInstrument(V(values, 0));
                if (instrument < 0) return 0;
                SetupInstrument(_curRegOffset, instrument, channel);
                return 0;
            }
            case 17:
                channel.SlideTempo = V(values, 0);
                channel.SlideStep = I16(Be16(values + 1));
                channel.PrimaryEffect = Effect.Slide;
                channel.SlideTimer = 0xff;
                return 0;
            case 18:
                channel.PrimaryEffect = Effect.None;
                channel.SlideStep = 0;
                return 0;
            case 19: channel.BaseFreq = V(values, 0); return 0;
            case 21:
                channel.VibratoTempo = V(values, 0);
                channel.VibratoStepRange = V(values, 1);
                channel.VibratoStepsCountdown = U8(V(values, 2) + 1);
                channel.VibratoNumSteps = U8(V(values, 2) << 1);
                channel.VibratoDelay = V(values, 3);
                channel.PrimaryEffect = Effect.Vibrato;
                return 0;
            case 26: channel.Priority = V(values, 0); return 0;
            case 28:
                _beatDivider = _beatDivCnt = V(values, 0) >> 1;
                _callbackTimer = 0xff;
                _beatCounter = _beatWaiting = 0;
                return 0;
            case 29: return UpdateWaitForNextBeat(channel, values);
            case 30:
                channel.OpExtraLevel1 = V(values, 0);
                AdjustVolume(channel);
                return 0;
            case 32:
                SetupDuration(V(values, 0), channel);
                return V(values, 0) != 0 ? 1 : 0;
            case 33:
                SetupDuration(V(values, 0), channel);
                NoteOn(channel);
                return V(values, 0) != 0 ? 1 : 0;
            case 36: channel.FractionalSpacing = V(values, 0) & 7; return 0;
            case 38: _tempo = V(values, 0); return 0;
            case 39: channel.SecondaryEffect = Effect.None; return 0;
            case 41: channel.Tempo = V(values, 0); return 0;
            case 43: channel.OpExtraLevel3 = V(values, 0); return 0;
            case 44: return UpdateSetExtraLevel2(values);
            case 45: return UpdateChangeExtraLevel2(values);
            case 46:
                if ((V(values, 0) & 1) != 0) _vibratoAndAMDepthBits |= 0x80;
                else _vibratoAndAMDepthBits &= 0x7f;
                WriteOpl(0xbd, _vibratoAndAMDepthBits);
                return 0;
            case 47:
                if ((V(values, 0) & 1) != 0) _vibratoAndAMDepthBits |= 0x40;
                else _vibratoAndAMDepthBits &= 0xbf;
                WriteOpl(0xbd, _vibratoAndAMDepthBits);
                return 0;
            case 48:
                channel.OpExtraLevel1 = U8(channel.OpExtraLevel1 + V(values, 0));
                AdjustVolume(channel);
                return 0;
            case 51: return UpdateClearChannel(channel, values);
            case 53: return UpdateChangeNoteRandomly(channel, values);
            case 54: channel.PrimaryEffect = Effect.None; return 0;
            case 57:
                channel.PitchBend = I8(V(values, 0));
                SetupNote(channel.RawNote, channel, true);
                return 0;
            case 58: channel.Tempo = _tempo; return 0;
            case 59: return 0;   // nop
            case 60: channel.DurationRandomness = V(values, 0); return 0;
            case 61: channel.Tempo = Clip(channel.Tempo + I8(V(values, 0)), 1, 255); return 0;
            case 63: return UpdateCallback46(values);
            case 64: return 0;   // nop
            case 65: return UpdateSetupRhythmSection(channel, values);
            case 66:
                WriteOpl(0xbd, (_rhythmSectionBits & ~(V(values, 0) & 0x1f)) | 0x20);
                _rhythmSectionBits |= V(values, 0);
                WriteOpl(0xbd, _vibratoAndAMDepthBits | 0x20 | _rhythmSectionBits);
                return 0;
            case 67:
                _rhythmSectionBits = 0;
                WriteOpl(0xbd, _vibratoAndAMDepthBits);
                return 0;
            case 68: return UpdateSetRhythmLevel2(values);
            case 69: return UpdateChangeRhythmLevel1(values);
            case 70: return UpdateSetRhythmLevel1(values);
            case 71: _soundTrigger = V(values, 0); return 0;
            case 72: channel.TempoReset = V(values, 0); return 0;
            case 73: channel.Unk39 = V(values, 0); channel.Unk40 = V(values, 1); return 0;
            default: return UpdateStopChannel(channel);
        }
    }

    private int UpdateStopChannel(Chan channel)
    {
        channel.Priority = 0;
        if (_curChannel != 9) NoteOff(channel);
        channel.Dataptr = -1;
        return 2;
    }

    private int UpdateSetupProgram(Chan channel, int values)
    {
        if (V(values, 0) == 0xff) return 0;
        int ptr = GetProgram(V(values, 0));
        if (CheckDataOffset(ptr, 2) < 0) return 0;
        int chanNum = _soundData[ptr++];
        int priority = _soundData[ptr++];
        if (chanNum > 9) return 0;
        var channel2 = _channels[chanNum];
        if (priority >= channel2.Priority)
        {
            int dataptrBackUp = channel.Dataptr;
            _programStartTimeout = 2;
            channel2.Reset();
            channel2.Priority = priority;
            channel2.Dataptr = ptr;
            channel2.Tempo = 0xff;
            channel2.Timer = 0xff;
            channel2.Duration = 1;
            channel2.VolumeModifier = chanNum <= 5 ? _musicVolume : _sfxVolume;
            InitAdlibChannel(chanNum);
            channel.Dataptr = dataptrBackUp;
        }
        return 0;
    }

    private int UpdateJump(Chan channel, int values)
    {
        int add = Le16(values);
        channel.Dataptr = _version == 1 ? CheckDataOffset(0, add - 191) : CheckDataOffset(channel.Dataptr, add);
        if (channel.Dataptr < 0) return UpdateStopChannel(channel);
        if ((_syncJumpMask & (1 << _curChannel)) != 0) channel.Lock = true;
        return 0;
    }

    private int UpdateJumpToSubroutine(Chan channel, int values)
    {
        int add = Le16(values);
        if (channel.DataptrStackPos >= 4) return 0;
        channel.DataptrStack[channel.DataptrStackPos++] = channel.Dataptr;
        channel.Dataptr = _version < 3 ? CheckDataOffset(0, add - 191) : CheckDataOffset(channel.Dataptr, add);
        if (channel.Dataptr < 0) channel.Dataptr = channel.DataptrStack[--channel.DataptrStackPos];
        return 0;
    }

    private int UpdateSetupSecondaryEffect1(Chan channel, int values)
    {
        channel.SecondaryEffectTimer = channel.SecondaryEffectTempo = V(values, 0);
        channel.SecondaryEffectSize = channel.SecondaryEffectPos = I8(V(values, 1));
        channel.SecondaryEffectRegbase = V(values, 2);
        channel.SecondaryEffectData = (_soundData[values + 3] | (_soundData[values + 4] << 8)) - 191;
        channel.SecondaryEffect = Effect.Secondary1;
        int start = channel.SecondaryEffectData + channel.SecondaryEffectSize;
        if (start < 0 || start >= _soundDataSize) channel.SecondaryEffect = Effect.None;
        return 0;
    }

    private int UpdateStopOtherChannel(int values)
    {
        if (V(values, 0) > 9) return 0;
        var channel2 = _channels[V(values, 0)];
        channel2.Duration = 0;
        channel2.Priority = 0;
        channel2.Dataptr = -1;
        return 0;
    }

    private int UpdateWaitForEndOfProgram(Chan channel, int values)
    {
        int ptr = GetProgram(V(values, 0));
        if (ptr < 0) return 0;
        int chanNum = _soundData[ptr];
        if (chanNum > 9 || _channels[chanNum].Dataptr < 0) return 0;
        channel.Dataptr -= 2;
        return 2;
    }

    private int UpdateWaitForNextBeat(Chan channel, int values)
    {
        if ((_beatCounter & V(values, 0)) != 0 && _beatWaiting != 0)
        {
            _beatWaiting = 0;
            return 0;
        }
        if ((_beatCounter & V(values, 0)) == 0) _beatWaiting = U8(_beatWaiting + 1);
        channel.Dataptr -= 2;
        channel.Duration = 1;
        return 2;
    }

    private int UpdateSetExtraLevel2(int values)
    {
        if (V(values, 0) > 9) return 0;
        int channelBackUp = _curChannel;
        _curChannel = V(values, 0);
        var channel2 = _channels[_curChannel];
        channel2.OpExtraLevel2 = V(values, 1);
        AdjustVolume(channel2);
        _curChannel = channelBackUp;
        return 0;
    }

    private int UpdateChangeExtraLevel2(int values)
    {
        if (V(values, 0) > 9) return 0;
        int channelBackUp = _curChannel;
        _curChannel = V(values, 0);
        var channel2 = _channels[_curChannel];
        channel2.OpExtraLevel2 = U8(channel2.OpExtraLevel2 + V(values, 1));
        AdjustVolume(channel2);
        _curChannel = channelBackUp;
        return 0;
    }

    private int UpdateClearChannel(Chan channel, int values)
    {
        if (V(values, 0) > 9) return 0;
        int channelBackUp = _curChannel;
        _curChannel = V(values, 0);
        int dataptrBackUp = channel.Dataptr;
        var channel2 = _channels[_curChannel];
        channel2.Duration = channel2.Priority = 0;
        channel2.Dataptr = -1;
        channel2.OpExtraLevel2 = 0;
        if (_curChannel != 9)
        {
            int regOff = RegOffset[_curChannel];
            WriteOpl(0xc0 + _curChannel, 0x00);
            WriteOpl(0x43 + regOff, 0x3f);
            WriteOpl(0x83 + regOff, 0xff);
            WriteOpl(0xb0 + _curChannel, 0x00);
        }
        _curChannel = channelBackUp;
        channel.Dataptr = dataptrBackUp;
        return 0;
    }

    private int UpdateChangeNoteRandomly(Chan channel, int values)
    {
        if (_curChannel >= 9) return 0;
        int mask = Be16(values);
        int note = ((channel.RegBx & 0x1f) << 8) | channel.RegAx;
        note = (note + (mask & GetRandomNr())) & 0xffff;
        note |= (channel.RegBx & 0x20) << 8;
        WriteOpl(0xa0 + _curChannel, note & 0xff);
        WriteOpl(0xb0 + _curChannel, (note & 0xff00) >> 8);
        return 0;
    }

    private int UpdateCallback46(int values)
    {
        int entry = V(values, 1);
        if (entry + 2 > Table2.Length) return 0;
        _tablePtr1 = Table2[entry];
        _tablePtr2 = Table2[entry + 1];
        if (V(values, 0) == 2) WriteOpl(0xa0, _tablePtr2[0]);
        return 0;
    }

    private int UpdateSetupRhythmSection(Chan channel, int values)
    {
        int channelBackUp = _curChannel;
        int regOffsetBackUp = _curRegOffset;
        _curChannel = 6;
        _curRegOffset = RegOffset[6];
        int instrument = GetInstrument(V(values, 0));
        if (instrument >= 0) SetupInstrument(_curRegOffset, instrument, channel);
        _opLevelBD = channel.OpLevel2;
        _curChannel = 7;
        _curRegOffset = RegOffset[7];
        instrument = GetInstrument(V(values, 1));
        if (instrument >= 0) SetupInstrument(_curRegOffset, instrument, channel);
        _opLevelHH = channel.OpLevel1;
        _opLevelSD = channel.OpLevel2;
        _curChannel = 8;
        _curRegOffset = RegOffset[8];
        instrument = GetInstrument(V(values, 2));
        if (instrument >= 0) SetupInstrument(_curRegOffset, instrument, channel);
        _opLevelTT = channel.OpLevel1;
        _opLevelCY = channel.OpLevel2;
        _channels[6].RegBx = V(values, 3) & 0x2f;
        WriteOpl(0xb6, _channels[6].RegBx);
        WriteOpl(0xa6, V(values, 4));
        _channels[7].RegBx = V(values, 5) & 0x2f;
        WriteOpl(0xb7, _channels[7].RegBx);
        WriteOpl(0xa7, V(values, 6));
        _channels[8].RegBx = V(values, 7) & 0x2f;
        WriteOpl(0xb8, _channels[8].RegBx);
        WriteOpl(0xa8, V(values, 8));
        _rhythmSectionBits = 0x20;
        _curRegOffset = regOffsetBackUp;
        _curChannel = channelBackUp;
        return 0;
    }

    private static int Cv(int x) => Clip(I16(x), 0, 0x3f);

    private int UpdateSetRhythmLevel2(int values)
    {
        int ops = V(values, 0);
        int v = V(values, 1);
        if ((ops & 1) != 0) { _opExtraLevel2HH = v; WriteOpl(0x51, Cv(v + _opLevelHH + _opExtraLevel1HH + _opExtraLevel2HH)); }
        if ((ops & 2) != 0) { _opExtraLevel2CY = v; WriteOpl(0x55, Cv(v + _opLevelCY + _opExtraLevel1CY + _opExtraLevel2CY)); }
        if ((ops & 4) != 0) { _opExtraLevel2TT = v; WriteOpl(0x52, Cv(v + _opLevelTT + _opExtraLevel1TT + _opExtraLevel2TT)); }
        if ((ops & 8) != 0) { _opExtraLevel2SD = v; WriteOpl(0x54, Cv(v + _opLevelSD + _opExtraLevel1SD + _opExtraLevel2SD)); }
        if ((ops & 16) != 0) { _opExtraLevel2BD = v; WriteOpl(0x53, Cv(v + _opLevelBD + _opExtraLevel1BD + _opExtraLevel2BD)); }
        return 0;
    }

    private int UpdateChangeRhythmLevel1(int values)
    {
        int ops = V(values, 0);
        int v = V(values, 1);
        if ((ops & 1) != 0) { _opExtraLevel1HH = Cv(v + _opLevelHH + _opExtraLevel1HH + _opExtraLevel2HH); WriteOpl(0x51, _opExtraLevel1HH); }
        if ((ops & 2) != 0) { _opExtraLevel1CY = Cv(v + _opLevelCY + _opExtraLevel1CY + _opExtraLevel2CY); WriteOpl(0x55, _opExtraLevel1CY); }
        if ((ops & 4) != 0) { _opExtraLevel1TT = Cv(v + _opLevelTT + _opExtraLevel1TT + _opExtraLevel2TT); WriteOpl(0x52, _opExtraLevel1TT); }
        if ((ops & 8) != 0) { _opExtraLevel1SD = Cv(v + _opLevelSD + _opExtraLevel1SD + _opExtraLevel2SD); WriteOpl(0x54, _opExtraLevel1SD); }
        if ((ops & 16) != 0) { _opExtraLevel1BD = Cv(v + _opLevelBD + _opExtraLevel1BD + _opExtraLevel2BD); WriteOpl(0x53, _opExtraLevel1BD); }
        return 0;
    }

    private int UpdateSetRhythmLevel1(int values)
    {
        int ops = V(values, 0);
        int v = V(values, 1);
        if ((ops & 1) != 0) { _opExtraLevel1HH = v; WriteOpl(0x51, Cv(v + _opLevelHH + _opExtraLevel2HH)); }
        if ((ops & 2) != 0) { _opExtraLevel1CY = v; WriteOpl(0x55, Cv(v + _opLevelCY + _opExtraLevel2CY)); }
        if ((ops & 4) != 0) { _opExtraLevel1TT = v; WriteOpl(0x52, Cv(v + _opLevelTT + _opExtraLevel2TT)); }
        if ((ops & 8) != 0) { _opExtraLevel1SD = v; WriteOpl(0x54, Cv(v + _opLevelSD + _opExtraLevel2SD)); }
        if ((ops & 16) != 0) { _opExtraLevel1BD = v; WriteOpl(0x53, Cv(v + _opLevelBD + _opExtraLevel2BD)); }
        return 0;
    }
}

/// <summary>
/// SoundPC_v1 (version 4): an .ADL file is a 500-byte track table followed by the driver's data.
/// The player owns the chip and the driver, and runs the driver's callback 72 times a second
/// however many samples that works out to.
/// </summary>
public sealed class AdlibPlayer
{
    private readonly int _sampleRate;
    private readonly double _samplesPerCallback;
    private AdLibDriver _driver;
    private Opl3 _opl;
    private byte[] _trackEntries;
    private double _untilCallback;
    private int _musicVolume = 255, _sfxVolume = 255;
    private string _loaded;

    public AdlibPlayer(int sampleRate)
    {
        _sampleRate = sampleRate;
        _samplesPerCallback = (double)sampleRate / AdLibDriver.CallbacksPerSecond;
    }

    public void Init()
    {
        _opl = new Opl3(_sampleRate);
        _driver = new AdLibDriver(_opl, 4);
        _driver.InitDriver();
        _driver.SetMusicVolume(_musicVolume);
        _driver.SetSfxVolume(_sfxVolume);
    }

    public void LoadFile(string name, byte[] bytes)
    {
        if (_loaded == name) return;
        if (_trackEntries != null) HaltTrack();
        Play(0, 0);
        Play(0, 0);
        _driver.StopAllChannels();
        _trackEntries = bytes.Take(500).ToArray();
        _driver.SetSoundData(bytes.Skip(500).ToArray());
        _loaded = name;
    }

    public void Play(int track, int volume)
    {
        if (_trackEntries == null || track < 0 || (track << 1) + 1 >= _trackEntries.Length) return;
        int soundId = _trackEntries[track << 1] | (_trackEntries[(track << 1) + 1] << 8);
        if (soundId == 0xffff) return;
        _driver.StartSound(soundId, volume);
    }

    public void PlayTrack(int track) => Play(track, 0xff);
    public void PlaySoundEffect(int track, int volume) => Play(track, volume);
    public void HaltTrack() { Play(0, 0); Play(0, 0); }
    public void BeginFadeOut() => Play(1, 0xff);
    public bool IsPlaying() => _driver.IsChannelPlaying(0);

    public void SetVolume(int music, int sfx)
    {
        _musicVolume = music;
        _sfxVolume = sfx;
        if (_driver != null) { _driver.SetMusicVolume(music); _driver.SetSfxVolume(sfx); }
    }

    /// <summary>Renders `n` frames, running the driver's callback at 72 Hz in between.</summary>
    public void Render(float[] left, float[] right, int n)
    {
        int at = 0;
        var bufL = new float[n];
        var bufR = right != null ? new float[n] : null;
        while (at < n)
        {
            if (_untilCallback <= 0)
            {
                _driver.Callback();
                _untilCallback += _samplesPerCallback;
            }
            int chunk = Math.Min(n - at, (int)Math.Ceiling(_untilCallback));
            var chunkL = new float[chunk];
            var chunkR = right != null ? new float[chunk] : null;
            _opl.Generate(chunkL, chunkR, chunk);
            Array.Copy(chunkL, 0, left, at, chunk);
            if (right != null) Array.Copy(chunkR, 0, right, at, chunk);
            at += chunk;
            _untilCallback -= chunk;
        }
        _ = bufL;
        _ = bufR;
    }
}
