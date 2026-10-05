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
        private SpriteRenderer grenadePrefab;
        private ActorVisualLayers layers;
        private PropArtwork propArtwork;
        private WeaponShotAnimation shotAnimation;
        private readonly Dictionary<string,Sprite> sprites=new();
        private readonly Dictionary<string,PropView> props=new();
        private readonly Dictionary<string,PropView> containers=new();
        private readonly Dictionary<string,WeaponView> weapons=new();
        private readonly Dictionary<string,SpriteRenderer> flights=new();
        private readonly HashSet<string> missingGraphics=new();
        public WeaponView FindWeapon(string actorId)=>weapons.TryGetValue(actorId,out var view)?view:null;
        public void Initialize(CoopSession session,InteractionAdapter adapter,GrenadeController grenades,SimulationGeometry geometry,ActorVisualLayers layers)
        {
            this.session=session;this.grenades=grenades;this.layers=layers;
            propArtwork=new PropArtwork();
            grenadePrefab=GrenadeArtwork.LoadPrefab();
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
        private SpriteRenderer CreateGrenade(string name)
        {
            var renderer=Instantiate(grenadePrefab);renderer.name=name;
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
            Prune(weapons,new(session.World.actors.Where(a=>ActorPresence.IsPresent(a) && !string.IsNullOrEmpty(a.weapon) && Remains.Content.ActorTypes.Get(a.actorType).hasWeapon && a.level==level).Select(a=>a.id)));
            Prune(flights,new(grenades.Flights.Where(f=>f.state.level==level).Select(f=>f.state.id)));
            foreach(var container in session.World.containers.Where(c=>c.level==level))
            {
                if(!containers.TryGetValue(container.id,out var view)) containers.Add(container.id,view=propArtwork.CreateContainerView(container));
                view.DrawMotion(container,session.LocalOwner,session.Pause.IsPaused(container.level),0);view.SetVisible(container.level==session.LocalActor?.level);
            }
            foreach(var prop in session.World.props.Where(p=>p.level==level)) if(!props.ContainsKey(prop.id)) props.Add(prop.id,propArtwork.CreateView(prop.definition,prop.id,prop.width));
            foreach(var actor in session.World.actors)
                if(ActorPresence.IsPresent(actor) && !string.IsNullOrEmpty(actor.weapon) && Remains.Content.ActorTypes.Get(actor.actorType).hasWeapon && actor.level==level && !weapons.ContainsKey(actor.id))
                {
                    weapons.Add(actor.id,CreateWeapon(actor));
                }
            foreach(var flight in grenades.Flights.Where(f=>f.state.level==level))
                if(!flights.ContainsKey(flight.state.id)) flights.Add(flight.state.id,CreateGrenade("Граната "+flight.state.id));
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
                if(props.TryGetValue(prop.id,out var view)) {view.DrawMotion(prop,session.LocalOwner,session.Pause.IsPaused(prop.level),Time.deltaTime);view.SetVisible(prop.level==session.LocalActor?.level);}
            foreach(var container in session.World.containers)
                if(containers.TryGetValue(container.id,out var view))
                {view.DrawMotion(container,session.LocalOwner,session.Pause.IsPaused(container.level),Time.deltaTime);view.SetVisible(container.level==session.LocalActor?.level);}
            foreach(var actor in session.World.actors)
            {
                if(!weapons.TryGetValue(actor.id,out var view)) continue;
                if(string.IsNullOrEmpty(actor.weapon)) {view.SetVisible(false);continue;}
                if(!sprites.TryGetValue(actor.weapon,out var sprite))
                { if(missingGraphics.Add(actor.id+":"+actor.weapon)) Debug.LogWarning($"Отсутствует графика оружия {actor.weapon} у {actor.id}.",this);view.SetVisible(false);continue; }
                if(view.Id!=actor.weapon)
                {view.gameObject.SetActive(false);Destroy(view.gameObject);view=CreateWeapon(actor);weapons[actor.id]=view;}
                view.SetVisible(ActorPresence.IsPresent(actor) && !ActorSessionRules.IsPassive(actor) && actor.level==session.LocalActor?.level);
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
            {
                view.enabled=flight.message.level==session.LocalActor?.level;view.transform.position=flight.position;
                view.transform.rotation=Quaternion.Euler(0,0,flight.state.angle);view.flipY=flight.state.mirrored;
            }
            shotAnimation.Step(Time.deltaTime);
        }
    }

}
