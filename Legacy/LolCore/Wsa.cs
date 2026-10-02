// WSA: the frame-delta animations the cutscenes and the in-game dialogues are made of.
//
// Transliterated from src/game/wsa.mjs (WSAMovie_v2) and Screen::copyWsaRect.
//
// A WSA is one compressed first frame followed by deltas, and every frame is XORed onto the one
// before it - so showing frame 7 means walking there from wherever the player left off, forwards or
// backwards, whichever is shorter. That walk is the whole trick, and it is why a frame cannot be
// decoded on its own.
namespace LolCore;

public sealed class WsaPlayer
{
    private const int WfOffscreenDecode = 0x10;
    private const int WfNoLastFrame = 0x20;
    private const int WfNoFirstFrame = 0x40;
    private const int WfHasPalette = 0x100;
    private const int WfXor = 0x200;

    private readonly Screen _screen;
    private bool _opened;
    private byte[] _offscreenBuffer;
    private byte[] _deltaBuffer;
    private uint[] _frameOffsTable;
    private byte[] _frameData;
    private int _deltaBufferSize;
    private int _flags;
    private int _currentFrame;
    private int _x, _y, _drawPage;

    public int NumFrames { get; private set; }
    public int XAdd { get; private set; }
    public int YAdd { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public string Name = "";

    public WsaPlayer(Screen screen) => _screen = screen;

    /// <summary>Screen::decodeFrameDelta / decodeFrameDeltaPage - the delta, XORed or copied in.</summary>
    private static void DecodeFrameDelta(byte[] dst, int dstStart, byte[] src, int pitch, int width, bool noXor)
    {
        int s = 0;
        int d = dstStart;
        int count = 0;
        int dstNext = dstStart;

        void Put(byte value)
        {
            if (d >= 0 && d < dst.Length)
            {
                if (noXor) dst[d] = value;
                else dst[d] ^= value;
            }
            d += 1;
            if (pitch != 0 && ++count == width)
            {
                count = 0;
                dstNext += pitch;
                d = dstNext;
            }
        }

        void Skip(int n)
        {
            if (pitch == 0) { d += n; return; }
            d += n;
            count += n;
            while (count >= width)
            {
                count -= width;
                dstNext += pitch;
                d = dstNext + count;
            }
        }

        while (s < src.Length)
        {
            int code = src[s++];
            if (code == 0)
            {
                int len = src[s++];
                code = src[s++];
                while (len-- > 0) Put((byte)code);
            }
            else if ((code & 0x80) != 0)
            {
                code -= 0x80;
                if (code != 0) Skip(code);
                else
                {
                    int subcode = src[s] | (src[s + 1] << 8);
                    s += 2;
                    if (subcode == 0) break;
                    if ((subcode & 0x8000) != 0)
                    {
                        subcode -= 0x8000;
                        if ((subcode & 0x4000) != 0)
                        {
                            int len = subcode - 0x4000;
                            code = src[s++];
                            while (len-- > 0) Put((byte)code);
                        }
                        else while (subcode-- > 0) Put(src[s++]);
                    }
                    else Skip(subcode);
                }
            }
            else while (code-- > 0) Put(src[s++]);
        }
    }

    /// <summary>open: the header, the frame offsets, and - unless the file says otherwise - frame 0.</summary>
    public int Open(byte[] bytes, int unk1, byte[] palBuf)
    {
        Close();
        int p = 0;
        int U16() { int v = bytes[p] | (bytes[p + 1] << 8); p += 2; return v; }
        short I16() { short v = (short)(bytes[p] | (bytes[p + 1] << 8)); p += 2; return v; }
        uint U32() { uint v = (uint)(bytes[p] | (bytes[p + 1] << 8) | (bytes[p + 2] << 16) | (bytes[p + 3] << 24)); p += 4; return v; }

        NumFrames = U16();
        XAdd = I16();
        YAdd = I16();
        Width = U16();
        Height = U16();
        _deltaBufferSize = U16();
        _flags = 0;
        int flags = U16();
        int offsPal = 0;
        if ((flags & 1) != 0)
        {
            offsPal = 0x300;
            _flags |= WfHasPalette;
            int at = p + 8 + ((NumFrames << 2) & 0xffff);
            if (palBuf != null && at + 0x300 <= bytes.Length) Array.Copy(bytes, at, palBuf, 0, 0x300);
        }
        if ((flags & 2) != 0) _flags |= WfXor;
        if ((unk1 & 2) == 0)
        {
            _flags |= WfOffscreenDecode;
            _offscreenBuffer = new byte[Width * Height];
        }
        else _offscreenBuffer = null;
        if ((NumFrames & 0x8000) != 0) NumFrames &= 0x7fff;
        _currentFrame = NumFrames;
        _deltaBuffer = new byte[_deltaBufferSize];
        _frameOffsTable = new uint[NumFrames + 2];
        uint frameDataOffs = U32();
        bool firstFrame = true;
        if (frameDataOffs == 0)
        {
            firstFrame = false;
            frameDataOffs = (uint)(bytes[p] | (bytes[p + 1] << 8) | (bytes[p + 2] << 16) | (bytes[p + 3] << 24));
            _flags |= WfNoFirstFrame;
        }
        for (int i = 1; i < NumFrames + 2; i += 1)
        {
            uint o = U32();
            _frameOffsTable[i] = o != 0 ? o - frameDataOffs : 0;
        }
        if (_frameOffsTable[NumFrames + 1] == 0) _flags |= WfNoLastFrame;
        p += offsPal;
        _frameData = bytes.Skip(p).ToArray();
        if (firstFrame) _deltaBuffer = Cps.DecodeLcwBlock(_frameData, 0, _frameData.Length, _deltaBufferSize);
        _opened = true;
        return NumFrames;
    }

    public void Close() => _opened = false;

    /// <summary>
    /// displayFrame: walk to the frame asked for and put it on the page. Bits 12-15 of `flags` pick
    /// the plot function when the frame was decoded offscreen.
    /// </summary>
    public void DisplayFrame(int frameNum, int pageNum, int x, int y, int flags = 0, byte[] table1 = null, byte[] table2 = null)
    {
        if (frameNum >= NumFrames || frameNum < 0 || !_opened) return;
        // A plot function that blends (0x5000 and its kind) reads the level's transparency tables.
        // Every call site in the JavaScript engine hands over the screen's own pair, so a caller that
        // does not say otherwise gets them rather than a null that fails inside the inner loop.
        table1 ??= _screen.TransparencyTable1;
        table2 ??= _screen.TransparencyTable2;
        x += XAdd;
        y += YAdd;
        _x = x;
        _y = y;
        _drawPage = pageNum;
        bool offscreen = (_flags & WfOffscreenDecode) != 0;
        var dst = offscreen ? _offscreenBuffer : _screen.Page(pageNum);
        int dstStart = offscreen ? 0 : y * Screen.Width + x;
        int pitch = offscreen ? 0 : Screen.Width;
        if (_currentFrame == NumFrames)
        {
            if ((_flags & WfNoFirstFrame) == 0)
                DecodeFrameDelta(dst, dstStart, _deltaBuffer, pitch, Width, !offscreen && (_flags & WfXor) == 0);
            _currentFrame = 0;
        }
        int diffCount = Math.Abs(_currentFrame - frameNum);
        int frameStep = 1;
        int frameCount;
        if (_currentFrame < frameNum)
        {
            frameCount = NumFrames - frameNum + _currentFrame;
            if (diffCount > frameCount && (_flags & WfNoLastFrame) == 0) frameStep = -1;
            else frameCount = diffCount;
        }
        else
        {
            frameCount = NumFrames - _currentFrame + frameNum;
            if (frameCount >= diffCount || (_flags & WfNoLastFrame) != 0)
            {
                frameStep = -1;
                frameCount = diffCount;
            }
        }
        if (frameStep > 0)
        {
            int cf = _currentFrame;
            while (frameCount-- > 0)
            {
                cf += frameStep;
                ProcessFrame(cf, dst, dstStart, pitch);
                if (cf == NumFrames) cf = 0;
            }
        }
        else
        {
            int cf = _currentFrame;
            while (frameCount-- > 0)
            {
                if (cf == 0) cf = NumFrames;
                ProcessFrame(cf, dst, dstStart, pitch);
                cf += frameStep;
            }
        }
        _currentFrame = frameNum;
        if (offscreen)
        {
            int backup = _screen.CurPage;
            _screen.CurPage = _drawPage;
            _screen.CopyWsaRect(_x, _y, Width, Height, 0, (flags & 0xff00) >> 12, _offscreenBuffer, flags & 0xff, table1, table2);
            _screen.CurPage = backup;
        }
    }

    private void ProcessFrame(int frameNum, byte[] dst, int dstStart, int pitch)
    {
        int at = (int)_frameOffsTable[frameNum];
        if (at < 0 || at >= _frameData.Length) return;
        var delta = Cps.DecodeLcwBlock(_frameData, at, _frameData.Length - at, _deltaBufferSize);
        DecodeFrameDelta(dst, dstStart, delta, pitch, Width, false);
    }
}
