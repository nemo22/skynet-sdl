using System.Numerics;
using RetroEng.Platform;
using SkyNet.Game.Hud;
using SkyNet.Game.Menu;

namespace SkyNet.Game.Tests.Hud;

/// <summary>The on-screen controls' places: the engine's grid as defaults, the player's places
/// kept to the screen and their sizes, the settings' text, and the editor's drags.</summary>
public class TouchLayoutTests
{
    private static readonly Vector2 Screen = new(640f, 360f);
    private const float Side = 50f;
    private static readonly TouchRoom Room = new(Screen, Side, Screen.Y);

    [Fact]
    public void GridMatchesTheEnginesColumnsAndRows()
    {
        // The engine's grid: margin 0.18 of a button, rows over the bottom 20 % (the radar's room).
        var margin = 0.18f * Side;
        var step = Side + margin;
        var centre = TouchLayout.Centre(TouchLayout.Grid(1, 2), Screen, Side);
        Assert.Equal(Screen.X - margin - Side / 2f - step, centre.X, 3);
        Assert.Equal(0.8f * Screen.Y - Side / 2f - 2 * step, centre.Y, 3);
    }

    [Fact]
    public void SpotKeepsToTheNearerCorner()
    {
        var spot = TouchLayout.SpotAt("FIRE", new Vector2(600f, 300f), 1f, Room);
        Assert.Equal(new Vector2(1f, 1f), spot.Place.Anchor);
        Assert.Equal(new Vector2(-0.8f, -1.2f), spot.Place.Offset);

        // The same distance from the corner on a wider screen, in button sides.
        var wide = new Vector2(800f, 360f);
        Assert.Equal(new Vector2(760f, 300f), TouchLayout.Centre(spot, wide, Side));
    }

    [Fact]
    public void SpotStaysOnTheScreen()
    {
        var spot = TouchLayout.SpotAt("FIRE", new Vector2(-100f, 900f), 2f, Room);
        Assert.Equal(new Vector2(Side, Screen.Y - Side), TouchLayout.Centre(spot, Screen, Side));
    }

    [Theory]
    [InlineData(0.1f, TouchLayout.MinSize)]
    [InlineData(5f, TouchLayout.MaxSize)]
    [InlineData(1.3f, 1.3f)]
    public void SizeIsKeptInRange(float asked, float kept) =>
        Assert.Equal(kept, TouchLayout.SpotAt("FIRE", new Vector2(320f, 180f), asked, Room).Size, 3);

    [Fact]
    public void SavedTextRoundTrips()
    {
        var layout = new TouchLayout(new Dictionary<string, string>());
        var spot = TouchLayout.SpotAt("FIRE", new Vector2(100f, 120f), 1.5f, Room);
        layout.Set("FIRE", spot);
        var again = new TouchLayout(layout.Save());
        Assert.Equal(spot, again.SpotOf("FIRE", TouchLayout.Grid(0, 0)));
        Assert.Equal(TouchLayout.Grid(1, 0), again.SpotOf("JUMP", TouchLayout.Grid(1, 0)));
    }

    [Theory]
    [InlineData("1,1,-1,-1")]
    [InlineData("2,1,-1,-1,1")]
    [InlineData("1,1,x,-1,1")]
    [InlineData("1,1,-1,-1,9")]
    [InlineData("1,1,-999,-1,1")]
    public void BrokenTextIsTheDefault(string text)
    {
        var layout = new TouchLayout(new Dictionary<string, string> { ["FIRE"] = text });
        Assert.Equal(TouchLayout.Grid(0, 0), layout.SpotOf("FIRE", TouchLayout.Grid(0, 0)));
        Assert.Empty(layout.Save());
    }

    [Fact]
    public void StickIsFloatingUntilPlaced()
    {
        var layout = new TouchLayout(new Dictionary<string, string>());
        Assert.Null(layout.Stick);
        layout.Set(TouchLayout.StickId, TouchLayout.SpotAt(TouchLayout.StickId, new Vector2(100f, 250f), 1f, Room));
        Assert.NotNull(layout.Stick);
        layout.Reset();
        Assert.Null(layout.Stick);
    }

