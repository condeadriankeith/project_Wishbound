using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Second-generation village kit pieces that sit on top of VillageKit: the hand cart settlers push around
/// for hauling/relay jobs, ground crates/bundles waiting to be picked up, and the little produce stack that
/// shows what a cart or crate is actually carrying. Same low-poly chamfer/blob/ring toon style as VillageKit.
/// </summary>
public static class VillageKitV2
{
    static Material Wood => VillageKit.Mat(VillageKit.Timber);
    static Material WoodDark => VillageKit.Mat(VillageKit.TimberDark);
    static Material Iron => VillageKit.Mat(new Color(.3f, .3f, .33f));
    static Mesh Box => VillageKit.ChamferBox(.12f);

    // ================================================================== hand cart
    /// <summary>Builds a two-wheeled hand cart (body, shafts/handles, wheels) with a HandCart behaviour wired up,
    /// parked at 'pos'. Matches the shapes HandCart.cs expects to drive (wheelL/wheelR/handles/body/loadRoot).</summary>
    public static GameObject Cart(Vector3 pos, Transform parent, int seed = 1)
    {
        var rng = new System.Random(seed);
        var root = new GameObject("HandCart"); root.transform.SetParent(parent, false);
        root.transform.position = VillageKit.Ground(pos);
        root.AddComponent<VillageKitPiece>();
        var cart = root.AddComponent<HandCart>();

        var body = new GameObject("Body"); body.transform.SetParent(root.transform, false);
        // bed
        Part(body.transform, "Bed", Box, Wood, new Vector3(0, .42f, -.1f), Quaternion.identity, new Vector3(1.0f, .32f, 1.55f), true);
        Part(body.transform, "BedFloor", Box, WoodDark, new Vector3(0, .27f, -.1f), Quaternion.identity, new Vector3(.94f, .05f, 1.48f));
        // side slats (low, so cargo reads above the rim)
        foreach (var side in new[] { -1f, 1f })
            Part(body.transform, "Side", Box, Wood, new Vector3(side * .47f, .5f, -.1f), Quaternion.identity, new Vector3(.05f, .18f, 1.45f));
        Part(body.transform, "Back", Box, Wood, new Vector3(0, .5f, -.82f), Quaternion.identity, new Vector3(.9f, .18f, .05f));
        // axle + wheels
        var axle = Part(root.transform, "Axle", VillageKit.Ring(8, .6f), Iron, new Vector3(.575f, cart.wheelRadius, -.3f), Quaternion.Euler(0, 0, 90), new Vector3(.05f, 1.15f, .05f));
        GameObject Wheel(float side)
        {
            var w = new GameObject("Wheel"); w.transform.SetParent(root.transform, false);
            w.transform.localPosition = new Vector3(side * .58f, cart.wheelRadius, -.3f);
            w.transform.localRotation = Quaternion.Euler(0, 0, 90);
            Part(w.transform, "Rim", VillageKit.Ring(10, .72f), WoodDark, Vector3.zero, Quaternion.identity, new Vector3(cart.wheelRadius, .09f, cart.wheelRadius));
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f;
                Part(w.transform, "Spoke", Box, Wood, Vector3.zero, Quaternion.Euler(0, a, 0), new Vector3(.62f, .035f, .035f));
            }
            Part(w.transform, "Hub", VillageKit.Blob(seed + (int)side, .05f), Iron, Vector3.zero, Quaternion.identity, Vector3.one * .12f);
            return w;
        }
        var wheelL = Wheel(-1f); var wheelR = Wheel(1f);
        // shafts + crossbar handles, dropped when parked
        var handles = new GameObject("Handles"); handles.transform.SetParent(root.transform, false); handles.transform.localPosition = new Vector3(0, .4f, .62f);
        foreach (var side in new[] { -1f, 1f })
            Part(handles.transform, "Shaft", Box, WoodDark, new Vector3(side * .34f, 0, .78f), Quaternion.identity, new Vector3(.08f, .08f, 1.95f));
        Part(handles.transform, "Crossbar", Box, WoodDark, new Vector3(0, 0, 1.5f), Quaternion.identity, new Vector3(.76f, .07f, .07f));

        var loadRoot = new GameObject("LoadRoot"); loadRoot.transform.SetParent(body.transform, false); loadRoot.transform.localPosition = new Vector3(0, .43f, -.1f);

