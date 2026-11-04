using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Editor
{
    // Opt-in Play Mode observation: the real FactoryLevelView runs its normal per-frame queue.
    // No scene/prefab writes, network prediction, damage, or performance claims.
    [InitializeOnLoad]
    public static class LampExtinctionChecks
    {
        private const string Key="Remains.LampExtinctionCheck";
        private static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/lamp-extinction-20261104"));
        private static FactoryLevelView view;
        private static IDisposable catalog;
        private static GameObject visual,cameraRoot;
        private static Camera camera;
        private static SpriteRenderer picture;
        private static Sprite litSprite,flashSprite;
        private static Texture2D flashTexture;
        private static string lamp;
        private static Report report;
        private static int phase,lastFrame=-1,step,settled;
        private static Color[] before;
        private static SpriteEmission emission;
        private static readonly SpriteEmission[] NoFlashes=Array.Empty<SpriteEmission>();
        private static readonly ProjectedLightShadow[] NoMasks=Array.Empty<ProjectedLightShadow>();
        private static readonly string[] NoIds=Array.Empty<string>();
        [Serializable] private sealed class Report
        {
            public bool success;
            public string lamp,definition,detail;
            public List<Frame> frames=new();
        }
        [Serializable] private sealed class Frame
        {
            public string scenario,stage,sprite;
            public int step,unityFrame,changedPixels;
            public bool pending,artChanged;
            public long directRevision,fillRevision;
            public double radiance;
        }
        static LampExtinctionChecks(){if(SessionState.GetBool(Key,false))EditorApplication.update+=Watch;}
        public static void RunBatch()
        {
            Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);
            SessionState.SetFloat(Key+".deadline",(float)EditorApplication.timeSinceStartup+420);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            EditorApplication.update-=Watch;EditorApplication.update+=Watch;EditorApplication.EnterPlaymode();
        }
        private static void Watch()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>SessionState.GetFloat(Key+".deadline",0))throw new InvalidOperationException("Таймаут наблюдения выключения лампы.");
                if(!EditorApplication.isPlaying || Time.frameCount==lastFrame)return;
                lastFrame=Time.frameCount;
                if(view==null){Prepare();return;}
                if(phase==0 || phase==3)
                {
                    view.RenderMuzzleFlashes(NoFlashes,NoMasks,NoIds);before=Read();step=0;settled=0;
                    Observe("before",true);
                    if(phase==3){view.RenderMuzzleFlashes(new[]{emission},NoMasks,new[]{"diagnostic-flash"});Observe("flash",true);}
                    view.SyncLamps(new[]{lamp},phase==0?2:4,new[]{lamp});
                    view.RenderMuzzleFlashes(NoFlashes,NoMasks,NoIds);Observe("break",true);phase++;return;
                }
                if(phase==1 || phase==4)
                {
                    step++;view.RenderMuzzleFlashes(NoFlashes,NoMasks,NoIds);
                    Observe("queued",step<3 || !view.Light.HasPendingPublication && settled==0);
                    if(view.Light.HasWork){settled=0;return;}
                    if(++settled<3)return;
                    if(phase==4){Finish();return;}
                    view.SyncLamps(NoIds,3);phase=2;return;
                }
                if(phase==2 && !view.Light.HasWork)phase=3;
            }
            catch(Exception error){Debug.LogException(error);End(1);}
        }
        private static void Prepare()
        {
            catalog=RoomCatalogAsset.AcquireRuntime("LampExtinctionChecks");
            Anatomy.Load();WeaponCatalog.Load();var world=new WorldState{seed=104};
            FactoryPreparation.Create(world,AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath),0);
            FactoryContentSources.Prepare(world,"factory:0");var authoring=RoomAuthoring.Load("factory:0",world);
            DoorContentInstaller.Apply(world,RoomDoorContent.Read(authoring,"factory:0"));var room=authoring.CreateLayout(false);
            visual=AuthoredRoomVisual.Create("factory:0",room,world);view=visual.GetComponent<FactoryLevelView>();view.Synchronize(room,world,1);
            // Diagnostic inspection only; the ID and placement still come from the production catalog.
            var lamps=(Dictionary<string,(RoomSpriteAuthoring picture,int frame,Sprite sprite,Remains.RoomFormat.RoomDefinition definition)>)
                typeof(FactoryLevelView).GetField("lamps",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(view);
            var broken=GameAssets.Load<BrokenLampArt>(BrokenLampArt.ResourcePath);
            var selected=lamps.First(pair=>broken.TryGet(pair.Value.definition.Id,out _));lamp=selected.Key;
            picture=selected.Value.picture.GetComponent<SpriteRenderer>();litSprite=picture.sprite;
            var placement=view.LampPlacements.Single(value=>value.id==lamp);var center=placement.Bounds.center+Vector2.down*3;
            view.Focus(center);view.SetViewArea(new Rect(center-new Vector2(9,5.25f),new Vector2(18,10.5f)));
            cameraRoot=new GameObject("Editor: наблюдение разбитой лампы");camera=cameraRoot.AddComponent<Camera>();
            camera.enabled=false;camera.orthographic=true;camera.orthographicSize=5.25f;camera.aspect=960f/560;
            camera.transform.position=new Vector3(center.x,center.y,-10);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            flashTexture=new Texture2D(4,4,TextureFormat.RGBA32,false,true);flashTexture.SetPixels(Enumerable.Repeat(new Color(1,.7f,.2f),16).ToArray());flashTexture.Apply();
            flashSprite=Sprite.Create(flashTexture,new Rect(0,0,4,4),new Vector2(.5f,.5f),20);
            emission=new SpriteEmission(flashSprite,Matrix4x4.TRS(center,Quaternion.identity,Vector3.one),100);
            report=new Report{lamp=lamp,definition=selected.Value.definition.Id};
            Debug.Log($"Лампа Play Mode: {lamp}, {report.definition}, позиция={placement.Bounds.center}; режим={Application.isPlaying}, async очередь 2/2.");
        }
        private static void Observe(string stage,bool capture)
        {
            var pixels=Read();int changed=0;double energy=0;
            for(int i=0;i<pixels.Length;i++){if(!pixels[i].Equals(before[i]))changed++;energy+=pixels[i].r+pixels[i].g+pixels[i].b;}
            var sample=new Frame{scenario=phase<3?"no-flash":"flash",stage=stage,step=step,unityFrame=Time.frameCount,
                sprite=picture.sprite.name,artChanged=picture.sprite!=litSprite,pending=view.Light.HasPendingPublication,
                directRevision=view.Light.Direct.Revision,fillRevision=view.Light.Fill.Revision,changedPixels=changed,radiance=energy};
            report.frames.Add(sample);
            if(capture)Remains.Runtime.NetworkFrameCapture.Save(camera,Path.Combine(Output,$"{sample.scenario}-{stage}-{step:D2}.png"),width:960,height:560);
            File.WriteAllText(Path.Combine(Output,"report.json"),JsonUtility.ToJson(report,true));
        }
        private static Color[] Read()
        {
            var request=AsyncGPUReadback.Request(Shader.GetGlobalTexture("_RemainsRadiance"),0,TextureFormat.RGBAFloat);request.WaitForCompletion();
            if(request.hasError)throw new InvalidOperationException("Ошибка GPU-чтения при наблюдении лампы.");
            return request.GetData<Color>().ToArray();
        }
        private static void Finish()
        {
            report.success=true;report.detail="Наблюдение в Play Mode, настоящий seed104 и очередь FactoryLevelView; без сети/реального выстрела. Во втором сценарии контролируемая однокадровая эмиссия в штатном композите. Readback/PNG влияют на wall-clock: это не замер задержки или производительности.";
            File.WriteAllText(Path.Combine(Output,"report.json"),JsonUtility.ToJson(report,true));Debug.Log("LampExtinctionChecks наблюдение завершено: "+report.detail);End(0);
        }
        private static void End(int code)
        {
            SessionState.SetBool(Key,false);EditorApplication.update-=Watch;
            foreach(var obj in new UnityEngine.Object[]{visual,cameraRoot,flashSprite,flashTexture})if(obj!=null)UnityEngine.Object.DestroyImmediate(obj);
            FactoryContentSources.Clear();catalog?.Dispose();catalog=null;EditorApplication.Exit(code);
        }
    }
}
