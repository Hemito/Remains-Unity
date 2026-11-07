using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    public static class HornLightChecks
    {
        public static void RunBatch()
        {try{Run();EditorApplication.Exit(0);}catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}}
        public static void Run()
        {
            Anatomy.Load();WeaponCatalog.Load();ActorTypes.Load();PonyVisual.Prepare();
            foreach(string clothing in new[]{"","armor.antirad"})CheckVisual(clothing);
            CheckMineActivity();
            Debug.Log("Свет рога: PASS — реальные bare/antirad, исходные и authored-позы, обе стороны, смещение, точный renderer, RGB/opacity, выключение/preview/culling; подтверждённая мина и её отпускание.");
        }
        private static ActorState Actor()=>new()
        {id="horn-check",character="horn-check",owner="horn-owner",player=true,connected=true,controlStateVersion=1,generation=3,lifecycleSequence=7,
            level="hub",anatomy="pony",actorType="pony",pose="idle",health=100,weapon=null,weaponInventoryVersion=1,loadoutVersion=1,x=2,y=3,
            appearance=new CharacterAppearance{version=CharacterAppearance.CurrentVersion,magic=0x43FF48}};
        private static void CheckVisual(string clothing)
        {
            var actor=Actor();var pistol=SharedInventory.Create("pistol",1);pistol.weaponState=new WeaponItemState{rounds=WeaponCatalog.Get("pistol").magazineSize};actor.inventory.Add(pistol);
            if(clothing.Length>0)
            {var item=SharedInventory.Create(clothing,1);actor.inventory.Add(item);Require(CosmeticEquipment.TryEquip(actor,item.id,false,out var reason),reason);}
            var world=new WorldState{worldId="horn-check-world"};world.actors.Add(actor);var held=new HeldPropColorSource();var magic=new PonyMagicActivitySource();
            void Refresh(){held.Rebuild(world);magic.Rebuild(world,held);}
            var visual=ActorVisualFactory.Create(actor) as PonyVisual;Require(visual!=null,"Представление пони не выбрано.");
            var visuals=new Dictionary<string,ActorVisual>{{actor.id,visual}};var output=new List<WorldLightEmission>();var lights=new HornLightSources();
            try
            {
                Refresh();visual.ConfigureMagicSource(magic);visual.Bind(actor);visual.DrawPose(0);
                lights.Append(visuals,output);Require(output.Count==0,"Пустые копыта светят.");
                Require(WeaponInventory.Select(actor,pistol.id,out var reason),reason);Refresh();
                foreach(var sample in new[]{(Pose:"idle",IsRest:(string)null),(Pose:"walk",IsRest:(string)null),(Pose:"down",IsRest:(string)null),
                    (Pose:BedRest.BackClip,IsRest:BedRest.Back),(Pose:BedRest.BellyClip,IsRest:BedRest.Belly)})foreach(int facing in new[]{-1,1})
                {
                    actor.pose=sample.Pose;actor.rest=sample.IsRest==null?null:"horn-check-bed";actor.restPose=sample.IsRest;actor.facing=facing;
                    visual.Bind(actor);visual.DrawPose(.1f,true);output.Clear();lights.Append(visuals,output);
                    Require(output.Count==1,"Нет рога: одежда="+clothing+", поза="+sample.Pose+", rest="+sample.IsRest+", facing="+facing);
                    var e=output[0];Require(visual.TryGetMagicRenderer(out var renderer) && renderer.enabled,"Нет настоящего renderer рога.");
                    Require(Vector2.Distance(e.Position,renderer.bounds.center)<.00001f,"Рог оторвался от actual renderer: "+sample.Pose);
                    var expected=PonyAppearance.FromRgb(actor.appearance.magic)*HornLightSources.RadianceScale;
                    Require(Vector3.Distance(new Vector3(e.Radiance.r,e.Radiance.g,e.Radiance.b),new Vector3(expected.r,expected.g,expected.b))<.00001f,"Цвет света не соответствует магии.");
                    Require(Mathf.Approximately(e.Gain,visual.DrawnMagicActivity.Opacity) && Mathf.Approximately(e.Gain,.5f),"Сила не совпадает с подтверждённым рогом.");
                }
                actor.rest=actor.restPose=null;actor.pose="idle";actor.facing=1;visual.Bind(actor);visual.DrawPose(0);output.Clear();lights.Append(visuals,output);var previous=output[0].Position;
                if(clothing.Length==0)CheckBrightness(output[0]);
                actor.x+=5;actor.y+=2;visual.DrawPose(0);output.Clear();lights.Append(visuals,output);
                Require(Vector2.Distance(output[0].Position-previous,new Vector2(5,2))<.00001f,"Источник не следует за переносом пони.");
                output.Clear();lights.Append(visuals,output,new Rect(100,100,2,2));Require(output.Count==0,"Источник вне активного вида не отсечён.");
                Require(WeaponInventory.Select(actor,null,out reason),reason);Refresh();visual.DrawPose(0);output.Clear();lights.Append(visuals,output);Require(output.Count==0,"Рог светит после освобождения копыт.");
                visual.ConfigureMagicPreview(1);visual.DrawPose(0);output.Clear();lights.Append(visuals,output);Require(output.Count==0,"Detached preview создал игровой свет.");
            }
            finally{lights.Clear();Object.DestroyImmediate(visual.gameObject);}
        }
        private static void CheckBrightness(WorldLightEmission horn)
        {
            var scene=new LevelLightScene(new float[60,60],Vector2.zero,10,Array.Empty<LevelLightScene.ObjectSprite>());
            using var light=new LevelLight(scene,new LevelLightSources(Array.Empty<LevelLightSource>(),60,60),.2f);light.BuildAll();
            var context=new LightShaderContext();using var view=new LevelLightView(light,context);
            var camera=new GameObject("Horn light comparison").AddComponent<Camera>();
            var wall=new GameObject("Horn light comparison wall").AddComponent<SpriteRenderer>();
            var material=new Material(GameAssets.Load<Shader>(RoomDecorView.SpriteShaderResource));
            var tile=Texture2D.whiteTexture;
            var sprite=Sprite.Create(tile,new Rect(0,0,tile.width,tile.height),Vector2.one*.5f,tile.width/6f);
            var position=horn.Position;
            var sample=position+Vector2.right*.4f;
            camera.enabled=false;camera.orthographic=true;camera.orthographicSize=1.45f;
            camera.transform.position=new Vector3(position.x,position.y-.35f,-10);
            wall.transform.position=new Vector3(3,3,1);wall.sortingOrder=short.MinValue;
            wall.sprite=sprite;wall.sharedMaterial=material;wall.color=new Color(.7f,.68f,.65f);
            var lampPosition=position+Vector2.up*.9f;
            var lamp=new WorldLightEmission("lamp-check",new[]{new Vector4(lampPosition.x,lampPosition.y,1,0)},
                new Rect(lampPosition-Vector2.one*.01f,Vector2.one*.02f),new Color(1.2f,1,.85f),.6f,4);
            var previous=new WorldLightEmission(horn.Id,horn.Samples,horn.Bounds,PonyAppearance.FromRgb(0x43FF48)*2,horn.Width,horn.Reach,horn.Gain);
            string output=Path.GetFullPath("../.reference/horn-brightness");Directory.CreateDirectory(output);
            context.Activate();
            try
            {
                float Draw(WorldLightEmission[] sources,string capture=null)
                {
                    view.Present(scene.WorldRect,Array.Empty<SpriteEmission>(),Array.Empty<ProjectedLightShadow>(),Array.Empty<string>(),sources);
                    if(capture!=null)NetworkFrameCapture.Save(camera,Path.Combine(output,capture+".png"),width:960,height:540);
                    var texture=Shader.GetGlobalTexture("_RemainsRadiance");var area=view.WorldArea;
                    int x=(int)((sample.x-area.xMin)/area.width*texture.width),y=(int)((sample.y-area.yMin)/area.height*texture.height);
                    var readback=AsyncGPUReadback.Request(texture,0,x,1,y,1,0,1,TextureFormat.RGBAFloat);readback.WaitForCompletion();
                    Require(!readback.hasError,"Не удалось измерить свет рога на GPU.");
                    return readback.GetData<Color>()[0].g;
                }
                foreach(bool lit in new[]{false,true})
                {
                    var baseline=lit?new[]{lamp}:Array.Empty<WorldLightEmission>();string label=lit?"lit":"dark";
                    float background=Draw(baseline);
                    float oldHorn=Draw(baseline.Append(previous).ToArray(),label+"-before")-background;
                    float newHorn=Draw(baseline.Append(horn).ToArray(),label+"-after")-background;
                    Require((lit?background>.2f:background<.001f) && oldHorn>.1f && Mathf.Abs(newHorn-oldHorn*.5f)<.002f,
                        $"Яркость рога {label}: фон={background}, прежде={oldHorn}, теперь={newHorn}.");
                    Debug.Log($"Horn brightness {label}: background={background:F4}, previous horn={oldHorn:F4}, current horn={newHorn:F4}.");
                }
            }
            finally
            {
                context.Release();Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(wall.gameObject);
                Object.DestroyImmediate(sprite);Object.DestroyImmediate(material);
            }
        }
        private static void CheckMineActivity()
        {
            var actor=Actor();var world=new WorldState{worldId="horn-mine-world"};world.actors.Add(actor);
            var mine=new MineState{id="horn-mine",definition="hmine",level=actor.level,holder=actor.id};world.mines.Add(mine);
            var held=new HeldPropColorSource();var magic=new PonyMagicActivitySource();held.Rebuild(world);magic.Rebuild(world,held);
            var active=magic.Get(actor);Require(active.Action==PonyMagicAction.Telekinesis && active.SourceId==mine.id && active.Opacity>.0f,"Подтверждённая мина не включает рог.");
            mine.holder=null;Require(magic.Get(actor).Opacity==0,"Отпущенная мина оставляет свет до следующего rebuild.");
            mine.holder=actor.id;held.Rebuild(world);magic.Rebuild(world,held);actor.generation++;
            Require(magic.Get(actor).Opacity==0,"Новая генерация держателя использует старую подготовку мины.");
        }
        private static void Require(bool value,string detail){if(!value)throw new InvalidOperationException("Свет рога: "+detail);}
    }
}
