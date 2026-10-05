using System;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    public static class AuthoredRoomVisual
    {
        public static GameObject Create(string level,RoomLayout room,WorldState world=null)
        {
            var source=RoomAuthoring.Load(level,world);
            if(!source.HasAuthoredArtwork) throw new InvalidOperationException("Не перенесено оформление уровня: "+level);
            var instance=UnityEngine.Object.Instantiate(source);instance.name="Комната: "+room.id;
            try
            {
                foreach(var audit in instance.GetComponentsInChildren<RoomImportAudit>(true)) audit.WarnIncomplete();
                RoomTrainingContent.HidePreviews(instance);
                foreach(var door in instance.GetComponentsInChildren<RoomDoorAuthoring>(true)) door.gameObject.SetActive(false);
                foreach(var platform in instance.GetComponentsInChildren<RoomMovingPlatformPlacement>(true)) platform.gameObject.SetActive(false);
                foreach(var button in instance.GetComponentsInChildren<RoomMechanismButtonPlacement>(true)) button.gameObject.SetActive(false);
                var lighting=instance.GetComponent<RoomLightingAuthoring>();
                if(lighting==null) throw new InvalidOperationException("Нет авторинга света: "+level);
                instance.gameObject.AddComponent<RoomLighting>().Initialize(room,lighting.CreateProfile(instance));return instance.gameObject;
            }
            catch {UnityEngine.Object.Destroy(instance.gameObject);throw;}
        }
    }
}
