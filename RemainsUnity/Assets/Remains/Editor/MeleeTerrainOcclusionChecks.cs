using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class MeleeTerrainOcclusionChecks
    {
        public static void Run()
        {
            int cases=RunDomain();
            CheckReceiver();
            Debug.Log($"Преграды ближнего удара: PASS — {cases} наборов, реальные поздние сегменты, 30/60/144 FPS, контакт двери, текущая локальная цель, открытие и пробивание.");
        }

        // Also callable in a managed harness with the real catalog/anatomy data loaded.
        public static int RunDomain()
        {
            int cases=0;
            foreach(float dt in new[]{1f/30,1f/60,1f/144,.3f,1f})
            foreach(int facing in new[]{1,-1})
            {
                var origin=new Vector2(.3f*facing,.8f);
                var target=Raider(2.2f*facing,facing);
                var door=Door(facing);
                var room=Room(door);
                var raw=Samples("spear",origin,facing,dt,null);
                var late=raw.FirstOrDefault(a=>!ShotObstacles.FirstContact(room,a.Origin,a.End,out _,a.radius).HasValue &&
                    Anatomy.Contact(target,a)!=null);
                Require(late!=null,"Исходный поздний сегмент больше не воспроизводит дефект; нужна актуализация fixture.");
                Require(AttackContacts.Capture(new[]{target},late,true,false).Count==1,
                    "Контрпример должен проходить прежнюю проверку tip→tip.");
                Require(Capture(room,new[]{target},late,false,true).Count==0,
                    "Опора позднего сегмента пропустила цель за закрытой office-door1.");

                var clipped=Samples("spear",origin,facing,dt,room);
                Require(clipped.Count==12 && clipped.All(a=>Capture(room,new[]{target},a,false,true).Count==0),
                    "Реальный ограниченный MeleeSweep прошёл закрытую дверь.");
                int surfaceContacts=clipped.Count(a=>ShotObstacles.FirstContact(room,a.Origin,a.End,out _,a.radius).HasValue);
                Require(surfaceContacts>0,"Остановка кончика подавила действительный контакт двери.");
                foreach(var attack in clipped)
                {
                    var contact=ShotObstacles.FirstContact(room,attack.Origin,attack.End,out _,attack.radius);
                    if(contact.HasValue && MeleeTerrainOcclusion.IsReachable(room,attack,contact.Value,true))
                        DoorDamageOperations.Apply(door,attack.id,WeaponCatalog.Get("spear").damage,out _);
                }
                Require(door.integrity.health==54 && door.Blocks && door.integrity.resolvedAttacks.Count==1,
                    "Один взмах должен оставить дверь закрытой с одним durable receipt.");

                door.open=true;room=Room(door);
                Require(raw.Any(a=>Capture(room,new[]{target},a,false,true).Count==1),"Открытая дверь сохранила запрет удара.");
                door.open=false;door.integrity.health=0;room=Room(door);
                Require(raw.Any(a=>Capture(room,new[]{target},a,false,true).Count==1),"Разрушенная дверь сохранила запрет удара.");
                room=new RoomLayout {platforms=Array.Empty<RoomSurface>()};
                Require(raw.Any(a=>Capture(room,new[]{target},a,false,true).Count==1),"Удалённая дверь сохранила скрытый запрет.");
                room=Room(Door(facing));
                Require(raw.Any(a=>Capture(room,new[]{target},a,false,false).Count==1),"stopAtTerrain=false потерял пробивание.");
                room.platforms[0].sourceDoorId=null;
                Require(raw.All(a=>Capture(room,new[]{target},a,false,true).Count==0),"Обычная solid поверхность пропустила поздний сегмент.");
                room.platforms[0].shotsPassThrough=true;
                Require(raw.Any(a=>Capture(room,new[]{target},a,false,true).Count==1),"shotsPassThrough не читается из общей геометрии.");
                cases++;
            }

            foreach(string weapon in new[]{"spear","crowbar"})
            foreach(float dt in new[]{1f/30,1f/60,1f/144,.3f,1f})
            foreach(int facing in new[]{1,-1})
            {
                var origin=new Vector2(-.2f*facing,.8f);
                var near=Raider(.25f*facing,facing);
                var door=Door(facing);door.width=.2f;door.height=3;
                if(facing<0) door.x=-1;
                var room=Room(door);
                var actual=Samples(weapon,origin,facing,dt,room);
                Require(actual.Any(a=>Capture(room,new[]{near},a,false,true).Count==1),
                    "Касание стены подавило цель на ближней стороне: "+weapon+", dt="+dt);
                foreach(var attack in actual)
                {
                    Require(MeleeTerrainOcclusion.Validate(attack,out _),"Sampler потерял опоры сегмента.");
                    Require(MeleeTerrainOcclusion.IsReachable(room,attack,1,true),"Разрешённый fixed sample нарисован за solid.");
                }
                cases++;
            }

            foreach(float offset in new[]{94f,150f})
            foreach(float dt in new[]{1f/30,1f/60,1f/144})
            foreach(int facing in new[]{1,-1})
            {
                var origin=new Vector2(offset+.3f*facing,.8f);
                var target=Raider(offset+2.2f*facing,facing);
                var door=Door(facing);door.x+=offset;var room=Room(door);
                var raw=Samples("spear",origin,facing,dt,null);
                Require(raw.Any(a=>Anatomy.Contact(target,a)!=null),"Перенос fixture потерял actual late contact.");
                Require(raw.All(a=>Capture(room,new[]{target},a,false,true).Count==0),"Закрытая дверь не работает при реальных координатах офиса/склада.");
                var actual=Samples("spear",origin,facing,dt,room);
                Require(actual.All(a=>MeleeTerrainOcclusion.IsReachable(room,a,1,true)),
                    $"Clipped sample стал occluded после переноса: x={offset}, facing={facing}, dt={dt}.");
                Require(actual.Any(a=>ShotObstacles.FirstContact(room,a.Origin,a.End,out _,a.radius).HasValue),
                    "Округление translated sampled tip потеряло действительный контакт двери.");
                door.open=true;room=Room(door);
                Require(raw.Any(a=>Capture(room,new[]{target},a,false,true).Count==1),"Открытая translated дверь продолжает блокировать удар.");
                door.open=false;door.width=.2f;door.height=3;door.x=offset+(facing>0?.8f:-1);
                room=Room(door);origin.x=offset-.2f*facing;var near=Raider(offset+.25f*facing,facing);
                foreach(string weapon in new[]{"spear","crowbar"})
                {
                    actual=Samples(weapon,origin,facing,dt,room);
                    Require(actual.Any(a=>Capture(room,new[]{near},a,false,true).Count==1),
                        $"Контакт ближней стороны потерян при x={offset}, facing={facing}, weapon={weapon}, dt={dt}.");
                }
                cases++;
            }

            var changingDoor=Door(1);var changingRoom=Room(changingDoor);
            var sameSwing=new MeleeSweep("spear",new Vector2(.3f,.8f),Vector2.right,null,changingRoom);
            var changingTarget=Raider(2.2f,1);int changingHits=0;
            void ObserveChange(MeleeSegment segment)
            {
                var attack=new AttackEvent {id="same-swing-opening",attacker="source",weapon="spear",level="hub",radius=MeleeSweep.ContactRadius,
                    originX=segment.start.x,originY=segment.start.y,endX=segment.end.x,endY=segment.end.y,meleeAnchor=segment.Anchor()};
                changingHits+=Capture(changingRoom,new[]{changingTarget},attack,false,true).Count;
            }
            sameSwing.Advance(new Vector2(.3f,.8f),sameSwing.Duration*.3f,ObserveChange,changingRoom);
            Require(changingHits==0,"Первый отрезок того же взмаха прошёл закрытую дверь.");
            changingDoor.open=true;changingRoom.platforms=Room(changingDoor).platforms;
            sameSwing.Advance(new Vector2(.3f,.8f),sameSwing.Duration*.7f,ObserveChange,changingRoom);
            Require(changingHits>0,"Открытие двери оставило скрытый запрет до конца того же взмаха.");

            // Moving support belongs to the same active time window, including a partial key interval.
            var definition=WeaponCatalog.Get("spear");
            float savedStart=definition.meleeContactStart,savedEnd=definition.meleeContactEnd;
            try
            {
                definition.meleeContactStart=.18f;definition.meleeContactEnd=.49f;
                foreach(float dt in new[]{1f/30,1f/60,1f/144,.3f,1f})
                {
                    var sweep=new MeleeSweep("spear",Vector2.zero,Vector2.right);
                    var samples=new List<MeleeSegment>();float time=0;
                    while(!sweep.Finished)
                    {time+=dt;sweep.Advance(Vector2.right*3*time,dt,samples.Add);}
                    Require(Mathf.Abs(samples[0].anchorStart.x-3*sweep.Duration*.18f)<.00001f &&
                        Mathf.Abs(samples.Last().anchorEnd.x-3*sweep.Duration*.49f)<.00001f,
                        "Опоры activeStart/activeEnd взяты из кадрового состояния вместо времени fixed sample.");
                    cases++;
                }
            }
            finally {definition.meleeContactStart=savedStart;definition.meleeContactEnd=savedEnd;}

            var first=Samples("spear",new Vector2(.3f,.8f),1,1,null).Last();
            var copy=StateJson.Copy(first);
            Require(copy.meleeAnchor.Start==first.meleeAnchor.Start && copy.meleeAnchor.End==first.meleeAnchor.End &&
                MeleeTerrainOcclusion.Validate(copy,out _),"Сериализация потеряла опоры actual segment.");
            copy.meleeAnchor=null;Require(!MeleeTerrainOcclusion.Validate(copy,out var missing) && missing.Contains(first.id),"Нет адресной ошибки missing anchor.");
            copy=StateJson.Copy(first);copy.meleeAnchor.version=2;Require(!MeleeTerrainOcclusion.Validate(copy,out _),"Принята неизвестная версия anchor.");
            copy=StateJson.Copy(first);copy.meleeAnchor.endY=float.NaN;Require(!MeleeTerrainOcclusion.Validate(copy,out _),"Принята нечисловая опора.");

            // A blocked nearest actor must be filtered before stopAtFirstActor chooses a later reachable actor.
            var synthetic=new AttackEvent {id="candidate-order",attacker="source",weapon="spear",level="hub",radius=.08f,
                originX=2,originY=.8f,endX=.5f,endY=.8f,
                meleeAnchor=new MeleeAttackAnchor(new Vector2(.3f,.8f),new Vector2(.3f,.8f))};
            var behind=Raider(2.2f,1);var before=Raider(.2f,1);before.id="before";
            var wallRoom=Room(Door(1));
            var chosen=AttackContacts.Capture(new[]{behind,before},synthetic,true,true,1,
                (_,fraction)=>MeleeTerrainOcclusion.IsReachable(wallRoom,synthetic,fraction,true));
            Require(chosen.Count==1 && chosen[0].id==before.id,"Occluded первая цель отсекает достижимую следующую.");
            return cases+3;
        }

        private static void CheckReceiver()
        {
            var root=new GameObject("Проверка реального ближнего взмаха у закрытой двери");
            try
            {
                var session=root.AddComponent<CoopSession>();var world=new WorldState();
                var source=new ActorState {id="source",owner="remote",player=true,connected=true,level="hub",x=.3f};
                var target=Raider(2.2f,1);
                world.actors.Add(source);world.actors.Add(target);world.doors.Add(Door(1));
                typeof(CoopSession).GetProperty("World").SetValue(session,world);
                var geometry=new SimulationGeometry(_=>new RoomLayout {platforms=Array.Empty<RoomSurface>()});geometry.Reconcile(world);
                var combat=new CombatController(session,geometry);
                var receive=typeof(CombatController).GetMethod("ReceiveAttack",BindingFlags.Instance|BindingFlags.NonPublic);
                int targets=0,surfaces=0,props=0;
                combat.ContactObserved+=(_,a)=>{if(a.id==target.id) targets++;};
                combat.SurfaceContactObserved+=(_,_,_)=>surfaces++;
                combat.PropContactObserved+=(_,_,_,_)=>props++;
                world.props.Add(new PropState {id="behind-prop",definition="box",level="hub",x=2,y=.3f,width=.4f,height=1,anchorHeight=0});
                var raw=Samples("spear",WeaponPose.Origin(source),1,1,null);
                var actual=Samples("spear",WeaponPose.Origin(source),1,1,geometry.Get("hub"));
                // A later source pose cannot change the transmitted historical support.
                source.x=10;
                foreach(var attack in raw) receive.Invoke(combat,new object[]{attack});
                Require(targets==0 && props==0,"Receiver пропустил late raw segment либо использовал текущую позу источника.");
                foreach(var attack in actual) receive.Invoke(combat,new object[]{attack});
                Require(targets==0 && surfaces>0,"Receiver подавил нужный контакт закрытой двери.");
                world.doors[0].open=true;world.doors[0].revision++;geometry.Reconcile(world);
                foreach(var attack in raw) receive.Invoke(combat,new object[]{attack});
                Require(targets>0 && props>0,"Открытая дверь блокирует actual emitted melee contacts.");
                // The victim decides against its latest local pose, not a pose captured by the attacker.
                int previous=targets;target.x=5;
                foreach(var attack in raw) receive.Invoke(combat,new object[]{attack});
                Require(targets==previous,"Receiver подменил текущую позу цели прошлой.");
                var definition=WeaponCatalog.Get("spear");bool old=definition.stopAtTerrain;
                try
                {
                    definition.stopAtTerrain=false;target.x=2.2f;world.doors[0].open=false;world.doors[0].revision++;geometry.Reconcile(world);
                    foreach(var attack in raw) receive.Invoke(combat,new object[]{attack});
                    Require(targets>previous,"CombatController игнорирует mod stopAtTerrain=false.");
                }
                finally {definition.stopAtTerrain=old;}
            }
            finally {UnityEngine.Object.DestroyImmediate(root);}
        }

        private static List<AttackTargetSnapshot> Capture(RoomLayout room,IEnumerable<ActorState> actors,AttackEvent attack,bool first,bool stop)
            =>AttackContacts.Capture(actors,attack,true,first,MeleeTerrainOcclusion.ContactLimit(room,attack,stop),
                (_,fraction)=>MeleeTerrainOcclusion.IsReachable(room,attack,fraction,stop));
        private static List<AttackEvent> Samples(string weapon,Vector2 origin,int facing,float dt,RoomLayout room)
        {
            var result=new List<AttackEvent>();var sweep=new MeleeSweep(weapon,origin,Vector2.right*facing,null,room);
            while(!sweep.Finished) sweep.Advance(origin,dt,segment=>result.Add(new AttackEvent {id="actual-"+weapon+"-"+facing,
                attacker="source",sourceOwner="remote",weapon=weapon,level="hub",radius=MeleeSweep.ContactRadius,speed=segment.speed,
                originX=segment.start.x,originY=segment.start.y,endX=segment.end.x,endY=segment.end.y,meleeAnchor=segment.Anchor()}),room);
            return result;
        }
        private static ActorState Raider(float x,int facing)=>new() {id="target",owner="other",level="hub",x=x,facing=facing,
            anatomy="raider",actorType="raider",pose="idle"};
        private static DoorState Door(int facing)=>new() {id="test-office-door1",definition="office-door1",surfaceTexture="door:metal",level="hub",
            x=facing>0?.8f:-1.6f,y=0,width=.8f,height=1.6f,integrity=new DoorIntegrity {maxHealth=60,health=60}};
        private static RoomLayout Room(DoorState door)=>new() {platforms=DoorGeometry.Compose(Array.Empty<RoomSurface>(),new[]{door},"hub")};
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("Ближний удар/преграда: "+reason);}
    }
}
