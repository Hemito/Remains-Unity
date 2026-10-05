using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    public static class PropContactAudioChecks
    {
        public static void Run()
        {
            RunMotion();
            PropCatalog.Load();
            var settings=JsonUtility.FromJson<PropContactAudio.Settings>(Resources.Load<TextAsset>("Presentation/prop-audio").text);
            foreach(var definition in PropCatalog.All.Where(d=>d.hasPhysicalBody && d.movable))
            {
                var entry=settings.entries.SingleOrDefault(e=>e.definition==definition.id);
                Require(entry!=null && Resources.Load<AudioClip>(entry.clip)?.samples>0,"Нет банка зарегистрированного объекта: "+definition.id);
            }
            foreach(var expected in new[]{("woodbox","crate-wood-fall"),("case","crate-metal-small-fall"),
                ("chest","crate-metal-fall"),("ammobox","crate-metal-small-fall"),("bigbox","crate-metal-fall"),
                ("mcrate4","crate-metal-safe-fall"),("explbox","crate-metal-small-fall"),
                ("weapbox","crate-metal-fall"),("instr1","crate-wood-fall")})
                Require(settings.entries.Single(e=>e.definition==expected.Item1).clip=="Original/Audio/"+expected.Item2,
                    "Банк заводского объекта расходится с подтверждённым назначением: "+expected.Item1);
            Debug.Log("Звуки физических объектов: PASS — реальные банки, один контакт с полом/ящиком, удержание, пауза, смена владельца, снятие опоры и сохранность состояния при 30/60/144 FPS.");
        }
        // Can run without loading audio assets: exercises production motion and observation classes.
        public static void RunMotion()
        {
            var room=new RoomLayout {left=-10,right=10,bottom=0,top=10,ladders=Array.Empty<RoomLadder>(),
                platforms=new[]{new RoomSurface {x=-10,y=-1,width=20,height=1}}};
            foreach(int fps in new[]{30,60,144})
            foreach(bool container in new[]{false,true})
            foreach(bool stack in new[]{false,true})
            {
                var support=new PropState {id="support",level="hub",y=.3f,width=1,height=.6f,anchorHeight=.3f};
                PropState falling=container?new ContainerState():new PropState();
                falling.id="falling";falling.level="hub";falling.x=0;falling.y=2;falling.width=falling.height=.6f;
                falling.anchorHeight=container?0:.3f;
                var neighbours=stack?new[]{support,falling}:new[]{falling};
                var track=new PropContactObservation();
                Require(track.Sample(falling,room,neighbours,false,3)==MotionContact.None,"Загрузка создала удар.");
                int landings=Advance(falling,room,neighbours,track,fps);
                Require(landings==1,$"Падение не дало ровно один контакт: fps={fps}, container={container}, stack={stack}, count={landings}.");
                Require(Mathf.Abs(falling.y-falling.anchorHeight-(stack?.6f:0))<.001f,
                    $"Наблюдение расходится с физической опорой: fps={fps}, container={container}, stack={stack}, y={falling.y}, anchor={falling.anchorHeight}.");
                var position=falling.Position;float vx=falling.vx,vy=falling.vy;
                Require(PropSupport.Supported(falling,falling.Position-Vector2.up*falling.anchorHeight,neighbours)==stack,"Опора на ящик распознана неверно.");
                Require(falling.Position==position && falling.vx==vx && falling.vy==vy,"Запрос опоры изменил физическое состояние.");
                if(stack)
                {
                    support.x=4;
                    Require(Advance(falling,room,neighbours,track,fps)==1,"Снятие опоры не дало один новый контакт с полом.");
                    support.x=0;support.level="factory:0";
                    falling.y=.6f+falling.anchorHeight;
                    Require(!PropSupport.Supported(falling,falling.Position-Vector2.up*falling.anchorHeight,neighbours),"Опора взята из другой локации.");
                }
                falling.y=2;falling.vx=falling.vy=0;falling.held=true;falling.generation++;
                for(int i=0;i<fps;i++)
                {
                    PropMotion.Step(falling,room,1f/fps,new Vector2(0,.3f),neighbours);
                    Require(track.Sample(falling,room,neighbours,false,3)==MotionContact.None,"Контакт удерживаемого ящика озвучен.");
                }
                falling.y=2;falling.vx=falling.vy=0;falling.held=false;falling.generation++;
                Require(track.Sample(falling,room,neighbours,false,3)==MotionContact.None,"Освобождение создало удар до падения.");
                Require(Advance(falling,room,neighbours,track,fps)==1,"Падение после удержания не дало один контакт.");
                falling.y=2;falling.vx=falling.vy=0;
                Require(Advance(falling,room,neighbours,track,fps,true)==0,"Пауза озвучила падение.");
                Require(track.Sample(falling,room,neighbours,false,3)==MotionContact.None,"Выход из паузы создал отложенный удар.");
                falling.generation++;
                Require(track.Sample(falling,room,neighbours,false,3)==MotionContact.None,"Передача владения создала удар.");
            }
        }
        private static int Advance(PropState prop,RoomLayout room,PropState[] neighbours,PropContactObservation track,int fps,bool paused=false)
        {
            int landings=0;
            for(int i=0;i<fps*3;i++)
            {
                PropMotion.Step(prop,room,1f/fps,null,neighbours);
                if(track.Sample(prop,room,neighbours,paused,3)==MotionContact.Landing) landings++;
            }
            return landings;
        }
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Контакт физического объекта: "+reason);}
    }
}
