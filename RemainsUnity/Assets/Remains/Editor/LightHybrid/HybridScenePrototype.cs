using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Remains.Content;
using Remains.Presentation;
using Remains.RoomFormat;
using UnityEngine;
using UnityEngine.Rendering;

namespace Remains.Editor
{
    // Decision preview only. Stop the actual scene painter before AllocateSteps: no fine whole-level cascades.
    public sealed class HybridScenePrototype : IDisposable
    {
        private RoomRadianceField field;
        private readonly List<SteadyLightEmitter> emitters=new();
        private readonly Dictionary<string,Vector4[]> sampledGlows=new(StringComparer.Ordinal);
        private readonly Dictionary<string,float> glowWidths=new(StringComparer.Ordinal);
        private static readonly Dictionary<(Sprite,float),GlowPixels> glowPixels=new();
        private static readonly Dictionary<(Sprite,int),Vector3[]> shapeSamples=new();
        public Texture Scene {get;private set;}
        public Rect WorldRect {get;private set;}
        public int Width {get;private set;}
        public int Height {get;private set;}
        public int GlowSources {get;private set;}
        public int LampSources {get;private set;}
        public int WindowSources {get;private set;}
        public int FineResolution {get;private set;}
        // CPU-uploaded walls texture plus sprite-painted target plus door-occlusion target.
        public long SceneBytes=>(long)Width*Height*8*3;
        public long SceneCpuReadableBytes=>(long)Width*Height*8;
        public IReadOnlyList<SteadyLightEmitter> Emitters=>emitters;
        public string GlowApproximation=>"Яркие пиксели экранов: до 128×128 линейных выборок, восемь энергетических столбцов, средний цвет источника; геометрия поглощения и исходное RGB сцены — штатный painter без этого упрощения.";
        public string ShapeApproximation=>"Лампы/окна: до восьми точек в сетке 4×2 по штатной alpha-маске на fine-разрешении; каждая точка лежит внутри маски, вес равен доле площади страты.";

