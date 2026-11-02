using System.Drawing;
using System.Numerics;
using RetroEng.Render;
using SkyNet.Formats;
using SkyNet.Game.Weapons;

/// <summary>
/// The laser rifle's live scope (issue #12, <see cref="LaserScope"/>): the frame's scene again as an
/// inset over the gun's monitor, magnified around the crosshair and mapped to the green ramp.
/// <code>
///  main view ──► × (2 · frame / scope) about the crosshair (the projection's centre)
///            ──► inset over the canvas at the gun's corner + LaserScope.Area
///            ──► ViewRemap: original Table (index → green), enhanced Ramp (brightness → green)
/// </code>
/// </summary>
internal sealed partial class AppHost
{
    // The green table and ramp, made on first use for the data they came from.
    private TextureHandle scopeTable, scopeRamp;
    private GameData? scopeData;

    // A 320 x 200 pixel in 640 x 480 ones.
    private static readonly SizeF HiresPixel = new(640f / 320f, 480f / 200f);

    // Draws the scope over the DOS gun art (not a mod's 3D gun, not in stereo).
    private void DrawScope(FrameSnapshot s, int width, int height)
    {
        if (!LaserScope.Shown(s.ViewModel) || s.Views.Count != 1 || height <= 0)
        {
            return;
        }

        if (viewModel.Placed(s.ViewModel, s.ViewModelPose, width, height, s.HudStyle) is not { } gun || !ScopeRemap())
        {
            return;
        }

        // The monitor on the gun: DOS pixels from the frame's corner, in the 640 x 480 set its screen's
        // (x 2, x 2.4), through the art's pixels into the rectangle the gun is drawn in (the hi-res
        // frame fills its low twin's: 313 x 235 into 150 x 93).
        var frames = viewModel.Frames(LaserScope.Record);
        var frame = s.ViewModelPose.Record == s.ViewModel.Record ? s.ViewModelPose.Frame : 0;
        var area = LaserScope.Area(frame, viewModel.ArtSet);
        var art = frames[0].FromHires ? HiresPixel : new SizeF(1f, 1f);
        var sx = art.Width * gun.Width / frames[0].Width;
        var sy = art.Height * gun.Height / frames[0].Height;
        var screen = new RectangleF(gun.X + area.X * sx, gun.Y + area.Y * sy, area.Width * sx, area.Height * sy);

        // Each scope pixel shows half a frame pixel (LaserScope.Zoom): the crop's clip span widened to ±1.
        var main = s.Views.Centre;
        var zoom = Matrix4x4.CreateScale(LaserScope.Zoom * width / screen.Width, LaserScope.Zoom * height / screen.Height, 1f);
        Matrix4x4.Invert(zoom, out var unzoom);
        var view = new View(main.ViewProjection * zoom, unzoom * main.ClipToDirection, main.Position)
        {
            Remap = new ViewRemap { Table = scopeTable, Ramp = scopeRamp },
        };

        var unit = Renderer.CanvasHeight / height;
        var canvas = new RectangleF(screen.X * unit, screen.Y * unit, screen.Width * unit, screen.Height * unit);
        renderer.BeginInset(view, canvas, InsetContent.Scene, InsetLayer.OverCanvas);
        renderer.EndInset();
    }

    // The green table (original look) and ramp (enhanced), again when a scope freed them or the data changed.
    private bool ScopeRemap()
    {
        if (ReferenceEquals(scopeData, data) && renderer.Exists(scopeTable) && renderer.Exists(scopeRamp))
        {
            return true;
        }

        if (data.LoadPalette() is not { } palette)
        {
            return false;
        }

        // The ramp by average byte: a grey palette's remap gives each 6-bit brightness its green index.
        var grey = new byte[Palette.RawSize];
        for (var i = 0; i < Palette.Colors; i++)
        {
            grey[i * 3] = grey[i * 3 + 1] = grey[i * 3 + 2] = (byte)(i >> 2);
        }

        var green = LaserScope.Remap(Palette.Parse(grey)!);
        var rgba = new byte[Palette.Colors * 4];
        for (var i = 0; i < Palette.Colors; i++)
        {
            var (r, g, b) = palette[green[i]];
            (rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]) = (r, g, b, 255);
        }

        foreach (var old in (ReadOnlySpan<TextureHandle>)[scopeTable, scopeRamp])
        {
            if (renderer.Exists(old))
            {
                renderer.Destroy(old);
            }
        }

        scopeTable = renderer.CreateShadeTable(LaserScope.Remap(palette), 0);
        scopeRamp = renderer.CreatePalette(rgba);
        scopeData = data;
        return true;
    }
}
