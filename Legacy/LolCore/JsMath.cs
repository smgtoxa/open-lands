// JavaScript's rounding, where it matters.
//
// Math.round in JavaScript rounds a half *up* (floor(x + 0.5)); .NET's Math.Round rounds a half to
// the nearest even number. The difference is one point of damage on exactly the casts that land on
// a half - which is the sort of thing that is invisible until a parity harness says "one monster
// row differs".
namespace LolCore;

public static class JsMath
{
    /// <summary>JavaScript's Math.round: half goes up, including for negatives (-0.5 -> 0).</summary>
    public static double Round(double value) => Math.Floor(value + 0.5);

    public static int RoundToInt(double value) => (int)Math.Floor(value + 0.5);
}
