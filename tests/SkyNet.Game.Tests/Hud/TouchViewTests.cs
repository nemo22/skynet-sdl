using System.Numerics;
using System.Reflection;
using RetroEng.Platform;
using RetroEng.Render;
using SkyNet.Game.Hud;
using Xunit;

namespace SkyNet.Game.Tests.Hud;

public class TouchViewTests
{
    /// <summary>The phone's window (its frame is 2138×954 at a render scale): fingers come in window pixels.</summary>
    private static readonly Vector2 Window = new(2376f, 1060f);

    [Fact]
    public void TouchedStickIsDrawnUnderTheFinger()
    {
        var touch = new TouchControls();
        var finger = new Vector2(400f, 700f);

        // The engine's finger lands (internal: driven as Window does).
        typeof(TouchControls).GetMethod("Down", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(touch, [1UL, finger, Window, new Input(touch.Actions)]);

        var (centre, knob) = TouchView.StickDrawn(touch, Window);

        var expected = finger * (Renderer.CanvasHeight / Window.Y);
        Assert.Equal(expected.X, centre.X, 2);
        Assert.Equal(expected.Y, centre.Y, 2);
        Assert.Equal(centre, knob);
    }
}
