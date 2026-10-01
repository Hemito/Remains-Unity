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
            PonyVisual.Prepare();var weapons=WeaponArtwork.Load();int index=0;
            foreach(string id in new[]{"spear","crowbar","pistol","energy"})
            {
                var actor=new ActorState {id=id,x=index*3,pose="idle"};
                var pony=new GameObject(id+" пони").AddComponent<PonyVisual>();pony.Bind(actor);pony.DrawPose(0);
                var weapon=new GameObject(id).AddComponent<SpriteRenderer>();SpriteArtworkMaterial.Apply(weapon,weapons[id]);weapon.sortingOrder=100;
                weapon.transform.position=actor.Position+new Vector2(.8f,.8f);
                index++;
            }
            UnityEngine.Rendering.SortingGroup.UpdateAllSortingGroups();
            var camera=new GameObject("Камера оружия").AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=2;
            camera.transform.position=new Vector3(4.8f,.8f,-10);camera.backgroundColor=new Color(.16f,.17f,.18f);
            var target=new RenderTexture(1440,480,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(1440,480,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,1440,480),0,0);image.Apply();
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/weapon-artwork.png"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,image.EncodeToPNG());
                Debug.Log("Размеры оружия относительно пони: "+path);
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(image);}
        }
    }
}
