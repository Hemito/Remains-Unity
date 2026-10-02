using System;
using System.Linq;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Content
{
    public sealed class RoomLightingAuthoring : MonoBehaviour
    {
        [SerializeField,InspectorName("Общее освещение")] private Color ambient;
        public void SetAmbient(Color value)=>ambient=value;
        public RoomLightProfile CreateProfile(RoomAuthoring room)=>new() {ambient=ambient,
            lights=GetComponentsInChildren<RoomLightAuthoring>(true).Where(light=>room.Included(light)).Select(light=>light.Read(room)).ToArray()};
    }
}
