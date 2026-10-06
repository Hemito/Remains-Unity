using System;
using System.Collections.Generic;
using Remains.Content;
using UnityEngine;

namespace Remains.Presentation
{
    // Saved room nodes already render maps/exits. Bind once after room creation;
    // queries do not scan the scene, load assets or manufacture another image.
    public sealed class TravelPointVisuals
    {
        private readonly Dictionary<string,Renderer> images=new(StringComparer.Ordinal);
        private GameObject boundInstance;
        public void Bind(GameObject instance)
        {
            if(instance!=null && instance==boundInstance) return;
            Clear();if(instance==null) return;
            var room=instance.GetComponent<RoomAuthoring>();
            if(room==null)
            {
                boundInstance=instance;
                Debug.LogWarning($"Реестр изображений переходов не заполнен: room={instance.name}, нет сохранённой RoomAuthoring; контуры карты/выхода недоступны.",instance);
                return;
            }
            foreach(var point in instance.GetComponentsInChildren<RoomTravelPointAuthoring>(true))
            {
                if(!room.Included(point)) continue;
                string id=point.StableId;
                if(string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("У изображения перехода нет ID: "+point.name);
                if(images.ContainsKey(id)) throw new InvalidOperationException("Повторное изображение перехода: "+id);
                var renderer=point.GetComponent<Renderer>();
                if(renderer==null)
                    Debug.LogWarning($"Нет фактического Renderer перехода: room={instance.name}, id={id}, object={point.name}; контур недоступен.",point);
                images.Add(id,renderer);
            }
            boundInstance=instance;
        }
        public Renderer Find(string id)=>id!=null && images.TryGetValue(id,out var image)?image:null;
        public void Clear() {images.Clear();boundInstance=null;}
    }
}
