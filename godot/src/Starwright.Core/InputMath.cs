namespace Starwright.Core;

/// <summary>Wheel normalisation from js/ui/input.js, so camera zoom matches the browser game.</summary>
public static class InputMath
{
    const double LinePx = 16;
    const double ZoomMin = 0.8;
    const double ZoomMax = 1.25;
    const double WheelGain = 0.0022;
    const double PinchGain = 0.012;

    public readonly record struct Wheel(double dx, double dy, double zoom, bool pinch);

    public static Wheel NormalizeWheel(double deltaX, double deltaY, int deltaMode, bool shiftKey, bool ctrlKey, double pageSize = 800)
    {
        var k = deltaMode == 1 ? LinePx : deltaMode == 2 ? pageSize : 1;
        var dx = deltaX * k;
        var dy = deltaY * k;
        if (shiftKey && dy == 0 && dx != 0) { dy = dx; dx = 0; }
        var pinch = ctrlKey;
        var lz = -dy * (pinch ? PinchGain : WheelGain);
        var lim = Math.Log(ZoomMax);
        lz = Math.Max(-lim, Math.Min(lim, lz));
        var zoom = Math.Exp(lz);
        zoom = Math.Max(ZoomMin, Math.Min(ZoomMax, zoom));
        return new Wheel(dx, dy, zoom, pinch);
    }
}
