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
        // Draw order inside the room's SortingGroup (same scale as RoomVisual).
        public const int BackOrder=-29,RearOrder=-6,FrontOrder=-4,BorderOrder=-3,FloorOrder=-2,RearSpriteOrder=-5,FrontSpriteOrder=1;
        private const string ShaderResource="Presentation/WorldTerrain";

        public static GameObject Build(RoomFile room,RoomDefinitionCatalog definitions,RoomTileArtAsset art,Vector2 origin,Transform parent=null)
        {
            if(room==null || definitions==null || art==null) throw new ArgumentNullException(room==null?nameof(room):definitions==null?nameof(definitions):nameof(art));
            var shader=Resources.Load<Shader>(ShaderResource)??throw new InvalidOperationException("Нет шейдера "+ShaderResource);
            var root=new GameObject("Оформление комнаты: "+room.Template);
            if(parent!=null) root.transform.SetParent(parent,false);
            root.transform.localPosition=origin;
            root.AddComponent<SortingGroup>().sortingOrder=WorldVisualOrderPolicy.SceneryRoomOrder;
            var lifetime=root.AddComponent<RoomMeshLifetime>();
            var grid=root.AddComponent<Grid>();
            float quarter=(float)room.Cell/2;
            grid.cellSize=new Vector3(quarter,quarter,0);
            var shapes=RoomTerrainShapes.Compute(room);
            var builder=new Builder(room,definitions,art,shader,root.transform,lifetime,quarter,origin);

            for(int x=0;x<room.Width;x++)
                for(int y=0;y<room.Height;y++)
                {
                    int height=builder.Height(x,y);
                    var back=builder.Definition(RoomLayers.Back,x,y);
                    if(back!=null) builder.Cell(back,"задняя стена",BackOrder,x,y,q=>shapes.Back(x,y,q),0,false);
                    var solid=builder.Definition(RoomLayers.Solid,x,y);
                    if(solid!=null) builder.Material(solid,x,y,q=>shapes.Front(x,y,q),height);
                    var platform=builder.Definition(RoomLayers.Platform,x,y);
                    if(platform!=null) builder.Material(platform,x,y,q=>shapes.Front(x,y,q),height);
                    foreach(string layer in new[]{RoomLayers.Ladder,RoomLayers.Stairs})
                    {
                        var definition=builder.Definition(layer,x,y);
                        if(definition?.FrontSprite!=null) builder.Sprite(definition,x,y);
                    }
                }
            return root;
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
            private readonly Dictionary<string,Tilemap> maps=new(StringComparer.Ordinal);

            public Builder(RoomFile room,RoomDefinitionCatalog definitions,RoomTileArtAsset art,Shader shader,Transform root,RoomMeshLifetime lifetime,float quarter,Vector2 origin)
            {this.room=room;this.definitions=definitions;this.art=art;this.shader=shader;this.root=root;this.lifetime=lifetime;this.quarter=quarter;this.origin=origin;}

            public RoomDefinition Definition(string layer,int x,int y)
            {
                if(!room.Layers.TryGetValue(layer,out var rows)) return null;
                char key=rows[room.Height-1-y][x];
                if(key==RoomFile.Empty) return null;
                return definitions.Require(room.Palettes[layer][key],$"{room.Template} {layer} {x},{y}");
            }

            public int Height(int x,int y)=>room.Layers.TryGetValue(RoomLayers.Height,out var rows) && rows[room.Height-1-y][x]!=RoomFile.Empty?rows[room.Height-1-y][x]-'0':0;

            // Solid or platform material: main, border and floor layers (Material.as order).
            public void Material(RoomDefinition definition,int x,int y,Func<Quarter,int> state,int height)
            {
                int order=definition.Rear?RearOrder:FrontOrder;
                Cell(definition,"основа",order,x,y,state,height,false);
                if(definition.BorderTexture!=null && definition.BorderMask!=null)
                    Layer(definition.BorderTexture,definition.BorderMask,"бордюр "+definition.Id,definition.Rear?RearOrder:BorderOrder,x,y,state,height,false);
                if(definition.FloorTexture!=null && definition.FloorMask!=null)
                    Layer(definition.FloorTexture,definition.FloorMask,"пол "+definition.Id,definition.Rear?RearOrder:FloorOrder,x,y,state,height,true);
            }

            public void Cell(RoomDefinition definition,string part,int order,int x,int y,Func<Quarter,int> state,int height,bool floor)
            {
                if(definition.MainTexture==null) return;
                // Material.as: a material without its own mask uses the plain square TileMask (rule, not substitution).
                Layer(definition.MainTexture,definition.MainMask??"TileMask",part+" "+definition.Id,order,x,y,state,height,floor);
            }

            private void Layer(string textureId,string maskName,string name,int order,int x,int y,Func<Quarter,int> state,int height,bool floor)
            {
                var manifest=art.Manifest;
                if(!manifest.Masks.TryGetValue(maskName,out var mask)) throw new InvalidOperationException($"{room.Template}: маска {maskName} не экспортирована ({name}).");
                var map=Map(name+"|"+textureId,name,textureId,order);
                float cell=(float)room.Cell;
                float scale=floor?1:(4-height)/4f;          // partial height squashes the shape onto the lowered top
                float drop=floor?cell*height/4f:0;           // the floor strip moves down with the lowered top
                if(mask.Kind=="whole")
                {
                    // One square shape per cell (no corner states), registered at the cell centre.
                    var entry=art.Tile(TileManifest.MaskFrameId(mask.Clip,1));
                    Place(map,entry.tile,new Vector3Int(x*2,y*2,0),new Vector2(x*cell+cell/2,y*cell+cell/2*scale-drop),new Vector2(1,scale));
                    return;
                }
                foreach(var q in mask.Quarters)
                {
                    var quarterId=(Quarter)Enum.Parse(typeof(Quarter),q.Quarter,true);
                    int s=state(quarterId);
                    if(s<0) continue;
                    bool top=quarterId==Quarter.TopLeft || quarterId==Quarter.TopRight;
                    bool right=quarterId==Quarter.TopRight || quarterId==Quarter.BottomRight;
                    // Registration: the cell corner of this quarter (original: centre ± half a cell, mirrored).
                    var registration=new Vector2((x+(right?1:0))*cell,y*cell+(top?cell*scale:0)-drop);
                    var cellPosition=new Vector3Int(x*2+(right?1:0),y*2+(top?1:0),0);
                    var entry=art.Tile(TileManifest.MaskFrameId(q.Clip,s+1));
                    Place(map,entry.tile,cellPosition,registration,new Vector2(q.FlipX?-1:1,(q.FlipY?-1:1)*scale));
                }
            }

            private void Place(Tilemap map,Tile tile,Vector3Int cell,Vector2 registration,Vector2 scale)
            {
                var cellOrigin=new Vector2(cell.x*quarter,cell.y*quarter);
                map.SetTile(cell,tile);
                map.SetTileFlags(cell,TileFlags.None);
                map.SetTransformMatrix(cell,Matrix4x4.TRS(registration-cellOrigin,Quaternion.identity,new Vector3(scale.x,scale.y,1)));
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
                renderer.sortingOrder=definition.Rear?RearSpriteOrder:FrontSpriteOrder;
            }

            private Tilemap Map(string key,string name,string textureId,int order)
            {
                if(maps.TryGetValue(key,out var map)) return map;
                var obj=new GameObject("Слой: "+name);
                obj.transform.SetParent(root,false);
                map=obj.AddComponent<Tilemap>();
                map.tileAnchor=Vector3.zero;
                var renderer=obj.AddComponent<TilemapRenderer>();
                renderer.sortingOrder=order;
                renderer.mode=TilemapRenderer.Mode.Chunk;
                if(!lifetime.Materials.TryGetValue(textureId,out var material))
                {
                    material=new Material(shader){name="Terrain:"+textureId};
                    material.SetTexture("_MaterialTex",art.Texture(textureId));
                    // Original bitmap fill starts at the room's top-left corner.
                    material.SetVector("_MaterialOrigin",new Vector4(origin.x,origin.y+room.Height*(float)room.Cell,0,0));
                    material.SetFloat("_TexelsPerMeter",(float)(art.Manifest.CellPixels/room.Cell));
                    lifetime.Materials.Add(textureId,material);
                }
                renderer.sharedMaterial=material;
                maps.Add(key,map);
                return map;
            }
        }
    }
}
