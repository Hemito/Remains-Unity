using System;
using System.Linq;
using UnityEngine;

namespace Remains.Content
{
    public static class RoomTrainingContent
    {
        public static TrainingLayout Read(RoomAuthoring room,string patch,string level)
        {
            if(room==null || string.IsNullOrWhiteSpace(patch) || string.IsNullOrWhiteSpace(level))
                throw new InvalidOperationException("Нет комнаты, версии или уровня расстановки.");
            var actors=room.GetComponentsInChildren<RoomActorPlacement>(true).Where(p=>room.Included(p)).Select(p=>p.Read(room)).ToArray();
            var containers=room.GetComponentsInChildren<RoomContainerPlacement>(true).Where(p=>room.Included(p)).Select(p=>
            {var placement=p.Read(room);placement.relocateExisting=true;return placement;}).ToArray();
            var ids=actors.Concat(containers).Select(p=>p.id).ToArray();
            if(ids.Distinct(StringComparer.Ordinal).Count()!=ids.Length) throw new InvalidOperationException("Повтор ID в расстановке комнаты: "+room.name);
            return new TrainingLayout {patch=patch,level=level,actors=actors,containers=containers};
        }
        public static void HidePreviews(RoomAuthoring instance)
        {
            // Placement nodes remain available as data. Gameplay representations are created from WorldState.
            foreach(var marker in instance.GetComponentsInChildren<RoomActorPlacement>(true))
                foreach(var view in marker.GetComponentsInChildren<Renderer>(true)) view.enabled=false;
            foreach(var marker in instance.GetComponentsInChildren<RoomContainerPlacement>(true))
                foreach(var view in marker.GetComponentsInChildren<Renderer>(true)) view.enabled=false;
        }
    }
}
