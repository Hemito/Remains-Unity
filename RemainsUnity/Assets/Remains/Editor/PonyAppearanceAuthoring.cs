using System;
using System.Linq;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonyAppearanceAuthoring
    {
        [MenuItem("Remains/Подготовить варианты внешности")]
        public static void Build()
        {
            PonyRenderCompiler.Build();PonyVisual.Prepare();ActorTypes.Load();
            PrepareActor();PreparePortrait();
            AssetDatabase.SaveAssets();PonyAppearanceChecks.Run();
            Debug.Log("Варианты внешности подготовлены: сохранённые кадры, структура пони и UI-портрет.");
        }
        public static void BuildAndVerify() {Build();PrototypeBuild.Build();PonyAppearanceChecks.Render();}
        private static void PrepareActor()
        {
            if(!EntityAssetCatalog.Load().TryGetActorPrefab(ActorTypes.Get("pony"),out var prefab))
                throw new InvalidOperationException("Нет префаба пони для вариантов внешности.");
            string path=AssetDatabase.GetAssetPath(prefab);var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<PonyVisual>();var data=new SerializedObject(view);
                var filters=data.FindProperty("filters");var renderers=data.FindProperty("renderers");
                if(filters==null || renderers==null || filters.arraySize!=renderers.arraySize) throw new InvalidOperationException("Повреждены сохранённые части префаба пони.");
                for(int i=filters.arraySize;i<PonyRenderSelection.Capacity;i++)
                {
                    var child=new GameObject("Часть пони "+i);child.transform.SetParent(root.transform,false);
                    var filter=child.AddComponent<MeshFilter>();var renderer=child.AddComponent<MeshRenderer>();renderer.enabled=false;
                    filters.InsertArrayElementAtIndex(i);filters.GetArrayElementAtIndex(i).objectReferenceValue=filter;
                    renderers.InsertArrayElementAtIndex(i);renderers.GetArrayElementAtIndex(i).objectReferenceValue=renderer;
                }
                data.ApplyModifiedPropertiesWithoutUndo();view.UseAuthoredStructure();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        private static void PreparePortrait()
        {
            const string path="Assets/Remains/Resources/Presentation/PersonalInventory.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<PersonalInventoryView>();
                var parts=Array.Find(PonyAnimationData.Library.clips,clip=>clip.id=="idle").frames[0].parts.Where(part=>!part.path.StartsWith("pip2/")).OrderBy(part=>part.drawOrder).ToArray();
                Vector2 minimum=new(float.PositiveInfinity,float.PositiveInfinity),maximum=new(float.NegativeInfinity,float.NegativeInfinity);
                foreach(var part in parts) foreach(var point in PonyAnimationData.Quad(part)) {minimum=Vector2.Min(minimum,point);maximum=Vector2.Max(maximum,point);}
                var saved=view.Portrait.GetComponentsInChildren<PortraitPart>(true).ToList();
                for(int i=0;i<parts.Length;i++)
                {
                    var part=parts[i];PortraitPart graphic;
                    if(i<saved.Count) graphic=saved[i];
                    else
                    {
                        var child=new GameObject("Часть портрета "+i,typeof(RectTransform));child.transform.SetParent(view.Portrait.transform,false);
                        graphic=child.AddComponent<PortraitPart>();saved.Add(graphic);
                    }
                    graphic.name="Портрет · "+part.path+" · "+part.hairStyle+"/"+part.eyeStyle;
                    graphic.transform.SetSiblingIndex(i);
                    var rect=graphic.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=Vector2.zero;rect.offsetMax=Vector2.zero;
                    var texture=Resources.Load<Texture2D>("Original/Pony/"+part.texture);
                    if(texture==null) throw new InvalidOperationException("Нет графики портрета: "+part.texture);
                    graphic.Configure(texture,PonyAnimationData.Quad(part),minimum,maximum);
                    graphic.SetAppearance(part.slot,new Color(part.color[0],part.color[1],part.color[2],part.color[3]));
                    graphic.ConfigureSelection(part.hairStyle,part.eyeStyle);graphic.ApplyAppearance(PonyAppearance.Snapshot(new Domain.ActorState()));
                }
                for(int i=saved.Count-1;i>=parts.Length;i--) {UnityEngine.Object.DestroyImmediate(saved[i].gameObject);saved.RemoveAt(i);}
                view.PortraitParts=saved.ToArray();view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
    }
}
