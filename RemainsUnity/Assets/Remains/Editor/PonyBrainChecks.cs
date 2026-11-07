using System;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    // PonyBrain, the sapient ponies' utility brain, on a flat floor: calm without a target, the reaction delay before a
    // seen target is acted on, a shooter shooting in range and stepping back from a close one, teeth closing in and
    // touching, a levitated weapon swinging from its distance, a hurt pony retreating and, cornered, fighting again, a
    // lost target searched and forgotten, no flicker between intents while nothing changes, a downed ally revived with a
    // medkit (HealingRules for NPCs, the brain's intent, the owner's RevivalSeconds and its request) and its own wounds
    // healed with one (after a retreat, out of sight; one request at a time), and grenades: aimed up to 6 s while safe,
    // thrown at once in danger or on the run, not without one or at a close target; an ordinary raider's 0–3.
    public static class PonyBrainChecks
    {
        // A sapient body on the line stops the shot; off it, past the target, down or not sapient it does not.
        private static void LineOfFire()
        {
            var shooter=Raider();shooter.id="shooter";shooter.level="l";shooter.x=0;shooter.y=0;
            var target=new ActorState {id="target",player=true,connected=true,level="l",x=8,y=0,health=100};
            var ally=Raider();ally.id="ally";ally.level="l";ally.x=4;ally.y=0;
            var actors=new System.Collections.Generic.List<ActorState> {shooter,ally,target};
            Vector2 from=new(0,.55f),to=new(8,.55f);
            Require(NpcLineOfFire.Blocked(actors,shooter,target,from,to),"союзник на линии огня закрывает выстрел");
            ally.y=2;Require(!NpcLineOfFire.Blocked(actors,shooter,target,from,to),"союзник в стороне не мешает");ally.y=0;
            ally.x=10;Require(!NpcLineOfFire.Blocked(actors,shooter,target,from,to),"союзник за целью не мешает");ally.x=4;
            ally.down=true;Require(!NpcLineOfFire.Blocked(actors,shooter,target,from,to),"лежащий союзник не мешает");ally.down=false;
            ally.level="другой";Require(!NpcLineOfFire.Blocked(actors,shooter,target,from,to),"на другом уровне не мешает");
        }
        public static void Run()
        {
            Anatomy.Load();WeaponCatalog.Load();
            var shooter=ActorTypes.Get("raider").raider;
            Require(shooter.pony!=null && Mathf.Approximately(shooter.pony.retreatHealth,.25f) && SapientPonies.Is(Raider()) &&
                !SapientPonies.Is(new ActorState {id="t",actorType="tarakan"}),"у рейдера — характер разумного пони по умолчанию; таракан не разумный пони");
            Calm(shooter);Notice(shooter);Shooting(shooter);Melee(shooter);Retreat(shooter);Search(shooter);Revival(shooter);SelfHeal(shooter);Grenades(shooter);
            LineOfFire();ShootingFromLadder();FiringStance();
            Debug.Log("Мозг разумных пони: PASS — спокойствие, задержка реакции, стрельба с отскоком, ближний бой в зубах и телекинезом, отступление и бой в углу, поиск, без дёрганья, подъём союзника и самолечение аптечкой, гранаты с прицеливанием по безопасности.");
        }

        private sealed class HoldPositionRandom : System.Random
        {
            public override double NextDouble()=>.99;
        }

        private static void FiringStance()
        {
            var room=Room();
            foreach(string typeName in new[]{"raider","raider5-p9mm","raider7-winchester"})
            foreach(int facing in new[]{-1,1})
            foreach(int rate in new[]{30,60,144})
            {
                var type=ActorTypes.Get(typeName);
                var front=Raider();front.id="front";front.level=room.id;front.actorType=front.anatomy=typeName;front.facing=facing;
                float rearHeight=typeName=="raider"?.6f:.24f;
                var rear=Raider(-3*facing);rear.id="rear";rear.level=room.id;rear.facing=facing;rear.y=rearHeight;
                var target=new ActorState {id="target",player=true,connected=true,x=6*facing,level=room.id};
                rear.target=target.id;
                NpcWeaponTestItems.Equip(front);NpcWeaponTestItems.Equip(rear);
                var actors=new[]{front,rear,target};
                var npc=new NpcRaiderController(MovementSettings.Load(),new HoldPositionRandom());
                Vector2 aim=target.BodyPosition+Vector2.up*.55f;
                var nominal=WeaponPose.PreviewAtlasAim(rear,aim);
                Require(Anatomy.Contact(front,nominal.Muzzle,aim,0,out _)!=null &&
                    !Anatomy.ProneContact(front,nominal.Muzzle,aim).HasValue,
                    $"Проверочный выстрел должен проходить только над лежащим: {typeName}, {facing}, muzzle={nominal.Muzzle}");
                for(int i=0;i<rate*4;i++) npc.Step(front,type,target,true,room,1f/rate,actors:actors);
                Require(ActorPose.IsTacticallyProne(front) && !front.down && front.health==50 &&
                    ActorTerrainMotion.Supported(room,Anatomy.Movement(front),front.Position),$"Передний стрелок не освободил линию: {typeName}, {facing}, {rate}");
                Require(!NpcLineOfFire.Blocked(actors,rear,target,WeaponPose.RangedAnchor(rear),aim,ranged:true),
                    "Мозг заднего стрелка продолжает считать открытую линию занятой");
                var replica=StateJson.Copy(front);
                Require(ActorPose.IsTacticallyProne(replica) && !Anatomy.ProneContact(replica,nominal.Muzzle,aim).HasValue,
                    "Поза или область попадания лежащего теряется в снимке");

                var clear=new AttackEvent {attacker=rear.id,level=room.id,weapon=rear.weapon,
                    originX=nominal.Muzzle.x,originY=nominal.Muzzle.y,endX=aim.x,endY=aim.y};
                var low=new AttackEvent {attacker=rear.id,level=room.id,weapon=rear.weapon,originX=rear.x,originY=.4f,endX=target.x,endY=.4f};
                Require(NpcLineOfFire.SafeVolley(actors,new[]{clear},room,true) &&
                    !NpcLineOfFire.SafeVolley(actors,new[]{clear,low},room,true),"Дробина в лежащего союзника должна остановить весь залп");

                int rounds=WeaponAmmunition.Magazine(rear,rear.weapon).rounds;
                Require(NpcAttackRules.TryBegin(rear,target),"Подготовка проверочного залпа");
                rear.attackRemaining=0;
                Require(NpcAttackRules.Complete(rear,target,room,null,
                    shot=>NpcLineOfFire.SafeVolley(actors,new[]{shot,low},room,true))==null &&
                    WeaponAmmunition.Magazine(rear,rear.weapon).rounds==rounds,"Отмена залпа потратила боеприпас");
                rear.attackCooldown=0;
                Require(NpcAttackRules.TryBegin(rear,target),"Подготовка открытого выстрела");
                rear.attackRemaining=0;
                Require(NpcAttackRules.Complete(rear,target,room,null,
                    shot=>NpcLineOfFire.SafeVolley(actors,new[]{shot},room,true))!=null &&
                    WeaponAmmunition.Magazine(rear,rear.weapon).rounds==rounds-1,"Открытый выстрел поверх союзника не прошёл");

                rear.y=-.2f;
                for(int i=0;i<rate/2;i++) npc.Step(front,type,target,true,room,1f/rate,actors:actors);
                Require(!ActorPose.IsTacticallyProne(front),"Поза не освобождает слишком низкую траекторию");
                rear.y=2;
                for(int i=0;i<rate/2;i++) npc.Step(front,type,target,true,room,1f/rate,actors:actors);
                Require(!ActorPose.IsTacticallyProne(front),"Союзник позади со свободной линией заставил лечь");
                rear.y=rearHeight;rear.target="other";
                for(int i=0;i<rate/2;i++) npc.Step(front,type,target,true,room,1f/rate,actors:actors);
                Require(!ActorPose.IsTacticallyProne(front),"Стрелок с другой целью заставил лечь");
            }
            Debug.Log("Тактика стрелков: PASS — три атласа, оба направления, 30/60/144 Гц, разрешённый выстрел, остановка залпа и сохранение патрона.");
        }

        private static void ShootingFromLadder()
        {
            WeaponRigs.Load();
            var type=ActorTypes.Get("raider");
            var room=new RoomLayout {id="raider-climb-check",left=-8,right=8,bottom=-3,top=8,
                platforms=new[]{new RoomSurface {x=-8,y=-.4f,width=16,height=.4f},
                    new RoomSurface {x=-5,y=3.4f,width=10,height=.2f,oneWay=true}},
                ladders=new[]{new RoomLadder {x=0,bottom=0,top=3.6f}}};
            RoomSurfaces.Prepare(room);
            foreach(int rate in new[]{30,60,144})
            {
                var target=new ActorState {id="target",player=true,connected=true,x=3.2f,y=3.6f,level=room.id};
                var actor=Raider();
                actor.level=room.id;actor.y=1.2f;actor.climbing=true;actor.pose="climb";
                var npc=new NpcRaiderController(MovementSettings.Load(),new HoldPositionRandom());
                bool choseShootOnLadder=false;
                for(int tick=0;tick<rate*4;tick++)
                {
                    float y=actor.y;
                    npc.Step(actor,type,target,true,room,1f/rate);
                    if(npc.Intent(actor.id)!=PonyIntentKind.Shoot || !actor.climbing) continue;
                    choseShootOnLadder=true;
                    Require(actor.y>y,"Стрелок остановил подъём в ожидании выстрела, Гц "+rate);
                }
                Require(choseShootOnLadder && !actor.climbing && Mathf.Abs(actor.y-3.6f)<.001f &&
                    ActorTerrainMotion.Supported(room,Anatomy.Movement(actor),actor.Position),
                    "Стрелок не завершил подъём до опоры, Гц "+rate);
            }
            Debug.Log("Подъём стрелка: PASS — намерение стрелять не останавливает подъём при 30/60/144 Гц.");
        }

        private static RoomLayout Room(float left=-20,float right=20)
        {
            var room=new RoomLayout {id="pony-brain-check",left=left,right=right,bottom=-1,top=20,ladders=Array.Empty<RoomLadder>(),
                platforms=new[]{new RoomSurface {x=left,y=-1,width=right-left,height=1},new RoomSurface {x=left-1,y=-1,width=1,height=21},new RoomSurface {x=right,y=-1,width=1,height=21}}};
            RoomSurfaces.Prepare(room);
            return room;
        }
        private static ActorState Raider(float x=0)=>new() {id="r",actorType="raider",anatomy="raider",level="l",x=x,y=0,health=50,facing=1};
        private static RaiderSenses Sees(Vector2 point)=>new(true,point,true,point);
        private static readonly RaiderSenses Nothing=new(false,default,false,default);
        private sealed class Pony
        {
            public readonly PonyMind pony=new() {maxHealth=50};public readonly RaiderMind mind=new();
            public readonly ActorState actor;public readonly RoomLayout room;public readonly RaiderSettings s;public readonly System.Random random;
            public Pony(RaiderSettings s,ActorState actor,RoomLayout room,int seed) {this.s=s;this.actor=actor;this.room=room;random=new System.Random(seed);}
            public RaiderIntent Tick(RaiderSenses senses,RaiderAttacker? attacker=null,bool wall=false,PonyAlly ally=default)=>
                PonyBrain.Tick(pony,mind,s,actor,room,senses,wall,random,attacker??s.attacker,ally);
            // Seen long enough to be alarmed (the reaction delay over).
            public void Alarm(Vector2 target) {for(int i=0;i<200 && (pony.reaction>0 || mind.alert==0);i++) Tick(Sees(target));}
        }

        private static void Calm(RaiderSettings s)
        {
            var run=new Pony(s,Raider(),Room(),1);int moving=0;
            for(int i=0;i<300;i++) {if(run.Tick(Nothing).move!=0) moving++;Require(run.pony.intent==PonyIntentKind.Calm || i<PonyBrain.DecisionFrames,"без цели — спокоен");}
            Require(moving>0 && moving<300,$"спокойный то гуляет, то стоит: шагов {moving} из 300");
        }

        private static void Notice(RaiderSettings s)
        {
            var run=new Pony(s,Raider(),Room(),2);var target=new Vector2(8,.7f);int delay=0;bool shot=false;
            for(int i=0;i<12;i++) run.Tick(Sees(target));
            while(run.pony.reaction>0 && delay<200) {Require(run.pony.intent==PonyIntentKind.Notice || delay<PonyBrain.DecisionFrames,"замечая цель, стоит");shot|=run.Tick(Sees(target)).shoot;delay++;}
            Require(delay>=s.slowness/2 && !shot,$"увиденная цель сначала замечается с задержкой tupizna и без выстрелов: {delay} кадров");
        }

        private static void Shooting(RaiderSettings s)
        {
            var run=new Pony(s,Raider(),Room(),3);var target=new Vector2(8,.7f);run.Alarm(target);
            int shots=0,switches=0;var last=run.pony.intent;
            for(int i=0;i<600;i++)
            {
                run.actor.x=0;run.actor.vx=0;
                if(run.Tick(Sees(target)).shoot) shots++;
                if(run.pony.intent!=last) {switches++;last=run.pony.intent;}
            }
            Require(run.pony.intent==PonyIntentKind.Shoot && shots>20 && switches<=1,$"стрелок в пределах дальности стреляет и не дёргается: выстрелов {shots}, смен {switches}");
            // A target 1.5 m away: the shooter steps back (state 4) now and then.
            var close=new Vector2(1.5f,.7f);bool back=false;
            for(int i=0;i<120;i++) {run.actor.x=0;var intent=run.Tick(Sees(close));back|=intent.pushX<0;}
            Require(back,"от близкой цели стрелок отскакивает");
        }

        private static void Melee(RaiderSettings source)
        {
            var teeth=StateJson.Copy(source);teeth.attacker=RaiderAttacker.Teeth;
            var run=new Pony(teeth,Raider(),Room(),4);var target=new Vector2(3,.7f);run.Alarm(target);
            int touches=0;bool charge=true;
            for(int i=0;i<60;i++) {run.actor.x=0;var intent=run.Tick(Sees(target));if(intent.bodyHit>0) touches++;charge&=run.pony.intent!=PonyIntentKind.Melee || intent.charge;}
            Require(run.pony.intent==PonyIntentKind.Melee && touches>0 && charge,$"оружие в зубах — ближний бой бегом и удар: ударов {touches}");

            var levitated=StateJson.Copy(source);levitated.attacker=RaiderAttacker.Levitated;levitated.swingHeight=2.4f;
            run=new Pony(levitated,Raider(),Room(),5);target=new Vector2(1.5f,.7f);run.Alarm(target);
            int swings=0,steps=0;
            for(int i=0;i<300;i++) {run.actor.x=0;var intent=run.Tick(Sees(target));if(intent.swing) swings++;if(intent.move!=0) steps++;}
            Require(run.pony.intent==PonyIntentKind.Melee && swings>45 && steps==0,$"оружие телекинезом — машет с 50–100 px, стоя: взмахов {swings}, шагов {steps}");
        }

        private static void Retreat(RaiderSettings s)
        {
            var run=new Pony(s,Raider(),Room(),6);var target=new Vector2(6,.7f);run.Alarm(target);
            run.actor.health=5;int away=0;
            for(int i=0;i<30;i++) {run.actor.x=0;if(run.Tick(Sees(target)).move<0) away++;}
            Require(run.pony.intent==PonyIntentKind.Retreat && away>0,$"раненый (10%) отступает от цели: шагов прочь {away}");
            // Against a wall it is cornered: it stops and after a while fights again.
            bool fights=false;
            for(int i=0;i<200 && !fights;i++) {run.actor.x=0;run.Tick(Sees(target),wall:true);fights=run.pony.intent!=PonyIntentKind.Retreat;}
            Require(fights,"загнанный в угол снова дерётся");
        }

        private static void Search(RaiderSettings s)
        {
            var run=new Pony(s,Raider(),Room(),7);var target=new Vector2(8,.7f);run.Alarm(target);
            for(int i=0;i<30;i++) run.Tick(Nothing);
            Require(run.pony.intent==PonyIntentKind.Search,$"потеряв цель, ищет: {run.pony.intent}");
            int frames=0;
            while(run.pony.intent!=PonyIntentKind.Calm && frames<20000) {run.actor.x=0;run.Tick(Nothing);frames++;}
            Require(run.pony.intent==PonyIntentKind.Calm,"поиск затухает, рейдер успокаивается");
        }

        private static void Revival(RaiderSettings s)
        {
            ActorState Medic(float x) {var actor=Raider(x);actor.id="medic";InventoryStacks.Add(actor,SharedInventory.Create("heal",1));return actor;}
            var downed=Raider(.8f);downed.id="downed";downed.down=true;downed.health=0;
            var player=new ActorState {id="p",player=true,connected=true,level="l",x=.5f,y=0,health=0,down=true,actorType="pony",anatomy="pony"};
            var medic=Medic(0);
            Require(HealingRules.Rejection(medic,downed,false)==null && HealingRules.Rejection(medic,player,false)!=null &&
                HealingRules.Rejection(player,downed,false)!=null && HealingRules.Rejection(Raider(),downed,false)!=null &&
                HealingRules.Rejection(medic,medic,true)!=null,"NPC поднимает лежащего союзника с аптечкой; не игрока, не без аптечки, себя не лечит");
            Require(HealingRules.Apply(medic,downed,false) && !downed.down && Mathf.Approximately(downed.health,HealingRules.Revived) && InventoryJournal.Count(medic,"heal")==0,
                "подъём NPC расходует аптечку и поднимает с 35");

            // The brain: an ally 6 m away is walked to, then worked on; without a medkit it is left.
            var run=new Pony(s,Raider(),Room(),8);var ally=new PonyAlly(true,new Vector2(6,0),true);
            for(int i=0;i<30;i++) run.Tick(Nothing,ally:ally);
            Require(run.pony.intent==PonyIntentKind.Revive,$"увидев лежащего союзника, идёт поднимать: {run.pony.intent}");
            bool working=false;
            for(int i=0;i<30;i++) {run.actor.x=5.5f;working|=run.Tick(Nothing,ally:ally).revive;}
            Require(working,"над союзником — поднимает");
            var empty=new Pony(s,Raider(),Room(),9);
            for(int i=0;i<30;i++) empty.Tick(Nothing,ally:new PonyAlly(true,new Vector2(6,0),false));
            Require(empty.pony.intent!=PonyIntentKind.Revive,"без аптечки не поднимает");

            // The owner: RevivalSeconds over the ally, then one request with that ally.
            var type=ActorTypes.Get("raider");var room=Room();
            medic=Medic(0);medic.owner="o";downed=Raider(.8f);downed.id="downed";downed.down=true;downed.health=0;
            var raiders=new Remains.Gameplay.NpcRaiderController(MovementSettings.Load(),new System.Random(10));
            var target=new ActorState {id="t",player=true,connected=true,level="l",x=15,y=0,health=100,actorType="pony",anatomy="pony"};
            ActorState asked=null;float time=0,dt=1f/30;
            for(int i=0;i<150 && asked==null;i++) {medic.x=0;medic.vx=0;asked=raiders.Step(medic,type,target,false,room,dt,downed).revive;raiders.EndFrame(dt);time+=dt;}
            Require(asked==downed && time>=HealingRules.RevivalSeconds,$"владелец просит подъём после {HealingRules.RevivalSeconds} с над союзником: {time:0.##} с");
        }

        private static void SelfHeal(RaiderSettings s)
        {
            ActorState Hurt(float health=15,bool medkit=true)
            {
                var actor=Raider();actor.factoryEnemy=new FactoryEnemyScaleState {maxHealth=50};actor.health=health;
                if(medkit) InventoryStacks.Add(actor,SharedInventory.Create("heal",1));
                return actor;
            }
            var hurt=Hurt();var unknown=Raider();unknown.health=15;InventoryStacks.Add(unknown,SharedInventory.Create("heal",1));
            var full=Hurt(50);var downed=Hurt();downed.down=true;
            Require(HealingRules.Rejection(hurt,hurt,true)==null && HealingRules.Rejection(Hurt(medkit:false),Hurt(medkit:false),true)!=null &&
                HealingRules.Rejection(unknown,unknown,true)!=null && HealingRules.Rejection(full,full,true)!=null && HealingRules.Rejection(downed,downed,true)!=null,
                "NPC лечит себя с аптечкой; не без аптечки, не без известного предела здоровья, не при полном и не лёжа");
            Require(HealingRules.Apply(hurt,hurt,true) && Mathf.Approximately(hurt.health,50) && InventoryJournal.Count(hurt,"heal")==0,
                "самолечение NPC расходует аптечку и лечит на 35 до предела уровня");

            // The brain: hurt and alone it heals; healthy or without a medkit it does not; after a retreat out of sight it heals.
            var run=new Pony(s,Hurt(),Room(),11);bool used=false;
            for(int i=0;i<30;i++) used|=run.Tick(Nothing).heal;
            Require(run.pony.intent==PonyIntentKind.Heal && used,$"раненый без врага в виду лечится: {run.pony.intent}");
            foreach(var calm in new[]{Hurt(40),Hurt(medkit:false)})
            {
                var other=new Pony(s,calm,Room(),12);
                for(int i=0;i<30;i++) other.Tick(Nothing);
                Require(other.pony.intent!=PonyIntentKind.Heal,"здоровый или без аптечки не лечится");
            }
            run=new Pony(s,Hurt(5),Room(),13);var target=new Vector2(6,.7f);run.Alarm(target);
            for(int i=0;i<30;i++) {run.actor.x=0;run.Tick(Sees(target));}
            Require(run.pony.intent==PonyIntentKind.Retreat,$"в виду врага тяжело раненый отступает, а не лечится: {run.pony.intent}");
            for(int i=0;i<30;i++) {run.actor.x=0;run.Tick(Nothing);}
            Require(run.pony.intent==PonyIntentKind.Heal,$"оторвавшись, лечится: {run.pony.intent}");

            // The owner: one request, then none while the host answers (5 s).
            var raiders=new Remains.Gameplay.NpcRaiderController(MovementSettings.Load(),new System.Random(14));
            var type=ActorTypes.Get("raider");var room=Room();var patient=Hurt();int requests=0;
            for(int i=0;i<140;i++) {patient.x=0;patient.vx=0;if(raiders.Step(patient,type,null,false,room,1f/30).heal) requests++;raiders.EndFrame(1f/30);}
            Require(requests==1,$"владелец просит самолечение один раз, пока ждёт ответа хоста: {requests}");
        }

        private static void Grenades(RaiderSettings s)
        {
            ActorState Grenadier(int count=1) {var actor=Raider();if(count>0) InventoryStacks.Add(actor,SharedInventory.Create("grenade",count));return actor;}
            // Frames from the alarm till the throw, and the throw's error.
            (int frames,float error) Throw(Pony run,Vector2 target,Action<Pony> each=null)
            {
                run.Alarm(target);
                for(int i=0;i<400;i++)
                {
                    run.actor.x=0;run.actor.vx=0;each?.Invoke(run);
                    var intent=run.Tick(Sees(target));
                    if(intent.grenade) return (i,intent.grenadeError);
                }
                return (-1,0);
            }
            float full=s.pony.throwError;
            // Safe, 10 m away: aims the whole 6 s, the error halved six times.
            var calm=new Pony(s,Grenadier(),Room(),20);var far=Throw(calm,new Vector2(10,.7f));
            Require(far.frames>=PonyBrain.MaxAimFrames-10 && far.error<full/32,$"в безопасности целится до 6 с: {far.frames} кадров, разброс {far.error:0.00}°");
            Require(calm.pony.grenadeRest>0,"после броска — пауза до следующего");
            // An enemy at 8.5 m is a danger of 0.375: it lets go after about a second and a half.
            var near=Throw(new Pony(s,Grenadier(),Room(),21),new Vector2(8.5f,.7f));
            Require(near.frames>10 && near.frames<70 && near.error>full/8 && near.error<full/1.5f,$"враг в 8,5 м — бросает примерно через полторы секунды: {near.frames} кадров, {near.error:0.00}°");
            // A hit while aiming: thrown at once, hardly aimed.
            int frame=0;var hit=Throw(new Pony(s,Grenadier(),Room(),22),new Vector2(10,.7f),run=>{if(++frame==40) PonyBrain.Threat(run.pony,PonyBrain.Hit);});
            Require(hit.frames>=0 && hit.frames<50 && hit.error>full/4,$"ранен в прицеливании — бросает сразу: {hit.frames} кадров, {hit.error:0.00}°");
            // Without a grenade or at a target inside its own blast: no grenade intent.
            foreach(var (actor,target,what) in new[]{(Grenadier(0),new Vector2(10,.7f),"без гранаты"),(Grenadier(),new Vector2(3,.7f),"цель ближе своего взрыва")})
            {
                var run=new Pony(s,actor,Room(),23);run.Alarm(target);bool grenade=false;
                for(int i=0;i<300;i++) {run.actor.x=0;grenade|=run.pony.intent==PonyIntentKind.Grenade || run.Tick(Sees(target)).grenade;}
                Require(!grenade,what+" — не бросает");
            }
            // Retreating, it throws without aiming.
            var flee=new Pony(s,Grenadier(),Room(),24);flee.actor.health=5;var spot=new Vector2(8,.7f);flee.Alarm(spot);float snap=-1;
            for(int i=0;i<400 && snap<0;i++) {flee.actor.x=0;var intent=flee.Tick(Sees(spot));if(intent.grenade) snap=intent.grenadeError;Require(flee.pony.intent!=PonyIntentKind.Grenade,"раненый не стоит целиться");}
            Require(Mathf.Approximately(snap,full),$"убегая, бросает без прицеливания: {snap:0.00}°");

            // The owner: a shot past it and a hit frighten it.
            var raiders=new Remains.Gameplay.NpcRaiderController(MovementSettings.Load(),new System.Random(25));
            var type=ActorTypes.Get("raider");var room=Room();var raider=Grenadier();raider.id="scared";
            raiders.Step(raider,type,null,false,room,1f/30);var mind=raiders.Danger(raider.id);
            raiders.Heard(new AttackEvent {id="shot",attacker="p",level="l",weapon="pistol",originX=-5,originY=raider.BodyPosition.y+1,endX=5,endY=raider.BodyPosition.y+1});
            Require(raiders.Danger(raider.id)>=PonyBrain.NearShot-.01f && mind<.01f,"выстрел рядом пугает");
            raider.damageSequence++;raiders.Step(raider,type,null,false,room,1f/30);
            Require(raiders.Danger(raider.id)>=PonyBrain.Hit-.01f,"попадание пугает");

            // An ordinary raider's grenades: 20% at least one, 5% two, 1% three, never more.
            var table=new ContainerLootTable {id="grenades",version="1",entries=new[]{
                new ContainerLootEntry {id="grenade1",definition="grenade",minimum=1,maximum=1,probability=.2f},
                new ContainerLootEntry {id="grenade2",definition="grenade",minimum=1,maximum=1,probability=.25f,afterSuccess="grenade1"},
                new ContainerLootEntry {id="grenade3",definition="grenade",minimum=1,maximum=1,probability=.2f,afterSuccess="grenade2"}}};
            var counts=new int[5];
            for(int i=0;i<20000;i++) {int n=0;foreach(var item in table.Generate("w","c"+i)) n+=item.count;counts[Mathf.Min(n,4)]++;}
            float Share(int from) {int sum=0;for(int i=from;i<5;i++) sum+=counts[i];return sum/20000f;}
            Require(Mathf.Abs(Share(1)-.2f)<.015f && Mathf.Abs(Share(2)-.05f)<.008f && Mathf.Abs(Share(3)-.01f)<.004f && counts[4]==0,
                $"гранаты рейдера: ≥1 {Share(1):P1}, ≥2 {Share(2):P1}, 3 {Share(3):P1}, больше трёх {counts[4]}");
        }

        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Мозг разумных пони: "+message);}
    }
}
