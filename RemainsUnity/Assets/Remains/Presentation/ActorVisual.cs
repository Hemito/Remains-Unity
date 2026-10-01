using Remains.Domain;
using UnityEngine;
namespace Remains.Presentation
{
    public abstract class ActorVisual : MonoBehaviour
    {
        public abstract string ActorId {get;}
        public abstract void Bind(ActorState actor);
        public virtual void SetVisible(bool visible)=>gameObject.SetActive(visible);
    }
}
