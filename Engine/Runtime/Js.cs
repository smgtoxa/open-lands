// JavaScript semantics the transliteration needs where C# differs. Use these instead of guessing:
//   Math.floor(a / b) on ints       -> Js.FloorDiv(a, b)      (C# int division truncates)
//   Math.round(x)                   -> Js.Round(x)            (C# Math.Round is banker's rounding)
//   x >>> n                         -> Js.Ushr(x, n)
//   Math.imul(a, b)                 -> Js.Imul(a, b)
//   (x << 16) >> 16, (x << 24) >> 24 -> (short)x, (sbyte)x    (same thing, spelled plainly)
//   String(n).padStart(w, "0")      -> n.ToString().PadLeft(w, '0')
//   Math.trunc(x) / x | 0           -> (int)x                 (for values in int range)
using System;
using System.Collections.Generic;
using System.Linq;

namespace Lol
{
    public static class Js
    {
        public static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);
        public static int Floor(double x) => (int)Math.Floor(x);
        public static int Ceil(double x) => (int)Math.Ceiling(x);
        public static int Round(double x) => (int)Math.Floor(x + 0.5);
        public static double RoundD(double x) => Math.Floor(x + 0.5);
        public static int Ushr(int x, int n) => (int)((uint)x >> (n & 31));
        public static uint UshrU(int x, int n) => (uint)x >> (n & 31);
        public static int Imul(int a, int b) => unchecked(a * b);
        public static int Sign(double x) => x > 0 ? 1 : x < 0 ? -1 : 0;
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>JS truthiness of a number.</summary>
        public static bool T(int v) => v != 0;
        public static bool T(double v) => v != 0 && !double.IsNaN(v);
        public static bool T(object v) => v != null;

        /// <summary>String.fromCharCode for a byte string (the game's text is 8-bit).</summary>
        public static string Chr(int c) => ((char)(c & 0xffff)).ToString();

        /// <summary>typedArray.fill(value, start, end)</summary>
        public static void Fill<T>(T[] a, T value, int start = 0, int end = int.MaxValue)
        {
            if (start < 0) start += a.Length;
            if (end < 0) end += a.Length;
            end = Math.Min(end, a.Length);
            for (int i = Math.Max(0, start); i < end; i += 1) a[i] = value;
        }

        /// <summary>typedArray.set(source, offset)</summary>
        public static void Set<T>(T[] dst, IList<T> src, int offset = 0)
        {
            for (int i = 0; i < src.Count; i += 1) dst[offset + i] = src[i];
        }

        /// <summary>typedArray.subarray / slice(start, end): a copy (only use where the JS does not write through it).</summary>
        public static T[] Slice<T>(T[] a, int start, int end = int.MaxValue)
        {
            if (start < 0) start = Math.Max(0, a.Length + start);
            if (end < 0) end = a.Length + end;
            end = Math.Min(end, a.Length);
            if (end <= start) return Array.Empty<T>();
            var r = new T[end - start];
            Array.Copy(a, start, r, 0, r.Length);
            return r;
        }

        public static int IndexOf<T>(T[] a, T v) => Array.IndexOf(a, v);
        public static bool Includes<T>(IEnumerable<T> a, T v) => a.Contains(v);
    }

    /// <summary>A view into an array at an offset, for JS code that passes `buf.subarray(o)` and writes through it.</summary>
    public readonly struct Span16
    {
        public readonly ushort[] Data;
        public readonly int Offset;
        public Span16(ushort[] data, int offset) { Data = data; Offset = offset; }
        public ushort this[int i] { get => Data[Offset + i]; set => Data[Offset + i] = value; }
        public int Length => Data.Length - Offset;
    }

    /// <summary>A byte view (Uint8Array.subarray) that reads and writes the original.</summary>
    public readonly struct Bytes
    {
        public readonly byte[] Data;
        public readonly int Offset;
        public readonly int Length;
        public Bytes(byte[] data) : this(data, 0, data?.Length ?? 0) { }
        public Bytes(byte[] data, int offset, int length) { Data = data; Offset = offset; Length = length; }
        public byte this[int i] { get => Data[Offset + i]; set => Data[Offset + i] = value; }
        public Bytes Sub(int start, int end = int.MaxValue) { end = Math.Min(end, Length); return new Bytes(Data, Offset + start, Math.Max(0, end - start)); }
        public byte[] ToArray() { var r = new byte[Length]; Array.Copy(Data, Offset, r, 0, Length); return r; }
        public static implicit operator Bytes(byte[] a) => new Bytes(a);
    }
}
