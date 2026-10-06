using System;
using System.Collections.Generic;
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
        private readonly HashSet<string> retainedProps=new(),retainedContainers=new(),retainedWeapons=new(),retainedFlights=new();
        private readonly List<string> removed=new();
        public WeaponView FindWeapon(string actorId)=>weapons.TryGetValue(actorId,out var view)?view:null;
        public PropView FindProp(string id)=>props.TryGetValue(id,out var view)?view:null;
        public PropView FindContainer(string id)=>containers.TryGetValue(id,out var view)?view:null;
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
                if(NpcNaturalAttack.IsNatural(attack)) return;
                if(attack.weapon is "spear" or "crowbar" or "grenade" || attack.level!=session.LocalActor?.level) return;
                if(Remains.Content.WeaponCatalog.Get(attack.weapon).projectileSpeed>0) return;
                float limit=Remains.Content.WeaponCatalog.Get(attack.weapon).stopAtTerrain?
                    ShotObstacles.Limit(geometry.Get(attack.level),attack.Origin,attack.End):1;
                if(Remains.Content.WeaponCatalog.Get(attack.weapon).stopAtFirstActor)
                    limit=AttackContacts.VisualLimit(session.World.actors,attack,session.World.friendlyFire,limit);
                if(!ShotArtwork.TryGet(attack.weapon,out var artwork)) return;
                var shot=new GameObject("Выстрел "+attack.id).AddComponent<ShotVisual>();
                shot.Initialize(attack,limit,artwork,session,layers.Orders);
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
            retainedProps.Clear();retainedContainers.Clear();retainedWeapons.Clear();retainedFlights.Clear();
            if(session.World==null || level==null)
            {
                Prune(props,retainedProps);Prune(containers,retainedContainers);Prune(weapons,retainedWeapons);Prune(flights,retainedFlights);
                return;
            }
            foreach(var prop in session.World.props) if(prop.level==level) retainedProps.Add(prop.id);
            foreach(var container in session.World.containers) if(container.level==level) retainedContainers.Add(container.id);
            foreach(var actor in session.World.actors)
                if(ActorPresence.IsPresent(actor) && !string.IsNullOrEmpty(actor.weapon) && Remains.Content.ActorTypes.Get(actor.actorType).hasWeapon && actor.level==level) retainedWeapons.Add(actor.id);
            foreach(var flight in grenades.Flights) if(flight.state.level==level) retainedFlights.Add(flight.state.id);
            Prune(props,retainedProps);Prune(containers,retainedContainers);Prune(weapons,retainedWeapons);Prune(flights,retainedFlights);
            foreach(var container in session.World.containers)
            {
                if(container.level!=level) continue;
                if(!containers.TryGetValue(container.id,out var view))
                {view=propArtwork.CreateContainerView(container);view.BindOrder(layers.Orders,WorldVisualKind.Container,container.id);containers.Add(container.id,view);}
                view.ApplyContainerAppearance(container);
                view.DrawMotion(container,session.LocalOwner,session.Pause.IsPaused(container.level),0);view.SetVisible(container.level==session.LocalActor?.level);
            }
            foreach(var prop in session.World.props) if(prop.level==level && !props.ContainsKey(prop.id))
            {var view=propArtwork.CreateView(prop.definition,prop.id,prop.width);view.BindOrder(layers.Orders,WorldVisualKind.Prop,prop.id);props.Add(prop.id,view);}
            foreach(var actor in session.World.actors)
                if(ActorPresence.IsPresent(actor) && !string.IsNullOrEmpty(actor.weapon) && Remains.Content.ActorTypes.Get(actor.actorType).hasWeapon && actor.level==level && !weapons.ContainsKey(actor.id))
                {
                    weapons.Add(actor.id,CreateWeapon(actor));
                }
            foreach(var flight in grenades.Flights)
                if(flight.state.level==level && !flights.ContainsKey(flight.state.id))
                {
                    var view=CreateGrenade("Граната "+flight.state.id);
                    view.gameObject.AddComponent<WorldVisualOrderBinding>().Initialize(layers.Orders,WorldVisualKind.Grenade,flight.state.id,null,view,flight.state.level,true);
                    flights.Add(flight.state.id,view);
                }
        }
        private void Prune<T>(Dictionary<string,T> views,HashSet<string> retained) where T:Component
        {
            removed.Clear();
            foreach(var id in views.Keys) if(!retained.Contains(id)) removed.Add(id);
            foreach(var id in removed)
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
                if(string.IsNullOrEmpty(actor.weapon) || HeldItemSelection.HasSelection(actor) || actor.weapon=="grenade" && !actor.heldItemPoseValid) {view.SetVisible(false);continue;}
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
