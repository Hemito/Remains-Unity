using System;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class ActorActivityChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);} catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            var world=new WorldState();
            var player=new ActorState {id="player",player=true,connected=true,level="factory:0"};
            var npc=new ActorState {id="npc",level=player.level,x=NpcActivity.HalfWidth+1};
            world.actors.Add(player);world.actors.Add(npc);
            var activity=new ActorActivity();activity.Refresh(world);
            Require(activity.Active(player) && !activity.Active(npc),"Игрок/дальний NPC: неверная активность.");
            string asleep=StateJson.ToJson(npc);
            for(int frame=0;frame<60;frame++) if(activity.Active(npc)) ActorPose.Advance(npc,1f/60);
            Require(StateJson.ToJson(npc)==asleep,"Спящий NPC изменился от цикла анимации.");
            npc.x=NpcActivity.HalfWidth;Require(activity.Active(npc),"Граница активной зоны исключена.");
            ActorPose.Advance(npc,.1f);Require(npc.poseTime>0,"Проснувшийся NPC не продолжил анимацию.");
            activity.ObserveSimulation(npc);npc.x=NpcActivity.HalfWidth+1;
            Require(activity.TakePosePublication(npc,out bool final) && final,"Заключительная надёжная поза на переходе в сон потеряна.");
            Require(!activity.TakePosePublication(npc,out _),"Спящий NPC продолжает повторную публикацию.");
            npc.x=0;activity.ObserveSimulation(npc);npc.x=100;
            Require(activity.TakePosePublication(npc,out bool shortFinal) && shortFinal,"Короткое пробуждение между сетевыми тиками потеряно.");
            npc.x=0;activity.ObserveSimulation(npc);npc.x=100;
            var pause=new LevelPauseCoordinator(new CooperativePauseRule());pause.SetManual(player,0,0,true);
            Require(!activity.TakePosePublication(npc,out _,pause.IsPaused(world,npc.level)),"Поза отправлена во время паузы.");
            pause.SetManual(player,0,0,false);
            Require(activity.TakePosePublication(npc,out bool resumedFinal,pause.IsPaused(world,npc.level)) && resumedFinal,
                "Пауза потеряла заключительную надёжную позу.");
            Require(!activity.TakePosePublication(npc,out _),"Возобновление повторило заключительную позу.");
            foreach(float aspect in new[]{4f/3,16f/9,32f/9,64f/9})
            foreach(float zoom in new[]{5f,10f})
            {
                var bounds=new Bounds(new Vector3(0,1,0),new Vector3(2,3,0));
                float halfWidth=zoom*aspect;
                var area=Remains.Presentation.CameraFollower.AnimationArea(bounds,halfWidth,zoom,Vector2.zero,false);
                activity.SetVisibleArea(player.level,area);
                foreach(float center in new[]{bounds.max.x-halfWidth+.15f,bounds.min.x+halfWidth-.15f})
                {
                    npc.Position=new Vector2(center+halfWidth-0.01f,1);
                    Require(activity.Active(npc),"Видимый NPC замёрз на краю широкого viewport.");
                }
                npc.Position=new Vector2(Mathf.Max(area.xMax,NpcActivity.HalfWidth)+1,1);
                Require(!activity.Active(npc),"Визуальная область активирует весь уровень.");
            }
            activity.Refresh(world);
            npc.x=100;npc.attackBusy=true;Require(activity.Active(npc),"Начатая атака вне зоны не завершается.");
            npc.attackBusy=false;npc.down=true;Require(activity.Active(npc),"Оседающее тело исключено.");
            npc.down=false;npc.hold=new ActorHoldState {holder=player.id};Require(activity.Active(npc),"Удерживаемое тело исключено.");
            npc.level="factory:1";npc.attackBusy=true;Require(!activity.Active(npc),"Неактивный уровень продолжает позы/атаку.");
            npc.hold=null;npc.attackBusy=false;npc.level=player.level;npc.down=false;
            player.level="hub";activity.Refresh(world);
            Require(!activity.Active(npc),"Завод остаётся активным после ухода игрока.");
            var frozen=RecoverySnapshot.FromWorld(world);
            Require(frozen.CreateLiveWorld().actors.Count==2,"Спящий актёр исчез из recovery.");
            player.connected=false;activity.Refresh(world);Require(!activity.Active(player),"Отключённый персонаж хаба активен.");
            player.connected=true;activity.Refresh(world);Require(activity.Active(player),"Возвращение игрока не обновило активность.");
            CheckIndex();
            Debug.Log("Активность писателей поз: PASS — сон/граница/пробуждение, атака, пассивное и удерживаемое тело, уход/возврат и recovery.");
        }
        private static void CheckIndex()
        {
            const string level="factory:0";
            var world=new WorldState();
            var player=new ActorState {id="player",player=true,connected=true,level=level};
            var npc=new ActorState {id="target",level=level,x=40};
            world.actors.Add(player);world.actors.Add(npc);
            for(int i=0;i<500;i++) world.actors.Add(new ActorState {id="sleep-"+i,level=level,x=300,y=300});
            var index=new ActorFrameIndex();
            var activity=new ActorActivity();
            index.Reconcile(world);
            void Select(Rect area)
            {
                activity.Refresh(world,index.Players);activity.SetVisibleArea(level,area);index.Select(activity,level,area);
            }
            var camera=new Rect(-15,-10,30,20);
            Select(camera);
            Require(index.Frame.Count==2 && index.Frame[1]==npc && index.Visited<20,"Индекс просматривает дальних спящих или исключает границу.");
            npc.x=240;Select(camera);
            Require(index.Frame.Count==1,"Локально сдвинувшийся NPC остался в старой корзине.");
            npc.x=70;index.Observe(npc);Select(new Rect(60,-10,30,20));
            Require(index.Frame.Count==2,"Авторитетное движение не пробудило NPC в широком кадре.");
            var replacement=npc.Copy();
            replacement.x=300;replacement.attackBusy=true;world.actors[1]=replacement;
            index.Reconcile(world,new[]{"actors:target"});Select(camera);
            Require(index.Frame.Count==2 && index.Frame[1]==replacement,"Точечное обновление потеряло начатую дальнюю атаку.");
            replacement.attackBusy=false;Select(camera);
            Require(index.Frame.Count==1,"Завершённая атака оставила дальнего NPC активным.");
            player.x=300;player.y=300;Select(camera);
            Require(index.Frame.Count==501,"Перемещение игрока не пробудило соседнюю группу.");
            world.actors.Remove(replacement);index.Reconcile(world,new[]{"actors:target"});
            Require(index.Find("target")==null,"Удалённый NPC остался в индексе.");
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
    }
}
