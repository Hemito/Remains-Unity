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
        private float albedo,edgeTexels,rimTexels;
        private bool objectGradient;
        public int OccluderTexels {get;private set;}   // diagnostics: scene texels set by occluders
        private int width,height,levels;
        private Vector4 rect;
        private float texel;
        private Texture staticScene;
        private RenderTexture transientScene;
        private bool distanceReady;
        private int transientSettle;
        private MaterialPropertyBlock emissionBlock;

        public readonly struct SpriteEmission
        {
            public readonly Sprite Sprite;
            public readonly Matrix4x4 LocalToWorld;
            public readonly float Gain;
            public SpriteEmission(Sprite sprite,Matrix4x4 localToWorld,float gain)
            {Sprite=sprite;LocalToWorld=localToWorld;Gain=gain;}
        }

        // Caller supplies already cached sprites and actual weapon poses. Empty input fades wall bounce.
        // Returns false when the field is settled; no geometry or content lookup happens per frame.
        public bool RenderTransient(IReadOnlyList<SpriteEmission> emissions)
        {
            bool active=emissions!=null && emissions.Count>0;
            if(!active && transientSettle==0) return false;
            if(staticScene==null) staticScene=scene;
            if(active)
            {
                EnsureSceneDrawing();
                if(transientScene==null) transientScene=Target(width,height,GraphicsFormat.R16G16B16A16_SFloat,"Сцена кратковременного света");
                var previous=RenderTexture.active;
                try {Graphics.Blit(staticScene,transientScene);}
                finally {RenderTexture.active=previous;}
                using(var cmd=new CommandBuffer{name="Кратковременное излучение"})
                {
                    cmd.SetRenderTarget(transientScene);
                    cmd.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(rect.x,rect.x+rect.z,rect.y,rect.y+rect.w,-1,1),false));
                    foreach(var emission in emissions)
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
                        cmd.DrawMesh(quad,emission.LocalToWorld*Matrix4x4.TRS(bounds.min,Quaternion.identity,bounds.size),sceneMaterial,0,1,emissionBlock);
                    }
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                transientSettle=8;
            }
            else transientSettle--;
            scene=active?transientScene:staticScene;
            // Jump-flood seeds include emitting RGB as well as absorption. A newly appearing
            // source must be included, otherwise sphere tracing can step straight past it.
            if(active) distanceReady=false;
            try {Render();}
            finally {scene=staticScene;if(active) distanceReady=false;}
            return true;
        }

        // occluders: world rectangles that stop a share of light (closed doors: original Box door_opac); 1 = a wall.
        public static RoomRadianceField Build(RoomFile room,RoomDefinitionCatalog definitions,RoomLandCatalog lands,RoomLightingTable table,Vector2 origin,Transform parent,IReadOnlyList<RoomSpritePlacement> sprites=null,IReadOnlyList<(Rect bounds,double opacity)> occluders=null)
        {
            if(!SystemInfo.supportsComputeShaders) throw new InvalidOperationException("Поле излучения: видеокарта/API без compute-шейдеров.");
            var compute=Resources.Load<ComputeShader>(ComputeResource)??throw new InvalidOperationException("Нет compute-шейдера "+ComputeResource);
            int resolution=table.RadianceResolution;
            var light=RoomLightScene.Build(room,definitions,lands,table,table.RadiancePower,table.RadianceLampSize);
            foreach(string issue in light.Issues) Debug.LogWarning(issue);
            var walls=RoomLightScene.Walls(room,RoomGeometryBuilder.Build(room,definitions),resolution);
            int w=walls.GetLength(0),h=walls.GetLength(1);

            var pixels=new Color[w*h];
            for(int y=0;y<h;y++)
                for(int x=0;x<w;x++)
                    if(walls[x,y]) pixels[y*w+x]=new Color(0,0,0,1);
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
            int occluderTexels=0;
            if(occluders!=null)
                foreach(var (bounds,opacity) in occluders)
                {
                    if(!(opacity>0 && opacity<=1)) throw new ArgumentOutOfRangeException(nameof(occluders),$"Поле излучения {room.Template}: непрозрачность заслонки {opacity} вне (0,1].");
                    // Light crosses the occluder along its thinner side; per texel so the whole thickness stops `opacity`.
                    double thickness=Math.Max(1,Math.Min(bounds.width,bounds.height)/t);
                    float alpha=opacity>=1?1:(float)Math.Min(MaxObjectTexelAbsorption,1-Math.Pow(1-opacity,1/thickness));
                    for(int y=Mathf.FloorToInt((bounds.yMin-origin.y)/t);y<Mathf.CeilToInt((bounds.yMax-origin.y)/t);y++)
                        for(int x=Mathf.FloorToInt((bounds.xMin-origin.x)/t);x<Mathf.CeilToInt((bounds.xMax-origin.x)/t);x++)
                        {
                            if(x<0 || y<0 || x>=w || y>=h) continue;
                            var p=pixels[y*w+x];
                            if(alpha>p.a) {pixels[y*w+x]=new Color(0,0,0,alpha);occluderTexels++;}
                        }
                }
            var sceneTexture=new Texture2D(w,h,TextureFormat.RGBAHalf,false,true){name="Сцена света",filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            sceneTexture.SetPixels(pixels);
            sceneTexture.Apply(false,false);

            var obj=new GameObject("Поле излучения: "+room.Template);
            if(parent!=null) obj.transform.SetParent(parent,false);
            var field=obj.AddComponent<RoomRadianceField>();
            field.compute=compute;
            field.wallsTexture=sceneTexture;
            field.scene=sceneTexture;
            field.OccluderTexels=occluderTexels;
            field.width=w;field.height=h;field.texel=t;field.albedo=(float)table.RadianceAlbedo;field.edgeTexels=(float)(table.ShadowEdge/t);field.rimTexels=(float)(table.ObjectRimWidth/t);field.objectGradient=table.ObjectLightMode=="gradient";
            int emitters=0,absorbers=0,glowing=0,dark=0;
            if(sprites!=null) field.scene=field.DrawSprites(sceneTexture,sprites,table,light,new Vector4(origin.x,origin.y,w*t,h*t),ref emitters,ref absorbers,ref glowing,ref dark);
            field.rect=new Vector4(origin.x,origin.y,w*t,h*t);
            // Top cascade: the ray interval reaches the room diagonal, (4^(levels) - 1) / 3 ≥ diagonal.
            float diagonal=Mathf.Sqrt(w*w+h*h);
            field.levels=1;
            while((Mathf.Pow(4,field.levels)-1)/3<diagonal) field.levels++;
            field.Allocate();
            field.EnsureSceneDrawing();
            Debug.Log($"Поле излучения {room.Template}: сетка {w}×{h}, каскадов {field.levels}, "+(sprites==null?$"ламп-дисков {light.Lamps.Count}.":$"ламп по спрайтам {emitters} (выключенных {dark}), поглощающих объектов {absorbers}, светящихся экранов {glowing}."));
            return field;
        }

        // Walls + sprites into one scene target at the light grid resolution (max blend: walls stay).
        private RenderTexture DrawSprites(Texture2D walls,IReadOnlyList<RoomSpritePlacement> sprites,RoomLightingTable table,RoomLightScene light,Vector4 worldRect,ref int emitters,ref int absorbers,ref int glowing,ref int dark)
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
                    double share=table.LampFrameShare(d,placement.Frame);
                    if(share<=0) {dark++;continue;}   // the "off" frame
                    // Flatland: fluence 1 m away ≈ radiance × apparent width; width = diameter of the disk
                    // with the silhouette's area, so every lamp sends the table's power whatever its shape.
                    var scale=placement.LocalToWorld.lossyScale;
                    double area=SilhouetteArea(placement.Sprite)*Math.Abs(scale.x*scale.y);
                    double width=Math.Max(texel,2*Math.Sqrt(area/Math.PI));
                    double l=share*table.RadiancePower*lamp.Intensity/width;
                    block.SetColor("_Emission",new Color((float)(l*lamp.Color[0]*light.Tint[0]),(float)(l*lamp.Color[1]*light.Tint[1]),(float)(l*lamp.Color[2]*light.Tint[2]),1));
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
            quad.SetTriangles(new[]{0,2,1,1,2,3},0);
        }

        private readonly Dictionary<Sprite,double> silhouettes=new();

        // Area (m²) a sprite's silhouette (alpha > 0.4) covers in the light grid (lamp emission, object size): the sprite drawn alone with
        // the scene shader into a grid-sized target, lit texels counted (once per sprite, at build).
        private double SilhouetteArea(Sprite sprite)
        {
            if(silhouettes.TryGetValue(sprite,out double cached)) return cached;
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
            RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
            int count=0;
            foreach(var pixel in copy.GetPixels()) if(pixel.r>0.5f) count++;
            UnityEngine.Object.DestroyImmediate(copy);
            double area=Math.Max(1,count)*texel*texel;
            silhouettes[sprite]=area;
            return area;
        }

        private void Allocate()
        {
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

        private static RenderTexture Target(int w,int h,GraphicsFormat format,string name)
        {
            var texture=new RenderTexture(w,h,0,format){name=name,enableRandomWrite=true,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            texture.Create();
            return texture;
        }

        public void Render()
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
            compute.SetFloat("_EdgeTexels",Mathf.Max(1,edgeTexels));
            compute.SetFloat("_RimTexels",Mathf.Max(1,rimTexels));
            compute.SetInt("_ObjectGradient",objectGradient?1:0);
            compute.SetTexture(fill,"_FluenceRaw",fluenceRaw);
            compute.SetTexture(fill,"_Fluence",fluence);
            compute.Dispatch(fill,gx,gy,1);
            compute.SetFloat("_Albedo",albedo);
            compute.SetTexture(bounce,"_Scene",scene);
            compute.SetTexture(bounce,"_Cascade0",upper);
            compute.SetTexture(bounce,"_BounceOut",bounceB);
            compute.Dispatch(bounce,gx,gy,1);
            (bounceA,bounceB)=(bounceB,bounceA);

            Shader.SetGlobalTexture("_RemainsRadiance",fluence);
            Shader.SetGlobalVector("_RemainsRadianceRect",rect);
            Shader.SetGlobalFloat("_RemainsRadianceTexel",texel);
            Shader.SetGlobalFloat("_RemainsRadianceEnabled",1);
        }

        private void OnDestroy()
        {
            Shader.SetGlobalFloat("_RemainsRadianceEnabled",0);
            foreach(var texture in new[]{seedsA,seedsB,distance,cascadeA,cascadeB,fluenceRaw,fluence,bounceA,bounceB,transientScene}) if(texture!=null) texture.Release();
            if(scene is RenderTexture sceneTarget) sceneTarget.Release();
            foreach(var obj in new UnityEngine.Object[]{seedsA,seedsB,distance,cascadeA,cascadeB,fluenceRaw,fluence,bounceA,bounceB,transientScene,scene,wallsTexture,sceneMaterial,quad})
                if(obj!=null) {if(Application.isPlaying) Destroy(obj);else DestroyImmediate(obj);}
        }
    }
}
