using System.IO;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class WeaponArtworkChecks
    {
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            PonyVisual.Prepare();Remains.Content.WeaponRigs.Load();int index=0;
            foreach(string id in new[]{"spear","crowbar","pistol","energy","grenade"})
            {
                var actor=new ActorState {id=id,x=index*3,pose="idle"};
                var pony=new GameObject(id+" пони").AddComponent<PonyVisual>();pony.Bind(actor);pony.DrawPose(0);
                var weapon=Object.Instantiate(Remains.Content.WeaponRigs.Prefab(id));weapon.name=id;
                weapon.transform.position=actor.Position+new Vector2(.8f,.8f);
                index++;
            }
            UnityEngine.Rendering.SortingGroup.UpdateAllSortingGroups();
            var camera=new GameObject("Камера оружия").AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=2;
            camera.transform.position=new Vector3(6.3f,.8f,-10);camera.backgroundColor=new Color(.16f,.17f,.18f);
            var target=new RenderTexture(1920,480,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(1920,480,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,1920,480),0,0);image.Apply();
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/weapon-artwork.png"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,image.EncodeToPNG());
                Debug.Log("Размеры оружия относительно пони: "+path);
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);}
        }
    }
}
