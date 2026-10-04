using System;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Domain
{
    [Serializable]
    public sealed class DoorState : ExtensibleState
    {
        public string id,definition,level,surfaceTexture;
        // Lower-left corner of the closed blocking volume, independent of artwork.
        public float x,y,width,height;
        public bool open;
        public long revision;
        public Rect Bounds=>new(x,y,width,height);
        public float DistanceTo(Vector2 point)=>Vector2.Distance(point,new Vector2(Mathf.Clamp(point.x,x,x+width),Mathf.Clamp(point.y,y,y+height)));
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(definition) || string.IsNullOrWhiteSpace(level) ||
                string.IsNullOrWhiteSpace(surfaceTexture) || revision<0 || !float.IsFinite(x) || !float.IsFinite(y) ||
                !float.IsFinite(width) || !float.IsFinite(height) || width<=0 || height<=0 || !float.IsFinite(x+width) || !float.IsFinite(y+height))
                throw new InvalidOperationException($"Некорректная дверь: id={id}, type={definition}, level={level}, bounds=({x},{y},{width},{height}), revision={revision}.");
        }
    }
}
