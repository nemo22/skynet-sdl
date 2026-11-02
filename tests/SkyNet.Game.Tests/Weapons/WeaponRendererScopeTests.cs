using System.Reflection;
using System.Runtime.CompilerServices;
using RetroEng.Render;
using SkyNet.Game.Enemies;
using SkyNet.Game.Mods;
using SkyNet.Game.Tests.Behaviour;
using SkyNet.Game.Weapons;

namespace SkyNet.Game.Tests.Weapons;

public class WeaponRendererScopeTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    // Issues #10/#13: the death blast's bank (358) is not preloaded, so its texture is first made
    // inside a map's renderer scope. A map change (a save's load enters its map) releases that scope;
    // a sprite kept past it draws a stale handle, which the renderer drops: no explosion. After
    // MapReleased nothing made in the released scope may be drawn again.
    // Render-free: a Renderer needs a window, so the view is built bare with a renderer-less store.
    [BehaviourDataFact]
    public void A_released_map_drops_the_effect_sprites_made_in_it()
    {
        var view = Bare();
        var before = Field(view, "_textures").GetValue(view);

        view.FramesOf(Enemy.DeathEffectBank);
        view.MapReleased();
        view.FramesOf(Enemy.DeathEffectBank);

        // The blast's texture is made again in the new map's scope, not reused from the old one.
        var now = (TextureStore)Field(view, "_textures").GetValue(view)!;
        Assert.NotSame(before, now);
        Assert.Contains(new TextureRef(Enemy.DeathEffectBank, 0, false), now.Created);
    }

    private static WeaponRenderer Bare()
    {
        var view = (WeaponRenderer)RuntimeHelpers.GetUninitializedObject(typeof(WeaponRenderer));
        Field(view, "_data").SetValue(view, BehaviourData.Data);
        Field(view, "_textures").SetValue(view, new TextureStore(null, BehaviourData.Data));
        Field(view, "_sprites").SetValue(view, new Dictionary<(int, int), (GpuTexture, Material)?>());
        Field(view, "_models").SetValue(view, new Dictionary<string, (ModelMesh, bool)?>(StringComparer.OrdinalIgnoreCase));
        Field(view, "_spriteModels").SetValue(view, new SpriteModelView(null!, default));
        return view;
    }

    private static FieldInfo Field(WeaponRenderer view, string name) =>
        typeof(WeaponRenderer).GetField(name, Private) ?? throw new MissingFieldException(nameof(WeaponRenderer), name);
}
