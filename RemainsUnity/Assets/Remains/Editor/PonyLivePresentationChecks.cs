using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    public static class PonyLivePresentationChecks
    {
        public static string Output=>Path.GetFullPath("../.reference/pony-live-20261102");
        public static void BuildPlayerBatch()
        {
            try
            {
                string destination="Builds/PonyPresentation/Remains.exe";
                var scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray();
                if(scenes.Length==0)throw new InvalidOperationException("Проверка пони: нет сцен для сборки.");
                var report=BuildPipeline.BuildPlayer(scenes,destination,BuildTarget.StandaloneWindows64,BuildOptions.Development|BuildOptions.DetailedBuildReport);
                if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)
                    throw new InvalidOperationException("Сборка представления пони: "+report.summary.result);
                File.WriteAllText(Path.Combine(Output,"player-build.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new {result=report.summary.result.ToString(),path=Path.GetFullPath(destination),bytes=report.summary.totalSize},Newtonsoft.Json.Formatting.Indented));
                Debug.Log("Сборка представления пони: "+Path.GetFullPath(destination));EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void PrepareBatch()
        {
            try
            {
                Directory.CreateDirectory(Output);PonyFacialAuthoring.Build();PonyAmbientMotionAuthoring.Build();
                PonyRenderSelection.ResetPrepared();Verify();AssetDatabase.SaveAssets();EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Verify()
        {
            ActorTypes.Load();WeaponCatalog.Load();Anatomy.Load();PonyVisual.Prepare();
            BedRestChecks.Run();PonyAmbientClockChecks.Run();PonyNestedEyeAuthoring.CheckClock();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var catalog=FactoryCatalogAsset.Load();var source=catalog.Templates.Values.OrderBy(t=>t.Template,StringComparer.Ordinal).First(t=>t.Objects.Any(o=>o.Type==FactoryRestAdapter.BedType));
            var room=Object.Instantiate(catalog.Prefab(source.Template,false));
            var point=room.GetComponentsInChildren<RoomTemplateObjectPoint>().First(p=>p.Type==FactoryRestAdapter.BedType);
            var bed=new RoomRest {id="bed-check",x=point.transform.position.x-point.Width*room.Cell*.5f,y=point.transform.position.y,width=point.Width*room.Cell,height=.56f};
            var actor=new ActorState {id="pony-live-check",player=true,connected=true,health=100,anatomy="pony",actorType="pony",weapon=null,
                x=bed.x+bed.width*.5f,y=bed.y,appearance=new CharacterAppearance {version=CharacterAppearance.CurrentVersion}};
            var visual=(PonyVisual)ActorVisualFactory.Create(actor);visual.ConfigureMagicPreview(0);visual.Bind(actor);visual.DrawPose(0);
            var camera=new GameObject("Проверка кровати и мимики").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=1.6f;
            camera.transform.position=new Vector3(actor.x,bed.y+1.05f,-10);camera.backgroundColor=new Color(.2f,.23f,.26f);
            try
            {
                int children=visual.GetComponentsInChildren<Transform>(true).Length;
                BedRest.LieDown(actor,bed,BedRest.Back);visual.DrawPose(actor.poseTime);
                Require(visual.DrawnClip==BedRest.BackClip,"выбран игровой bedBack");
                Require(actor.BodyPosition==actor.Position && Mathf.Abs(actor.y-bed.y-bed.height)<.00001f,"на спине нет поправки опоры копыт");
                Require(visual.TryGetDrawnBounds(out var lying),"есть границы лежащего тела");
                Capture("warmup.png");Capture("bed-back-bare.png");
                float bodyAnchor=actor.y;
                for(int i=0;i<295;i++)visual.AdvanceNestedAnimation(1f/30);
                visual.DrawPose(actor.poseTime);Require(visual.DrawnExpression==PonyFacialExpression.Closed,"на кровати работает моргание");Capture("bed-back-blink.png");
                for(int i=0;i<5;i++)visual.AdvanceNestedAnimation(1f/30);
                actor.attackBusy=true;visual.DrawPose(actor.poseTime);Require(visual.DrawnExpression==PonyFacialExpression.Alert,"реакция на атаку");Capture("bed-back-alert.png");
                actor.attackBusy=false;actor.down=true;visual.DrawPose(actor.poseTime);Require(visual.DrawnExpression==PonyFacialExpression.Dead,"состояние без сознания");
                actor.down=false;visual.DrawPose(actor.poseTime);
                int meshes=Resources.FindObjectsOfTypeAll<Mesh>().Length;
                for(int i=0;i<120;i++){visual.AdvanceNestedAnimation(1f/30);visual.DrawPose(actor.poseTime);}
                Require(visual.GetComponentsInChildren<Transform>(true).Length==children && Resources.FindObjectsOfTypeAll<Mesh>().Length==meshes,"мимика не создаёт меши или объекты в кадре после прогрева камеры");
                var item=SharedInventory.Create("armor.antirad",1);InventoryStacks.Add(actor,item);
                Require(CosmeticEquipment.TryEquip(actor,item.id,false,out var reason),"антирад: "+reason);visual.Bind(actor);visual.DrawPose(actor.poseTime);Capture("bed-back-antirad.png");
                BedRest.GetUp(actor);actor.x=bed.x+bed.width*.5f;actor.y=bed.y+bed.height;
                visual.Bind(actor);visual.DrawPose(0);
                var seen=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);var activeFrames=new System.Collections.Generic.HashSet<string>();
                for(int tick=0;tick<6000;tick++)
                {
                    visual.AdvanceNestedAnimation(1f/30);visual.DrawPose(0);
                    if(visual.DrawnIdle!=null)
                    {
                        seen.Add(visual.DrawnIdle);activeFrames.Add(visual.DrawnIdle+":"+tick);
                        if(seen.Count<=3 && !File.Exists(Path.Combine(Output,"idle-"+visual.DrawnIdle+".png")) && tick%50==25)Capture("idle-"+visual.DrawnIdle+".png");
                    }
                    if(seen.Count==3 && activeFrames.Count>170)break;
                }
                Require(seen.Count==3,"игровой рендер выбирает все три idle");
                actor.pose="walk";actor.vx=1;visual.AdvanceNestedAnimation(0);visual.DrawPose(0);Require(visual.DrawnIdle==null,"движение немедленно отменяет idle");
                actor.pose="idle";actor.vx=0;BedRest.LieDown(actor,bed,BedRest.Back);visual.AdvanceNestedAnimation(0);visual.DrawPose(actor.poseTime);Require(visual.DrawnIdle==null,"idle не подменяет авторскую позу кровати");
                Require(Mathf.Abs(actor.y-bodyAnchor)<.00001f,"мимика и idle не двигают игровой якорь кровати");
                File.WriteAllText(Path.Combine(Output,"report.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new {status="PASS",template=source.Template,bed=new {bed.x,bed.y,bed.width,bed.height},actorY=actor.y,
                    lyingMinY=lying.min.y,lyingMaxY=lying.max.y,idles=seen.ToArray(),meshGenerationInAnimation=false},Newtonsoft.Json.Formatting.Indented));
                Debug.Log($"Пони, игровой рендер: PASS — кровать {source.Template}/{point.Id}, mattress={bed.y+bed.height:G6}, drawnMin={lying.min.y:G6}, все три idle, четыре выражения, моргание на спине, антирад и отмена idle при движении/кровати; {Output}.");
            }
            finally {Object.DestroyImmediate(visual.gameObject);Object.DestroyImmediate(room.gameObject);Object.DestroyImmediate(camera.gameObject);}
            void Capture(string file)=>Runtime.NetworkFrameCapture.Save(camera,Path.Combine(Output,file),width:1280,height:800);
        }
        private static void Require(bool condition,string reason){if(!condition)throw new InvalidOperationException("Пони, живое представление: "+reason);}
    }
}
