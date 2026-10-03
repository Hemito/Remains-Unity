using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    // Presentation only: this object never decides a contact or applies damage.
    public sealed class ShotVisual : MonoBehaviour
    {
        private ShotArtwork artwork;
        private SpriteRenderer view;
        private CoopSession session;
        private Vector2 origin,direction;
        private float distance,age;
        private bool contact;
        private string level;
        public void Initialize(AttackEvent attack,float limit,ShotArtwork artwork,CoopSession session=null)
        {
            this.artwork=artwork;this.session=session;level=attack.level;
            origin=attack.Origin;var delta=Vector2.Lerp(attack.Origin,attack.End,Mathf.Clamp01(limit))-origin;
            distance=delta.magnitude;direction=delta.normalized;contact=limit<1;
            view=gameObject.AddComponent<SpriteRenderer>();view.sortingOrder=ActorVisualLayers.EffectOrder;
            SpriteArtworkMaterial.SetSelfIlluminated(view);
            transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg);
            Draw(0);
        }
        public bool Draw(float elapsed)
        {
            float head,tail;float alpha=1;
            Sprite sprite=artwork.trace;
            if(artwork.beam)
            {head=distance;tail=0;alpha=Mathf.Clamp01(1-elapsed/artwork.lifetime);}
            else
            {
                float arrival=distance/artwork.speed;
                if(elapsed>=arrival)
                {
                    int frame=Mathf.FloorToInt((elapsed-arrival)*artwork.impactFramesPerSecond);
                    if(!contact || frame>=artwork.impact.Length) {view.enabled=false;return false;}
                    SpriteArtworkMaterial.Apply(view,artwork.impact[frame]);view.enabled=true;view.color=Color.white;
                    transform.localScale=Vector3.one;transform.position=origin+direction*distance;return true;
                }
                head=Mathf.Min(distance,elapsed*artwork.speed);tail=Mathf.Max(0,head-artwork.length);
            }
            float length=head-tail;
            view.enabled=alpha>0 && length>0;
            if(!view.enabled) return elapsed==0;
            SpriteArtworkMaterial.Apply(view,sprite);view.color=new Color(1,1,1,alpha);
            transform.localScale=new Vector3(length/sprite.bounds.size.x,1,1);
            transform.position=origin+direction*((head+tail)*.5f);return true;
        }
        private void Update()
        {
            if(session!=null)
            {
                if(session.LocalActor?.level!=level) {Destroy(gameObject);return;}
                if(session.Pause.IsPaused(level)) return;
            }
            age+=Time.deltaTime;if(!Draw(age)) Destroy(gameObject);
        }
    }
}
