using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.RoomFormat;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

namespace Remains.Presentation
{
    // Draws a room template's terrain from its grid (stage B3): Tilemaps at half-cell
    // resolution, one per material layer; each quarter tile is a shape mask, mirrored per
    // quarter by the tile matrix; the material texture is laid in world space by the
    // WorldTerrain shader. Shape states come from RoomTerrainShapes (one rule, grid-based).
    // Flash filters (bevel, glow, drips) are not reproduced yet (stage B4).
    public static class RoomTerrainView
    {
        // Draw order inside the room's SortingGroup, original display list bottom-up: backdrop, back
        // walls, decor of layers ≤ 0 (backBmp), rear materials, decor of layers > 0 (backBmp2), objects,
        // front terrain. Gaps leave room for the decor layers and objects (RoomDecorView).
        public const int BackdropOrder=-300,BackOrder=-290,DecorBackOrder=-280,RearOrder=-60,DecorFrontOrder=-55,RearSpriteOrder=-50,ObjectOrder=-45,
            FrontOrder=-40,BorderOrder=-30,FloorOrder=-20,FrontSpriteOrder=10;
        private const string ShaderResource="Presentation/WorldTerrain",SpriteShaderResource="Presentation/WorldSprite";

        // litEdge: how deep (m, up to one cell) lamp light enters a wall from its open edge (lighting table shadows.edge), 0 = walls lit evenly.
        // backdrops: for a composed level (FactoryGrid) the room whose options give each backdrop and its
        // bottom-left offset in the level; null = the room itself is one backdrop.
        // ownSortingGroup: false when the caller's object already sorts the terrain together with room decor
        // (a composed level: decor of installed room prefabs interleaves with the walls by sorting order).
        public static GameObject Build(RoomFile room,RoomDefinitionCatalog definitions,RoomLandCatalog lands,RoomTileArtAsset art,Vector2 origin,double litEdge,Transform parent=null,IReadOnlyList<(RoomFile options,Vector2 offset)> backdrops=null,bool ownSortingGroup=true)
        {
            if(room==null || definitions==null || lands==null || art==null) throw new ArgumentNullException(room==null?nameof(room):definitions==null?nameof(definitions):lands==null?nameof(lands):nameof(art));
            var shader=GameAssets.Load<Shader>(ShaderResource)??throw new InvalidOperationException("Нет шейдера "+ShaderResource);
            var root=new GameObject("Оформление комнаты: "+room.Template);
            if(parent!=null) root.transform.SetParent(parent,false);
            root.transform.localPosition=origin;
            if(ownSortingGroup) root.AddComponent<SortingGroup>().sortingOrder=WorldVisualOrderPolicy.SceneryRoomOrder;
            var lifetime=root.AddComponent<RoomMeshLifetime>();
            var grid=root.AddComponent<Grid>();
            float quarter=(float)room.Cell/2;
            grid.cellSize=new Vector3(quarter,quarter,0);
            RoomTerrainShapes shapes;
            using(Remains.Diagnostics.FrameHitchLog.Section("Тайлы: формы углов")) shapes=RoomTerrainShapes.Compute(room);
            if(litEdge>room.Cell) Debug.LogWarning($"{room.Template}: кромка {litEdge} м больше клетки {room.Cell} м — свет лампы заходит в стену не глубже одной клетки.");
            var edges=litEdge>0?EdgeMap(RoomShadowShapes.Edges(RoomGeometryBuilder.Build(room,definitions)),room.Cell,lifetime):null;
            var builder=new Builder(room,definitions,art,shader,root.transform,lifetime,quarter,origin,edges,(float)litEdge,shapes);
            if(backdrops==null) builder.Backdrop(lands,room,Vector2.zero);
            else foreach(var (options,offset) in backdrops) builder.Backdrop(lands,options,offset);

            for(int x=0;x<room.Width;x++)
                for(int y=0;y<room.Height;y++)
                {
                    int height=builder.Height(x,y);
                    var back=builder.Definition(RoomLayers.Back,x,y);
                    if(back!=null) builder.Cell(back,"задняя стена",BackOrder,x,y,0,true);
                    var solid=builder.Definition(RoomLayers.Solid,x,y);
                    if(solid!=null) builder.Material(solid,x,y,height);
                    var platform=builder.Definition(RoomLayers.Platform,x,y);
                    if(platform!=null) builder.Material(platform,x,y,height);
                    var ladder=builder.Definition(RoomLayers.Ladder,x,y);
                    if(ladder?.FrontSprite!=null) builder.Sprite(ladder,x,y);
                    var stairs=builder.Definition(RoomLayers.Stairs,x,y);
                    if(stairs?.FrontSprite!=null) builder.Sprite(stairs,x,y);
                }
            builder.PublishTiles();
            return root;
        }

