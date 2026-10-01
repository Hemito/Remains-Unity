using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class CrouchArtworkChecks
    {
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Anatomy.Load();PonyVisual.Prepare();
            var crouch=Array.Find(PonyAnimationData.Library.clips,c=>c.id=="crouch");
            var crawl=Array.Find(PonyAnimationData.Library.clips,c=>c.id=="crawl");
            if(crouch?.frames.Length!=1 || crawl?.frames.Length!=14) throw new InvalidOperationException("Неверное количество кадров приседания/ползания.");
            string[] poses={"idle","crouch","crawl","crawl"};
            for(int i=0;i<poses.Length;i++)
            {
                var actor=new ActorState {id="crouch-art-"+i,x=i*3,pose=poses[i],poseTime=i==3?.2f:0};
                var view=new GameObject(poses[i]).AddComponent<PonyVisual>();view.Bind(actor);view.DrawPose(actor.poseTime);
            }
            var camera=new GameObject("Камера поз").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=2;
            camera.transform.position=new Vector3(4.5f,.8f,-10);camera.aspect=3;camera.backgroundColor=new Color(.18f,.2f,.21f);
            var target=new RenderTexture(1500,500,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(1500,500,TextureFormat.RGBA32,false);
            try
            {
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1500,500),0,0);image.Apply();
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/crouch-artwork.png"));
                File.WriteAllBytes(path,image.EncodeToPNG());Debug.Log("Графика приседания: PASS — 1/14 кадров; "+path);
            }
            finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
