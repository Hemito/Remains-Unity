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
    // Mines on seed 104: content install (once per patch), the original's sensing zone and sides, countdown, blast through
    // the common explosion record, interim visibility/disarm rules, the artwork and sounds the view needs, save text and
    // the world delta of a trigger and of a removal.
    //   batch -executeMethod Remains.Editor.MineChecks.RunBatch
    public static class MineChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Run()
        {
            Anatomy.Load();WeaponCatalog.Load();
            CheckRules();CheckArtwork();
            var world=new WorldState {worldId=Guid.NewGuid().ToString("N"),seed=104};
            FactoryPreparation.Create(world,Resources.Load<FactoryCatalogAsset>(FactoryCatalogAsset.ResourceKey),0);
            try
            {
                FactoryContentSources.Prepare(world,"factory:0");
                var batch=RoomContentBatch.Read(FactoryContentSources.Require(world,"factory:0"),"factory:0");
                Require(batch.mines!=null && batch.mines.mines.Length>0,"на заводе seed 104 нет мин");
                Require(batch.mines.mines.Select(m=>m.id).Distinct().Count()==batch.mines.mines.Length,"повтор ID мин");
                Require(RoomContentInstaller.Apply(world,batch),"первая установка содержимого");
                int installed=world.mines.Count;
                Require(installed==batch.mines.mines.Length && world.mines.All(m=>m.side==MineRules.LevelSide && m.definition==FactoryMineAdapter.LevelMine),"установлены не все мины уровня");
                Require(!MineContentInstaller.Apply(world,batch.mines),"повторная установка мин");
                string saved=StateJson.ToJson(world);
                var loaded=StateJson.FromJson<WorldState>(saved);
                Require(StateJson.ToJson(loaded)==saved && loaded.mines.Count==installed,"мины после сохранения");
                CheckDelta(world);
                Debug.Log($"PASS мины: seed104 factory:0 мин={installed} (одиночных {batch.mines.mines.Count(m=>!m.id.Contains('/'))}, минных полей {batch.mines.mines.Count(m=>m.id.Contains('/'))/3}); зона/стороны/отсчёт/взрыв/обезвреживание, изображение, сохранение, дельта срабатывания и удаления.");
            }
            finally {FactoryContentSources.Clear();}
        }

        private static void CheckRules()
        {
            var world=new WorldState {worldId="mine-rules"};
            var mine=new MineState {id="m",level="l",definition="hmine",side=MineRules.LevelSide,x=0,y=0};
            MineRules.Validate(mine);var profile=MineRules.Profile(mine);float s=profile.sensitivity;
            var player=new ActorState {id="p",player=true,connected=true,level="l",x=s*.5f,y=s*.5f,health=100};
            var npc=new ActorState {id="n",player=false,level="l",x=0,y=0,health=100,actorType="tarakan"};
            world.mines.Add(mine);world.actors.Add(player);world.actors.Add(npc);
            Require(MineRules.Senses(mine,player,profile),"игрок в зоне над миной");
            Require(!MineRules.Senses(mine,npc,profile),"мина уровня реагирует на NPC");
            player.y=-.5f*s;Require(!MineRules.Senses(mine,player,profile),"ниже 0.4×зоны мина не чувствует");
            player.y=-.3f*s;Require(MineRules.Senses(mine,player,profile),"немного ниже мины в зоне");
            player.x=s*1.01f;Require(!MineRules.Senses(mine,player,profile),"за краем зоны");
            player.down=true;player.x=0;player.y=0;Require(!MineRules.Senses(mine,player,profile),"упавший игрок не задевает мину");player.down=false;
            var own=new MineState {id="o",level="l",definition="hmine",side=MineRules.PlayerSide,author="p"};
            Require(MineRules.Senses(own,npc,profile) && !MineRules.Senses(own,player,profile),"мина игрока реагирует на NPC, а не на игроков");
            own.arming=MineRules.PlacedArmingSeconds;MineRules.Validate(own);
            Require(!MineRules.Senses(own,npc,profile),"взводящаяся мина игрока уже чувствует");own.arming=0;
            Require(MineRules.Trigger(own,profile) && Mathf.Approximately(own.remaining,profile.delaySeconds*MineRules.PlacedDelayFactor),"укороченный отсчёт мины игрока");
            Require(MineRules.PlacedWeapon("hmine")=="hmine" && MineRules.PlacedWeapon("weapon:mine:grade:0")=="mine" &&
                MineRules.PlacedWeapon("weapon:mine:grade:1")==null && MineRules.PlacedWeapon("grenade")==null && MineRules.PlacedWeapon("weapon:grenade:grade:0")==null,
                "предметы, которые ставят мину");
            Require(DroppedItemTransfer.Size("weapon:hmine:grade:0")==new Vector2(profile.width,profile.height) &&
                DroppedItemTransfer.Size("grenade")==new Vector2(DroppedItemTransfer.Width,DroppedItemTransfer.Height),"размер выброшенной мины — её собственный, остальных вещей — общий");
            bool rejected=false;
            try {MineRules.Validate(new MineState {id="x",level="l",definition="hmine",side=MineRules.LevelSide,arming=1});}
            catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"взведение у мины уровня");

            player.x=MineRules.DisarmRadius+.5f;
            Require(!MineRules.Visible(mine,new ActorState {level="l",x=profile.visibility+.1f},profile),"мина видна дальше радиуса");
            Require(MineRules.DisarmRejection(mine,player,profile)!=null,"обезвреживание издали");
            player.x=.5f;Require(MineRules.DisarmRejection(mine,player,profile)==null,"обезвреживание рядом");

            mine.holder="p";
            Require(MineRules.CanTriggerHeld(mine) && MineRules.DisarmRejection(mine,player,profile)!=null &&
                MineRules.Visible(mine,new ActorState {level="l",x=profile.visibility+5},profile),"удерживаемая мина: риск срабатывания, видна, не обезвреживается");
            var body=MineRules.TelekinesisBody(mine,profile);
            Require(body.mass>0 && body.movable && body.hasPhysicalBody && body.id==mine.id && Mathf.Approximately(body.mass,profile.mass),"мина для правил телекинеза");
            mine.holder=null;Require(!MineRules.CanTriggerHeld(mine),"лежащая мина не срабатывает от телекинеза");
            Require(MineRules.Trigger(mine,profile) && !MineRules.Trigger(mine,profile) && Mathf.Approximately(mine.remaining,profile.delaySeconds),"отсчёт с задержкой из оружия");
            Require(MineRules.DisarmRejection(mine,player,profile)!=null,"обезвреживание сработавшей мины");
            var explosion=MineRules.Detonate(world,mine,new[] {"n","p"});
            Require(world.mines.Count==0 && world.explosions.Count==1 && explosion.stationary,"взрыв убирает мину и записывается");
            Require(explosion.attack.weapon=="hmine" && WeaponCatalog.IsExplosive(explosion.attack.weapon) &&
                Mathf.Approximately(explosion.attack.End.y,profile.height*.5f),"взрыв по оружию мины из центра корпуса");
            Require(explosion.players.SequenceEqual(new[] {"p"}) && explosion.enemies.SequenceEqual(new[] {"n"}),"игроки решают сами, хост перечисляет NPC");
            Require(MineRules.Detonate(world,mine,null)==null,"повторный взрыв");
        }

        private static void CheckArtwork()
        {
            var exported=ExportedContentCatalog.Load();
            foreach(string id in MineContentAuthoring.Mines)
            {
                var profile=WeaponCatalog.Get(id).mine;
                var frames=exported.Resource(profile.artwork).frames.Where(f=>f.frame>=2).Select(exported.FrameSprite).ToArray();
                Require(frames.Length>=2 && frames.All(f=>f!=null && f.bounds.size.x>0),"кадры мины "+id);
                Require(exported.Resource(profile.artwork).frames.Any(f=>f.frame==1 && exported.FrameSprite(f)!=null),"кадр взведения мины "+id);
                foreach(string sound in new[] {profile.beepSound,profile.defuseSound,profile.placeSound})
                    Require(exported.Resource(sound).sounds?.FirstOrDefault()!=null,$"звук {sound} мины {id}");
                Require(exported.TryIcon("weapon:"+id,0,out _),"значок предмета мины "+id);
                // The blast is heard through the combat sound table by weapon (GameplayAudio): a missing entry is silent.
                Require(Resources.Load<TextAsset>("Presentation/combat-audio").text.Contains("{\"weapon\":\""+id+"\",\"clip\":\"grenade-explosion\",\"kind\":\"explosion\""),
                    "звук взрыва мины "+id+" (expl_e, как у гранаты)");
            }
        }

        private static void CheckDelta(WorldState host)
        {
            WorldChanges.Track(host);
            var a=RecoverySnapshot.FromWorld(host);var replica=WorldReplica.FromSnapshot(a);var client=a.CreateLiveWorld();
            var mine=host.mines[0];MineRules.Trigger(mine,MineRules.Profile(mine));WorldChanges.Mark(host,mine);host.revision++;
            var b=RecoverySnapshot.FromWorld(host);
            var delta=WorldDelta.Build(host.worldId,a.Id,a.Composition,b.Id,b.Composition);
            Require(WorldDelta.ChangedParts(delta)==1,"срабатывание мины — одна часть дельты, а не "+WorldDelta.ChangedParts(delta));
            var applied=replica.Apply(StateJson.FromJson<WorldDeltaPayload>(StateJson.ToJson(delta)),client);
            Require(applied.Json==b.Json,"клиент собрал другой мир после срабатывания");
            var next=applied.CreateLiveWorld();
            Require(next.mines[0].triggered && (next.mines.Count<2 || ReferenceEquals(next.mines[1],client.mines[1])),"сработавшая мина разобрана, остальные переиспользованы");
            MineRules.Detonate(host,mine,null);host.revision++;
            var c=RecoverySnapshot.FromWorld(host);
            var removal=replica.Apply(StateJson.FromJson<WorldDeltaPayload>(StateJson.ToJson(WorldDelta.Build(host.worldId,b.Id,b.Composition,c.Id,c.Composition))),next);
            Require(removal.Json==c.Json && removal.CreateLiveWorld().mines.Count==host.mines.Count,"удаление мины по дельте");
        }

        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException("Мины: "+message);}
    }
}
