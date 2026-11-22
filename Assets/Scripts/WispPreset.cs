using UnityEngine;

/// <summary>
/// Single source of truth for the will-o'-the-wisp's look. The tutorial wisp that leads the Star
/// (GuideWisp) and the wisp cursor shown in menus (WishboundTitleMenu) both build their visuals from
/// this recipe, so the cursor is uniform across the game and matches the tutorial wisp exactly.
///
/// The wisp is drawn as layered additive quads (halo / body / core / motes) plus a trailing ribbon
/// and a drifting spark shell. Every value below is shared so no two wisps ever look different.
/// </summary>
public static class WispPreset
{
    public static readonly Color Colour = new Color(1f, 0.85f, 0.5f);

    public static float HaloIntensity => 2.4f;
    public static float GlowRange => 5f;
    public static float PulseSpeed => 3.1f;
    public static float FlickerFreq => 7f;

    /// <summary>Layer recipe: name, scale, inner color, outer color, falloff, rays, intensity, orbit radius, orbit speed, tilt.</summary>
    public struct Layer
    {
        public string name;
        public float scale;
        public Color inner, outer;
        public float falloff, rays, intensity;
        public float orbit, orbitSpeed, tilt;
    }

    public static Layer[] Layers => new Layer[]
    {
        new Layer { name = "Halo",      scale = 3.8f, inner = new Color(.9f, .55f, .25f), outer = new Color(.22f, .18f, .6f), falloff = 3.4f, rays = .55f,  intensity = .30f },
        new Layer { name = "Body",      scale = 1.5f, inner = new Color(1.5f, 1.0f, .45f),  outer = new Color(.5f, .35f, .85f), falloff = 2.6f, rays = .12f,  intensity = .85f },
        new Layer { name = "Core",      scale = .42f, inner = new Color(3f, 2.8f, 2.2f),     outer = new Color(1.6f, 1.1f, .5f),  falloff = 1.8f, rays = 0f,    intensity = 1.1f },
        new Layer { name = "Mote A",    scale = .17f, inner = new Color(1.6f, 2f, 2.6f),     outer = new Color(.4f, .6f, 1.2f),  falloff = 2f,   rays = 0f,    intensity = .9f,  orbit = .46f, orbitSpeed = 2.1f, tilt = .35f },
        new Layer { name = "Mote B",    scale = .13f, inner = new Color(2.4f, 1.8f, 1f),     outer = new Color(.8f, .5f, .3f),  falloff = 2f,   rays = 0f,    intensity = .8f,  orbit = .64f, orbitSpeed = -1.5f, tilt = -.6f },
        new Layer { name = "Mote C",    scale = .11f, inner = new Color(1.6f, 2f, 2.6f),     outer = new Color(.4f, .6f, 1.2f),  falloff = 2f,   rays = 0f,    intensity = .7f,  orbit = .84f, orbitSpeed = 1.1f,  tilt = .9f },
    };

    /// <summary>Trail curve: time -> width multiplier (relative).</summary>
    public static AnimationCurve TrailWidthCurve =>
        new AnimationCurve(new Keyframe(0, .2f), new Keyframe(.35f, .12f), new Keyframe(1, 0));

    /// <summary>Trail colour gradient (warm head -> cyan tail -> violet end).</summary>
    public static Gradient TrailGradient
    {
        get
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, .95f, .8f), 0),
                    new GradientColorKey(new Color(1f, .75f, .35f), .45f),
                    new GradientColorKey(new Color(.5f, .6f, 1f), 1)
                },
                new[] { new GradientAlphaKey(.9f, 0), new GradientAlphaKey(.35f, .5f), new GradientAlphaKey(0, 1) });
            return g;
        }
    }

    /// <summary>Spark colour gradient (warm core -> cyan -> magenta edge).</summary>
    public static Gradient SparkGradient
    {
        get
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            return g;
        }
    }
}
