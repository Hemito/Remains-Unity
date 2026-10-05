using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Presentation;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class LadderEntryPoseChecks
    {
        public static void Run()
        {
            Anatomy.Load();PonyVisual.Prepare();
            var settings=MovementSettings.Load();
            var trace=new StringBuilder("sequence,rate,facing,tick,inputY,jump,x,y,vy,pose,phase,sampledFrame,drawnClip,drawnFrame,lowestFootY,climbing\n");
            foreach(int rate in new[]{30,60,144})
            foreach(int facing in new[]{-1,1})
            {
                CheckFirstUp(rate,facing,settings,trace);
                foreach(string sequence in new[]{"up-stop-down","up-down","up-jump"})
                    CheckCommands(sequence,rate,facing,settings,trace);
                CheckAdmission(rate,facing,settings,trace);
            }
            CheckOneWay(settings);CheckCeiling(settings);CheckMissingData();CheckPositionRecovery(settings);
            var directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../..", ".reference/tests/ladder-entry"));
            Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"commands.csv"),trace.ToString());
            Debug.Log("Вход на лестницу: PASS — обычные Up→stop, Up→Down, Up→jump, фактические четыре контакта и PonyVisual, общий pose/phase без подъёма тела, 30/60/144 Гц, обе стороны, сохранение входа, явный старт climb, проход балки и потолок. Trace: "+directory);
        }

        private static void CheckFirstUp(int rate,int facing,MovementSettings settings,StringBuilder trace)
        {
            var room=Room(facing);var actor=Actor(room,rate,facing,"first-up");
            var motion=new MovementController(room,settings);
            var visual=new GameObject(actor.id).AddComponent<PonyVisual>();
            try
            {
                visual.Bind(actor);
                Step(actor,motion,visual,Vector2.up,false,rate,0,"first-up",trace);
                Require(actor.climbing && actor.pose=="jump" && actor.poseTime==0,
                    "Первый Up не выбрал явную входную позу без скрытого продвижения climb");
                Require(Mathf.Abs(actor.y-settings.climbSpeed/rate)<.00001f && actor.vy==settings.climbSpeed,
                    "Вход изменил обычное перемещение или скорость тела");
                Require(visual.DrawnFrame==10,"Первый Up должен рисовать выбранный общей кривой jump10");
                var unsafeClimb=StateJson.Copy(actor);unsafeClimb.pose="climb";unsafeClimb.poseTime=0;
                Require(PonyMotionFrames.Sample(unsafeClimb,0)==1 && MinimumY(Contacts(unsafeClimb))+unsafeClimb.y<-.1f,
                    "Реальный исходный climb1 больше не воспроизводит проникновение копыта при первом Up");
                Step(actor,motion,visual,Vector2.zero,false,rate,1,"first-up-stop",trace);
                Require(actor.climbing && actor.pose=="jump" && actor.vy==0 && visual.DrawnFrame==15,
                    "Отпускание Up не сохранило безопасную позу при том же хвате");
                unsafeClimb=StateJson.Copy(actor);unsafeClimb.pose="climb";unsafeClimb.poseTime=0;
                Require(PonyMotionFrames.Sample(unsafeClimb,0)==0 && MinimumY(Contacts(unsafeClimb))+unsafeClimb.y<-.1f,
                    "Реальный неподвижный climb0 больше не воспроизводит проникновение после первого Up");
            }
            finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);}
        }

        private static void CheckCommands(string sequence,int rate,int facing,MovementSettings settings,StringBuilder trace)
        {
            var room=Room(facing);var actor=Actor(room,rate,facing,sequence);
            var motion=new MovementController(room,settings);
            var visual=new GameObject(actor.id).AddComponent<PonyVisual>();
            try
            {
                visual.Bind(actor);int tick=0;
                Step(actor,motion,visual,Vector2.up,false,rate,tick++,sequence,trace);
                Step(actor,motion,visual,Vector2.up,false,rate,tick++,sequence,trace);
                Require(actor.climbing && actor.pose=="jump" && Mathf.Abs(actor.y-2*settings.climbSpeed/rate)<.00001f,
                    "Два коротких Up изменили тело или преждевременно выбрали нижнее копыто climb");
                if(sequence=="up-stop-down")
                {
                    float height=actor.y;
                    for(int i=0;i<Mathf.CeilToInt(rate*.2f);i++)
                        Step(actor,motion,visual,Vector2.zero,false,rate,tick++,sequence,trace);
                    Require(actor.climbing && actor.pose=="jump" && actor.y==height && actor.vy==0 && actor.poseTime==0,
                        "Остановка продвинула невидимый climb или сдвинула хват");
                    var restored=StateJson.Copy(actor);
                    new MovementController(room,settings).Step(restored,Vector2.zero,false,1f/rate);
                    Require(restored.climbing && restored.pose==actor.pose && restored.poseTime==actor.poseTime && restored.Position==actor.Position,
                        "Восстановление без скрытого состояния сервиса изменило входную позу");
                }
                if(sequence=="up-jump")
                {
                    Step(actor,motion,visual,Vector2.zero,true,rate,tick++,sequence,trace);
                    Require(!actor.climbing && actor.pose=="jump" && actor.vy>0,"Обычный прыжок не отпустил входной хват");
                    Step(actor,motion,visual,Vector2.down,false,rate,tick++,sequence,trace);
                    Require(!actor.climbing,"Down в воздухе повторно получил отпущенный прыжком хват");
                }
                else
                {
                    Step(actor,motion,visual,Vector2.down,false,rate,tick++,sequence,trace);
                    Require(!actor.climbing || actor.vy<0 && actor.pose=="jump" && visual.DrawnFrame==23,
                        "Смена Up→Down не выбрала общий безопасный кадр спуска");
                }
                bool landed=false;
                for(int i=0;i<rate*3;i++)
                {
                    Step(actor,motion,visual,actor.climbing?Vector2.down:Vector2.zero,false,rate,tick++,sequence,trace);
                    Require(actor.y>=-.0001f,"Тело пересекло твёрдый нижний пол");
                    if(!actor.climbing && Mathf.Abs(actor.y)<.0001f && actor.vy==0) {landed=true;break;}
                }
                Require(landed && actor.pose!="climb","Обычные команды не завершились опорой без хвата");
            }
            finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);}
        }

        private static void CheckAdmission(int rate,int facing,MovementSettings settings,StringBuilder trace)
        {
            var room=Room(facing);var actor=Actor(room,rate,facing,"up-admission");
            var motion=new MovementController(room,settings);
            var visual=new GameObject(actor.id).AddComponent<PonyVisual>();
            try
            {
                visual.Bind(actor);bool admitted=false;int climbSamples=0;
                for(int tick=0;tick<rate/2;tick++)
                {
                    string previous=actor.pose;float y=actor.y;
                    Step(actor,motion,visual,Vector2.up,false,rate,tick,"up-admission",trace);
                    Require(actor.climbing && Mathf.Abs(actor.y-y-settings.climbSpeed/rate)<.00001f,
                        "Вход добавил скачок или задержку обычного движения тела");
                    if(actor.pose!="climb") continue;
                    climbSamples++;
                    if(previous!="climb")
                    {
                        admitted=true;
                        Require(actor.poseTime==0 && visual.DrawnFrame==1,"Climb не начал свой первый реальный кадр с нулевой фазой");
                    }
                }
                Require(admitted && climbSamples>3,"Безопасный вход не перешёл в обычный цикл climb");
            }
            finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);}
        }

        private static void CheckOneWay(MovementSettings settings)
        {
            var room=Room(1);room.platforms[0].oneWay=true;
            var actor=Actor(room,60,1,"one-way");
            new MovementController(room,settings).Step(actor,Vector2.up,false,1f/60);
            Require(actor.climbing && actor.pose=="climb" && MinimumY(Contacts(actor))+actor.y<0,
                "Новая входная policy заблокировала намеренный проход сквозь one-way балку");
        }

        private static void CheckCeiling(MovementSettings settings)
        {
            var room=Room(1);
            room.platforms=new[]{room.platforms[0],new RoomSurface {x=-2,y=1.44f,width=4,height=.2f}};
            var actor=Actor(room,60,1,"ceiling");var motion=new MovementController(room,settings);
            for(int i=0;i<10;i++) {ActorPose.Advance(actor,1f/60);motion.Step(actor,Vector2.up,false,1f/60);}
            Require(actor.climbing && actor.pose=="jump" && actor.vy==0 && Mathf.Abs(actor.y-.04f)<.00001f,
                "Вход обошёл настоящий потолок или телепортировал тело к безопасной высоте");
            foreach(var point in Contacts(actor)) Require(actor.y+point.y>0,"Вход под потолком рисует копыто в полу");
        }

        private static void CheckMissingData()
        {
            var room=Room(1);var actor=Actor(room,60,1,"missing-contacts");actor.climbing=true;actor.anatomy="raider";
            ExpectDataError(()=>new LadderEntryPose().Select(actor,room),actor,room);
            actor.anatomy="pony";
            ExpectDataError(()=>new LadderEntryPose("missing-entry-pose").Select(actor,room),actor,room);
        }
        private static void CheckPositionRecovery(MovementSettings settings)
        {
            var room=Room(1);var actor=Actor(room,60,1,"position-recovery");actor.x=room.right+2;
            var motion=new MovementController(room,settings);int calls=0;
            motion.PositionRecovered+=repaired=>
            {
                Require(ReferenceEquals(repaired,actor) && repaired.x==room.right && repaired.y==0,
                    "Сигнал восстановления потерял настоящее исправленное состояние до дальнейшего движения");calls++;
            };
            motion.Step(actor,Vector2.zero,false,1f/60);
            motion.Step(actor,Vector2.zero,false,1f/60);
            Require(calls==1,"Восстановление позиции не сообщило один фактический reset или повторило его для обычного кадра");
        }
        private static void ExpectDataError(Action action,ActorState actor,RoomLayout room)
        {
            try {action();}
            catch(InvalidOperationException error)
            {
                Require(error.Message.Contains(actor.id) && error.Message.Contains(actor.anatomy) && error.Message.Contains(room.id),
                    "Ошибка входных данных потеряла контекст актёра/анатомии/комнаты");return;
            }
            throw new InvalidOperationException("Вход лестницы молча заменил отсутствующие данные.");
        }

        private static void Step(ActorState actor,MovementController motion,PonyVisual visual,Vector2 input,bool jump,int rate,int tick,string sequence,StringBuilder trace)
        {
            ActorPose.Advance(actor,1f/rate);motion.Step(actor,input,jump,1f/rate);
            int sampled=PonyMotionFrames.Sample(actor,actor.poseTime);visual.DrawPose(actor.poseTime);
            Require(visual.DrawnClip==ActorPose.Clip(actor) && visual.DrawnFrame==sampled,
                $"Рисунок не совпал с общей позой: sequence={sequence}, rate={rate}, tick={tick}, pose={actor.pose}");
            var contacts=Contacts(actor);Require(contacts.Count==4,"Проверка не получила все четыре действительных копыта");
            float foot=actor.y+MinimumY(contacts);
            if(actor.climbing)
            {
                Require(actor.facing==(int)motion.Room.ladders[0].facing,"Вход потерял направление настоящего хвата");
                foreach(var point in contacts)
                    Require(actor.y+point.y>=-.0001f,
                        $"Копыто в полу при активном хвате: sequence={sequence}, rate={rate}, facing={actor.facing}, tick={tick}, pose={actor.pose}, frame={sampled}, bodyY={actor.y:R}, footY={actor.y+point.y:R}");
            }
            trace.AppendLine(string.Join(",",sequence,rate,actor.facing,tick,F(input.y),jump,F(actor.x),F(actor.y),F(actor.vy),
                actor.pose,F(actor.poseTime),sampled,visual.DrawnClip,visual.DrawnFrame,F(foot),actor.climbing));
        }
        private static IReadOnlyList<Vector2> Contacts(ActorState actor)
        {
            Require(Anatomy.TrySupportContacts(actor,out var contacts),"Не найдены запечённые контакты: "+actor.id);return contacts;
        }
        private static float MinimumY(IReadOnlyList<Vector2> points)
        {
            float value=float.PositiveInfinity;foreach(var point in points) value=Mathf.Min(value,point.y);return value;
        }
        private static ActorState Actor(RoomLayout room,int rate,int facing,string sequence)=>new()
        {
            id=$"ladder-entry-{sequence}-{rate}-{facing}",player=true,connected=true,level="hub",pose="idle",
            x=room.ladders[0].x+room.ladders[0].movementOffset,facing=-facing
        };
        private static RoomLayout Room(int facing)=>new()
        {
            id="ladder-entry-solid",left=-5,right=5,bottom=-1,top=6,
            ladders=new[]{new RoomLadder {x=0,movementOffset=facing*.5f,facing=(LadderFacing)facing,bottom=0,top=3}},
            platforms=new[]{new RoomSurface {x=-5,y=-1,width=10,height=1}}
        };
        private static string F(float value)=>value.ToString("R",CultureInfo.InvariantCulture);
        private static void Require(bool valid,string context)
        {
            if(!valid) throw new InvalidOperationException("Вход на лестницу: "+context);
        }
    }
}
