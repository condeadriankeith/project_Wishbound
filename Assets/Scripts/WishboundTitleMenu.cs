using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

/// <summary>Staged title, magnetic wisp selection, settings and a directed farewell.</summary>
public sealed class WishboundTitleMenu : MonoBehaviour
{
    sealed class Choice
    {
        public Text text; public CanvasGroup group; public RectTransform rect;
        public Shadow[] glow; public Action action; public float heat;
    }
    readonly List<Choice> main = new List<Choice>(), settings = new List<Choice>();
    RectTransform root, logo; CanvasGroup logoGroup, settingsGroup;
    Image fade, shade; IslandThirdPersonController player;
    Font font;
    Vector2 cursor, rawPointer, previousPointer; int selected = -1;
    bool ready, busy, settingsOpen, pointerInitialized, keyboardSelection;
    float menuAge; bool claimed;
    void Unclaim() { WispCursor.ScreenOverride = null; if (claimed) { claimed = false; WispCursor.Claims--; } }
    public bool PlayRequested { get; private set; }
    public bool Ready => ready;
    public bool SettingsOpen => settingsOpen;
    public string FocusedOption => selected >= 0 && selected < Choices.Count ? Choices[selected].text.text : "";
    List<Choice> Choices => settingsOpen ? settings : main;

    static float Ease(float t) { t=Mathf.Clamp01(t); return t*t*t*(t*(t*6-15)+10); }
    public void Configure(RectTransform title, CanvasGroup titleAlpha, Image black, IslandThirdPersonController controller)
    {
        root = (RectTransform)transform; logo = title; logoGroup = titleAlpha; fade = black; player = controller;
        font = Resources.Load<Font>("Fonts/CutoutChaos");
        if (!font) { Debug.LogError("Cutout Chaos font is missing"); font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        shade = ImageAt("Title atmosphere", transform, Vector2.zero, Vector2.zero, new Color(.005f,.012f,.035f,0));
        shade.rectTransform.anchorMin=Vector2.zero; shade.rectTransform.anchorMax=Vector2.one;
        shade.rectTransform.offsetMin=shade.rectTransform.offsetMax=Vector2.zero;
        shade.transform.SetAsFirstSibling();
        main.Add(MakeChoice("PLAY",new Vector2(0,-65),new Vector2(420,74),SelectPlay,transform));
        main.Add(MakeChoice("SETTINGS",new Vector2(0,-143),new Vector2(420,74),OpenSettings,transform));
        main.Add(MakeChoice("QUIT",new Vector2(0,-221),new Vector2(420,74),SelectQuit,transform));
        var panel=new GameObject("Settings page",typeof(RectTransform),typeof(CanvasGroup)); panel.transform.SetParent(transform,false);
        var rect=(RectTransform)panel.transform; rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f); rect.sizeDelta=new Vector2(900,600);
        settingsGroup=panel.GetComponent<CanvasGroup>(); settingsGroup.alpha=0;
        var heading=MakeChoice("SETTINGS",new Vector2(0,170),new Vector2(800,90),null,panel.transform); heading.text.fontSize=58; heading.group.alpha=1;
        settings.Add(MakeChoice("",new Vector2(0,65),new Vector2(600,66),()=>ChangeVolume(.1f),panel.transform));
        settings.Add(MakeChoice("",new Vector2(0,-20),new Vector2(600,66),()=>ChangeSensitivity(.02f),panel.transform));
        settings.Add(MakeChoice("",new Vector2(0,-105),new Vector2(600,66),()=>{WishboundSettings.SetFullscreen(!Screen.fullScreen); RefreshSettings();},panel.transform));
        settings.Add(MakeChoice("BACK",new Vector2(0,-225),new Vector2(420,70),CloseSettings,panel.transform));
        var help=MakeChoice("LEFT / RIGHT TO ADJUST   -   ESC TO RETURN",new Vector2(0,-315),new Vector2(900,45),null,panel.transform); help.text.fontSize=20; help.group.alpha=.65f;
        foreach(var c in settings) c.group.alpha=1;
        settingsGroup.gameObject.SetActive(false); RefreshSettings();
        fade.transform.SetAsLastSibling();
    }

