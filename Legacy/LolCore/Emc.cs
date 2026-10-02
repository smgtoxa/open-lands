// Westwood EMC bytecode: the IFF container and the little stack VM the game's scripts run on.
//
// Transliterated from src/formats/emc.mjs (ScummVM's EMCInterpreter). The opcode numbering, the
// stack layout and the sign extensions are load-bearing: a subtly wrong one produces a level that
// loads but is quietly missing a wall, so this stays a literal translation.
namespace LolCore;

public sealed class EmcScript
{
    public ushort[] Ordr;
    public ushort[] Data;
    public byte[] Text = Array.Empty<byte>();

    public static EmcScript Decode(byte[] bytes)
    {
        if (bytes.Length < 12 || System.Text.Encoding.ASCII.GetString(bytes, 0, 4) != "FORM")
            throw new InvalidDataException("Not an IFF script");
        var chunks = new Dictionary<string, byte[]>();
        int offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
            int size = (bytes[offset + 4] << 24) | (bytes[offset + 5] << 16) | (bytes[offset + 6] << 8) | bytes[offset + 7];
            if (offset + 8 + size > bytes.Length) throw new InvalidDataException($"Truncated {id} chunk");
            chunks[id] = bytes.AsSpan(offset + 8, size).ToArray();
            offset += 8 + size + (size & 1);
        }
        if (!chunks.ContainsKey("ORDR") || !chunks.ContainsKey("DATA")) throw new InvalidDataException("Script lacks ORDR or DATA");
        static ushort[] Words(byte[] chunk)
        {
            var words = new ushort[chunk.Length >> 1];
            for (int i = 0; i < words.Length; i += 1) words[i] = (ushort)((chunk[i * 2] << 8) | chunk[i * 2 + 1]);
            return words;
        }
        return new EmcScript
        {
            Ordr = Words(chunks["ORDR"]),
            Data = Words(chunks["DATA"]),
            Text = chunks.TryGetValue("TEXT", out var text) ? text : Array.Empty<byte>(),
        };
    }
}

public sealed class EmcState
{
    public const int StackSize = 100;

    public readonly EmcScript Script;
    public int Ip = -1;
    public int RetValue;
    public int Bp = StackSize + 1;
    public int Sp = StackSize - 1;
    public readonly short[] Regs = new short[30];
    public readonly short[] Stack = new short[StackSize];

    public EmcState(EmcScript script) => Script = script;

    /// <summary>Function entry; false for a function the script does not define (ORDR 0xffff).</summary>
    public bool Start(int func)
    {
        if (func < 0 || func >= Script.Ordr.Length) return false;
        int offset = Script.Ordr[func];
        if (offset == 0xffff || offset + 1 >= Script.Data.Length) return false;
        Ip = offset + 1;
        return true;
    }

    /// <summary>The word before a function's first instruction: the block event mask.</summary>
    public int EntryFlags() => Script.Data[Ip - 1];

    /// <summary>
    /// Set by an opcode that is still waiting for the player. Emc.Run stops at the next instruction
    /// boundary and leaves Ip where it is, so the same call is made again when the script resumes.
    /// This is how a conversation waits a frame in a VM that has no coroutines.
    /// </summary>
    public bool Yield;

    /// <summary>Where the instruction being executed began, so a yield can repeat it.</summary>
    public int InstructionIp;

    /// <summary>Stops the script here and makes the same call again when it resumes.</summary>
    public void YieldAndRepeat() { Yield = true; Ip = InstructionIp; }

    /// <summary>True while this state has somewhere left to go.</summary>
    public bool Running => Ip >= 0;

    public int Arg(int index) => index >= 0 && Sp + index < StackSize ? Stack[Sp + index] : 0;

    public string ArgString(int index)
    {
        var text = Script.Text;
        int table = Arg(index) << 1;
        if (table + 1 >= text.Length) return "";
        int at = (text[table] << 8) | text[table + 1];
        var value = new System.Text.StringBuilder();
        while (at < text.Length && text[at] != 0) value.Append((char)text[at++]);
        return value.ToString();
    }
}

