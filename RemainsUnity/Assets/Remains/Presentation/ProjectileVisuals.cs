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
        public void Initialize(CoopSession session,ProjectileController controller)
        {this.session=session;this.controller=controller;controller.Created+=Created;}
        private void Created(ProjectileFlight flight)
        {
            if(flight.state.launch.level!=session.LocalActor?.level || !ShotArtwork.TryGet(flight.state.launch.weapon,out var artwork)) return;
            var view=new GameObject("Полёт "+flight.state.launch.id).AddComponent<ProjectileVisual>();
            view.Initialize(flight,artwork,session);
        }
        private void OnDestroy() {if(controller!=null) controller.Created-=Created;}
    }
    public sealed class ProjectileVisual : MonoBehaviour
    {
        private ProjectileFlight flight;
        private ShotArtwork artwork;
        private SpriteRenderer view;
        private CoopSession session;
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
            if(session.LocalActor?.level!=flight.state.launch.level) {Destroy(gameObject);return;}
            if(session.Pause.IsPaused(flight.state.launch.level)) return;
            var path=flight.path;
            if(path.Finished && !artwork.beam)
            {
                int frame=Mathf.FloorToInt(flight.finishedAge*30);
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
