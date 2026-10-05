using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class MechanismVisuals : MonoBehaviour
    {
        private readonly Dictionary<string,GameObject> platforms=new(StringComparer.Ordinal);
        private readonly Dictionary<string,(GameObject closed,GameObject opened)> buttons=new(StringComparer.Ordinal);
        private CoopSession session;
        private PlatformSimulation simulation;
        private string worldId,level;
        private WorldState boundWorld;
        public void Initialize(CoopSession value,PlatformSimulation poses)
        {session=value;simulation=poses;session.Changed+=Reconcile;Reconcile();}
        private void Reconcile()
        {
            var world=session.World;string visible=session.LocalActor?.level;
            if(ReferenceEquals(world,boundWorld) && level==visible) return;
            boundWorld=world;
            if(worldId!=world?.worldId || level!=visible) {Clear();worldId=world?.worldId;level=visible;}
            if(world==null || visible==null) return;
            var selectedPlatforms=world.movingPlatforms.Where(p=>!p.removed && p.level==visible).ToArray();
            var selectedButtons=world.mechanismButtons.Where(b=>!b.removed && b.level==visible).ToArray();
            foreach(string id in platforms.Keys.Where(id=>!selectedPlatforms.Any(p=>p.id==id)).ToArray())
            {Remove(platforms[id]);platforms.Remove(id);}
            foreach(string id in buttons.Keys.Where(id=>!selectedButtons.Any(b=>b.id==id)).ToArray())
            {var old=buttons[id];Remove(old.closed);Remove(old.opened);buttons.Remove(id);}
            var catalog=MechanismCatalogAsset.Load();
            foreach(var platform in selectedPlatforms)
                if(!platforms.ContainsKey(platform.id)) platforms.Add(platform.id,Create(catalog.Platform(platform.definition).Prefab,"Платформа: "+platform.id));
            foreach(var button in selectedButtons)
            {
                if(!buttons.TryGetValue(button.id,out var view))
                {
                    var type=catalog.Button(button.definition);
                    view=(Create(type.ClosedPrefab,"Кнопка: "+button.id),Create(type.OpenedPrefab,"Подсветка кнопки: "+button.id));buttons.Add(button.id,view);
                }
                view.closed.transform.localPosition=view.opened.transform.localPosition=new Vector3(button.x,button.y,0);
                view.closed.SetActive(!button.pressed);view.opened.SetActive(button.pressed);
            }
            DrawPlatforms();
        }
        private GameObject Create(GameObject prefab,string title)
        {
            if(prefab==null) throw new InvalidOperationException("Нет сохранённой графики механизма: "+title);
            var view=Instantiate(prefab,transform);view.name=title;
            foreach(var renderer in view.GetComponentsInChildren<Renderer>(true)) renderer.sortingOrder=ActorVisualLayers.RestingObjectOrder;
            return view;
        }
        private void LateUpdate()=>DrawPlatforms();
        private void DrawPlatforms()
        {
            foreach(var pair in platforms)
                if(simulation.TrySample(pair.Key,out var sample)) pair.Value.transform.localPosition=new Vector3(sample.Anchor.x,sample.Anchor.y,0);
                else throw new InvalidOperationException("Нет физического положения изображения платформы: "+pair.Key);
        }
        private static void Remove(GameObject view) {view.SetActive(false);Destroy(view);}
        private void Clear()
        {foreach(var view in platforms.Values) Remove(view);foreach(var view in buttons.Values) {Remove(view.closed);Remove(view.opened);}platforms.Clear();buttons.Clear();}
        private void OnDestroy() {if(session!=null) session.Changed-=Reconcile;}
    }
}
