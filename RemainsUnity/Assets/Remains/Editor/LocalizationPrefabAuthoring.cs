using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Editor
{
    // Static captions only. Any Text exposed by a View remains owned by its runtime writer.
    public static class LocalizationPrefabAuthoring
    {
        public const string Folder="Assets/Remains/Resources/Presentation";

        public static JObject[] Collect()
        {
            var records=new Dictionary<string,JObject>(StringComparer.Ordinal);
            int prefabs=0,captions=0,bindings=0,dynamic=0,input=0;
            foreach(string path in AssetDatabase.FindAssets("t:Prefab",new[]{Folder}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(x=>x,StringComparer.Ordinal))
            {
                var assetRoot=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if(assetRoot==null)throw new InvalidOperationException("Не загружен исходный префаб подписей: "+path);
                var root=PrefabUtility.LoadPrefabContents(path);
                bool changed=false;
                try
                {
                    prefabs++;
                    var dynamicTexts=new HashSet<Text>();
                    foreach(var view in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if(view==null || !view.GetType().Name.EndsWith("View",StringComparison.Ordinal))continue;
                        using var serialized=new SerializedObject(view);
                        var property=serialized.GetIterator();
                        while(property.NextVisible(true))
                            if(property.propertyType==SerializedPropertyType.ObjectReference && property.objectReferenceValue is Text text)
                                dynamicTexts.Add(text);
                    }
                    var inputTexts=root.GetComponentsInChildren<InputField>(true).Where(field=>field.textComponent!=null).Select(field=>field.textComponent).ToHashSet();
                    foreach(var text in root.GetComponentsInChildren<Text>(true))
                    {
                        if(inputTexts.Contains(text)){input++;continue;}
                        if(dynamicTexts.Contains(text)){dynamic++;continue;}
                        if(!HasRussian(text.text))continue;
                        var source=PersistentText(root.transform,assetRoot.transform,text);
                        if(source==null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source,out string _,out long fileId) || fileId==0)
                            throw new InvalidOperationException("Не найден устойчивый ID подписи префаба: "+path+", text="+text.name);
                        string key="prefab."+AssetDatabase.AssetPathToGUID(path)+"."+fileId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        if(records.TryGetValue(key,out var previous) && (string)previous["russian"]!=text.text)
                            throw new InvalidOperationException("У разных подписей совпал ID: "+key+", prefab="+path+", text="+text.name);
                        records[key]=new JObject{["key"]=key,["russian"]=text.text,["kind"]="text",["registered"]=true};
                        captions++;
                        var binding=text.GetComponent<LocalizedUiText>();
                        if(binding==null){binding=text.gameObject.AddComponent<LocalizedUiText>();changed=true;bindings++;}
                        if(binding.SourceKey!=key || binding.RussianSource!=text.text)
                        {binding.SetSource(key,text.text);EditorUtility.SetDirty(binding);changed=true;}
                    }
                    if(changed)PrefabUtility.SaveAsPrefabAsset(root,path);
                }
                finally {PrefabUtility.UnloadPrefabContents(root);}
            }
            Debug.Log($"Локализация префабов: префабов={prefabs}, статических подписей={captions}, новых привязок={bindings}, динамических Text исключено={dynamic}, полей ввода исключено={input}.");
            return records.Values.ToArray();
        }
        private static Text PersistentText(Transform contentRoot,Transform assetRoot,Text text)
        {
            // Prefab contents are isolated scene objects: source links are not guaranteed there.
            // Correlate with the persistent asset by the exact sibling/component index path.
            var path=new Stack<int>();
            for(var current=text.transform;current!=contentRoot;current=current.parent)
            {
                if(current==null)throw new InvalidOperationException("Подпись находится вне загруженного префаба: "+text.name);
                path.Push(current.GetSiblingIndex());
            }
            var persistent=assetRoot;
            while(path.Count>0)
            {
                int index=path.Pop();
                if(index>=persistent.childCount)throw new InvalidOperationException("Структура префаба не совпала при поиске ID подписи: "+text.name);
                persistent=persistent.GetChild(index);
            }
            int component=Array.IndexOf(text.GetComponents<Text>(),text);
            var candidates=persistent.GetComponents<Text>();
            if(component<0 || component>=candidates.Length)throw new InvalidOperationException("Компоненты подписи префаба не совпали: "+text.name);
            return candidates[component];
        }
        private static bool HasRussian(string text)=>!string.IsNullOrWhiteSpace(text) && text.Any(c=>c>='А' && c<='я' || c is 'ё' or 'Ё');
    }
}