    [Fact]
    public void SlotIsPlaced()
    {
        var layout = new TouchLayout(new Dictionary<string, string>());
        var slot = layout.Slot(3, "FIRE", 0, 0);
        Assert.Equal(TouchLayout.Grid(0, 0).Place, slot.Place);
        Assert.Equal(1f, slot.Size);
    }

    [Fact]
    public void EditorDragsAControlAndSaves()
    {
        var editor = Editor();
        var fire = editor.AreaOf("FIRE");
        var from = new Vector2(fire.X + fire.Width / 2f, fire.Y + fire.Height / 2f);
        editor.Press(from);
        editor.Drag(from - new Vector2(200f, 0f));
        editor.Release();

        Assert.Equal("FIRE", editor.Selected);
        Assert.Equal(fire.X - 200f, editor.AreaOf("FIRE").X, 2);
        Assert.True(editor.Changed);
        Assert.Contains("FIRE", editor.Layout.Save().Keys);
    }

    [Fact]
    public void EditorGrowsTheSelectedAndResets()
    {
        var editor = Editor();
        var fire = editor.AreaOf("FIRE");
        editor.Press(new Vector2(fire.X + 2f, fire.Y + 2f));
        editor.Release();
        Assert.Equal(TouchEditor.Outcome.Editing, editor.Use(TouchEditor.Tool.Bigger));
        Assert.True(editor.AreaOf("FIRE").Width > fire.Width);

        editor.Use(TouchEditor.Tool.Reset);
        Assert.Equal(fire, editor.AreaOf("FIRE"));
        Assert.False(editor.Changed);
    }

    [Fact]
    public void SpotStaysAboveThePanel()
    {
        // A panel from y 300: a 1.3 button snaps its bottom to the panel's top.
        var room = Room with { Floor = 300f };
        var spot = TouchLayout.SpotAt("FIRE", new Vector2(600f, 340f), 1.3f, room);
        Assert.Equal(300f - 1.3f * Side / 2f, TouchLayout.Centre(spot, Screen, Side).Y, 3);
    }

    [Fact]
    public void PauseDefaultMatchesTheEngine()
    {
        // The engine's pause: the grid's top-right square, margin 0.18 of a button from both edges.
        var margin = 0.18f * Side;
        var centre = TouchLayout.Centre(TouchLayout.PauseDefault, Screen, Side);
        Assert.Equal(new Vector2(Screen.X - margin - Side / 2f, margin + Side / 2f), centre);
    }

    [Fact]
    public void ApplyPutsThePlacesOnTheEngine()
    {
        var actions = new ActionSet();
        var fire = actions.Add("Fire");
        actions.AddTouch(fire, "FIRE", 0, 0).AddTouch(actions.Add("Jump"), "JUMP", 1, 0);
        var touch = new TouchControls();
        var layout = new TouchLayout(new Dictionary<string, string>
        {
            ["FIRE"] = "0,1,2,-2,1.5",
            [TouchLayout.PauseId] = "0,0,1,1,0.8",
            // A layout saved before the stick could be resized: size 1.
            [TouchLayout.StickId] = "0,1,2,-3,1",
        });

        layout.Apply(actions, touch);
        Assert.Equal(new TouchPlace(new Vector2(0f, 1f), new Vector2(2f, -2f)), actions.Touch[0].Place);
        Assert.Equal(1.5f, actions.Touch[0].Size);
        Assert.Equal(TouchLayout.Grid(1, 0).Place, actions.Touch[1].Place);
        Assert.Equal(new TouchPlace(Vector2.Zero, Vector2.One), touch.PausePlace);
        Assert.Equal(0.8f, touch.PauseSize);
        Assert.Equal(new TouchPlace(new Vector2(0f, 1f), new Vector2(2f, -3f)), touch.FixedStick);
        Assert.Equal(1f, touch.StickScale);

        // Every default back: the grid, the top-right pause, a floating stick.
        new TouchLayout(new Dictionary<string, string>()).Apply(actions, touch);
        Assert.Equal(TouchLayout.Grid(0, 0).Place, actions.Touch[0].Place);
        Assert.Null(touch.PausePlace);
        Assert.Null(touch.FixedStick);
    }

