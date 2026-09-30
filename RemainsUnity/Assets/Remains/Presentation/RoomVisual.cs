using System;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{
    public static class RoomVisual
    {
        public static void Build(RoomLayout room)
        {
            var parent=new GameObject("Комната: "+room.id).transform;
            Quad(parent,"Задняя стена",room.left,0,room.right-room.left,7,"wall",-10);
            foreach(var platform in room.platforms)
                Quad(parent,"Платформа",platform.x,platform.y,platform.width,platform.height,platform.texture,0);
            foreach(var ladder in room.ladders)
            {
                Quad(parent,"Левая стойка",ladder.x-0.25f,ladder.bottom,0.06f,ladder.top-ladder.bottom,"floor",1);
                Quad(parent,"Правая стойка",ladder.x+0.2f,ladder.bottom,0.06f,ladder.top-ladder.bottom,"floor",1);
                for(float y=ladder.bottom;y<ladder.top;y+=0.3f) Quad(parent,"Перекладина",ladder.x-0.25f,y,0.5f,0.06f,"floor",1);
            }
        }
        private static void Quad(Transform parent,string name,float x,float y,float width,float height,string key,int order)
        {
            var texture=Resources.Load<Texture2D>("Original/Room/"+key);
            if(texture==null) throw new InvalidOperationException($"Отсутствует текстура комнаты: {key}, объект={name}.");
            texture.wrapMode=TextureWrapMode.Repeat;
            var obj=new GameObject(name); obj.transform.SetParent(parent,false); obj.transform.position=new Vector3(x,y,0);
            var mesh=new Mesh { vertices=new[]{Vector3.zero,new Vector3(width,0,0),new Vector3(0,height,0),new Vector3(width,height,0)},
                uv=new[]{Vector2.zero,new Vector2(width,0),new Vector2(0,height),new Vector2(width,height)},triangles=new[]{0,2,1,2,3,1} };
            obj.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial=new Material(Shader.Find("Sprites/Default")){mainTexture=texture};
            renderer.sortingOrder=order;
        }
    }
}
