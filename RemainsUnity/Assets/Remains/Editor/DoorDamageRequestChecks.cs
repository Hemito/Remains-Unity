using System;
using System.Collections.Generic;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class DoorDamageRequestChecks
    {
        private sealed class FixedRule : IDoorDamageRule
        {
            private readonly float damage;
            public FixedRule(float damage)=>this.damage=damage;
            public float Damage(DoorState door,AttackEvent impact,WeaponDefinition weapon)=>damage;
        }
        private sealed class Fixture
        {
            public readonly WorldState world=new() {worldId="door-world"};
            public readonly ActorState player=new() {id="player",player=true,connected=true,owner="connection:1",level="factory:0",generation=3,lifecycleSequence=5};
            public readonly DoorState door=new() {id="office-door",definition="base:door",level="factory:0",surfaceTexture="metal",x=2,y=0,width=.4f,height=2.4f,
                integrity=new DoorIntegrity {maxHealth=100,health=100}};
            public readonly WeaponDefinition weapon=new() {id="crowbar",damage=14};
            public readonly DoorDamageAttackLedger ledger=new();
            public readonly DoorDamageRequestData request;
            public Fixture(bool projectile=false)
            {
                world.actors.Add(player);world.doors.Add(door);
                var attack=new AttackEvent {id="attack",attacker=player.id,weapon=projectile?"pistol":"crowbar",level=player.level,
                    sourceOwner=player.owner,sourcePlayer=true,originX=0,originY=1,endX=4,endY=1,speed=projectile?25:4,radius=projectile?0:.08f};
                weapon.id=attack.weapon;weapon.projectileSpeed=projectile?25:0;weapon.damage=projectile?11:14;
                request=new DoorDamageRequestData {world=world.worldId,door=door.id,decisionActor=player.id,
                    decisionGeneration=player.generation,decisionLifecycle=player.lifecycleSequence,sourceGeneration=player.generation,
                    sourceLifecycle=player.lifecycleSequence,attack=attack,contact=new Vector2(2,1),projectileGeneration=projectile?4:0};
                if(projectile) world.projectiles.Add(new ProjectileState {launch=StateJson.Copy(attack),owner=player.owner,generation=4,flightSpeed=25});
                else if(ledger.Record(world.worldId,player,attack)!=null) throw new InvalidOperationException("Не записан test segment.");
            }
            public DoorChange Apply(out string reason,bool paused=false,IDoorDamageRule rule=null)=>
                DoorDamageRequestRules.Apply(world,player,request,paused,ledger,rule??new CatalogDoorImpactDamage(),id=>id==weapon.id?weapon:null,out reason);
        }
        public static void Run()=>RunDomain(true);
        public static int RunDomain(bool log=false)
        {
            int count=0;
            void Check(bool valid,string reason) {count++;if(!valid) throw new InvalidOperationException("Door damage request: "+reason);}
            void Reject(Action<Fixture> change,bool projectile=false,bool paused=false)
            {
                var f=new Fixture(projectile);change(f);string before=StateJson.ToJson(f.door);
                Check(f.Apply(out string reason,paused)==DoorChange.Rejected && !string.IsNullOrWhiteSpace(reason),"Некорректный контакт должен иметь явную причину отказа.");
                Check(before==StateJson.ToJson(f.door),"Отказ не меняет HP, revision, receipts или extensions.");
            }
            var melee=new Fixture();
            Reject(f=>f.door.integrity.resolvedAttacks=null);
            Reject(f=>f.door.integrity.version=2);
            Reject(f=>f.door.integrity.health=-1);
            Check(melee.Apply(out _) == DoorChange.Changed && melee.door.integrity.health==86 && melee.door.revision==1,"Принятый crowbar contact меняет HP один раз.");
            Check(melee.Apply(out _,true)==DoorChange.Unchanged && melee.door.integrity.health==86 && melee.door.revision==1,"Durable повтор безопасен на паузе.");
            var secondSegment=StateJson.Copy(melee.request.attack);secondSegment.originX=1;
            Check(melee.ledger.Record(melee.world.worldId,melee.player,secondSegment)==null,"Несколько точных sweeps одного ID допускаются.");
            Check(melee.ledger.Contains(melee.world.worldId,melee.player,melee.request.attack),"Поздний sweep не заменяет ранее принятый.");
            melee.request.attack=secondSegment;
            Check(melee.Apply(out _)==DoorChange.Unchanged && melee.door.integrity.health==86,"Другой sweep одного удара не повторяет HP.");
            melee.request.attack.id="second-attack";melee.ledger.Record(melee.world.worldId,melee.player,melee.request.attack);
            Check(melee.Apply(out _)==DoorChange.Changed && melee.door.integrity.health==72 && melee.door.revision==2,"Независимая атака имеет отдельный receipt.");
            Check(melee.Apply(out _,false,new FixedRule(999))==DoorChange.Unchanged,"Повтор не вызывает заменённое правило урона.");
            var lethal=new Fixture();
            Check(lethal.Apply(out _,false,new FixedRule(150))==DoorChange.Changed && lethal.door.Destroyed && !lethal.door.Blocks,"Custom rule разрушает ровно до нуля.");
            Check(lethal.Apply(out _,true)==DoorChange.Unchanged && lethal.door.revision==1,"Разрушенный объект сохраняет exactly-once receipt.");
            lethal.request.attack.id="after-destroy";lethal.ledger.Record(lethal.world.worldId,lethal.player,lethal.request.attack);
            Check(lethal.Apply(out string destroyedReason)==DoorChange.Rejected && destroyedReason!=null && lethal.door.integrity.resolvedAttacks.Count==1,"Новый удар по разрушенному не создаёт receipt.");
            Reject(f=>f.request.world="other");Reject(f=>f.player.owner="observer");Reject(f=>f.player.connected=false);
            Reject(f=>f.request.decisionActor="other");Reject(f=>f.request.decisionGeneration++);Reject(f=>f.request.decisionLifecycle++);
            Reject(f=>f.request.sourceGeneration++);Reject(f=>f.request.sourceLifecycle++);Reject(f=>f.player.down=true);
            Reject(f=>f.player.level="hub");Reject(f=>f.door.level="hub");Reject(f=>f.door.integrity=null);
            Reject(f=>f.door.open=true);Reject(f=>{},paused:true);Reject(f=>f.ledger.Clear());
            Reject(f=>f.request.contact=new Vector2(2,1.1f));Reject(f=>f.request.attack.originY=1.1f);
            Reject(f=>f.request.attack.speed=float.NaN);Reject(f=>f.request.attack.radius=-1);
            Reject(f=>f.request.attack.endX=float.PositiveInfinity);Reject(f=>f.request.contact=new Vector2(float.NaN,1));
            Reject(f=>f.request.projectileGeneration=7);
            var custom=new Fixture();Check(custom.Apply(out _,false,new FixedRule(3))==DoorChange.Changed && custom.door.integrity.health==97,"Mod hook задаёт independent object damage.");
            foreach(float invalid in new[] {0f,-1f,float.NaN,float.PositiveInfinity})
            {
                var f=new Fixture();Check(f.Apply(out string error,false,new FixedRule(invalid))==DoorChange.Rejected && error!=null && f.door.revision==0,"Invalid mod amount не меняет объект.");
            }
            var unknown=new Fixture();unknown.weapon.id="mod:unknown";unknown.request.attack.weapon=unknown.weapon.id;unknown.request.attack.id="mod-attack";
            unknown.ledger.Record(unknown.world.worldId,unknown.player,unknown.request.attack);
            Check(unknown.Apply(out string unknownReason)==DoorChange.Rejected && unknownReason.Contains("IDoorDamageRule"),"Unknown weapon требует явного hook, без baseDamage fallback.");
            Check(unknown.Apply(out _,false,new FixedRule(7))==DoorChange.Changed,"Мод может явно разрешить свой weapon.");
            var flight=new Fixture(true);
            Check(flight.Apply(out _)==DoorChange.Changed && flight.door.integrity.health==89,"Реальный принятый flight segment использует base damage.");
            Check(flight.Apply(out _,true)==DoorChange.Unchanged,"Повтор текущего owner безопасен при paused flight.");
            Reject(f=>f.world.projectiles[0].owner="connection:2",true);Reject(f=>f.request.projectileGeneration--,true);
            Reject(f=>f.request.attack.sourceOwner="forged",true);Reject(f=>f.request.attack.originY=1.01f,true);
            Reject(f=>f.request.attack.endX=5,true);Reject(f=>f.request.attack.speed=99,true);Reject(f=>f.request.attack.radius=.2f,true);
            Reject(f=>f.world.projectiles.Clear(),true);Reject(f=>f.request.attack.attacker="other",true);
            var transferred=new Fixture(true);var next=transferred.player.Copy();next.id="next";next.owner="connection:2";next.generation=2;
            transferred.world.actors.Add(next);transferred.world.projectiles[0].owner=next.owner;transferred.world.projectiles[0].generation++;
            transferred.request.decisionActor=next.id;transferred.request.decisionGeneration=next.generation;transferred.request.decisionLifecycle=next.lifecycleSequence;
            transferred.request.projectileGeneration++;
            Check(DoorDamageRequestRules.Apply(transferred.world,next,transferred.request,false,transferred.ledger,new CatalogDoorImpactDamage(),_=>transferred.weapon,out _)==DoorChange.Changed,
                "Текущий flight owner решает контакт после handoff, старый launch attacker stamp не заменяет его.");
            var recovered=StateJson.Copy(transferred.world);var recoveredNext=recovered.Find(next.id);
            Check(DoorDamageRequestRules.Apply(recovered,recoveredNext,transferred.request,true,new DoorDamageAttackLedger(),new CatalogDoorImpactDamage(),_=>transferred.weapon,out _)==DoorChange.Unchanged,
                "Persisted flight+perdoor receipt сохраняют duplicate после reload без transient ledger.");
            transferred.request.decisionActor=transferred.player.id;transferred.request.decisionGeneration=transferred.player.generation;
            Check(transferred.Apply(out string previousOwner)==DoorChange.Rejected && previousOwner!=null && transferred.door.integrity.health==89,
                "Предыдущий owner не может повторить damage даже после receipt.");
            var preserved=new Fixture();preserved.door.integrity.extensions=new Dictionary<string,Newtonsoft.Json.Linq.JToken> {{"mod:door",new Newtonsoft.Json.Linq.JObject {{"kept",2}}}};
            Check(preserved.Apply(out _)==DoorChange.Changed && (int)StateJson.Copy(preserved.door).integrity.extensions["mod:door"]["kept"]==2,"Unknown integrity extension сохраняется.");
            var authored=new RoomSurface {x=0,y=0,width=1,height=1,texture="metal"};
            var derived=DoorGeometry.Compose(new[] {authored},new[] {preserved.door},preserved.player.level);
            Check(derived.Length==2 && derived[0].sourceDoorId==null && derived[1].sourceDoorId==preserved.door.id,"Только derived DoorGeometry несёт source ID.");
            string serialized=StateJson.ToJson(derived[1]);
            Check(!serialized.Contains("sourceDoorId") && StateJson.Copy(derived[1]).sourceDoorId==null,"Derived metadata не меняет authored JSON/fingerprint.");
            var fresh=new Fixture();var duplicate=StateJson.Copy(fresh.request.attack);duplicate.attacker="foreign";
            Check(fresh.ledger.Record(fresh.world.worldId,fresh.player,duplicate)!=null,"Ledger не принимает произвольный header чужого actor.");
            duplicate=StateJson.Copy(fresh.request.attack);duplicate.weapon="spear";
            Check(fresh.ledger.Record(fresh.world.worldId,fresh.player,duplicate)!=null,"Один attack ID не переиспользуется другим weapon.");
            fresh.ledger.Record("new-world",fresh.player,new AttackEvent {id="new",attacker=fresh.player.id,sourceOwner=fresh.player.owner,level=fresh.player.level,weapon="crowbar"});
            Check(!fresh.ledger.Contains(fresh.world.worldId,fresh.player,fresh.request.attack),"World switch очищает transient accepted envelope.");
            var bounded=new Fixture();bool boundedSegments=true;
            for(int i=1;i<DoorDamageAttackLedger.MaximumSegmentsPerAttack;i++)
            {
                var segment=StateJson.Copy(bounded.request.attack);segment.speed=i+.5f;
                boundedSegments &= bounded.ledger.Record(bounded.world.worldId,bounded.player,segment)==null;
            }
            var excess=StateJson.Copy(bounded.request.attack);excess.speed=999;
            Check(boundedSegments && bounded.ledger.Record(bounded.world.worldId,bounded.player,excess)!=null,"Segment cap имеет явный отказ, не подменяет принятые sweeps.");
            Check(bounded.ledger.Contains(bounded.world.worldId,bounded.player,bounded.request.attack),"Cap сохраняет ранее принятый envelope.");
            for(int i=0;i<DoorDamageAttackLedger.MaximumAttacks;i++)
            {
                var attack=StateJson.Copy(bounded.request.attack);attack.id="bounded-"+i;
                bounded.ledger.Record(bounded.world.worldId,bounded.player,attack);
            }
            Check(!bounded.ledger.Contains(bounded.world.worldId,bounded.player,bounded.request.attack),"Transient attack retention ограничен без выдачи старой отметки за valid envelope.");
            var beforeEpoch=new Fixture();beforeEpoch.player.generation++;
            Check(beforeEpoch.ledger.Record(beforeEpoch.world.worldId,beforeEpoch.player,beforeEpoch.request.attack)!=null,"Owner generation reset запрещает reuse melee ID.");
            var legacy=new Fixture();legacy.door.integrity=null;
            var legacySurface=DoorGeometry.Compose(Array.Empty<RoomSurface>(),new[] {legacy.door},legacy.player.level);
            Check(legacySurface.Length==1 && legacySurface[0].sourceDoorId==legacy.door.id && legacy.door.Blocks,"Неразрушаемая legacy дверь остаётся physical barrier с derived identity.");
            var projectileSurface=new Fixture(true);
            var room=new RoomLayout {platforms=DoorGeometry.Compose(Array.Empty<RoomSurface>(),new[] {projectileSurface.door},projectileSurface.player.level),ladders=Array.Empty<RoomLadder>()};
            var first=ShotObstacles.FirstContact(room,projectileSurface.request.attack.Origin,projectileSurface.request.attack.End,out var actualSurface);
            Check(first.HasValue && actualSurface.sourceDoorId==projectileSurface.door.id && Mathf.Abs(first.Value-.5f)<.00001f,
                "Обычный ShotObstacles source contact читает derived stable ID, не texture.");
            if(log) Debug.Log($"DoorDamageRequestChecks PASS: {count}; owner/generation/contact/pause/dedup/reload/mod/derived-source.");
            return count;
        }
    }
}