    [Fact]
    public void OnlyFireAims()
    {
        var actions = SkyNet.Game.Campaign.CampaignControls.Create().Actions;

        var aiming = actions.Touch.Where(s => s.Aim == TouchAim.Look).Select(s => s.Label);
        Assert.Equal(["FIRE"], aiming);
    }

    [Fact]
    public void ApplyKeepsTheAim()
    {
        var actions = SkyNet.Game.Campaign.CampaignControls.Create().Actions;

        new TouchLayout(new Dictionary<string, string> { ["FIRE"] = "0,1,2,-2,1.5" }).Apply(actions, new TouchControls());
        Assert.Equal(TouchAim.Look, actions.Touch.Single(s => s.Label == "FIRE").Aim);
    }

    [Fact]
    public void FitBringsAHandEditedSpotBack()
    {
        // 90 buttons left of the right edge: past a 640-pixel screen.
        var layout = new TouchLayout(new Dictionary<string, string> { ["FIRE"] = "1,1,-90,-1,1" });
        layout.Fit(Room);
        var centre = TouchLayout.Centre(layout.SpotOf("FIRE", TouchLayout.Grid(0, 0)), Screen, Side);
        Assert.Equal(Side / 2f, centre.X, 3);
    }

    [Fact]
    public void EditorTapDoesNotMove()
    {
        // A tap's wobble (under the drag start) selects but leaves the stick floating.
        var editor = Editor();
        var stick = editor.AreaOf(TouchLayout.StickId);
        var from = new Vector2(stick.X + stick.Width / 2f, stick.Y + stick.Height / 2f);
        editor.Press(from);
        editor.Drag(from + new Vector2(5f, 5f));
        editor.Release();

        Assert.Equal(TouchLayout.StickId, editor.Selected);
        Assert.Null(editor.Layout.Stick);
        Assert.False(editor.Changed);
    }

    [Fact]
    public void EditorResetsTheSelectedThenAll()
    {
        var editor = Editor();
        Drag(editor, "FIRE", new Vector2(-200f, 0f));
        Drag(editor, "JUMP", new Vector2(0f, -100f));

        // JUMP selected: only JUMP goes back; again: every control.
        editor.Use(TouchEditor.Tool.Reset);
        Assert.Equal(["FIRE"], editor.Layout.Save().Keys);
        editor.Use(TouchEditor.Tool.Reset);
        Assert.Empty(editor.Layout.Save());
    }

    [Fact]
    public void EditorKeepsTheSelectionOnAnEmptyTap()
    {
        var editor = Editor();
        var fire = editor.AreaOf("FIRE");
        editor.Press(new Vector2(fire.X + 2f, fire.Y + 2f));
        editor.Release();
        editor.Press(new Vector2(320f, 180f));
        editor.Release();

        Assert.Equal("FIRE", editor.Selected);
        editor.Use(TouchEditor.Tool.Bigger);
        Assert.True(editor.AreaOf("FIRE").Width > fire.Width);
    }

    [Fact]
    public void EditorResetStandsApartFromSave()
    {
        var tools = Editor().Tools;
        Assert.Equal(TouchEditor.Tool.Reset, tools[0].Tool);
        Assert.True(tools[1].Area.Left - tools[0].Area.Right > tools[2].Area.Left - tools[1].Area.Right);
    }

    [Fact]
    public void EditorResizesTheStick()
    {
        var editor = Editor();
        var stick = editor.AreaOf(TouchLayout.StickId);
        editor.Press(new Vector2(stick.X + stick.Width / 2f, stick.Y + stick.Height / 2f));
        editor.Release();
        editor.Use(TouchEditor.Tool.Bigger);
        Assert.True(editor.AreaOf(TouchLayout.StickId).Width > stick.Width);
    }

    [Fact]
    public void EditorResizesAFloatingStickWithoutPlacingIt()
    {
        var editor = Editor();
        var stick = editor.AreaOf(TouchLayout.StickId);
        editor.Press(new Vector2(stick.X + stick.Width / 2f, stick.Y + stick.Height / 2f));
        editor.Release();
        editor.Use(TouchEditor.Tool.Bigger);
        Assert.Null(editor.Layout.Stick);
        Assert.True(editor.Changed);

        // Saved as a size alone: it loads back floating, at its scale.
        var actions = new ActionSet();
        var touch = new TouchControls();
        new TouchLayout(editor.Layout.Save()).Apply(actions, touch);
        Assert.Null(touch.FixedStick);
        Assert.Equal(1.1f, touch.StickScale, 3);

        editor.Use(TouchEditor.Tool.Reset);
        Assert.Empty(editor.Layout.Save());
    }

