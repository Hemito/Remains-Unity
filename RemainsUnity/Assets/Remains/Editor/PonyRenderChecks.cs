using System;
using Remains.Content;
using UnityEditor;
using UnityEngine;
namespace Remains.Editor
{
    public static class PonyRenderChecks
    {
        public static void Run()
        {
            var asset=Resources.Load<PonyRenderAsset>("Original/Pony/render");asset.Validate();PonyAnimationData.Load();
            var source=PonyAnimationData.Library;
            if(asset.Clips.Length!=source.clips.Length) throw new InvalidOperationException("Количество сохранённых поз пони расходится с исходными данными.");
            int count=0;
            for(int c=0;c<source.clips.Length;c++)
            {
                var actual=asset.Clips[c];var expected=source.clips[c];
                if(actual.id!=expected.id || actual.frames.Length!=expected.frames.Length) throw new InvalidOperationException("Не совпали кадры позы: "+expected.id);
                for(int f=0;f<actual.frames.Length;f++)
                {
                    var frame=actual.frames[f];var parts=expected.frames[f].parts;
                    if(frame.parts.Length!=parts.Length) throw new InvalidOperationException("Не совпало число частей: "+actual.id);
                    for(int p=0;p<parts.Length;p++)
                    {
                        var part=frame.parts[p];var vertices=PonyAnimationData.Quad(parts[p]);var saved=part.mesh.vertices;
                        if(!AssetDatabase.Contains(part.mesh) || !AssetDatabase.Contains(part.material) || saved.Length!=4 || part.slot!=parts[p].slot || part.visible==parts[p].path.StartsWith("pip2/") ||
                            part.hairStyle!=parts[p].hairStyle || part.eyeStyle!=parts[p].eyeStyle || part.drawOrder!=parts[p].drawOrder)
                            throw new InvalidOperationException($"Некорректная сохранённая часть: {actual.id}:{f}:{p}.");
                        for(int v=0;v<4;v++) if(Vector3.Distance(saved[v],vertices[v])>.000001f)
                            throw new InvalidOperationException($"Сместилась геометрия: {actual.id}:{f}:{p}:{v}.");
                        count++;
                    }
                }
            }
            Debug.Log($"Сохранённая графика пони: PASS — частей {count}, геометрия, постоянные ссылки и состав кадров.");
        }
    }
    [CustomEditor(typeof(PonyRenderAsset))]
    public sealed class PonyRenderAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Готовая графика, собранная из исходных данных анимации пони. Игра загружает сохранённые меши и материалы; изменения исходных данных требуют пересборки.",MessageType.Info);
            var asset=(PonyRenderAsset)target;EditorGUILayout.LabelField("Позы",(asset.Clips?.Length??0).ToString());
            if(GUILayout.Button("Пересобрать графику пони")) PonyRenderCompiler.Build();
        }
    }
}
