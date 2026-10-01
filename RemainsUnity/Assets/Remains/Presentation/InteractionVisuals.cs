using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class InteractionVisuals : MonoBehaviour
    {
        private CoopSession session;
        private GrenadeController grenades;
        private ActorVisualLayers layers;
        private readonly Dictionary<string,Sprite> sprites=new();
        private readonly Dictionary<string,SpriteRenderer> props=new();
        private readonly Dictionary<string,SpriteRenderer> containers=new();
        private readonly Dictionary<string,SpriteRenderer> weapons=new();
        private readonly Dictionary<string,SpriteRenderer> flights=new();
        private readonly HashSet<string> missingGraphics=new();
        public void Initialize(CoopSession session,InteractionAdapter adapter,GrenadeController grenades,SimulationGeometry geometry,ActorVisualLayers layers)
        {
            this.session=session;this.grenades=grenades;this.layers=layers;
            foreach(var key in new[]{"crate","grenade"}) sprites.Add(key,Load("Original/Props/"+key));
            foreach(var pair in WeaponArtwork.Load()) sprites.Add(pair.Key,pair.Value);
            session.Changed+=Refresh;
            grenades.Created+=flight=>Refresh();
            session.AttackReceived+=attack=>
            {
                if(attack.weapon is "spear" or "crowbar" or "grenade" || attack.level!=session.LocalActor?.level) return;
                float limit=Remains.Content.WeaponCatalog.Get(attack.weapon).stopAtTerrain?
                    ShotObstacles.Limit(geometry.Get(attack.level),attack.Origin,attack.End):1;
                var shot=new GameObject("Выстрел "+attack.id).AddComponent<ShotVisual>();shot.Initialize(attack,limit);
            };
        }
        private static Sprite Load(string path)
        {
            var texture=Resources.Load<Texture2D>(path);
            if(texture==null) throw new InvalidOperationException($"Отсутствует визуальный ресурс: {path}.");
            return Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(0.5f,0.5f),100);
        }
        private static SpriteRenderer Create(string name,Sprite sprite,float width)
        {
            var renderer=new GameObject(name).AddComponent<SpriteRenderer>();SpriteArtworkMaterial.Prepare(sprite);SpriteArtworkMaterial.Apply(renderer,sprite);renderer.sortingOrder=ActorVisualLayers.WorldObjectOrder;
            renderer.transform.localScale=Vector3.one*(width/sprite.bounds.size.x);
            return renderer;
        }
        private void Refresh()
        {
            string level=session.LocalActor?.level;
            if(session.World==null || level==null)
            {
                Prune(props,new());Prune(containers,new());Prune(weapons,new());Prune(flights,new());
                return;
            }
            Prune(props,new(session.World.props.Where(p=>p.level==level).Select(p=>p.id)));
            Prune(containers,new(session.World.containers.Where(c=>c.level==level).Select(c=>c.id)));
            Prune(weapons,new(session.World.actors.Where(a=>ActorPresence.IsPresent(a) && a.level==level).Select(a=>a.id)));
            Prune(flights,new(grenades.Flights.Where(f=>f.state.level==level).Select(f=>f.state.id)));
            foreach(var container in session.World.containers.Where(c=>c.level==level))
            {
                if(!containers.TryGetValue(container.id,out var view)) containers.Add(container.id,view=Create("Контейнер "+container.id,sprites["crate"],.8f));
                view.transform.position=container.Position+Vector2.up*.4f;view.enabled=container.level==session.LocalActor?.level;
            }
            foreach(var prop in session.World.props.Where(p=>p.level==level)) if(!props.ContainsKey(prop.id)) props.Add(prop.id,Create(prop.id,sprites["crate"],0.8f));
            foreach(var actor in session.World.actors)
                if(ActorPresence.IsPresent(actor) && actor.level==level && !weapons.ContainsKey(actor.id))
                {
                    var weapon=Create("Оружие "+actor.id,sprites["pistol"],sprites["pistol"].bounds.size.x);
                    weapon.sortingOrder=100;layers.Attach(actor.id,weapon.transform);weapons.Add(actor.id,weapon);
                }
            foreach(var flight in grenades.Flights.Where(f=>f.state.level==level))
                if(!flights.ContainsKey(flight.state.id)) flights.Add(flight.state.id,Create("Граната "+flight.state.id,sprites["grenade"],.35f));
        }
        private static void Prune(Dictionary<string,SpriteRenderer> views,HashSet<string> retained)
        {
            foreach(var id in views.Keys.Where(id=>!retained.Contains(id)).ToArray())
            {views[id].enabled=false;Destroy(views[id].gameObject);views.Remove(id);}
        }
        private void Update()
        {
            if(session?.World==null) return;
            foreach(var prop in session.World.props)
                if(props.TryGetValue(prop.id,out var view)) {view.transform.position=prop.Position;view.enabled=prop.level==session.LocalActor?.level;}
            foreach(var actor in session.World.actors)
            {
                if(!weapons.TryGetValue(actor.id,out var view)) continue;
                view.enabled=ActorPresence.IsPresent(actor) && !actor.down && actor.level==session.LocalActor?.level;
                if(!sprites.TryGetValue(actor.weapon,out var sprite))
                { if(missingGraphics.Add(actor.id+":"+actor.weapon)) Debug.LogWarning($"Отсутствует графика оружия {actor.weapon} у {actor.id}.",this);view.enabled=false;continue; }
                float width=sprite.bounds.size.x;
                if(view.sprite!=sprite) { SpriteArtworkMaterial.Apply(view,sprite);view.transform.localScale=Vector3.one*(width/sprite.bounds.size.x); }
                var direction=WeaponPose.Direction(actor);
                view.transform.position=actor.weapon is "spear" or "crowbar"?
                    WeaponPose.Origin(actor.Position)+direction*(actor.weaponReach-sprite.bounds.max.x):WeaponPose.Mount(actor);
                view.transform.rotation=Quaternion.Euler(0,0,actor.weaponAngle);
                view.flipY=WeaponPose.Flip(actor);
            }
            foreach(var pair in flights) pair.Value.enabled=false;
            foreach(var flight in grenades.Flights) if(flights.TryGetValue(flight.message.id,out var view))
            { view.enabled=flight.message.level==session.LocalActor?.level;view.transform.position=flight.position; }
        }
    }

    public sealed class ShotVisual : MonoBehaviour
    {
        private float expires;
        public void Initialize(AttackEvent attack,float limit)
        {
            var line=gameObject.AddComponent<LineRenderer>();line.positionCount=2;
            line.SetPosition(0,attack.Origin);line.SetPosition(1,Vector2.Lerp(attack.Origin,attack.End,limit));
            line.startWidth=line.endWidth=0.025f;
            line.material=new Material(Shader.Find("Sprites/Default"));
            line.startColor=line.endColor=attack.weapon=="energy"?Color.green:new Color(1,0.8f,0.3f);
            line.sortingOrder=ActorVisualLayers.EffectOrder;expires=Time.time+0.08f;
        }
        private void Update() { if(Time.time>=expires) Destroy(gameObject); }
        private void OnDestroy() { var line=GetComponent<LineRenderer>();if(line!=null) Destroy(line.material); }
    }
}