    [Theory]
    [InlineData("1.3", 1.3f)]
    [InlineData("9", 1f)]
    [InlineData("x", 1f)]
    public void StickSizeAloneLoads(string text, float scale)
    {
        var touch = new TouchControls();
        new TouchLayout(new Dictionary<string, string> { [TouchLayout.StickId] = text }).Apply(new ActionSet(), touch);
        Assert.Null(touch.FixedStick);
        Assert.Equal(scale, touch.StickScale, 3);
    }

    [Fact]
    public void EditorKeepsButtonsOffThePause()
    {
        // FIRE dragged onto the pause button stops beside it.
        var editor = Editor();
        var pause = editor.AreaOf(TouchLayout.PauseId);
        var fire = editor.AreaOf("FIRE");
        Drag(editor, "FIRE", new Vector2(pause.X - fire.X, pause.Y - fire.Y));
        Assert.False(Overlap(editor.AreaOf("FIRE"), pause));

        // Grown next to it, it still stays off.
        for (var i = 0; i < 10; i++)
        {
            editor.Use(TouchEditor.Tool.Bigger);
        }

        Assert.False(Overlap(editor.AreaOf("FIRE"), pause));
    }

    [Fact]
    public void EditorKeepsThePauseOffButtons()
    {
        var editor = Editor();
        var pause = editor.AreaOf(TouchLayout.PauseId);
        var fire = editor.AreaOf("FIRE");
        Drag(editor, TouchLayout.PauseId, new Vector2(fire.X - pause.X, fire.Y - pause.Y));
        Assert.False(Overlap(editor.AreaOf(TouchLayout.PauseId), editor.AreaOf("FIRE")));

        for (var i = 0; i < 10; i++)
        {
            editor.Use(TouchEditor.Tool.Bigger);
        }

        Assert.False(Overlap(editor.AreaOf(TouchLayout.PauseId), editor.AreaOf("FIRE")));
    }

    [Fact]
    public void EditorMovesThePauseButton()
    {
        var editor = Editor();
        Assert.Contains(TouchLayout.PauseId, editor.Ids);
        var pause = editor.AreaOf(TouchLayout.PauseId);
        Assert.Equal(Screen.X - 0.18f * Side, pause.Right, 3);

        Drag(editor, TouchLayout.PauseId, new Vector2(-300f, 0f));
        Assert.Equal(pause.X - 300f, editor.AreaOf(TouchLayout.PauseId).X, 2);
        Assert.Contains(TouchLayout.PauseId, editor.Layout.Save().Keys);
    }

    [Fact]
    public void EditorToolsComeFirst()
    {
        var editor = Editor();
        var (tool, area) = editor.Tools[^1];
        Assert.Equal(TouchEditor.Tool.Save, tool);
        Assert.Equal(TouchEditor.Outcome.Saved, editor.Press(new Vector2(area.X + 1f, area.Y + 1f)));
    }

    private static TouchEditor Editor()
    {
        var editor = new TouchEditor(new Dictionary<string, string>(), [("FIRE", 0, 0), ("JUMP", 1, 0)]);
        editor.Measure(Room);
        return editor;
    }

    /// <summary>Two areas share more than an edge (a pixel's tolerance).</summary>
    private static bool Overlap(System.Drawing.RectangleF a, System.Drawing.RectangleF b) =>
        System.Drawing.RectangleF.Inflate(a, -1f, -1f).IntersectsWith(b);

    /// <summary>A control dragged from its centre by <paramref name="by"/>.</summary>
    private static void Drag(TouchEditor editor, string id, Vector2 by)
    {
        var area = editor.AreaOf(id);
        var from = new Vector2(area.X + area.Width / 2f, area.Y + area.Height / 2f);
        editor.Press(from);
        editor.Drag(from + by);
        editor.Release();
    }
}
