using System;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class SurfaceSoundChecks
    {
        public static void ConfigureFactory()
        {
            const string path="Assets/Remains/RoomDrafts/FactoryEntry46.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var room=root.GetComponent<RoomAuthoring>();
                foreach(var surface in root.GetComponentsInChildren<RoomSurfaceAuthoring>(true))
                {
                    string bank=surface.Read(room).texture switch {
                        "Factory46/16_tMetal" or "Factory46/36_tShelf"=>"metal",
                        "Factory46/26_tConcrete" or "Factory46/41_tRock"=>"normal",
                        var key=>throw new InvalidOperationException("Не исследован материал опоры: "+key)};
                    surface.SetMovementSound(bank);
                }
                PrefabUtility.SaveAsPrefabAsset(root,path);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            ContractChecks.Run();
        }
        public static void Run()
        {
            var concrete=new RoomSurface {x=0,y=0,width=2,height=1,movementSound="normal"};
            var metal=new RoomSurface {x=2,y=0,width=2,height=1,movementSound="metal",oneWay=true};
            var room=new RoomLayout {movementSound="normal",platforms=new[]{concrete,metal}};
            var hull=new MovementHull {width=1,height=1};
            void Check(float x,float y,string expected)
            {
                if(SurfaceContactSound.Resolve(room,hull,new Vector2(x,y))!=expected)
                    throw new InvalidOperationException($"Неверный звук опоры: x={x}, y={y}, ожидался {expected}.");
            }
            Check(1,1,"normal");Check(3,1,"metal");Check(2.1f,1,"metal");Check(2,1,"normal");Check(3,2,null);
            room.platforms=new[]{metal,concrete};Check(2,1,"normal");
            metal.movementSound=null;Check(3,1,"normal");
            Debug.Log("Звуки опор: PASS — бетон, металл, односторонняя платформа, граница материалов, отсутствие опоры и явное наследование комнаты.");
        }
    }
}
