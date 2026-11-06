using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEditor;
using UnityEngine;
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
            appearance=new CharacterAppearance{version=CharacterAppearance.CurrentVersion,magic=0x286CFF}};
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
                actor.x+=5;actor.y+=2;visual.DrawPose(0);output.Clear();lights.Append(visuals,output);
                Require(Vector2.Distance(output[0].Position-previous,new Vector2(5,2))<.00001f,"Источник не следует за переносом пони.");
                output.Clear();lights.Append(visuals,output,new Rect(100,100,2,2));Require(output.Count==0,"Источник вне активного вида не отсечён.");
                Require(WeaponInventory.Select(actor,null,out reason),reason);Refresh();visual.DrawPose(0);output.Clear();lights.Append(visuals,output);Require(output.Count==0,"Рог светит после освобождения копыт.");
                visual.ConfigureMagicPreview(1);visual.DrawPose(0);output.Clear();lights.Append(visuals,output);Require(output.Count==0,"Detached preview создал игровой свет.");
            }
            finally{lights.Clear();Object.DestroyImmediate(visual.gameObject);}
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
