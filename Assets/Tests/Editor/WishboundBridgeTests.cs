using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class WishboundBridgeTests
{
    GameObject bridge;
    Type surfaceType;

    [SetUp]
    public void Build()
    {
        surfaceType=Type.GetType("BridgeWalkSurface, Assembly-CSharp",true);
        var builder=Type.GetType("IslandDeckBridge, Assembly-CSharp",true);
        bridge=(GameObject)builder.GetMethod("Build").Invoke(null,new object[]{"Regression bridge",new[]{new Vector3(0,1000,0),new Vector3(0,1000.4f,4),new Vector3(0,1000,8)},2.4f,null});
        Physics.SyncTransforms();
    }
    [TearDown] public void Cleanup(){UnityEngine.Object.DestroyImmediate(bridge);}

    [Test]
    public void DeckHasContinuousSupportAcrossEveryPlankJoin()
    {
        var collider=bridge.GetComponent<MeshCollider>();
        Assert.That(collider,Is.Not.Null);
        for(float z=.05f;z<7.99f;z+=.07f)
            foreach(float x in new[]{-.8f,0,.8f})
            {
                Assert.That(collider.Raycast(new Ray(new Vector3(x,1002,z),Vector3.down),out var hit,3),Is.True,$"Gap at {x}, {z}");
                Assert.That(hit.normal.y,Is.GreaterThan(.95f));
            }
        Assert.That(bridge.GetComponentsInChildren<MeshCollider>().Length,Is.EqualTo(1));
    }

    [Test]
    public void CompanionCorridorClampsLateralSwayWithoutLosingDeckHeight()
    {
        var surface=bridge.GetComponent(surfaceType);
        object[] args={new Vector3(1.1f,1000.3f,3),.5f,null};
        Assert.That(surfaceType.GetMethod("Project").Invoke(surface,args),Is.EqualTo(true));
        var support=(Vector3)args[2];
        Assert.That(Mathf.Abs(support.x),Is.LessThan(.52f));
        Assert.That(support.y,Is.EqualTo(1000.3f).Within(.001f));
    }

    [Test]
    public void SupportRejectsTerrainFarBelowBridgeAndReleasesDestroyedBridge()
    {
        object[] near={new Vector3(0,1000.5f,4),null,null};
        var query=surfaceType.GetMethod("TrySupport");
        Assert.That(query.Invoke(null,near),Is.EqualTo(true));
        object[] below={new Vector3(0,990,4),null,null};
        Assert.That(query.Invoke(null,below),Is.EqualTo(false));
        UnityEngine.Object.DestroyImmediate(bridge);
        Assert.That(query.Invoke(null,near),Is.EqualTo(false));
    }
}
