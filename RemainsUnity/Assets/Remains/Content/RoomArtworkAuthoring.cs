using System;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Content
{
    public sealed class RoomArtworkAuthoring : MonoBehaviour
    {
        [SerializeField,HideInInspector] private string textureKey;
        [SerializeField,HideInInspector] private bool repeat,stretchY;
        [SerializeField,HideInInspector] private int slopeDirection;
        public void Configure(RoomArtwork source) {textureKey=source.texture;repeat=source.repeat;stretchY=source.stretchY;slopeDirection=source.slopeDirection;}
        public RoomArtwork Read(RoomAuthoring root)
        {
            var filter=GetComponent<MeshFilter>();var renderer=GetComponent<MeshRenderer>();
            if(filter==null || filter.sharedMesh==null || renderer==null || renderer.sharedMaterial==null)
                throw new InvalidOperationException($"Слой {name}: отсутствует геометрия или материал.");
            var matrix=root.transform.worldToLocalMatrix*transform.localToWorldMatrix;var bounds=filter.sharedMesh.bounds;
            var min=matrix.MultiplyPoint3x4(bounds.min);var max=matrix.MultiplyPoint3x4(bounds.max);
            var colors=filter.sharedMesh.colors;
            return new RoomArtwork {texture=textureKey,x=min.x,y=min.y,width=max.x-min.x,height=max.y-min.y,
                order=renderer.sortingOrder,slopeDirection=slopeDirection,shade=colors.Length>0?colors[0].r:1,repeat=repeat,stretchY=stretchY,
                selfIlluminated=WorldArtworkMaterial.ReadFloat(renderer,"_Emission")>0,
                backgroundLighting=WorldArtworkMaterial.ReadFloat(renderer,"_BackgroundLighting")>0};
        }
    }
}