        public HybridScenePrototype(FactoryLevelView view,FactoryLevelRoot factory)
        {
            if(view==null || factory==null) throw new ArgumentNullException("Нет вида/корня для прототипа сцены.");
            if(factory.transform.lossyScale!=Vector3.one || factory.transform.rotation!=Quaternion.identity)
                throw new InvalidOperationException("Прототип сцены ожидает единичный масштаб и поворот заводского корня.");
            IEnumerator work=null;
            try
            {
                var level=new FactoryTemplateLevel(factory);var table=view.ShadowSettings;
                FineResolution=table.RadianceResolution;
                var pictures=Private<List<(RoomSpriteAuthoring sprite,RoomDefinition definition)>>(view,"pictures");
                var lampIds=Private<Dictionary<RoomSpriteAuthoring,string>>(view,"lampByPicture");
                var windowIds=Private<Dictionary<RoomSpriteAuthoring,string>>(view,"windowSourceIds");
                var shown=typeof(FactoryLevelView).GetMethod("ShownForPreparation",BindingFlags.NonPublic|BindingFlags.Instance);
                var dark=typeof(FactoryLevelView).GetMethod("IsDark",BindingFlags.NonPublic|BindingFlags.Instance);
                if(shown==null || dark==null) throw new InvalidOperationException("Изменился штатный интерфейс подготовки картинок света.");
                var placements=new List<RoomSpritePlacement>();var authored=new List<RoomSpriteAuthoring>();
                foreach(var pair in pictures)
                {
                    if(!(bool)shown.Invoke(view,new object[]{pair.sprite})) continue;
                    var renderer=pair.sprite.GetComponent<SpriteRenderer>();
                    if(renderer==null || renderer.sprite==null) throw new InvalidOperationException("У картинки прототипа нет спрайта: "+pair.sprite.name);
                    placements.Add(new RoomSpritePlacement {Definition=pair.definition,Sprite=renderer.sprite,Frame=pair.sprite.Frame,
                        LocalToWorld=pair.sprite.transform.localToWorldMatrix,Erase=pair.sprite.Erase,
                        Dark=(bool)dark.Invoke(view,new object[]{pair.sprite})});authored.Add(pair.sprite);
                }
                var prepared=RoomRadianceField.PreparedScene.Create(level.Grid,level.Definitions,level.Lands,table);
                var occluders=(IReadOnlyList<(Rect bounds,double opacity)>)typeof(FactoryLevelView)
                    .GetMethod("WorldDoorOccluders",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);
                Vector2 origin=factory.transform.TransformPoint(Vector3.zero);
                var initialize=typeof(RoomRadianceField).GetMethod("InitializeSteps",BindingFlags.Static|BindingFlags.NonPublic);
                work=(IEnumerator)initialize.Invoke(null,new object[]{prepared,level.Lands,origin,factory.transform,
                    new Action<RoomRadianceField>(created=>field=created),placements,occluders,new LightShaderContext()});
                // #1 CPU scene upload; #2 sprites; #3 snapped door occluders. The next MoveNext allocates cascades.
                for(int i=0;i<3;i++) if(!work.MoveNext()) throw new InvalidOperationException("Подготовка сцены завершилась до трёх ожидаемых этапов.");
                if(field==null || Private<RenderTexture>(field,"cascadeA")!=null)
                    throw new InvalidOperationException("Прототип сцены неожиданно выделил каскады полного разрешения.");
                Scene=Private<Texture>(field,"scene");Width=Scene.width;Height=Scene.height;
                Vector4 rect=Private<Vector4>(field,"rect");WorldRect=new Rect(rect.x,rect.y,rect.z,rect.w);
                for(int i=0;i<placements.Count;i++)
                {
                    var placement=placements[i];var picture=authored[i];
                    if(lampIds.TryGetValue(picture,out var lamp) && field.TryCreateSteadyEmitter(lamp,placement,false,out var lampEmitter))
                    {emitters.Add(lampEmitter);LampSources++;}
                    if(windowIds.TryGetValue(picture,out var window) && field.TryCreateSteadyEmitter(window,placement,true,out var windowEmitter))
                    {emitters.Add(windowEmitter);WindowSources++;}
                    // Lamp emission takes the lamp branch in LightScene and never uses the glow branch.
                    if(!table.Lamps.ContainsKey(placement.Definition.Id) && table.Glow.TryGetValue(placement.Definition.Id,out var glow))
                        AddGlow("glow:"+level.Level+":"+i,placement,(float)glow.Gain,(float)glow.Threshold);
                }
                Debug.Log($"Гибрид, fine-сцена: {Width}×{Height}, ламп={LampSources}, окон={WindowSources}, экранов={GlowSources}; полные fine-каскады не выделялись. {GlowApproximation}");
            }
            catch {Dispose();throw;}
            finally {(work as IDisposable)?.Dispose();}
        }
        public Vector4[] Samples(SteadyLightEmitter emitter)
        {
            if(sampledGlows.TryGetValue(emitter.Id,out var glow)) return glow;
            if(!shapeSamples.TryGetValue((emitter.Sprite,FineResolution),out var local))
                shapeSamples.Add((emitter.Sprite,FineResolution),local=ReadShape(emitter.Sprite));
            var result=new Vector4[local.Length];
            for(int i=0;i<result.Length;i++)
            {
                var p=emitter.LocalToWorld.MultiplyPoint3x4(new Vector3(local[i].x,local[i].y,0));
                result[i]=new Vector4(p.x,p.y,local[i].z,0);
            }
            return result;
        }
        private Vector3[] ReadShape(Sprite sprite)
        {
            float texel=1f/FineResolution;var bounds=sprite.bounds;
            int width=Mathf.CeilToInt(bounds.size.x/texel)+2,height=Mathf.CeilToInt(bounds.size.y/texel)+2;
            var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
            var image=new Texture2D(width,height,TextureFormat.RGBAHalf,false,true);var previous=RenderTexture.active;
            try
            {
                using(var command=new CommandBuffer {name="Гибрид: двумерная форма источника"})
                {
                    command.SetRenderTarget(target);command.ClearRenderTarget(false,true,Color.clear);
                    command.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(
                        bounds.min.x-texel,bounds.min.x-texel+width*texel,bounds.min.y-texel,bounds.min.y-texel+height*texel,-1,1),false));
                    var properties=new MaterialPropertyBlock();properties.SetTexture("_MainTex",sprite.texture);
                    properties.SetColor("_Emission",Color.white);properties.SetFloat("_Absorption",0);properties.SetFloat("_Glow",0);
                    properties.SetVector("_SpriteUvRect",new Vector4(0,0,1,1));
                    command.DrawMesh(Private<Mesh>(field,"quad"),Matrix4x4.TRS(bounds.min,Quaternion.identity,bounds.size),
                        Private<Material>(field,"sceneMaterial"),0,0,properties);
                    Graphics.ExecuteCommandBuffer(command);
                }
                RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply(false);
                var pixels=image.GetPixels();var cells=new List<Vector2>[8];var sums=new Vector2[8];int total=0;
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    if(pixels[y*width+x].r<=.5f) continue;
                    int bin=Mathf.Min(3,x*4/width)+4*Mathf.Min(1,y*2/height);
                    var point=new Vector2(bounds.min.x-texel+(x+.5f)*texel,bounds.min.y-texel+(y+.5f)*texel);
                    (cells[bin]??=new List<Vector2>()).Add(point);sums[bin]+=point;total++;
                }
                if(total==0) throw new InvalidOperationException("Пустая alpha-форма источника гибрида: "+sprite.name);
                var result=new List<Vector3>(8);
                for(int i=0;i<8;i++)
                {
                    if(cells[i]==null) continue;
                    var center=sums[i]/cells[i].Count;var selected=cells[i][0];float distance=float.PositiveInfinity;
                    // A stratum centroid may land in the hole of a window frame. Keep the representative on its emitting mask.
                    foreach(var candidate in cells[i])
                    {float d=(candidate-center).sqrMagnitude;if(d<distance){distance=d;selected=candidate;}}
                    result.Add(new Vector3(selected.x,selected.y,(float)cells[i].Count/total));
                }
                return result.ToArray();
            }
            finally {RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(image);}
        }
        public float ProjectedWidth(SteadyLightEmitter emitter)=>glowWidths.TryGetValue(emitter.Id,out var width)?width:field.SteadyEmitterWidth(emitter);
        private void AddGlow(string id,RoomSpritePlacement placement,float gain,float threshold)
        {
            if(gain<=0) return;
            if(!glowPixels.TryGetValue((placement.Sprite,threshold),out var glow))
                glowPixels.Add((placement.Sprite,threshold),glow=ReadGlow(placement.Sprite,threshold));
            if(glow.Points.Length==0) return;
            var bounds=placement.Sprite.bounds;var samples=new Vector4[glow.Points.Length];
            for(int i=0;i<samples.Length;i++)
            {
                var p=glow.Points[i];var world=placement.LocalToWorld.MultiplyPoint3x4(new Vector3(bounds.min.x+p.x*bounds.size.x,bounds.min.y+p.y*bounds.size.y,0));
                samples[i]=new Vector4(world.x,world.y,p.z,0);
            }
            var scale=placement.LocalToWorld.lossyScale;
            float area=glow.AreaFraction*bounds.size.x*bounds.size.y*Mathf.Abs(scale.x*scale.y);
            sampledGlows.Add(id,samples);glowWidths.Add(id,Mathf.Max(1f/FineResolution,2*Mathf.Sqrt(area/Mathf.PI)));
            emitters.Add(new SteadyLightEmitter(id,placement.Sprite,placement.LocalToWorld,glow.Color*gain));GlowSources++;
        }
        private sealed class GlowPixels {public Color Color;public Vector3[] Points;public float AreaFraction;}
        private static GlowPixels ReadGlow(Sprite sprite,float threshold)
        {
            int width=Mathf.Min(128,sprite.texture.width),height=Mathf.Min(128,sprite.texture.height);
            var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
            var image=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);var previous=RenderTexture.active;
            try
            {
                Graphics.Blit(sprite.texture,target);RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply(false);
                var pixels=image.GetPixels();var sums=new Vector2[8];var energies=new float[8];float energy=0,area=0;Color color=Color.clear;
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    var c=pixels[y*width+x];float luminance=.2126f*c.r+.7152f*c.g+.0722f*c.b;
                    float t=Mathf.Clamp01((luminance-threshold)/.15f),coverage=c.a*t*t*(3-2*t),e=luminance*coverage;
                    if(e<=0) continue;int bin=Mathf.Min(7,x*8/width);
                    sums[bin]+=new Vector2((x+.5f)/width,(y+.5f)/height)*e;energies[bin]+=e;
                    energy+=e;area+=coverage;color+=c*coverage;
                }
                var points=new List<Vector3>();
                for(int i=0;i<8;i++) if(energies[i]>0) points.Add(new Vector3(sums[i].x/energies[i],sums[i].y/energies[i],energies[i]/energy));
                return new GlowPixels {Points=points.ToArray(),Color=area>0?color/area:Color.clear,AreaFraction=area/(width*height)};
            }
            finally {RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.DestroyImmediate(image);}
        }
        private static T Private<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);
        public void Dispose()
        {
            if(field!=null) {field.ReleaseResources();UnityEngine.Object.DestroyImmediate(field.gameObject);field=null;}
            Scene=null;
        }
    }
}
