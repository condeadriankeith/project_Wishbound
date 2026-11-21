using System;
using UnityEngine;
using UnityEngine.Rendering;
[DefaultExecutionOrder(1000)]
public class LivingDayNight : MonoBehaviour
{
 public Light sun; public Material sky; public Material ocean; public Material cloudCookieMaterial; public static LivingDayNight Instance {get;private set;}
 [Header("Debug panel (hidden for now - tick to bring it back)")] public bool showPanel=false;
 [Header("Story time")] [Tooltip("While true the world holds night (storyNightHour) regardless of the real clock. Released by EndStoryNight() when the star and child sleep in the house.")] public bool storyNight=false; [Range(0,24)] public float storyNightHour=21.5f;
 [Tooltip("Set by cutscenes: hour follows scriptedHour exactly, with no smoothing.")] [NonSerialized] public bool scripted; [NonSerialized] public float scriptedHour=18;
 public void BeginStoryNight(){storyNight=true;}
 public void EndStoryNight(){storyNight=false;computerTime=true;}
 public static bool PointerOverPanel { get { if(!Instance||!Instance.showPanel)return false; var mouse=UnityEngine.InputSystem.Mouse.current; if(mouse==null)return false; var p=mouse.position.ReadValue(); return p.x>=16 && p.x<=316 && Screen.height-p.y>=16 && Screen.height-p.y<=210; } } public bool computerTime=true; [Range(0,24)]public float manualHour=12;
 public float transitionSeconds=2; public float CurrentHour {get;private set;} public string Mode=>computerTime?"Computer time":"Preview";
 float hour; bool initialized; GUIStyle label,button; bool expanded=true; float nextGI;
 void OnEnable(){
 Instance=this; initialized=false;
 if(!sun){var lights=FindObjectsByType<Light>(FindObjectsSortMode.None);foreach(var l in lights)if(l.type==LightType.Directional){sun=l;break;}}
 if(!sky)sky=RenderSettings.skybox;
}
 public void SetHour(float value){computerTime=false;manualHour=Mathf.Repeat(value,24);}
 public void UseComputerTime(){computerTime=true;}
 void LateUpdate(){if(!ocean){var water=GameObject.Find("Ocean");if(water){var renderer=water.GetComponent<Renderer>();if(renderer)ocean=renderer.sharedMaterial;}}if(scripted){hour=Mathf.Repeat(scriptedHour,24);initialized=true;CurrentHour=hour;Apply(hour);return;}float desired=storyNight?storyNightHour:computerTime?(float)DateTime.Now.TimeOfDay.TotalHours:manualHour;if(!initialized){hour=desired;initialized=true;}float delta=Mathf.DeltaAngle(hour*15,desired*15)/15;hour=Mathf.Repeat(hour+delta*(1-Mathf.Exp(-Time.deltaTime*3/Mathf.Max(.1f,transitionSeconds))),24);if(Mathf.Abs(delta)<.0001f)hour=desired;CurrentHour=hour;Apply(hour);}
 public void Apply(float h){float elevation=Mathf.Sin((h-6)/24*Mathf.PI*2);float day=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.22f,.22f,elevation));float golden=(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.45f,Mathf.Abs(elevation))))*day;float night=1-day; if(cloudCookieMaterial)cloudCookieMaterial.SetFloat("_intensity",Mathf.Lerp(.08f,.42f,day));
 Color zenith=Color.Lerp(new Color(.008f,.015f,.05f),new Color(.08f,.32f,.68f),day);Color horizon=Color.Lerp(new Color(.035f,.06f,.13f),new Color(.62f,.82f,.95f),day);horizon=Color.Lerp(horizon,new Color(1,.39f,.18f),golden*.8f);
 Vector3 sunDir=new Vector3(Mathf.Cos((h-6)/24*Mathf.PI*2),elevation,.28f).normalized;
 if(sun){sun.transform.rotation=Quaternion.LookRotation(elevation>-.04f?-sunDir:sunDir);sun.color=Color.Lerp(new Color(.4f,.55f,.9f),Color.Lerp(new Color(1,.97f,.85f),new Color(1,.57f,.25f),golden),day);sun.intensity=Mathf.Lerp(.12f,1.35f,day);RenderSettings.sun=sun;}
 Color ambient=Color.Lerp(new Color(.12f,.18f,.32f),new Color(.65f,.79f,.95f),day);Color ground=Color.Lerp(new Color(.06f,.08f,.14f),new Color(.32f,.4f,.25f),day);float strength=Mathf.Lerp(.48f,1.05f,day);
 Shader.SetGlobalColor("_HemiSky",ambient);Shader.SetGlobalColor("_HemiGround",ground);Shader.SetGlobalFloat("_HemiIntensity",strength);Shader.SetGlobalVector("_GhibliSunDir",sunDir);Shader.SetGlobalVector("_GhibliSunDirSrc",sunDir);
 RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=ambient*strength;RenderSettings.ambientEquatorColor=Color.Lerp(ambient,ground,.5f)*strength;RenderSettings.ambientGroundColor=ground*strength;RenderSettings.fogColor=horizon;float fogD=WishboundIntro.FogOverride>0f?WishboundIntro.FogOverride:.0011f;RenderSettings.fogDensity=fogD;Shader.SetGlobalFloat("_GhibliFogDensity",fogD);Shader.SetGlobalColor("_GhibliFogColor",RenderSettings.fogColor);
 if(ocean){ocean.SetColor("_CDeep",Color.Lerp(new Color(.005f,.015f,.04f),new Color(.09f,.42f,.6f),day));ocean.SetColor("_CShal",Color.Lerp(new Color(.015f,.06f,.1f),new Color(.36f,.8f,.8f),day));} if(sky){RenderSettings.skybox=sky;sky.SetColor("_Top",zenith);sky.SetColor("_Hor",horizon);sky.SetColor("_Cloud",Color.Lerp(new Color(.08f,.12f,.22f),Color.Lerp(new Color(.96f,.97f,1),new Color(1,.65f,.4f),golden),day));sky.SetFloat("_Night",night);sky.SetVector("_SunDir",sunDir);}
 if(Time.unscaledTime>nextGI){nextGI=Time.unscaledTime+10;DynamicGI.UpdateEnvironment();}}
 void OnGUI(){if(!showPanel)return;if(label==null){label=new GUIStyle(GUI.skin.label){fontSize=15};button=new GUIStyle(GUI.skin.button){fontSize=13};}GUI.Box(new Rect(16,16,300,expanded?194:42),"");if(GUI.Button(new Rect(24,23,282,28),$"{Mode}  •  {((int)CurrentHour):00}:{((int)(CurrentHour%1*60)):00}   {(expanded?"▾":"▸")}",button))expanded=!expanded;if(!expanded)return;
 string[] names={"Sunrise","Day","Midday","Sunset","Night","Midnight"};float[] hours={6,9,12,18,21,0};for(int i=0;i<6;i++)if(GUI.Button(new Rect(24+(i%3)*94,60+(i/3)*36,90,30),names[i],button))SetHour(hours[i]);if(GUI.Button(new Rect(24,136,282,30),(computerTime?"✓ ":"")+"Use computer time",button))UseComputerTime();GUI.Label(new Rect(25,174,280,24),"Wheel: zoom   ·   Esc: cursor",label);}
}