        cart.wheelL = wheelL.transform; cart.wheelR = wheelR.transform; cart.handles = handles.transform; cart.body = body.transform; cart.loadRoot = loadRoot.transform;
        return root;
    }

    /// <summary>Rebuilds the visible produce/cargo stack on a cart's LoadRoot to match cargoRes/cargoQty.
    /// Called by HandCart whenever its cargo changes.</summary>
    public static void RefreshCartLoad(HandCart cart)
    {
        if (!cart || !cart.loadRoot) return;
        for (int i = cart.loadRoot.childCount - 1; i >= 0; i--) Object.Destroy(cart.loadRoot.GetChild(i).gameObject);
        if (!cart.HasCargo) return;
        var rng = new System.Random(cart.GetInstanceID());
        int shown = Mathf.Clamp(cart.cargoQty, 1, 8);
        Color c = ResInfo.Colour(cart.cargoRes);
        var mat = VillageKit.Mat(c);
        for (int i = 0; i < shown; i++)
        {
            float row = i % 3, col = i / 3;
            Vector3 p = new Vector3(Mathf.Lerp(-.32f, .32f, row / 2f), .12f + col * .22f, Mathf.Lerp(-.5f, .5f, (float)rng.NextDouble()));
            var g = new GameObject("Cargo"); g.transform.SetParent(cart.loadRoot, false);
            g.transform.localPosition = p; g.transform.localRotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360, 0);
            g.AddComponent<MeshFilter>().sharedMesh = VillageKit.Blob(rng.Next(), .14f);
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            g.transform.localScale = Vector3.one * Mathf.Lerp(.16f, .22f, (float)rng.NextDouble());
        }
    }

    // ================================================================== ground pickups (crates / bundles)
    /// <summary>A small crate/bundle of produce sitting on the ground, ready to be hauled. Returns the root
    /// (carries a Pickup component so VillageWorld's job board picks it up automatically).</summary>
    public static GameObject Crate(Res res, int qty, Vector3 pos, Transform parent, int seed = 1)
    {
        var rng = new System.Random(seed == 1 ? pos.GetHashCode() : seed);
        var root = new GameObject("Crate_" + res); root.transform.SetParent(parent, false);
        root.transform.position = VillageKit.Ground(pos); root.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360, 0);
        root.AddComponent<VillageKitPiece>();
        var pickup = root.AddComponent<Pickup>(); pickup.res = res; pickup.qty = qty;

        bool crateBox = res == Res.Wood || res == Res.Water || res == Res.Bread;
        if (crateBox)
        {
            Part(root.transform, "CrateBody", Box, Wood, new Vector3(0, .14f, 0), Quaternion.identity, new Vector3(.38f, .28f, .38f), true);
            foreach (var x in new[] { -1f, 1f }) foreach (var z in new[] { -1f, 1f })
                Part(root.transform, "Post", Box, WoodDark, new Vector3(x * .17f, .14f, z * .17f), Quaternion.identity, new Vector3(.04f, .3f, .04f));
        }
        else
        {
            Part(root.transform, "Basket", VillageKit.Ring(10, .85f), VillageKit.Mat(VillageKit.Wicker), new Vector3(0, .13f, 0), Quaternion.identity, new Vector3(.34f, .24f, .34f), true);
        }
        int show = Mathf.Clamp(qty, 2, 6);
        var mat = VillageKit.Mat(ResInfo.Colour(res));
        for (int i = 0; i < show; i++)
        {
            float a = i * Mathf.PI * 2 / show;
            Vector3 lp = new Vector3(Mathf.Cos(a) * .12f, .3f + (i % 2) * .09f, Mathf.Sin(a) * .12f);
            var g = new GameObject("Produce"); g.transform.SetParent(root.transform, false); g.transform.localPosition = lp;
            g.AddComponent<MeshFilter>().sharedMesh = VillageKit.Blob(rng.Next(), .14f);
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            g.transform.localScale = Vector3.one * Mathf.Lerp(.14f, .2f, (float)rng.NextDouble());
        }
        return root;
    }

    static GameObject Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 localPos, Quaternion localRot, Vector3 scale, bool collider = false)
    {
        var g = new GameObject(name); g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos; g.transform.localRotation = localRot; g.transform.localScale = scale;
        g.AddComponent<MeshFilter>().sharedMesh = mesh; g.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (collider) { var c = g.AddComponent<BoxCollider>(); }
        return g;
    }
}
