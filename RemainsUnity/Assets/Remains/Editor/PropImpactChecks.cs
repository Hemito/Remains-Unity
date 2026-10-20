using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class PropImpactChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        private sealed class NeverWeapon : IDamageRule
        {public float Calculate(string weapon,float speed)=>throw new InvalidOperationException("Box не должен использовать правило оружия.");}
        private sealed class NoProtection : IHitProtectionRule,IPhysicalHitProtectionRule
        {
            public float Resolve(ActorState actor,AttackEvent attack,float amount)=>throw new InvalidOperationException("Box не является AttackEvent.");
            public float ResolvePhysical(ActorState actor,float amount)=>amount;
        }
        public static void Run()
        {
            Anatomy.Load();ActorTypes.Load();WeaponCatalog.Load();PropCatalog.Load();PropImpactCatalog.Load();
            UnitsAndProfiles();AuthorityAndRecovery();SpatialAndScheduling();FullFactoryBudget();
            Debug.Log("PropImpactChecks: PASS — исходные единицы/пороги/две скорости, player-victim и Box-owner authority, source journal, epoch, cooldown, повтор/смена владельца/recovery, spatial pruning и неактивный уровень.");
        }
        private static void Require(bool value,string reason)
        {if(!value) throw new InvalidOperationException("Контакт Box: "+reason);}
        private static void Equal(float actual,float expected,string reason)=>Require(Mathf.Abs(actual-expected)<.0001f,$"{reason}: {actual} != {expected}");
        private static WorldState World()
        {
            var world=new WorldState {worldId="box-impact-check"};
            world.actors.Add(new ActorState {id="player",player=true,connected=true,owner="victim",level="l",health=1000});
            world.actors.Add(new ActorState {id="npc",owner="npc-owner",level="l",health=1000});
            world.props.Add(new PropState {id="box",definition="woodbox",owner="thrower",level="l",mass=50,width=1,height=1,anchorHeight=0,x=0,y=0,generation=3,vx=6});
            return world;
        }
        private static PropImpactSample Source(WorldState world,uint sequence=1)
        {
            var prop=world.props[0];var sample=new PropImpactSample {world=world.worldId,prop=prop.id,owner=prop.owner,level=prop.level,
                generation=prop.generation,sequence=sequence,x=prop.x,y=prop.y,vx=prop.vx,vy=prop.vy};
            sample.id=PropImpactRules.SampleId(world.worldId,prop.id,prop.generation,sequence);return sample;
        }
        private static void UnitsAndProfiles()
        {
            Require(!PropImpactRules.Dangerous(new Vector2(3,3)),"Обе компоненты ровно 5 px/frame не запускают attDrop");
            Require(!PropImpactRules.Dangerous(new Vector2(3.01f,0)),"Компонента >5 без v²>=50 даёт урон");
            Require(PropImpactRules.Dangerous(new Vector2(6,0)),"10 px/frame не распознаны как 6 м/с");
            Equal(PropImpactRules.Damage(50,new Vector2(6,0)),10,"massa=1, vel²=100, World.boxDamage=.2");
            Equal(PropImpactRules.Damage(15,new Vector2(6,0)),3,"15 кг = .3 исходной массы");
            foreach(var pair in new[]{("case",.5f),("chest",1f)})
            {
                var prop=PropCatalog.Get(pair.Item1);
                float originalMass=(prop.width*50)*(prop.height*50)*(prop.height*50)/250000*pair.Item2;
                Equal(prop.mass,originalMass*50,pair.Item1+": shipped kg не соответствуют Box.setXmlParam geometry/massaMult");
            }
            foreach(var creature in CreatureCatalog.Load().Creatures)
                Require(PropImpactCatalog.TryGet(creature.actorType,out var body) && body.mass>=0 && body.knocked>=0 && body.immunity==20d/30,
                    "Нет явного исходного профиля типа "+creature.actorType);
            Require(!PropImpactCatalog.TryGet("unknown:mod",out _),"Незарегистрированный модовый тип получил скрытый профиль");
            bool testedSlime=false;
            foreach(var creature in CreatureCatalog.Load().Creatures)
            {
                if(ActorTypes.Get(creature.actorType).behavior!=NpcCreatureBehaviours.Slime) continue;
                var slime=new ActorState {id="fixed-slime",actorType=creature.actorType,creature=new NpcCreatureState {inverted=true}};
                Require(PropImpactCatalog.ResolveBody(slime,out var hanging) && hanging.fixedBody,"Потолочная слизь потеряла исходный fixed branch");
                slime.creature.inverted=false;
                Require(PropImpactCatalog.ResolveBody(slime,out var crawling) && !crawling.fixedBody,"Слизь на полу осталась fixed после схода с потолка");
                testedSlime=true;break;
            }
            Require(testedSlime,"Не найден represented slime для fixed-state проверки");
            var loose=new ContainerState {id="loose",definition="loose-item",presentationItem="item:ammo10mm",mass=.1f};
            Require(!PropImpactRules.IsBox(loose),"Loose Loot получил Box damage");
        }
        private static void AuthorityAndRecovery()
        {
            var world=World();var box=world.props[0];var npc=world.Find("npc");var player=world.Find("player");
            var request=Source(world);string clean=StateJson.ToJson(world);
            Require(!PropImpactRules.AcceptSample(world,box,"wrong",request,10,out _) && StateJson.ToJson(world)==clean,"Неверный owner мутировал source journal");
            var malformed=StateJson.Copy(request);malformed.vx=float.NaN;
            Require(!PropImpactRules.AcceptSample(world,box,"thrower",malformed,10,out _) && StateJson.ToJson(world)==clean,"NaN sample мутировал мир");
            Require(PropImpactRules.AcceptSample(world,box,"thrower",request,10,out var source),"Источник владельца не принят");
            Require(source.mass==50 && source.width==1 && source.at==10 && PropImpactRules.Contact(source,npc),"Принятый источник не привязан к канонической форме/часам");
            Require(PropImpactRules.CanReport(source,player,"victim") && !PropImpactRules.CanReport(source,player,"thrower"),"Решение контакта игрока принадлежит бросившему");
            Require(PropImpactRules.CanReport(source,npc,"thrower") && !PropImpactRules.CanReport(source,npc,"npc-owner"),"Решение контакта NPC принадлежит жертве вместо Box owner");
            var aged=StateJson.Copy(world);aged.props[0].impacts.samples.Clear();
            Require(!PropImpactRules.AcceptSample(aged,aged.props[0],"thrower",request,100,out _),"Удаление expired samples разрешило replay прежней source sequence");
            aged.props[0].impacts.sampleSequence=100;
            Require(PropImpactRules.NextSampleSequence(aged.props[0],0,0)==101,"Возврат в активную зону/новый controller сбросил source sequence ниже persisted watermark");
            Require(PropImpactRules.NextSampleSequence(aged.props[0],aged.props[0].generation,110)==111,"Локальный непринятый максимум source sequence откатился к snapshot watermark");
            var inbox=new PropImpactSourceInbox();var arrivals=new List<PropImpactSample>();
            Require(inbox.Accept(source,true),"Принятый source не поставлен на однократную проверку");
            inbox.Drain(source.level,arrivals);Require(arrivals.Count==1,"Source не доставлен для geometry decision");
            npc.x=10;Require(!PropImpactRules.Contact(source,npc),"Fixture промаха пересекает source");
            npc.x=0;inbox.Drain(source.level,arrivals);
            Require(arrivals.Count==0 && !inbox.Accept(source,true),"Старый source снова проверяет geometry после входа цели в старую AABB");
            inbox.Accept(NextSample(source),true);inbox.DropInactive(_=>false);inbox.Drain(source.level,arrivals);
            Require(arrivals.Count==0,"Возврат на неактивный уровень сохранил старую очередь geometry decisions");
            var profile=new PropImpactBody(50,1,20d/30);
            var forged=StateJson.Copy(source);forged.mass=500;
            Require(PropImpactRules.Prepare(world,box,forged,npc,profile,Vector2.zero,10)==null,"Непринятый источник с подменённой массой разрешил урон");
            var contact=PropImpactRules.Prepare(world,box,source,npc,profile,Vector2.zero,10);
            Require(contact!=null,"Допустимый контакт не подготовлен");
            Equal(contact.targetVx,6,"Удар передал скорость цели");Equal(contact.propVx,2.25f,"Box отдаёт часть импульса");
            var fixedContact=PropImpactRules.Prepare(world,box,source,npc,new PropImpactBody(0,1,20d/30,true),Vector2.zero,10);
            Equal(fixedContact.propVx,3,"Fixed цель делит скорость Box пополам");Equal(fixedContact.targetVx,0,"Fixed цель сдвинулась");
            Require(PropImpactRules.Prepare(world,box,source,npc,new PropImpactBody(50,1,20d/30,false,true),Vector2.zero,10)==null,"noBox профиль не исключён");
            var replicaBefore=StateJson.Copy(box);
            PropImpactRules.Commit(world,box,npc,contact);
            Require(box.generation==4 && npc.boxImpactUntil==10+20d/30,"Epoch/cooldown не опубликованы вместе");
            Require(PropImpactRules.AdoptImpulse(replicaBefore,contact) && !PropImpactRules.AdoptImpulse(replicaBefore,contact),"Impulse не идемпотентен");
            var stale=StateJson.Copy(world.props[0]);stale.generation=3;stale.impacts=null;stale.vx=6;
            PropReplication.PreserveNewerPose(replicaBefore,stale,"thrower");
            Require(stale.generation==4 && stale.vx==contact.propVx,"Старый snapshot откатил подтверждённый impulse epoch");
            replicaBefore.generation=5;replicaBefore.held=true;replicaBefore.vx=99;
            Require(!PropImpactRules.AdoptImpulse(replicaBefore,contact) && replicaBefore.vx==99,"Поздний impulse перезаписал новый захват");
            Require(!PropReplication.Accept(box,3,999,0,0,99,0),"Старая motion sequence пересекла новую generation");
            var recovered=StateJson.Copy(world);var recoveredBox=recovered.props[0];var recoveredNpc=recovered.Find("npc");
            Require(recoveredBox.impacts.contacts.Count==1 && recoveredNpc.boxImpactUntil==npc.boxImpactUntil,"Save/restore потеряли контакт/cooldown");
            recoveredNpc.owner="new-owner";recoveredNpc.generation++;
            var receiver=new DamageReceiver(new NeverWeapon(),null,new NoProtection());
            int deaths=0;receiver.Applied+=_=>deaths++;
            Require(receiver.ApplyPhysical(recoveredNpc,recoveredBox.impacts.contacts[0]),"Handoff потерял принятую атаку");
            Equal(recoveredNpc.health,990,"Восстановленный контакт нанёс неверный урон");
            Require(!receiver.ApplyPhysical(recoveredNpc,contact) && deaths==1,"Retry повторил урон после receipt");
            var oldHealth=world.Find("npc").Copy();oldHealth.generation=recoveredNpc.generation+1;oldHealth.owner="third-owner";
            PropImpactRules.PreserveTarget(recoveredNpc,oldHealth);
            Require(!oldHealth.resolvedAttacks.Contains(contact.id) && receiver.ApplyPhysical(oldHealth,contact) && oldHealth.health==990,
                "Receipt-only merge оставил новое владение без урона");
            recoveredNpc.lifecycleSequence++;
            Require(!receiver.ApplyPhysical(recoveredNpc,contact),"Контакт прежней жизни повторно повредил поднятого персонажа");
            recoveredBox.vx=6;var next=Source(recovered,2);
            Require(PropImpactRules.AcceptSample(recovered,recoveredBox,"thrower",next,10.1,out var nextSource),"Новый source epoch не принят");
            recoveredNpc.lifecycleSequence=contact.lifecycle;
            Require(PropImpactRules.Prepare(recovered,recoveredBox,nextSource,recoveredNpc,profile,Vector2.zero,10.1)==null,"Контакт обошёл сохранённый cooldown");
            Require(PropImpactRules.Prepare(recovered,recoveredBox,nextSource,recoveredNpc,profile,Vector2.zero,10.8)!=null,"Cooldown не истёк по времени активного уровня");
        }
        private static PropImpactSample NextSample(PropImpactSample source)
        {var next=StateJson.Copy(source);next.sequence++;next.id=PropImpactRules.SampleId(next.world,next.prop,next.generation,next.sequence);return next;}
        private static void SpatialAndScheduling()
        {
            var index=new PropImpactSpatialIndex();var world=World();var box=world.props[0];
            for(int i=0;i<1000;i++)
            {
                box.id="box-"+i;box.x=i*8;var request=Source(world);
                request.prop=box.id;request.id=PropImpactRules.SampleId(world.worldId,box.id,box.generation,1);
                request.width=request.height=1;request.mass=50;request.at=0;index.Add(request);
            }
            var found=new List<PropImpactSample>();index.Query("l",new Rect(-.5f,0,1,1),found);
            Require(found.Count==1 && index.CandidateVisits<=2,"Broadphase перебрал весь завод вместо соседних buckets");
            Require(index.Remove(found[0].prop,found[0].id),"Просроченный источник не удалён из индекса");
            index.Query("l",new Rect(-.5f,0,1,1),found);
            Require(found.Count==0 && index.CandidateVisits==0,"Expired source оставил вечные bucket visits");
            var targets=new PropImpactTargetIndex();var actors=World();
            targets.Refresh(actors);
            Require(targets.IntersectsActiveZone("l",new Rect(39.9f,0,1,1)),"Box extent пересекает active zone, но source отсеян по центру");
            Require(!targets.IntersectsActiveZone("l",new Rect(40.1f,0,1,1)) && !targets.IntersectsActiveZone("l",new Rect(0,22.1f,1,1)),"Дальний source вне NpcActivity продолжает публикацию");
            Require(targets.IntersectsActiveZone("l",new Rect(-41,0,1,1)) && !targets.IntersectsActiveZone("inactive",new Rect(0,0,1,1)),"Граница/неактивный уровень source отбираются неправильно");
            for(int i=0;i<1000;i++) actors.actors.Add(new ActorState {id="far-"+i,level="l",x=1000+i*8});
            targets.Refresh(actors);actors.Find("npc").vx=1;
            targets.Refresh(actors,new[]{"actors:npc"});
            Require(targets.LastVisited==1,"Локальное событие NPC пересобрало весь actor index");
            actors.Find("player").x=4;targets.Refresh(actors,new[]{"actors:player"});
            Require(targets.LastVisited<20,"Движение игрока сканирует дальних актёров вместо buckets активной зоны");
            var sleeping=World();sleeping.actors[0].connected=false;
            var context=new FreePropMotionContext();var room=new RoomLayout {id="l",left=-100,right=100,bottom=-100,top=100,platforms=Array.Empty<RoomSurface>(),ladders=Array.Empty<RoomLadder>()};
            int stepped=FreePropMotion.Step(sleeping,"thrower",1f/30,_=>room,_=>false,null,null,context);
            Require(stepped==0,"Неактивный уровень симулирует Box");
            var wallWorld=World();var wallBox=wallWorld.props[0];wallBox.vx=30;wallBox.x=-.5f;
            var wall=new RoomLayout {id="l",left=-100,right=100,bottom=-100,top=100,
                platforms=new[]{new RoomSurface {x=0,y=-5,width=1,height=10}}};
            FreePropMotion.Step(wallWorld,"thrower",1f/30,_=>wall,_=>false,null,null,context);
            Require(wallBox.vx==0,"Физика не обнулила скорость у стены");
            var settings=MovementSettings.Load();var pushed=new ActorState {id="pushed",x=0,y=5,vx=6,vy=3};
            new MovementController(room,settings).Step(pushed,Vector2.zero,false,1f/144);
            Require(pushed.x>0 && pushed.y>5 && pushed.vx>0,"Следующий movement input уничтожил фактический impulse");
        }
        private static void FullFactoryBudget()
        {
            var world=new WorldState {worldId="prop-impact-budget-seed104",seed=104};
            FactoryPreparation.Create(world,Resources.Load<FactoryCatalogAsset>(FactoryCatalogAsset.ResourceKey),0);
            try
            {
                FactoryContentSources.Prepare(world,"factory:0");
                var authored=FactoryContentSources.Require(world,"factory:0");
                RoomContentInstaller.Apply(world,RoomContentBatch.Read(authored,"factory:0"));
                var root=authored.GetComponent<FactoryLevelRoot>();var room=root.Prepared.Physics;
                var player=new ActorState {id="budget-player",player=true,connected=true,owner="budget-owner",level="factory:0",x=room.spawnX,y=room.spawnY};
                world.actors.Add(player);
                Require(world.containers.Count>1000 && world.actors.Count>200,"Бюджет Box проверяется не на полном заводе seed104");
                var targets=new PropImpactTargetIndex();targets.Refresh(world);
                var spatial=new PropImpactSpatialIndex();var inbox=new PropImpactSourceInbox();
                var bodies=new List<ActorState>();var arrivals=new List<PropImpactSample>();
                var boxes=new List<PropState>();foreach(var prop in WorldProps.All(world)) if(PropImpactRules.IsBox(prop)) boxes.Add(prop);
                long checksum=0,visits=0;int accepted=0;
                void Phase(int tick)
                {
                    targets.Refresh(world,new[]{"actors:"+player.id});
                    accepted=0;
                    foreach(var box in boxes)
                    {
                        var area=new Rect(box.x-box.width*.5f,box.y-box.anchorHeight,box.width,box.height);
                        if(!targets.IntersectsActiveZone(box.level,area)) continue;
                        var sample=new PropImpactSample {world=world.worldId,prop=box.id,owner="budget-owner",level=box.level,
                            generation=box.generation,sequence=(uint)tick+1,x=box.x,y=box.y,vx=6,vy=0,width=box.width,height=box.height,anchorHeight=box.anchorHeight,mass=box.mass,at=tick/30d};
                        sample.id=PropImpactRules.SampleId(sample.world,sample.prop,sample.generation,sample.sequence);
                        spatial.Add(sample);inbox.Accept(sample,true);accepted++;
                    }
                    inbox.Drain("factory:0",arrivals);
                    foreach(var source in arrivals)
                    {
                        targets.Query(source.level,source.Bounds,bodies);visits+=targets.LastVisited;
                        foreach(var body in bodies) if(PropImpactRules.Contact(source,body)) checksum++;
                    }
                }
                for(int i=0;i<30;i++) Phase(i);
                var times=new double[300];visits=0;
                for(int i=0;i<times.Length;i++)
                {
                    long before=System.Diagnostics.Stopwatch.GetTimestamp();Phase(i+30);
                    times[i]=(System.Diagnostics.Stopwatch.GetTimestamp()-before)*1000d/System.Diagnostics.Stopwatch.Frequency;
                }
                Array.Sort(times);double median=times[times.Length/2],p99=times[(int)Math.Ceiling(times.Length*.99)-1];
                Debug.Log($"PropImpact budget seed104: containers={world.containers.Count}, actors={world.actors.Count}, physicalBoxes={boxes.Count}, activeSources={accepted}, candidateVisits/phase={visits/300d:F1}, checksum={checksum}; warmed phases=300; median={median:F3}ms, p99={p99:F3}ms; phase budget median<=1ms/p99<=2ms: {(median<=1 && p99<=2?"PASS":"FAIL")}; frame target=16.7ms. Это CPU source/target indexes и contact candidates, не полный physics/network/render кадр или EXE-приёмка.");
            }
            finally {FactoryContentSources.Clear();}
        }
    }
}
