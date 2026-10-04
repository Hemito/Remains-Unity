using System;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    public static class AuthoredRoomVisual
    {
        public static GameObject Create(string level,RoomLayout room)
        {
            var source=RoomAuthoring.Load(level);
            if(!source.HasAuthoredArtwork) throw new InvalidOperationException("Не перенесено оформление уровня: "+level);
            var instance=UnityEngine.Object.Instantiate(source);instance.name="Комната: "+room.id;
            try
            {
                RoomTrainingContent.HidePreviews(instance);
                foreach(var door in instance.GetComponentsInChildren<RoomDoorAuthoring>(true)) door.gameObject.SetActive(false);
                var lighting=instance.GetComponent<RoomLightingAuthoring>();
                if(lighting==null) throw new InvalidOperationException("Нет авторинга света: "+level);
                instance.gameObject.AddComponent<RoomLighting>().Initialize(room,lighting.CreateProfile(instance));return instance.gameObject;
            }
            catch {UnityEngine.Object.Destroy(instance.gameObject);throw;}
        }
    }
}