        // Edge map for the WorldTerrain shader, one texel per cell: r = WallOpen bits, g = wall, b = top/cell.
        private static Texture2D EdgeMap(WallEdge[,] edges,double cell,RoomMeshLifetime lifetime)
        {
            int width=edges.GetLength(0),height=edges.GetLength(1);
            var texture=new Texture2D(width,height,TextureFormat.RGBA32,false,true){name="Кромки стен",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color32[width*height];
            for(int y=0;y<height;y++)
                for(int x=0;x<width;x++)
                {
                    var e=edges[x,y];
                    pixels[y*width+x]=e.Wall?new Color32((byte)e.Open,255,(byte)Math.Round(255*e.Height/cell),0):new Color32(0,0,0,0);
                }
            texture.SetPixels32(pixels);
            texture.Apply(false,true);
            lifetime.Textures.Add(texture);
            return texture;
        }

        private sealed class Builder
        {
            private readonly RoomFile room;
            private readonly RoomDefinitionCatalog definitions;
            private readonly RoomTileArtAsset art;
            private readonly Shader shader;
            private readonly Transform root;
            private readonly RoomMeshLifetime lifetime;
            private readonly float quarter;
            private readonly Vector2 origin;
            private readonly Texture2D edges;
            private readonly float litEdge;
            private readonly RoomTerrainShapes shapes;
            private readonly Dictionary<string,Tilemap> maps=new(StringComparer.Ordinal);
            private readonly Dictionary<Tilemap,Dictionary<Vector3Int,TileChangeData>> tiles=new();
            private readonly Dictionary<(RoomDefinition Definition,string Part),(Tilemap Map,TileMask Mask)> layers=new();
            private readonly Dictionary<(string Layer,char Key),RoomDefinition> resolvedDefinitions=new();
            private readonly Dictionary<TileMaskQuarter,Quarter> quarterIds=new();
            private readonly Dictionary<(int Clip,int Frame),Tile> maskTiles=new();

            public Builder(RoomFile room,RoomDefinitionCatalog definitions,RoomTileArtAsset art,Shader shader,Transform root,RoomMeshLifetime lifetime,float quarter,Vector2 origin,Texture2D edges,float litEdge,RoomTerrainShapes shapes)
            {this.room=room;this.definitions=definitions;this.art=art;this.shader=shader;this.root=root;this.lifetime=lifetime;this.quarter=quarter;this.origin=origin;this.edges=edges;this.litEdge=litEdge;this.shapes=shapes;}

            public RoomDefinition Definition(string layer,int x,int y)
            {
                if(!room.Layers.TryGetValue(layer,out var rows)) return null;
                char key=rows[room.Height-1-y][x];
                if(key==RoomFile.Empty) return null;
                if(resolvedDefinitions.TryGetValue((layer,key),out var definition))return definition;
                definition=definitions.Require(room.Palettes[layer][key],$"{room.Template} {layer} {x},{y}");
                resolvedDefinitions.Add((layer,key),definition);
                return definition;
            }

            // Room backdrop (original drawBackWall): the room's own backwall or its land default;
            // "sky" = no backdrop (far background is a separate layer, not drawn yet). backform
            // keeps only parts of the room: 1 side strips, 2 lower part from row 16, 3 from row 24.
            public void Backdrop(RoomLandCatalog lands,RoomFile room,Vector2 offset)
            {
                var issues=new List<string>();
                string name=lands.Option(room,"backwall",issues);
                foreach(string issue in issues) Debug.LogWarning(issue);
                if(name==null) {Debug.LogWarning($"{room.Template}: нет фона комнаты ни в комнате, ни у земли.");return;}
                if(name=="sky") return;
                // Original rule: a present but empty backwall overrides the land default and draws tBackWall.
                string textureId=TileManifest.TextureId(name.Length==0?"tBackWall":name);
                if(!art.Manifest.Art.ContainsKey(textureId))
                {
                    // The original substitutes tBackWall; we keep its look but say so.
                    Debug.LogWarning($"{room.Template}: фон {name} не экспортирован, нарисован tBackWall (как в оригинале при отсутствии текстуры).");
                    textureId=TileManifest.TextureId("tBackWall");
                }
                int form=room.Options.TryGetValue("backform",out string value) && int.TryParse(value,out int parsed)?parsed:0;
                float pixels=art.Manifest.CellPixels;            // source pixels per cell
                float cell=(float)room.Cell,width=room.Width*cell,height=room.Height*cell;
                float Px(float sourcePixels)=>sourcePixels/pixels*cell;
                switch(form)
                {
                    case 0: BackdropRect(textureId,offset.x,offset.y,width,height);break;
                    case 1: BackdropRect(textureId,offset.x,offset.y,Px(11*pixels-10),height);BackdropRect(textureId,offset.x+Px(37*pixels+10),offset.y,width-Px(37*pixels+10),height);break;
                    case 2: BackdropRect(textureId,offset.x,offset.y,width,height-Px(16*pixels+10));break;
                    case 3: BackdropRect(textureId,offset.x,offset.y,width,height-Px(24*pixels+10));break;
                    default: Debug.LogWarning($"{room.Template}: неизвестная форма фона backform={form}, фон не нарисован.");break;
                }
            }

            private void BackdropRect(string textureId,float x,float y,float width,float height)
            {
                if(width<=0 || height<=0) return;
                var obj=new GameObject("Фон комнаты");
                obj.transform.SetParent(root,false);
                obj.transform.localPosition=new Vector3(x+width/2,y+height/2,0);
                obj.transform.localScale=new Vector3(width,height,1);
                obj.AddComponent<MeshFilter>().sharedMesh=Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                var renderer=obj.AddComponent<MeshRenderer>();
                renderer.sharedMaterial=TerrainMaterial(textureId,false);
                renderer.sortingOrder=BackdropOrder;
            }

            public int Height(int x,int y)=>room.Layers.TryGetValue(RoomLayers.Height,out var rows) && rows[room.Height-1-y][x]!=RoomFile.Empty?rows[room.Height-1-y][x]-'0':0;

            // Solid or platform material: main, border and floor layers (Material.as order).
            public void Material(RoomDefinition definition,int x,int y,int height)
            {
                int order=definition.Rear?RearOrder:FrontOrder;
                Cell(definition,"основа",order,x,y,height,false);
                if(definition.BorderTexture!=null && definition.BorderMask!=null)
                    Layer(definition,"бордюр",definition.BorderTexture,definition.BorderMask,definition.Rear?RearOrder:BorderOrder,x,y,height,false,false);
                if(definition.FloorTexture!=null && definition.FloorMask!=null)
                    Layer(definition,"пол",definition.FloorTexture,definition.FloorMask,definition.Rear?RearOrder:FloorOrder,x,y,height,false,true);
            }

            public void Cell(RoomDefinition definition,string part,int order,int x,int y,int height,bool back)
            {
                if(definition.MainTexture==null) return;
                // Material.as: a material without its own mask uses the plain square TileMask (rule, not substitution).
                Layer(definition,part,definition.MainTexture,definition.MainMask??"TileMask",order,x,y,height,back,false);
            }

            private void Layer(RoomDefinition definition,string part,string textureId,string maskName,int order,int x,int y,int height,bool back,bool floor)
            {
                if(!layers.TryGetValue((definition,part),out var layer))
                {
                    string name=part+" "+definition.Id;
                    if(!art.Manifest.Masks.TryGetValue(maskName,out var found)) throw new InvalidOperationException($"{room.Template}: маска {maskName} не экспортирована ({name}).");
                    layer=(Map(name+"|"+textureId,name,textureId,order,order!=BackOrder),found);
                    layers.Add((definition,part),layer);
                }
                var (map,mask)=layer;
                float cell=(float)room.Cell;
                float scale=floor?1:(4-height)/4f;          // partial height squashes the shape onto the lowered top
                float drop=floor?cell*height/4f:0;           // the floor strip moves down with the lowered top
                if(mask.Kind=="whole")
                {
                    // One square shape per cell (no corner states), registered at the cell centre.
                    Place(map,MaskTile(mask.Clip,1),new Vector3Int(x*2,y*2,0),new Vector2(x*cell+cell/2,y*cell+cell/2*scale-drop),new Vector2(1,scale));
                    return;
                }
                foreach(var q in mask.Quarters)
                {
                    if(!quarterIds.TryGetValue(q,out var quarterId))
                    {
                        quarterId=(Quarter)Enum.Parse(typeof(Quarter),q.Quarter,true);
                        quarterIds.Add(q,quarterId);
                    }
                    int s=back?shapes.Back(x,y,quarterId):shapes.Front(x,y,quarterId);
                    if(s<0) continue;
                    bool top=quarterId==Quarter.TopLeft || quarterId==Quarter.TopRight;
                    bool right=quarterId==Quarter.TopRight || quarterId==Quarter.BottomRight;
                    // Registration: the cell corner of this quarter (original: centre ± half a cell, mirrored).
                    var registration=new Vector2((x+(right?1:0))*cell,y*cell+(top?cell*scale:0)-drop);
                    var cellPosition=new Vector3Int(x*2+(right?1:0),y*2+(top?1:0),0);
                    Place(map,MaskTile(q.Clip,s+1),cellPosition,registration,new Vector2(q.FlipX?-1:1,(q.FlipY?-1:1)*scale));
                }
            }
            private Tile MaskTile(int clip,int frame)
            {
                if(maskTiles.TryGetValue((clip,frame),out var tile))return tile;
                tile=art.Tile(TileManifest.MaskFrameId(clip,frame)).tile;
                maskTiles.Add((clip,frame),tile);
                return tile;
            }

            private void Place(Tilemap map,Tile tile,Vector3Int cell,Vector2 registration,Vector2 scale)
            {
                var cellOrigin=new Vector2(cell.x*quarter,cell.y*quarter);
                // If two terrain layers target the same map cell, the later write wins.
                tiles[map][cell]=new TileChangeData(cell,tile,tile.color,
                    Matrix4x4.TRS(registration-cellOrigin,Quaternion.identity,new Vector3(scale.x,scale.y,1)));
            }
            public void PublishTiles()
            {
                using var section=Remains.Diagnostics.FrameHitchLog.Section("Тайлы: публикация пакетов");
                foreach(var pair in tiles)
                {
                    var changes=new TileChangeData[pair.Value.Count];
                    pair.Value.Values.CopyTo(changes,0);
                    pair.Key.SetTiles(changes,true);
                    foreach(var change in changes)
                        if(((Tile)change.tile).flags!=TileFlags.None)pair.Key.SetTileFlags(change.position,TileFlags.None);
                }
            }

            public void Sprite(RoomDefinition definition,int x,int y)
            {
                var entry=art.Tile(definition.FrontSprite);
                var obj=new GameObject("Спрайт "+definition.Id);
                obj.transform.SetParent(root,false);
                float cell=(float)room.Cell;
                obj.transform.localPosition=new Vector3(x*cell,(y+1)*cell,0);   // original: tile top-left is the registration point
                var renderer=obj.AddComponent<SpriteRenderer>();
                renderer.sprite=entry.sprite;
                renderer.sharedMaterial=SpriteMaterial();
                renderer.sortingOrder=definition.Rear?RearSpriteOrder:FrontSpriteOrder;
            }

            // Sprites (ladders, stair sprites) share the world lighting of the terrain (WorldSprite).
            private Material SpriteMaterial()
            {
                const string key="спрайты";
                if(lifetime.Materials.TryGetValue(key,out var material)) return material;
                var spriteShader=GameAssets.Load<Shader>(SpriteShaderResource)??throw new InvalidOperationException("Нет шейдера "+SpriteShaderResource);
                material=new Material(spriteShader){name="Sprites:"+room.Template};
                lifetime.Materials.Add(key,material);
                return material;
            }

            // Walls (solid layers) fade lamp light from their open edges; backdrop and back walls do not.
            private Material TerrainMaterial(string textureId,bool wall)
            {
                bool edged=wall && edges!=null;
                string key=textureId+(wall?"|стена":"|фон")+(edged?"|кромка":"");
                if(lifetime.Materials.TryGetValue(key,out var material)) return material;
                material=new Material(shader){name="Terrain:"+key};
                // Backdrop and back walls are drawn before the original's background erase (windows).
                if(!wall) {material.SetFloat("_EraseAffected",1);material.SetFloat("_ReceiveProjectedShadow",1);}
                if(edged)
                {
                    material.SetTexture("_EdgeTex",edges);
                    material.SetFloat("_EdgeWidth",litEdge);
                    material.SetFloat("_EdgeCell",(float)room.Cell);
                    material.SetVector("_EdgeOrigin",new Vector4(origin.x,origin.y,0,0));
                }
                material.SetTexture("_MaterialTex",art.Texture(textureId));
                // Original bitmap fill starts at the room's top-left corner.
                material.SetVector("_MaterialOrigin",new Vector4(origin.x,origin.y+room.Height*(float)room.Cell,0,0));
                material.SetFloat("_TexelsPerMeter",(float)(art.Manifest.CellPixels/room.Cell));
                lifetime.Materials.Add(key,material);
                return material;
            }

            private Tilemap Map(string key,string name,string textureId,int order,bool wall)
            {
                if(maps.TryGetValue(key,out var map)) return map;
                var obj=new GameObject("Слой: "+name);
                obj.transform.SetParent(root,false);
                map=obj.AddComponent<Tilemap>();
                map.tileAnchor=Vector3.zero;
                var renderer=obj.AddComponent<TilemapRenderer>();
                renderer.sortingOrder=order;
                renderer.mode=TilemapRenderer.Mode.Chunk;
                renderer.sharedMaterial=TerrainMaterial(textureId,wall);
                maps.Add(key,map);
                tiles.Add(map,new Dictionary<Vector3Int,TileChangeData>());
                return map;
            }
        }
    }
}
