using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class JumpPoseRender
    {
        public static void Render()=>Render(false,"jump");
        public static void RenderMotion()=>Render(true,"jump");
        public static void RenderLanding()=>Render(false,"landing");
        public static void RenderFall()=>Render(false,"fall");
        private static void Render(bool motion,string pose)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            PonyVisual.Prepare();
            var clip=System.Array.Find(PonyAnimationData.Library.clips,c=>c.id==pose);
            float[] velocities={7.2f,4.8f,2.4f,0,-2.4f,-4.8f,-7.2f,-12};
            int count=motion?velocities.Length:clip.frames.Length;
            int columns=8,rows=Mathf.CeilToInt(count/(float)columns);
            for(int i=0;i<count;i++)
            {
                var actor=new ActorState {id=pose+"-frame-"+i,pose=pose,x=(i%columns)*3,y=-(i/columns)*3};
                var visual=new GameObject(actor.id).AddComponent<PonyVisual>();
                if(motion) actor.vy=velocities[i];
                visual.Bind(actor);visual.DrawPose((i+.01f)/clip.fps,!motion);
                var label=new GameObject("Номер кадра").AddComponent<TextMesh>();
                label.text=motion?$"{actor.vy:0.0} м/с":(i+1).ToString();label.fontSize=48;label.characterSize=.08f;
                label.transform.position=new Vector3(actor.x-1,actor.y-.1f,-1);
            }
            UnityEngine.Rendering.SortingGroup.UpdateAllSortingGroups();
            var camera=new GameObject("Библиотека поз прыжка").AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=rows*1.5f;
            camera.transform.position=new Vector3((columns-1)*1.5f,1-(rows-1)*1.5f,-10);
            camera.backgroundColor=new Color(.13f,.15f,.17f);
            var target=new RenderTexture(columns*240,rows*240,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            var previous=RenderTexture.active;
            try
            {
                camera.Render();RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../..",motion?".reference/tests/jump-motion-poses.png":$".reference/tests/{pose}-pose-library.png"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,image.EncodeToPNG());
                Debug.Log($"Библиотека прыжка: {clip.frames.Length} кадров, {path}");
            }
            finally
            {
                RenderTexture.active=previous;camera.targetTexture=null;target.Release();
                Object.DestroyImmediate(image);Object.DestroyImmediate(target);
            }
        }
    }
}
