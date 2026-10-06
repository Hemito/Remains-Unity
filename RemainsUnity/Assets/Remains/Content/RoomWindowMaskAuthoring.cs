using System;
using UnityEngine;

namespace Remains.Content
{
    [Serializable]
    public sealed class WindowMaskRegion
    {
        [InspectorName("Исходная запись")] public string sourceRecord;
        [InspectorName("Рисунок окна")] public RoomArtworkAuthoring artwork;
        [InspectorName("Исходная маска покрытия")] public Texture2D coverage;
        [HideInInspector] public Rect bakedBounds;
    }

    // Saved rendering data. Rebuilt only by an explicit editor command, never by Update.
    public sealed class RoomWindowMaskAuthoring : MonoBehaviour
    {
        [SerializeField,InspectorName("Сохранённая маска видимости")] private Texture2D visibility;
        [SerializeField,InspectorName("Границы маски в комнате")] private Rect bounds;
        [SerializeField,InspectorName("Окна и исходные маски")] private WindowMaskRegion[] regions;
        [SerializeField,InspectorName("Слои с вырезанием")] private MeshRenderer[] layers;
        public Texture2D Visibility=>visibility;
        public Rect Bounds=>bounds;
        public WindowMaskRegion[] Regions=>regions;
        public MeshRenderer[] Layers=>layers;
        public void Configure(Texture2D texture,Rect area,WindowMaskRegion[] windows,MeshRenderer[] targets)
        {visibility=texture;bounds=area;regions=windows;layers=targets;}
        public void Validate()
        {
            var room=GetComponent<RoomAuthoring>();
            if(room==null || visibility==null || bounds.width<=0 || bounds.height<=0 || regions==null || regions.Length==0 || layers==null || layers.Length==0)
                throw new InvalidOperationException("Маска окон: отсутствуют авторские данные, room="+name);
            foreach(var region in regions)
            {
                if(region?.artwork==null || region.coverage==null || string.IsNullOrWhiteSpace(region.sourceRecord))
                    throw new InvalidOperationException("Маска окон: неполная запись, room="+name);
                var artwork=region.artwork.Read(room);var current=new Rect(artwork.x,artwork.y,artwork.width,artwork.height);
                if(Vector4.Distance(new Vector4(current.x,current.y,current.width,current.height),new Vector4(region.bakedBounds.x,region.bakedBounds.y,region.bakedBounds.width,region.bakedBounds.height))>.0001f)
                    throw new InvalidOperationException("Маска окон устарела: перемещён рисунок, record="+region.sourceRecord+". Выполните «Remains/Завод/Пересобрать маски окон офиса».");
            }
            foreach(var renderer in layers)
            {
                var mesh=renderer==null?null:renderer.GetComponent<MeshFilter>()?.sharedMesh;
                var material=renderer==null?null:renderer.sharedMaterial;
                if(mesh==null || material==null || !material.HasProperty("_ArtworkVisibilityEnabled") || material.GetFloat("_ArtworkVisibilityEnabled")!=1 || material.GetTexture("_ArtworkVisibility")!=visibility)
                    throw new InvalidOperationException("Маска окон: потерян material/mesh binding, room="+name+", layer="+renderer?.name);
                var vertices=mesh.vertices;var uv=mesh.uv2;var matrix=room.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
                if(uv.Length!=vertices.Length) throw new InvalidOperationException("Маска окон: отсутствует второй UV, layer="+renderer.name);
                for(int i=0;i<vertices.Length;i++)
                {
                    var position=matrix.MultiplyPoint3x4(vertices[i]);var expected=new Vector2((position.x-bounds.x)/bounds.width,(position.y-bounds.y)/bounds.height);
                    if(Vector2.Distance(uv[i],expected)>.00001f)
                        throw new InvalidOperationException("Маска окон устарела: перемещён слой, layer="+renderer.name+". Выполните пересборку масок.");
                }
            }
        }
    }
}
