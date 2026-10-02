using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class ExplosionVisual : MonoBehaviour
    {
        private ExplosionArtwork artwork;
        private CoopSession session;
        private string level;
        [SerializeField,InspectorName("Отрисовка")] private SpriteRenderer view;
        [SerializeField,InspectorName("Анимация")] private Remains.Content.ExplosionAnimationAsset animation;
        private float age;
        public void Initialize(CoopSession session,string level,Vector2 position)
        {
            Validate();artwork=animation.CreateArtwork();this.session=session;this.level=level;transform.position=position;age=0;


            Draw(0);
        }
        public void Validate()
        {
            if(view==null || !view.transform.IsChildOf(transform) || view.sharedMaterial==null || animation==null)
                throw new System.InvalidOperationException($"Взрыв {name}: отсутствует дочерний renderer, материал или анимация.");
            animation.Validate();
        }
        public bool Draw(float elapsed)
        {
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
            age+=Time.deltaTime;if(!Draw(age)) Destroy(gameObject);
        }
    }
}
