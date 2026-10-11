using System;
using System.Collections.Generic;
using System.Globalization;
using Remains.Content;
using Remains.RoomFormat;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Presentation
{
    // Room decor and objects as sprites (stage D3); placement rules (frames, flips, mirroring) are
    // RoomSpriteLayout, shared with the room prefabs. Light-zone decor (halo only) and object types drawn
    // by their own systems (units, pickups, traps, markers) are reported, not drawn.
    // A drawn decor/object sprite for light consumers (RoomRadianceField: lamps emit over their
    // silhouette, objects absorb).
    public sealed class RoomSpritePlacement
    {
        public RoomDefinition Definition;
        public Sprite Sprite;
        public int Frame;
        public Matrix4x4 LocalToWorld;
        public Sprite Erase;   // window: the background-erase clip (light from outside comes through it)
        public bool Dark;      // a lamp switched off or broken in the world: no emission whatever its frame
    }

    public static class RoomDecorView
    {
        public const string SpriteShaderResource="Presentation/WorldSprite",EraseShaderResource="Presentation/EraseMask";

        // glowThresholds: definition -> luminance threshold of self-lit pixels (screens; lighting table "glow").
        // withoutPicture: decor art drawn only by light (DecorWithoutPicture); any other missing picture is reported.
        public static GameObject Build(RoomFile room,RoomDefinitionCatalog definitions,RoomLandCatalog lands,RoomTileArtAsset art,IReadOnlyDictionary<string,string> withoutPicture,Vector2 origin,Transform terrainRoot,List<RoomSpritePlacement> placements=null,IReadOnlyDictionary<string,double> glowThresholds=null)
        {
            if(room==null || definitions==null || lands==null || art==null || terrainRoot==null) throw new ArgumentNullException(room==null?nameof(room):definitions==null?nameof(definitions):lands==null?nameof(lands):art==null?nameof(art):nameof(terrainRoot));
            var spriteShader=Resources.Load<Shader>(SpriteShaderResource)??throw new InvalidOperationException("Нет шейдера "+SpriteShaderResource);
            var root=new GameObject("Декор и объекты: "+room.Template);
            root.transform.SetParent(terrainRoot,false);   // inside the room's SortingGroup
            var holder=root.AddComponent<RoomDecorResources>();
            var materials=new Dictionary<string,Material>(StringComparer.Ordinal);
            Material MaterialFor(RoomDefinition d)
            {
                string key=MaterialKey(d,glowThresholds);
                if(materials.TryGetValue(key,out var m)) return m;
                m=new Material(spriteShader){name="Декор:"+key};
                Configure(m,d,glowThresholds,room.Template);
                materials.Add(key,m);holder.Materials.Add(m);
                return m;
            }

            var issues=new List<string>();
            var lightZones=new SortedDictionary<string,int>(StringComparer.Ordinal);
            var otherObjects=new SortedDictionary<string,int>(StringComparer.Ordinal);
            var sprites=RoomSpriteLayout.Place(room,false,definitions,art.Manifest,Number(lands.Option(room,"lon",issues)),withoutPicture,issues,lightZones,otherObjects);
            var erases=new List<(Sprite sprite,Matrix4x4 matrix)>();
            int decorCount=0,objectCount=0;
            foreach(var placed in sprites)
            {
                var d=placed.Definition;
                var obj=new GameObject((placed.Kind==RoomSpriteKind.Decor?"Декор ":"Объект ")+d.Art);
                obj.transform.SetParent(root.transform,false);
                obj.transform.localPosition=new Vector3((float)placed.X,(float)placed.Y,0);
                obj.transform.localScale=new Vector3((float)placed.ScaleX,(float)placed.ScaleY,1);
                var renderer=obj.AddComponent<SpriteRenderer>();
                renderer.sprite=art.Tile(placed.ArtId).sprite;
                renderer.sharedMaterial=MaterialFor(d);
                renderer.color=new Color(1,1,1,(float)placed.Alpha);
                renderer.sortingOrder=Order(placed);
                if(placed.Kind==RoomSpriteKind.Decor) decorCount++; else objectCount++;
                placements?.Add(new RoomSpritePlacement{Definition=d,Sprite=renderer.sprite,Frame=placed.Frame,LocalToWorld=obj.transform.localToWorldMatrix,Erase=placed.EraseArtId!=null?art.Tile(placed.EraseArtId).sprite:null});
                if(placed.EraseArtId!=null) erases.Add((art.Tile(placed.EraseArtId).sprite,obj.transform.localToWorldMatrix));
            }

            if(erases.Count>0) holder.BuildErase(erases,origin,new Vector2((float)(room.Width*room.Cell),(float)(room.Height*room.Cell)),Resources.Load<Shader>(EraseShaderResource)??throw new InvalidOperationException("Нет шейдера "+EraseShaderResource));
            if(lightZones.Count>0) issues.Add($"{room.Template}: декор-светозоны не рисуются (свет/тень новой системы, не картинка): {Join(lightZones)}.");
            if(otherObjects.Count>0) issues.Add($"{room.Template}: объекты без рисунка vis (своя отрисовка, этап позже): {Join(otherObjects)}.");
            foreach(string issue in issues) Debug.LogWarning(issue);
            Debug.Log($"Декор {room.Template}: рисунков {decorCount}, окон-стираний {erases.Count}, объектов {objectCount}.");
            return root;
        }

        // Sorting: erase-first decor below everything, decor layer <= 0 under rear materials, > 0 above; objects.
        public static int Order(RoomPlacedSprite placed)
        {
            var d=placed.Definition;
            if(placed.Kind==RoomSpriteKind.Object) return RoomTerrainView.ObjectOrder;
            return d.EraseFirst?RoomTerrainView.DecorBackOrder-5:d.DecorLayer<=0?RoomTerrainView.DecorBackOrder+d.DecorLayer:RoomTerrainView.DecorFrontOrder+d.DecorLayer;
        }

        // Material variants of the WorldSprite shader: blend mode, background-erase flag, glow threshold.
        public static string MaterialKey(RoomDefinition d,IReadOnlyDictionary<string,double> glowThresholds)
        {
            double glow=GlowThreshold(d,glowThresholds);
            return (d.Blend??"normal")+(d.EraseFirst?"|стирается":"")+(glow<2?"|свечение "+glow.ToString(CultureInfo.InvariantCulture):"");
        }

        public static void Configure(Material m,RoomDefinition d,IReadOnlyDictionary<string,double> glowThresholds,string context)
        {
            switch(d.Blend)
            {
                case null: case "normal": m.SetFloat("_BlendMode",0);m.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);m.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);break;
                case "multiply": m.SetFloat("_BlendMode",1);m.SetFloat("_SrcBlend",(float)BlendMode.DstColor);m.SetFloat("_DstBlend",(float)BlendMode.Zero);break;
                case "overlay": m.SetFloat("_BlendMode",2);m.SetFloat("_SrcBlend",(float)BlendMode.DstColor);m.SetFloat("_DstBlend",(float)BlendMode.SrcColor);break;
                default: throw new InvalidOperationException($"{context}: режим смешивания декора \"{d.Blend}\" не поддержан.");
            }
            if(d.EraseFirst) m.SetFloat("_EraseAffected",1);
            m.SetFloat("_GlowThreshold",(float)GlowThreshold(d,glowThresholds));
        }

        public static double GlowThreshold(RoomDefinition d,IReadOnlyDictionary<string,double> glowThresholds)
            =>glowThresholds!=null && d.Id!=null && glowThresholds.TryGetValue(d.Id,out double g)?g:2;

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
