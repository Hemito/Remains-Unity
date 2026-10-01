using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Presentation
{
    [RequireComponent(typeof(SortingGroup))]
    public sealed class ActorVisualGroup : MonoBehaviour
    {
        public string ActorId {get;private set;}
        public int Order {get=>GetComponent<SortingGroup>().sortingOrder;set=>GetComponent<SortingGroup>().sortingOrder=value;}
        public void Initialize(string actorId) {ActorId=actorId;Order=ActorVisualLayers.FirstActorOrder;}
    }
}
