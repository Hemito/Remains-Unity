using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Presentation
{
    [RequireComponent(typeof(SortingGroup))]
    public sealed class ActorVisualGroup : MonoBehaviour
    {
        public string ActorId {get;private set;}
        public SortingGroup Sorting {get;private set;}
        public WorldVisualOrderBinding Depth {get;private set;}
        public int Order {get=>Sorting.sortingOrder;set=>Sorting.sortingOrder=value;}
        private void Awake()=>Sorting=GetComponent<SortingGroup>();
        public void Initialize(string actorId,WorldVisualOrderTable orders)
        {ActorId=actorId;Sorting=GetComponent<SortingGroup>();Depth=gameObject.AddComponent<WorldVisualOrderBinding>();Depth.Initialize(orders,WorldVisualKind.Actor,actorId,Sorting);}
    }
}
