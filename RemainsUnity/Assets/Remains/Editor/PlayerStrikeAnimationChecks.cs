using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class PlayerStrikeAnimationChecks
    {
        private static string Output=>Path.GetFullPath("../.reference/small-fixes-20261105");
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            ActorTypes.Load();WeaponCatalog.Load();Anatomy.Load();PonyAnimationData.Load();
            PonyStrikeClipAuthoring.Check();PonyStrikeChecks.Run();
            foreach(bool kick in new[]{false,true}) foreach(int fps in new[]{30,60,120}) CheckMotion(kick,fps);
            Capture();
            Debug.Log("PT-20261103-04/31: PASS — полные punch/kick20кадров, прежний контакт8/30 и cooldown13/30, пауза/повтор/прерывание/копия позы, реальные меши без одежды/антирад.");
        }
        private static ActorState Actor()=>new() {id="strike-visual",player=true,connected=true,owner="owner",level="test",
            actorType="pony",anatomy="pony",health=100,pose="idle",facing=1};
        private static void CheckMotion(bool kick,int fps)
        {
            var room=new RoomLayout {id="test",left=-20,right=20,bottom=-1,top=20,ladders=Array.Empty<RoomLadder>(),
                platforms=new[]{new RoomSurface{x=-20,y=-1,width=40,height=1}}};RoomSurfaces.Prepare(room);
            var actor=Actor();var world=new WorldState();world.actors.Add(actor);
            var strikes=new PonyStrikes();var movement=new MovementController(room);var attacks=new List<AttackEvent>();
            string id=PonyStrikeRules.Weapon(kick);float dt=1f/fps,elapsed=0;
            Require(strikes.Begin(actor,kick,room) && actor.pose==id && actor.poseTime==0,"Начало не выбрало полную позу.");
            uint sequence=actor.poseSequence;
            strikes.Step(world,"owner",1,_=>true,attacks.Add);
            Require(actor.poseTime==0 && attacks.Count==0 && strikes.Busy(actor),"Пауза продолжила удар.");
            var frames=new HashSet<int>{PonyMotionFrames.Sample(actor,0)};
            while(elapsed<.8f)
            {
                ActorPose.Advance(actor,dt);movement.Step(actor,Vector2.zero,false,dt);
                strikes.Step(world,"owner",dt,_=>false,attacks.Add);elapsed+=dt;
                if(actor.pose==id)frames.Add(PonyMotionFrames.Sample(actor,actor.poseTime));
                if(elapsed<8f/30-.001f)Require(attacks.Count==0,"Контакт сдвинулся раньше8/30.");
                if(elapsed>=13f/30+.001f)Require(!strikes.Busy(actor),"Анимация удлинила игровой cooldown.");
            }
            Require(attacks.Count==(kick?2:5) && attacks.Select(a=>a.id).Distinct().Count()==1,"Урон повторён/пропущен.");
            Require(actor.pose=="idle" && frames.Contains(19),"Не завершён последний кадр/возврат в стойку.");
            if(fps==120)Require(frames.Count==20,"При120FPS потеряны исходные кадры.");
            Require(strikes.Begin(actor,kick,room),"Повторный удар не начался.");
            strikes.Step(world,"owner",13f/30+.001f,_=>false,_=>{});actor.poseTime=.45f;
            Require(strikes.Begin(actor,kick,room) && actor.poseTime==0 && actor.poseSequence>sequence,"Та же поза не перезапущена новым действием.");
            var copy=StateJson.Copy(actor);ActorPose.Advance(copy,.1f);
            Require(copy.pose==id && PonyMotionFrames.Sample(copy,copy.poseTime)==3,"Сохранённая/удалённая поза не имеет той же фазы.");
            StanceAnimation.Select(actor,"walk",false);Require(actor.pose==id,"Ходьба стёрла удар.");
            StanceAnimation.Select(actor,"climb",false);Require(actor.pose=="climb","Лестница не прервала удар.");
            actor.pose=id;actor.poseTime=.1f;actor.crouched=true;StanceAnimation.Select(actor,"crouch",false);
            Require(actor.pose=="crouchLower","Приседание не прервало удар.");
        }
        private static void Capture()
        {
            Directory.CreateDirectory(Output);PonyVisual.Prepare();
            var actor=Actor();var visual=(PonyVisual)ActorVisualFactory.Create(actor);
            var camera=new GameObject("Проверка удара").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=1.6f;
            camera.transform.position=new Vector3(0,1,-10);camera.backgroundColor=new Color(.21f,.24f,.27f);
            try
            {
                visual.ConfigureMagicPreview(0);
                foreach(string outfit in new[]{"bare","armor.antirad"})
                {
                    if(outfit!="bare")
                    {var item=SharedInventory.Create(outfit,1);InventoryStacks.Add(actor,item);Require(CosmeticEquipment.TryEquip(actor,item.id,false,out var reason),reason);}
                    foreach(string id in new[]{"punch","kick"}) foreach(int frame in new[]{0,7,13,19})
                    {
                        actor.pose=id;actor.poseTime=(frame+.1f)/30;visual.Bind(actor);visual.DrawPose(actor.poseTime);
                        Require(visual.DrawnClip==id && visual.DrawnFrame==frame && visual.DrawnPartCount>0,"Игровое представление не выбрало кадр удара.");
                        string file=Path.Combine(Output,$"{id}-{outfit}-{frame:D2}.png");
                        Runtime.NetworkFrameCapture.Save(camera,file,width:640,height:480);
                    }
                }
            }
            finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);UnityEngine.Object.DestroyImmediate(camera.gameObject);}
        }
        private static void Require(bool value,string message)
        {if(!value)throw new InvalidOperationException("Анимация удара: "+message);}
    }
}
