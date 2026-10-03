using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly Dictionary<string,ProjectileVisual> views=new();
        public void Initialize(CoopSession session,ProjectileController controller)
        {
            this.session=session;this.controller=controller;controller.Created+=Created;
            session.Changed+=Reconcile;Reconcile();
        }
        private void Reconcile()
        {
            string level=session.LocalActor?.level;
            foreach(var pair in views.ToArray())
                if(pair.Value==null || pair.Value.Level!=level || session.World==null)
                {
                    views.Remove(pair.Key);
                    if(pair.Value!=null)
                    {
                        pair.Value.gameObject.SetActive(false);
                        if(Application.isPlaying) Destroy(pair.Value.gameObject);else DestroyImmediate(pair.Value.gameObject);
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
            view.Ended+=ended=>{if(views.TryGetValue(id,out var current) && current==ended) views.Remove(id);};
        }
        private void OnDestroy()
        {
            if(controller!=null) controller.Created-=Created;
            if(session!=null) session.Changed-=Reconcile;
        }
    }
    public sealed class ProjectileVisual : MonoBehaviour
    {
        private ProjectileFlight flight;
        private ShotArtwork artwork;
        private SpriteRenderer view;
        private CoopSession session;
        public string Level=>flight?.state.launch.level;
        public event Action<ProjectileVisual> Ended;
        private void OnDestroy()=>Ended?.Invoke(this);
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
            if(flight.cancelled || session.LocalActor?.level!=flight.state.launch.level) {Destroy(gameObject);return;}
            // Presentation samples the frozen path as well, including a newly restored view.
            var path=flight.path;
            if(path.Finished && !artwork.beam)
            {
                int frame=Mathf.FloorToInt(flight.finishedAge*artwork.impactFramesPerSecond);
                if(!flight.contact || frame>=artwork.impact.Length) {Destroy(gameObject);return;}
                SpriteArtworkMaterial.Apply(view,artwork.impact[frame]);view.enabled=true;view.color=Color.white;
                transform.localScale=Vector3.one;transform.position=path.Position;return;
            }
            float alpha=artwork.beam && path.Finished?Mathf.Clamp01(1-flight.finishedAge/artwork.lifetime):1;
            if(alpha<=0) {Destroy(gameObject);return;}
            float head=path.Traveled,tail=artwork.beam?0:Mathf.Max(0,head-artwork.length);
            view.enabled=head>tail;
            SpriteArtworkMaterial.Apply(view,artwork.trace);view.color=new Color(1,1,1,alpha);
            transform.localScale=new Vector3((head-tail)/artwork.trace.bounds.size.x,1,1);
            transform.position=path.Origin+path.Direction*((head+tail)*.5f);
        }
    }
}
