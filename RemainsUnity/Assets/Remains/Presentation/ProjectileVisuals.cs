using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class ProjectileVisuals : MonoBehaviour
    {
        private CoopSession session;
        private ProjectileController controller;
        private WorldVisualOrderTable orders;
        private readonly Dictionary<string,ProjectileVisual> views=new();
        private readonly List<string> removed=new();
        public event Action<ProjectileVisual> TrailDrawn;
        public void Initialize(CoopSession session,ProjectileController controller,WorldVisualOrderTable orderTable)
        {
            this.session=session;this.controller=controller;orders=orderTable??throw new ArgumentNullException(nameof(orderTable));controller.Created+=Created;
            session.AttackReceived+=ObserveAttack;
            session.Changed+=Reconcile;Reconcile();
        }
        private void ObserveAttack(AttackEvent attack)
        {
            if(views.TryGetValue(attack.id,out var view) && view!=null)
                view.JoinRemoteMuzzle();
        }
        private void Reconcile()
        {
            string level=session.LocalActor?.level;
            removed.Clear();
            foreach(var pair in views)
                if(pair.Value==null || pair.Value.Level!=level || session.World==null)
                    removed.Add(pair.Key);
            foreach(var id in removed)
                {
                    var view=views[id];views.Remove(id);
                    if(view!=null)
                    {
                        view.gameObject.SetActive(false);
                        if(Application.isPlaying) Destroy(view.gameObject);else DestroyImmediate(view.gameObject);
                    }
                }
            if(session.World==null) return;
            // A representation can be needed after a level change or ownership recovery,
            // even though the simulation flight already exists. Never replay old impacts.
            foreach(var flight in controller.Flights) if(!flight.path.Finished) Created(flight);
        }
        private void Created(ProjectileFlight flight)
        {
            string id=flight.state.launch.id;
            if(views.TryGetValue(id,out var existing) && existing!=null) return;
            if(flight.state.launch.level!=session.LocalActor?.level || !ShotArtwork.TryGet(flight.state.launch.weapon,out var artwork)) return;
            var view=new GameObject("Полёт "+flight.state.launch.id).AddComponent<ProjectileVisual>();
            view.transform.SetParent(transform,false);view.Initialize(flight,artwork,session);views[id]=view;
            view.gameObject.AddComponent<WorldVisualOrderBinding>().Initialize(orders,WorldVisualKind.Effect,"projectile:"+id,null,view.Artwork,flight.state.launch.level);
            view.TrailDrawn+=value=>TrailDrawn?.Invoke(value);
            view.Ended+=ended=>{if(views.TryGetValue(id,out var current) && current==ended) views.Remove(id);};
        }
        private void OnDestroy()
        {
            if(controller!=null) controller.Created-=Created;
            if(session!=null) session.Changed-=Reconcile;
            if(session!=null) session.AttackReceived-=ObserveAttack;
        }
    }
    public sealed class ProjectileVisual : MonoBehaviour
    {
        private ProjectileFlight flight;
        private ShotArtwork artwork;
        private SpriteRenderer view;
        public SpriteRenderer Artwork=>view;
        private CoopSession session;
        private ProjectileTrailJoin join;
        private BeamMuzzleAttachment beamMuzzle;
        private int beamGeneration;
        private int joinLifecycle,joinFlightGeneration;
        public Vector2 TrailStart {get;private set;}
        public Vector2 TrailEnd {get;private set;}
        public float TrailTailDistance {get;private set;}
        public bool TrailVisible=>view!=null && view.enabled && !flight.path.Finished;
        public string AttackId=>flight.state.launch.id;
        public Vector2 PhysicalHead=>flight.path.Position;
        public string AttackerId=>flight.state.launch.attacker;
        public event Action<ProjectileVisual> TrailDrawn;
        public string Level=>flight?.state.launch.level;
        public event Action<ProjectileVisual> Ended;
        public string MuzzleJoinState {get;private set;}="not-requested";
        public string MuzzlePoseState {get;private set;}="not-sampled";
        private void OnDestroy()=>Ended?.Invoke(this);
        public void JoinRemoteMuzzle()
        {
            var actor=session.World?.Find(flight.state.launch.attacker);
            if(artwork.beam && actor!=null && actor.level==Level && flight.path.Traveled==0)
            {
                beamMuzzle=new BeamMuzzleAttachment(flight.state.launch,actor);
                beamGeneration=flight.state.generation;
            }
            MuzzleJoinState=join!=null?"already-joined":actor==null?"missing-actor":
                flight.state.launch.sourceOwner==session.LocalOwner?"local-shot":actor.level!=Level?"other-level":
                actor.weapon!=Remains.Content.WeaponRigs.PresentationId(flight.state.launch.weapon)?"other-weapon":
                flight.path.Traveled!=0?"advanced-flight":"joined";
            if(join!=null || actor==null || flight.state.launch.sourceOwner==session.LocalOwner || actor.level!=Level ||
                actor.weapon!=Remains.Content.WeaponRigs.PresentationId(flight.state.launch.weapon) || flight.path.Traveled!=0) return;
            join=new ProjectileTrailJoin();joinLifecycle=actor.lifecycleSequence;
            joinFlightGeneration=flight.state.generation;
        }
        public void Initialize(ProjectileFlight flight,ShotArtwork artwork,CoopSession session)
        {
            this.flight=flight;this.artwork=artwork;this.session=session;
            view=gameObject.AddComponent<SpriteRenderer>();view.sortingOrder=ActorVisualLayers.EffectOrder;
            SpriteArtworkMaterial.SetSelfIlluminated(view);
            var direction=flight.path.Direction;
            transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg);
        }
        private void LateUpdate()
        {
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section("ProjectileVisuals.LateUpdate");
            if(flight.cancelled || session.LocalActor?.level!=flight.state.launch.level) {Destroy(gameObject);return;}
            // Presentation samples the frozen path as well, including a newly restored view.
            var path=flight.path;
            // Recovery resumes a canonical flight, not a fresh muzzle departure.
            if(join!=null && joinFlightGeneration!=flight.state.generation) {join=null;MuzzlePoseState="flight-recovered";}
            if(beamMuzzle!=null && beamGeneration!=flight.state.generation) beamMuzzle=null;
            if(path.Finished && !artwork.beam)
            {
                int frame=Mathf.FloorToInt(flight.finishedAge*artwork.impactFramesPerSecond);
                if(!flight.contact || frame>=artwork.impact.Length) {Destroy(gameObject);return;}
                SpriteArtworkMaterial.Apply(view,artwork.impact[frame]);view.enabled=true;view.color=Color.white;
                transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2(path.Direction.y,path.Direction.x)*Mathf.Rad2Deg);
                transform.localScale=Vector3.one;transform.position=path.Position;return;
            }
            float alpha=artwork.beam && path.Finished?Mathf.Clamp01(1-flight.finishedAge/artwork.lifetime):1;
            if(alpha<=0) {Destroy(gameObject);return;}
            float head=path.Traveled,tail=artwork.beam?0:Mathf.Max(0,head-artwork.length);
            TrailTailDistance=tail;
            TrailStart=path.Origin+path.Direction*tail;TrailEnd=path.Position;
            if(artwork.beam && beamMuzzle!=null && path.Finished)
            {
                var offset=beamMuzzle.Offset(session.World?.Find(flight.state.launch.attacker));
                TrailStart+=offset;TrailEnd+=offset;
            }
            else if(join!=null)
            {
                var actor=session.World?.Find(flight.state.launch.attacker);
                MuzzlePoseState=actor==null?"missing-actor":actor.level!=Level?"other-level":
                    actor.lifecycleSequence!=joinLifecycle?"actor-lifecycle":
                    actor.weapon!=Remains.Content.WeaponRigs.PresentationId(flight.state.launch.weapon)?"other-weapon":actor.down?"down":"current";
                Vector2? muzzle=MuzzlePoseState=="current"?WeaponPose.Muzzle(actor):null;
                TrailStart=join.Tail(path.Origin,path.Direction,head,tail,artwork.length,artwork.beam,muzzle);
            }
            // Never show a backwards tracer when the newer visible barrel is ahead of
            // the physical projectile; a contact still renders immediately above.
            view.enabled=head>tail && ProjectileTrailJoin.IsForward(TrailStart,TrailEnd,path.Direction);
            SpriteArtworkMaterial.Apply(view,artwork.trace);view.color=new Color(1,1,1,alpha);
            Vector2 delta=TrailEnd-TrailStart;
            transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg);
            transform.localScale=new Vector3(delta.magnitude/artwork.trace.bounds.size.x,1,1);
            transform.position=(TrailStart+TrailEnd)*.5f;
            if(view.enabled) TrailDrawn?.Invoke(this);
        }
    }
}
