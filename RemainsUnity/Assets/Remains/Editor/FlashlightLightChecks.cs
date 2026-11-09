using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    public static class FlashlightLightChecks
    {
        private static string Output=>Path.GetFullPath("../.reference/light-tools-20261109");
        [Serializable] private sealed class Report
        {
            public bool success;
            public float forward,edge,far,foreignLightError;
            public int buildsBeforeRotation,buildsAfterRotation;
            public long cachedBytes,residualBytes;
        }
        public static void RunBatch()
        {try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
        public static void Run()
        {
            Directory.CreateDirectory(Output);var report=new Report();
            Anatomy.Load();CheckSelection();CheckCone();long before=LightTextureBudget.Bytes;CheckGpu(report);
            Require(LightTextureBudget.Bytes==before,"Фонарик оставил текстуры после Dispose.");
            report.residualBytes=LightTextureBudget.Bytes-before;report.success=true;
            File.WriteAllText(Path.Combine(Output,"flashlight-checks.json"),JsonUtility.ToJson(report,true));
            Debug.Log("Фонарик: PASS — выбранный предмет, поза, направление, плавный конус/дальность, поворот без transport, свои тени, преграды, отсутствие отражений и очистка.");
        }
        private static ActorState Actor()
        {
            var actor=new ActorState{id="flashlight-check",level="hub",owner="check",player=true,connected=true,health=100,
                weaponInventoryVersion=1,heldItemId="flashlight-stack",heldItemPoseValid=true,heldItemPoseX=.4f,heldItemPoseY=.2f};
            actor.inventory.Add(new ItemStack{id="flashlight-stack",definition=FlashlightLightSources.ItemDefinition,count=1});return actor;
        }
        private static void CheckSelection()
        {
            var actor=Actor();var sources=new FlashlightLightSources();var output=new List<WorldLightEmission>();
            void Append(){output.Clear();sources.Begin();sources.Append(actor,output);sources.End();}
            Append();Require(output.Count==1 && output[0].Position==HeldItemPose.Mount(actor) &&
                output[0].Cone.Direction==Vector2.right,"Фонарик не совпал с подтверждённой позой предмета.");
            int geometry=output[0].GeometrySignature(),composition=output[0].CompositionSignature();
            actor.weaponAngle=180;actor.weaponFlipY=true;Append();Require(output.Count==1 && output[0].Cone.Direction.x<-.999f &&
                output[0].GeometrySignature()==geometry && output[0].CompositionSignature()!=composition,
                "Поворот фонарика не изменил конус либо перестроил transport.");
            actor.heldItemId=null;Append();Require(output.Count==0,"Убранный из копыт фонарик продолжает светить.");
            actor.heldItemId="flashlight-stack";actor.down=true;Append();Require(output.Count==0,"Нокдаун не выключил фонарик.");
            actor.down=false;actor.connected=false;Append();Require(output.Count==0,"Скрытый персонаж хаба оставил фонарик.");
            actor.connected=true;actor.heldItemPoseValid=false;Append();Require(output.Count==0,"Неподтверждённая поза дала выдуманный фонарик.");
            sources.Clear();
        }
        private static void CheckCone()
        {
            var cone=new LightEmissionCone(Vector2.right,15,30);
            float middle=cone.Weight(new Vector2(Mathf.Cos(22.5f*Mathf.Deg2Rad),Mathf.Sin(22.5f*Mathf.Deg2Rad)));
            Require(cone.Weight(Vector2.right)==1 && cone.Weight(Vector2.left)==0 && cone.Weight(Vector2.up)==0 &&
                middle>0 && middle<1 && default(LightEmissionCone).Weight(Vector2.left)==1,"Неверный плавный профиль конуса или сломан круговой свет.");
        }
        private static void CheckGpu(Report report)
        {
            var origin=new Vector2(1,1);var scene=new LevelLightScene(new float[180,120],new Vector2(-4,-3),10,Array.Empty<LevelLightScene.ObjectSprite>());
            using var light=new LevelLight(scene,new LevelLightSources(Array.Empty<LevelLightSource>(),180,120),.2f);light.BuildAll();
            var context=new LightShaderContext();context.Activate();using var view=new LevelLightView(light,context);
            var white=new Texture2D(1,1,TextureFormat.RGBA32,false,true);white.SetPixel(0,0,Color.white);white.Apply();
            try
            {
                var points=new[]{new Vector4(origin.x,origin.y,1,0)};var bounds=new Rect(origin-Vector2.one*.02f,Vector2.one*.04f);
                WorldLightEmission Beam(Vector2 axis)=>new("flashlight:gpu",points,bounds,FlashlightLightSources.Radiance,
                    FlashlightLightSources.EmitterWidth,FlashlightLightSources.Reach,cone:new LightEmissionCone(axis,15,30));
                var blue=new WorldLightEmission("check:other",points,bounds,new Color(.1f,.2f,4),.18f,7.5f);
                void Draw(WorldLightEmission[] sources,ProjectedLightShadow[] masks=null)=>view.Present(scene.WorldRect,null,masks,null,sources);
                float Red(Vector2 point)=>At(Read("_RemainsRadiance"),view.WorldArea,Shader.GetGlobalTexture("_RemainsRadiance").width,point).r;
                var beam=Beam(Vector2.right);long fill=light.Fill.Revision;Draw(new[]{beam});
                float front=Red(origin+Vector2.right*2),far=Red(origin+Vector2.right*5);
                Require(front>.05f && far>0 && far<front && Red(origin+Vector2.up*2)<.001f && Red(origin+Vector2.left*2)<.001f &&
                    Red(origin+Vector2.right*7.6f)<.001f,"Фонарик светит вне конуса/дальности или не затухает.");
                var shoulder=origin+new Vector2(Mathf.Cos(22.5f*Mathf.Deg2Rad),Mathf.Sin(22.5f*Mathf.Deg2Rad))*2;
                float edge=Red(shoulder);Require(edge>0 && edge<front,"У конуса нет плавного бокового края.");
                Save("flashlight-right-front.png","_RemainsRadiance");Save("flashlight-right-back.png","_RemainsBackgroundRadiance");
                int builds=view.WorldLightBuilds;Draw(new[]{Beam(Vector2.up)});
                Require(view.WorldLightBuilds==builds && Red(origin+Vector2.up*2)>.05f && Red(origin+Vector2.right*2)<.001f,
                    "Поворот не обновил картинку либо перестроил радиальное поле.");
                report.buildsBeforeRotation=builds;report.buildsAfterRotation=view.WorldLightBuilds;
                Save("flashlight-up-front.png","_RemainsRadiance");
                Require(light.Fill.Revision==fill,"Фонарик изменил заполнение/отражения.");
                Draw(new[]{blue});var blueOnly=Read("_RemainsBackgroundRadiance");
                Draw(new[]{beam,blue},new[]{new ProjectedLightShadow(beam.Id,white,view.WorldArea,1)});
                var masked=Read("_RemainsBackgroundRadiance");float error=0;
                for(int i=0;i<masked.Length;i++)error=Mathf.Max(error,Mathf.Abs(masked[i].r-blueOnly[i].r),Mathf.Abs(masked[i].g-blueOnly[i].g),Mathf.Abs(masked[i].b-blueOnly[i].b));
                Require(error<.002f,"Тень фонарика убрала свет другого источника: "+error);
                Save("flashlight-own-shadow-back.png","_RemainsBackgroundRadiance");
                light.SetDoors(new[]{new LevelLightScene.Door("flashlight-wall",new Rect(3,-3,.3f,12),1,true)});
                for(int i=0;light.HasWork && i<1000;i++)light.Step(2,2);
                Require(!light.HasWork,"Очередь препятствия фонарика не завершилась.");Draw(new[]{beam});
                Require(Red(origin+Vector2.right*3)<.001f && Red(origin+Vector2.right)>.05f,"Стена не блокирует луч фонарика.");
                Save("flashlight-blocked-front.png","_RemainsRadiance");
                report.forward=front;report.edge=edge;report.far=far;report.foreignLightError=error;report.cachedBytes=view.WorldLightBytes;
                Draw(Array.Empty<WorldLightEmission>());Require(view.WorldLightBytes==0 && Read("_RemainsRadiance").All(c=>c.maxColorComponent<.001f),"Выключенный фонарик оставил поле.");
                Debug.Log($"Фонарик GPU: ось={front:F4}, край={edge:F4}, дальний={far:F4}, ошибка чужого света={error:F6}; transport при повороте сохранён.");
            }
            finally {context.Release();Object.DestroyImmediate(white);}
        }
        private static Color[] Read(string property)
        {
            var readback=AsyncGPUReadback.Request(Shader.GetGlobalTexture(property),0,TextureFormat.RGBAFloat);readback.WaitForCompletion();
            Require(!readback.hasError,"Ошибка чтения света фонарика с GPU.");return readback.GetData<Color>().ToArray();
        }
        private static void Save(string file,string property)
        {
            var source=Shader.GetGlobalTexture(property);var pixels=Read(property);
            var image=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false);
            try
            {
                for(int i=0;i<pixels.Length;i++) {pixels[i]=pixels[i].gamma;pixels[i].a=1;}
                image.SetPixels(pixels);image.Apply();File.WriteAllBytes(Path.Combine(Output,file),image.EncodeToPNG());
            }
            finally {Object.DestroyImmediate(image);}
        }
        private static Color At(Color[] data,Rect area,int width,Vector2 point)
        {
            int height=data.Length/width,x=Mathf.Clamp((int)((point.x-area.x)/area.width*width),0,width-1),y=Mathf.Clamp((int)((point.y-area.y)/area.height*height),0,height-1);
            return data[y*width+x];
        }
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException("Фонарик: "+message);}
    }
}
