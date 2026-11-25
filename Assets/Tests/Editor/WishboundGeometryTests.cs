using System.Reflection;
using NUnit.Framework;
using UnityEngine;

// Regression guards for procedural village geometry. VillageKit lives in Assembly-CSharp, which this
// editor test assembly does not reference directly, so it is resolved by its assembly-qualified name.
public class WishboundGeometryTests
{
    static readonly System.Type VK = System.Type.GetType("VillageKit, Assembly-CSharp");

    static object Call(string method, params object[] args)
    {
        Assert.IsNotNull(VK, "VillageKit type not found");
        var m = VK.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(m, method + " not found");
        return m.Invoke(null, args);
    }

    // Positive when triangle winding faces outward (Unity front faces are the outward side).
    static double SignedVolume(Mesh m)
    {
        var v = m.vertices;
        var t = m.triangles;
        double s = 0;
        for (int i = 0; i < t.Length; i += 3)
            s += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]]));
        return s / 6.0;
    }

    [TestCase(0f)]
    [TestCase(45f)]
    [TestCase(90f)]
    public void RollingCartRimsTouchTheFloor(float angle)
    {
        var cart = System.Type.GetType("VillageKitV2, Assembly-CSharp").GetMethod("Cart")
            .Invoke(null, new object[] { Vector3.zero, null, 1 }) as GameObject;
        try
        {
            var behaviour = cart.GetComponent("HandCart");
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            behaviour.GetType().GetField("wheelAngle", flags).SetValue(behaviour, angle);
            behaviour.GetType().GetMethod("LateUpdate", flags).Invoke(behaviour, null);
            foreach (var filter in cart.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.name != "Rim") continue;
                float bottom = float.PositiveInfinity;
                foreach (var vertex in filter.sharedMesh.vertices)
                    bottom = Mathf.Min(bottom, filter.transform.TransformPoint(vertex).y);
                Assert.That(bottom - cart.transform.position.y, Is.InRange(-.001f, .018f));
            }
        }
        finally { Object.DestroyImmediate(cart); }
    }

    [Test]
    public void ProceduralBlobsFaceOutward()
    {
        for (int seed = 1; seed <= 6; seed++)
            Assert.IsTrue(SignedVolume((Mesh)Call("Blob", seed, .18f, 4, 7)) > 0, "Blob seed " + seed + " is inside-out (hollow)");
    }

    [Test]
    public void ProceduralRingFacesOutward()
    {
        Assert.IsTrue(SignedVolume((Mesh)Call("Ring", 12, 0f)) > 0, "Ring is inside-out (hollow)");
    }

    [Test]
    public void WaterPumpPartsOverlap()
    {
        var host = new GameObject("PumpTestHost");
        try
        {
            Call("AddWaterPump", host.transform, Vector3.zero, new System.Random(1));

            var pump = host.transform.Find("WaterPump");
            Assert.IsNotNull(pump, "WaterPump was not built");
            var column = pump.Find("PumpColumn");
            var pivot = pump.Find("PumpPivot");
            var handle = pump.Find("PumpHandle");
            var spout = pump.Find("PumpSpout");

            float columnTop = column.localPosition.y + column.localScale.y * .5f;
            Assert.AreEqual(columnTop, pivot.localPosition.y, .01f, "Pump cap floats above the column");
            Assert.AreEqual(pivot.localPosition.y, handle.localPosition.y, .001f, "Lever does not start at the cap");
            Assert.AreEqual(0f, spout.localPosition.x, .001f, "Spout does not start inside the column");
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
    }
}