// src/game/wsa.mjs: WSAMovie_v2 port: frame-delta animation played into screen pages or an offscreen buffer.
using System;
using System.Buffers.Binary;

namespace Lol
{
    public static class WsaGame
    {
        public const int WF_OFFSCREEN_DECODE = 0x10;
        public const int WF_NO_LAST_FRAME = 0x20;
        public const int WF_NO_FIRST_FRAME = 0x40;
        public const int WF_HAS_PALETTE = 0x100;
        public const int WF_XOR = 0x200;

        // Screen::decodeFrameDelta (xor into a linear buffer) / decodeFrameDeltaPage (pitch-aware).
        // JS reads past src give undefined (0 here, which behaves the same) and writes past dst are dropped.
        public static void decodeFrameDelta(byte[] dst, int dstStart, byte[] src, int pitch, int width, bool noXor)
        {
            int s = 0;
            int d = dstStart;
            int count = 0;
            int dstNext = dstStart;
            int rd(int i) => i < src.Length ? src[i] : 0;
            void put(int value)
            {
                if ((uint)d < (uint)dst.Length)
                {
                    if (noXor) dst[d] = (byte)value;
                    else dst[d] ^= (byte)value;
                }
                d += 1;
                if (pitch != 0 && ++count == width)
                {
                    count = 0;
                    dstNext += pitch;
                    d = dstNext;
                }
            }
            void skip(int n)
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
                    int len = rd(s++);
                    code = rd(s++);
                    while (len-- != 0) put(code);
                }
                else if ((code & 0x80) != 0)
                {
                    code -= 0x80;
                    if (code != 0) skip(code);
                    else
                    {
                        int subcode = rd(s) | (rd(s + 1) << 8);
                        s += 2;
                        if (subcode == 0) break;
                        if ((subcode & 0x8000) != 0)
                        {
                            subcode -= 0x8000;
                            if ((subcode & 0x4000) != 0)
                            {
                                int len = subcode - 0x4000;
                                code = rd(s++);
                                while (len-- != 0) put(code);
                            }
                            else while (subcode-- != 0) put(rd(s++));
                        }
                        else skip(subcode);
                    }
                }
                else while (code-- != 0) put(rd(s++));
            }
        }
    }

    public sealed class WsaPlayer
    {
        public Screen screen;
        public bool opened;
        /// <summary>set by the callers (spells.mjs openWsa, tim.mjs), read by the host's onWsaFrame</summary>
        public string name;
        public int numFrames, xAdd, yAdd, width, height, deltaBufferSize, flags, currentFrame;
        public byte[] offscreenBuffer;
        public byte[] deltaBuffer;
        public uint[] frameOffsTable;
        public Bytes frameData;
        public int x, y, drawPage;

        public WsaPlayer(Screen screen)
        {
            this.screen = screen;
            this.opened = false;
        }

        // open flags: 1 = offscreen decode requested (bit 2 set means decode in page), palBuf receives the embedded palette.
        public int open(byte[] bytes, int unk1, byte[] palBuf)
        {
            this.close();
            int p = 0;
            this.numFrames = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(bytes, p, 2)); p += 2;
            this.xAdd = BinaryPrimitives.ReadInt16LittleEndian(new ReadOnlySpan<byte>(bytes, p, 2)); p += 2;
            this.yAdd = BinaryPrimitives.ReadInt16LittleEndian(new ReadOnlySpan<byte>(bytes, p, 2)); p += 2;
            this.width = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(bytes, p, 2)); p += 2;
            this.height = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(bytes, p, 2)); p += 2;
            this.deltaBufferSize = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(bytes, p, 2)); p += 2;
            this.flags = 0;
            int flags = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(bytes, p, 2)); p += 2;
            int offsPal = 0;
            if ((flags & 1) != 0)
            {
                offsPal = 0x300;
                this.flags |= WsaGame.WF_HAS_PALETTE;
                if (palBuf != null) Js.Set(palBuf, Js.Slice(bytes, p + 8 + ((this.numFrames << 2) & 0xffff), p + 8 + ((this.numFrames << 2) & 0xffff) + 0x300));
            }
            if ((flags & 2) != 0) this.flags |= WsaGame.WF_XOR;
            if ((unk1 & 2) == 0)
            {
                this.flags |= WsaGame.WF_OFFSCREEN_DECODE;
                this.offscreenBuffer = new byte[this.width * this.height];
            }
            else this.offscreenBuffer = null;
            if ((this.numFrames & 0x8000) != 0) this.numFrames &= 0x7fff;
            this.currentFrame = this.numFrames;
            this.deltaBuffer = new byte[this.deltaBufferSize];
            this.frameOffsTable = new uint[this.numFrames + 2];
            uint frameDataOffs = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(bytes, p, 4)); p += 4;
            bool firstFrame = true;
            if (frameDataOffs == 0)
            {
                firstFrame = false;
                frameDataOffs = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(bytes, p, 4));
                this.flags |= WsaGame.WF_NO_FIRST_FRAME;
            }
            for (int i = 1; i < this.numFrames + 2; i += 1)
            {
                uint o = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(bytes, p, 4));
                this.frameOffsTable[i] = o != 0 ? unchecked(o - frameDataOffs) : 0; // Uint32Array store wraps
                p += 4;
            }
            if (this.frameOffsTable[this.numFrames + 1] == 0) this.flags |= WsaGame.WF_NO_LAST_FRAME;
            p += offsPal;
            this.frameData = new Bytes(bytes).Sub(p);
            if (firstFrame) this.deltaBuffer = Cps.decodeLcwBlock(this.frameData, this.deltaBufferSize);
            this.opened = true;
            return this.numFrames;
        }

        public void close()
        {
            this.opened = false;
        }

        // flags: plot function in bits 12-15 (0 copy, 1 transparency blend, 4 skip zero, 5 both).
        public void displayFrame(int frameNum, int pageNum, int x, int y, int flags = 0, byte[] table1 = null, byte[] table2 = null)
        {
            if (frameNum >= this.numFrames || frameNum < 0 || !this.opened) return;
            x += this.xAdd;
            y += this.yAdd;
            this.x = x;
            this.y = y;
            this.drawPage = pageNum;
            bool offscreen = (this.flags & WsaGame.WF_OFFSCREEN_DECODE) != 0;
            var dst = offscreen ? this.offscreenBuffer : this.screen.page(pageNum);
            int dstStart = offscreen ? 0 : y * LandsOfLore.SCREEN_W + x;
            int pitch = offscreen ? 0 : LandsOfLore.SCREEN_W;
            if (this.currentFrame == this.numFrames)
            {
                if ((this.flags & WsaGame.WF_NO_FIRST_FRAME) == 0) WsaGame.decodeFrameDelta(dst, dstStart, this.deltaBuffer, pitch, this.width, !offscreen && (this.flags & WsaGame.WF_XOR) == 0);
                this.currentFrame = 0;
            }
            int diffCount = Math.Abs(this.currentFrame - frameNum);
            int frameStep = 1;
            int frameCount;
            if (this.currentFrame < frameNum)
            {
                frameCount = this.numFrames - frameNum + this.currentFrame;
                if (diffCount > frameCount && (this.flags & WsaGame.WF_NO_LAST_FRAME) == 0) frameStep = -1;
                else frameCount = diffCount;
            }
            else
            {
                frameCount = this.numFrames - this.currentFrame + frameNum;
                if (frameCount >= diffCount || (this.flags & WsaGame.WF_NO_LAST_FRAME) != 0)
                {
                    frameStep = -1;
                    frameCount = diffCount;
                }
            }
            if (frameStep > 0)
            {
                int cf = this.currentFrame;
                while (frameCount-- != 0)
                {
                    cf += frameStep;
                    this.processFrame(cf, dst, dstStart, pitch);
                    if (cf == this.numFrames) cf = 0;
                }
            }
            else
            {
                int cf = this.currentFrame;
                while (frameCount-- != 0)
                {
                    if (cf == 0) cf = this.numFrames;
                    this.processFrame(cf, dst, dstStart, pitch);
                    cf += frameStep;
                }
            }
            this.currentFrame = frameNum;
            if (this.screen.onWsaFrame != null) this.screen.onWsaFrame(this, frameNum, pageNum, x, y); // host: HD cutscene pictures
            if (offscreen)
            {
                int backup = this.screen.curPage;
                this.screen.curPage = this.drawPage;
                this.screen.copyWsaRect(this.x, this.y, this.width, this.height, 0, (flags & 0xff00) >> 12, this.offscreenBuffer, flags & 0xff, table1, table2);
                this.screen.curPage = backup;
            }
            if (pageNum == 0) this.screen.dirty = true;
        }

        public void processFrame(int frameNum, byte[] dst, int dstStart, int pitch)
        {
            // subarray(begin) clamps begin to the length
            var src = this.frameData.Sub((int)Math.Min(this.frameOffsTable[frameNum], (uint)this.frameData.Length));
            var delta = Cps.decodeLcwBlock(src, this.deltaBufferSize);
            WsaGame.decodeFrameDelta(dst, dstStart, delta, pitch, this.width, false);
        }
    }
}
