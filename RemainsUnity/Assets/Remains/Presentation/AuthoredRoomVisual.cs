using System;
using Remains.Content;
using Remains.Diagnostics;
using Remains.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Presentation
{
    public static class AuthoredRoomVisual
    {
        public static GameObject Create(string level,RoomLayout room,WorldState world=null)
        {
            using var timing=TransitionTiming.Measure("authored-room.create",level);
            RoomAuthoring source;
            using(TransitionTiming.Measure("authored-room.resolve",level))
                source=RoomAuthoring.Load(level,world);
            if(!source.HasAuthoredArtwork) throw new InvalidOperationException("Не перенесено оформление уровня: "+level);
            RoomAuthoring instance;
            using(TransitionTiming.Measure("authored-room.instantiate",level))
            {instance=UnityEngine.Object.Instantiate(source);instance.name="Комната: "+room.id;}
            try
            {
                using(TransitionTiming.Measure("authored-room.audit",level))
                    foreach(var audit in instance.GetComponentsInChildren<RoomImportAudit>(true)) audit.WarnIncomplete();
                using(TransitionTiming.Measure("authored-room.window-validation",level))
                    foreach(var mask in instance.GetComponentsInChildren<RoomWindowMaskAuthoring>(true)) mask.Validate();
                using(TransitionTiming.Measure("authored-room.previews",level))
                {
                    RoomTrainingContent.HidePreviews(instance);
                    foreach(var door in instance.GetComponentsInChildren<RoomDoorAuthoring>(true)) door.gameObject.SetActive(false);
                    foreach(var platform in instance.GetComponentsInChildren<RoomMovingPlatformPlacement>(true)) platform.gameObject.SetActive(false);
                    foreach(var button in instance.GetComponentsInChildren<RoomMechanismButtonPlacement>(true)) button.gameObject.SetActive(false);
                }
                var lighting=instance.GetComponent<RoomLightingAuthoring>();
                if(lighting==null) throw new InvalidOperationException("Нет авторинга света: "+level);
                var sorting=instance.GetComponent<SortingGroup>();if(sorting==null) sorting=instance.gameObject.AddComponent<SortingGroup>();
                sorting.sortingLayerID=0;sorting.sortingOrder=WorldVisualOrderPolicy.SceneryRoomOrder;
                var runtimeLighting=instance.gameObject.AddComponent<RoomLighting>();
                RoomLightProfile profile;
                using(TransitionTiming.Measure("authored-room.profile",level))
                    profile=lighting.CreateProfile(instance);
                using(TransitionTiming.Measure("authored-room.light-prepare",level))
                {
                    if(Application.isPlaying) runtimeLighting.InitializeAsync(room,profile);
                    else runtimeLighting.Initialize(room,profile);
                }
                return instance.gameObject;
            }
            catch {UnityEngine.Object.Destroy(instance.gameObject);throw;}
        }
    }
}
