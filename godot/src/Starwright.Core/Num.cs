namespace Starwright.Core;

public static class Num
{
    public const double Tau = Math.PI * 2;
    public const double G0 = 9.80665;

    public static double Clamp(double x, double lo, double hi) => x < lo ? lo : x > hi ? hi : x;

    /// <summary>JavaScript Math.round: half rounds toward +infinity.</summary>
    public static double JsRound(double x) => Math.Floor(x + 0.5);

    public static bool Finite(double x) => double.IsFinite(x);

    public static int Imul(int a, int b) => unchecked((int)((long)a * (long)b));
}
