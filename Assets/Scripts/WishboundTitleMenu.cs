using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

/// <summary>Staged title, magnetic wisp selection (WispMagnet), settings and a directed farewell.</summary>
public sealed class WishboundTitleMenu : MonoBehaviour
{
    sealed class Choice : WispWord
    {
        public CanvasGroup group; public Action action;
    }
    readonly List<Choice> main = new List<Choice>(), settings = new List<Choice>();
    RectTransform root, logo; CanvasGroup logoGroup, settingsGroup;
    WishProgressPage progressPage;
    int pageClosedFrame=-1;
    Image fade, shade; IslandThirdPersonController player;
    Font font;
    Vector2 rawPointer, previousPointer;
    readonly WispMagnet magnet = new WispMagnet();
    bool ready, busy, settingsOpen, keyboardSelection;
    float menuAge;
    public bool PlayRequested { get; private set; }
    public bool Ready => ready;
    public bool SettingsOpen => settingsOpen;
    public string FocusedOption => magnet.Selected >= 0 && magnet.Selected < Choices.Count ? Choices[magnet.Selected].text.text : "";
    List<Choice> Choices => settingsOpen ? settings : main;

    static float Ease(float t) { t=Mathf.Clamp01(t); return t*t*t*(t*(t*6-15)+10); }
    static int Step(int from,int dir,int count) { if(from<0) return dir>0 ? 0 : count-1; return (from+dir+count)%count; }

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
        settings.Add(MakeChoice("SAVED PROGRESS",new Vector2(0,-175),new Vector2(640,66),OpenProgress,panel.transform));
        settings.Add(MakeChoice("BACK",new Vector2(0,-250),new Vector2(420,70),CloseSettings,panel.transform));
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
        var c=new Choice {text=t,rect=r,group=go.GetComponent<CanvasGroup>(),action=action,glow=WispMagnet.AddGlow(go)};
        c.group.alpha=0; c.group.blocksRaycasts=false;
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
        WispCursor.TitleOwner = this;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
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
        if (WispCursor.TitleOwner != this || progressPage || pageClosedFrame==Time.frameCount) return;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = !WispCursor.IsShowing;


