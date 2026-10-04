using System;
using System.Linq;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Content
{
    public sealed class RoomLightingAuthoring : MonoBehaviour
    {
        [SerializeField,InspectorName("Общее освещение")] private Color ambient;
        [SerializeField,Min(0),InspectorName("Множитель общего света для фона")] private float backgroundAmbientScale=1;
        public void SetBackgroundAmbientScale(float value)=>backgroundAmbientScale=value;
        public void SetAmbient(Color value)=>ambient=value;
        private void OnValidate() {if(!float.IsFinite(backgroundAmbientScale)) {Debug.LogWarning("Некорректный множитель света фона.",this);backgroundAmbientScale=1;}backgroundAmbientScale=Mathf.Max(0,backgroundAmbientScale);}
        public RoomLightProfile CreateProfile(RoomAuthoring room)=>new() {ambient=ambient,backgroundAmbientScale=backgroundAmbientScale,
            lights=GetComponentsInChildren<RoomLightAuthoring>(true).Where(light=>room.Included(light)).Select(light=>light.Read(room)).ToArray()};
    }
}
