using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    public static class PonyBedPoseChecks
    {
        private static string Output=>Path.GetFullPath("../.reference/bed-game-20261102");
        public static void RunBatch()
        {
            try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            Directory.CreateDirectory(Output);ActorTypes.Load();WeaponCatalog.Load();Anatomy.Load();PonyVisual.Prepare();
            PonyAuthoredWardrobeChecks.Run();
            var input=JObject.Parse(File.ReadAllText("Assets/Remains/Editor/PonyAnimation/Authored/Published/bed-back-confirmed.json"));
            var expected=input["frames"][0]["nodes"].ToDictionary(n=>(string)n["socket"],n=>n,StringComparer.Ordinal);
            string runtimeJson=GameAssets.Load<TextAsset>(PonyAuthoredClips.Resource).text;
            Require(!runtimeJson.Contains("donor",StringComparison.Ordinal) && !runtimeJson.Contains("baseSourceHash",StringComparison.Ordinal),"в игровом банке нет рецепта исходных кадров");
            float matrixError=0;int verified=0;
            foreach(string outfit in new[]{"","armor.antirad","armor.battle"})
            {
                var art=PonyAnimationArtwork.Prepare(outfit,"bedBack",0);
                foreach(var node in art.Frame.Nodes)
                {
                    var source=expected[node.socket];var m=node.transform.Matrix;float[] got={m.m00,m.m10,m.m01,m.m11,m.m03,m.m13};
                    for(int k=0;k<6;k++)matrixError=Math.Max(matrixError,Math.Abs(got[k]-(float)source["matrix"][k]));
                    foreach(var part in art.Parts.Where((p,i)=>art.Frame.PartNodes[i]==Array.IndexOf(art.Frame.Nodes,node)))
                        Require(part.symbolId==(string)source["symbol"],$"сохранён рисунок {outfit}/{node.socket}: {part.symbolId}");
                    verified++;
                }
            }
            Require(matrixError<.00001f,"точность готовых узлов: "+matrixError);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var catalog=FactoryCatalogAsset.Load();var template=catalog.Templates.Values.OrderBy(t=>t.Template,StringComparer.Ordinal).First(t=>t.Objects.Any(o=>o.Type==FactoryRestAdapter.BedType));
            var room=Object.Instantiate(catalog.Prefab(template.Template,false));
            var marker=room.GetComponentsInChildren<RoomTemplateObjectPoint>().First(p=>p.Type==FactoryRestAdapter.BedType);
            var bed=new RoomRest {id="bed-exact-check",x=marker.transform.position.x-marker.Width*room.Cell*.5f,y=marker.transform.position.y,width=marker.Width*room.Cell,height=.56f};
            var actor=new ActorState {id="bed-exact-pony",player=true,connected=true,anatomy="pony",actorType="pony",weapon=null,
                appearance=new CharacterAppearance {version=CharacterAppearance.CurrentVersion},x=bed.x+bed.width*.5f,y=bed.y};
            BedRest.LieDown(actor,bed,BedRest.Back);
            var visual=(PonyVisual)ActorVisualFactory.Create(actor);visual.ConfigureMagicPreview(0);visual.Bind(actor);visual.DrawPose(actor.poseTime);
            var camera=new GameObject("Точная поза на кровати").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=1.45f;
            camera.transform.position=new Vector3(actor.x,bed.y+.95f,-10);camera.backgroundColor=new Color(.2f,.23f,.26f);
            try
            {
                Capture("warmup.png");Capture("bed-back-bare.png");
                visual.TryGetDrawnBounds(out var bareBounds);
                foreach(string outfit in new[]{"armor.antirad","armor.battle"})
                {
                    var item=SharedInventory.Create(outfit,1);InventoryStacks.Add(actor,item);
                    Require(CosmeticEquipment.TryEquip(actor,item.id,false,out var reason),outfit+": "+reason);
                    visual.Bind(actor);visual.DrawPose(actor.poseTime);Capture("bed-back-"+outfit.Substring(6)+".png");
                }
                for(int i=0;i<295;i++)visual.AdvanceNestedAnimation(1f/30);visual.DrawPose(actor.poseTime);
                Require(visual.DrawnExpression==PonyFacialExpression.Closed,"моргание сохраняет позу");Capture("bed-back-blink.png");
                Require(visual.DrawnIdle==null && actor.BodyPosition==actor.Position,"кровать сохраняет якорь и не получает idle");
                var report=new JObject { ["pass"]=true,["input"]="bed-back (3).json",["verifiedNodes"]=verified,["maxMatrixError"]=matrixError,
                    ["runtimeIndependent"]=true,["template"]=template.Template,["mattressY"]=bed.y+bed.height,["actorY"]=actor.y,
                    ["drawnBoundsMinY"]=bareBounds.min.y,["note"]="Готовая поза без смены формы; y=0 JSON совмещён с высотой лежанки. Голова может выступать ниже линии." };
                File.WriteAllText(Path.Combine(Output,"report.json"),report.ToString());Debug.Log("Точная поза на кровати: PASS — "+report.ToString(Newtonsoft.Json.Formatting.None));
            }
            finally{Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(visual.gameObject);Object.DestroyImmediate(room.gameObject);}
            void Capture(string file)=>Runtime.NetworkFrameCapture.Save(camera,Path.Combine(Output,file),width:1280,height:800);
        }
        private static void Require(bool condition,string text){if(!condition)throw new InvalidOperationException("Точная поза пони: "+text);}
    }
}
