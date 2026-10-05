using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryLadderLandingChecks
    {
        public static void Run()
        {
            // The connected-room check installs NPC content, including its initial weapon.
            WeaponCatalog.Load();
            var source=FactoryLadderLandingSource.Load();
            Require(source.records.Length==6 && source.Room(46).Length==2 && source.Room(18).Length==4,"Нужны ровно шесть подтверждённых записей.");
            var entry=AssetDatabase.LoadAssetAtPath<GameObject>(FactoryLadderLandingAuthoring.EntryPath).GetComponent<RoomAuthoring>();
            var workshop=AssetDatabase.LoadAssetAtPath<GameObject>(FactoryWorkshopDraft.Path).GetComponent<RoomAuthoring>();
            Require(entry.GetComponentsInChildren<RoomLadderLandingAuthoring>(true).Length==2 && workshop.GetComponentsInChildren<RoomLadderLandingAuthoring>(true).Length==4,"Незнакомые marker в отдельных модулях.");
            CheckInstalled(entry,source.Room(46),0,false);CheckInstalled(workshop,source.Room(18),0,false);
            var connected=AssetDatabase.LoadAssetAtPath<GameObject>(ConnectedFactoryAuthoring.Path).GetComponent<RoomAuthoring>();
            CheckConnected(connected,false);ConnectedFactoryChecks.Check(connected);CheckMissingLandingGuard(connected);
            Debug.Log("Верхние посадки: PASS — шесть stable ID, отдельные опоры/исходная графика/UV/Clamp, rung без наложения, вход и цех в связном уровне.");
        }
        private static void CheckMissingLandingGuard(RoomAuthoring original)
        {
            var copy=UnityEngine.Object.Instantiate(original.gameObject);
            try
            {
                foreach(var marker in copy.GetComponentsInChildren<RoomLadderLandingAuthoring>(true))
                    UnityEngine.Object.DestroyImmediate(marker.gameObject);
                bool rejected=false;
                try {ConnectedFactoryChecks.Check(copy.GetComponent<RoomAuthoring>());}
                catch(InvalidOperationException) {rejected=true;}
                Require(rejected,"Общая проверка приняла связный завод после удаления всех обязательных верхних посадок.");
            }
            finally {UnityEngine.Object.DestroyImmediate(copy);}
        }
        public static int CheckConnected(RoomAuthoring room,bool allowAbsent=true)
        {
            var source=FactoryLadderLandingSource.Load();var markers=room.GetComponentsInChildren<RoomLadderLandingAuthoring>(true);
            if(markers.Length==0 && allowAbsent) return 0;
            Require(markers.Length==source.records.Length && markers.Select(m=>m.StableId).Distinct(StringComparer.Ordinal).Count()==markers.Length,"Неполная установка или повтор ID в общем уровне.");
            return CheckInstalled(room,source.Room(46),0,false)+CheckInstalled(room,source.Room(18),38.4f,false);
        }
        public static int CheckInstalled(RoomAuthoring room,FactoryLadderLandingRecord[] records,float offset,bool allowAbsent)
        {
            var all=room.GetComponentsInChildren<RoomLadderLandingAuthoring>(true);
            var markers=all.Where(m=>records.Any(r=>r.id==m.StableId)).ToArray();
            if(all.Length==0 && allowAbsent) return 0;
            Require(markers.Length==records.Length && markers.Select(m=>m.StableId).Distinct(StringComparer.Ordinal).Count()==records.Length,"Нет полного списка ID: "+room.name);
            if(offset==0 && records.Length==all.Length) Require(all.All(m=>records.Any(r=>r.id==m.StableId)),"Незнакомый marker: "+room.name);
            var layout=room.CreateLayout();
            foreach(var record in records)
            {
                record.Validate();ValidateTexture(record.artwork.texture);
                var marker=markers.Single(m=>m.StableId==record.id);Require(room.Included(marker),"Посадка выключена: "+record.id);
                var support=marker.GetComponent<RoomSurfaceAuthoring>();Require(support!=null,"Marker без опоры: "+record.id);
                Require(SameSurface(support.Read(room),record.surface,offset),"Опора отличается от явных данных: "+record.id);
                var caps=marker.GetComponentsInChildren<RoomArtworkAuthoring>(true);Require(caps.Length==1 && SameArtwork(caps[0].Read(room),record.artwork,offset),"Графика/границы не соответствуют исходной верхушке: "+record.id);
                var mesh=caps[0].GetComponent<MeshFilter>().sharedMesh;var uv=mesh.uv;
                Require(AssetDatabase.Contains(mesh) && uv.Length==4 && uv[0]==Vector2.zero && uv[1]==Vector2.right && uv[2]==Vector2.up && uv[3]==Vector2.one,"Верхушка не имеет сохранённых full-quad UV: "+record.id);
                Require(caps[0].GetComponent<MeshRenderer>().sharedMaterial.mainTexture==Resources.Load<Texture2D>("Original/Room/"+record.artwork.texture),"Материал верхушки использует другой ресурс: "+record.id);
                var ladder=layout.ladders.Single(l=>Mathf.Abs(l.x-record.ladderX-offset)<.001f && Mathf.Abs(l.top-record.ladderTop)<.001f);
                var texture=Resources.Load<Texture2D>("Original/Room/"+record.rungTexture);
                var rungs=room.GetComponentsInChildren<MeshFilter>(true).Where(p=>p.GetComponent<MeshRenderer>()?.sharedMaterial?.mainTexture==texture &&
                    SameRect(MeshRect(p,room),record.artwork.x+offset,ladder.bottom,record.artwork.width,ladder.top-ladder.bottom-record.surface.height)).ToArray();
                Require(rungs.Length==1,"Нет отдельного нижнего rung без верхнего наложения: "+record.id);
                RoomTextureSampling.ValidateMesh(rungs[0].sharedMesh,texture,record.id);
                var hull=new MovementHull {width=.2f,height=.4f};var position=new Vector2(ladder.x+ladder.movementOffset,record.ladderTop);
                Require(ActorTerrainMotion.IsClear(layout,hull,position) && ActorTerrainMotion.Supported(layout,hull,position),"Верхняя посадка не поддерживает тело или стала сплошной стеной: "+record.id);
            }
            return markers.Length;
        }
        internal static void ValidateTexture(string key)
        {
            var texture=Resources.Load<Texture2D>("Original/Room/"+key);string path=texture==null?null:AssetDatabase.GetAssetPath(texture);
            var importer=string.IsNullOrEmpty(path)?null:AssetImporter.GetAtPath(path) as TextureImporter;
            Require(texture!=null && texture.width==100 && texture.height==140 && texture.wrapModeU==TextureWrapMode.Clamp && texture.wrapModeV==TextureWrapMode.Clamp &&
                importer!=null && importer.wrapModeU==TextureWrapMode.Clamp && importer.wrapModeV==TextureWrapMode.Clamp &&
                importer.textureCompression==TextureImporterCompression.Uncompressed && !importer.mipmapEnabled,"Нет исходного PNG100×140/Clamp без сжатия: "+key);
        }
        internal static Rect MeshRect(MeshFilter filter,RoomAuthoring room)
        {
            var bounds=filter.sharedMesh.bounds;var matrix=room.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;
            var min=matrix.MultiplyPoint3x4(bounds.min);var max=matrix.MultiplyPoint3x4(bounds.max);return new Rect(min.x,min.y,max.x-min.x,max.y-min.y);
        }
        internal static bool SameRect(Rect rect,float x,float y,float width,float height)=>
            Mathf.Abs(rect.x-x)<.001f && Mathf.Abs(rect.y-y)<.001f && Mathf.Abs(rect.width-width)<.001f && Mathf.Abs(rect.height-height)<.001f;
        internal static bool SameSurface(RoomSurface actual,RoomSurface expected,float offset=0)=>SameRect(new Rect(actual.x,actual.y,actual.width,actual.height),expected.x+offset,expected.y,expected.width,expected.height) &&
            actual.texture==expected.texture && actual.movementSound==expected.movementSound && actual.oneWay==expected.oneWay && actual.shotsPassThrough==expected.shotsPassThrough && actual.slopeDirection==expected.slopeDirection;
        internal static bool SameArtwork(RoomArtwork actual,RoomArtwork expected,float offset=0)=>SameRect(new Rect(actual.x,actual.y,actual.width,actual.height),expected.x+offset,expected.y,expected.width,expected.height) &&
            actual.texture==expected.texture && actual.order==expected.order && actual.repeat==expected.repeat && actual.slopeDirection==expected.slopeDirection && Mathf.Abs(actual.shade-expected.shade)<.001f &&
            actual.stretchY==expected.stretchY && actual.selfIlluminated==expected.selfIlluminated && actual.backgroundLighting==expected.backgroundLighting;
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Верхние посадки: "+reason);}
    }
}
