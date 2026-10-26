using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.RoomFormat;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Remains.Presentation
{
    // Light absorption of a whole level on the full light grid (one R16F texture; seed104 factory 1512×4040):
    // walls from the level data, objects by their sprites (table rule), closed doors as rectangles. Emitting shapes
    // are not absorbers here: direct light is traced from the points of their form. A door change redraws only
    // its own rectangle (base walls and objects copied back, then every blocking door over it).
    public sealed class LevelLightScene : IDisposable
    {
        public readonly struct Door
        {
            public readonly string Id;
            public readonly Rect Bounds;        // world
            public readonly double Opacity;
            public readonly bool Blocks;
            public Door(string id,Rect bounds,double opacity,bool blocks) {Id=id;Bounds=bounds;Opacity=opacity;Blocks=blocks;}
        }
        public readonly struct ObjectSprite
        {
            public readonly Sprite Sprite;
            public readonly Matrix4x4 LocalToWorld;
            public readonly double Absorption;  // what the whole object stops (table rule)
            public ObjectSprite(Sprite sprite,Matrix4x4 localToWorld,double absorption) {Sprite=sprite;LocalToWorld=localToWorld;Absorption=absorption;}
        }
        private const string SceneShaderResource="Presentation/LightScene";
        private const int AbsorptionPass=5,RectPass=6;
        private RenderTexture baseScene,scene;
        private Material material;
        private Mesh quad;
        private readonly Dictionary<string,Door> doors=new(StringComparer.Ordinal);
        private readonly List<Door> blocking=new();
        public Rect WorldRect {get;}
        public int Width {get;}
        public int Height {get;}
        public float Texel {get;}
        public int Resolution {get;}
        public Texture Texture=>scene;
        public long Bytes=>baseScene==null?0:2*LightTextureBudget.Size(Width,Height,2);
        // Increases with every door change; a direct field built at an older revision may be out of date.
        public long Revision {get;private set;}
        public IReadOnlyDictionary<string,Door> Doors=>doors;
        // Increases when a door appears, disappears or moves (not when it opens or closes).
        public long DoorSetRevision {get;private set;}

        // walls[x,y]: per-texel absorption from RoomLightScene.Absorption, y up, origin at the level's bottom-left.
        public LevelLightScene(float[,] walls,Vector2 origin,int resolution,IReadOnlyList<ObjectSprite> objects)
        {
            if(walls==null) throw new ArgumentNullException(nameof(walls));
            if(resolution<=0) throw new ArgumentOutOfRangeException(nameof(resolution));
            Width=walls.GetLength(0);Height=walls.GetLength(1);Resolution=resolution;Texel=1f/resolution;
            WorldRect=new Rect(origin,new Vector2(Width*Texel,Height*Texel));
            try
            {
                material=new Material(GameAssets.Load<Shader>(SceneShaderResource)??throw new InvalidOperationException("Нет шейдера "+SceneShaderResource)) {name="Сцена света уровня",hideFlags=HideFlags.HideAndDontSave};
                quad=LightSpriteShapes.UnitQuad("Квадрат сцены света уровня");
                baseScene=Target("Свет уровня: стены и предметы");scene=Target("Свет уровня: поглощение");
                var upload=new Texture2D(Width,Height,TextureFormat.RHalf,false,true) {name="Свет уровня: стены (загрузка)"};
                try
                {
                    var data=new ushort[Width*Height];
                    for(int y=0;y<Height;y++) for(int x=0;x<Width;x++) data[y*Width+x]=Mathf.FloatToHalf(walls[x,y]);
                    upload.SetPixelData(data,0);upload.Apply(false,false);
                    Graphics.CopyTexture(upload,baseScene);
                }
                finally {UnityEngine.Object.DestroyImmediate(upload);}
                if(objects!=null && objects.Count>0)
                {
                    using var cmd=Commands("Свет уровня: предметы",baseScene);
                    var block=new MaterialPropertyBlock();
                    foreach(var item in objects)
                    {
                        if(item.Sprite==null) throw new ArgumentException("Свет уровня: предмет без спрайта.");
                        var scale=item.LocalToWorld.lossyScale;var bounds=item.Sprite.bounds;
                        double perTexel=LightRules.ObjectTexelAbsorption(item.Absorption,Math.Min(Math.Abs(bounds.size.x*scale.x),Math.Abs(bounds.size.y*scale.y))/Texel);
                        if(perTexel<=0) continue;
                        block.Clear();block.SetTexture("_MainTex",item.Sprite.texture);block.SetFloat("_Absorption",(float)perTexel);
                        cmd.DrawMesh(quad,item.LocalToWorld*Matrix4x4.TRS(bounds.min,Quaternion.identity,bounds.size),material,0,AbsorptionPass,block);
                    }
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                Graphics.CopyTexture(baseScene,scene);
            }
            catch {Dispose();throw;}
        }

        // Applies a door snapshot; returns the texel rectangles of doors whose blocking or opacity changed (their ids
        // in changedIds). Unchanged doors cost nothing.
        public List<RectInt> SetDoors(IReadOnlyList<Door> states,List<string> changedIds)
        {
            if(states==null) throw new ArgumentNullException(nameof(states));
            changedIds?.Clear();
            var changed=new List<RectInt>();
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var door in states)
            {
                if(string.IsNullOrEmpty(door.Id) || !seen.Add(door.Id)) throw new ArgumentException("Свет уровня: дверь без ID или повтор: "+door.Id);
                if(doors.TryGetValue(door.Id,out var old) && old.Bounds==door.Bounds && old.Blocks==door.Blocks && old.Opacity==door.Opacity) continue;
                bool known=doors.ContainsKey(door.Id);
                if(known && old.Bounds!=door.Bounds) AddChange(old);
                if(!known || old.Bounds!=door.Bounds) DoorSetRevision++;
                doors[door.Id]=door;AddChange(door);
            }
            var removed=new List<string>();
            foreach(var id in doors.Keys) if(!seen.Contains(id)) removed.Add(id);
            foreach(string id in removed) {AddChange(doors[id]);doors.Remove(id);DoorSetRevision++;}
            if(changed.Count==0) return changed;
            blocking.Clear();foreach(var door in doors.Values) if(door.Blocks) blocking.Add(door);
            using var cmd=Commands("Свет уровня: двери",scene);
            foreach(var rect in changed)
            {
                cmd.CopyTexture(baseScene,0,0,rect.x,rect.y,rect.width,rect.height,scene,0,0,rect.x,rect.y);
                foreach(var door in blocking)
                {
                    var texels=Texels(door.Bounds);
                    if(!texels.Overlaps(rect)) continue;
                    var block=new MaterialPropertyBlock();
                    block.SetFloat("_Absorption",LightRules.DoorTexelAbsorption(door.Opacity,Math.Min(door.Bounds.width,door.Bounds.height)/Texel));
                    cmd.DrawMesh(quad,Matrix4x4.TRS(new Vector3(WorldRect.x+texels.x*Texel,WorldRect.y+texels.y*Texel,0),Quaternion.identity,new Vector3(texels.width*Texel,texels.height*Texel,1)),material,0,RectPass,block);
                }
            }
            Graphics.ExecuteCommandBuffer(cmd);
            Revision++;
            return changed;

            void AddChange(Door door)
            {
                var texels=Texels(door.Bounds);
                if(texels.width<=0 || texels.height<=0) return;
                changed.Add(texels);changedIds?.Add(door.Id);
            }
        }

        // The texels a world rectangle covers (snapped outwards, clamped to the level).
        public RectInt Texels(Rect world)
        {
            int left=Mathf.Clamp(Mathf.FloorToInt((world.xMin-WorldRect.x)/Texel),0,Width),right=Mathf.Clamp(Mathf.CeilToInt((world.xMax-WorldRect.x)/Texel),0,Width);
            int bottom=Mathf.Clamp(Mathf.FloorToInt((world.yMin-WorldRect.y)/Texel),0,Height),top=Mathf.Clamp(Mathf.CeilToInt((world.yMax-WorldRect.y)/Texel),0,Height);
            return new RectInt(left,bottom,Math.Max(0,right-left),Math.Max(0,top-bottom));
        }

        private CommandBuffer Commands(string name,RenderTexture target)
        {
            var cmd=new CommandBuffer {name=name};
            cmd.SetRenderTarget(target);
            cmd.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(WorldRect.xMin,WorldRect.xMax,WorldRect.yMin,WorldRect.yMax,-1,1),false));
            return cmd;
        }
        private RenderTexture Target(string name)
        {
            var texture=new RenderTexture(Width,Height,0,GraphicsFormat.R16_SFloat) {name=name,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            try
            {
                LightTextureBudget.Track(texture,LightTextureBudget.Size(Width,Height,2));
                if(!texture.Create()) throw new InvalidOperationException("Не создана текстура света уровня "+name);
                return texture;
            }
            catch {LightTextureBudget.Release(texture);throw;}
        }
        public void Dispose()
        {
            LightTextureBudget.Release(baseScene);LightTextureBudget.Release(scene);baseScene=scene=null;
            if(material!=null) UnityEngine.Object.DestroyImmediate(material);
            if(quad!=null) UnityEngine.Object.DestroyImmediate(quad);
            material=null;quad=null;doors.Clear();blocking.Clear();
        }
    }
}
