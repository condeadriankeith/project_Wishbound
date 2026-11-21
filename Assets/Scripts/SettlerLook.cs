using System.Collections.Generic;
using UnityEngine;

/// <summary>Seeded character palettes, proportions and occupation-specific wardrobes.</summary>
public static class SettlerLook
{
    public enum Age { Child, Adult, Elder }

    public class Outfit
    {
        public SettlerBrain.Role role; public float stature = 1f, build = 1f; public int hairstyle;
        public Age age; public Color skin, hair, shirt, jacket, trousers;
        public bool strawHat, cap, scarf, bun, beard, moustache, apron;
        public Color hatColor = new Color(.88f, .74f, .45f), apronColor = new Color(.86f, .82f, .72f);
    }

    static readonly Color[] Skins = { new Color(1f, .8f, .68f), new Color(.95f, .72f, .58f), new Color(.82f, .58f, .44f), new Color(.62f, .42f, .32f), new Color(1f, .86f, .76f) };
    static readonly Color[] Hairs = { new Color(.35f, .22f, .14f), new Color(.2f, .14f, .1f), new Color(.85f, .45f, .2f), new Color(.92f, .78f, .45f), new Color(.6f, .35f, .2f), new Color(.12f, .1f, .1f) };
    static readonly Color[] Elder = { new Color(.85f, .85f, .83f), new Color(.7f, .7f, .7f), new Color(.92f, .9f, .86f) };
    static readonly Color[] Cloth = { new Color(.45f, .3f, .2f), new Color(.38f, .5f, .32f), new Color(.3f, .4f, .6f), new Color(.7f, .32f, .28f), new Color(.85f, .62f, .3f), new Color(.5f, .45f, .62f), new Color(.92f, .88f, .78f), new Color(.3f, .52f, .5f) };

    public static Outfit Random(Age age, System.Random rng)
    {
        T Pick<T>(T[] a) => a[rng.Next(a.Length)];
        var o = new Outfit { age = age, skin = Pick(Skins), hair = age == Age.Elder ? Pick(Elder) : Pick(Hairs), shirt = Pick(Cloth), jacket = Pick(Cloth), trousers = Pick(Cloth) * .8f };
        o.trousers.a = 1;
        bool male = rng.NextDouble() < .5;
        double r = rng.NextDouble();
        if (age == Age.Child) { o.cap = r < .3; o.bun = !o.cap && r < .55; }
        else
        {
            o.strawHat = r < .35; o.cap = !o.strawHat && r < .55; o.scarf = !o.strawHat && !o.cap && !male && r < .8;
            o.bun = !male && !o.strawHat && !o.cap && !o.scarf;
            o.beard = male && rng.NextDouble() < (age == Age.Elder ? .7 : .4); o.moustache = male && !o.beard && rng.NextDouble() < .45;
            o.apron = rng.NextDouble() < .45;
        }
        o.hatColor = rng.NextDouble() < .5 ? new Color(.88f, .74f, .45f) : new Color(.55f, .42f, .3f);
        return o;
    }

    // Stable per-character variation, with a readable palette and silhouette for each occupation.
    public static void DressForRole(Outfit o, SettlerBrain.Role role, System.Random rng)
    {
        o.role = role;
        o.stature = Mathf.Lerp(.92f, 1.08f, (float)rng.NextDouble());
        o.build = Mathf.Lerp(.9f, 1.12f, (float)rng.NextDouble());
        o.hairstyle = rng.Next(3);
        o.strawHat = o.cap = o.scarf = o.bun = o.apron = false;
        switch (role)
        {
            case SettlerBrain.Role.Farmer:
                o.strawHat = true; o.apron = true; o.jacket = new Color(.67f,.34f,.18f); o.apronColor = new Color(.3f,.36f,.49f); break;
            case SettlerBrain.Role.Gardener:
                o.scarf = true; o.apron = true; o.jacket = new Color(.3f,.48f,.32f); o.apronColor = new Color(.83f,.70f,.46f); break;
            case SettlerBrain.Role.Fisher:
                o.cap = true; o.jacket = new Color(.22f,.43f,.56f); o.shirt = new Color(.86f,.81f,.64f); break;
            case SettlerBrain.Role.Wanderer:
                o.bun = o.hairstyle == 2; o.jacket = new Color(.65f,.30f,.29f); break;
            case SettlerBrain.Role.Elder:
                o.bun = true; o.jacket = new Color(.44f,.35f,.55f); o.hairstyle = 1; break;
            case SettlerBrain.Role.Kid:
                o.cap = rng.NextDouble() < .5; o.jacket = Color.Lerp(o.jacket, new Color(.94f,.65f,.24f), .55f); break;
        }
        o.jacket = Color.Lerp(o.jacket, o.shirt, .15f);
        o.trousers = new Color(.24f,.25f,.29f);
    }

    /// <summary>Build a fitted NPC wardrobe on its own rig; template is retained for call compatibility.</summary>
    public static Transform Build(Transform template, Transform parent, Outfit o)
    {
        return SettlerWardrobe.Build(parent, o);
    }
}