        Vector2 screen = WishboundInput.Pointer;
        bool clicked = WishboundInput.PrimaryPressed;
        if (!ready)
        {
            WispCursor.ScreenOverride = screen;
            return;
        }
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root,screen,null,out rawPointer);
        if(!busy)
        {
            menuAge+=Time.unscaledDeltaTime;
            if((rawPointer-previousPointer).sqrMagnitude>2) keyboardSelection=false;   // the mouse takes over once it moves
            KeyboardNavigation();
            if (clicked) keyboardSelection = false;
        }
        previousPointer=rawPointer;

        // the wisp always follows the magnet (also while a transition plays, holding whichever word it had grabbed)
        magnet.Track(root,Choices,rawPointer,Time.unscaledDeltaTime,keyboardSelection||busy);
        WispCursor.ScreenOverride=magnet.ScreenPoint(root);
        if(busy) return;

        int sel=magnet.Selected;
        if(sel>=0 && (clicked||(WishboundInput.Pressed(Key.Enter, KeyCode.Return)))){ WorldSound.Menu("Select"); Choices[sel].action(); }
    }

    void OpenProgress()
    {
        settingsGroup.gameObject.SetActive(false);
        progressPage=WishProgressPage.Open(root,font,changed=>{
            progressPage=null;pageClosedFrame=Time.frameCount;
            settingsGroup.gameObject.SetActive(true);ClearFocus();
            if(changed)CloseSettings();
        });
    }

    void KeyboardNavigation()
    {
        var list=Choices;
        if(WishboundInput.Pressed(Key.DownArrow, KeyCode.DownArrow) || WishboundInput.Pressed(Key.Tab, KeyCode.Tab)){magnet.Selected=Step(magnet.Selected,1,list.Count);keyboardSelection=true;}
        if(WishboundInput.Pressed(Key.UpArrow, KeyCode.UpArrow)){magnet.Selected=Step(magnet.Selected,-1,list.Count);keyboardSelection=true;}
        if(!settingsOpen) return;
        if(WishboundInput.Pressed(Key.Escape, KeyCode.Escape)){CloseSettings();return;}
        if(magnet.Selected>=0 && (WishboundInput.Pressed(Key.LeftArrow, KeyCode.LeftArrow) || WishboundInput.Pressed(Key.RightArrow, KeyCode.RightArrow)))
        {
            float sign=WishboundInput.Pressed(Key.LeftArrow, KeyCode.LeftArrow)?-1:1;
            if(magnet.Selected==0)ChangeVolume(sign*.1f);
            else if(magnet.Selected==1)ChangeSensitivity(sign*.02f);
            else if(magnet.Selected==2)list[magnet.Selected].action();
        }
    }

    void ClearFocus(){ magnet.Reset(); foreach(var c in main)c.heat=0; foreach(var c in settings)c.heat=0; }

    public void SelectPlay(){if(!ready || busy || settingsOpen)return;StoryFlags.LoadActive();busy=true;PlayRequested=true;}
    public void OpenSettings(){ if(!ready||busy||settingsOpen)return; StartCoroutine(Morph(true)); }
    public void CloseSettings(){ if(!ready||busy||!settingsOpen)return; StartCoroutine(Morph(false)); }
    IEnumerator Morph(bool open)
    {
        busy=true; ClearFocus();
        var panel=(RectTransform)settingsGroup.transform;
        if(open){ RefreshSettings(); settingsGroup.alpha=0; settingsGroup.gameObject.SetActive(true); }
        const float D=.6f;
        for(float t=0;t<D;t+=Time.unscaledDeltaTime)
        {
            float k=Ease(t/D);
            float veil=open?k:1f-k;
            Shader.SetGlobalFloat("_WishMenuBlur",veil);
            shade.color=new Color(.012f,.019f,.042f,Mathf.Lerp(.22f,.59f,veil));
            float outA=1-Ease(t/(D*.5f)), inA=Ease((t-D*.35f)/(D*.65f));
            float mainA=open?outA:inA, setA=open?inA:outA;
            float mainS=open?Mathf.Lerp(1f,.9f,k):Mathf.Lerp(.9f,1f,k);
            float setS=open?Mathf.Lerp(.9f,1f,k):Mathf.Lerp(1f,.9f,k);
            logoGroup.alpha=mainA; logo.localScale=Vector3.one*mainS;
            foreach(var c in main){c.group.alpha=mainA;c.rect.localScale=Vector3.one*mainS;}
            settingsGroup.alpha=setA; panel.localScale=Vector3.one*setS; panel.anchoredPosition=new Vector2(0,open?-28*(1-k):-28*k);
            yield return null;
        }
        settingsOpen=open; Shader.SetGlobalFloat("_WishMenuBlur",open?1f:0f);
        shade.color=new Color(.012f,.019f,.042f,open?.59f:.22f);
        logoGroup.alpha=open?0:1; logo.localScale=Vector3.one;
        foreach(var c in main){c.group.alpha=open?0:1;c.rect.localScale=Vector3.one;}
        settingsGroup.alpha=open?1:0; panel.localScale=Vector3.one; panel.anchoredPosition=Vector2.zero;
        if(!open)settingsGroup.gameObject.SetActive(false);
        ClearFocus(); busy=false;
    }
    public void ChangeVolume(float delta){WorldSound.Menu("Adjust"); WishboundSettings.SetVolume(WishboundSettings.Volume+delta);RefreshSettings();}
    public void ChangeSensitivity(float delta){WorldSound.Menu("Adjust"); WishboundSettings.SetSensitivity(WishboundSettings.Sensitivity+delta,player);RefreshSettings();}
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
        // hand the wisp over, at its current spot on QUIT, to the shared farewell sequence (input is dead from here on)
        Vector2 screen=magnet.ScreenPoint(root);
        ReleaseCursor();
        Cursor.lockState=CursorLockMode.None;Cursor.visible=false;
        WishboundFarewell.Play(screen);
    }
    public void ReleaseCursor()
    {
        ready = false;
        if (WispCursor.TitleOwner != this) return;
        WispCursor.TitleOwner = null;
        WispCursor.ScreenOverride = null;
    }

    void OnDisable()
    {
        Shader.SetGlobalFloat("_WishMenuBlur",0);
        if (WispCursor.TitleOwner != this) return;
        ReleaseCursor();
        if (!PlayRequested) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }
}