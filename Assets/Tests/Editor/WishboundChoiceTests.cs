using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

// Reflection keeps the existing Assembly-CSharp scene GUIDs and assembly layout intact.
public sealed class WishboundChoiceTests
{
    Type flags, alignment, cottageType;
    GameObject owner;
    Component cottage;
    const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [SetUp]
    public void SetUp()
    {
        flags = Type.GetType("StoryFlags, Assembly-CSharp", true);
        alignment = Type.GetType("StarAlignment, Assembly-CSharp", true);
        cottageType = Type.GetType("CottageChoiceScene, Assembly-CSharp", true);
        flags.GetMethod("NewDemoRun").Invoke(null, null);
        owner = new GameObject("Choice regression test");
        cottage = owner.AddComponent(cottageType);
    }

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.DestroyImmediate(owner);
        flags.GetMethod("ResetRuntimeCache", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
    }

    void OpenChoice()
    {
        var phase = cottageType.GetField("phase", PrivateInstance);
        phase.SetValue(cottage, Enum.Parse(phase.FieldType, "Choice"));
    }

    bool Commit(string route) => (bool)cottageType.GetMethod("TryCommitChoice").Invoke(cottage, new[] { Enum.Parse(alignment, route) });
    bool Flag(string key) => (bool)flags.GetMethod("Get").Invoke(null, new object[] { key });

    [TestCase("Tether", 60f, "ChoseGatherFragments", "ChoseLiveAmongHumans")]
    [TestCase("Roots", -60f, "ChoseLiveAmongHumans", "ChoseGatherFragments")]
    public void ChoiceCommitsExactlyOnce(string route, float spectrum, string selected, string rejected)
    {
        OpenChoice();
        int events = 0;
        Action<object> observe = _ => events++;
        // Bind a generic callback to the runtime enum without an assembly dependency.
        var callback = GetType().GetMethod(nameof(Observe), BindingFlags.NonPublic | BindingFlags.Static)
            .MakeGenericMethod(alignment).Invoke(null, new object[] { observe });
        cottageType.GetEvent("RouteCommitted").AddEventHandler(cottage, (Delegate)callback);
        Assert.That(Commit(route), Is.True);
        Assert.That(Commit(route == "Tether" ? "Roots" : "Tether"), Is.False);
        Assert.That(events, Is.EqualTo(1));
        Assert.That(flags.GetProperty("Spectrum").GetValue(null), Is.EqualTo(spectrum));
        Assert.That(Flag(selected), Is.True);
        Assert.That(Flag(rejected), Is.False);
        Assert.That(Flag("CottageChoiceMade"), Is.True);
    }

    static Action<T> Observe<T>(Action<object> callback) => value => callback(value);

    [Test]
    public void SavedSlotsResumeIndependentlyAndResetOnlyTheChosenSlot()
    {
        var slots = Type.GetType("WishSaveSlots, Assembly-CSharp",true);
        var prefix = slots.GetField("Prefix",BindingFlags.NonPublic|BindingFlags.Static);
        var original = prefix.GetValue(null);
        string testPrefix = "Wishbound.TestSlots."+Guid.NewGuid()+".";
        var load = flags.GetMethod("LoadActive",BindingFlags.NonPublic|BindingFlags.Static);
        void Select(int n) { slots.GetMethod("Select").Invoke(null,new object[]{n}); load.Invoke(null,null); }
        try
        {
            prefix.SetValue(null,testPrefix);
            PlayerPrefs.SetInt(testPrefix+"Migrated",1);
            Select(1);
            flags.GetMethod("Set").Invoke(null,new object[]{"ChildTookHand",true});
            Select(2);
            Assert.That(Flag("ChildTookHand"),Is.False);
            flags.GetMethod("Set").Invoke(null,new object[]{"GardenFragmentRecovered",true});
            Select(1);
            Assert.That(Flag("ChildTookHand"),Is.True);
            Assert.That(Flag("GardenFragmentRecovered"),Is.False);
            slots.GetMethod("Reset").Invoke(null,new object[]{2});
            Assert.That(slots.GetMethod("Read").Invoke(null,new object[]{2}),Is.Null);
            Assert.That(slots.GetMethod("Read").Invoke(null,new object[]{3}),Is.Null);
            Select(1);
            Assert.That(Flag("ChildTookHand"),Is.True);
        }
        finally
        {
            flags.GetMethod("NewDemoRun").Invoke(null,null);
            prefix.SetValue(null,original);
            foreach(var suffix in new[]{"1","2","3","Active","Migrated"}) PlayerPrefs.DeleteKey(testPrefix+suffix);
            PlayerPrefs.Save();
        }
    }

    [TestCase("FerryParcel")]
    [TestCase("FerryPier")]
    [TestCase("ThawPrism")]
    [TestCase("ShadowStone0")]
    [TestCase("ShadowStone1")]
    [TestCase("ShadowStone2")]
    [TestCase("ObservatoryAltar")]
    public void AuthoredQuestPrefabHasGeometryUvsAndTexturedMaterials(string name)
    {
        var prefab = Resources.Load<GameObject>("VerticalSlice/" + name);
        Assert.That(prefab, Is.Not.Null);
        var meshes = prefab.GetComponentsInChildren<MeshFilter>();
        Assert.That(meshes.Length, Is.GreaterThan(0));
        foreach (var filter in meshes)
        {
            Assert.That(filter.sharedMesh, Is.Not.Null);
            Assert.That(filter.sharedMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0), Is.True);
        }
        foreach(var renderer in prefab.GetComponentsInChildren<Renderer>())
            foreach(var material in renderer.sharedMaterials)
            {
                Assert.That(material, Is.Not.Null);
                Assert.That(material.shader.isSupported, Is.True);
                Assert.That(material.mainTexture, Is.Not.Null);
            }
    }

    [Test]
    public void ChoiceRejectsEarlyNoneAndInvalidSelections()
    {
        Assert.That(Commit("Tether"), Is.False);
        OpenChoice();
        Assert.That(Commit("None"), Is.False);
        Assert.That(Commit("999"), Is.False);
        Assert.That(Flag("CottageChoiceMade"), Is.False);
    }

    [Test]
    public void NewRunClearsEveryInMemoryFlagWithoutChangingPersistentSave()
    {
        const string key = "Wishbound.Flag.VerticalSliceSaveIsolationTest";
        bool existed = PlayerPrefs.HasKey(key);
        int original = PlayerPrefs.GetInt(key);
        try
        {
            PlayerPrefs.SetInt(key, 1);
            flags.GetMethod("Set").Invoke(null, new object[] { "RootsEnding", true });
            flags.GetMethod("Set").Invoke(null, new object[] { "FutureQuestFlag", true });
            flags.GetMethod("NewDemoRun").Invoke(null, null);
            Assert.That(Flag("RootsEnding"), Is.False);
            Assert.That(Flag("FutureQuestFlag"), Is.False);
            Assert.That(Flag("VerticalSliceSaveIsolationTest"), Is.False);
            Assert.That(PlayerPrefs.GetInt(key), Is.EqualTo(1));
            Assert.That(flags.GetProperty("Spectrum").GetValue(null), Is.EqualTo(0f));
        }
        finally
        {
            if (existed) PlayerPrefs.SetInt(key, original); else PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }
    }
}
