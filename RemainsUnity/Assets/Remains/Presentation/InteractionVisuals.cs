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
        private PropArtwork propArtwork;
        private WeaponShotAnimation shotAnimation;
        private readonly Dictionary<string,Sprite> sprites=new();
        private readonly Dictionary<string,PropView> props=new();
        private readonly Dictionary<string,PropView> containers=new();
        private readonly Dictionary<string,WeaponView> weapons=new();
        private readonly Dictionary<string,SpriteRenderer> flights=new();
        private readonly HashSet<string> missingGraphics=new();
        public void Initialize(CoopSession session,InteractionAdapter adapter,GrenadeController grenades,SimulationGeometry geometry,ActorVisualLayers layers)
        {
            this.session=session;this.grenades=grenades;this.layers=layers;
            propArtwork=new PropArtwork();
            foreach(var key in new[]{"crate","grenade"}) sprites.Add(key,Load("Original/Props/"+key));
            foreach(var pair in WeaponArtwork.Load()) sprites.Add(pair.Key,pair.Value);
            shotAnimation=gameObject.AddComponent<WeaponShotAnimation>();shotAnimation.Initialize(session);
            ShotArtwork.Prepare();
            session.Changed+=Refresh;
            grenades.Created+=flight=>Refresh();
            session.AttackReceived+=attack=>
            {
                if(attack.weapon is "spear" or "crowbar" or "grenade" || attack.level!=session.LocalActor?.level) return;
                if(Remains.Content.WeaponCatalog.Get(attack.weapon).projectileSpeed>0) return;
                float limit=Remains.Content.WeaponCatalog.Get(attack.weapon).stopAtTerrain?
                    ShotObstacles.Limit(geometry.Get(attack.level),attack.Origin,attack.End):1;
                if(Remains.Content.WeaponCatalog.Get(attack.weapon).stopAtFirstActor)
                    limit=AttackContacts.VisualLimit(session.World.actors,attack,session.World.friendlyFire,limit);
                if(!ShotArtwork.TryGet(attack.weapon,out var artwork)) return;
                var shot=new GameObject("Выстрел "+attack.id).AddComponent<ShotVisual>();
                shot.Initialize(attack,limit,artwork,session);
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
            Prune(weapons,new(session.World.actors.Where(a=>ActorPresence.IsPresent(a) && Remains.Content.ActorTypes.Get(a.actorType).hasWeapon && a.level==level).Select(a=>a.id)));
            Prune(flights,new(grenades.Flights.Where(f=>f.state.level==level).Select(f=>f.state.id)));
            foreach(var container in session.World.containers.Where(c=>c.level==level))
            {
                if(!containers.TryGetValue(container.id,out var view)) containers.Add(container.id,view=propArtwork.CreateView(container.definition,"Контейнер "+container.id,container.width));
                view.transform.position=container.Center;view.SetVisible(container.level==session.LocalActor?.level);
            }
            foreach(var prop in session.World.props.Where(p=>p.level==level)) if(!props.ContainsKey(prop.id)) props.Add(prop.id,propArtwork.CreateView(prop.definition,prop.id,prop.width));
            foreach(var actor in session.World.actors)
                if(ActorPresence.IsPresent(actor) && Remains.Content.ActorTypes.Get(actor.actorType).hasWeapon && actor.level==level && !weapons.ContainsKey(actor.id))
                {
                    weapons.Add(actor.id,CreateWeapon(actor));
                }
            foreach(var flight in grenades.Flights.Where(f=>f.state.level==level))
                if(!flights.ContainsKey(flight.state.id)) flights.Add(flight.state.id,Create("Граната "+flight.state.id,sprites["grenade"],.35f));
        }
        private static void Prune<T>(Dictionary<string,T> views,HashSet<string> retained) where T:Component
        {
            foreach(var id in views.Keys.Where(id=>!retained.Contains(id)).ToArray())
            {views[id].gameObject.SetActive(false);Destroy(views[id].gameObject);views.Remove(id);}
        }
        private WeaponView CreateWeapon(ActorState actor)
        {
            var view=Instantiate(Remains.Content.WeaponRigs.Prefab(actor.weapon));view.name="Оружие "+actor.id;
            layers.Attach(actor.id,view.transform);return view;
        }
        private void Update()
        {
            if(session?.World==null) return;
            foreach(var prop in session.World.props)
                if(props.TryGetValue(prop.id,out var view)) {view.transform.position=prop.Center;view.SetVisible(prop.level==session.LocalActor?.level);}
            foreach(var container in session.World.containers)
                if(containers.TryGetValue(container.id,out var view))
                {view.transform.position=container.Center;view.SetVisible(container.level==session.LocalActor?.level);}
            foreach(var actor in session.World.actors)
            {
                if(!weapons.TryGetValue(actor.id,out var view)) continue;
                if(!sprites.TryGetValue(actor.weapon,out var sprite))
                { if(missingGraphics.Add(actor.id+":"+actor.weapon)) Debug.LogWarning($"Отсутствует графика оружия {actor.weapon} у {actor.id}.",this);view.Artwork.enabled=false;continue; }
                if(view.Id!=actor.weapon)
                {view.gameObject.SetActive(false);Destroy(view.gameObject);view=CreateWeapon(actor);weapons[actor.id]=view;}
                view.Artwork.enabled=ActorPresence.IsPresent(actor) && !actor.down && actor.level==session.LocalActor?.level;
                sprite=shotAnimation.Select(actor,sprite);
                view.SetFrame(sprite);
                var direction=WeaponPose.Direction(actor);
                view.transform.position=actor.weapon is "spear" or "crowbar"?
                    WeaponPose.Origin(actor)+direction*(actor.weaponReach-sprite.bounds.max.x):WeaponPose.Mount(actor);
                view.transform.rotation=Quaternion.Euler(0,0,actor.weaponAngle);
                view.SetMirrored(WeaponPose.Flip(actor));
            }
            foreach(var pair in flights) pair.Value.enabled=false;
            foreach(var flight in grenades.Flights) if(flights.TryGetValue(flight.message.id,out var view))
            { view.enabled=flight.message.level==session.LocalActor?.level;view.transform.position=flight.position; }
            shotAnimation.Step(Time.deltaTime);
        }
    }

}
