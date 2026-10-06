using Remains.Domain;
using UnityEngine;
namespace Remains.Presentation
{
    public abstract class ActorVisual : MonoBehaviour
    {
        public abstract string ActorId {get;}
        public abstract void Bind(ActorState actor);
        // Actual completed drawing, not a collision hull or a resampled actor pose.
        public virtual bool TryGetDrawnBounds(out Bounds bounds) {bounds=default;return false;}
        public virtual void SetVisible(bool visible)=>gameObject.SetActive(visible);
    }
}
