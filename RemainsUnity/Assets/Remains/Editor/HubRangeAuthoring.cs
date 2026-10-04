using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Explicit editor migration. The resulting room is an ordinary editable prefab.
    public static class HubRangeAuthoring
    {
        public static void ApplyStructuralLightResponse()
        {
            const string path="Assets/Remains/Resources/Levels/hub.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);int count=0;
            try
            {
                var room=root.GetComponent<RoomAuthoring>();
                foreach(var art in root.GetComponentsInChildren<RoomArtworkAuthoring>())
                {
                    var data=art.Read(room);
                    if(data.texture!="Hub/concrete" && data.texture!="Hub/rim") continue;
                    string materialPath="Assets/Remains/Resources/Levels/hub-structural-"+data.texture.Substring(4)+".mat";
                    var renderer=art.GetComponent<MeshRenderer>();
                    var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if(material==null)
                    {
                        material=new Material(renderer.sharedMaterial) {name="Обрамление хаба: "+data.texture};
                        material.SetFloat("_Emission",1);AssetDatabase.CreateAsset(material,materialPath);
                    }
                    if(material.GetFloat("_Emission")!=1) throw new InvalidOperationException("Материал обрамления изменён: "+materialPath);
                    renderer.sharedMaterial=material;count++;
                }
                if(count==0) throw new InvalidOperationException("Не найдены бетонные слои обрамления хаба.");
                PrefabUtility.SaveAsPrefabAsset(root,path);
                Debug.Log($"Обрамление хаба: сохранено слоёв {count}, общий свет и материалы актёров не изменены.");
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();RenderReferenceHub();
        }
        public static void ApplyOriginalLightMasks()
        {
            Render();
            foreach(int x in new[]{36,50})
            {
                string image=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../..",$".reference/tests/hub-range-{x}.png"));
                string before=image.Replace(".png","-before-original-mask.png");
                if(!System.IO.File.Exists(before)) System.IO.File.Copy(image,before);
            }
            const string path="Assets/Remains/Resources/Levels/hub.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var room=root.GetComponent<RoomAuthoring>();
                RoomLight reference=null;RoomArtwork fixture=null;
                foreach(var light in root.GetComponentsInChildren<RoomLightAuthoring>(true))
                {
                    var data=light.Read(room);
                    if(data.id=="lamp-7") reference=data;
                }
                foreach(var art in root.GetComponentsInChildren<RoomArtworkAuthoring>(true))
                {
                    var data=art.Read(room);
                    if(data.texture=="Hub/decor-light3-1" && data.x<37) fixture=data;
                }
                if(reference?.maskTexture==null || fixture==null || reference.mask!="Original/Room/Hub/light-mask-light3-1")
                    throw new InvalidOperationException("Не найден эталонный плафон light3 с исходной маской хаба.");
                int count=0;
                foreach(var light in root.GetComponentsInChildren<RoomLightAuthoring>(true))
                {
                    var data=light.Read(room);if(!data.id.StartsWith("range-",StringComparison.Ordinal)) continue;
                    var artwork=light.GetComponentsInChildren<RoomArtworkAuthoring>();
                    if(artwork.Length!=1) throw new InvalidOperationException("Лампа тира должна содержать один плафон: "+data.id);
                    var art=artwork[0].Read(room);
                    if(art.texture!=fixture.texture) throw new InvalidOperationException("Другой тип плафона тира: "+data.id);
                    float sx=art.width/fixture.width;
                    // Preserve the source fixture's aspect ratio, with its top edge fixed.
                    float top=art.y+art.height;
                    var scale=artwork[0].transform.localScale;scale.y*=fixture.height*sx/art.height;
                    artwork[0].transform.localScale=scale;
                    art=artwork[0].Read(room);
                    artwork[0].transform.position+=room.transform.TransformVector(Vector3.up*(top-art.y-art.height));
                    art=artwork[0].Read(room);
                    float sy=art.height/fixture.height;
                    data.mask=reference.mask;data.maskTexture=reference.maskTexture;
                    data.maskX=art.x+(reference.maskX-fixture.x)*sx;
                    data.maskY=art.y+(reference.maskY-fixture.y)*sy;
                    data.maskWidth=reference.maskWidth*sx;data.maskHeight=reference.maskHeight*sy;
                    var local=light.transform.localPosition;light.Configure(data,reference.maskTexture);light.transform.localPosition=local;count++;
                }
                if(count!=3) throw new InvalidOperationException("Ожидались три источника тира, найдено "+count);
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();ContractChecks.Run();Render();
        }
        public static void BindFixtures()
        {
            const string path="Assets/Remains/Resources/Levels/hub.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var room=root.GetComponent<RoomAuthoring>();int count=0;
                foreach(var light in root.GetComponentsInChildren<RoomLightAuthoring>(true))
                {
                    var data=light.Read(room);if(!data.id.StartsWith("range-",StringComparison.Ordinal)) continue;
                    var matches=new List<RoomArtworkAuthoring>();
                    foreach(var art in root.GetComponentsInChildren<RoomArtworkAuthoring>(true))
                    {
                        var item=art.Read(room);
                        if(item.texture=="Hub/decor-light3-1" && Mathf.Abs(item.x-(data.x-1))<.001f && Mathf.Abs(item.y-6.6f)<.001f)
                            matches.Add(art);
                    }
                    if(matches.Count!=1) throw new InvalidOperationException($"Свет тира {data.id}: найдено изображений {matches.Count}, ожидалось одно.");
                    matches[0].transform.SetParent(light.transform,true);count++;
                }
                if(count!=3) throw new InvalidOperationException($"Ожидались три лампы тира, найдено {count}.");
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            ContractChecks.Run();
        }
        public static void PolishFloor()
        {
            const string path="Assets/Remains/Resources/Levels/hub.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var room=root.GetComponent<RoomAuthoring>();var range=root.transform.Find("Тир");int changed=0;
                foreach(var part in range.GetComponentsInChildren<RoomArtworkAuthoring>())
                {
                    var art=part.Read(room);if(art.texture!="Hub/floor") continue;
                    var material=part.GetComponent<MeshRenderer>().sharedMaterial;var original=material.mainTexture;
                    var replacement=Resources.Load<Texture2D>("Original/Room/Hub/concrete");
                    if(replacement==null) throw new InvalidOperationException("Нет бетона для основания тира.");
                    var mesh=part.GetComponent<MeshFilter>().sharedMesh;var uv=mesh.uv;
                    var ratio=new Vector2((float)original.width/replacement.width,(float)original.height/replacement.height);
                    for(int i=0;i<uv.Length;i++) uv[i]=Vector2.Scale(uv[i],ratio);
                    mesh.uv=uv;material.mainTexture=replacement;art.texture="Hub/concrete";part.Configure(art);
                    EditorUtility.SetDirty(mesh);EditorUtility.SetDirty(material);changed++;
                }
                if(changed!=1) throw new InvalidOperationException("Ожидалось одно старое основание тира, найдено "+changed);
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();
        }
        public static void Render()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
            Anatomy.Load();WeaponCatalog.Load();PonyAnimationData.Load();PonyVisual.Prepare();
            var layout=RoomLayout.Load("hub");AuthoredRoomVisual.Create("hub",layout);
            var world=new WorldState();HubTrainingContent.Install(world);
            foreach(var actor in world.actors) ActorVisualFactory.Create(actor).Bind(actor);
            var player=new ActorState {id="range-view",x=40,y=.8f,facing=1};
            ActorVisualFactory.Create(player).Bind(player);
            var camera=new GameObject("Камера тира").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=4.8f;camera.backgroundColor=new Color(.07f,.08f,.09f);
            foreach(float x in new[]{36f,50f})
            {
                camera.transform.position=new Vector3(x,3.5f,-10);
                string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../..",$".reference/tests/hub-range-{x}.png"));
                Remains.Runtime.NetworkFrameCapture.Save(camera,path,width:1920,height:1080);
            }
        }
        public static void RenderReferenceHub()
            =>RenderReferenceHub(false);
        public static void RenderReferenceHubCandidate()=>RenderReferenceHub(true);
        private static void RenderReferenceHub(bool candidate)
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Single);
            var layout=RoomLayout.Load("hub");var room=AuthoredRoomVisual.Create("hub",layout);
            if(candidate)
            {
                var lighting=room.GetComponent<RoomLighting>();var profile=lighting.Profile;
                profile.ambient=new Color(.09f,.09f,.09f,1);
                foreach(var light in profile.lights) if(light.id=="hall-0" || light.id=="hall-1" || light.id=="hall-2") light.intensity=.05f;
                lighting.Initialize(layout,profile);
                foreach(var art in room.GetComponentsInChildren<RoomArtworkAuthoring>())
                {
                    var data=art.Read(room.GetComponent<RoomAuthoring>());
                    if(data.texture!="Hub/concrete" && data.texture!="Hub/rim") continue;
                    var block=new MaterialPropertyBlock();var renderer=art.GetComponent<MeshRenderer>();
                    renderer.GetPropertyBlock(block);block.SetFloat("_Emission",1);renderer.SetPropertyBlock(block);
                }
            }
            var camera=new GameObject("Сравнение исходной части хаба").AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=38.4f*1327/2606/2;
            camera.transform.position=new Vector3(19.2f,10,-10);camera.backgroundColor=new Color(.07f,.08f,.09f);
            string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,candidate?"../../.reference/tests/hub-reference-lighting-candidate.png":"../../.reference/tests/hub-reference-lighting.png"));
            Remains.Runtime.NetworkFrameCapture.Save(camera,path,width:2606,height:1327);
        }
        public static void CheckPassage()
        {
            var room=RoomLayout.Load("hub");
            if(room.right<60) throw new InvalidOperationException("Тир не входит в границы хаба.");
            foreach(float dt in new[]{1f/30,1f/60,1f/144})
            {
                var actor=new ActorState {id="range-passage",x=36,y=.8f};
                var movement=new Remains.Gameplay.MovementController(room);
                for(int i=0;i<Mathf.CeilToInt(3/dt);i++) movement.Step(actor,Vector2.right,false,dt);
                if(actor.x<44 || Mathf.Abs(actor.y-.8f)>.001f) throw new InvalidOperationException("Проход в тир заблокирован или пол расходится: "+actor.Position);
                for(int i=0;i<Mathf.CeilToInt(3/dt);i++) movement.Step(actor,Vector2.left,false,dt);
                if(actor.x>37 || Mathf.Abs(actor.y-.8f)>.001f) throw new InvalidOperationException("Обратный проход из тира заблокирован: "+actor.Position);
            }
            Debug.Log("Проход хаб/тир: PASS — непрерывная геометрия, ходьба в обе стороны при 30/60/144 FPS.");
        }
        public static void Build()
        {
            const string path="Assets/Remains/Resources/Levels/hub.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);GameObject generated=null;
            try
            {
                var room=root.GetComponent<RoomAuthoring>();var layout=room.CreateLayout();
                if(layout.right!=37.6f || root.transform.Find("Тир")!=null) throw new InvalidOperationException("Тир уже добавлен или границы хаба изменены.");
                int opened=0;
                foreach(var surface in root.GetComponentsInChildren<RoomSurfaceAuthoring>())
                {
                    var data=surface.Read(room);
                    if(Mathf.Abs(data.x-37.6f)<.01f && data.y>=.79f && data.y<4)
                    {UnityEngine.Object.DestroyImmediate(surface.gameObject);opened++;}
                }
                if(opened!=4) throw new InvalidOperationException("Проём тира: ожидались четыре сегмента стены, найдено "+opened);
                foreach(var art in root.GetComponentsInChildren<RoomArtworkAuthoring>())
                {
                    var data=art.Read(room);
                    if(data.x>37.5f && data.x<37.7f && data.y>=.79f && data.y<4 &&
                        (data.texture=="Hub/concrete" || data.texture=="Hub/rim")) UnityEngine.Object.DestroyImmediate(art.gameObject);
                }
                var surfaces=new[]{
                    new RoomSurface {x=37.6f,y=0,width=24.8f,height=.8f,texture="Hub/floor"},
                    new RoomSurface {x=38.4f,y=7.2f,width=24,height=.8f,texture="Hub/concrete"},
                    new RoomSurface {x=61.6f,y=.8f,width=.8f,height=6.4f,texture="Hub/concrete"}};
                var artLayers=new List<RoomArtwork> {
                    new() {x=37.6f,y=.8f,width=24,height=6.4f,texture="Hub/F",order=-30,repeat=true,shade=.85f}};
                foreach(var surface in surfaces) artLayers.Add(new RoomArtwork {x=surface.x,y=surface.y,width=surface.width,height=surface.height,texture=surface.texture,order=-4,repeat=true});
                foreach(float x in new[]{43f,49f,55f,61f})
                    artLayers.Add(new RoomArtwork {x=x,y=.8f,width=.14f,height=6.4f,texture="Hub/rim",order=-15,repeat=true});
                foreach(float x in new[]{42f,49f,56f})
                    artLayers.Add(new RoomArtwork {x=x,y=6.6f,width=1.8f,height=.4f,texture="Hub/decor-light3-1",order=-12,selfIlluminated=true});
                var annex=new RoomLayout {id="hub-range",left=37.6f,right=62.4f,bottom=0,top=8,lightingProfile=layout.lightingProfile,
                    platforms=surfaces,ladders=Array.Empty<RoomLadder>(),artwork=artLayers.ToArray()};
                generated=RoomVisual.Build(annex,new RoomLightProfile {ambient=Color.white,lights=Array.Empty<RoomLight>()});generated.name="Тир";
                RoomArtworkAssets.Save(generated,"Assets/Remains/Resources/Levels","hub-range-artwork");
                generated.transform.SetParent(root.transform,false);
                foreach(var surface in surfaces)
                {
                    var part=new GameObject("Геометрия тира: "+surface.texture);part.transform.SetParent(generated.transform,false);
                    part.AddComponent<RoomSurfaceAuthoring>().Configure(surface);
                }
                var lighting=root.GetComponentInChildren<RoomLightingAuthoring>();
                foreach(float x in new[]{43f,50f,57f})
                {
                    var light=new GameObject("Свет тира "+x);light.transform.SetParent(lighting.transform,false);
                    light.AddComponent<RoomLightAuthoring>().Configure(new RoomLight {id="range-"+x,x=x,y=6.4f,radius=6,intensity=.85f,color=new Color(1,.93f,.75f)},null);
                }
                layout.right=61.6f;room.Configure(layout);
                PrefabUtility.SaveAsPrefabAsset(root,path);generated=null;
            }
            finally {if(generated!=null) UnityEngine.Object.DestroyImmediate(generated);PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();BindFixtures();
        }
    }
}
