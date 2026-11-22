using UnityEngine;

/// <summary>Little hand-held tools for settlers (hoe, watering can, bucket, fishing rod, broom), made from kit shapes.</summary>
public static class SettlerTools
{
    public static GameObject Make(string kind, Transform hand)
    {
        var root = new GameObject("Tool_" + kind); root.transform.SetParent(hand, false);
        // the hand bone's local axes vary, so lay tools out in the character's space and keep them upright-ish
        root.transform.localPosition = Vector3.zero;
        var body = hand.root;
        Transform avatar = hand; while (avatar.parent && avatar.name != "Body") avatar = avatar.parent;
        root.transform.rotation = avatar.rotation;
        var grip = root.AddComponent<SettlerToolGrip>(); grip.avatar = avatar; grip.rig = hand.GetComponentInParent<SettlerRig>(); grip.kind = kind;
        // the rig's bones carry the FBX's unit scale - cancel it so tools come out at true size (times the settler's own size)
        float boneScale = Mathf.Max(1e-4f, hand.lossyScale.x), bodyScale = avatar.parent ? avatar.parent.lossyScale.x : 1f;
        root.transform.localScale = Vector3.one * (bodyScale / boneScale);
        var wood = VillageKit.Mat(new Color(.6f, .42f, .25f)); var iron = VillageKit.Mat(new Color(.55f, .56f, .6f)); var box = VillageKit.ChamferBox(.2f);
        GameObject P(string n, Mesh m, Material mat, Vector3 lp, Vector3 sc, Vector3 e)
        {
            var g = new GameObject(n); g.transform.SetParent(root.transform, false); g.transform.localPosition = lp; g.transform.localRotation = Quaternion.Euler(e); g.transform.localScale = sc;
            g.AddComponent<MeshFilter>().sharedMesh = m; g.AddComponent<MeshRenderer>().sharedMaterial = mat; return g;
        }
        switch (kind)
        {
            case "crate":
                P("CrateBase",box,wood,new Vector3(-.15f,-.12f,.05f),new Vector3(.48f,.06f,.34f),Vector3.zero);
                foreach(float side in new[]{-1f,1f}) {
                    P("CrateEnd",box,wood,new Vector3(-.15f+side*.23f,-.02f,.05f),new Vector3(.04f,.2f,.34f),Vector3.zero);
                    for(int row=0;row<2;row++) P("CrateSlat",box,wood,new Vector3(-.15f,-.08f+row*.11f,.05f+side*.16f),new Vector3(.48f,.07f,.035f),Vector3.zero);
                }
                break;
            case "hoe":
                // Carried on the shoulder: shaft rests across the back, blade over the far shoulder (Images 1 & 2).
                P("Shaft", VillageKit.Ring(6, 0), wood, new Vector3(0, -.55f, .45f), new Vector3(.035f, 1.25f, .035f), new Vector3(-25, 0, 0));
                P("Blade", box, iron, new Vector3(0, -.55f, .45f), new Vector3(.22f, .03f, .16f), new Vector3(0, 0, 0));
                P("ShoulderPad", box, VillageKit.Mat(new Color(.5f, .38f, .26f)), new Vector3(0, -.08f, .62f), new Vector3(.3f, .06f, .3f), new Vector3(0, 15, 0)); break;
            case "can":
                P("Can", VillageKit.Ring(10, 0), VillageKit.Mat(new Color(.4f, .55f, .62f)), new Vector3(0, -.25f, .05f), new Vector3(.14f, .2f, .14f), Vector3.zero);
                P("Spout", VillageKit.Ring(6, 0), VillageKit.Mat(new Color(.4f, .55f, .62f)), new Vector3(0, -.12f, .2f), new Vector3(.025f, .25f, .025f), new Vector3(55, 0, 0)); break;
            case "bucket":
                P("Bucket", VillageKit.Ring(10, .85f), wood, new Vector3(0, -.32f, 0), new Vector3(.14f, .18f, .14f), Vector3.zero);
                P("Water", VillageKit.Disc(3, 0, 10), VillageKit.Mat(new Color(.25f, .45f, .55f)), new Vector3(0, -.16f, 0), new Vector3(.12f, 1, .12f), Vector3.zero); break;
            case "rod":
                P("Rod", VillageKit.Ring(6, 0), wood, new Vector3(0, 0, 0), new Vector3(.02f, 1.8f, .02f), new Vector3(55, 0, 0));
                P("Line", VillageKit.Ring(4, 0), VillageKit.Mat(new Color(.9f, .9f, .9f)), new Vector3(0, -.267f, 1.474f), new Vector3(.004f, 1.3f, .004f), Vector3.zero); break;
            case "broom":
                P("Handle", VillageKit.Ring(6, 0), wood, new Vector3(0, -.95f, .55f), new Vector3(.03f, 1.1f, .03f), new Vector3(-30, 0, 0));
                P("Bristles", VillageKit.Blob(5, .1f), VillageKit.Mat(new Color(.82f, .7f, .42f)), new Vector3(0, -.95f, .55f), new Vector3(.18f, .12f, .1f), Vector3.zero); break;
        }
        if (kind == "can" || kind == "bucket")
        {
            float y = kind == "can" ? -.06f : -.14f;
            P("Grip", box, iron, new Vector3(0,.015f,0), new Vector3(.22f,.025f,.025f), Vector3.zero);
            foreach (float side in new[] {-1f,1f})
                P("HandleSide", box, iron, new Vector3(side*.10f,y*.5f,0), new Vector3(.025f,Mathf.Abs(y)+.04f,.025f), Vector3.zero);
        }
        return root;
    }
}
