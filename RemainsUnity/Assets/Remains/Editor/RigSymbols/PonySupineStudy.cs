using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
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
    // Detached pose study: moves the existing seventeen draw nodes. No game pose, rig bank or source artwork is edited.
    public static class PonySupineStudy
    {
        private const string RecipePath="Assets/Remains/Editor/RigSymbols/SupinePose.json";
        [Serializable] private sealed class Recipe {public int schema;public string outfit;public Rule[] nodes;}
        [Serializable] private sealed class Rule
        {
            public string socket,parent,follow;
            public float[] pivot,target;
            public float angle,scaleX=1,scaleY=1,squashY=1;
        }
        private const int Width=1200,Height=760;
        public static void RunBatch()
        {
            int exit=0;float oldLight=Shader.GetGlobalFloat("_WorldLightEnabled");
            GameObject cameraObject=null,floor=null;Material floorMaterial=null;
            var output=Path.GetFullPath("../.reference/supine-study");Directory.CreateDirectory(output);
            try
            {
                var recipe=JsonConvert.DeserializeObject<Recipe>(File.ReadAllText(RecipePath));
                if(recipe?.schema!=1 || recipe.nodes?.Length!=17 || string.IsNullOrWhiteSpace(recipe.outfit))
                    throw new InvalidDataException("Поза на спине: нужны schema 1, комплект и 17 правил гнёзд.");
                WeaponCatalog.Load();Anatomy.Load();PonyVisual.Prepare();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                Shader.SetGlobalFloat("_WorldLightEnabled",0);
                cameraObject=new GameObject("Предпросмотр позы на спине");var camera=cameraObject.AddComponent<Camera>();
                camera.orthographic=true;camera.orthographicSize=.85f;camera.aspect=(float)Width/Height;
                camera.transform.position=new Vector3(0,.48f,-10);camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(.48f,.48f,.49f,1);
                floor=GameObject.CreatePrimitive(PrimitiveType.Quad);floor.name="Линия пола предпросмотра";
                floor.transform.position=new Vector3(0,-.014f,2);floor.transform.localScale=new Vector3(10,.012f,1);
                floorMaterial=WorldArtworkMaterial.Create(Texture2D.whiteTexture);
                floor.GetComponent<MeshRenderer>().sharedMaterial=floorMaterial;floor.GetComponent<MeshRenderer>().sortingOrder=-100;
                var floorColor=new MaterialPropertyBlock();floorColor.SetColor("_Color",new Color(.31f,.31f,.32f,1));
                floor.GetComponent<MeshRenderer>().SetPropertyBlock(floorColor);
                var report=new JObject { ["recipe"]=RecipePath,["scope"]="Editor-only own pose; existing meshes/textures, no new artwork or gameplay",["looks"]=new JArray()};
                Dictionary<string,Bounds> sourceBounds=null;Dictionary<string,Matrix4x4> deltas=null;
                foreach(bool worn in new[]{false,true})
                {
                    var actor=new ActorState {id="supine-study",actorType="pony",anatomy="pony",player=true,connected=true,weapon=null,pose="idle",facing=1,
                        appearance=new CharacterAppearance {version=CharacterAppearance.CurrentVersion,hair=0xA34D08,eyes=0x35BD37}};
                    if(worn)
                    {
                        var item=SharedInventory.Create(recipe.outfit,1);InventoryStacks.Add(actor,item);
                        if(!CosmeticEquipment.TryEquip(actor,item.id,false,out var reason))throw new InvalidOperationException(reason);
                    }
                    var view=(PonyVisual)ActorVisualFactory.Create(actor);
                    try
                    {
                        view.Bind(actor);view.DrawPose(0,true);
                        var parts=view.CurrentDrawPlan.Frames("idle")[0];var frame=PonyRigDrawPlan.Prepare(view.CurrentDrawPlan).Get("idle",0);
                        if(!worn)
                        {
                            sourceBounds=NodeBounds(parts,frame);deltas=BuildPose(recipe,sourceBounds);
                            var nodes=new JArray();foreach(var node in frame.Nodes)
                                nodes.Add(new JObject { ["socket"]=node.socket,["sourceMin"]=sourceBounds.TryGetValue(node.socket,out var box)?Vec(box.min):null,["sourceMax"]=sourceBounds.ContainsKey(node.socket)?Vec(box.max):null,
                                    ["matrix"]=new JArray(Enumerable.Range(0,16).Select(i=>(double)deltas[node.socket][i]))});
                            report["nodes"]=nodes;
                        }
                        if(deltas==null)throw new InvalidOperationException("Нет подготовленной позы.");
                        var objects=new SerializedObject(view);var outer=objects.FindProperty("rigOuter");var inner=objects.FindProperty("rigInner");
                        Bounds bounds=default;bool any=false;
                        for(int i=0;i<frame.Nodes.Length;i++)
                        {
                            var node=frame.Nodes[i];var matrix=deltas[node.socket]*node.transform.Matrix;
                            var trs=PonyRigTransform.FromMatrix(matrix);
                            var a=(Transform)outer.GetArrayElementAtIndex(i).objectReferenceValue;var b=(Transform)inner.GetArrayElementAtIndex(i).objectReferenceValue;
                            if(a==null || b==null)throw new InvalidDataException("Нет affine-пары гнезда "+node.socket);
                            a.localPosition=trs.position;a.localRotation=trs.rotation;a.localScale=trs.scale;b.localRotation=trs.innerRotation;
                        }
                        for(int i=0;i<parts.Length;i++)if(parts[i].visible)
                        {
                            var node=frame.Nodes[frame.PartNodes[i]];
                            var box=PonyRigDrawPlan.TransformBounds(parts[i].mesh.bounds,deltas[node.socket]*node.transform.Matrix);
                            if(!any){bounds=box;any=true;}else bounds.Encapsulate(box);
                        }
                        if(!any || !float.IsFinite(bounds.min.y))throw new InvalidDataException("Поза не имеет конечных границ.");
                        // Mesh AABBs include transparent texture padding. Measure the actual rendered silhouette,
                        // otherwise its invisible corners make the character hover over the guide line.
                        floor.SetActive(false);float bottom=Capture(camera,null);floor.SetActive(true);
                        float groundShift=-bottom+.003f;view.transform.position=Vector3.up*groundShift;
                        string file=worn?"armored.png":"bare.png";Capture(camera,Path.Combine(output,file));
                        ((JArray)report["looks"]).Add(new JObject { ["outfit"]=worn?recipe.outfit:"bare",["file"]=file,["parts"]=parts.Length,
                            ["boundsMin"]=Vec(bounds.min),["boundsMax"]=Vec(bounds.max),["groundShift"]=groundShift});
                    }
                    finally {Object.DestroyImmediate(view.gameObject);}
                }
                File.WriteAllText(Path.Combine(output,"report.json"),report.ToString(Formatting.Indented));
                File.WriteAllText(Path.Combine(output,"index.html"),"<!doctype html><meta charset='utf-8'><title>Поза на спине</title><style>body{background:#262a30;color:#eee;font:18px sans-serif;margin:24px}img{width:100%;max-width:1200px}p{max-width:900px}</style><h1>Черновая поза на спине</h1><p>Собрана из существующих17гнёзд и рисунков. Броня следует тем же преобразованиям. Это отдельный предпросмотр, в игру поза не подключена.</p><h2>Боевая броня</h2><img src='armored.png'><h2>Без костюма</h2><img src='bare.png'>");
                Debug.Log("Поза на спине: подготовлены 17 гнёзд, два варианта, исходная графика не изменена; "+output);
            }
            catch(Exception error){Debug.LogException(error);exit=1;}
            finally
            {
                Shader.SetGlobalFloat("_WorldLightEnabled",oldLight);
                foreach(var item in new Object[]{cameraObject,floor,floorMaterial})if(item!=null)Object.DestroyImmediate(item);
            }
            EditorApplication.Exit(exit);
        }
        private static Dictionary<string,Bounds> NodeBounds(PonyRenderPart[] parts,PonyRigDrawPlan.Frame frame)
        {
            var boxes=new Dictionary<string,Bounds>(StringComparer.Ordinal);
            for(int i=0;i<parts.Length;i++)
            {
                var part=parts[i];if(!part.visible || part.slot=="magic")continue;
                var node=frame.Nodes[frame.PartNodes[i]];var box=PonyRigDrawPlan.TransformBounds(part.mesh.bounds,node.transform.Matrix);
                if(boxes.TryGetValue(node.socket,out var previous)){previous.Encapsulate(box);boxes[node.socket]=previous;}else boxes.Add(node.socket,box);
            }
            return boxes;
        }
        private static Dictionary<string,Matrix4x4> BuildPose(Recipe recipe,Dictionary<string,Bounds> boxes)
        {
            var rules=recipe.nodes.ToDictionary(n=>n.socket,StringComparer.Ordinal);var result=new Dictionary<string,Matrix4x4>(StringComparer.Ordinal);
            var resolving=new HashSet<string>(StringComparer.Ordinal);
            foreach(string socket in rules.Keys)Resolve(socket);return result;
            Matrix4x4 Resolve(string socket)
            {
                if(result.TryGetValue(socket,out var done))return done;
                if(!rules.TryGetValue(socket,out var rule) || !resolving.Add(socket))throw new InvalidDataException("Неизвестное/циклическое гнездо: "+socket);
                Matrix4x4 matrix;
                if(!string.IsNullOrEmpty(rule.follow))matrix=Resolve(rule.follow);
                else
                {
                    if(rule.pivot?.Length!=2 || rule.scaleX<=0 || rule.scaleY<=0 || rule.squashY<=0)throw new InvalidDataException("Нет pivot/scale: "+socket);
                    if(!boxes.TryGetValue(socket,out var bounds))throw new InvalidDataException("Нет исходного рисунка для pivot: "+socket+"; доступны: "+string.Join(", ",boxes.Keys));
                    var pivot=new Vector3(Mathf.LerpUnclamped(bounds.min.x,bounds.max.x,rule.pivot[0]),Mathf.LerpUnclamped(bounds.min.y,bounds.max.y,rule.pivot[1]),0);
                    var rotate=Matrix4x4.Scale(new Vector3(1,rule.squashY,1))*Matrix4x4.TRS(Vector3.zero,Quaternion.Euler(0,0,rule.angle),new Vector3(rule.scaleX,rule.scaleY,1));
                    if(!string.IsNullOrEmpty(rule.parent))matrix=Resolve(rule.parent)*Matrix4x4.Translate(pivot)*rotate*Matrix4x4.Translate(-pivot);
                    else
                    {
                        if(rule.target?.Length!=2)throw new InvalidDataException("Нет target: "+socket);
                        matrix=Matrix4x4.Translate(new Vector3(rule.target[0],rule.target[1],0))*rotate*Matrix4x4.Translate(-pivot);
                    }
                }
                for(int i=0;i<16;i++)if(!float.IsFinite(matrix[i]))throw new InvalidDataException("Неконечная матрица: "+socket);
                if(Math.Abs(matrix.determinant)<1e-9)throw new InvalidDataException("Вырожденное гнездо: "+socket);
                resolving.Remove(socket);result.Add(socket,matrix);return matrix;
            }
        }
        private static JArray Vec(Vector3 value)=>new(value.x,value.y,value.z);
        private static float Capture(Camera camera,string path)
        {
            var target=new RenderTexture(Width,Height,24);var image=new Texture2D(Width,Height,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
            try
            {
                camera.targetTexture=target;camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,Width,Height),0,0);image.Apply();
                if(path!=null){File.WriteAllBytes(path,image.EncodeToPNG());return 0;}
                var pixels=image.GetPixels();var background=pixels[0];
                for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
                {
                    var pixel=pixels[y*Width+x];
                    if(Mathf.Max(Mathf.Abs(pixel.r-background.r),Mathf.Abs(pixel.g-background.g),Mathf.Abs(pixel.b-background.b))<.04f)continue;
                    if(y==0 || x==0 || x==Width-1)throw new InvalidDataException("Поза обрезана камерой; нельзя определить опору.");
                    return camera.transform.position.y-camera.orthographicSize+(y+.5f)/Height*camera.orthographicSize*2;
                }
                throw new InvalidDataException("Пустой предпросмотр: опора не найдена.");
            }
            finally {camera.targetTexture=null;RenderTexture.active=previous;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);}
        }
    }
}
