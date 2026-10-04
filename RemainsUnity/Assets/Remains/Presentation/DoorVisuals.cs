using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class DoorVisuals : MonoBehaviour
    {
        private sealed class Entry {public DoorView view;public long revision;public bool open;}
        private readonly Dictionary<string,Entry> views=new(StringComparer.Ordinal);
        private readonly Dictionary<string,DoorView> prefabs=new(StringComparer.Ordinal);
        private CoopSession session;
        private GameplayAudio playback;
        private string worldId,level;
        public int SoundChanges {get;private set;}
        public int PlayedSounds {get;private set;}
        public int Count=>views.Count;
        public DoorView Find(string id)=>views.TryGetValue(id,out var entry)?entry.view:null;
        public void Initialize(CoopSession value,GameplayAudio audio)
        {session=value;playback=audio;session.Changed+=Refresh;Refresh();}
        private void Refresh()=>Reconcile(session.World,session.LocalActor?.level);
        public void Reconcile(WorldState world,string visibleLevel)
        {
            if(worldId!=world?.worldId || level!=visibleLevel)
            {Clear();worldId=world?.worldId;level=visibleLevel;}
            if(world==null || visibleLevel==null) {Clear();return;}
            var selected=world.doors.Where(d=>d.level==visibleLevel).ToArray();
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var door in selected)
            {
                door.Validate();
                if(!ids.Add(door.id)) throw new InvalidOperationException("Повторный ID изображения двери: "+door.id);
            }
            foreach(string id in views.Keys.Where(id=>!ids.Contains(id)).ToArray()) Remove(id);
            foreach(var door in selected)
            {
                if(views.TryGetValue(door.id,out var old) && old.view.Definition.Id!=door.definition) Remove(door.id);
                bool exists=views.TryGetValue(door.id,out var entry);
                if(!exists)
                {
                    if(!prefabs.TryGetValue(door.definition,out var prefab))
                    {
                        prefab=Resources.Load<DoorView>("Presentation/Doors/"+door.definition);
                        if(prefab==null || prefab.Definition==null || prefab.Definition.Id!=door.definition)
                            throw new InvalidOperationException($"Нет префаба двери: id={door.id}, type={door.definition}, level={door.level}.");
                        prefab.Definition.Validate();prefabs.Add(door.definition,prefab);
                    }
                    entry=new Entry {view=Instantiate(prefab,transform),revision=door.revision,open=door.open};
                    entry.view.name="Дверь: "+door.id;views.Add(door.id,entry);
                }
                entry.view.transform.localPosition=new Vector3(door.x+door.width*.5f,door.y,0);
                var size=entry.view.Definition.Size;
                entry.view.transform.localScale=new Vector3(door.width/size.x,door.height/size.y,1);
                entry.view.Show(door.open);
                if(exists && door.revision>entry.revision && door.open!=entry.open)
                {
                    SoundChanges++;
                    var clip=door.open?entry.view.Definition.OpenSound:entry.view.Definition.CloseSound;
                    if(playback!=null && playback.PlayContact(clip,door.level,new Vector2(door.x+door.width*.5f,door.y+door.height*.5f),1)) PlayedSounds++;
                }
                entry.revision=door.revision;entry.open=door.open;
            }
        }
        private void Remove(string id)
        {var entry=views[id];entry.view.gameObject.SetActive(false);if(Application.isPlaying) Destroy(entry.view.gameObject);else DestroyImmediate(entry.view.gameObject);views.Remove(id);}
        private void Clear() {foreach(string id in views.Keys.ToArray()) Remove(id);}
        private void OnDestroy() {if(session!=null) session.Changed-=Refresh;}
    }
}
