using System;
using Remains.Content;
using Remains.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    [InitializeOnLoad]
    public static class RoomLightingPreview
    {
        private static GameObject preview;
        static RoomLightingPreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload+=Clear;
            EditorApplication.playModeStateChanged+=_=>Clear();
            EditorSceneManager.sceneClosing+=(_,__)=>Clear();
            PrefabStage.prefabStageClosing+=_=>Clear();
            PrefabStage.prefabStageOpened+=stage=>
            {if(!Application.isBatchMode && stage.prefabContentsRoot.TryGetComponent<RoomAuthoring>(out var room)) Refresh(room);};
            EditorSceneManager.sceneOpened+=(scene,mode)=>
            {
                if(Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
                foreach(var root in scene.GetRootGameObjects()) if(root.TryGetComponent<RoomAuthoring>(out var room)) {Refresh(room);break;}
            };
        }
        public static void Refresh(RoomAuthoring room)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Предпросмотр света доступен только вне Play.");
            Clear();
            try
            {
                var lighting=room.GetComponent<RoomLightingAuthoring>();
                if(lighting==null) throw new InvalidOperationException("Нет настроек освещения у "+room.name);
                preview=EditorUtility.CreateGameObjectWithHideFlags("Предпросмотр света Remains",HideFlags.HideAndDontSave,typeof(RoomLighting));
                preview.GetComponent<RoomLighting>().Initialize(room.CreateLayout(),lighting.CreateProfile(room));
                SceneView.RepaintAll();
            }
            catch {Clear();throw;}
        }
        [MenuItem("Remains/Освещение/Обновить для выбранного уровня")]
        private static void RefreshSelected()
        {
            var room=Selection.activeGameObject!=null?Selection.activeGameObject.GetComponentInParent<RoomAuthoring>():null;
            if(room==null) {Debug.LogWarning("Выберите корень уровня или его дочерний объект для предпросмотра света.");return;}
            Refresh(room);
        }
        [MenuItem("Remains/Освещение/Выключить предпросмотр")]
        public static void Clear()
        {
            if(preview!=null) {preview.GetComponent<RoomLighting>().Release();UnityEngine.Object.DestroyImmediate(preview);}
            preview=null;SceneView.RepaintAll();
        }
    }
}
