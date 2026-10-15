using System;
using System.Collections.Generic;
using Remains.RoomFormat;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Remains.Presentation
{
    // Lamp light of one room by radiance cascades (prototype, experiment after the reference tracer):
    // flatland transport on the GPU, same model as RoomConvert trace. Wall bounce: each update reflects
    // the previous update's light once (multi-bounce settles over a few updates; lamps off → it fades).
    // Scene texture: the wall raster (RoomLightScene) plus, when the room's sprites are given
    // (RoomDecorView), lamp decor emitting over its silhouette (an "off" frame emits nothing) and
    // objects absorbing by RoomLightingTable.ObjectTransmittance; without sprites, lamp disks at the decor
    // positions (as the reference tracer). Render() recomputes on
    // demand (scene change), not per frame. The result is a world-space fluence texture read by
    // WorldTerrain instead of URP lamp lights (_RemainsRadiance*, one room at a time for now).
    public sealed class RoomRadianceField : MonoBehaviour
    {
        private const double MaxObjectTexelAbsorption=0.985;
        private const string ComputeResource="Presentation/RadianceCascades",SceneShaderResource="Presentation/LightScene";
        private ComputeShader compute;
        private Texture scene;
        private Texture2D wallsTexture;
        private Material sceneMaterial;
        private Mesh quad;
        private RenderTexture seedsA,seedsB,distance,cascadeA,cascadeB,fluenceRaw,fluence,bounceA,bounceB;
        private RenderTexture fillSamples,fillWeights;
        private float albedo,edgeTexels,rimTexels;
        private bool objectGradient;
        public int OccluderTexels {get;private set;}   // diagnostics: scene texels set by occluders
        private int width,height,levels;
        private Vector4 rect;
        private float texel;
        private Texture baseScene;
        private RoomFile sourceRoom;
        private RoomLandCatalog sourceLands;
        private RoomLightingTable sourceTable;
        private RoomLightScene sourceLight;
        private RenderTexture occlusionScene;
        public int SilhouetteReadbacks {get;private set;}
        public int OcclusionUpdates {get;private set;}
        public int StaticRenderUpdates {get;private set;}
        private Texture staticScene;
        private RenderTexture transientScene;
        private bool distanceReady;
        private bool transientActive;
        private RenderTexture staticFluence,directFluence,projectedShadowMask,backgroundFluence,backgroundAccumulation;
        private MaterialPropertyBlock emissionBlock;
        private CommandBuffer transientCommands;
        private readonly SteadyLightShadowCache steadyShadows=new();
        private readonly HashSet<string> composedSteadyIds=new(StringComparer.Ordinal);
        private readonly HashSet<string> warnedSteadyIds=new(StringComparer.Ordinal);
        private int backgroundSignature;
        private bool hasBackgroundSignature;
        private bool hasWindowEmission;
        private Color windowEmission;
        public int SteadyShadowFieldCount=>steadyShadows.Count;
        public int SteadyShadowGeneration=>steadyShadows.Generation;
        public bool TryGetSteadyShadowField(string id,out Texture field)
        {bool found=steadyShadows.TryGet(id,out var target);field=target;return found;}
        public void RegisterSteadyEmitters(IReadOnlyList<SteadyLightEmitter> sources)
        {steadyShadows.Register(sources);hasBackgroundSignature=false;warnedSteadyIds.Clear();}
        public void PrepareSteadyShadowFields(IReadOnlyCollection<string> ids)
        {using(Remains.Diagnostics.FrameHitchLog.Section("Свет: поля прямого света для теней")) steadyShadows.Prepare(ids,BuildSteadyDirect);hasBackgroundSignature=false;}

        private RenderTexture BuildSteadyDirect(SteadyLightEmitter source)
        {
            if(staticScene==null) staticScene=scene;
            var restore=scene;
            try
            {
                // Preserve the full scene's sampling geometry, including non-absorbing glow pixels.
                // Isolating RGB must not change the ray steps or the contribution decomposition.
                if(!distanceReady || !freeReady)
                {
                    scene=staticScene;distanceReady=false;RenderField(true);
                    Graphics.CopyTexture(directFluence,freeFluence);freeReady=true;
                }
                compute.SetInts("_Size",width,height);
                int clear=compute.FindKernel("ClearEmission");
                compute.SetTexture(clear,"_Scene",staticScene);compute.SetTexture(clear,"_EmissionScene",transientScene);
                compute.Dispatch(clear,(width+7)/8,(height+7)/8,1);
                var cmd=transientCommands;cmd.Clear();cmd.SetRenderTarget(transientScene);
                cmd.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(rect.x,rect.x+rect.z,rect.y,rect.y+rect.w,-1,1),false));
                emissionBlock.Clear();emissionBlock.SetTexture("_MainTex",source.Sprite.texture);
                emissionBlock.SetVector("_SpriteUvRect",new Vector4(0,0,1,1));
                emissionBlock.SetColor("_Emission",source.Radiance);emissionBlock.SetFloat("_Glow",0);
                var bounds=source.Sprite.bounds;
                cmd.DrawMesh(quad,transform.worldToLocalMatrix*source.LocalToWorld*Matrix4x4.TRS(bounds.min,Quaternion.identity,bounds.size),sceneMaterial,0,1,emissionBlock);
                Graphics.ExecuteCommandBuffer(cmd);
                scene=transientScene;RenderField(true);
                var target=Target(width,height,GraphicsFormat.R16G16B16A16_SFloat,"Прямой вклад "+source.Id);
                target.filterMode=FilterMode.Bilinear;
                int apply=compute.FindKernel("ApplyStaticFill");
                compute.SetTexture(apply,"_FillSamples",fillSamples);compute.SetTexture(apply,"_FillWeights",fillWeights);
                compute.SetTexture(apply,"_FluenceRaw",fluenceRaw);compute.SetTexture(apply,"_MappedDirect",target);
                compute.Dispatch(apply,(width+7)/8,(height+7)/8,1);return target;
            }
            finally {scene=restore;distanceReady=restore==staticScene;}
        }

        private void ComposeSteadyShadows(IReadOnlyList<ProjectedLightShadow> masks)
        {
            Graphics.Blit(staticFluence,backgroundAccumulation);composedSteadyIds.Clear();
            if(masks!=null) foreach(var mask in masks)
            {
                if(composedSteadyIds.Contains(mask.SourceId)) continue;
                if(!steadyShadows.TryGet(mask.SourceId,out var direct))
                {
                    if(steadyShadows.IsRegistered(mask.SourceId) && warnedSteadyIds.Add(mask.SourceId))
                        Debug.LogWarning($"Поле света {name}: прямой вклад {mask.SourceId} ещё не подготовлен, поколение {steadyShadows.Generation}; тень пропущена до явной подготовки.",this);
                    continue; // Flash IDs are handled separately; unknown IDs are diagnosed during preparation.
                }
                composedSteadyIds.Add(mask.SourceId);
                DrawProjectedMask(masks,mask.SourceId);
                int subtract=compute.FindKernel("SubtractSteadyDirect");
                compute.SetTexture(subtract,"_SteadyDirect",direct);compute.SetTexture(subtract,"_ProjectedShadowMask",projectedShadowMask);
                compute.SetTexture(subtract,"_BackgroundFluence",backgroundAccumulation);
                compute.Dispatch(subtract,(width+7)/8,(height+7)/8,1);
            }
        }

        public readonly struct SpriteEmission
        {
            public readonly Sprite Sprite;
            public readonly Matrix4x4 LocalToWorld;
            public readonly float Gain;
            public SpriteEmission(Sprite sprite,Matrix4x4 localToWorld,float gain)
            {Sprite=sprite;LocalToWorld=localToWorld;Gain=gain;}
        }

        public readonly struct LightOccluder
        {
            public readonly Sprite Sprite;
            public readonly Mesh Mesh;
            public readonly Texture Texture;
            public readonly Matrix4x4 LocalToWorld;
            public readonly float Opacity;
            public LightOccluder(Sprite sprite,Matrix4x4 matrix,float opacity=1)
            {Sprite=sprite;Mesh=null;Texture=null;LocalToWorld=matrix;Opacity=opacity;}
            public LightOccluder(Mesh mesh,Texture texture,Matrix4x4 matrix,float opacity=1)
            {Sprite=null;Mesh=mesh;Texture=texture;LocalToWorld=matrix;Opacity=opacity;}
        }

        // Actor and weapon shadows are projected by the presentation renderer. They do not change
        // flatland transport. Temporary sources add direct light without altering lamp bounce history.
        public bool RenderTransient(IReadOnlyList<SpriteEmission> emissions,IReadOnlyList<LightOccluder> occluders=null)
            =>RenderTransient(emissions,null,null);
        public bool RenderTransient(IReadOnlyList<SpriteEmission> emissions,IReadOnlyList<ProjectedLightShadow> masks,IReadOnlyList<string> sourceIds)
        {
            compute.SetInts("_Size",width,height);
            var hash=new HashCode();hash.Add(steadyShadows.Generation);
            if(masks!=null) foreach(var mask in masks)
            {hash.Add(mask.SourceId);hash.Add(mask.Mask!=null?mask.Mask.GetInstanceID():0);hash.Add(mask.WorldBounds);hash.Add(mask.Opacity);hash.Add(mask.Revision);}
            int signature=hash.ToHashCode();
            if((emissions?.Count??0)==0)
            {
                if(!transientActive && hasBackgroundSignature && signature==backgroundSignature) return false;
                if(transientActive) Graphics.CopyTexture(staticFluence,fluence);
                ComposeSteadyShadows(masks);
                Graphics.Blit(backgroundAccumulation,backgroundFluence);
                Shader.SetGlobalTexture("_RemainsBackgroundRadiance",backgroundFluence);
                Shader.SetGlobalFloat("_RemainsBackgroundRadianceEnabled",composedSteadyIds.Count>0?1:0);
                transientActive=false;
                backgroundSignature=signature;hasBackgroundSignature=true;
                return true;
            }
            if(masks!=null && masks.Count>0 && sourceIds==null) throw new ArgumentException("Для масок тени необходимы ID источников.");
            if(sourceIds!=null && sourceIds.Count!=emissions.Count) throw new ArgumentException("Количество ID источников не совпадает с количеством вспышек.");
            if(staticScene==null) staticScene=scene;
            if(!transientActive) Graphics.CopyTexture(fluence,staticFluence);
            try
            {
                if(!freeReady)
                {
                    scene=staticScene;distanceReady=false;
                    RenderField(true);
                    Graphics.CopyTexture(directFluence,freeFluence);
                    freeReady=true;
                }
                Graphics.CopyTexture(staticFluence,fluence);
                ComposeSteadyShadows(masks);
                for(int emissionIndex=0;emissionIndex<emissions.Count;emissionIndex++)
                {
                var previous=RenderTexture.active;
                try {Graphics.Blit(staticScene,transientScene);}
                finally {RenderTexture.active=previous;}
                {
                    var cmd=transientCommands;cmd.Clear();
                    cmd.SetRenderTarget(transientScene);
                    cmd.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(rect.x,rect.x+rect.z,rect.y,rect.y+rect.w,-1,1),false));
                    var emission=emissions[emissionIndex];
                    {
                        if(emission.Sprite==null || !float.IsFinite(emission.Gain) || emission.Gain<=0)
                        {Debug.LogWarning($"Поле излучения {name}: некорректная маска вспышки или сила {emission.Gain}.",this);continue;}
                        var sprite=emission.Sprite;
                        if(sprite.packed && (sprite.packingMode==SpritePackingMode.Tight || sprite.packingRotation!=SpritePackingRotation.None))
                        {Debug.LogWarning($"Поле излучения {name}: маска вспышки {sprite.name} упакована с обрезкой или поворотом; требуется прямоугольный спрайт без поворота.",this);continue;}
                        var bounds=sprite.bounds;
                        var uv=sprite.textureRect;
                        emissionBlock.Clear();
                        emissionBlock.SetTexture("_MainTex",sprite.texture);
                        emissionBlock.SetVector("_SpriteUvRect",new Vector4(uv.x/sprite.texture.width,uv.y/sprite.texture.height,uv.width/sprite.texture.width,uv.height/sprite.texture.height));
                        emissionBlock.SetFloat("_Glow",emission.Gain);
                        emissionBlock.SetFloat("_GlowThreshold",-0.15f);
                        cmd.DrawMesh(quad,transform.worldToLocalMatrix*emission.LocalToWorld*Matrix4x4.TRS(bounds.min,Quaternion.identity,bounds.size),sceneMaterial,0,1,emissionBlock);
                    }
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                scene=transientScene;distanceReady=false;
                RenderField(true);
                DrawProjectedMask(masks,sourceIds==null?null:sourceIds[emissionIndex]);
                int combine=compute.FindKernel("CombineDirect");
                compute.SetTexture(combine,"_ProjectedShadowMask",projectedShadowMask);
                compute.SetTexture(combine,"_FreeFluence",freeFluence);
                compute.SetTexture(combine,"_DirectFluence",directFluence);
                compute.SetTexture(combine,"_Fluence",fluence);
                compute.SetTexture(combine,"_BackgroundFluence",backgroundAccumulation);
                compute.Dispatch(combine,(width+7)/8,(height+7)/8,1);
                }
            }
            finally {scene=staticScene;distanceReady=false;}
            Graphics.Blit(backgroundAccumulation,backgroundFluence);
            Shader.SetGlobalTexture("_RemainsBackgroundRadiance",backgroundFluence);
            Shader.SetGlobalFloat("_RemainsBackgroundRadianceEnabled",1);
            transientActive=true;DynamicUpdates++;
            hasBackgroundSignature=false;
            return true;
        }

        private void PrepareTransientTargets()
        {
            transientCommands=new CommandBuffer{name="Вспышка и маска тени"};
            transientScene=Target(width,height,GraphicsFormat.R16G16B16A16_SFloat,"Сцена динамического света");
            directFluence=Target(width,height,GraphicsFormat.R16G16B16A16_SFloat,"Прямой свет вспышки");
            freeFluence=Target(width,height,GraphicsFormat.R16G16B16A16_SFloat,"Прямой свет ламп");
            staticFluence=Target(width,height,GraphicsFormat.R16G16B16A16_SFloat,"Сохранённый свет ламп");
            projectedShadowMask=Target(width,height,GraphicsFormat.R16G16B16A16_SFloat,"Маска тени вспышки");
            backgroundFluence=Target(width,height,GraphicsFormat.R16G16B16A16_SFloat,"Свет фона с тенью вспышки");
            backgroundAccumulation=Target(width,height,GraphicsFormat.R32G32B32A32_SFloat,"Композиция прямого света без промежуточного округления");
            backgroundFluence.filterMode=FilterMode.Bilinear;
        }

        private void DrawProjectedMask(IReadOnlyList<ProjectedLightShadow> masks,string sourceId)
        {
            {
                var cmd=transientCommands;cmd.Clear();
                cmd.SetRenderTarget(projectedShadowMask);
                cmd.ClearRenderTarget(false,true,Color.clear);
                cmd.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(rect.x,rect.x+rect.z,rect.y,rect.y+rect.w,-1,1),false));
                if(masks!=null && sourceId!=null) foreach(var shadow in masks)
                {
                    if(shadow.SourceId!=sourceId) continue;
                    if(shadow.Mask==null || !float.IsFinite(shadow.Opacity) || shadow.Opacity<0 || shadow.Opacity>1 ||
                        shadow.WorldBounds.width<=0 || shadow.WorldBounds.height<=0)
                        throw new ArgumentException($"Некорректная маска тени источника {sourceId}.");
                    emissionBlock.Clear();
                    emissionBlock.SetTexture("_MainTex",shadow.Mask);
                    emissionBlock.SetVector("_SpriteUvRect",new Vector4(0,0,1,1));
                    emissionBlock.SetFloat("_OccluderOpacity",shadow.Opacity);
                    var bounds=shadow.WorldBounds;
                    var matrix=transform.worldToLocalMatrix*Matrix4x4.TRS(new Vector3(bounds.x,bounds.y,0),Quaternion.identity,new Vector3(bounds.width,bounds.height,1));
                    cmd.DrawMesh(quad,matrix,sceneMaterial,0,4,emissionBlock);
                }
                Graphics.ExecuteCommandBuffer(cmd);
            }
        }
        public int DynamicUpdates {get;private set;}
        private RenderTexture freeFluence;
        private bool freeReady;

        private Color LampRadiance(RoomSpritePlacement placement,RoomLightingTable table,RoomLightScene light,double share)
        {
            var lamp=table.Lamps[placement.Definition.Id];var scale=placement.LocalToWorld.lossyScale;
            double area=SilhouetteArea(placement.Sprite)*Math.Abs(scale.x*scale.y);
            double width=Math.Max(texel,2*Math.Sqrt(area/Math.PI));
            double l=share*table.RadiancePower*lamp.Intensity/width;
            return new Color((float)(l*lamp.Color[0]*light.Tint[0]),(float)(l*lamp.Color[1]*light.Tint[1]),(float)(l*lamp.Color[2]*light.Tint[2]),1);
        }
        public bool TryCreateSteadyEmitter(string id,RoomSpritePlacement placement,bool window,out SteadyLightEmitter emitter)
        {
            emitter=default;
            if(window)
            {
                if(placement.Erase==null || !hasWindowEmission) return false;
                emitter=new SteadyLightEmitter(id,placement.Erase,placement.LocalToWorld,windowEmission);return true;
            }
            if(placement.Definition.Kind!=RoomDefinitionKind.Decor || !sourceTable.Lamps.ContainsKey(placement.Definition.Id)) return false;
            double share=placement.Dark?0:sourceTable.LampFrameShare(placement.Definition,placement.Frame);
            if(share<=0) return false;
            emitter=new SteadyLightEmitter(id,placement.Sprite,placement.LocalToWorld,LampRadiance(placement,sourceTable,sourceLight,share));return true;
        }
        public float SteadyEmitterWidth(SteadyLightEmitter emitter)
        {
            var scale=emitter.LocalToWorld.lossyScale;
            return Mathf.Max(texel,(float)(2*Math.Sqrt(SilhouetteArea(emitter.Sprite)*Math.Abs(scale.x*scale.y)/Math.PI)));
        }

        // occluders: world rectangles that stop a share of light (closed doors: original Box door_opac); 1 = a wall.
        // The CPU part of a field: light scene and wall absorption of a room (pure data, no Unity API), so a light
        // window can prepare it on a worker thread ahead of the change (FactoryLevelView neighbour windows).
        public sealed class PreparedScene
        {
            public readonly RoomFile Room;public readonly RoomLightScene Light;public readonly float[,] Walls;
            internal readonly RoomLightingTable Table;
            private Color[] pixels;   // the wall scene texels, built with the data; handed over once (~10 MB on the factory)
            private PreparedScene(RoomFile room,RoomLightScene light,float[,] walls,RoomLightingTable table)
            {
                Room=room;Light=light;Walls=walls;Table=table;
                int w=walls.GetLength(0),h=walls.GetLength(1);
                pixels=new Color[w*h];
                for(int y=0;y<h;y++) for(int x=0;x<w;x++) if(walls[x,y]>0) pixels[y*w+x]=new Color(0,0,0,walls[x,y]);
            }
            internal Color[] TakePixels()
            {
                var taken=System.Threading.Interlocked.Exchange(ref pixels,null);
                if(taken!=null) return taken;
                // Built again for a second field from the same data (checks, previews).
                int w=Walls.GetLength(0),h=Walls.GetLength(1);taken=new Color[w*h];
                for(int y=0;y<h;y++) for(int x=0;x<w;x++) if(Walls[x,y]>0) taken[y*w+x]=new Color(0,0,0,Walls[x,y]);
                return taken;
            }
            public static PreparedScene Create(RoomFile room,RoomDefinitionCatalog definitions,RoomLandCatalog lands,RoomLightingTable table,RoomGeometry geometry=null)
            {
                var light=RoomLightScene.Build(room,definitions,lands,table,table.RadiancePower,table.RadianceLampSize);
                var walls=RoomLightScene.Absorption(room,geometry??RoomGeometryBuilder.Build(room,definitions),table.RadianceResolution,table.BeamTransmittance,MaxObjectTexelAbsorption,table.BeamHeight);
                return new PreparedScene(room,light,walls,table);
            }
        }
        public static RoomRadianceField Build(RoomFile room,RoomDefinitionCatalog definitions,RoomLandCatalog lands,RoomLightingTable table,Vector2 origin,Transform parent,IReadOnlyList<RoomSpritePlacement> sprites=null,IReadOnlyList<(Rect bounds,double opacity)> occluders=null)
        {
            PreparedScene prepared;
            using(Remains.Diagnostics.FrameHitchLog.Section("Поле света: сцена")) prepared=PreparedScene.Create(room,definitions,lands,table);
            return Build(prepared,lands,origin,parent,sprites,occluders);
        }
        public static RoomRadianceField Build(PreparedScene prepared,RoomLandCatalog lands,Vector2 origin,Transform parent,IReadOnlyList<RoomSpritePlacement> sprites=null,IReadOnlyList<(Rect bounds,double opacity)> occluders=null)
        {
            if(prepared==null) throw new ArgumentNullException(nameof(prepared));
            if(!SystemInfo.supportsComputeShaders) throw new InvalidOperationException("Поле излучения: видеокарта/API без compute-шейдеров.");
            var compute=Resources.Load<ComputeShader>(ComputeResource)??throw new InvalidOperationException("Нет compute-шейдера "+ComputeResource);
            var room=prepared.Room;var light=prepared.Light;var table=prepared.Table;
            int resolution=table.RadianceResolution;
            var buildWalls=Remains.Diagnostics.FrameHitchLog.Section("Поле света: стены");
            foreach(string issue in light.Issues) Debug.LogWarning(issue);
            var walls=prepared.Walls;
            int w=walls.GetLength(0),h=walls.GetLength(1);
            var pixels=prepared.TakePixels();
            float t=1f/resolution;
            if(sprites==null) foreach(var lamp in light.Lamps)
            {
                double r=Math.Max(lamp.DiskRadius,t*0.75);
                for(int y=Mathf.FloorToInt((float)((lamp.Y-r)*resolution));y<=Mathf.CeilToInt((float)((lamp.Y+r)*resolution));y++)
                    for(int x=Mathf.FloorToInt((float)((lamp.X-r)*resolution));x<=Mathf.CeilToInt((float)((lamp.X+r)*resolution));x++)
                    {
                        if(x<0 || y<0 || x>=w || y>=h) continue;
                        double dx=(x+.5)*t-lamp.X,dy=(y+.5)*t-lamp.Y;
                        if(dx*dx+dy*dy>r*r) continue;
                        pixels[y*w+x]=new Color((float)lamp.Radiance[0],(float)lamp.Radiance[1],(float)lamp.Radiance[2],1);
                    }
            }
            buildWalls.Dispose();
            var sceneTexture=new Texture2D(w,h,TextureFormat.RGBAHalf,false,true){name="Сцена света",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            sceneTexture.SetPixels(pixels);
            sceneTexture.Apply(false,false);

            var obj=new GameObject("Поле излучения: "+room.Template);
            if(parent!=null) obj.transform.SetParent(parent,false);
            var field=obj.AddComponent<RoomRadianceField>();
            field.compute=compute;
            field.wallsTexture=sceneTexture;
            field.sourceRoom=room;field.sourceLands=lands;field.sourceTable=table;field.sourceLight=light;
            field.scene=sceneTexture;

            field.width=w;field.height=h;field.texel=t;field.albedo=(float)table.RadianceAlbedo;field.edgeTexels=(float)(table.ShadowEdge/t);field.rimTexels=(float)(table.ObjectRimWidth/t);field.objectGradient=table.ObjectLightMode=="gradient";
            int emitters=0,absorbers=0,glowing=0,dark=0,windows=0;
            if(sprites!=null) using(Remains.Diagnostics.FrameHitchLog.Section("Поле света: спрайты")) field.scene=field.DrawSprites(sceneTexture,sprites,table,light,new Vector4(origin.x,origin.y,w*t,h*t),room,lands,ref emitters,ref absorbers,ref glowing,ref dark,ref windows);
            field.rect=new Vector4(origin.x,origin.y,w*t,h*t);
            // Top cascade: the ray interval reaches the room diagonal, (4^(levels) - 1) / 3 ≥ diagonal.
            float diagonal=Mathf.Sqrt(w*w+h*h);
            field.levels=1;
            while((Mathf.Pow(4,field.levels)-1)/3<diagonal) field.levels++;
            field.baseScene=field.scene;
            using(Remains.Diagnostics.FrameHitchLog.Section("Поле света: буферы"))
            {
                field.UpdateOccluders(occluders);
                using(Remains.Diagnostics.FrameHitchLog.Section("Поле света: рабочие текстуры")) field.Allocate();
                field.EnsureSceneDrawing();
                using(Remains.Diagnostics.FrameHitchLog.Section("Поле света: текстуры вспышек")) field.PrepareTransientTargets();
            }
            Debug.Log($"Поле излучения {room.Template}: сетка {w}×{h}, каскадов {field.levels}, "+(sprites==null?$"ламп-дисков {light.Lamps.Count}.":$"ламп по спрайтам {emitters} (выключенных {dark}), поглощающих объектов {absorbers}, светящихся экранов {glowing}, светящих окон {windows}."));
            return field;
        }

        // Lamp changes redraw emission over the cached walls. The measured silhouette areas and all
        // transport buffers survive; dark lamp frames skip emission before any silhouette measurement.
        public void UpdateSprites(IReadOnlyList<RoomSpritePlacement> sprites,IReadOnlyList<(Rect bounds,double opacity)> occluders)
        {
            if(sprites==null) throw new ArgumentNullException(nameof(sprites));
            int emitters=0,absorbers=0,glowing=0,dark=0,windows=0;
            var next=DrawSprites(wallsTexture,sprites,sourceTable,sourceLight,rect,sourceRoom,sourceLands,
                ref emitters,ref absorbers,ref glowing,ref dark,ref windows);
            var previous=baseScene;
            baseScene=next;
            UpdateOccluders(occluders);
            if(previous!=wallsTexture)
            {
                if(previous is RenderTexture target) target.Release();
                if(Application.isPlaying) Destroy(previous);else DestroyImmediate(previous);
            }
        }

        // Door changes reuse the prepared wall/lamp/object scene and all GPU buffers. No sprite
        // lookup, wall rasterization or synchronous silhouette readback belongs to this path.
        public void UpdateOccluders(IReadOnlyList<(Rect bounds,double opacity)> occluders)
        {
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section("Свет: заслонки");
            steadyShadows.InvalidateFields();hasBackgroundSignature=false;warnedSteadyIds.Clear();
            EnsureSceneDrawing();
            if(occlusionScene==null) occlusionScene=Target(width,height,GraphicsFormat.R16G16B16A16_SFloat,"Сцена света с дверями");
            var previous=RenderTexture.active;
            try {Graphics.Blit(baseScene,occlusionScene);}
            finally {RenderTexture.active=previous;}
            OccluderTexels=0;
            using(var cmd=new CommandBuffer{name="Свет: изменение заслонок"})
            {
                cmd.SetRenderTarget(occlusionScene);
                cmd.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(rect.x,rect.x+rect.z,rect.y,rect.y+rect.w,-1,1),false));
                if(occluders!=null) foreach(var (bounds,opacity) in occluders)
                {
                    if(!(opacity>0 && opacity<=1)) throw new ArgumentOutOfRangeException(nameof(occluders),$"Поле света {name}: непрозрачность заслонки {opacity} вне (0,1].");
                    int left=Mathf.Clamp(Mathf.FloorToInt((bounds.xMin-rect.x)/texel),0,width);
                    int right=Mathf.Clamp(Mathf.CeilToInt((bounds.xMax-rect.x)/texel),0,width);
                    int bottom=Mathf.Clamp(Mathf.FloorToInt((bounds.yMin-rect.y)/texel),0,height);
                    int top=Mathf.Clamp(Mathf.CeilToInt((bounds.yMax-rect.y)/texel),0,height);
                    if(right<=left || top<=bottom) continue;
                    double thickness=Math.Max(1,Math.Min(bounds.width,bounds.height)/texel);
                    float alpha=opacity>=1?1:(float)Math.Min(MaxObjectTexelAbsorption,1-Math.Pow(1-opacity,1/thickness));
                    emissionBlock.Clear();emissionBlock.SetFloat("_Absorption",alpha);
                    cmd.DrawMesh(quad,Matrix4x4.TRS(new Vector3(rect.x+left*texel,rect.y+bottom*texel,0),Quaternion.identity,new Vector3((right-left)*texel,(top-bottom)*texel,1)),sceneMaterial,0,3,emissionBlock);
                    OccluderTexels+=(right-left)*(top-bottom);
                }
                Graphics.ExecuteCommandBuffer(cmd);
            }
            scene=staticScene=occlusionScene;
            transientActive=false;freeReady=false;distanceReady=false;OcclusionUpdates++;
        }

        // Walls + sprites into one scene target at the light grid resolution (max blend: walls stay).
        private RenderTexture DrawSprites(Texture2D walls,IReadOnlyList<RoomSpritePlacement> sprites,RoomLightingTable table,RoomLightScene light,Vector4 worldRect,RoomFile room,RoomLandCatalog lands,ref int emitters,ref int absorbers,ref int glowing,ref int dark,ref int windows)
        {
            EnsureSceneDrawing();
            var target=Target(walls.width,walls.height,GraphicsFormat.R16G16B16A16_SFloat,"Сцена света");
            var previousTarget=RenderTexture.active;
            try {Graphics.Blit(walls,target);}
            finally {RenderTexture.active=previousTarget;}
            var cmd=new CommandBuffer{name="Сцена света: спрайты"};
            cmd.SetRenderTarget(target);
            cmd.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(worldRect.x,worldRect.x+worldRect.z,worldRect.y,worldRect.y+worldRect.w,-1,1),false));
            var block=new MaterialPropertyBlock();
            // Windows: the erase clip emits the far background's radiance (table "windows" by the land's fon);
            // a bigger opening sends more light. An unknown background is reported, its windows stay dark.
            var issues=new List<string>();
            string fon=lands.Option(room,"fon",issues);
            if(string.IsNullOrEmpty(fon)) fon="fonDefault";
            foreach(string issue in issues) Debug.LogWarning(issue);
            bool hasWindowLight=table.WindowRadiance.TryGetValue(fon,out var sky);
            hasWindowEmission=hasWindowLight;
            windowEmission=hasWindowLight?new Color((float)(sky[0]*light.Tint[0]),(float)(sky[1]*light.Tint[1]),(float)(sky[2]*light.Tint[2]),1):Color.clear;
            int windowCount=0;
            foreach(var placement in sprites)
            {
                if(placement.Erase==null) continue;
                if(!hasWindowLight) {Debug.LogWarning($"Поле излучения {room.Template}: нет света окон для дальнего фона «{fon}» в таблице света — окна не светят.");break;}
                var eraseBounds=placement.Erase.bounds;
                block.Clear();
                block.SetTexture("_MainTex",placement.Erase.texture);
                block.SetColor("_Emission",windowEmission);
                block.SetFloat("_Absorption",0);block.SetFloat("_Glow",0);block.SetFloat("_GlowThreshold",1);
                cmd.DrawMesh(quad,placement.LocalToWorld*Matrix4x4.TRS(eraseBounds.min,Quaternion.identity,eraseBounds.size),sceneMaterial,0,0,block);
                windowCount++;
            }
            windows=windowCount;
            foreach(var placement in sprites)
            {
                var d=placement.Definition;
                var bounds=placement.Sprite.bounds;
                block.Clear();
                block.SetTexture("_MainTex",placement.Sprite.texture);
                bool glows=table.Glow.TryGetValue(d.Id,out var glow);
                block.SetFloat("_Glow",glows?(float)glow.Gain:0);
                block.SetFloat("_GlowThreshold",glows?(float)glow.Threshold:1);
                if(glows) glowing++;
                if(d.Kind==RoomDefinitionKind.Decor && table.Lamps.TryGetValue(d.Id,out var lamp))
                {
                    double share=placement.Dark?0:table.LampFrameShare(d,placement.Frame);
                    if(share<=0) {dark++;continue;}   // the "off" frame or a dark lamp
                    // Flatland: fluence 1 m away ≈ radiance × apparent width; width = diameter of the disk
                    // with the silhouette's area, so every lamp sends the table's power whatever its shape.
                    block.SetColor("_Emission",LampRadiance(placement,table,light,share));
                    block.SetFloat("_Absorption",0);
                    emitters++;
                }
                else
                {
                    // Decor hangs on the back wall (no absorption); objects by the table rule.
                    double absorption=0;
                    if(d.Kind==RoomDefinitionKind.Object)
                    {
                        var objectScale=placement.LocalToWorld.lossyScale;
                        double silhouette=SilhouetteArea(placement.Sprite)*Math.Abs(objectScale.x*objectScale.y);
                        absorption=1-table.ObjectTransmittance(d,silhouette);
                    }
                    if(absorption<=0 && !glows) continue;
                    // The table gives what the whole object stops; per texel so that crossing its
                    // thickness (smaller side) stops that much: 1 - (1 - A)^(1 / thickness).
                    var scale=placement.LocalToWorld.lossyScale;
                    double thickness=Math.Max(1,Math.Min(Math.Abs(bounds.size.x*scale.x),Math.Abs(bounds.size.y*scale.y))/texel);
                    // Capped below the wall threshold (0.99): an object stays an object (lit through its
                    // body, no wall bounce) even when it stops all light across its thickness.
                    double perTexel=absorption<=0?0:Math.Min(MaxObjectTexelAbsorption,1-Math.Pow(1-Math.Min(absorption,0.9999),1/thickness));
                    block.SetColor("_Emission",Color.clear);
                    block.SetFloat("_Absorption",(float)perTexel);
                    if(absorption>0) absorbers++;
                }
                cmd.DrawMesh(quad,placement.LocalToWorld*Matrix4x4.TRS(bounds.min,Quaternion.identity,bounds.size),sceneMaterial,0,0,block);
            }
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
            return target;
        }

        private void EnsureSceneDrawing()
        {
            if(sceneMaterial!=null) return;
            var shader=Resources.Load<Shader>(SceneShaderResource)??throw new InvalidOperationException("Нет шейдера "+SceneShaderResource);
            sceneMaterial=new Material(shader){name="Сцена света"};
            emissionBlock=new MaterialPropertyBlock();
            quad=new Mesh{name="Квадрат спрайта"};
            quad.SetVertices(new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,1,0),new Vector3(1,1,0)});
            quad.SetUVs(0,new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1)});
            quad.SetColors(new[]{Color.white,Color.white,Color.white,Color.white});
            quad.SetTriangles(new[]{0,2,1,1,2,3},0);
        }

        // Silhouette measurements depend only on the (immutable) sprite and the grid texel, not on the window: shared
        // by every field of the session. Per field they cost a GPU render + synchronous ReadPixels per lamp sprite on
        // every light window change (~60 ms of the ~90 ms factory window rebuild).
        private static readonly Dictionary<(Sprite sprite,float texel),(double area,Vector3[] footprint)> measured=new();

        // Uses the same alpha-threshold raster as the lamp's emitted light, prepared at rebuild time.
        // Eight weighted columns retain the extent of a long lamp without tracing every lit pixel.
        public Vector3[] PrepareLampShadowSamples(Sprite sprite,Matrix4x4 localToWorld)
        {
            SilhouetteArea(sprite);
            var local=measured[(sprite,texel)].footprint;var output=new Vector3[local.Length];
            for(int i=0;i<local.Length;i++)
            {
                var world=localToWorld.MultiplyPoint3x4(new Vector3(local[i].x,local[i].y,0));
                output[i]=new Vector3(world.x,world.y,local[i].z);
            }
            return output;
        }

        // Area (m²) a sprite's silhouette (alpha > 0.4) covers in the light grid (lamp emission, object size): the sprite drawn alone with
        // the scene shader into a grid-sized target, lit texels counted (once per sprite, at build).
        private double SilhouetteArea(Sprite sprite)
        {
            if(measured.TryGetValue((sprite,texel),out var cached)) return cached.area;
            var bounds=sprite.bounds;
            int w=Mathf.CeilToInt(bounds.size.x/texel)+2,h=Mathf.CeilToInt(bounds.size.y/texel)+2;
            var target=RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
            var cmd=new CommandBuffer{name="Площадь лампы"};
            cmd.SetRenderTarget(target);
            cmd.ClearRenderTarget(false,true,Color.clear);
            cmd.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(bounds.min.x-texel,bounds.min.x-texel+w*texel,bounds.min.y-texel,bounds.min.y-texel+h*texel,-1,1),false));
            var block=new MaterialPropertyBlock();
            block.SetTexture("_MainTex",sprite.texture);
            block.SetColor("_Emission",Color.white);
            cmd.DrawMesh(quad,Matrix4x4.TRS(bounds.min,Quaternion.identity,bounds.size),sceneMaterial,0,0,block);
            Graphics.ExecuteCommandBuffer(cmd);
            cmd.Release();
            var previous=RenderTexture.active;RenderTexture.active=target;
            var copy=new Texture2D(w,h,TextureFormat.RGBAHalf,false,true);
            copy.ReadPixels(new Rect(0,0,w,h),0,0);
            SilhouetteReadbacks++;
            RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
            int count=0;var pixels=copy.GetPixels();
            var sums=new Vector2[8];var counts=new int[8];
            for(int y=0;y<h;y++) for(int x=0;x<w;x++) if(pixels[y*w+x].r>.5f)
            {
                count++;int column=Mathf.Min(7,x*8/w);counts[column]++;
                sums[column]+=new Vector2(bounds.min.x-texel+(x+.5f)*texel,bounds.min.y-texel+(y+.5f)*texel);
            }
            var footprint=new List<Vector3>(8);
            for(int i=0;i<8;i++) if(counts[i]>0)
                footprint.Add(new Vector3(sums[i].x/counts[i],sums[i].y/counts[i],(float)counts[i]/count));
            UnityEngine.Object.DestroyImmediate(copy);
            double area=Math.Max(1,count)*texel*texel;
            measured[(sprite,texel)]=(area,footprint.ToArray());
            return area;
        }

        private void Allocate()
        {
            // Float32 linear texel indices remain exact for this bounded light window.
            if((long)width*height>16777216) throw new InvalidOperationException($"Поле света {name}: {width}×{height} превышает точность индексов проб заполнения.");
            fillSamples=Target(width,height,GraphicsFormat.R32G32B32A32_SFloat,"Проба заполнения по RGB");
            fillWeights=Target(width,height,GraphicsFormat.R32G32B32A32_SFloat,"Вес проб заполнения по RGB");
            seedsA=Target(width,height,GraphicsFormat.R32G32_SFloat,"Ближайшая стена A");
            seedsB=Target(width,height,GraphicsFormat.R32G32_SFloat,"Ближайшая стена B");
            distance=Target(width,height,GraphicsFormat.R32_SFloat,"Расстояние до стены");
            int cw=2*width+(2<<levels),ch=2*height+(2<<levels);
            cascadeA=Target(cw,ch,GraphicsFormat.R16G16B16A16_SFloat,"Каскад A");
            cascadeB=Target(cw,ch,GraphicsFormat.R16G16B16A16_SFloat,"Каскад B");
            fluenceRaw=Target(width,height,GraphicsFormat.R16G16B16A16_SFloat,"Освещённость по пробам");
            fluence=Target(width,height,GraphicsFormat.R16G16B16A16_SFloat,"Освещённость");
            fluence.filterMode=FilterMode.Bilinear;
            bounceA=Target(width,height,GraphicsFormat.R16G16B16A16_SFloat,"Отражение стен A");
            bounceB=Target(width,height,GraphicsFormat.R16G16B16A16_SFloat,"Отражение стен B");
        }

        // Window rebuilds replace a field with one of the same size: its working targets are taken over from the
        // previous field (cleared) instead of freed and allocated again (~20 targets per light window change).
        private static readonly Dictionary<(int w,int h,GraphicsFormat format,string name),RenderTexture> spare=new();
        private static RenderTexture Target(int w,int h,GraphicsFormat format,string name)
        {
            if(spare.Remove((w,h,format,name),out var reused) && reused!=null && reused.IsCreated())
            {
                reused.filterMode=FilterMode.Point;reused.wrapMode=TextureWrapMode.Clamp;
                var previous=RenderTexture.active;
                try {RenderTexture.active=reused;GL.Clear(false,true,Color.clear);}
                finally {RenderTexture.active=previous;}
                return reused;
            }
            var texture=new RenderTexture(w,h,0,format){name=name,enableRandomWrite=true,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            texture.Create();
            return texture;
        }
        // Keeps one spare target per size/format/purpose; any other is freed.
        private static void Recycle(RenderTexture texture)
        {
            if(texture==null) return;
            var key=(texture.width,texture.height,texture.graphicsFormat,texture.name);
            if(texture.IsCreated() && !spare.ContainsKey(key)) {spare.Add(key,texture);return;}
            texture.Release();
            if(Application.isPlaying) Destroy(texture);else DestroyImmediate(texture);
        }

        public void Render()
        {
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section("Свет: проход поля");
            staticScene=scene;steadyShadows.InvalidateFields();hasBackgroundSignature=false;warnedSteadyIds.Clear();
            Shader.SetGlobalFloat("_RemainsBackgroundRadianceEnabled",0);
            transientActive=false;freeReady=false;
            StaticRenderUpdates++;
            RenderField(false);
            if(staticFluence==null) staticFluence=Target(width,height,GraphicsFormat.R16G16B16A16_SFloat,"Сохранённый свет ламп");
            Graphics.CopyTexture(fluence,staticFluence);
        }

        private void RenderField(bool directOnly)
        {
            int seed=compute.FindKernel("Seed"),jump=compute.FindKernel("Jump"),dist=compute.FindKernel("Distance"),cascade=compute.FindKernel("Cascade"),flu=compute.FindKernel("Fluence"),fill=compute.FindKernel("Fill"),bounce=compute.FindKernel("Bounce");
            compute.SetInts("_Size",width,height);
            int gx=(width+7)/8,gy=(height+7)/8;

            if(!distanceReady)
            {
            compute.SetTexture(seed,"_Scene",scene);
            compute.SetTexture(seed,"_SeedsOut",seedsA);
            compute.Dispatch(seed,gx,gy,1);
            var source=seedsA;var target=seedsB;
            for(int step=Mathf.NextPowerOfTwo(Mathf.Max(width,height))/2;step>=1;step/=2)
            {
                compute.SetInt("_Step",step);
                compute.SetTexture(jump,"_SeedsIn",source);
                compute.SetTexture(jump,"_SeedsOut",target);
                compute.Dispatch(jump,gx,gy,1);
                (source,target)=(target,source);
            }
            compute.SetTexture(dist,"_SeedsFinal",source);
            compute.SetTexture(dist,"_DistanceOut",distance);
            compute.Dispatch(dist,gx,gy,1);
            distanceReady=true;
            }

            compute.SetInt("_Top",levels-1);
            compute.SetFloat("_BounceGain",directOnly?0:1);
            RenderTexture upper=cascadeB,current=cascadeA;
            for(int c=levels-1;c>=0;c--)
            {
                compute.SetInt("_Level",c);
                compute.SetTexture(cascade,"_Scene",scene);
                compute.SetTexture(cascade,"_Distance",distance);
                compute.SetTexture(cascade,"_Upper",upper);
                compute.SetTexture(cascade,"_Bounce",bounceA);
                compute.SetTexture(cascade,"_Cascade",current);
                int spacing=1<<c,block=2<<c;
                int cw=(width+spacing-1)/spacing*block,ch=(height+spacing-1)/spacing*block;
                compute.Dispatch(cascade,(cw+7)/8,(ch+7)/8,1);
                (upper,current)=(current,upper);
            }
            compute.SetTexture(flu,"_Cascade0",upper);
            compute.SetTexture(flu,"_Fluence",fluenceRaw);
            compute.Dispatch(flu,gx,gy,1);
            compute.SetTexture(fill,"_Scene",scene);
            compute.SetInt("_RecordFill",directOnly?0:1);
            compute.SetTexture(fill,"_FillSamples",fillSamples);compute.SetTexture(fill,"_FillWeights",fillWeights);
            compute.SetFloat("_EdgeTexels",Mathf.Max(1,edgeTexels));
            compute.SetFloat("_RimTexels",Mathf.Max(1,rimTexels));
            compute.SetInt("_ObjectGradient",objectGradient?1:0);
            compute.SetTexture(fill,"_FluenceRaw",fluenceRaw);
            compute.SetTexture(fill,"_Fluence",directOnly?directFluence:fluence);
            compute.Dispatch(fill,gx,gy,1);
            if(!directOnly)
            {
            compute.SetFloat("_Albedo",albedo);
            compute.SetTexture(bounce,"_Scene",scene);
            compute.SetTexture(bounce,"_Cascade0",upper);
            compute.SetTexture(bounce,"_BounceOut",bounceB);
            compute.Dispatch(bounce,gx,gy,1);
            (bounceA,bounceB)=(bounceB,bounceA);
            }

            Shader.SetGlobalTexture("_RemainsRadiance",fluence);
            Shader.SetGlobalVector("_RemainsRadianceRect",rect);
            Shader.SetGlobalFloat("_RemainsRadianceTexel",texel);
            Shader.SetGlobalFloat("_RemainsRadianceEnabled",1);
        }

        private void OnDestroy()
        {
            steadyShadows.Dispose();
            Shader.SetGlobalFloat("_RemainsBackgroundRadianceEnabled",0);
            transientCommands?.Release();
            Shader.SetGlobalFloat("_RemainsRadianceEnabled",0);
            foreach(var texture in new[]{seedsA,seedsB,distance,cascadeA,cascadeB,fluenceRaw,fluence,bounceA,bounceB,fillSamples,fillWeights,transientScene,staticFluence,directFluence,freeFluence,projectedShadowMask,backgroundFluence,backgroundAccumulation,occlusionScene})
                Recycle(texture);
            if(baseScene is RenderTexture baseTarget) Recycle(baseTarget);
            foreach(var obj in new UnityEngine.Object[]{baseScene is RenderTexture?null:baseScene==wallsTexture?null:baseScene,wallsTexture,sceneMaterial,quad})
                if(obj!=null) {if(Application.isPlaying) Destroy(obj);else DestroyImmediate(obj);}
        }
    }
}
