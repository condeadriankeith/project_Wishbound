using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

internal sealed class WishProgressPage : MonoBehaviour
{
    sealed class Option : WispWord { public Action choose; }
    readonly List<Option> options=new List<Option>();
    readonly WispMagnet magnet=new WispMagnet();
    RectTransform root;
    Font font;
    Action<bool> finished;
    CanvasGroup group;
    int confirming, openedFrame;
    bool reset, keyboard;
    Vector2 previous;

    public static WishProgressPage Open(RectTransform parent,Font font,Action<bool> finished)
    {
        var go=new GameObject("Saved progress",typeof(RectTransform),typeof(CanvasGroup));
        var rect=(RectTransform)go.transform;
        rect.SetParent(parent,false);
        rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one;
        rect.offsetMin=rect.offsetMax=Vector2.zero;
        var page=go.AddComponent<WishProgressPage>();
        page.root=rect; page.group=go.GetComponent<CanvasGroup>();
        page.font=font; page.finished=finished; page.Draw();
        page.StartCoroutine(page.Reveal());
        return page;
    }
    IEnumerator Reveal()
    {
        for(float t=0;t<.24f;t+=Time.unscaledDeltaTime)
        {
            float a=Mathf.SmoothStep(0,1,t/.24f);
            group.alpha=a; root.localScale=Vector3.one*Mathf.Lerp(.97f,1,a);
            yield return null;
        }
        group.alpha=1; root.localScale=Vector3.one;
    }
    void Line(string label,float x,float y,int size,float width,Action choose=null)
    {
        var go=new GameObject(label,typeof(RectTransform),typeof(Text));
        var rect=(RectTransform)go.transform;rect.SetParent(root,false);
        rect.anchoredPosition=new Vector2(x,y);rect.sizeDelta=new Vector2(width,70);
        var text=go.GetComponent<Text>();text.font=font;text.fontSize=size;text.text=label;
        text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;
        if(choose!=null) options.Add(new Option{rect=rect,text=text,choose=choose,glow=WispMagnet.AddGlow(go)});
    }
    void Draw()
    {
        foreach(Transform row in root){row.gameObject.SetActive(false);Destroy(row.gameObject);}
        options.Clear();magnet.Reset();openedFrame=Time.frameCount;
        if(confirming!=0)
        {
            Line(reset?"Reset slot "+confirming+"?":"Start a new story in slot "+confirming+"?",0,110,46,1080);
            Line(reset?"This slot’s saved progress will be erased.":"Your other slots will keep their progress.",0,30,28,1000);
            Line("CANCEL",-160,-95,37,250,Back);
            Line(reset?"RESET":"START",160,-95,37,250,Confirm);
        }
        else
        {
            Line("SAVED PROGRESS",0,217,55,900);
            for(int row=0;row<3;row++)
            {
                int slot=row==0?WishSaveSlots.Active:row<=WishSaveSlots.Active-1?row:row+1;
                var save=WishSaveSlots.Read(slot);
                int y=116-row*97;
                string label="SLOT "+slot+(slot==WishSaveSlots.Active?"  ·  CURRENT":"");
                Line(label+"     "+(save==null?"Empty":save.Chapter),-75,y,29,690,()=>Select(slot));
                if(save!=null) Line("RESET",345,y,27,155,()=>Ask(slot,true));
            }
            Line("BACK",0,-218,38,300,Back);
        }
        keyboard=true;magnet.Selected=0; previous=WishboundInput.Pointer;
    }
    void Select(int slot)
    {
        if(WishSaveSlots.Read(slot)==null){Ask(slot,false);return;}
        bool changed=slot!=WishSaveSlots.Active;
        WishSaveSlots.Select(slot);Close(changed);
    }
    void Ask(int slot,bool erase){confirming=slot;reset=erase;Draw();}
    void Confirm()
    {
        if(reset)
        {
            WishSaveSlots.Reset(confirming);
            if(confirming==WishSaveSlots.Active){Close(true);return;}
            confirming=0;Draw();return;
        }
        WishSaveSlots.Select(confirming);Close(true);
    }
    void Back(){if(confirming!=0){confirming=0;Draw();}else Close(false);}
    void Close(bool changed){enabled=false;gameObject.SetActive(false);Destroy(gameObject);finished(changed);}
    void Update()
    {
        if(Time.frameCount==openedFrame)return;
        if(WishboundInput.Pressed(Key.Escape,KeyCode.Escape)){Back();return;}
        Vector2 screen=WishboundInput.Pointer;
        if((screen-previous).sqrMagnitude>2f)keyboard=false;
        previous=screen;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root,screen,null,out var pointer);
        bool clicked=WishboundInput.PrimaryPressed;
        if(clicked) keyboard=false;
        int delta=0;
        if(WishboundInput.Pressed(Key.DownArrow,KeyCode.DownArrow)||WishboundInput.Pressed(Key.Tab,KeyCode.Tab)||WishboundInput.Pressed(Key.RightArrow,KeyCode.RightArrow))delta=1;
        if(WishboundInput.Pressed(Key.UpArrow,KeyCode.UpArrow)||WishboundInput.Pressed(Key.LeftArrow,KeyCode.LeftArrow))delta=-1;
        if(delta!=0){keyboard=true;magnet.Selected=(magnet.Selected+delta+options.Count)%options.Count;}
        magnet.Track(root,options,pointer,Time.unscaledDeltaTime,keyboard);
        WispCursor.ScreenOverride=magnet.ScreenPoint(root);
        int selected=magnet.Selected;
        if(selected>=0 && (clicked||WishboundInput.Pressed(Key.Enter,KeyCode.Return)))options[selected].choose();
    }
}
