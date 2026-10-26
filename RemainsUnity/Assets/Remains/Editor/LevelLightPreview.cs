using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Frames of the seed104 factory in the game's own pipeline with the level light (FactoryLevelView → LevelLightView),
    // for a look by eye: a few rooms, each with all lamps, one door opened and one lamp switched off.
    //   batch -executeMethod Remains.Editor.LevelLightPreview.RenderBatch → .reference/level-light/room-*.png
    public static class LevelLightPreview
    {
        private const int ImageWidth=1280,ImageHeight=672;
        public static void RenderBatch()
        {
            GameObject visual=null,cameraRoot=null;
            try
            {
                Anatomy.Load();WeaponCatalog.Load();
                var world=new WorldState{seed=104};
                FactoryPreparation.Create(world,AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath),0);
                FactoryContentSources.Prepare(world,"factory:0");
                var authoring=RoomAuthoring.Load("factory:0",world);
                DoorContentInstaller.Apply(world,RoomDoorContent.Read(authoring,"factory:0"));
                var room=authoring.CreateLayout(false);
                visual=AuthoredRoomVisual.Create("factory:0",room,world);
                var view=visual.GetComponent<FactoryLevelView>();var factory=visual.GetComponent<FactoryLevelRoot>();
                view.Synchronize(room,world,1);
                view.Focus(new Vector2(room.spawnX,room.spawnY));
                cameraRoot=new GameObject("Камера кадров света");var camera=cameraRoot.AddComponent<Camera>();
                camera.enabled=false;camera.orthographic=true;camera.aspect=(float)ImageWidth/ImageHeight;camera.allowHDR=false;camera.allowMSAA=false;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.07f,.07f,.08f);
                string output=Path.GetFullPath("../.reference/level-light");Directory.CreateDirectory(output);
                var grid=factory.Prepared.Grid;float cell=(float)grid.Level.Cell;
                var doors=world.doors.Where(d=>d.level=="factory:0").ToArray();
                long revision=1;
                foreach(var (x,y,name) in new[]{(1,1,"hall"),(1,9,"office"),(2,11,"door"),(0,0,"entry")})
                {
                    var placement=grid.Rooms.Single(r=>r.Cell.X==x && r.Cell.Y==y);
                    var bounds=new Rect(placement.Column*cell,(grid.Level.Height-placement.Row-placement.Template.Height)*cell,placement.Template.Width*cell,placement.Template.Height*cell);
                    camera.transform.position=new Vector3(bounds.center.x,bounds.center.y,-10);
                    camera.orthographicSize=Mathf.Max(bounds.height*.5f,bounds.width/(2*camera.aspect));
                    float height=2*camera.orthographicSize;
                    view.SetViewArea(new Rect(bounds.center.x-height*camera.aspect*.5f,bounds.center.y-height*.5f,height*camera.aspect,height));
                    view.Focus(bounds.center);
                    Save(view,camera,Path.Combine(output,$"room-{name}-all.png"));
                    string prefix=$"factory:0:{x},{y}:";
                    var door=doors.Where(d=>d.id.StartsWith(prefix,StringComparison.Ordinal) && d.Blocks).OrderBy(d=>d.id,StringComparer.Ordinal).FirstOrDefault();
                    if(door!=null)
                    {
                        door.open=true;view.SyncDoors(doors,++revision);
                        Save(view,camera,Path.Combine(output,$"room-{name}-door-open.png"));
                        door.open=false;view.SyncDoors(doors,++revision);
                    }
                    string lamp=view.Light.Sources.All.Where(s=>s.Kind==LevelLightSourceKind.Lamp && s.Enabled && s.Id.StartsWith(prefix,StringComparison.Ordinal))
                        .Select(s=>s.Id).OrderBy(id=>id,StringComparer.Ordinal).FirstOrDefault();
                    if(lamp!=null)
                    {
                        world.lampRevision++;view.SyncLamps(new[]{lamp},world.lampRevision);
                        Save(view,camera,Path.Combine(output,$"room-{name}-lamp-off.png"));
                        world.lampRevision++;view.SyncLamps(Array.Empty<string>(),world.lampRevision);
                    }
                    Debug.Log($"Кадры света: {name} ({x},{y}) {placement.Template.Template}; дверь {door?.id ?? "нет"}, лампа {lamp ?? "нет"}.");
                }
                Debug.Log("LevelLightPreview: кадры сохранены — "+output);
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
            finally
            {
                foreach(var obj in new Object[]{visual,cameraRoot}) if(obj!=null) Object.DestroyImmediate(obj);
                FactoryContentSources.Clear();
            }
        }
        private static void Save(FactoryLevelView view,Camera camera,string path)
        {
            view.RenderMuzzleFlashes(Array.Empty<SpriteEmission>(),Array.Empty<ProjectedLightShadow>(),Array.Empty<string>());
            var target=RenderTexture.GetTemporary(ImageWidth,ImageHeight,24,RenderTextureFormat.ARGB32);var previous=RenderTexture.active;
            var image=new Texture2D(ImageWidth,ImageHeight,TextureFormat.RGBA32,false);
            try
            {
                camera.targetTexture=target;camera.Render();camera.Render();
                RenderTexture.active=target;image.ReadPixels(new Rect(0,0,ImageWidth,ImageHeight),0,0);image.Apply();
                File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally {camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(image);RenderTexture.ReleaseTemporary(target);}
        }
    }
}
