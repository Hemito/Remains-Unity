using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;
using UnityEngine.U2D.IK;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    public static class NativePonyChecks
    {
        public static void RunBatch()
        {
            int exit=0;var output=Path.GetFullPath("../.reference/native-pony");Directory.CreateDirectory(output);
            try
            {
                if(Environment.GetCommandLineArgs().Contains("-nativeRepair"))
                {
                    string before=Path.Combine(output,"geometry-before.json");
                    if(!File.Exists(before))File.WriteAllText(before,NativePonyMeshAudit.Measure().ToString());
                    NativePonyAuthoring.Build(true);
                }
                else NativePonyAuthoring.CreateSample();
                Run(output);
            }
            catch(Exception problem)
            {
                Debug.LogException(problem);File.WriteAllText(Path.Combine(output,"failure.txt"),problem.ToString());
                File.WriteAllText(Path.Combine(output,"report.json"),new JObject { ["status"]="FAIL",["reason"]=problem.Message }.ToString());exit=1;
            }
            EditorApplication.Exit(exit);
        }
        private static void Run(string output)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(NativePonyAuthoring.Root+"/PonyAnimation.prefab"));
            root.GetComponent<Animator>().enabled=false;
            var skins=root.GetComponentsInChildren<SpriteSkin>();var library=root.GetComponent<SpriteLibrary>();var resolvers=root.GetComponentsInChildren<SpriteResolver>();
            Require(skins.Length==10 && resolvers.Length==10,"Число слоёв не равно 10");
            float restError=0;int vertices=0;
            foreach(var skin in skins)
            {
                var renderer=skin.GetComponent<SpriteRenderer>();var sprite=renderer.sprite;
                var positions=sprite.GetVertexAttribute<Vector3>(VertexAttribute.Position);var weights=sprite.GetVertexAttribute<BoneWeight>(VertexAttribute.BlendWeight);var poses=sprite.GetBindPoses();
                Require(sprite.GetBones().Length==22 && poses.Length==22,"Не сохранены 22 кости в "+skin.name);
                vertices+=positions.Length;
                for(int v=0;v<positions.Length;v++)
                {
                    var weight=weights[v];Vector3 actual=Vector3.zero;
                    foreach(var pair in new[]{(weight.boneIndex0,weight.weight0),(weight.boneIndex1,weight.weight1),(weight.boneIndex2,weight.weight2),(weight.boneIndex3,weight.weight3)})
                        if(pair.Item2>0)actual+=skin.boneTransforms[pair.Item1].localToWorldMatrix.MultiplyPoint3x4(poses[pair.Item1].MultiplyPoint3x4(positions[v]))*pair.Item2;
                    restError=Mathf.Max(restError,Vector3.Distance(actual,renderer.transform.TransformPoint(positions[v])));
                }
            }
            Require(restError<.0001f,"Регистрация native SpriteSkin сдвигает исходный рисунок: "+restError);
            var cameraObject=new GameObject("Камера проверки native рига");var camera=cameraObject.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=1.1f;
            camera.transform.position=new Vector3(-.04f,.76f,-10);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.16f,.18f,.21f);
            var walk=AssetDatabase.LoadAssetAtPath<AnimationClip>(NativePonyAuthoring.Root+"/Walk.anim");
            var curl=AssetDatabase.LoadAssetAtPath<AnimationClip>(NativePonyAuthoring.Root+"/LegCurl.anim");
            var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>(NativePonyAuthoring.Root+"/Idle.anim");
            Require(walk!=null && curl!=null && idle!=null && AnimationUtility.GetCurveBindings(walk).Length>20,"Нет штатных клипов с ключами Transform");
            int captures=0;float maxDeformation=0;var headSprites=new Sprite[4];
            for(int variant=0;variant<NativePonyAuthoring.Variants.Length;variant++)
            {
                string name=NativePonyAuthoring.Variants[variant];library.spriteLibraryAsset=AssetDatabase.LoadAssetAtPath<SpriteLibraryAsset>(NativePonyAuthoring.Root+"/"+name+".asset");
                foreach(var resolver in resolvers)resolver.ResolveSpriteToSpriteRenderer();
                headSprites[variant]=library.spriteLibraryAsset.GetSprite("head","default");
                foreach(var skin in skins)Require(skin.GetComponent<SpriteRenderer>().sprite==library.spriteLibraryAsset.GetSprite(skin.name,"default"),"Смена библиотеки не обновила "+name+"/"+skin.name);
                Vector3[] restVertices=null;var foreleg=skins.Single(s=>s.name=="frontNear");
                foreach(var shot in new[]{(idle,0f,"rest"),(walk,.25f,"step"),(curl,.5f,"curl")})
                {
                    shot.Item1.SampleAnimation(root,shot.Item2);Deform(skins);
                    var actual=foreleg.GetDeformedVertexPositionData().ToArray();
                    if(shot.Item3=="rest")restVertices=actual;
                    if(shot.Item3=="curl")for(int v=0;v<actual.Length;v++)maxDeformation=Mathf.Max(maxDeformation,Vector3.Distance(actual[v],restVertices[v]));
                    Capture(camera,Path.Combine(output,name+"-"+shot.Item3+".png"));captures++;
                }
                Require(!File.ReadAllBytes(Path.Combine(output,name+"-rest.png")).SequenceEqual(File.ReadAllBytes(Path.Combine(output,name+"-curl.png"))),"Кадры стойки и сгиба совпадают: "+name);
                if(variant==0 || variant==3)for(int frame=0;frame<=30;frame++)
                {walk.SampleAnimation(root,frame/30f);Deform(skins);Capture(camera,Path.Combine(output,name+"-walk-"+frame.ToString("D2")+".png"));captures++;}
            }
            Require(headSprites[0]!=headSprites[2] && headSprites[1]!=headSprites[3],"Альтернативная голова не имеет отдельного спрайта");
            Require(!File.ReadAllBytes(AssetDatabase.GetAssetPath(headSprites[0])).SequenceEqual(File.ReadAllBytes(AssetDatabase.GetAssetPath(headSprites[2]))),"Рисунки двух голов совпадают");
            Require(!File.ReadAllBytes(AssetDatabase.GetAssetPath(headSprites[1])).SequenceEqual(File.ReadAllBytes(AssetDatabase.GetAssetPath(headSprites[3]))),"Антирад потерял выбранную форму головы");
            Require(maxDeformation>.05f,"Штатный буфер не содержит заметного сгиба сетки");
            Object.DestroyImmediate(root);
            var posing=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(NativePonyAuthoring.Root+"/PonyPosing.prefab"));
            var manager=posing.GetComponent<IKManager2D>();Require(manager!=null && manager.solvers.Count==4,"Нет четырёх штатных IK цепочек");
            var solver=manager.solvers[0];var chain=solver.GetChain(0);var initial=chain.effector.position;chain.target.position=initial+new Vector3(.08f,.18f,0);manager.UpdateManager();
            float ikError=Vector3.Distance(chain.target.position,chain.effector.position);Require(ikError<.005f,"Native IK не достигает доступной цели: "+ikError);
            Deform(posing.GetComponentsInChildren<SpriteSkin>());Capture(camera,Path.Combine(output,"native-ik.png"));captures++;
            Object.DestroyImmediate(posing);Object.DestroyImmediate(cameraObject);
            var geometry=NativePonyMeshAudit.Measure();File.WriteAllText(Path.Combine(output,"geometry-after.json"),geometry.ToString());
            NativePonyPreviewPage.Write(output);
            Require((int)geometry["maxFlippedTriangles"]==0 && (int)geometry["maxCollapsedTriangles"]==0,"Видимая сетка выворачивается или схлопывается; см. geometry-after.json");
            File.WriteAllText(Path.Combine(output,"report.json"),new JObject { ["status"]="PASS",["bones"]=22,["spriteSkins"]=10,["vertices"]=vertices,["variants"]=4,
                ["nativeAnimationClips"]=3,["nativeIkSolvers"]=4,["restVertexError"]=restError,["nativeIkError"]=ikError,["maxVertexDeformation"]=maxDeformation,["capturedImages"]=captures,
                ["maxVisibleFlippedTriangles"]=(int)geometry["maxFlippedTriangles"],["maxVisibleCollapsedTriangles"]=(int)geometry["maxCollapsedTriangles"],
                ["minSignedAreaRatio"]=(float)geometry["minSignedAreaRatio"],
                ["limits"]="Standalone native rig; original raster artwork; no Player integration or performance claim"}.ToString());
            Debug.Log($"Штатный 2D-риг: PASS; 22 кости, 10 SpriteSkin, {vertices} вершин, 4 библиотеки, 3 AnimationClip, 4 IK; видимых выворотов и схлопываний нет; rest={restError}, IK={ikError}; {output}");
        }
        internal static void Deform(SpriteSkin[] skins)
        {
            // Diagnostic only: execute the installed package's real LateUpdate synchronously between
            // capture samples. A batch method has no intervening Editor player-loop frame.
            const string typeName="UnityEngine.U2D.Animation.DeformationManager";
            var type=typeof(SpriteSkin).Assembly.GetType(typeName)??throw new InvalidOperationException("Проверка рига: изменилась версия 2D Animation, нет "+typeName);
            var instance=type.GetProperty("instance",BindingFlags.Public|BindingFlags.Static)?.GetValue(null);
            var update=type.GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance);
            if(instance==null || update==null)throw new InvalidOperationException("Проверка рига: нет точки обновления штатного DeformationManager.");
            update.Invoke(instance,null);
            foreach(var skin in skins)Require(skin.HasCurrentDeformedVertices(),"Нет штатного буфера деформации: "+skin.name);
        }
        private static void Capture(Camera camera,string path)
        {
            const int width=640,height=600;var target=new RenderTexture(width,height,24);var image=new Texture2D(width,height,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
            try {camera.targetTexture=target;camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
            finally{camera.targetTexture=null;RenderTexture.active=previous;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);}
        }
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException("Проверка native рига: "+message);}
    }
}
