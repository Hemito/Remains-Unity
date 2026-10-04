using System;
using System.Linq;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    public sealed class RoomAuthoring : MonoBehaviour
    {
        [SerializeField,InspectorName("Границы, оформление и настройки комнаты")] private RoomLayout settings;
        [SerializeField,HideInInspector] private bool authoredArtwork;
        public bool HasAuthoredArtwork=>authoredArtwork;
        public void UseAuthoredArtwork() {authoredArtwork=true;settings.artwork=null;}
        public void Configure(RoomLayout source)
        {
            settings=StateJson.FromJson<RoomLayout>(StateJson.ToJson(source));
            settings.platforms=null;settings.ladders=null;
            if(authoredArtwork) settings.artwork=null;
        }
        public RoomLayout CreateLayout(bool includeArtwork=true)
        {
            if(settings==null) throw new InvalidOperationException($"Комната {name}: отсутствуют настройки.");
            var result=StateJson.FromJson<RoomLayout>(StateJson.ToJson(settings));
            // Rendering properties (including GPU-dependent material values) do not define simulation compatibility.
            if(!includeArtwork) result.artwork=null;
            else if(authoredArtwork) result.artwork=GetComponentsInChildren<RoomArtworkAuthoring>(true).Where(item=>Included(item)).Select(item=>item.Read(this)).ToArray();
            result.platforms=GetComponentsInChildren<RoomSurfaceAuthoring>(true).Where(item=>Included(item)).Select(item=>item.Read(this)).ToArray();
            result.ladders=GetComponentsInChildren<RoomLadderAuthoring>(true).Where(item=>Included(item)).Select(item=>item.Read(this)).ToArray();
            if(result.platforms.Length==0 || result.left>=result.right) throw new InvalidOperationException($"Комната {name}: некорректные границы или нет поверхностей.");
            return result;
        }
        public bool Included(Behaviour component)
        {
            if(!component.enabled) return false;
            for(var parent=component.transform;parent!=transform;parent=parent.parent)
                if(!parent.gameObject.activeSelf) return false;
            return true;
        }
        public static RoomAuthoring Load(string level)
        {
            return Load(level,null);
        }
        public static RoomAuthoring Load(string level,WorldState world)=>RoomCatalogAsset.Load().Resolve(WorldRooms.Definition(world,level));
        public static Rect ReadRect(Transform part,Transform root)
        {
            var matrix=root.worldToLocalMatrix*part.localToWorldMatrix;
            var origin=matrix.MultiplyPoint3x4(Vector3.zero);var x=matrix.MultiplyVector(Vector3.right);var y=matrix.MultiplyVector(Vector3.up);
            if(!float.IsFinite(origin.x) || !float.IsFinite(origin.y) || !float.IsFinite(x.x) || !float.IsFinite(y.y) ||
                x.x<=0 || y.y<=0 || Mathf.Abs(x.y)+Mathf.Abs(x.z)+Mathf.Abs(y.x)+Mathf.Abs(y.z)+Mathf.Abs(origin.z)>.00001f)
                throw new InvalidOperationException($"Геометрия {part.name}: нужны положительный масштаб, Z=0 и оси без поворота.");
            return new Rect(origin.x,origin.y,x.x,y.y);
        }
    }
}