/// <summary>The VM itself. `sysFunc` is the opcode table: id and state in, int result out.</summary>
public static class Emc
{
    public static void Run(EmcState state, Func<int, EmcState, int> sysFunc)
    {
        var data = state.Script.Data;
        var stack = state.Stack;
        int guard = 0;
        state.Yield = false;
        while (state.Ip >= 0)
        {
            if (state.Yield) return;
            state.InstructionIp = state.Ip;
            if (state.Ip >= data.Length) throw new InvalidDataException($"Script execution outside DATA at {state.Ip}");
            if (++guard > 1000000) throw new InvalidDataException("Script did not terminate");
            int code = (short)data[state.Ip++];
            int opcode = (code >> 8) & 0x1f;
            int param = 0;
            if ((code & 0x8000) != 0) { opcode = 0; param = code & 0x7fff; }
            else if ((code & 0x4000) != 0) param = (sbyte)code;
            else if ((code & 0x2000) != 0) param = (short)data[state.Ip++];

            switch (opcode)
            {
                case 0: state.Ip = param; break;
                case 1: state.RetValue = param; break;
                case 2:
                    if (param == 0) stack[--state.Sp] = (short)state.RetValue;
                    else if (param == 1)
                    {
                        stack[--state.Sp] = (short)(state.Ip + 1);
                        stack[--state.Sp] = (short)state.Bp;
                        state.Bp = state.Sp + 2;
                    }
                    else state.Ip = -1;
                    break;
                case 3: case 4: stack[--state.Sp] = (short)param; break;
                case 5: stack[--state.Sp] = state.Regs[param]; break;
                case 6: stack[--state.Sp] = stack[state.Bp - (param + 2)]; break;
                case 7: stack[--state.Sp] = stack[state.Bp + param - 1]; break;
                case 8:
                    if (param == 0) state.RetValue = stack[state.Sp++];
                    else if (param == 1 && state.Sp < EmcState.StackSize - 1)
                    {
                        state.Bp = stack[state.Sp++];
                        state.Ip = stack[state.Sp++];
                    }
                    else state.Ip = -1;
                    break;
                case 9: state.Regs[param] = stack[state.Sp++]; break;
                case 10: stack[state.Bp - (param + 2)] = stack[state.Sp++]; break;
                case 11: stack[state.Bp + param - 1] = stack[state.Sp++]; break;
                case 12: state.Sp += param; break;
                case 13: state.Sp -= param; break;
                case 14:
                    state.RetValue = sysFunc(param & 0xff, state);
                    guard = 0; // only loops without system calls can hang the VM
                    break;
                case 15: if (stack[state.Sp++] == 0) state.Ip = param & 0x7fff; break;
                case 16:
                {
                    int value = stack[state.Sp];
                    if (param == 0) stack[state.Sp] = (short)(value != 0 ? 0 : 1);
                    else if (param == 1) stack[state.Sp] = (short)(-value);
                    else if (param == 2) stack[state.Sp] = (short)(~value);
                    else state.Ip = -1;
                    break;
                }
                case 17:
                {
                    int a = stack[state.Sp++];
                    int b = stack[state.Sp++];
                    int result = param switch
                    {
                        0 => b != 0 && a != 0 ? 1 : 0,
                        1 => b != 0 || a != 0 ? 1 : 0,
                        2 => a == b ? 1 : 0,
                        3 => a != b ? 1 : 0,
                        4 => a > b ? 1 : 0,
                        5 => a >= b ? 1 : 0,
                        6 => a < b ? 1 : 0,
                        7 => a <= b ? 1 : 0,
                        8 => a + b,
                        9 => b - a,
                        10 => a * b,
                        11 => a != 0 ? b / a : 0,
                        12 => b >> a,
                        13 => b << a,
                        14 => a & b,
                        15 => a | b,
                        16 => a != 0 ? b % a : 0,
                        17 => a ^ b,
                        _ => int.MinValue,
                    };
                    if (result == int.MinValue) state.Ip = -1;
                    else stack[--state.Sp] = (short)result;
                    break;
                }
                case 18:
                    if (state.Sp < EmcState.StackSize - 1)
                    {
                        state.RetValue = stack[state.Sp++];
                        state.Ip = stack[state.Sp++] & 0xffff;
                        stack[EmcState.StackSize - 1] = 0;
                    }
                    else state.Ip = -1;
                    break;
                default:
                    throw new InvalidDataException($"Unknown script opcode {opcode}");
            }
        }
    }
}
