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
        public static GameObject Create(string level,RoomLayout room,WorldState world=null,bool staged=false)
        {
            using var timing=TransitionTiming.Measure("authored-room.create",level);
            if(FactoryContentSources.IsGenerated(world,level))
            {
                var factory=FactoryLevelRoot.Create(world,level);
                if(staged) factory.gameObject.SetActive(false);
                try
                {
                    factory.InstantiateRooms();
                    if(!factory.TryGetComponent<FactoryDoorAdapter>(out var doors) || !factory.TryGetComponent<FactoryLevelView>(out var view))
                        throw new InvalidOperationException("Корень завода: отсутствует авторский адаптер дверей или вид уровня.");
                    doors.Install(factory);
                    if(!factory.TryGetComponent<FactoryPropAdapter>(out var props))
                        throw new InvalidOperationException("Корень завода: отсутствует авторский адаптер физических объектов.");
                    props.Install(factory);
                    if(!factory.TryGetComponent<FactoryContainerAdapter>(out var containers))throw new InvalidOperationException("Нет авторского адаптера контейнеров завода.");
                    containers.Install(factory,world);
                    foreach(var door in factory.GetComponentsInChildren<RoomDoorAuthoring>(true)) door.gameObject.SetActive(false);
                    view.Initialize(factory,room);
                    return factory.gameObject;
                }
                catch {ReleaseFailed(factory.gameObject);throw;}
            }
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
                foreach(var composition in instance.GetComponentsInChildren<RoomBackdropCompositionAuthoring>(true)) composition.Validate();
                using(TransitionTiming.Measure("authored-room.previews",level))
                {
                    RoomTrainingContent.HidePreviews(instance);
                    foreach(var door in instance.GetComponentsInChildren<RoomDoorAuthoring>(true)) door.gameObject.SetActive(false);
                    foreach(var platform in instance.GetComponentsInChildren<RoomMovingPlatformPlacement>(true)) platform.gameObject.SetActive(false);
                    foreach(var button in instance.GetComponentsInChildren<RoomMechanismButtonPlacement>(true)) button.gameObject.SetActive(false);
                }
                // The hub drawn from the camp's room templates: authored artwork and old light are replaced by the
                // template level view; gameplay (surfaces, ladders, containers, targets, travel) stays as authored.
                if(instance.TryGetComponent<HubTemplateLevel>(out var hub))
                {
                    if(staged) instance.gameObject.SetActive(false);
                    if(!instance.TryGetComponent<FactoryLevelView>(out var hubView)) throw new InvalidOperationException("Хаб из шаблонов: нет вида уровня.");
                    foreach(var artwork in instance.GetComponentsInChildren<RoomArtworkAuthoring>(true))
                        foreach(var renderer in artwork.GetComponents<Renderer>()) renderer.enabled=false;
                    foreach(var fixture in instance.GetComponentsInChildren<RoomLightAuthoring>(true))
                        foreach(var renderer in fixture.GetComponentsInChildren<Renderer>(true)) renderer.enabled=false;
                    using(TransitionTiming.Measure("authored-room.hub-template",level)) {hub.Build(room);hubView.Initialize(hub,room);}
                    return instance.gameObject;
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
            catch {ReleaseFailed(instance.gameObject);throw;}
        }
        private static void ReleaseFailed(GameObject root)
        {if(Application.isPlaying) UnityEngine.Object.Destroy(root);else UnityEngine.Object.DestroyImmediate(root);}
    }
}
