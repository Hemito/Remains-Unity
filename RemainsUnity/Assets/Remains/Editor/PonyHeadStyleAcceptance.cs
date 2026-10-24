using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonyHeadStyleAcceptance
    {
        [MenuItem("Remains/Подготовить и проверить формы головы")]
        public static void Prepare()
        {
            PonyHeadCatalog.Reset();PonyHeadCatalog.Load();
            foreach(var style in PonyHeadCatalog.Styles)
            {
                if(style.id==CharacterAppearance.OriginalHeadStyleId) continue;
                string path="Assets/Remains/Resources/"+style.texture+".png";
                var importer=AssetImporter.GetAtPath(path) as TextureImporter;
                if(importer==null) throw new InvalidOperationException($"Нет импорта головы: head={style.id}, path={path}.");
                importer.textureType=TextureImporterType.Default;importer.alphaIsTransparency=true;
                importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled=false;importer.npotScale=TextureImporterNPOTScale.None;
                importer.maxTextureSize=2048;importer.SaveAndReimport();
            }
            PonyAppearanceAuthoring.Build();
            CharacterInterfaceAuthoring.RefreshHeadSelector();
            PonyHeadAppearanceChecks.Run();CharacterInterfaceChecks.Run();
            CheckRenderedFrames();RenderCatalog();
            AssetDatabase.SaveAssets();
            Debug.Log("Формы головы подготовлены и проверены: исходная + девять новых, мир, портрет, все позы/направления и сохранение.");
        }

        public static void CheckRenderedFrames()
        {
            WeaponCatalog.Load();Anatomy.Load();PonyVisual.Prepare();
            var actor=new ActorState {id="head-render-check",appearance=new CharacterAppearance {version=CharacterAppearance.CurrentVersion},weapon=null};
            var visual=(PonyVisual)ActorVisualFactory.Create(actor);
            var inventory=UnityEngine.Object.Instantiate(GameAssets.Load<PersonalInventoryView>("Presentation/PersonalInventory"));
            var portrait=new InventoryPortrait(inventory.Portrait,inventory.PortraitParts);
            int drawn=0;var structure=visual.transform.childCount;
            try
            {
                foreach(var style in PonyHeadCatalog.Styles)
                {
                    actor.appearance.headStyleId=style.id;visual.Bind(actor);portrait.Refresh(actor);
                    var key=PonyAppearance.Snapshot(actor);var plan=PonyRenderSelection.PrepareEquipment(actor);
                    var portraitParts=portrait.Parts.Where(part=>part.gameObject.activeSelf).ToArray();
                    Require(portraitParts.Length==plan.PortraitFrame.Length,"число частей портрета: "+style.id);
                    for(int p=0;p<portraitParts.Length;p++)
                    {
                        var expected=plan.PortraitFrame[p];
                        Require(portraitParts[p].mainTexture==expected.material.mainTexture,"текстура портрета: "+style.id);
                        Require(portraitParts[p].color==expected.baseColor*PonyAppearance.Tint(expected.slot,key),"цвет портрета: "+style.id);
                        for(int v=0;v<4;v++) Require(portraitParts[p].PreparedPoint(v)==expected.points[v],"вершина портрета: "+style.id);
                    }
                    foreach(var clip in PonyAnimationData.Library.clips)
                    {
                        actor.pose=clip.id;actor.down=false;actor.climbing=false;
                        var frames=PonyRenderSelection.Frames(key,clip.id);
                        for(int f=0;f<frames.Length;f++) foreach(int facing in new[]{-1,1})
                        {
                            actor.facing=facing;
                            actor.climbing=clip.id=="climb" && f==0;actor.vy=0;
                            float elapsed=(!clip.nonLooping && !clip.playIntro && f>=clip.loopStartFrame)
                                ?(f-clip.loopStartFrame+.1f)/clip.fps:(f+.1f)/clip.fps;
                            visual.DrawPose(elapsed,true);
                            Require(visual.DrawnClip==clip.id && visual.DrawnFrame==f,$"кадр/поза: head={style.id}, clip={clip.id}, frame={f}");
                            Require(visual.transform.localScale.x==facing,"направление: "+style.id);
                            Require(visual.transform.childCount==structure,"пул частей пересоздан при отрисовке: "+style.id);
                            var actual=visual.GetComponentsInChildren<MeshFilter>().Where(filter=>filter.GetComponent<MeshRenderer>().enabled).ToArray();
                            Require(actual.Select(filter=>filter.sharedMesh).SequenceEqual(frames[f].Select(part=>part.mesh)),
                                $"меши мира: head={style.id}, clip={clip.id}, frame={f}, facing={facing}");
                            drawn++;
                        }
                    }
                }
                Debug.Log($"Формы головы, actual renderer: PASS — {drawn} состояний, все кадры × обе стороны; общий idle-портрет и постоянный пул.");
            }
            finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);UnityEngine.Object.DestroyImmediate(inventory.gameObject);}
        }

        public static void RenderCatalog()
        {
            // Isolated editor scene only; no saved gameplay scene is modified.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            WeaponCatalog.Load();Anatomy.Load();PonyVisual.Prepare();
            var objects=new List<GameObject>();
            var actors=new List<(ActorState actor,PonyVisual visual)>();
            var camera=new GameObject("Сравнение форм головы").AddComponent<Camera>();objects.Add(camera.gameObject);
            camera.orthographic=true;camera.orthographicSize=3.6f;
            camera.transform.position=new Vector3(5.6f,-.6f,-10);
            camera.backgroundColor=new Color(.12f,.14f,.16f);
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/pony-heads"));
            Directory.CreateDirectory(directory);
            try
            {
                for(int i=0;i<PonyHeadCatalog.Styles.Count;i++)
                {
                    var style=PonyHeadCatalog.Styles[i];
                    var actor=new ActorState {id="head-catalog:"+style.id,x=(i%5)*2.8f,y=-(i/5)*3.1f,weapon=null,
                        appearance=new CharacterAppearance {version=CharacterAppearance.CurrentVersion,headStyleId=style.id,
                            fur=0xBBCCDD,hair=0x8855DD,eyes=0x66DD55}};
                    var visual=(PonyVisual)ActorVisualFactory.Create(actor);objects.Add(visual.gameObject);visual.Bind(actor);visual.DrawPose(0);
                    actors.Add((actor,visual));
                }
                Capture("ten-heads-native-right.png");
                foreach(var pair in actors)
                {
                    pair.actor.facing=-1;pair.visual.DrawPose(0);
                }
                Capture("ten-heads-native-left.png");
                File.WriteAllText(Path.Combine(directory,"catalog-order.json"),JsonUtility.ToJson(new PonyHeadStyleList {version=1,styles=PonyHeadCatalog.Styles.ToArray()},true));

                void Capture(string name)
                {
                    // Fix the editorial viewport explicitly: restoring a temporary
                    // render target must not inherit the editor's screen aspect.
                    camera.rect=new Rect(0,0,1,1);camera.aspect=2;
                    camera.orthographicSize=3.6f;camera.transform.position=new Vector3(5.6f,-.6f,-10);
                    camera.projectionMatrix=Matrix4x4.Ortho(-7.2f,7.2f,-3.6f,3.6f,camera.nearClipPlane,camera.farClipPlane);
                    Runtime.NetworkFrameCapture.Save(camera,Path.Combine(directory,name),width:2880,height:1440);
                }
            }
            finally {foreach(var item in objects) UnityEngine.Object.DestroyImmediate(item);}
        }
        private static void Require(bool value,string detail)
        {if(!value) throw new InvalidOperationException("Проверка новых голов: "+detail);}
    }
}
