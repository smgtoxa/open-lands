// src/formats/emc.mjs: Westwood EMC bytecode (IFF FORM "EMC2": ORDR, TEXT, DATA), after ScummVM's EMCInterpreter.
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Lol
{
    /// <summary>emc.mjs decodeEmc: { ordr, data, text }</summary>
    public sealed class EmcScript
    {
        public ushort[] ordr;
        public ushort[] data;
        public byte[] text;
    }

    public static class Emc
    {
        public const int STACK_SIZE = 100;

        static string fourCC(byte[] bytes, int at)
        {
            var sb = new StringBuilder();
            for (int i = at; i < at + 4; i += 1) sb.Append((char)bytes[i]);
            return sb.ToString();
        }

        public static EmcScript decodeEmc(byte[] buffer)
        {
            var bytes = buffer;
            if (bytes.Length < 12 || fourCC(bytes, 0) != "FORM") throw new Exception("Not an IFF script");
            var chunks = new Dictionary<string, byte[]>();
            int offset = 12;
            while (offset + 8 <= bytes.Length)
            {
                string id = fourCC(bytes, offset);
                // getUint32 without littleEndian: big-endian
                long size = BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(bytes, offset + 4, 4));
                if (offset + 8 + size > bytes.Length) throw new Exception($"Truncated {id} chunk");
                chunks[id] = Js.Slice(bytes, offset + 8, offset + 8 + (int)size);
                offset += 8 + (int)size + (int)(size & 1);
            }
            if (!chunks.ContainsKey("ORDR") || !chunks.ContainsKey("DATA")) throw new Exception("Script lacks ORDR or DATA");
            ushort[] words(byte[] chunk)
            {
                var @out = new ushort[chunk.Length >> 1];
                for (int i = 0; i < @out.Length; i += 1) @out[i] = BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(chunk, i * 2, 2));
                return @out;
            }
            return new EmcScript { ordr = words(chunks["ORDR"]), data = words(chunks["DATA"]), text = chunks.TryGetValue("TEXT", out var text) ? text : new byte[0] };
        }

        // sysFuncs: array indexed by opcode; each receives the EmcState and returns (or resolves to) the int result.
        public static async Task runEmc(EmcState state, Func<EmcState, Task<int>>[] sysFuncs, Action<int> onUnknown)
        {
            var data = state.script.data;
            var stack = state.stack;
            int guard = 0;
            while (state.ip >= 0)
            {
                if (state.ip >= data.Length) throw new Exception($"Script execution outside DATA at {state.ip}");
                if (++guard > 1000000) throw new Exception("Script did not terminate");
                int code = (short)data[state.ip++];
                int opcode = (code >> 8) & 0x1f;
                int param = 0;
                if ((code & 0x8000) != 0)
                {
                    opcode = 0;
                    param = code & 0x7fff;
                }
                else if ((code & 0x4000) != 0)
                {
                    param = (sbyte)code;
                }
                else if ((code & 0x2000) != 0)
                {
                    param = (short)data[state.ip++];
                }

                switch (opcode)
                {
                    case 0: state.ip = param; break;
                    case 1: state.retValue = param; break;
                    case 2:
                        if (param == 0) stack[--state.sp] = (short)state.retValue;
                        else if (param == 1)
                        {
                            stack[--state.sp] = (short)(state.ip + 1);
                            stack[--state.sp] = (short)state.bp;
                            state.bp = state.sp + 2;
                        }
                        else state.ip = -1;
                        break;
                    case 3: case 4: stack[--state.sp] = (short)param; break;
                    case 5: stack[--state.sp] = state.regs[param]; break;
                    case 6: stack[--state.sp] = stack[state.bp - (param + 2)]; break;
                    case 7: stack[--state.sp] = stack[state.bp + param - 1]; break;
                    case 8:
                        if (param == 0) state.retValue = stack[state.sp++];
                        else if (param == 1 && state.sp < STACK_SIZE - 1)
                        {
                            state.bp = stack[state.sp++];
                            state.ip = stack[state.sp++];
                        }
                        else state.ip = -1;
                        break;
                    case 9: state.regs[param] = stack[state.sp++]; break;
                    case 10: stack[state.bp - (param + 2)] = stack[state.sp++]; break;
                    case 11: stack[state.bp + param - 1] = stack[state.sp++]; break;
                    case 12: state.sp += param; break;
                    case 13: state.sp -= param; break;
                    case 14:
                    {
                        int id = param & 0xff;
                        var func = id < sysFuncs.Length ? sysFuncs[id] : null;
                        // JS awaits only a thenable; every entry returns a Task here (a sync one is already completed).
                        int result = func != null ? await func(state) : 0;
                        guard = 0; // only loops without system calls can hang the VM; polling loops yield inside the call
                        state.retValue = result;
                        if (func == null && onUnknown != null) onUnknown(id);
                        break;
                    }
                    case 15: if (stack[state.sp++] == 0) state.ip = param & 0x7fff; break;
                    case 16:
                    {
                        int value = stack[state.sp];
                        if (param == 0) stack[state.sp] = (short)(value != 0 ? 0 : 1);
                        else if (param == 1) stack[state.sp] = (short)(-value);
                        else if (param == 2) stack[state.sp] = (short)~value;
                        else state.ip = -1;
                        break;
                    }
                    case 17:
                    {
                        int a = stack[state.sp++];
                        int b = stack[state.sp++];
                        int[] results = { b != 0 && a != 0 ? 1 : 0, b != 0 || a != 0 ? 1 : 0, a == b ? 1 : 0, a != b ? 1 : 0, a > b ? 1 : 0, a >= b ? 1 : 0,
                            a < b ? 1 : 0, a <= b ? 1 : 0, a + b, b - a, a * b, a != 0 ? b / a : 0, b >> a, b << a, a & b, a | b, a != 0 ? b % a : 0, a ^ b };
                        if (param >= results.Length) state.ip = -1;
                        else stack[--state.sp] = (short)results[param];
                        break;
                    }
                    case 18:
                        if (state.sp < STACK_SIZE - 1)
                        {
                            state.retValue = stack[state.sp++];
                            state.ip = stack[state.sp++] & 0xffff;
                            stack[STACK_SIZE - 1] = 0;
                        }
                        else state.ip = -1;
                        break;
                    default:
                        throw new Exception($"Unknown script opcode {opcode}");
                }
            }
        }
    }

    public sealed class EmcState
    {
        public EmcScript script;
        public int ip;
        public int retValue;
        public int bp;
        public int sp;
        public short[] regs;
        public short[] stack;

        public EmcState(EmcScript script)
        {
            this.script = script;
            this.ip = -1;
            this.retValue = 0;
            this.bp = Emc.STACK_SIZE + 1;
            this.sp = Emc.STACK_SIZE - 1;
            this.regs = new short[30];
            this.stack = new short[Emc.STACK_SIZE];
        }

        // Function entry; returns false for missing functions (ORDR 0xffff).
        public bool start(int func)
        {
            // JS: ordr[func] out of range is undefined
            if (func < 0 || func >= this.script.ordr.Length) return false;
            int offset = this.script.ordr[func];
            if (offset == 0xffff || offset + 1 >= this.script.data.Length) return false;
            this.ip = offset + 1;
            return true;
        }

        // The word before a function's first instruction: the block event mask.
        public int entryFlags()
        {
            return this.script.data[this.ip - 1];
        }

        public int arg(int index)
        {
            // JS: stack[i] out of range is undefined (0 in the integer math callers do)
            int i = this.sp + index;
            return i >= 0 && i < this.stack.Length ? this.stack[i] : 0;
        }

        public string argString(int index)
        {
            var text = this.script.text;
            int table = this.arg(index) << 1;
            // JS: text[i] out of range is undefined, which | and << treat as 0
            int t(int i) => i >= 0 && i < text.Length ? text[i] : 0;
            int at = (t(table) << 8) | t(table + 1);
            var value = new StringBuilder();
            while (at < text.Length && text[at] != 0) value.Append((char)text[at++]);
            return value.ToString();
        }
    }
}
