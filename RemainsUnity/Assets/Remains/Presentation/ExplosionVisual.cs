using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class ExplosionVisual : MonoBehaviour
    {
        private const float FlashDuration=.16f,FlashReach=8,FlashWidth=.7f;
        private static readonly Color FlashRadiance=new(5,2.7f,1.25f);
        private ExplosionArtwork artwork;
        private CoopSession session;
        private string level;
        [SerializeField,InspectorName("Отрисовка")] private SpriteRenderer view;
        [SerializeField,InspectorName("Анимация")] private Remains.Content.ExplosionAnimationAsset animation;
        private float age;
        private int bornFrame;
        private WorldLightEmission flash;
        public void Initialize(CoopSession session,string level,Vector2 position,string lightId=null)
        {
            Validate();artwork=animation.CreateArtwork();this.session=session;this.level=level;transform.position=position;
            bornFrame=Time.frameCount;flash=default;
            if(lightId!=null)
                flash=new WorldLightEmission(lightId,new[]{new Vector4(position.x,position.y,1,0)},
                    new Rect(position-Vector2.one*.02f,Vector2.one*.04f),FlashRadiance,FlashWidth,FlashReach);
            Draw(0);
        }
        public bool TryGetFlash(out WorldLightEmission emission)
        {
            emission=default;
            if(flash.Id==null || !gameObject.activeInHierarchy || !view.enabled || age>=FlashDuration ||
                session!=null && session.LocalActor?.level!=level)return false;
            float fade=1-age/FlashDuration;
            emission=new WorldLightEmission(flash.Id,flash.Samples,flash.Bounds,flash.Radiance,flash.Width,flash.Reach,fade*fade);
            return true;
        }
        public void Validate()
        {
            if(view==null || !view.transform.IsChildOf(transform) || view.sharedMaterial==null || animation==null)
                throw new System.InvalidOperationException($"Взрыв {name}: отсутствует дочерний renderer, материал или анимация.");
            animation.Validate();
        }
        public bool Draw(float elapsed)
        {
            age=elapsed;
            int frame=Mathf.FloorToInt(elapsed*artwork.framesPerSecond);
            view.enabled=frame>=0 && frame<artwork.frames.Length;
            if(view.enabled) view.sprite=artwork.frames[frame];
            return view.enabled;
        }
        private void Update()
        {
            if(session!=null)
            {
                if(session.LocalActor?.level!=level) {view.enabled=false;Destroy(gameObject);return;}
                if(session.Pause.IsPaused(level)) return;
            }
            // Every detonation has one bright frame even when it arrives during a slow frame.
            if(Time.frameCount!=bornFrame && !Draw(age+Time.deltaTime)) Destroy(gameObject);
        }
    }
}
