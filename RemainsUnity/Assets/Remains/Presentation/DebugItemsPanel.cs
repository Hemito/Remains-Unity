using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Remains.Presentation
{
    // The hidden item menu: DEBUG typed while the Esc menu is open (user 2026-11-03; physical keys, any layout) opens it.
    // A click gives one, Shift + click ten; a weapon is always one. The rows are built on the first opening, filtering
    // only switches them.
    public sealed class DebugItemsPanel
    {
        private const int ShiftCount=10;
        private static readonly (char letter,Key key)[] Letters={('D',Key.D),('E',Key.E),('B',Key.B),('U',Key.U),('G',Key.G)};
        private sealed class Entry {public DebugItem item;public string search;public GameObject row;}
        private readonly DebugItemsView view;private readonly CoopSession session;
        private readonly InventoryArtwork artwork;private readonly Func<string,string> itemName;private readonly Action changed;
        private readonly SecretWord word=new("DEBUG");
        private readonly List<Entry> entries=new();
        private string category;
        public bool IsOpen=>view.Panel.activeSelf;

        public DebugItemsPanel(DebugItemsView view,CoopSession session,InventoryArtwork artwork,Func<string,string> itemName,Action changed)
        {
            this.view=view;this.session=session;this.artwork=artwork;this.itemName=itemName;this.changed=changed;
            view.Validate();view.Row.gameObject.SetActive(false);view.Panel.SetActive(false);
            view.Close.onClick.AddListener(Close);
            view.Search.onValueChanged.AddListener(_=>Filter());
            for(int i=0;i<view.Tabs.Length;i++)
            {
                string tab=i==0?null:DebugItems.Categories[i-1];
                view.Tabs[i].onClick.AddListener(()=>{category=tab;Filter();});
            }
        }
        // While the Esc menu is open: true when the word has just been completed.
        public bool WatchWord(Keyboard keyboard,bool menuOpen)
        {
            if(!menuOpen || keyboard==null) {word.Reset();return false;}
            if(!keyboard.anyKey.wasPressedThisFrame) return false;
            char pressed='\0';
            foreach(var (letter,key) in Letters) if(keyboard[key].wasPressedThisFrame) pressed=letter;
            return word.Feed(pressed);
        }
        public void Open()
        {
            if(entries.Count==0) Build();
            view.Panel.SetActive(true);view.transform.SetAsLastSibling();Filter();changed();
        }
        public void Close() {view.Panel.SetActive(false);changed();}
        private void Build()
        {
            foreach(var item in DebugItems.Build())
            {
                var row=UnityEngine.Object.Instantiate(view.Row,view.Content);row.gameObject.SetActive(true);
                string title=itemName(item.id);row.Name.text=title;artwork.Set(row.Icon,item.id);row.name=item.id;
                string id=item.id;row.Button.onClick.AddListener(()=>Grant(id));
                entries.Add(new Entry {item=item,search=(title+" "+id).ToLowerInvariant(),row=row.gameObject});
            }
        }
        private void Filter()
        {
            string text=view.Search.text?.Trim().ToLowerInvariant();int shown=0;
            foreach(var entry in entries)
            {
                bool show=(category==null || entry.item.category==category) && (string.IsNullOrEmpty(text) || entry.search.Contains(text));
                if(entry.row.activeSelf!=show) entry.row.SetActive(show);
                if(show) shown++;
            }
            view.Status.text=$"Показано {shown} из {entries.Count} · ЛКМ — 1 · Shift+ЛКМ — {ShiftCount}";
        }
        private void Grant(string id)=>session.RequestDebugGrant(id,Keyboard.current?.shiftKey.isPressed==true?ShiftCount:1);
    }
}
