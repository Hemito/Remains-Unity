using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // An explicit asset upgrade: physical supports and their full bitmap artwork have separate bounds.
    public static class FactoryStairArtworkAuthoring
    {
        public const string TextureKey="WorkshopStairs/stair-down-right";
        public static void Install()
        {
            RoomAuthoringScenes.ValidateSavedOverrides();
            var texture=Resources.Load<Texture2D>("Original/Room/"+TextureKey);
            if(texture==null || texture.width!=40 || texture.height!=80)
                throw new InvalidOperationException("Не импортирована исходная графика ступеней 40×80: "+TextureKey);
            var root=PrefabUtility.LoadPrefabContents(FactoryWorkshopDraft.Path);
            try
            {
                var room=root.GetComponent<RoomAuthoring>();
                var layers=root.GetComponentsInChildren<RoomArtworkAuthoring>(true)
                    .Where(part=>part.Read(room).slopeDirection!=0).OrderBy(part=>part.Read(room).x).ToArray();
                var supports=root.GetComponentsInChildren<RoomSurfaceAuthoring>(true)
                    .Where(part=>part.Read(room).slopeDirection!=0).OrderBy(part=>part.Read(room).x).ToArray();
                if(layers.Length!=4 || supports.Length!=4)
                    throw new InvalidOperationException("Ожидались четыре прежних слоя и опоры ступеней; повторная установка или авторские изменения: "+FactoryWorkshopDraft.Path);
                for(int i=0;i<4;i++)
                {
                    var layer=layers[i];var old=layer.Read(room);var support=supports[i].Read(room);
                    float x=33.6f+i*.8f,y=3.2f-i*.8f;
                    if(old.texture!="Factory18/36_tShelf" || old.slopeDirection!=-1 || Mathf.Abs(old.x-x)>.001f || Mathf.Abs(old.y-y)>.001f ||
                        Mathf.Abs(old.width-.8f)>.001f || Mathf.Abs(old.height-.8f)>.001f || Mathf.Abs(support.x-x)>.001f || Mathf.Abs(support.y-y)>.001f)
                        throw new InvalidOperationException($"Ступени {i}: исходная геометрия отличается; слой={old.texture}/({old.x},{old.y},{old.width},{old.height}), опора=({support.x},{support.y}).");
                    var filter=layer.GetComponent<MeshFilter>();var mesh=filter.sharedMesh;
                    if(!AssetDatabase.Contains(mesh)) throw new InvalidOperationException("Ступени не имеют сохранённого меша: "+layer.name);
                    mesh.vertices=new[]{Vector3.zero,new Vector3(.8f,0,0),new Vector3(0,1.6f,0),new Vector3(.8f,1.6f,0)};
                    mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one};mesh.triangles=new[]{0,2,1,2,3,1};mesh.RecalculateBounds();
                    EditorUtility.SetDirty(mesh);
                    layer.transform.localPosition-=Vector3.up*.8f;
                    var renderer=layer.GetComponent<MeshRenderer>();renderer.sharedMaterial.mainTexture=texture;EditorUtility.SetDirty(renderer.sharedMaterial);
                    old.texture=TextureKey;old.y-=.8f;old.height=1.6f;old.repeat=false;old.slopeDirection=0;layer.Configure(old);
                    support.texture=TextureKey;supports[i].Configure(support);
                }
                Check(room,0);
                PrefabUtility.SaveAsPrefabAsset(root,FactoryWorkshopDraft.Path);AssetDatabase.SaveAssets();
            }
            finally {PrefabUtility.UnloadPrefabContents(root);}
            RunChecks();
            Debug.Log("Графика ступеней: сохранены четыре исходных полных рисунка; наклонные опоры и прочее содержимое не заменены.");
        }
        public static void RunChecks()
        {
            Check(AssetDatabase.LoadAssetAtPath<GameObject>(FactoryWorkshopDraft.Path).GetComponent<RoomAuthoring>(),0);
            Check(AssetDatabase.LoadAssetAtPath<GameObject>(ConnectedFactoryAuthoring.Path).GetComponent<RoomAuthoring>(),38.4f);
            Debug.Log("Графика ступеней: PASS — четыре полноразмерных quad/Clamp/UV/регистрации, отдельная геометрия опор в цехе и общем заводе.");
        }
        private static void Check(RoomAuthoring room,float offset)
        {
            var parts=room.GetComponentsInChildren<RoomArtworkAuthoring>(true).Where(part=>part.Read(room).texture==TextureKey).OrderBy(part=>part.Read(room).x).ToArray();
            if(parts.Length!=4) throw new InvalidOperationException("Нет четырёх исходных ступеней: "+room.name);
            var texture=Resources.Load<Texture2D>("Original/Room/"+TextureKey);
            if(texture.width!=40 || texture.height!=80 || texture.wrapModeU!=TextureWrapMode.Clamp || texture.wrapModeV!=TextureWrapMode.Clamp)
                throw new InvalidOperationException("Ступени потеряли нативный размер или Clamp sampler: "+TextureKey);
            var supports=room.CreateLayout().platforms.Where(s=>s.texture==TextureKey).OrderBy(s=>s.x).ToArray();
            if(supports.Length!=4) throw new InvalidOperationException("Нет четырёх отдельных опор ступеней: "+room.name);
            for(int i=0;i<4;i++)
            {
                var art=parts[i].Read(room);var support=supports[i];var mesh=parts[i].GetComponent<MeshFilter>().sharedMesh;
                if(art.repeat || art.slopeDirection!=0 || art.order!=-5 || Mathf.Abs(art.x-offset-33.6f-i*.8f)>.001f || Mathf.Abs(art.y-2.4f+i*.8f)>.001f ||
                    Mathf.Abs(art.width-.8f)>.001f || Mathf.Abs(art.height-1.6f)>.001f || support.slopeDirection!=-1 || !support.oneWay || !support.shotsPassThrough ||
                    Mathf.Abs(support.y-art.y-.8f)>.001f || Mathf.Abs(support.width-.8f)>.001f || Mathf.Abs(support.height-.8f)>.001f ||
                    mesh.uv.Length!=4 || mesh.uv[0]!=Vector2.zero || mesh.uv[1]!=Vector2.right || mesh.uv[2]!=Vector2.up || mesh.uv[3]!=Vector2.one)
                    throw new InvalidOperationException($"Некорректная графика/опора ступеней: room={room.name}, index={i}, art=({art.x},{art.y},{art.width},{art.height}), slope={support.slopeDirection}.");
            }
        }
    }
}
