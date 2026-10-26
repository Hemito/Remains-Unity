using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    [DefaultExecutionOrder(1000)] // Publish light after the actors have drawn their LateUpdate poses.
    public sealed class InteractionVisuals : MonoBehaviour
    {
        private CoopSession session;
        private GrenadeController grenades;
        private SimulationGeometry geometry;
        private SpriteRenderer grenadePrefab;
        private ActorVisualLayers layers;
        private PropArtwork propArtwork;
        private WeaponShotAnimation shotAnimation;
        private Action<IReadOnlyList<SpriteEmission>> muzzleLighting;
        private readonly List<SpriteEmission> muzzleEmissions=new();
        private readonly List<string> muzzleSourceIds=new();
        private Action<AttackEvent,SpriteEmission> prepareMuzzleShadow;
        public IReadOnlyList<string> MuzzleSourceIds=>muzzleSourceIds;
        public void ConfigureMuzzleLighting(Action<IReadOnlyList<SpriteEmission>> publish,
            Action<AttackEvent,SpriteEmission> prepare=null)
        {muzzleLighting=publish;prepareMuzzleShadow=prepare;}
        private WeaponReloadAnimation reloadAnimation;
        private CasingEffects casingEffects;
        public CasingEffects Casing=>casingEffects;
        private HeldPropColorSource heldColors;
        private string refreshedLevel;private long refreshedHolders=-1;
        private PonyMagicActivitySource ponyMagic;
        public WeaponReloadAnimation ReloadAnimation=>reloadAnimation;
        private readonly Dictionary<string,Sprite> sprites=new();
        private readonly Dictionary<string,PropView> props=new();
        private readonly Dictionary<string,PropView> containers=new();
        private readonly Dictionary<string,WeaponView> weapons=new();
        private readonly Dictionary<string,SpriteRenderer> flights=new();
        private readonly HashSet<string> missingGraphics=new();
        private readonly HashSet<string> retainedProps=new(),retainedContainers=new(),retainedWeapons=new(),retainedFlights=new();
        private readonly List<string> removed=new();
        public WeaponView FindWeapon(string actorId)=>weapons.TryGetValue(actorId,out var view)?view:null;
        public void AppendWeaponLightOccluders(List<LightOccluder> output)
            =>AppendWeaponLightOccluders(null,output);
        public void AppendWeaponLightOccluders(string actorId,List<LightOccluder> output)
        {
            foreach(var pair in weapons)
            {
                if(actorId!=null && pair.Key!=actorId) continue;
                var view=pair.Value;
                var renderer=view.Artwork;
                if(!view.gameObject.activeInHierarchy || !renderer.enabled || renderer.forceRenderingOff || renderer.color.a<=0) continue;
                // Shot art includes emissive muzzle pixels. Only the cached physical weapon
                // silhouette absorbs; use the same actual weapon transform, not another pose sample.
                if(!sprites.TryGetValue(view.Id,out var sprite))
                {if(missingGraphics.Add("shadow:"+view.Id)) Debug.LogWarning($"Нет силуэта оружия для тени: actor={pair.Key}, weapon={view.Id}.",this);continue;}
                output.Add(new LightOccluder(sprite,renderer.transform.localToWorldMatrix,renderer.color.a));
            }
        }
        public PropView FindProp(string id)=>props.TryGetValue(id,out var view)?view:null;
        public PropView FindContainer(string id)=>containers.TryGetValue(id,out var view)?view:null;
        // Install once before a world is presented. This source is rebuilt in
        // the same event as view binding, before any first draw of a new epoch.
        public void ConfigureHeldAppearance(HeldPropColorSource source)
            =>ConfigureHeldAppearance(source,null);
        public void ConfigureHeldAppearance(HeldPropColorSource source,PonyMagicActivitySource magic)
        {
            if(session==null) throw new InvalidOperationException("Цвет удержания подключён до инициализации представлений.");
            if(heldColors!=null) throw new InvalidOperationException("Повторный источник цветов удержания в представлениях.");
            heldColors=source??throw new ArgumentNullException(nameof(source));ponyMagic=magic;Refresh();
        }
        public void Initialize(CoopSession session,InteractionAdapter adapter,GrenadeController grenades,SimulationGeometry geometry,ActorVisualLayers layers)
            =>Initialize(session,adapter,grenades,geometry,layers,null);
        public void Initialize(CoopSession session,InteractionAdapter adapter,GrenadeController grenades,SimulationGeometry geometry,ActorVisualLayers layers,WeaponReloadController reload)
        {
            this.session=session;this.grenades=grenades;this.layers=layers;this.geometry=geometry;
            propArtwork=new PropArtwork();
            grenadePrefab=GrenadeArtwork.LoadPrefab();
            foreach(var pair in WeaponArtwork.Load()) sprites.Add(pair.Key,pair.Value);
            shotAnimation=gameObject.AddComponent<WeaponShotAnimation>();shotAnimation.Initialize(session);
            reloadAnimation=gameObject.AddComponent<WeaponReloadAnimation>();reloadAnimation.Initialize(session,reload);
            casingEffects=gameObject.AddComponent<CasingEffects>();casingEffects.Initialize(session,layers.Orders);
            ShotArtwork.Prepare();
            session.Changed+=Refresh;
            session.Deposits.Reconciled+=Refresh;
            grenades.Created+=OnGrenadeCreated;
            session.AttackReceived+=OnAttackReceived;
        }
        private void OnGrenadeCreated(GrenadeFlight flight)=>Refresh();
        private void OnAttackReceived(AttackEvent attack)
        {
                if(prepareMuzzleShadow!=null && shotAnimation.TryAttackEmission(attack,out var emission))
                    prepareMuzzleShadow(attack,emission);
                if(NpcNaturalAttack.IsNatural(attack) || attack.weapon=="mtrap-bite") return;
                if(attack.weapon is "spear" or "crowbar" || Remains.Content.WeaponCatalog.IsExplosive(attack.weapon) || attack.level!=session.LocalActor?.level) return;
                if(Remains.Content.WeaponCatalog.Get(attack.weapon).projectileSpeed>0) return;
                float limit=Remains.Content.WeaponCatalog.Get(attack.weapon).stopAtTerrain?
                    ShotObstacles.Limit(geometry.Get(attack.level),attack.Origin,attack.End):1;
                if(Remains.Content.WeaponCatalog.Get(attack.weapon).stopAtFirstActor)
                    limit=AttackContacts.VisualLimit(session.World.actors,attack,session.World.friendlyFire,limit);
                if(!ShotArtwork.TryGet(attack.weapon,out var artwork)) return;
                var shot=new GameObject("Выстрел "+attack.id).AddComponent<ShotVisual>();
                shot.transform.SetParent(transform,true);
                shot.Initialize(attack,limit,artwork,session,layers.Orders);
        }
        private void OnDestroy()
        {
            if(session!=null) {session.Changed-=Refresh;session.Deposits.Reconciled-=Refresh;session.AttackReceived-=OnAttackReceived;}
            if(grenades!=null) grenades.Created-=OnGrenadeCreated;
        }
        private SpriteRenderer CreateGrenade(string name)
        {
            var renderer=Instantiate(grenadePrefab,transform);renderer.name=name;
            return renderer;
        }
        private void Refresh()
        {
            string level=session.LocalActor?.level;
            if(session.World!=null && level!=null && heldColors==null)
                throw new InvalidOperationException("Не подключён источник цвета удержания: world="+session.World.worldId+", level="+level+".");
            heldColors?.Rebuild(session.World);
            ponyMagic?.Rebuild(session.World,heldColors);
            retainedProps.Clear();retainedContainers.Clear();retainedWeapons.Clear();retainedFlights.Clear();
            if(session.World==null || level==null)
            {
                Prune(props,retainedProps);Prune(containers,retainedContainers);Prune(weapons,retainedWeapons);Prune(flights,retainedFlights);
                refreshedLevel=null;
                return;
            }
            // Body and container views (~1400 on the factory) only when they, the level or the holders changed.
            bool sameContext=level==refreshedLevel && heldColors!=null && heldColors.Revision==refreshedHolders;
            bool bodies=session.LastChange.Any(WorldChangeKind.Containers|WorldChangeKind.Props) || !sameContext;
            refreshedLevel=level;refreshedHolders=heldColors?.Revision??-1;
            // Only some bodies changed (physics moves a few every snapshot): update those views, not all ~1400.
            if(bodies && sameContext && !session.LastChange.Any(WorldChangeKind.Identity) && session.LastChangedParts!=null)
            {
                bodies=false;
                foreach(string key in session.LastChangedParts)
                {
                    bool container=key.StartsWith("containers:",StringComparison.Ordinal);
                    if(!container && !key.StartsWith("props:",StringComparison.Ordinal)) continue;
                    string id=key.Substring(container?11:6);
                    var part=WorldProps.Find(session.World,id);
                    if(part==null) throw new InvalidOperationException($"Изменённое тело отсутствует в мире: {key}.");
                    if(part.level!=level) {Remove(container?(Dictionary<string,PropView>)containers:props,id);continue;}
                    if(part is ContainerState state) UpdateContainer(state,level);else UpdateProp(part,level);
                }
            }
            if(bodies)
            {
            foreach(var prop in session.World.props) if(prop.level==level) retainedProps.Add(prop.id);
            foreach(var container in session.World.containers) if(container.level==level) retainedContainers.Add(container.id);
            Prune(props,retainedProps);Prune(containers,retainedContainers);
            }
            foreach(var actor in session.World.actors)
                if(ActorPresence.IsPresent(actor) && !string.IsNullOrEmpty(actor.weapon) && Remains.Content.ActorTypes.Get(actor.actorType).hasWeapon && actor.level==level) retainedWeapons.Add(actor.id);
            foreach(var flight in grenades.Flights) if(flight.state.level==level) retainedFlights.Add(flight.state.id);
            Prune(weapons,retainedWeapons);Prune(flights,retainedFlights);
            if(bodies)
            {
            foreach(var container in session.World.containers) if(container.level==level) UpdateContainer(container,level);
            foreach(var prop in session.World.props) if(prop.level==level) UpdateProp(prop,level);
            }
            foreach(var actor in session.World.actors)
                if(ActorPresence.IsPresent(actor) && !string.IsNullOrEmpty(actor.weapon) && Remains.Content.ActorTypes.Get(actor.actorType).hasWeapon && actor.level==level)
                {
                    if(weapons.TryGetValue(actor.id,out var previous))
                    {
                        if(previous.Id==actor.weapon) continue;
                        previous.gameObject.SetActive(false);Destroy(previous.gameObject);
                    }
                    weapons[actor.id]=CreateWeapon(actor);
                }
            foreach(var flight in grenades.Flights)
                if(flight.state.level==level && !flights.ContainsKey(flight.state.id))
                {
                    var view=CreateGrenade("Граната "+flight.state.id);
                    view.gameObject.AddComponent<WorldVisualOrderBinding>().Initialize(layers.Orders,WorldVisualKind.Grenade,flight.state.id,null,view,flight.state.level,true);
                    flights.Add(flight.state.id,view);
                }
        }
        private void UpdateContainer(ContainerState container,string level)
        {
            if(!containers.TryGetValue(container.id,out var view))
            {view=propArtwork.CreateContainerView(container);if(view==null)return;view.transform.SetParent(transform,true);view.BindOrder(layers.Orders,WorldVisualKind.Container,container.id);containers.Add(container.id,view);}
            view.ApplyContainerAppearance(container);
            view.ApplyHeldAppearance(container,heldColors);
            view.DrawMotion(container,session.LocalOwner,session.Pause.IsPaused(container.level),0);view.SetVisible(container.level==level);
        }
        private void UpdateProp(PropState prop,string level)
        {
            if(!props.TryGetValue(prop.id,out var view))
            {view=propArtwork.CreateView(prop.definition,prop.id,prop.width);view.transform.SetParent(transform,true);view.BindOrder(layers.Orders,WorldVisualKind.Prop,prop.id);props.Add(prop.id,view);}
            view.ApplyHeldAppearance(prop,heldColors);
        }
        private void Remove<T>(Dictionary<string,T> views,string id) where T:Component
        {
            if(!views.TryGetValue(id,out var view)) return;
            view.gameObject.SetActive(false);Destroy(view.gameObject);views.Remove(id);
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
            var rest=view.ResolveRestSprite();SpriteArtworkMaterial.Prepare(rest);view.SetFrame(rest);
            sprites[actor.weapon]=rest;
            layers.Attach(actor.id,view.transform);return view;
        }
        private void Update()
        {
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section("InteractionVisuals.Update");
            muzzleEmissions.Clear();
            muzzleSourceIds.Clear();
            if(session?.World==null) return;
            // Read once: both are per-frame constants for ~1400 body views.
            string shownLevel=session.LocalActor?.level,localOwner=session.LocalOwner;
            foreach(var prop in session.World.props)
                if(props.TryGetValue(prop.id,out var view)) {view.DrawMotion(prop,localOwner,session.Pause.IsPaused(prop.level),Time.deltaTime);view.SetVisible(prop.level==shownLevel);}
            foreach(var container in session.World.containers)
                if(containers.TryGetValue(container.id,out var view))
                {view.DrawMotion(container,localOwner,session.Pause.IsPaused(container.level),Time.deltaTime);view.SetVisible(container.level==shownLevel);}
            foreach(var actor in session.World.actors)
            {
                if(!weapons.TryGetValue(actor.id,out var view)) continue;
                if(string.IsNullOrEmpty(actor.weapon) || HeldItemSelection.HasSelection(actor) || actor.weapon=="grenade" && !actor.heldItemPoseValid) {view.SetVisible(false);continue;}
                if(!sprites.TryGetValue(actor.weapon,out var sprite))
                { if(missingGraphics.Add(actor.id+":"+actor.weapon)) Debug.LogWarning($"Отсутствует графика оружия {actor.weapon} у {actor.id}.",this);view.SetVisible(false);continue; }
                if(view.Id!=actor.weapon)
                {
                    if(missingGraphics.Add("binding:"+actor.id+":"+actor.weapon))
                        Debug.LogWarning($"Представление оружия не обновлено событием: actor={actor.id}, view={view.Id}, weapon={actor.weapon}.",this);
                    view.SetVisible(false);continue;
                }
                view.SetVisible(ActorPresence.IsPresent(actor) && !ActorSessionRules.IsPassive(actor) && actor.level==shownLevel);
                sprite=shotAnimation.Select(actor,sprite);
                sprite=reloadAnimation.Select(actor,sprite);
                view.SetFrame(sprite);
                var direction=WeaponPose.Direction(actor);
                var pose=WeaponPose.SampleRanged(actor);
                view.transform.position=actor.weapon is "spear" or "crowbar"?
                    WeaponPose.Origin(actor)+direction*(actor.weaponReach-sprite.bounds.max.x):pose.Root;
                view.transform.rotation=Quaternion.Euler(0,0,pose.Angle);
                view.SetMirrored(WeaponPose.Flip(actor));
                if(view.gameObject.activeInHierarchy && shotAnimation.TryEmission(actor,sprite,view.transform.localToWorldMatrix,out var emission))
                {muzzleEmissions.Add(emission);muzzleSourceIds.Add("flash:"+shotAnimation.EmissionId(actor));}
            }
            foreach(var pair in flights) pair.Value.enabled=false;
            foreach(var flight in grenades.Flights) if(flights.TryGetValue(flight.message.id,out var view))
            {
                view.enabled=flight.message.level==shownLevel;view.transform.position=flight.position;
                view.transform.rotation=Quaternion.Euler(0,0,flight.state.angle);view.flipY=flight.state.mirrored;
            }
            shotAnimation.Step(Time.deltaTime);
            reloadAnimation.Step(Time.deltaTime);
            casingEffects.Step(Time.deltaTime);
        }
        private void LateUpdate() {using(Remains.Diagnostics.FrameHitchLog.Section("InteractionVisuals.LateUpdate")) muzzleLighting?.Invoke(muzzleEmissions);}
    }

}
