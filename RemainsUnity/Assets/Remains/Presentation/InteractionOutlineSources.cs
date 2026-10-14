using System;
using Remains.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Presentation
{
    public sealed class InteractionOutlineSources
    {
        private readonly ActorVisualLayers actors;
        private readonly InteractionVisuals items;
        private readonly DoorVisuals doors;
        private readonly TravelPointVisuals travel;
        private readonly MechanismVisuals mechanisms;
        private InteractionKind lastKind;
        private string lastId,bodyActorId;
        private Renderer lastRenderer;
        private SortingGroup lastGroup;
        private WorldOutlineSource lastSource;
        public InteractionOutlineSources(ActorVisualLayers actors,InteractionVisuals items,DoorVisuals doors,TravelPointVisuals travel,MechanismVisuals mechanisms)
        {
            this.actors=actors??throw new ArgumentNullException(nameof(actors));this.items=items??throw new ArgumentNullException(nameof(items));
            this.doors=doors??throw new ArgumentNullException(nameof(doors));this.travel=travel??throw new ArgumentNullException(nameof(travel));
            this.mechanisms=mechanisms??throw new ArgumentNullException(nameof(mechanisms));
        }
        public WorldOutlineSource Resolve(InteractionTarget target)
        {
            if(target.kind!=lastKind || target.id!=lastId)
            {
                lastKind=target.kind;lastId=target.id;lastRenderer=null;lastGroup=null;lastSource=default;
                bodyActorId=target.kind==InteractionKind.Container && target.id!=null && target.id.StartsWith(InventorySource.BodyPrefix,StringComparison.Ordinal)?
                    target.id.Substring(InventorySource.BodyPrefix.Length):null;
            }
            if(target.kind==InteractionKind.Revival || bodyActorId!=null)
            {
                var id=bodyActorId??target.id;
                var group=id!=null && actors.TryGetGroup(id,out var actor)?actor.Sorting:null;
                if(group!=lastGroup) {lastGroup=group;lastSource=WorldOutlineSource.ForGroup(group);}
                return lastSource;
            }
            Renderer renderer=target.id==null?null:target.kind switch
            {
                InteractionKind.Container or InteractionKind.Pickup=>items.FindContainer(target.id)?.Artwork,
                InteractionKind.Door=>doors.Find(target.id)?.Artwork,
                InteractionKind.Map or InteractionKind.Exit=>travel.Find(target.id),
                InteractionKind.Mechanism=>mechanisms.FindButtonRenderer(target.id),
                _=>null
            };
            // Ownership is prepared when a view appears/replaces its frame. An
            // unchanged pointer target only does a dictionary lookup each frame.
            if(renderer!=lastRenderer || lastSource.Group==null)
            {lastRenderer=renderer;lastSource=WorldOutlineSource.ForRenderer(renderer,target.kind==InteractionKind.Container);}
            return lastSource;
        }
    }
}
