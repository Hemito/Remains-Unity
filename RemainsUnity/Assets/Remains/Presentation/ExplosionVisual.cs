using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class ExplosionVisual : MonoBehaviour
    {
        private ExplosionArtwork artwork;
        private CoopSession session;
        private string level;
        private SpriteRenderer view;
        private float age;
        public void Initialize(ExplosionArtwork artwork,CoopSession session,string level,Vector2 position)
        {
            this.artwork=artwork;this.session=session;this.level=level;transform.position=position;
            view=gameObject.AddComponent<SpriteRenderer>();view.sortingOrder=ActorVisualLayers.EffectOrder;
            SpriteArtworkMaterial.SetSelfIlluminated(view);
            Draw(0);
        }
        public bool Draw(float elapsed)
        {
            int frame=Mathf.FloorToInt(elapsed*artwork.framesPerSecond);
            view.enabled=frame>=0 && frame<artwork.frames.Length;
            if(view.enabled) SpriteArtworkMaterial.Apply(view,artwork.frames[frame]);
            return view.enabled;
        }
        private void Update()
        {
            if(session!=null)
            {
                if(session.LocalActor?.level!=level) {view.enabled=false;Destroy(gameObject);return;}
                if(session.Pause.IsPaused(level)) return;
            }
            age+=Time.deltaTime;if(!Draw(age)) Destroy(gameObject);
        }
    }
}