    Choice MakeChoice(string caption,Vector2 pos,Vector2 size,Action action,Transform parent)
    {
        var go=new GameObject(caption.Length>0?caption:"Setting",typeof(RectTransform),typeof(CanvasGroup),typeof(Text)); go.transform.SetParent(parent,false);
        var r=(RectTransform)go.transform; r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f); r.anchoredPosition=pos; r.sizeDelta=size;
        var t=go.GetComponent<Text>(); t.text=caption; t.font=font; t.fontSize=48; t.alignment=TextAnchor.MiddleCenter; t.color=Color.white; t.raycastTarget=false;
        var shadows=new Shadow[4]; Vector2[] offsets={new Vector2(2,0),new Vector2(-2,0),new Vector2(0,2),new Vector2(0,-2)};
        for(int i=0;i<4;i++){ shadows[i]=go.AddComponent<Shadow>(); shadows[i].effectDistance=offsets[i]; shadows[i].effectColor=Color.clear; }
        var c=new Choice {text=t,rect=r,group=go.GetComponent<CanvasGroup>(),action=action,glow=shadows}; c.group.alpha=0; c.group.blocksRaycasts=false;
        return c;
    }
    Image ImageAt(string name,Transform parent,Vector2 pos,Vector2 size,Color color)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);
        var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=pos;r.sizeDelta=size;
        var image=go.GetComponent<Image>();image.color=color;image.raycastTarget=false;return image;
    }


    public IEnumerator Present()
    {
        Cursor.lockState=CursorLockMode.None;Cursor.visible=false;
        for(float t=0;t<1.5f;t+=Time.unscaledDeltaTime){logoGroup.alpha=Ease(t/1.5f);shade.color=new Color(.005f,.012f,.035f,.22f*Ease(t/1.5f));yield return null;}
        logoGroup.alpha=1;
        yield return new WaitForSecondsRealtime(2);
        for(float t=0;t<1.65f;t+=Time.unscaledDeltaTime)
        {
            float k=Ease(t/1.25f);logo.anchoredPosition=Vector2.up*145*k;logo.sizeDelta=Vector2.Lerp(new Vector2(860,340),new Vector2(760,300),k);
            for(int i=0;i<main.Count;i++){float a=Ease((t-i*.18f)/.65f);main[i].group.alpha=a;main[i].rect.anchoredPosition=new Vector2(0,-65-i*78-14*(1-a));}
            yield return null;
        }
        foreach(var c in main)c.group.alpha=1;
        ready=true;
    }
    void Update()
    {
        if(!ready || busy)return;
        menuAge+=Time.unscaledDeltaTime;
        Cursor.lockState=CursorLockMode.None;Cursor.visible=false;
        var mouse=Mouse.current;
        Vector2 screen=mouse!=null?mouse.position.ReadValue():new Vector2(Screen.width*.5f,Screen.height*.5f);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root,screen,null,out rawPointer);
        if(!pointerInitialized){cursor=rawPointer;previousPointer=rawPointer;pointerInitialized=true;}
        if((rawPointer-previousPointer).sqrMagnitude>2){keyboardSelection=false;}previousPointer=rawPointer;
        var list=Choices;
        if(!keyboardSelection)
        {
            int candidate=-1;float nearest=76;
            for(int i=0;i<list.Count;i++)
            {
                Vector2 centre=root.InverseTransformPoint(list[i].rect.position);
                float halfWidth=Mathf.Min(list[i].rect.rect.width*.5f,list[i].text.preferredWidth*.5f+20);
                Vector2 d=rawPointer-centre;d.x=Mathf.Max(0,Mathf.Abs(d.x)-halfWidth);d.y=Mathf.Max(0,Mathf.Abs(d.y)-25);
                float distance=d.magnitude;
                if(distance<nearest){nearest=distance;candidate=i;}
            }
            selected=candidate;
        }
        var kb=Keyboard.current;
        if(kb!=null)
        {
            if(kb.downArrowKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame){selected=(selected+1)%list.Count;keyboardSelection=true;}
            if(kb.upArrowKey.wasPressedThisFrame){selected=(selected+list.Count-1)%list.Count;keyboardSelection=true;}
            if(settingsOpen && kb.escapeKey.wasPressedThisFrame){CloseSettings();return;}
            if(settingsOpen && selected>=0 && (kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame))
            {float sign=kb.leftArrowKey.wasPressedThisFrame?-1:1;if(selected==0)ChangeVolume(sign*.1f);else if(selected==1)ChangeSensitivity(sign*.02f);else if(selected==2)list[selected].action();}
        }
        Vector2 target=rawPointer;
        if(selected>=0) target=(Vector2)root.InverseTransformPoint(list[selected].rect.position)+new Vector2(-Mathf.Min(170,list[selected].text.preferredWidth*.5f+24),0);
        cursor=Vector2.Lerp(cursor,target,1-Mathf.Exp(-18*Time.unscaledDeltaTime));
        WispCursor.ScreenOverride=RectTransformUtility.WorldToScreenPoint(null,root.TransformPoint(cursor));
        if(selected>=0 && ((mouse!=null && mouse.leftButton.wasPressedThisFrame)||(kb!=null && kb.enterKey.wasPressedThisFrame)))list[selected].action();
    }

    public void SelectPlay(){if(!ready || busy || settingsOpen)return;busy=true;PlayRequested=true;}
    public void OpenSettings(){ if(!ready||busy||settingsOpen)return; StartCoroutine(Morph(true)); }
    public void CloseSettings(){ if(!ready||busy||!settingsOpen)return; StartCoroutine(Morph(false)); }
    IEnumerator Morph(bool open)
    {
        busy=true; selected=-1;
        var panel=(RectTransform)settingsGroup.transform;
        if(open){ RefreshSettings(); settingsGroup.alpha=0; settingsGroup.gameObject.SetActive(true); }
        const float D=.6f;
        for(float t=0;t<D;t+=Time.unscaledDeltaTime)
        {
            float k=Ease(t/D);
            float outA=1-Ease(t/(D*.5f)), inA=Ease((t-D*.35f)/(D*.65f));
            float mainA=open?outA:inA, setA=open?inA:outA;
            float mainS=open?Mathf.Lerp(1f,.9f,k):Mathf.Lerp(.9f,1f,k);
            float setS=open?Mathf.Lerp(.9f,1f,k):Mathf.Lerp(1f,.9f,k);
            logoGroup.alpha=mainA; logo.localScale=Vector3.one*mainS;
            foreach(var c in main){c.group.alpha=mainA;c.rect.localScale=Vector3.one*mainS;}
            settingsGroup.alpha=setA; panel.localScale=Vector3.one*setS; panel.anchoredPosition=new Vector2(0,open?-28*(1-k):-28*k);
            yield return null;
        }
        settingsOpen=open;
        logoGroup.alpha=open?0:1; logo.localScale=Vector3.one;
        foreach(var c in main){c.group.alpha=open?0:1;c.rect.localScale=Vector3.one;}
        settingsGroup.alpha=open?1:0; panel.localScale=Vector3.one; panel.anchoredPosition=Vector2.zero;
        if(!open)settingsGroup.gameObject.SetActive(false);
        selected=-1; busy=false;
    }
    public void ChangeVolume(float delta){WishboundSettings.SetVolume(WishboundSettings.Volume+delta);RefreshSettings();}
    public void ChangeSensitivity(float delta){WishboundSettings.SetSensitivity(WishboundSettings.Sensitivity+delta,player);RefreshSettings();}
    void RefreshSettings()
    {
        if(settings.Count<3)return;
        settings[0].text.text="VOLUME   "+Mathf.RoundToInt(WishboundSettings.Volume*100)+"%";
        settings[1].text.text="LOOK SPEED   "+(WishboundSettings.Sensitivity/.15f).ToString("0.0")+"X";
        settings[2].text.text="FULLSCREEN   "+(Screen.fullScreen?"ON":"OFF");
    }
    public IEnumerator Dismiss()
    {
        busy=true;
        for(float t=0;t<.9f;t+=Time.unscaledDeltaTime){float a=1-Ease(t/.9f);logoGroup.alpha=a;foreach(var c in main)c.group.alpha=a;shade.color=new Color(0,0,0,.22f*a);yield return null;}
        logoGroup.alpha=0;foreach(var c in main)c.group.alpha=0;shade.color=Color.clear;ReleaseCursor();
    }
    public void SelectQuit()
    {
        if(!ready||busy||settingsOpen)return;
        busy=true;
        // hand the wisp over, at its current spot beside QUIT, to the shared farewell sequence (input is dead from here on)
        Vector2 screen=RectTransformUtility.WorldToScreenPoint(null,root.TransformPoint(cursor));
        ReleaseCursor();
        Cursor.lockState=CursorLockMode.None;Cursor.visible=false;
        WishboundFarewell.Play(screen);
    }
    public void ReleaseCursor(){Unclaim();ready=false;}
    void OnDestroy()
    {
        Unclaim();
        if(!PlayRequested){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
    }
}