using System;
using Remains.Domain;
using Remains.Content;
using Remains.Gameplay;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class LadderHoldChecks
    {
        public static void Run()
        {
            Anatomy.Load();
            CheckAnimation();LadderPoseContactChecks.Run();
            var room=RoomLayout.Load();
            foreach(float dt in new[]{1f/30,1f/60,1f/144})
            {
                CheckJunctionAndStance(dt);
                CheckHeldDownLanding(dt);
                var falling=new ActorState {id="ladder-air-down",x=4,y=1,vy=-1};
                Require(!ActorTerrainMotion.Supported(room,Anatomy.Movement(falling),falling.Position),"Проверка захвата в воздухе ошибочно стоит на опоре");
                var fallingMotion=new MovementController(room);
                fallingMotion.Step(falling,Vector2.down,false,dt);
                Require(!falling.climbing && falling.y<1 && falling.vy<0,"S в воздухе повторно захватывает лестницу");
                fallingMotion.Step(falling,Vector2.up,false,dt);
                Require(falling.climbing && falling.vy>0,"W не захватывает лестницу в воздухе");
                fallingMotion.Step(falling,Vector2.down,false,dt);
                Require(falling.climbing && falling.vy<0,"S не продолжает спуск после захвата");
                var topRoom=new RoomLayout {id="ladder-top",left=-5,right=5,bottom=-1,top=6,
                    ladders=new[]{new RoomLadder {x=0,bottom=0,top=3}},
                    platforms=new[]{new RoomSurface {x=-5,y=-1,width=10,height=1},
                        new RoomSurface {x=-2,y=2.8f,width=4,height=.2f,oneWay=true}}};
                var onTop=new ActorState {id="ladder-top-down",x=0,y=3};
                new MovementController(topRoom).Step(onTop,Vector2.down,false,dt);
                Require(onTop.climbing && onTop.y<3 && onTop.vy<0,"S с верхней опоры не начинает спуск");
                var actor=new ActorState {id="ladder-hold",x=4,y=1};
                var motion=new MovementController(room);
                motion.Step(actor,Vector2.up,false,dt);
                Require(actor.climbing && actor.vy>0,"Вход на лестницу");
                float height=actor.y;
                for(int i=0;i<300;i++) motion.Step(actor,Vector2.zero,false,dt);
                Require(actor.climbing && actor.pose=="climb" && actor.y==height && actor.vy==0,"Удержание без ввода");
                float time=actor.poseTime;ActorPose.Advance(actor,1);
                Require(actor.poseTime==time,"Неподвижная поза не перебирает кадры");
                var copy=StateJson.Copy(actor);
                new MovementController(room).Step(copy,Vector2.zero,false,dt);
                Require(copy.climbing && copy.y==height,"Загрузка сохраняет захват лестницы");
                motion.Step(actor,Vector2.zero,true,dt);
                Require(!actor.climbing && actor.vy>0 && actor.pose=="jump","Прыжок отпускает лестницу");
                motion.Step(actor,Vector2.down,false,dt);
                Require(!actor.climbing && actor.vy>0,"S сразу после прыжка вернул персонажа на лестницу");
                foreach(int facing in new[]{-1,1})
                {
                    var ladder=room.ladders[0];var originalFacing=ladder.facing;
                    ladder.facing=(LadderFacing)facing;
                    var turning=new ActorState {id="ladder-facing",x=4,y=1,facing=-facing};
                    var turningMotion=new MovementController(room);
                    turningMotion.Step(turning,Vector2.up,false,dt);
                    Require(turning.climbing && turning.facing==facing,"Вход спиной не развернул тело к ступеням");
                    var sideways=Vector2.right*-facing;
                    turningMotion.Step(turning,sideways,false,dt);
                    Require(turning.climbing && turning.facing==facing,"Горизонтальный ввод развернул корпус на лестнице");
                    var restored=StateJson.Copy(turning);
                    Require(restored.facing==facing && restored.climbing,"Копия потеряла направление хвата");
                    turningMotion.Step(turning,sideways,true,dt);
                    Require(!turning.climbing && turning.facing==-facing,"После прыжка направление осталось заблокировано");
                    ladder.facing=originalFacing;
                }
                actor=new ActorState {id="ladder-side",x=4,y=1,climbing=true};
                for(int i=0;i<60;i++) motion.Step(actor,Vector2.right,false,dt);
                Require(actor.climbing && Mathf.Abs(actor.x-4)<.001f && Mathf.Abs(actor.y-1)<.001f,"Боковой ввод сдвигает тело без опоры");
                motion.Step(actor,Vector2.right,true,dt);
                Require(!actor.climbing && actor.vx>0 && actor.vy>0,"Прыжок вбок не отпускает хват");
                foreach(int direction in new[]{-1,1})
                {
                    var supportedActor=new ActorState {id="ladder-supported-side",x=0,y=3,climbing=true};
                    new MovementController(topRoom).Step(supportedActor,Vector2.right*direction,false,dt);
                    Require(!supportedActor.climbing && supportedActor.vx*direction>0 && Mathf.Abs(supportedActor.y-3)<.001f,
                        "Боковой сход с опоры требует прыжка");
                }
                var openEnd=new RoomLayout {id="ladder-open-end",left=-5,right=5,bottom=-1,top=6,
                    ladders=new[]{new RoomLadder {x=0,bottom=0,top=3}},
                    platforms=new[]{new RoomSurface {x=-5,y=-1,width=10,height=1}}};
                var exiting=new ActorState {id="ladder-no-top-platform",x=0,y=2.9f,climbing=true};
                var exitMotion=new MovementController(openEnd);
                for(int i=0;i<20 && exiting.climbing;i++) exitMotion.Step(exiting,Vector2.up,false,dt);
                Require(!exiting.climbing && exiting.y==3 && exiting.vy>0,"Конец лестницы без платформы запирает подъём");
                exitMotion.Step(exiting,Vector2.right,false,dt);
                Require(!exiting.climbing && exiting.vx>0,"Выход сверху без платформы не возвращает горизонтальное управление");
                foreach(int direction in new[]{-1,1})
                {
                    var sidewaysEnd=new ActorState {id="ladder-end-side",y=3,climbing=true};
                    new MovementController(openEnd).Step(sidewaysEnd,Vector2.right*direction,false,dt);
                    Require(!sidewaysEnd.climbing && sidewaysEnd.vx*direction>0,"Боковой сход с конца лестницы требует прыжка");
                }
                var landing=new ActorState {id="ladder-floor-exit",y=.01f,climbing=true};
                new MovementController(openEnd).Step(landing,Vector2.down,false,dt);
                Require(!landing.climbing && landing.y==0 && landing.vy==0 && landing.pose!="climb",
                    "После опоры на нижний пол осталась поза лестницы");
                actor=new ActorState {id="ladder-down",x=4,y=1,climbing=true,down=true};
                motion.Step(actor,Vector2.zero,false,dt);
                Require(!actor.climbing,"Нокдаун снимает захват лестницы");
                Debug.Log($"Удержание лестницы: PASS, dt={dt}, остановка, загрузка, прыжок и выход.");
            }
        }
        private static void CheckHeldDownLanding(float dt)
        {
            foreach(bool intermediate in new[]{false,true})
            {
                float landing=intermediate?2:0;
                var room=new RoomLayout {id="ladder-held-down-landing",left=-5,right=5,bottom=-1,top=7,
                    ladders=new[]{new RoomLadder {x=0,bottom=0,top=4}},
                    platforms=intermediate?new[]{new RoomSurface {x=-5,y=-1,width=10,height=1},
                        new RoomSurface {x=-2,y=1.8f,width=4,height=.2f}}:
                        new[]{new RoomSurface {x=-5,y=-1,width=10,height=1}}};
                var actor=new ActorState {id="descending-held-S",y=landing+1.2f,climbing=true,pose="climb",facing=1};
                var motion=new MovementController(room);
                for(int i=0;i<Mathf.CeilToInt(2/dt);i++) motion.Step(actor,Vector2.down,false,dt);
                Require(!actor.climbing && !actor.crouched && actor.pose!="crouch" && actor.pose!="crouchLower" && Mathf.Abs(actor.y-landing)<.001f,
                    $"Held S после схода {(intermediate?"на промежуточный твёрдый пол":"на нижний пол")} повторно захватывает лестницу или ложится: dt={dt}, y={actor.y}, climbing={actor.climbing}, crouched={actor.crouched}, pose={actor.pose}");
                var replica=actor.Copy();ActorPose.CopyMotion(actor,replica);
                new MovementController(room).Step(replica,Vector2.down,false,dt);
                Require(replica.crouchPressConsumed && !replica.crouched,"Обновление snapshot повторно вооружило уже использованный S-жест");
                var newLife=actor.Copy();newLife.lifecycleSequence++;ActorPose.CopyMotion(actor,newLife);
                Require(!newLife.crouchPressConsumed,"Input-память лестницы перешла в другую жизнь актёра");
                motion.Step(actor,Vector2.zero,false,dt);
                for(int i=0;i<Mathf.CeilToInt(.25f/dt);i++) motion.Step(actor,Vector2.down,false,dt);
                Require(actor.crouched && !actor.climbing,"Новый S после отпускания не разрешает обычное приседание на посадке");
                motion.Step(actor,Vector2.up,false,dt);
                Require(!actor.crouched,"W не выводит из приседания возле лестницы после нового S");
            }
        }
        private static void CheckJunctionAndStance(float dt)
        {
            var settings=MovementSettings.Load();
            foreach(bool reversed in new[]{false,true})
            {
                var lower=new RoomLadder {x=0,bottom=0,top=3,facing=LadderFacing.Right};
                var upper=new RoomLadder {x=.5f,bottom=2.9f,top=6,facing=LadderFacing.Left};
                var junction=new RoomLayout {id="ladder-z-junction",left=-5,right=5,bottom=-1,top=8,
                    platforms=new[]{new RoomSurface {x=-5,y=-1,width=10,height=1},
                        new RoomSurface {x=-2,y=2.8f,width=4,height=.2f,oneWay=true}},
                    ladders=reversed?new[]{upper,lower}:new[]{lower,upper}};
                var actor=new ActorState {id="junction",x=0,y=2.7f};
                var motion=new MovementController(junction,settings);
                motion.Step(actor,Vector2.up,false,dt);
                Require(actor.climbing && actor.facing==1,"Вход выбрал соседнюю верхнюю лестницу по порядку массива");
                for(int i=0;i<120 && actor.climbing;i++)
                {
                    float previous=actor.y;
                    motion.Step(actor,Vector2.up,false,dt);
                    Require(Mathf.Abs(actor.x)<.0001f && actor.facing==1,"Хват нижней лестницы перескочил на ось/направление верхней");
                    Require(actor.y>=previous && actor.y-previous<=settings.climbSpeed*dt+.0001f,
                        "Стык лестниц телепортировал тело по вертикали");
                }
                Require(!actor.climbing && Mathf.Abs(actor.y-3)<.0001f,"Нижняя лестница не выпустила на свою верхнюю опору");
                var crouched=new ActorState {id="stand-under-ladder",x=0,y=0,crouched=true,pose="crouch"};
                var stanceMotion=new MovementController(junction);
                stanceMotion.Step(crouched,Vector2.up,false,dt);
                Require(!crouched.crouched && crouched.climbing,"Подъём под лестницей сохранил лежачее состояние вместо стоячего хвата");
                stanceMotion.Step(crouched,Vector2.right,true,dt);
                Require(!crouched.crouched && !crouched.climbing,"Прыжок с лестницы вернул лежачее состояние");
            }
        }
        private static void CheckAnimation()
        {
            PonyAnimationData.Load();
            foreach(int direction in new[]{-1,1})
            {
                float phase=0;
                for(int tick=1;tick<=48;tick++)
                {
                    phase+=direction/30f;
                    int expected=1+((direction*tick%12)+12)%12;
                    Require(PonyAnimationData.FrameIndex("climb",phase,false)==expected,
                        $"Погрешность границы пропустила/повторила кадр лестницы: direction={direction}, tick={tick}, time={phase:R}");
                }
            }
            Require(AnimationFrames.Sample(-3.001f/30,30,13,true,1)==9 &&
                AnimationFrames.Sample(-2.999f/30,30,13,true,1)==10 &&
                AnimationFrames.Sample(2.999f/30,30,13,true,1)==3 &&
                AnimationFrames.Sample(3.001f/30,30,13,true,1)==4,
                "Допуск границы захватил обычную промежуточную фазу");
            var actor=new ActorState {id="ladder-animation",climbing=true,pose="climb",vy=3.15f};
            actor.poseTime=.14f;
            int before=PonyAnimationData.FrameIndex(actor.pose,actor.poseTime,false);
            ActorPose.Advance(actor,1f/30);
            Require(PonyAnimationData.FrameIndex(actor.pose,actor.poseTime,false)==before+1,"Подъём продвигает цикл");
            actor.vy=-4.725f;ActorPose.Advance(actor,1f/30);
            Require(PonyAnimationData.FrameIndex(actor.pose,actor.poseTime,false)==before,"Смена направления возвращает прежнюю фазу");
            actor.poseTime=0;ActorPose.Advance(actor,.02f);
            Require(PonyAnimationData.FrameIndex(actor.pose,actor.poseTime,false)==12,"Спуск с начальной фазы проходит через конец цикла");
            var copy=StateJson.Copy(actor);ActorPose.Advance(actor,.05f);ActorPose.Advance(copy,.05f);
            Require(actor.poseTime==copy.poseTime && actor.poseTime<0,"Отрицательная фаза сохраняется без повторного старта");
            for(int i=0;i<120;i++)
            {
                ActorPose.Advance(actor,1f/60);
                int frame=PonyAnimationData.FrameIndex(actor.pose,actor.poseTime,false);
                Require(frame>=1 && frame<=12,"Неподвижный хват не входит в цикл спуска");
            }
            actor.vy=0;ActorPose.Advance(actor,.1f);
            Require(actor.poseTime==0 && PonyAnimationData.FrameIndex(actor.pose,actor.poseTime,false,ActorPose.StationaryClimb(actor))==0,
                "Остановка выбирает отдельный неподвижный хват");
            var atlas=new AtlasActorClip {count=13,framesPerSecond=30,loop=true};
            Require(atlas.FrameIndex(-.02f)==12 && atlas.FrameIndex(-.02f,true)==0,"Атлас поддерживает обратную фазу и покой");
            Require(PonyAnimationData.FrameIndex("crouchLower",-.1f,false)==0,"Незацикленный переход не оборачивается назад");
        }
        public static void RenderGrip()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            Anatomy.Load();WeaponCatalog.Load();PonyAnimationData.Load();Remains.Presentation.PonyVisual.Prepare();
            var camera=new GameObject("Камера хвата лестницы").AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=2;
            foreach(string level in new[]{"hub","factory:0"})
            {
                var layout=RoomLayout.Load(level);var room=Remains.Presentation.AuthoredRoomVisual.Create(level,layout);
                for(int index=0;index<layout.ladders.Length;index++)
                {
                    var ladder=layout.ladders[index];
                    var actor=new ActorState {id="grip-check",x=ladder.x+ladder.movementOffset,y=(ladder.bottom+ladder.top)/2,facing=-(int)ladder.facing};
                    new MovementController(layout).Step(actor,Vector2.up,false,1f/60);
                    var visual=new GameObject(actor.id).AddComponent<Remains.Presentation.PonyVisual>();visual.Bind(actor);visual.DrawPose(0);
                    camera.transform.position=new Vector3(actor.x,actor.y+.9f,-10);
                    string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../..",$".reference/tests/ladder-grip-{level.Replace(':','-')}-{index}.png"));
                    Remains.Runtime.NetworkFrameCapture.Save(camera,path,width:1920,height:1440);
                    UnityEngine.Object.DestroyImmediate(visual.gameObject);
                }
                UnityEngine.Object.DestroyImmediate(room);
            }
        }
        public static void Render()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            WeaponCatalog.Load();PonyAnimationData.Load();Remains.Presentation.PonyVisual.Prepare();
            for(int row=0;row<3;row++)
                for(int column=0;column<4;column++)
                {
                    var actor=new ActorState {id=$"ladder-{row}-{column}",climbing=true,pose="climb",x=column*2.2f,y=-row*2.8f,
                        vy=row==0?3.15f:row==1?-4.725f:0,poseTime=row==0?column*.08f:row==1?-column*.08f:0};
                    var visual=new GameObject(actor.id).AddComponent<Remains.Presentation.PonyVisual>();
                    visual.Bind(actor);visual.DrawPose(actor.poseTime);
                }
            var camera=new GameObject("Камера лестничных поз").AddComponent<Camera>();camera.orthographic=true;
            camera.orthographicSize=4.7f;camera.transform.position=new Vector3(3.3f,-2,-10);camera.backgroundColor=new Color(.18f,.2f,.22f);
            string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../..", ".reference/tests/ladder-poses.png"));
            Remains.Runtime.NetworkFrameCapture.Save(camera,path);
            Debug.Log("Проверочный кадр лестницы: "+path);
        }
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Проверка лестницы: "+reason);}
    }
}
