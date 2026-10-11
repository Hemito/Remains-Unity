using Remains.Domain;
using System.Collections.Generic;
using UnityEngine;
namespace Remains.Presentation
{
    public abstract class ActorVisual : MonoBehaviour
    {
        public abstract string ActorId {get;}
        public abstract void Bind(ActorState actor);
        public abstract void AppendLightOccluders(List<RoomRadianceField.LightOccluder> output);
        protected static void AppendSpriteOccluder(SpriteRenderer renderer,List<RoomRadianceField.LightOccluder> output)
        {
            if(renderer==null || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy || renderer.sprite==null || renderer.color.a<=0) return;
            var matrix=renderer.transform.localToWorldMatrix*Matrix4x4.Scale(new Vector3(renderer.flipX?-1:1,renderer.flipY?-1:1,1));
            output.Add(new RoomRadianceField.LightOccluder(renderer.sprite,matrix,renderer.color.a));
        }
        // Actual completed drawing, not a collision hull or a resampled actor pose.
        public virtual bool TryGetDrawnBounds(out Bounds bounds) {bounds=default;return false;}
        public virtual void SetVisible(bool visible)=>gameObject.SetActive(visible);
    }
}
