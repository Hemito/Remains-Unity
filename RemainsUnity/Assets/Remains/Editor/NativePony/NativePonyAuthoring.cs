using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.U2D.Animation;
using UnityEngine.U2D.IK;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    public static class NativePonyAuthoring
    {
        public const string Root="Assets/Remains/Samples/NativePony";
        public static readonly string[] Variants={"Bare","Antirad","Creator","AntiradCreator"};
        [MenuItem("Remains/Пони/Создать образец штатного 2D-рига")]
        public static void CreateSample()
        {
            if(File.Exists(Root+"/PonyAnimation.prefab"))
            {
                if(!File.Exists(Root+"/PonyPosing.prefab"))throw new InvalidOperationException("Новый риг: сборка не завершена, отсутствует PonyPosing.prefab.");
                if(!File.Exists(Root+"/NativePony.unity"))BuildScene();
                if(!File.Exists(Root+"/README.md")){File.WriteAllText(Root+"/README.md",Readme);AssetDatabase.ImportAsset(Root+"/README.md");}
                Debug.Log("Образец штатного рига уже существует: "+Root+". Для своих изменений сохраните копию префаба и клипов.");return;
            }
            Build();
        }
        public static void Build(bool replaceSample=false)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Новый риг: создание недоступно в Play Mode.");
            if(File.Exists(Root+"/PonyAnimation.prefab") && !replaceSample)throw new InvalidOperationException("Новый риг уже создан; существующий редактируемый префаб не перезаписывается.");
            EnsureFolder(Root);EnsureFolder(Root+"/Art");
            var alternate=new CharacterAppearance {version=CharacterAppearance.CurrentVersion,headStyleId="pony.head.creator.5"};
            var variants=new[]{PonyAnimationArtwork.Prepare(),PonyAnimationArtwork.Prepare("armor.antirad"),
                PonyAnimationArtwork.Prepare(appearance:alternate),PonyAnimationArtwork.Prepare("armor.antirad",appearance:alternate)};
            const float ppu=512;var rectangles=NativePonyArtBake.Rectangles(variants,ppu);var sprites=new Sprite[variants.Length][];
            var libraries=new SpriteLibraryAsset[variants.Length];
            for(int v=0;v<variants.Length;v++)
            {
                EnsureFolder(Root+"/Art/"+Variants[v]);sprites[v]=new Sprite[NativePonyArtBake.Groups.Length];var library=ScriptableObject.CreateInstance<SpriteLibraryAsset>();
                for(int g=0;g<sprites[v].Length;g++)
                {
                    string group=NativePonyArtBake.Groups[g],path=Root+"/Art/"+Variants[v]+"/"+group+".png";
                    NativePonyArtBake.Bake(variants[v],group,rectangles[g],ppu,path);
                    NativePonyArtBake.Grid(group,rectangles[g],out var vertices,out var triangles);
                    sprites[v][g]=NativePonySpriteAuthoring.Import(path,rectangles[g],vertices,triangles,NativePonyRigDefinition.Bones,NativePonyRigDefinition.Weights(group,vertices));
                    library.AddCategoryLabel(sprites[v][g],group,"default");
                }
                libraries[v]=Save(library,Root+"/"+Variants[v]+".asset");
            }
            var shader=Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if(shader==null)throw new InvalidOperationException("Новый риг: отсутствует штатный шейдер URP 2D Sprite-Unlit-Default.");
            var material=Save(new Material(shader) {name="NativePony"},Root+"/NativePony.mat");
            var previewScene=EditorSceneManager.NewPreviewScene();GameObject actor=null;
            try
            {
                actor=CreateActor(sprites[0],rectangles,libraries[0],material,false,previewScene,out var bones);
                var idle=Save(NativePonyRigClips.CreateIdle(actor.transform,bones),Root+"/Idle.anim");
                var walk=Save(NativePonyRigClips.CreateWalk(actor.transform,bones),Root+"/Walk.anim");
                var curl=Save(NativePonyRigClips.CreateCurl(actor.transform,bones),Root+"/LegCurl.anim");
                string controllerPath=Root+"/Pony.controller";
                var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath)??AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                var machine=controller.layers[0].stateMachine;
                foreach(var state in machine.states)machine.RemoveState(state.state);
                var idleState=machine.AddState("Стойка");idleState.motion=idle;
                var walkState=machine.AddState("Ходьба");walkState.motion=walk;machine.defaultState=walkState;
                var curlState=machine.AddState("Сгиб ноги");curlState.motion=curl;
                EditorUtility.SetDirty(controller);AssetDatabase.SaveAssetIfDirty(controller);
                actor.AddComponent<Animator>().runtimeAnimatorController=controller;
                PrefabUtility.SaveAsPrefabAsset(actor,Root+"/PonyAnimation.prefab");Object.DestroyImmediate(actor);actor=null;
                actor=CreateActor(sprites[0],rectangles,libraries[0],material,true,previewScene,out _);
                PrefabUtility.SaveAsPrefabAsset(actor,Root+"/PonyPosing.prefab");Object.DestroyImmediate(actor);actor=null;
            }
            finally {if(actor!=null)Object.DestroyImmediate(actor);EditorSceneManager.ClosePreviewScene(previewScene);}
            BuildScene(replaceSample);
            File.WriteAllText(Root+"/README.md",Readme);
            AssetDatabase.ImportAsset(Root+"/README.md");
            Debug.Log("Штатный 2D-риг создан: 22 кости, 10 SpriteSkin, Animation Clips, четыре библиотеки внешности, отдельный префаб IK; "+Root);
        }
        private static GameObject CreateActor(Sprite[] sprites,Rect[] rectangles,SpriteLibraryAsset library,Material material,bool ik,Scene scene,out Transform[] bones)
        {
            var actor=new GameObject(ik?"PonyPosing":"PonyAnimation");SceneManager.MoveGameObjectToScene(actor,scene);
            var skeleton=new GameObject("Skeleton").transform;skeleton.SetParent(actor.transform,false);bones=NativePonyRigDefinition.BuildTransforms(skeleton);
            actor.AddComponent<SpriteLibrary>().spriteLibraryAsset=library;
            var art=new GameObject("Artwork").transform;art.SetParent(actor.transform,false);
            for(int g=0;g<sprites.Length;g++)
            {
                var layer=new GameObject(NativePonyArtBake.Groups[g]);layer.transform.SetParent(art,false);layer.transform.localPosition=rectangles[g].min;
                var renderer=layer.AddComponent<SpriteRenderer>();renderer.sprite=sprites[g];renderer.sharedMaterial=material;renderer.sortingOrder=g;
                var skin=NativePonySpriteAuthoring.Bind(renderer,bones[0],bones);skin.forceCpuDeformation=true;
                var resolver=layer.AddComponent<SpriteResolver>();resolver.SetCategoryAndLabel(NativePonyArtBake.Groups[g],"default");resolver.ResolveSpriteToSpriteRenderer();
            }
            if(ik)AddIk(actor,bones);
            return actor;
        }
        private static void AddIk(GameObject actor,Transform[] bones)
        {
            var manager=actor.AddComponent<IKManager2D>();var controls=new GameObject("Controls").transform;controls.SetParent(actor.transform,false);
            foreach(string group in new[]{"frontNear","frontFar","hindNear","hindFar"})
            {
                var chain=NativePonyRigDefinition.Chain(group);var end=bones[chain[chain.Length-1]];
                var target=new GameObject(group+"Target").transform;target.SetParent(controls,false);target.position=end.position;
                var solverObject=new GameObject(group+"Solver");solverObject.transform.SetParent(controls,false);var solver=solverObject.AddComponent<LimbSolver2D>();
                solver.GetChain(0).effector=end;solver.GetChain(0).target=target;solver.constrainRotation=false;solver.Initialize();manager.AddSolver(solver);
                if(!solver.isValid)throw new InvalidOperationException("Новый риг: не инициализировалась цепь IK "+group);
            }
        }
        private static void BuildScene(bool replace=false)
        {
            if(!replace && File.Exists(Root+"/NativePony.unity"))return;
            if(!Application.isBatchMode && EditorSceneManager.GetSceneManagerSetup().Any(s=>string.IsNullOrEmpty(s.path)))
                throw new InvalidOperationException("Для создания сцены образца сначала сохраните или закройте безымянную сцену. Текущая работа Editor не изменена.");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,Application.isBatchMode?NewSceneMode.Single:NewSceneMode.Additive);
            try
            {
                var cameraObject=new GameObject("Камера образца");SceneManager.MoveGameObjectToScene(cameraObject,scene);var camera=cameraObject.AddComponent<Camera>();
                camera.orthographic=true;camera.orthographicSize=1.1f;camera.transform.position=new Vector3(0,.75f,-10);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.16f,.18f,.21f);
                var animated=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/PonyAnimation.prefab"),scene);animated.transform.position=Vector3.left*.9f;
                var posing=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/PonyPosing.prefab"),scene);posing.transform.position=Vector3.right*.9f;
                EditorSceneManager.SaveScene(scene,Root+"/NativePony.unity");
            }
            finally {EditorSceneManager.CloseScene(scene,true);}
        }
        private static T Save<T>(T value,string path) where T:Object
        {
            var previous=AssetDatabase.LoadAssetAtPath<T>(path);
            if(previous==null){AssetDatabase.CreateAsset(value,path);return value;}
            EditorUtility.CopySerialized(value,previous);Object.DestroyImmediate(value);EditorUtility.SetDirty(previous);AssetDatabase.SaveAssetIfDirty(previous);return previous;
        }
        private static void EnsureFolder(string path)
        {
            if(AssetDatabase.IsValidFolder(path))return;string parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
        private const string Readme="# Штатный 2D-риг пони\n\nОткройте NativePony.unity. Слева PonyAnimation с Animator, справа PonyPosing с родным IK Manager 2D. Это отдельный образец; игровая кукла не заменена.\n\n## Анимация\n\nВыберите корень PonyAnimation, откройте Window → Animation → Animation. Выберите Idle, Walk или LegCurl. Ключи — обычные Transform; их можно редактировать штатными средствами. Перед изменениями сохраните копии клипа и префаба.\n\n## Постановка позы\n\nВ PonyPosing выберите Controls → frontNearTarget и перемещайте его инструментом Move в плоскости XY. Остальные копыта имеют свои цели. Родной Limb Solver управляет двумя нижними звеньями; плечо/бедро можно вращать отдельно. У Solver есть Flip для выбора стороны сгиба.\n\n## Внешность\n\nНа корне компонент Sprite Library. Поменяйте Sprite Library Asset на Bare, Antirad, Creator или AntiradCreator. Все варианты используют одинаковый скелет.\n\n## Сетка и веса\n\nВыберите PNG в Art, нажмите Sprite Editor → Skinning Editor. Сетка, кости и веса сохранены в импортере PNG. В этом образце здесь редактируются сетка и веса. Изменение исходных костей в одном PNG не обновляет общий скелет и остальные слои: для смены bind-позы требуется согласованное обновление всех спрайтов и префаба. Четыре ноги — сплошные деформируемые спрайты; голова, туловище и отдельный ПипБак жёсткие. Переходы весов перекрываются, а нижнее звено от запястья или скакательного сустава сохраняет жёсткость. Исходные рисунки пока сохранены, художественная очистка контуров и сгибов не выполнена.\n\n## Границы\n\nЦвета запечены для образца. Независимая мимика, произвольное смешивание одежды, игровое освещение, силуэты теней и подключение к боевым состояниям не переносились. По сравнению с исходным составом рисунка изменён порядок девяти крупных слоёв. Образец использует штатный Sprite-Unlit материал и SpriteSkin с включёнными CPU-данными для диагностики; производительность полного завода не заявляется.\n";
    }
}
