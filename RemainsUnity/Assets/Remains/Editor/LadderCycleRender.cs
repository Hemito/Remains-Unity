using System;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    // Native-resolution evidence of every authored ladder with the actual controller and pose sampler.
    public static class LadderCycleRender
    {
        public static void Run()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Anatomy.Load();WeaponCatalog.Load();PonyAnimationData.Load();PonyVisual.Prepare();
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/ladder-cycles"));
            Directory.CreateDirectory(directory);
            var camera=new GameObject("Камера цикла лестницы").AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=1.8f;
            var target=new RenderTexture(960,540,24);target.Create();camera.targetTexture=target;
            var previous=RenderTexture.active;
            try
            {
                foreach(string level in new[]{"hub","factory:0"})
                {
                    var layout=RoomLayout.Load(level);var room=AuthoredRoomVisual.Create(level,layout);
                    try
                    {
                        for(int index=0;index<layout.ladders.Length;index++)
                            foreach(int direction in new[]{1,-1})
                            {
                                var ladder=layout.ladders[index];
                                var actor=new ActorState {id="ladder-cycle",x=ladder.x+ladder.movementOffset,
                                    y=ladder.bottom+(ladder.top-ladder.bottom)*(direction>0?.25f:.75f),facing=-(int)ladder.facing};
                                var motion=new MovementController(layout);
                                var visual=new GameObject(actor.id).AddComponent<PonyVisual>();visual.Bind(actor);
                                var sheet=new Texture2D(3840,2160,TextureFormat.RGB24,false);
                                var frames=new int[13];
                                try
                                {
                                    // Acquire in the air with Up before observing either direction of the cycle.
                                    motion.Step(actor,Vector2.up,false,1f/30);
                                    for(int frame=0;frame<13;frame++)
                                    {
                                        if(frame==0) motion.Step(actor,Vector2.zero,false,1f/30);
                                        else {motion.Step(actor,Vector2.up*direction,false,1f/30);ActorPose.Advance(actor,1f/30);}
                                        if(!actor.climbing || actor.facing!=(int)ladder.facing)
                                            throw new InvalidOperationException($"Потерян хват/сторона: {level}, лестница={index}, направление={direction}, кадр={frame}.");
                                        visual.DrawPose(actor.poseTime);frames[frame]=visual.DrawnFrame;
                                        camera.transform.position=new Vector3(actor.x,actor.y+.9f,-10);
                                        camera.Render();RenderTexture.active=target;
                                        sheet.ReadPixels(new Rect(0,0,960,540),frame%4*960,(3-frame/4)*540);
                                    }
                                    sheet.Apply();
                                    string name=$"{level.Replace(':','-')}-{index}-{(direction>0?"up":"down")}";
                                    File.WriteAllBytes(Path.Combine(directory,name+".png"),sheet.EncodeToPNG());
                                    File.WriteAllText(Path.Combine(directory,name+".txt"),"Ячейки слева направо, сверху вниз; первая — неподвижный хват. Кадры: "+string.Join(",",frames));
                                }
                                finally {UnityEngine.Object.DestroyImmediate(sheet);UnityEngine.Object.DestroyImmediate(visual.gameObject);}
                            }
                    }
                    finally {UnityEngine.Object.DestroyImmediate(room);}
                }
            }
            finally
            {
                RenderTexture.active=previous;camera.targetTexture=null;target.Release();
                UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(camera.gameObject);
            }
        }
    }
}
