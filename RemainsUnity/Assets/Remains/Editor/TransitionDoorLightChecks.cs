using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Presentation;
using Remains.RoomFormat;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // PT-20261103-17: transition artwork lives on the back wall. Preserve original
    // wall=2 metadata; the authored lighting override applies to every scene consumer.
    public static class TransitionDoorLightChecks
    {
        private const string Original="Assets/Remains/Rooms/Original/";
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            var definitions=RoomDefinitionCatalog.Read(Text("definitions.json"),"проверка переходных дверей");
            var table=RoomLightingTable.Read(Text("room-lighting.json"),"проверка света переходных дверей");
            var art=AssetDatabase.LoadAssetAtPath<RoomTileArtAsset>(Original+"Tiles/TileArt.asset")??
                throw new InvalidOperationException("Нет авторского каталога рисунков для проверки переходных дверей.");
            for(int i=1;i<=4;i++)
            {
                string id="remains:obj:indoor"+i;
                var definition=definitions.Require(id,"проверка переходных дверей");
                Require(definition.Wall==2 && definition.ObjectType=="box",$"Изменены исходные метаданные {id} вместо настройки света.");
                foreach(double area in new[]{.1,1,100})
                    Require(table.ObjectTransmittance(definition,area)==1,$"Переходная дверь {id} поглощает свет при площади {area}.");
                var sprite=art.Tile(TileManifest.ObjectFrameId(definition.Art,1)).sprite;
                Require(sprite!=null,$"Отсутствует настоящий рисунок {id}.");
                foreach(bool mirror in new[]{false,true})
                {
                    var pixels=Raster(sprite,1-table.ObjectTransmittance(definition,4),mirror);
                    for(int p=1;p<pixels.Length;p++)
                        Require(pixels[p].r==0,$"Переходная дверь {id}, зеркало={mirror}: поглощение={pixels[p].r} в текселе {p}.");
                    Require(pixels[0].r==1,$"Переходная дверь {id} стёрла каноническую стену.");
                }
            }
            foreach(string id in new[]{"door1","door2","door3","door4"})
            {
                var definition=definitions.Require("remains:obj:"+id,"проверка обычных дверей");
                Require(definition.ObjectType=="door" && table.ObjectTransmittance(definition,100)==0,
                    $"Обычная закрытая дверь {id} перестала поглощать свет.");
            }
            var ordinary=definitions.Require("remains:obj:door1","проверка обычной двери");
            var doorSprite=art.Tile(TileManifest.ObjectFrameId(ordinary.Art,1)).sprite;
            var closed=Raster(doorSprite,1-table.ObjectTransmittance(ordinary,4),false);
            float maximum=0;for(int p=1;p<closed.Length;p++)maximum=Mathf.Max(maximum,closed[p].r);
            Require(maximum>.1f,$"Настоящий рисунок обычной закрытой двери не поглощает свет: максимум={maximum}.");
            Debug.Log("PT-20261103-17: PASS — indoor1..4 прозрачны для света на GPU, включая зеркало; исходный wall=2 сохранён, обычные двери и стены блокируют свет.");
        }
        private static Color[] Raster(Sprite sprite,double absorption,bool mirror)
        {
            var walls=new float[64,64];walls[0,0]=1;
            var matrix=Matrix4x4.TRS(new Vector3(3.2f,3.2f,0),Quaternion.identity,new Vector3(mirror?-1:1,1,1))*
                Matrix4x4.Translate(-sprite.bounds.center);
            using var scene=new LevelLightScene(walls,Vector2.zero,10,
                new[]{new LevelLightScene.ObjectSprite(sprite,matrix,absorption)});
            var previous=RenderTexture.active;Texture2D copy=null;
            try
            {
                RenderTexture.active=(RenderTexture)scene.Texture;
                copy=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
                copy.ReadPixels(new Rect(0,0,64,64),0,0);copy.Apply();
                return copy.GetPixels();
            }
            finally {RenderTexture.active=previous;if(copy!=null)UnityEngine.Object.DestroyImmediate(copy);}
        }
        private static string Text(string name)=>AssetDatabase.LoadAssetAtPath<TextAsset>(Original+name)?.text??
            throw new InvalidOperationException("Нет исходных данных "+Original+name);
        private static void Require(bool condition,string message) {if(!condition)throw new InvalidOperationException(message);}
    }
}
