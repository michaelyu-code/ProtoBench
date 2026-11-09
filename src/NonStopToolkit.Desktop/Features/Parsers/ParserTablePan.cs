namespace NonStopToolkit.Desktop.Features.Parsers;

/// <summary>Holding the middle button scrolls continuously in the pointer's direction from its starting point.</summary>
public sealed class ParserTablePan
{
    private double anchorX, anchorY, currentX, currentY;
    public bool Active { get; private set; }
    public bool Held { get; private set; }
    public void Begin(double x, double y)
    {
        Active = Held = true;
        anchorX = currentX = x; anchorY = currentY = y;
    }
    public void Move(double x, double y)
    {
        currentX = x; currentY = y;
    }
    public void Release() => Stop();
    public (double X, double Y) AutoStep => Active && Held
        ? (Velocity(currentX - anchorX), Velocity(currentY - anchorY)) : (0, 0);
    private static double Velocity(double distance) => Math.Abs(distance) <= 10 ? 0
        : Math.Clamp((distance - Math.Sign(distance) * 10) * 0.12, -32, 32);
    public void Stop() { Active = Held = false; }
}
