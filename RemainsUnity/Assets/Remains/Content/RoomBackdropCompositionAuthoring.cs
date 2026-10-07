using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Content
{
    [Serializable] public sealed class BackdropCompositionLayer
    {
        public string sourceRecord,operation;
        public RoomArtworkAuthoring artwork;
        public Texture2D diffuse;
        public int maskFrame;
        public Rect bakedBounds;
    }
    // Saved provenance and binding guard, not a runtime compositor or physics source.
    public sealed class RoomBackdropCompositionAuthoring : MonoBehaviour
    {
        [SerializeField] private int schema=1;
        [SerializeField] private string sourceId;
        [SerializeField] private BackdropCompositionLayer[] layers;
        public string SourceId=>sourceId;
        public BackdropCompositionLayer[] Layers=>layers;
        public void Configure(string source,BackdropCompositionLayer[] value) {sourceId=source;layers=value;}
        public void Validate()
        {
            var room=GetComponent<RoomAuthoring>();var mask=GetComponent<RoomWindowMaskAuthoring>();
            if(schema!=1 || room==null || mask==null || string.IsNullOrWhiteSpace(sourceId) || layers==null || layers.Length==0)
                throw new InvalidOperationException("Композиция фона: неполные сохранённые данные, room="+name);
            mask.Validate();
            for(int i=0;i<layers.Length;i++)
            {
                var layer=layers[i];
                if(layer?.artwork==null || layer.diffuse==null || string.IsNullOrWhiteSpace(layer.sourceRecord))
                    throw new InvalidOperationException("Композиция фона: неполная запись, source="+sourceId+", index="+i);
                for(int prior=0;prior<i;prior++) if(layers[prior].sourceRecord==layer.sourceRecord)
                    throw new InvalidOperationException("Композиция фона: повтор source record="+layer.sourceRecord);
                RoomArtwork current=layer.artwork.Read(room);var r=new Rect(current.x,current.y,current.width,current.height);
                if(Vector4.Distance(new Vector4(r.x,r.y,r.width,r.height),new Vector4(layer.bakedBounds.x,layer.bakedBounds.y,layer.bakedBounds.width,layer.bakedBounds.height))>.0001f)
                    throw new InvalidOperationException("Композиция фона устарела: перемещён рисунок, record="+layer.sourceRecord);
                var renderer=layer.artwork.GetComponent<MeshRenderer>();var material=renderer.sharedMaterial;
                if(material.mainTexture!=layer.diffuse || !current.backgroundLighting || current.repeat || current.selfIlluminated)
                    throw new InvalidOperationException("Композиция фона: изменены resource/lighting/sampler, record="+layer.sourceRecord);
                if(layer.operation=="multiply")
                {
                    if(current.order!=-20 || layer.maskFrame!=0 || material.shader.name!="Remains/BackdropMultiply")
                        throw new InvalidOperationException("Композиция фона: потерян multiply material/depth, record="+layer.sourceRecord);
                }
                else if(layer.operation=="erase")
                {
                    if(current.order!=-30 || layer.maskFrame<=0 || material.shader.name!="Remains/WorldLit")
                        throw new InvalidOperationException("Композиция фона: потерян diffuse erasure/depth, record="+layer.sourceRecord);
                    bool found=false;
                    foreach(var region in mask.Regions) if(region.sourceRecord==layer.sourceRecord && region.artwork==layer.artwork) found=true;
                    bool bound=false;foreach(var target in mask.Layers) if(target==renderer)bound=true;
                    if(!found || !bound) throw new InvalidOperationException("Композиция фона: нет единственной atlas region/layer, record="+layer.sourceRecord);
                }
                else throw new InvalidOperationException("Композиция фона: неизвестная операция, record="+layer.sourceRecord+", operation="+layer.operation);
            }
        }
    }
}
