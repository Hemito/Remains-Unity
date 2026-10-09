using System;
using System.Collections.Generic;
using System.Globalization;
using Remains.Content;
using Remains.RoomFormat;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Presentation
{
    // Room decor and objects as sprites (stage D3), placed by the original rules:
    //  - decor (BackObj): clip origin at the placement cell's top-left; frame = placement "fr", else the
    //    definition's fixed frame, else the lights-on/off frame by the room's "lon", else random; "mirr" 2
    //    flips at random; placement "w"/"h" scale, "a" opacity; layer ≤ 0 under rear materials, > 0 above;
    //    background erase clips (windows) cut the backdrop, back walls and "eraseFirst" decor;
    //  - objects (Box): clip origin at the bottom centre of "size" cells, 1 px above the cell bottom, frame 1.
    // Random choices are seeded by room template and placement index (same for every player and load).
    // Light-zone decor (halo only) and object types drawn by their own systems (units, pickups, traps,
    // markers) are reported, not drawn.
    // A drawn decor/object sprite for light consumers (RoomRadianceField: lamps emit over their
    // silhouette, objects absorb).
    public sealed class RoomSpritePlacement
    {
        public RoomDefinition Definition;
        public Sprite Sprite;
        public Matrix4x4 LocalToWorld;
    }

    public static class RoomDecorView
    {
        private const string SpriteShaderResource="Presentation/WorldSprite",EraseShaderResource="Presentation/EraseMask";
        private const float SourcePixelsPerMeter=50;

        // glowThresholds: definition -> luminance threshold of self-lit pixels (screens; lighting table "glow").
        public static GameObject Build(RoomFile room,RoomDefinitionCatalog definitions,RoomLandCatalog lands,RoomTileArtAsset art,Vector2 origin,Transform terrainRoot,List<RoomSpritePlacement> placements=null,IReadOnlyDictionary<string,double> glowThresholds=null)
        {
            if(room==null || definitions==null || lands==null || art==null || terrainRoot==null) throw new ArgumentNullException(room==null?nameof(room):definitions==null?nameof(definitions):lands==null?nameof(lands):art==null?nameof(art):nameof(terrainRoot));
            var spriteShader=Resources.Load<Shader>(SpriteShaderResource)??throw new InvalidOperationException("Нет шейдера "+SpriteShaderResource);
            var root=new GameObject("Декор и объекты: "+room.Template);
            root.transform.SetParent(terrainRoot,false);   // inside the room's SortingGroup
            var holder=root.AddComponent<RoomDecorResources>();
            var materials=new Dictionary<string,Material>(StringComparer.Ordinal);
            Material MaterialFor(string blend,bool erase,string definition)
            {
                double glow=glowThresholds!=null && definition!=null && glowThresholds.TryGetValue(definition,out double g)?g:2;
                string key=(blend??"normal")+(erase?"|стирается":"")+(glow<2?"|свечение "+glow.ToString(CultureInfo.InvariantCulture):"");
                if(materials.TryGetValue(key,out var m)) return m;
                m=new Material(spriteShader){name="Декор:"+key};
                switch(blend)
                {
                    case null: case "normal": m.SetFloat("_BlendMode",0);m.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);m.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);break;
                    case "multiply": m.SetFloat("_BlendMode",1);m.SetFloat("_SrcBlend",(float)BlendMode.DstColor);m.SetFloat("_DstBlend",(float)BlendMode.Zero);break;
                    case "overlay": m.SetFloat("_BlendMode",2);m.SetFloat("_SrcBlend",(float)BlendMode.DstColor);m.SetFloat("_DstBlend",(float)BlendMode.SrcColor);break;
                    default: throw new InvalidOperationException($"{room.Template}: режим смешивания декора \"{blend}\" не поддержан.");
                }
                if(erase) m.SetFloat("_EraseAffected",1);
                m.SetFloat("_GlowThreshold",(float)glow);
                materials.Add(key,m);holder.Materials.Add(m);
                return m;
            }

            var frames=FrameCounts(art.Manifest);
            var issues=new List<string>();
            int lightsOn=(int)Number(lands.Option(room,"lon",issues));
            float cell=(float)room.Cell;
            var erases=new List<(Sprite sprite,Matrix4x4 matrix)>();
            var lightZones=new SortedDictionary<string,int>(StringComparer.Ordinal);
            int decorCount=0;
            for(int i=0;i<room.Decor.Count;i++)
            {
                var placement=room.Decor[i];
                var d=definitions.Require(placement.Type,$"{room.Template} декор {i}");
                if(!frames.TryGetValue("decor:"+d.Art,out int count)) {lightZones[d.Art]=lightZones.TryGetValue(d.Art,out int n)?n+1:1;continue;}
                var random=Seeded(room.Template,i);
                int frame=d.DecorFrame;
                if(frame==0)
                {
                    if(lightsOn>0 && d.DecorFrameOn>0) frame=d.DecorFrameOn;
                    else if(lightsOn<0 && d.DecorFrameOff>0) frame=d.DecorFrameOff;
                    else frame=random.Next(count)+1;
                }
                if(Param(placement.Params,"fr") is double fr) frame=(int)fr;
                if(Param(placement.Params,"lon") is double lon)
                {
                    if(lon>1 && d.DecorFrameOn>0) frame=d.DecorFrameOn;
                    if(lon<1 && d.DecorFrameOff>0) frame=d.DecorFrameOff;
                }
                if(frame<1 || frame>count) {issues.Add($"{room.Template}: декор {d.Id} кадр {frame} вне 1..{count} — взят кадр 1.");frame=1;}

                // Original BackObj: position = cell top-left; mirr 2 flips by the width; w/h replace the scale.
                float x=placement.X*cell,y=(placement.Y+1)*cell,scaleX=1,scaleY=1;
                double? w=Param(placement.Params,"w"),h=Param(placement.Params,"h");
                float width=(float)(w??(d.DecorWidth>0?d.DecorWidth:1))*cell;
                if(d.Mirror==2 && random.NextDouble()<0.5) {x+=width;scaleX=-1;}
                if(w is double ws) scaleX=(float)ws;
                if(h is double hs) scaleY=(float)hs;
                float alpha=(float)(Param(placement.Params,"a")??d.Alpha);

                var obj=new GameObject("Декор "+d.Art);
                obj.transform.SetParent(root.transform,false);
                obj.transform.localPosition=new Vector3(x,y,0);
                obj.transform.localScale=new Vector3(scaleX,scaleY,1);
                var renderer=obj.AddComponent<SpriteRenderer>();
                renderer.sprite=art.Tile(TileManifest.DecorFrameId(d.Art,frame)).sprite;
                renderer.sharedMaterial=MaterialFor(d.Blend,d.EraseFirst,d.Id);
                renderer.color=new Color(1,1,1,alpha);
                renderer.sortingOrder=d.EraseFirst?RoomTerrainView.DecorBackOrder-5:d.DecorLayer<=0?RoomTerrainView.DecorBackOrder+d.DecorLayer:RoomTerrainView.DecorFrontOrder+d.DecorLayer;
                decorCount++;
                placements?.Add(new RoomSpritePlacement{Definition=d,Sprite=renderer.sprite,LocalToWorld=obj.transform.localToWorldMatrix});
                string eraseId=TileManifest.DecorEraseFrameId(d.Art,frame);
                if(art.Manifest.Art.ContainsKey(eraseId)) erases.Add((art.Tile(eraseId).sprite,obj.transform.localToWorldMatrix));
            }

            var otherObjects=new SortedDictionary<string,int>(StringComparer.Ordinal);
            int objectCount=0;
            foreach(var placement in room.Objects)
            {
                var d=definitions.Require(placement.Type,$"{room.Template} объект {placement.Code}");
                if(!frames.ContainsKey("object:"+d.Art)) {string type=d.ObjectType??"?";otherObjects[type]=otherObjects.TryGetValue(type,out int n)?n+1:1;continue;}
                int size=Math.Max(1,d.ObjectSize);
                var obj=new GameObject("Объект "+d.Art);
                obj.transform.SetParent(root.transform,false);
                obj.transform.localPosition=new Vector3((placement.X+size/2f)*cell,placement.Y*cell+1/SourcePixelsPerMeter,0);
                var renderer=obj.AddComponent<SpriteRenderer>();
                renderer.sprite=art.Tile(TileManifest.ObjectFrameId(d.Art,1)).sprite;
                renderer.sharedMaterial=MaterialFor(null,false,d.Id);
                renderer.sortingOrder=RoomTerrainView.ObjectOrder;
                objectCount++;
                placements?.Add(new RoomSpritePlacement{Definition=d,Sprite=renderer.sprite,LocalToWorld=obj.transform.localToWorldMatrix});
            }

            if(erases.Count>0) holder.BuildErase(erases,origin,new Vector2(room.Width*cell,room.Height*cell),Resources.Load<Shader>(EraseShaderResource)??throw new InvalidOperationException("Нет шейдера "+EraseShaderResource));
            if(lightZones.Count>0) issues.Add($"{room.Template}: декор-светозоны не рисуются (свет/тень новой системы, не картинка): {Join(lightZones)}.");
            if(otherObjects.Count>0) issues.Add($"{room.Template}: объекты без рисунка vis (своя отрисовка, этап позже): {Join(otherObjects)}.");
            foreach(string issue in issues) Debug.LogWarning(issue);
            Debug.Log($"Декор {room.Template}: рисунков {decorCount}, окон-стираний {erases.Count}, объектов {objectCount}.");
            return root;
        }

        // Frame count per art clip: "decor:<art>" / "object:<art>" -> frames 1..N present in the manifest.
        private static Dictionary<string,int> FrameCounts(TileManifest manifest)
        {
            var counts=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(string id in manifest.Art.Keys)
            {
                if(!id.StartsWith("decor:",StringComparison.Ordinal) && !id.StartsWith("object:",StringComparison.Ordinal)) continue;
                int split=id.LastIndexOf(':');
                string key=id.Substring(0,split);
                int frame=int.Parse(id.Substring(split+1),CultureInfo.InvariantCulture);
                counts[key]=Math.Max(frame,counts.TryGetValue(key,out int n)?n:0);
            }
            return counts;
        }

        private static System.Random Seeded(string template,int index)
        {
            uint hash=2166136261;
            foreach(char c in template+"#"+index.ToString(CultureInfo.InvariantCulture)) {hash^=c;hash*=16777619;}
            return new System.Random((int)hash);
        }

        private static double? Param(IReadOnlyDictionary<string,string> parameters,string key)
            =>parameters!=null && parameters.TryGetValue(key,out string v) && v.Length>0?double.Parse(v,NumberStyles.Float,CultureInfo.InvariantCulture):null;

        private static double Number(string value)=>string.IsNullOrEmpty(value)?0:double.Parse(value,NumberStyles.Float,CultureInfo.InvariantCulture);

        private static string Join(SortedDictionary<string,int> counts)
        {
            var parts=new List<string>();
            foreach(var e in counts) parts.Add($"{e.Key} ×{e.Value}");
            return string.Join(", ",parts);
        }
    }

    // Materials and the background-erase mask of one room's decor; released with the room.
    public sealed class RoomDecorResources : MonoBehaviour
    {
        public readonly List<Material> Materials=new();
        private RenderTexture erase;
        private Mesh quad;

        // Renders the erase clips (world matrices) into a room-sized mask, 1:1 with the original pixels,
        // once at build time; WorldTerrain/WorldSprite layers flagged _EraseAffected read it.
        public void BuildErase(List<(Sprite sprite,Matrix4x4 matrix)> clips,Vector2 origin,Vector2 size,Shader shader)
        {
            const float pixelsPerMeter=50;
            erase=new RenderTexture(Mathf.CeilToInt(size.x*pixelsPerMeter),Mathf.CeilToInt(size.y*pixelsPerMeter),0,RenderTextureFormat.R8){name="Стирание фона",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            erase.Create();
            var material=new Material(shader){name="Стирание фона"};
            Materials.Add(material);
            quad=new Mesh{name="Квадрат стирания"};
            quad.SetVertices(new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,1,0),new Vector3(1,1,0)});
            quad.SetUVs(0,new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1)});
            quad.SetTriangles(new[]{0,2,1,1,2,3},0);
            var cmd=new CommandBuffer{name="Стирание фона комнаты"};
            cmd.SetRenderTarget(erase);
            cmd.ClearRenderTarget(false,true,Color.black);
            cmd.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(origin.x,origin.x+size.x,origin.y,origin.y+size.y,-1,1),false));
            var block=new MaterialPropertyBlock();
            foreach(var (sprite,matrix) in clips)
            {
                var bounds=sprite.bounds;
                block.SetTexture("_MainTex",sprite.texture);
                cmd.DrawMesh(quad,matrix*Matrix4x4.TRS(bounds.min,Quaternion.identity,bounds.size),material,0,0,block);
            }
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
            Shader.SetGlobalTexture("_RemainsErase",erase);
            Shader.SetGlobalVector("_RemainsEraseRect",new Vector4(origin.x,origin.y,size.x,size.y));
            Shader.SetGlobalFloat("_RemainsEraseEnabled",1);
        }

        private void OnDestroy()
        {
            if(erase!=null) {Shader.SetGlobalFloat("_RemainsEraseEnabled",0);erase.Release();Destroy(erase);}
            foreach(var m in Materials) if(m!=null) Destroy(m);
            if(quad!=null) Destroy(quad);
        }

        private new static void Destroy(UnityEngine.Object o) {if(Application.isPlaying) UnityEngine.Object.Destroy(o);else DestroyImmediate(o);}
    }
}
