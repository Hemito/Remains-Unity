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
            PonyRenderCompiler.Build();PrepareCompiled();
        }
        public static void PrepareCompiled()
        {
            WeaponCatalog.Load();PonyRenderSelection.ResetPrepared();PonyVisual.Prepare();ActorTypes.Load();
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
                view.PortraitParts=PreparePortraitRoot(view.Portrait);view.Validate();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        public static PortraitPart[] PreparePortraitRoot(GameObject portrait)
        {
                var actor=new Domain.ActorState();var plan=PonyRenderSelection.PrepareEquipment(actor);
                var parts=plan.PortraitFrame;var bounds=plan.FrameBounds("idle",0);var appearance=PonyAppearance.Snapshot(actor);
                var saved=portrait.GetComponentsInChildren<PortraitPart>(true).ToList();
                int capacity=Math.Max(saved.Count,PonyRenderSelection.Capacity);
                for(int i=0;i<capacity;i++)
                {
                    var part=parts[i<parts.Length?i:0];PortraitPart graphic;
                    if(i<saved.Count) graphic=saved[i];
                    else
                    {
                        var child=new GameObject("Часть портрета "+i,typeof(RectTransform));child.transform.SetParent(portrait.transform,false);
                        graphic=child.AddComponent<PortraitPart>();saved.Add(graphic);
                    }
                    graphic.name="Часть портрета "+i;
                    graphic.transform.SetSiblingIndex(i);
                    var rect=graphic.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=Vector2.zero;rect.offsetMax=Vector2.zero;
                    graphic.ConfigurePrepared(part,appearance,bounds);if(i>=parts.Length) graphic.gameObject.SetActive(false);
                }
                return saved.ToArray();
        }
    }
}
