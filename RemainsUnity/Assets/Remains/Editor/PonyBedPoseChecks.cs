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
        public static void RunBellyBatch()
        {
            try{Run(BedRest.Belly);EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void RunBellyPillowBatch()
        {
            try{Run(BedRest.Belly,-1);EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run(string pose=BedRest.Back,int facing=1)
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
                appearance=new CharacterAppearance {version=CharacterAppearance.CurrentVersion},x=bed.x+bed.width*.5f,y=bed.y,facing=facing};
            BedRest.LieDown(actor,bed,pose);
            var visual=(PonyVisual)ActorVisualFactory.Create(actor);visual.ConfigureMagicPreview(0);visual.Bind(actor);visual.DrawPose(actor.poseTime);
            var camera=new GameObject("Точная поза на кровати").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=1.45f;
            camera.transform.position=new Vector3(actor.x,bed.y+.95f,-10);camera.backgroundColor=new Color(.2f,.23f,.26f);
            try
            {
                string prefix=pose==BedRest.Back?"bed-back":"bed-belly";
                if(facing<0)prefix+="-left";
                Require(visual.DrawnClip==BedRest.Clip(actor),"выбран клип позы на кровати: "+pose);
                Capture(prefix+"-warmup.png");Capture(prefix+"-bare.png");
                visual.TryGetDrawnBounds(out var bareBounds);
                foreach(string outfit in new[]{"armor.antirad","armor.battle"})
                {
                    var item=SharedInventory.Create(outfit,1);InventoryStacks.Add(actor,item);
                    Require(CosmeticEquipment.TryEquip(actor,item.id,false,out var reason),outfit+": "+reason);
                    visual.Bind(actor);visual.DrawPose(actor.poseTime);Capture(prefix+"-"+outfit.Substring(6)+".png");
                }
                for(int i=0;i<295;i++)visual.AdvanceNestedAnimation(1f/30);visual.DrawPose(actor.poseTime);
                Require(visual.DrawnExpression==PonyFacialExpression.Closed,"моргание сохраняет позу");Capture(prefix+"-blink.png");
                Require(visual.DrawnIdle==null && Mathf.Abs(actor.y-bed.y-bed.height)<.00001f,"кровать сохраняет якорь и не получает idle");
                if(pose==BedRest.Back)Require(actor.BodyPosition==actor.Position,"на спине нет опоры копыт");
                var report=new JObject { ["pass"]=true,["verifiedAuthoredInput"]="bed-back (3).json",["verifiedAuthoredNodes"]=verified,["maxMatrixError"]=matrixError,
                    ["authoredBankIndependent"]=true,["pose"]=pose,["facing"]=facing,["drawnClip"]=visual.DrawnClip,["template"]=template.Template,["mattressY"]=bed.y+bed.height,["actorY"]=actor.y,
                    ["drawnBoundsMinY"]=bareBounds.min.y,["note"]=pose==BedRest.Back?
                        "Готовая поза без смены формы; y=0 JSON совмещён с высотой лежанки. Голова может выступать ниже линии.":
                        "Исходная поза down, последний кадр; штатная опора копыт сохранена." };
                string reportFile=facing<0?"report-"+pose+"-left.json":pose==BedRest.Back?"report.json":"report-belly.json";
                File.WriteAllText(Path.Combine(Output,reportFile),report.ToString());Debug.Log("Точная поза на кровати: PASS — "+report.ToString(Newtonsoft.Json.Formatting.None));
            }
            finally{Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(visual.gameObject);Object.DestroyImmediate(room.gameObject);}
            void Capture(string file)=>Runtime.NetworkFrameCapture.Save(camera,Path.Combine(Output,file),width:1280,height:800);
        }
        private static void Require(bool condition,string text){if(!condition)throw new InvalidOperationException("Точная поза пони: "+text);}
    }
}
