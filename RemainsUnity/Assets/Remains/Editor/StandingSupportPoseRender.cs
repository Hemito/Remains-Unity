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
    // Synthetic visual evidence. The room used by the controller also defines the drawn floor.
    public static class StandingSupportPoseRender
    {
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Anatomy.Load();PonyVisual.Prepare();StandingEdgePoseCatalog.Load();
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/standing-support-render"));
            Directory.CreateDirectory(directory);
            var camera=new GameObject("Камера проверки краевых поз").AddComponent<Camera>();
            camera.orthographic=true;camera.orthographicSize=2.5f;
            camera.transform.position=new Vector3(.3f,.9f,-10);
            camera.backgroundColor=new Color(.10f,.13f,.15f);
            var target=new RenderTexture(1920,1080,24);target.Create();camera.targetTexture=target;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            var floorSprite=Sprite.Create(Texture2D.whiteTexture,new Rect(0,0,Texture2D.whiteTexture.width,Texture2D.whiteTexture.height),new Vector2(.5f,.5f),Texture2D.whiteTexture.width);
            var previous=RenderTexture.active;float previousLighting=Shader.GetGlobalFloat("_WorldLightEnabled");
            try
            {
                Shader.SetGlobalFloat("_WorldLightEnabled",0);
                foreach(bool crouched in new[]{false,true}) foreach(int facing in new[]{-1,1}) foreach(bool edge in new[]{false,true})
                {
                    string name=$"{(crouched?"crouch":"stand")}-{facing}-{(edge?"edge":"floor")}";
                    var room=new RoomLayout {id="standing-render",left=-4,right=4,bottom=-1,top=5,
                        platforms=new[]{new RoomSurface {x=-4,y=-1,width=edge?4:8,height=1}},ladders=Array.Empty<RoomLadder>()};
                    var actor=new ActorState {id=name,player=true,connected=true,x=.45f,y=0,facing=facing,crouched=crouched,pose=crouched?"crouch":"idle"};
                    var visual=new GameObject(name).AddComponent<PonyVisual>();
                    var floor=new GameObject("Опора из той же геометрии").AddComponent<SpriteRenderer>();
                    var label=new GameObject("Условия проверки").AddComponent<TextMesh>();
                    try
                    {
                        visual.Bind(actor);new MovementController(room).Step(actor,Vector2.zero,false,1f/60);visual.DrawPose(actor.poseTime);
                        if(actor.x!=.45f || actor.y!=0 || visual.DrawnClip!=actor.pose || visual.DrawnFrame!=0 ||
                            edge!=actor.pose.StartsWith("edge",StringComparison.Ordinal))
                            throw new InvalidOperationException($"Краевой рисунок не соответствует входам: {name}, pose={actor.pose}, drawn={visual.DrawnClip}/{visual.DrawnFrame}, body={actor.Position}.");
                        var surface=room.platforms[0];
                        floor.sprite=floorSprite;floor.color=new Color(.38f,.42f,.43f);
                        floor.transform.position=new Vector3(surface.x+surface.width*.5f,surface.y+surface.height*.5f,1);
                        floor.transform.localScale=new Vector3(surface.width,surface.height,1);floor.sortingOrder=-1;
                        label.text=$"Синтетическая опора · {(edge?"край":"сплошной пол")} · {(crouched?"приседание":"стойка")}\n{actor.pose} · направление {facing} · тело (0,45; 0)";
                        label.fontSize=42;label.characterSize=.045f;
                        label.transform.position=new Vector3(-3.7f,2.9f,-1);
                        UnityEngine.Rendering.SortingGroup.UpdateAllSortingGroups();
                        camera.Render();RenderTexture.active=target;
                        image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                        File.WriteAllBytes(Path.Combine(directory,name+".png"),image.EncodeToPNG());
                        File.WriteAllText(Path.Combine(directory,name+".json"),JsonUtility.ToJson(new Witness {actor=actor,
                            drawnClip=visual.DrawnClip,drawnFrame=visual.DrawnFrame,support=surface,width=target.width,height=target.height},true));
                    }
                    finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);UnityEngine.Object.DestroyImmediate(floor.gameObject);UnityEngine.Object.DestroyImmediate(label.gameObject);}
                }
                Debug.Log($"Краевые позы: восемь синтетических кадров 1920×1080 с фактическим контроллером и PonyVisual, {directory}. Обычный маршрут и оригинальный runtime этим не проверены.");
            }
            finally
            {
                Shader.SetGlobalFloat("_WorldLightEnabled",previousLighting);RenderTexture.active=previous;camera.targetTexture=null;
                target.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(floorSprite);UnityEngine.Object.DestroyImmediate(camera.gameObject);
            }
        }
        [Serializable] private sealed class Witness {public ActorState actor;public string drawnClip;public int drawnFrame,width,height;public RoomSurface support;}
    }
}
