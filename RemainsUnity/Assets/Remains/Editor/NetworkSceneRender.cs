using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Runtime;
using Remains.Serialization;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Remains.Editor
{
    public static class NetworkSceneRender
    {
        public static void Render()
        {
            var args=Environment.GetCommandLineArgs();int option=Array.IndexOf(args,"--network-scene-dir");
            if(option<0 || option+1>=args.Length) throw new ArgumentException("Нужен --network-scene-dir с записями клиентов.");
            var files=Directory.GetFiles(args[option+1],"*.scene.json");
            if(files.Length==0) throw new InvalidOperationException("Нет записанных сетевых кадров.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            WeaponCatalog.Load();PonyVisual.Prepare();
            foreach(string file in files)
            {
                var record=StateJson.FromJson<NetworkSceneRecord>(File.ReadAllText(file));
                if(record.schema is not (1 or 2) || record.width<=0 || record.height<=0) throw new InvalidDataException($"Некорректная запись: {file}.");
                var expected=record.world.actors.Where(a=>ActorPresence.IsPresent(a) && a.level==record.level && ActorTypes.Get(a.actorType).presentation=="pony").Select(a=>a.id).OrderBy(id=>id);
                var staticIds=record.world.actors.Where(a=>ActorPresence.IsPresent(a) && a.level==record.level && ActorTypes.Get(a.actorType).presentation is "static" or "atlas").Select(a=>a.id).OrderBy(id=>id);
                if(!staticIds.SequenceEqual(record.sprites.Where(s=>staticIds.Contains(s.name)).Select(s=>s.groupId).OrderBy(id=>id)))
                    throw new InvalidOperationException($"Состав статических актёров не совпадает с миром: {file}.");
                if(!expected.SequenceEqual(record.ponies.Select(p=>p.actor.id).OrderBy(id=>id)) || record.ponies.Any(p=>!p.active || p.scale.x!=p.actor.facing))
                    throw new InvalidOperationException($"Состав или направление реальных отображений не совпадает с миром: {file}.");
                foreach(var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                    UnityEngine.Object.DestroyImmediate(root);
                RoomVisual.Build(RoomLayout.Load(record.level));
                var layers=new ActorVisualLayers();
                layers.Orders.Reconcile(record.world,record.level);
                foreach(var pony in record.ponies)
                {
                    var actor=pony.actor.Copy();actor.Position=pony.position;actor.facing=(int)pony.scale.x;
                    actor.pose=pony.clip;actor.down=pony.clip=="down";
                    var visual=new GameObject(actor.id).AddComponent<PonyVisual>();visual.Bind(actor);visual.DrawPose(pony.time);
                    if(!string.IsNullOrEmpty(pony.groupId)) layers.Attach(pony.groupId,visual.transform).Order=pony.groupOrder;
                    if(visual.DrawnFrame!=pony.frame) throw new InvalidOperationException($"Не воспроизведён кадр {pony.frame}: {actor.id}, {file}.");
                    var parts=visual.GetComponentsInChildren<MeshRenderer>().Where(p=>p.enabled).ToArray();
                    if(parts.Length==0 || parts.Any(p=>p.sharedMaterial==null || p.GetComponent<MeshFilter>().sharedMesh==null))
                        throw new InvalidOperationException($"Нет мешей или материалов записанного персонажа: {actor.id}, {file}.");
                }
                foreach(var sprite in record.sprites)
                {
                    var texture=Resources.Load<Texture2D>(sprite.resource)??throw new InvalidOperationException($"Нет ресурса {sprite.resource}: {file}.");
                    var view=new GameObject(sprite.name).AddComponent<SpriteRenderer>();
                    view.sprite=Sprite.Create(texture,new Rect(sprite.rectX,sprite.rectY,sprite.rectWidth,sprite.rectHeight),sprite.pivot,sprite.pixelsPerUnit);
                    SpriteArtworkMaterial.Prepare(view.sprite);SpriteArtworkMaterial.Apply(view,view.sprite);
                    view.transform.position=sprite.position;view.transform.localScale=sprite.scale;view.transform.eulerAngles=sprite.euler;
                    view.sortingOrder=sprite.order;view.color=sprite.color;view.flipX=sprite.flipX;view.flipY=sprite.flipY;view.enabled=sprite.enabled;
                    if(!string.IsNullOrEmpty(sprite.groupId)) layers.Attach(sprite.groupId,view.transform).Order=sprite.groupOrder;
                }
                var camera=new GameObject("Камера записанного клиента").AddComponent<Camera>();camera.orthographic=true;
                camera.orthographicSize=record.cameraSize;camera.transform.position=record.cameraPosition;camera.backgroundColor=record.background;
                var target=new RenderTexture(record.width,record.height,24);target.Create();camera.targetTexture=target;
                var image=new Texture2D(record.width,record.height,TextureFormat.RGBA32,false);
                try
                {
                    UnityEngine.Rendering.SortingGroup.UpdateAllSortingGroups();
                    camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,record.width,record.height),0,0);image.Apply();
                    File.WriteAllBytes(file+".png",image.EncodeToPNG());
                }
                finally {RenderTexture.active=null;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);}
                Debug.Log($"Сетевой кадр воспроизведён: observer={record.observer}, phase={record.phase}, level={record.level}, actors={record.ponies.Length}, файл={file}.png");
            }
        }
    }
}
