using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Traps and charges on seed 104: generated definitions and weapons, content install (once per patch), the trap's
    // sides and acttrap, the bite through the common damage rule, skin, the charge's blast through the common explosion
    // record and its reach, interim visibility/action rules, the artwork and sounds the view needs, save text and the
    // world delta of a change and of a removal.
    //   batch -executeMethod Remains.Editor.HazardChecks.RunBatch
    public static class HazardChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Run()
        {
            Anatomy.Load();WeaponCatalog.Load();
            var trap=HazardCatalog.All.Single(d=>d.kind==HazardRules.Trap);var charge=HazardCatalog.All.Single(d=>d.kind==HazardRules.Charge);
            Require(trap.id=="mtrap" && trap.objects.Contains("trap") && Mathf.Approximately(trap.health,10) && Mathf.Approximately(trap.skin,10) &&
                Mathf.Approximately(WeaponCatalog.Get(trap.biteWeapon).damage,24),"капкан из AllData (mtrap: здоровье 10, кожа 10, укус 24)");
            Require(charge.id=="damexpl1" && charge.objects.Contains("expl1") && Mathf.Approximately(charge.health,20) &&
                WeaponCatalog.IsExplosive(charge.blastWeapon) && charge.disarmItem=="weapon:dinamit" && charge.disarmCount==3,"заряд из AllData (damexpl1: здоровье 20, взрыв, три динамита)");
            Require(ActorTypes.Get("pony").activateTrap==HazardRules.SpringsTraps && ActorTypes.Get("tarakan").activateTrap==1,"acttrap: пони 2 по умолчанию, таракан 1 из AllData");
            CheckRules(trap,charge);CheckArtwork(trap,charge);
            var world=new WorldState {worldId=Guid.NewGuid().ToString("N"),seed=104};
            FactoryPreparation.Create(world,Resources.Load<FactoryCatalogAsset>(FactoryCatalogAsset.ResourceKey),0);
            try
            {
                FactoryContentSources.Prepare(world,"factory:0");
                var batch=RoomContentBatch.Read(FactoryContentSources.Require(world,"factory:0"),"factory:0");
                Require(batch.hazards!=null && batch.hazards.hazards.Length>0,"на заводе seed 104 нет ловушек");
                Require(batch.hazards.hazards.Select(h=>h.id).Distinct().Count()==batch.hazards.hazards.Length,"повтор ID ловушек");
                Require(RoomContentInstaller.Apply(world,batch),"первая установка содержимого");
                int traps=world.hazards.Count(h=>h.definition==trap.id),charges=world.hazards.Count(h=>h.definition==charge.id);
                Require(world.hazards.Count==batch.hazards.hazards.Length && world.hazards.All(h=>h.side==MineRules.LevelSide && !h.closed),"установлены не все ловушки уровня");
                Require(!HazardContentInstaller.Apply(world,batch.hazards),"повторная установка ловушек");
                string saved=StateJson.ToJson(world);
                var loaded=StateJson.FromJson<WorldState>(saved);
                Require(StateJson.ToJson(loaded)==saved && loaded.hazards.Count==world.hazards.Count,"ловушки после сохранения");
                CheckDelta(world);
                Debug.Log($"PASS ловушки: seed104 factory:0 капканов={traps}, зарядов={charges}; стороны/acttrap/укус/кожа/взрыв/досягаемость, видимость и действия, изображение и звуки, сохранение, дельта изменения и удаления.");
            }
            finally {FactoryContentSources.Clear();}
        }

        private static void CheckRules(HazardDefinition trapProfile,HazardDefinition chargeProfile)
        {
            var world=new WorldState {worldId="hazard-rules"};
            var trap=HazardRules.Create("t","l",trapProfile,Vector2.zero);
            var player=new ActorState {id="p",player=true,connected=true,level="l",x=.1f,y=0,health=100,actorType="pony"};
            var npc=new ActorState {id="n",level="l",x=0,y=0,health=100,actorType="tarakan"};
            var big=new ActorState {id="b",level="l",x=0,y=0,health=100,actorType="raider"};
            world.hazards.Add(trap);world.actors.Add(player);world.actors.Add(npc);world.actors.Add(big);
            Require(HazardRules.Springs(trap,player,trapProfile),"игрок на капкане");
            Require(!HazardRules.Springs(trap,npc,trapProfile) && !HazardRules.Springs(trap,big,trapProfile),"капкан уровня ловит только игроков");
            player.x=trapProfile.width*.5f+Anatomy.Movement(player).width*.5f+.05f;Require(!HazardRules.Springs(trap,player,trapProfile),"игрок рядом, но не на капкане");
            player.x=0;player.down=true;Require(!HazardRules.Springs(trap,player,trapProfile),"лежащий игрок не задевает капкан");player.down=false;
            trap.closed=true;Require(!HazardRules.Springs(trap,player,trapProfile),"захлопнутый капкан не ловит");
            Require(HazardRules.Action(trap,trapProfile)=="Взвести","захлопнутый капкан взводят");trap.closed=false;
            Require(HazardRules.Action(trap,trapProfile)=="Обезвредить","открытый капкан обезвреживают");
            var own=new HazardState {id="o",level="l",definition=trapProfile.id,side=MineRules.PlayerSide,author="p",health=trapProfile.health};
            HazardRules.Validate(own);
            Require(HazardRules.Springs(own,big,trapProfile) && !HazardRules.Springs(own,npc,trapProfile) && !HazardRules.Springs(own,player,trapProfile),
                "капкан игрока ловит не-игроков с acttrap 2, но не мелких (acttrap 1) и не игроков");

            // The bite: the victim's own hit regions decide it; the damage is the bite weapon's through the common rule.
            var bite=HazardRules.Bite(trap,trapProfile,player,"host");
            Require(!bite.sourcePlayer && bite.weapon==trapProfile.biteWeapon && HitGeometry.Contacts(player,bite),"укус касается тела жертвы");
            var receiver=new DamageReceiver(new PrototypeDamageRule());float before=player.health;
            Require(receiver.Apply(player,bite,true) && Mathf.Approximately(before-player.health,24),"укус отнимает 24 здоровья");

            // Skin: a hit up to the skin does nothing (pistol 11 against 10: 1 point).
            Require(!HazardRules.Damage(trap,trapProfile,10) && Mathf.Approximately(trap.health,trapProfile.health),"удар в пределах кожи не ранит капкан");
            Require(!HazardRules.Damage(trap,trapProfile,11) && Mathf.Approximately(trap.health,trapProfile.health-1),"кожа вычитается");
            Require(HazardRules.Damage(trap,trapProfile,100),"сильный удар ломает капкан");

            var charge=HazardRules.Create("c","l",chargeProfile,new Vector2(5,0));world.hazards.Add(charge);
            Require(HazardRules.Action(charge,chargeProfile)=="Обезвредить","заряд обезвреживают");
            var blast=HazardRules.Blast(charge,chargeProfile);float range=WeaponCatalog.Get(chargeProfile.blastWeapon).range;
            var near=HazardRules.Create("near","l",chargeProfile,new Vector2(5+range+chargeProfile.width*.5f-.05f,0));
            var far=HazardRules.Create("far","l",chargeProfile,new Vector2(5+range+chargeProfile.width*.5f+.1f,0));
            Require(HazardRules.InBlast(near,chargeProfile,blast) && !HazardRules.InBlast(far,chargeProfile,blast),"взрыв достаёт корпус в радиусе, не дальше");
            Require(!HazardRules.Damage(charge,chargeProfile,chargeProfile.health-1) && HazardRules.Damage(charge,chargeProfile,1),"заряд разрушается по здоровью");
            var explosion=HazardRules.Detonate(world,charge,new[] {"n"});
            Require(!world.hazards.Contains(charge) && world.explosions.Count==1 && explosion.stationary && explosion.attack.weapon==chargeProfile.blastWeapon &&
                Mathf.Approximately(explosion.attack.End.y,chargeProfile.height*.5f),"взрыв заряда убирает его и записывается из центра корпуса");
            Require(explosion.players.SequenceEqual(new[] {"p"}) && explosion.enemies.SequenceEqual(new[] {"n"}),"игроки решают сами, хост перечисляет NPC");
            Require(HazardRules.Detonate(world,charge,null)==null,"повторный взрыв");

            player.x=MineRules.DisarmRadius+.5f;
            Require(!HazardRules.Visible(trap,new ActorState {level="l",x=trapProfile.visibility+.1f},trapProfile) && HazardRules.Visible(own,null,trapProfile),
                "капкан уровня виден только вблизи, капкан игрока — всегда");
            Require(HazardRules.DisarmRejection(trap,player,trapProfile)!=null,"действие издали");
            player.x=.5f;Require(HazardRules.DisarmRejection(trap,player,trapProfile)==null,"действие рядом");
            bool rejected=false;
            try {HazardRules.Validate(new HazardState {id="x",level="l",definition=trapProfile.id,side=MineRules.PlayerSide,health=1});}
            catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"капкан игрока без автора");
        }

        private static void CheckArtwork(HazardDefinition trap,HazardDefinition charge)
        {
            var exported=ExportedContentCatalog.Load();
            var trapFrames=exported.Resource(trap.artwork).frames.OrderBy(f=>f.frame).Select(exported.FrameSprite).ToArray();
            Require(trapFrames.Length==20 && trapFrames.All(f=>f!=null && f.bounds.size.x>0),"20 кадров капкана");
            Require(exported.Resource(charge.artwork).frames.Select(exported.FrameSprite).All(f=>f!=null && f.bounds.size.x>0),"кадр заряда");
            foreach(string sound in new[] {trap.snapSound,trap.rearmSound})
                Require(exported.Resource(sound).sounds?.FirstOrDefault()!=null,"звук капкана "+sound);
            Require(exported.TryIcon(charge.disarmItem,0,out _),"значок динамита");
            string audio=Resources.Load<TextAsset>("Presentation/combat-audio").text;
            Require(audio.Contains("{\"weapon\":\""+trap.biteWeapon+"\",\"clip\":\"exported:"+trap.snapSound+"\",\"kind\":\"melee\"") &&
                audio.Contains("{\"weapon\":\""+charge.blastWeapon+"\",\"clip\":\"grenade-explosion\",\"kind\":\"explosion\""),"звуки укуса (trap_a) и взрыва заряда");
        }

        private static void CheckDelta(WorldState host)
        {
            WorldChanges.Track(host);
            var a=RecoverySnapshot.FromWorld(host);var replica=WorldReplica.FromSnapshot(a);var client=a.CreateLiveWorld();
            var hazard=host.hazards[0];hazard.closed=!hazard.closed;WorldChanges.Mark(host,hazard);host.revision++;
            var b=RecoverySnapshot.FromWorld(host);
            var delta=WorldDelta.Build(host.worldId,a.Id,a.Composition,b.Id,b.Composition);
            Require(WorldDelta.ChangedParts(delta)==1,"изменение ловушки — одна часть дельты, а не "+WorldDelta.ChangedParts(delta));
            var applied=replica.Apply(StateJson.FromJson<WorldDeltaPayload>(StateJson.ToJson(delta)),client);
            Require(applied.Json==b.Json,"клиент собрал другой мир после изменения ловушки");
            var next=applied.CreateLiveWorld();
            Require(next.hazards[0].closed==hazard.closed && (next.hazards.Count<2 || ReferenceEquals(next.hazards[1],client.hazards[1])),"изменённая ловушка разобрана, остальные переиспользованы");
            host.hazards.Remove(hazard);host.revision++;
            var c=RecoverySnapshot.FromWorld(host);
            var removal=replica.Apply(StateJson.FromJson<WorldDeltaPayload>(StateJson.ToJson(WorldDelta.Build(host.worldId,b.Id,b.Composition,c.Id,c.Composition))),next);
            Require(removal.Json==c.Json && removal.CreateLiveWorld().hazards.Count==host.hazards.Count,"удаление ловушки по дельте");
        }

        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Ловушки: "+message);}
    }
}
