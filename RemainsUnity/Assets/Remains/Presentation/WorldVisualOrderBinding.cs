using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Presentation
{
    // Cached references: no hierarchy search or sorting in Update/LateUpdate.
    public sealed class WorldVisualOrderBinding : MonoBehaviour
    {
        private SortingGroup group;
        private Renderer artwork;
        private bool transient;
        private string registeredWorld;
        private int? previousOrder;
        public WorldVisualOrderTable Orders {get;private set;}
        public WorldVisualKind Kind {get;private set;}
        public string Id {get;private set;}
        public WorldVisualCategory Category=>Orders.GetCategory(Kind,Id);
        public void Initialize(WorldVisualOrderTable orders,WorldVisualKind kind,string id,SortingGroup owner,Renderer renderer=null,string effectLevel=null,bool registerTransient=false)
        {
            if(Orders!=null) throw new InvalidOperationException("Порядок изображения уже привязан: "+name);
            if(orders==null || owner==null && renderer==null) throw new ArgumentException("Привязка требует общую таблицу и рендерер или группу: "+name);
            Kind=kind;Id=id;group=owner;artwork=renderer;
            transient=kind==WorldVisualKind.Effect || registerTransient;
            if(transient) registeredWorld=orders.RegisterTransient(kind,id,effectLevel,kind==WorldVisualKind.Effect?WorldVisualCategory.Effect:WorldVisualCategory.OrdinaryObject);
            try {orders.GetOrder(kind,id);} // Missing registration is a data error, never a default layer.
            catch {if(transient) orders.ReleaseTransient(kind,id,registeredWorld);throw;}
            Orders=orders;Orders.Changed+=Apply;Apply();
        }
        private void Apply()
        {
            // The representation manager removes disappeared entries after Reconcile.
            if(transient && registeredWorld!=Orders.WorldId || !Orders.TryGet(Kind,Id,out int order,out _))
            {if(transient && artwork!=null) artwork.forceRenderingOff=true;return;}
            if(transient && artwork!=null) artwork.forceRenderingOff=false;
            if(previousOrder==order) return;previousOrder=order;
            if(group!=null) {group.sortingLayerID=0;group.sortingOrder=order;}
            else {artwork.sortingLayerID=0;artwork.sortingOrder=order;}
        }
        private void OnDestroy()
        {if(Orders==null) return;Orders.Changed-=Apply;if(transient) Orders.ReleaseTransient(Kind,Id,registeredWorld);}
    }

    public static class RenderSorting
    {
        // Call on selection/creation and cache the result. The category remains in
        // the shared binding/table; an inner pony SortingGroup is not a world layer.
        public static SortingGroup FindOwner(Renderer renderer)
        {
            if(renderer==null) throw new ArgumentNullException(nameof(renderer));
            SortingGroup result=null;
            for(var node=renderer.transform;node!=null;node=node.parent)
                if(node.TryGetComponent<SortingGroup>(out var group) && group.enabled && group.gameObject.activeInHierarchy) result=group;
            return result;
        }
    }
}
