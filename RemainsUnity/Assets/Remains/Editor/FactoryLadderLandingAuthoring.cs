using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Add only reviewed records. Never rebuild a saved room or infer a legacy ladder rule.
    public static class FactoryLadderLandingAuthoring
    {
        public const string EntryPath="Assets/Remains/RoomDrafts/FactoryEntry46.prefab";
        public static void Install()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();
            InstallPrefab(EntryPath,46);InstallPrefab(FactoryWorkshopDraft.Path,18);
            AssetDatabase.SaveAssets();FactoryLadderLandingChecks.Run();
            Debug.Log("Верхние посадки: сохранены шесть явных опор и исходные верхушки; комнаты и содержимое не пересозданы.");
        }
        private static void InstallPrefab(string path,int sourceRoom)
        {
            string guid=AssetDatabase.AssetPathToGUID(path);
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                InstallOn(root,sourceRoom);
                PrefabUtility.SaveAsPrefabAsset(root,path);AssetDatabase.SaveAssets();
                if(guid!=AssetDatabase.AssetPathToGUID(path)) throw new InvalidOperationException("Изменился GUID комнаты: "+path);
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
        }
        public static void InstallOn(GameObject root,int sourceRoom)
        {
            var room=root.GetComponent<RoomAuthoring>();
            if(room==null || !room.HasAuthoredArtwork) throw new InvalidOperationException("Верхние посадки: нужно сохранённое оформление комнаты "+root.name);
            var records=FactoryLadderLandingSource.Load().Room(sourceRoom);
            if(records.Length==0) throw new InvalidOperationException("Нет явных посадок исходной комнаты "+sourceRoom);
            var markers=root.GetComponentsInChildren<RoomLadderLandingAuthoring>(true);
            if(markers.Length>0)
            {
                if(markers.Length!=records.Length || markers.Any(m=>!records.Any(r=>r.id==m.StableId)))
                    throw new InvalidOperationException("Посадки комнаты "+sourceRoom+": неполный список или незнакомые stable ID; ручные данные не заменены.");
                FactoryLadderLandingChecks.CheckInstalled(room,records,0,false);return;
            }
            string folderName="Factory"+sourceRoom+"LadderLandingArtwork",folder="Assets/Remains/RoomDrafts/"+folderName;
            if(AssetDatabase.IsValidFolder(folder)) throw new InvalidOperationException("Посадки не установлены, но каталог уже существует; исходные данные не заменены: "+folder);
            var before=room.CreateLayout();string content=StateJson.ToJson(RoomContentBatch.Read(room,"factory:0"));
            var surfaces=new RoomSurfaceAuthoring[records.Length];var caps=new RoomArtworkAuthoring[records.Length];var rungs=new MeshFilter[records.Length];
            for(int i=0;i<records.Length;i++)
            {
                var record=records[i];FactoryLadderLandingChecks.ValidateTexture(record.artwork.texture);
                var ladder=before.ladders.SingleOrDefault(l=>Mathf.Abs(l.x-record.ladderX)<.001f && Mathf.Abs(l.top-record.ladderTop)<.001f);
                if(ladder==null || ladder.top-ladder.bottom<=record.surface.height || ladder.artwork!=record.rungTexture)
                    throw new InvalidOperationException("Посадка "+record.id+": не найдена прежняя лестница с нужным верхом/ресурсом.");
                surfaces[i]=Unique(root.GetComponentsInChildren<RoomSurfaceAuthoring>(true).Where(p=>FactoryLadderLandingChecks.SameSurface(p.Read(room),record.surface)),record.id,"опора");
                caps[i]=Unique(root.GetComponentsInChildren<RoomArtworkAuthoring>(true).Where(p=>FactoryLadderLandingChecks.SameArtwork(p.Read(room),record.artwork)),record.id,"верхушка");
                var collisions=before.platforms.Where(p=>Mathf.Abs(p.x-record.surface.x)<.001f && Mathf.Abs(p.y-record.surface.y)<.001f);
                if(surfaces[i]==null && collisions.Any()) throw new InvalidOperationException("Посадка "+record.id+": существующая опора отличается; авторские изменения не заменены.");
                var texture=Resources.Load<Texture2D>("Original/Room/"+record.rungTexture);
                if(texture==null) throw new InvalidOperationException("Посадка "+record.id+": отсутствует прежний ресурс лестницы "+record.rungTexture);
                rungs[i]=Unique(root.GetComponentsInChildren<MeshFilter>(true).Where(p=>
                    p.GetComponent<MeshRenderer>()?.sharedMaterial?.mainTexture==texture &&
                    FactoryLadderLandingChecks.SameRect(FactoryLadderLandingChecks.MeshRect(p,room),record.artwork.x,ladder.bottom,record.artwork.width,ladder.top-ladder.bottom)),record.id,"прежнее изображение лестницы");
                if(rungs[i]==null || !AssetDatabase.Contains(rungs[i].sharedMesh) || rungs[i].sharedMesh.vertexCount!=4 || rungs[i].sharedMesh.uv.Length!=4)
                    throw new InvalidOperationException("Посадка "+record.id+": нет единственного сохранённого прямоугольного rung; ручная графика не заменена.");
            }
            var layout=new RoomLayout {id="factory-ladder-landings-"+sourceRoom,platforms=Array.Empty<RoomSurface>(),ladders=Array.Empty<RoomLadder>(),
                artwork=records.Where((r,i)=>caps[i]==null).Select(r=>r.artwork).ToArray()};
            var graphics=RoomVisual.Build(layout,new RoomLightProfile {ambient=Color.white,lights=Array.Empty<RoomLight>()});
            graphics.name="Верхушки внутренних лестниц";RoomArtworkAssets.Save(graphics,"Assets/Remains/RoomDrafts",folderName);graphics.transform.SetParent(root.transform,false);
            var created=graphics.GetComponentsInChildren<RoomArtworkAuthoring>();
            for(int i=0;i<records.Length;i++)
            {
                var record=records[i];
                if(surfaces[i]==null)
                {
                    var node=new GameObject("Верхняя посадка · "+record.id);node.transform.SetParent(root.transform,false);
                    surfaces[i]=node.AddComponent<RoomSurfaceAuthoring>();surfaces[i].Configure(record.surface);
                }
                surfaces[i].gameObject.AddComponent<RoomLadderLandingAuthoring>().Configure(record.id);
                if(caps[i]==null) caps[i]=created.Single(p=>FactoryLadderLandingChecks.SameArtwork(p.Read(room),record.artwork));
                caps[i].transform.SetParent(surfaces[i].transform,true);
                var filter=rungs[i];var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);mesh.name="Нижний фрагмент · "+record.id;
                var bounds=mesh.bounds;float ratio=(FactoryLadderLandingChecks.MeshRect(filter,room).height-record.surface.height)/FactoryLadderLandingChecks.MeshRect(filter,room).height;
                var vertices=mesh.vertices;for(int v=0;v<vertices.Length;v++) vertices[v].y=bounds.min.y+(vertices[v].y-bounds.min.y)*ratio;
                mesh.vertices=vertices;var uv=mesh.uv;float min=uv.Min(p=>p.y);for(int v=0;v<uv.Length;v++) uv[v].y=min+(uv[v].y-min)*ratio;
                mesh.uv=uv;mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,folder+"/rung-"+i+".asset");filter.sharedMesh=mesh;
            }
            var after=room.CreateLayout();
            if(StateJson.ToJson(before.ladders)!=StateJson.ToJson(after.ladders) || content!=StateJson.ToJson(RoomContentBatch.Read(room,"factory:0")) ||
                before.platforms.Any(s=>!after.platforms.Any(a=>FactoryLadderLandingChecks.SameSurface(a,s))) ||
                before.artwork.Any(s=>!after.artwork.Any(a=>FactoryLadderLandingChecks.SameArtwork(a,s))))
                throw new InvalidOperationException("Посадки: изменились прежние поверхности/оформление/лестницы или содержимое комнаты "+sourceRoom);
            FactoryLadderLandingChecks.CheckInstalled(room,records,0,false);
        }
        private static T Unique<T>(System.Collections.Generic.IEnumerable<T> items,string id,string kind) where T:class
        {
            var array=items.ToArray();if(array.Length>1) throw new InvalidOperationException("Посадка "+id+": несколько объектов «"+kind+"»; конфликт не скрыт.");
            return array.SingleOrDefault();
        }
    }
}
