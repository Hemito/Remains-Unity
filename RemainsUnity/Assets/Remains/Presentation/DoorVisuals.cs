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
        private sealed class Entry {public DoorView view;public long revision;public bool open,destroyed;}
        private readonly Dictionary<string,Entry> views=new(StringComparer.Ordinal);
        private readonly Dictionary<string,DoorView> prefabs=new(StringComparer.Ordinal);
        private CoopSession session;
        private GameplayAudio playback;
        private WorldVisualOrderTable orders;
        private string worldId,level;
        public int SoundChanges {get;private set;}
        public int PlayedSounds {get;private set;}
        public int Count=>views.Count;
        public DoorView Find(string id)=>views.TryGetValue(id,out var entry)?entry.view:null;
        // Explicit registration supports startup content/mods and isolated checks.
        // Missing IDs still require their exact Resources prefab; no substitute is chosen.
        public void RegisterPrefab(DoorView prefab)
        {
            if(prefab==null || prefab.Definition==null || prefab.Artwork==null) throw new ArgumentException("Регистрация проёма требует определение и изображение.");
            prefab.Definition.Validate();
            if(prefabs.ContainsKey(prefab.Definition.Id)) throw new InvalidOperationException("Повторный префаб проёма: "+prefab.Definition.Id);
            prefabs.Add(prefab.Definition.Id,prefab);
        }
        public void Initialize(CoopSession value,GameplayAudio audio,WorldVisualOrderTable orderTable)
        {session=value;playback=audio;orders=orderTable??throw new ArgumentNullException(nameof(orderTable));session.Changed+=Refresh;Refresh();}
        public void SetOrders(WorldVisualOrderTable value)=>orders=value??throw new ArgumentNullException(nameof(value));
        // Door views change only with doors, the visible level or the world (CoopSession.LastChange).
        private void Refresh()
        {
            if(!session.LastChange.Any(WorldChangeKind.Doors) && worldId==session.World?.worldId && level==session.LocalActor?.level) return;
            Reconcile(session.World,session.LocalActor?.level);
        }
        public void Reconcile(WorldState world,string visibleLevel)
        {
            if(session==null)
            {
                if(orders==null) throw new InvalidOperationException("Изолированная проверка проёмов требует явную таблицу SetOrders.");
                orders.Reconcile(world,visibleLevel);
            }
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
                if(views.TryGetValue(door.id,out var old))
                {
                    if(door.revision<old.revision) continue;
                    if(old.destroyed && !door.Destroyed)
                        throw new InvalidOperationException($"Проём {door.id}: новая версия {door.revision} пытается отменить разрушение.");
                    if(old.view.Definition.Id!=door.definition)
                    {
                        if(door.revision==old.revision) throw new InvalidOperationException($"Проём {door.id}: определение изменилось без новой версии {door.revision}.");
                        Remove(door.id);
                    }
                }
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
                    entry=new Entry {view=Instantiate(prefab,transform),revision=door.revision,open=door.open,destroyed=door.Destroyed};
                    if(orders==null) throw new InvalidOperationException("Изображения проёмов требуют общую таблицу слоёв.");
                    entry.view.BindOrder(orders,door.id);
                    entry.view.name="Дверь: "+door.id;views.Add(door.id,entry);
                }
                if(exists && door.revision==entry.revision && (entry.open!=door.open || entry.destroyed!=door.Destroyed))
                    throw new InvalidOperationException($"Проём {door.id}: снимок противоречит необратимому состоянию или версии {entry.revision}/{door.revision}.");
                entry.view.transform.localPosition=new Vector3(door.x+door.width*.5f,door.y,0);
                var size=entry.view.Definition.Size;
                entry.view.transform.localScale=new Vector3(door.width/size.x,door.height/size.y,1);
                entry.view.Show(door);
                if(exists && door.revision>entry.revision)
                {
                    var clip=!entry.destroyed && door.Destroyed?entry.view.Definition.DestroySound:
                        !door.Destroyed && door.open!=entry.open?(door.open?entry.view.Definition.OpenSound:entry.view.Definition.CloseSound):null;
                    if(clip!=null)
                    {
                        SoundChanges++;
                        if(playback!=null && playback.PlayContact(clip,door.level,new Vector2(door.x+door.width*.5f,door.y+door.height*.5f),1)) PlayedSounds++;
                    }
                }
                entry.revision=door.revision;entry.open=door.open;entry.destroyed=door.Destroyed;
            }
        }
        private void Remove(string id)
        {var entry=views[id];entry.view.gameObject.SetActive(false);if(Application.isPlaying) Destroy(entry.view.gameObject);else DestroyImmediate(entry.view.gameObject);views.Remove(id);}
        private void Clear() {foreach(string id in views.Keys.ToArray()) Remove(id);}
        private void OnDestroy() {if(session!=null) session.Changed-=Refresh;}
    }
}
