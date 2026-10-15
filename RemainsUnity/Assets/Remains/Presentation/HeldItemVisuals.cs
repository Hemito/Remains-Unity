using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    // Inventory/asset resolution runs on selection and transaction changes, not in Update.
    public sealed class HeldItemVisuals : MonoBehaviour
    {
        private CoopSession session;
        private ActorVisualLayers layers;
        private InventoryArtwork artwork;
        private SpriteRenderer prefab;
        private readonly Dictionary<string,SpriteRenderer> views=new();
        private readonly Dictionary<string,string> definitions=new();
        private readonly HashSet<string> missing=new();
        private readonly HashSet<string> retained=new(StringComparer.Ordinal);
        private readonly List<string> removed=new();
        public SpriteRenderer Find(string actorId)=>views.TryGetValue(actorId,out var view)?view:null;
        public void Initialize(CoopSession value,ActorVisualLayers visualLayers)
        {
            session=value;layers=visualLayers;artwork=new InventoryArtwork();artwork.PrepareWorldMaterials();
            prefab=Resources.Load<SpriteRenderer>("Presentation/HeldItem");
            if(prefab==null) throw new InvalidOperationException("Нет сохранённого префаба удерживаемой вещи Presentation/HeldItem.");
            session.Changed+=Refresh;session.Deposits.Reconciled+=Refresh;
            Refresh();
        }
        private void Refresh()
        {
            retained.Clear();
            if(session.World!=null)
            foreach(var actor in session.World.actors)
            {
                if(actor.level!=session.LocalActor?.level || !ActorPresence.IsPresent(actor) || ActorSessionRules.IsPassive(actor)) continue;
                var item=HeldItemSelection.Selected(actor);
                if(item==null || InventoryAvailability.Count(actor,item)<=0) continue;
                if(!artwork.TryGet(item.definition,out var sprite))
                {
                    if(missing.Add(actor.id+":"+item.definition))
                        Debug.LogWarning($"Нет графики удерживаемой вещи: actor={actor.id}, item={item.id}, definition={item.definition}. Изображение пропущено.",this);
                    continue;
                }
                retained.Add(actor.id);
                if(!views.TryGetValue(actor.id,out var view))
                {
                    view=Instantiate(prefab);view.name="Предмет в копытах "+actor.id;
                    layers.Attach(actor.id,view.transform);views.Add(actor.id,view);
                }
                if(!definitions.TryGetValue(actor.id,out var previous) || previous!=item.definition)
                {
                    SpriteArtworkMaterial.Apply(view,sprite);
                    // A held mine keeps its world size (original artwork scale); other items are shown icon-sized.
                    float scale=MineRules.PlacedWeapon(item.definition)!=null?1:DroppedItemTransfer.Width/sprite.bounds.size.x;
                    view.transform.localScale=new Vector3(scale,scale,1);
                    definitions[actor.id]=item.definition;
                }
                view.enabled=actor.heldItemPoseValid;
            }
            removed.Clear();
            foreach(var id in views.Keys) if(!retained.Contains(id)) removed.Add(id);
            foreach(var id in removed)
            {views[id].gameObject.SetActive(false);Destroy(views[id].gameObject);views.Remove(id);definitions.Remove(id);}
        }
        private void Update()
        {
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section("HeldItemVisuals.Update");
            if(session?.World==null) return;
            foreach(var pair in views)
            {
                var actor=session.World.Find(pair.Key);
                pair.Value.enabled=actor!=null && HeldItemSelection.HasSelection(actor) && actor.heldItemPoseValid &&
                    ActorPresence.IsPresent(actor) && !ActorSessionRules.IsPassive(actor) && actor.level==session.LocalActor?.level;
                if(!pair.Value.enabled) continue;
                pair.Value.transform.position=HeldItemPose.Mount(actor);
                pair.Value.transform.rotation=Quaternion.Euler(0,0,actor.weaponAngle);pair.Value.flipY=actor.weaponFlipY;
            }
        }
        private void OnDestroy()
        {
            if(session==null) return;
            session.Changed-=Refresh;session.Deposits.Reconciled-=Refresh;
        }
    }
}
