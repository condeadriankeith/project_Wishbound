using System;
using System.Linq;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEditor;

public static class StarSwarmGraphBuilder
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static PropertyInfo Property(Type type,string name) { for(var t=type;t!=null;t=t.BaseType) { var p=t.GetProperty(name,Flags|BindingFlags.DeclaredOnly); if(p!=null)return p; } throw new MissingMemberException(name); }
    public static Type TypeOf(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name)).First(t=>t!=null);
    public static object Get(object o,string name) => Property(o.GetType(),name).GetValue(o);
    public static void Set(object o,string name,object value) => Property(o.GetType(),name).SetValue(o,value);
    public static object Call(object o,string name,params object[] args)
    {
        var type=o as Type ?? o.GetType();
        var method=type.GetMethods(Flags).First(m=>m.Name==name && !m.IsGenericMethod && m.GetParameters().Length>=args.Length && m.GetParameters().Skip(args.Length).All(p=>p.IsOptional) && m.GetParameters().Take(args.Length).Select((p,i)=>args[i]==null || p.ParameterType.IsInstanceOfType(args[i])).All(x=>x));
        var full=method.GetParameters().Select((p,i)=>i<args.Length?args[i]:p.DefaultValue).ToArray();
        return method.Invoke(o is Type?null:o,full);
    }
    public static object[] List(object o,string name) => ((IEnumerable)Get(o,name)).Cast<object>().ToArray();
    public static object New(string name) => ScriptableObject.CreateInstance(TypeOf("UnityEditor.VFX."+name));
    public static object Resource(string path) => Call(TypeOf("UnityEditor.VFX.VisualEffectResource"),"GetResourceAtPath",path);
    public static object Graph(string path) => Call(TypeOf("UnityEditor.VFX.VisualEffectResourceExtensions"),"GetOrCreateGraph",Resource(path));
    public static void Save(string path) { Call(TypeOf("UnityEditor.VFX.VisualEffectResourceExtensions"),"WriteAssetWithSubAssets",Resource(path)); AssetDatabase.ImportAsset(path); }
    public static object Slot(object model,string path)
    {
        var names=path.Split('/'); object slot=List(model,"inputSlots").First(s=>(string)Get(s,"name")==names[0]);
        foreach(var n in names.Skip(1)) slot=List(slot,"children").First(s=>(string)Get(s,"name")==n);
        return slot;
    }
    public static void Value(object model,string path,object value) => Set(Slot(model,path),"value",value);
    public static object Describe(object model) => new {type=model.GetType().Name,name=(model as UnityEngine.Object)?.name,slots=List(model,"inputSlots").Select(s=>new{name=Get(s,"name"),value=Get(s,"value")?.ToString(),children=List(s,"children").Select(c=>new{name=Get(c,"name"),value=Get(c,"value")?.ToString()}).ToArray()}).ToArray()};
}

