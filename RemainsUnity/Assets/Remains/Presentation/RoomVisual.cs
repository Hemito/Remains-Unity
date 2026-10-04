using System;
using System.Collections.Generic;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    public static class RoomVisual
    {
        public static GameObject Build(RoomLayout room,RoomLightProfile lightingProfile=null)
        {
            var parent=new GameObject("Комната: "+room.id).transform;
            parent.gameObject.AddComponent<RoomMeshLifetime>();
            var lighting=parent.gameObject.AddComponent<RoomLighting>();
            if(lightingProfile==null) lighting.Initialize(room);else lighting.Initialize(room,lightingProfile);
            foreach(var light in lighting.Profile.lights)
                if(!string.IsNullOrEmpty(light.fixture)) Quad(parent,"Светильник: "+light.id,light.x-light.width/2,light.y,light.width,light.height,light.fixture,2,false,1,true);
            if(room.artwork!=null)
                foreach(var layer in room.artwork)
                    Quad(parent,"Фон: "+layer.texture,layer.x,layer.y,layer.width,layer.height,layer.texture,layer.order,layer.repeat,layer.shade,true,layer.stretchY,layer.selfIlluminated,backgroundLighting:layer.backgroundLighting,slopeDirection:layer.slopeDirection).AddComponent<Remains.Content.RoomArtworkAuthoring>().Configure(layer);
            else
            {
                Quad(parent,"Задняя стена",room.left,0,room.right-room.left,7,"wall",-10);
                foreach(var platform in room.platforms)
                    Quad(parent,"Платформа",platform.x,platform.y,platform.width,platform.height,platform.texture,0);
            }
            foreach(var ladder in room.ladders)
            {
                if(!string.IsNullOrEmpty(ladder.artwork))
                {
                    Quad(parent,"Пристенная лестница",ladder.x+ladder.artworkOffset,ladder.bottom,1f,ladder.top-ladder.bottom,
                        ladder.artwork,1,true,1,true,false,false,100,true);
                    continue;
                }
                float imageX=ladder.x+ladder.artworkOffset;
                Quad(parent,"Левая стойка",imageX-0.25f,ladder.bottom,0.06f,ladder.top-ladder.bottom,ladder.texture,1,true,1,ladder.nativeScale);
                Quad(parent,"Правая стойка",imageX+0.2f,ladder.bottom,0.06f,ladder.top-ladder.bottom,ladder.texture,1,true,1,ladder.nativeScale);
                for(float y=ladder.bottom;y<ladder.top;y+=0.3f) Quad(parent,"Перекладина",imageX-0.25f,y,0.5f,0.06f,ladder.texture,1,true,1,ladder.nativeScale);
            }
            return parent.gameObject;
        }
        private static GameObject Quad(Transform parent,string name,float x,float y,float width,float height,string key,int order,bool repeat=true,float shade=1,bool nativeScale=false,bool stretchY=false,bool selfIlluminated=false,float pixelsPerUnit=50,bool localUv=false,bool backgroundLighting=false,int slopeDirection=0)
        {
            var texture=Resources.Load<Texture2D>("Original/Room/"+key);
            if(texture==null) throw new InvalidOperationException($"Отсутствует текстура комнаты: {key}, объект={name}.");
            texture.wrapMode=TextureWrapMode.Repeat;
            var obj=new GameObject(name); obj.transform.SetParent(parent,false); obj.transform.position=new Vector3(x,y,0);
            float u=repeat?(nativeScale?width*pixelsPerUnit/texture.width:width):1;
            float v=repeat && !stretchY?(nativeScale?height*pixelsPerUnit/texture.height:height):1;
            var offset=repeat && nativeScale && !localUv?new Vector2(x*pixelsPerUnit/texture.width,stretchY?0:y*pixelsPerUnit/texture.height):Vector2.zero;
            var mesh=new Mesh { vertices=new[]{Vector3.zero,new Vector3(width,0,0),new Vector3(0,height,0),new Vector3(width,height,0)},
                uv=new[]{offset,offset+new Vector2(u,0),offset+new Vector2(0,v),offset+new Vector2(u,v)},triangles=new[]{0,2,1,2,3,1},
                colors=new[]{new Color(shade,shade,shade),new Color(shade,shade,shade),new Color(shade,shade,shade),new Color(shade,shade,shade)} };
            if(slopeDirection!=0)
            {
                // Clip a diagonal beam to its authored cell. Its upper edge is the physical support.
                float thickness=Mathf.Min(.12f,height),inset=thickness*width/height;
                var vertices=new[]{Vector3.zero,new Vector3(inset,0,0),new Vector3(width,height,0),new Vector3(width,height-thickness,0)};
                if(slopeDirection<0) for(int i=0;i<vertices.Length;i++) vertices[i].x=width-vertices[i].x;
                mesh.vertices=vertices;
                mesh.triangles=slopeDirection>0?new[]{0,2,1,2,3,1}:new[]{0,1,2,2,1,3};
                float length=Mathf.Sqrt(width*width+height*height);
                mesh.uv=new[]{Vector2.zero,new Vector2(inset/length,0),new Vector2(length*pixelsPerUnit/texture.width,1),new Vector2(length*pixelsPerUnit/texture.width,0)};
                mesh.RecalculateBounds();
            }
            parent.GetComponent<RoomMeshLifetime>().Meshes.Add(mesh);
            obj.AddComponent<MeshFilter>().sharedMesh=mesh;
            var materials=parent.GetComponent<RoomMeshLifetime>().Materials;
            if(!materials.TryGetValue(key,out var material))
            {material=WorldArtworkMaterial.Create(texture);material.name="RoomMaterial:"+parent.name+":"+key;materials.Add(key,material);}
            var renderer=obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material;
            renderer.sortingOrder=order;
            if(selfIlluminated || backgroundLighting)
            {
                var properties=new MaterialPropertyBlock();properties.SetFloat("_Emission",selfIlluminated?1:0);
                properties.SetFloat("_BackgroundLighting",backgroundLighting?1:0);renderer.SetPropertyBlock(properties);
            }
            return obj;
        }
    }
}
