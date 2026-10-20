using System;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class FactoryEnemyScalingChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        public static void Run()
        {
            Anatomy.Load();WeaponCatalog.Load();ActorTypes.Load();
            CheckFloors();CheckMigration();CheckDamage();CheckTurrets();CheckProvenance();
            Debug.Log("Уровни противников завода: PASS — ряды 0–20, исходные множители, seed/ID, раненые/мёртвые/удалённые, повтор установки, независимые каталоги и исторические атаки.");
        }
        private static void CheckFloors()
        {
            int[] minimum={0,1,1,2,3,4,4,5,6,6,7};
            int[] maximum={0,1,2,3,4,5,7,8,9,10,11};
            int changed=0,champions=0;
            for(int floor=0;floor<=20;floor++)
            {
                string id=$"factory:0:2,{floor}:placement";
                Require(FactoryEnemyScaling.PlacementFloor(id,"factory:0")==floor,"Потерян глобальный ряд ID: "+id);
                for(int sample=0;sample<100;sample++)
                {
                    var a=FactoryEnemyScaling.Roll(104,id+sample,floor,FactoryEnemyScaling.Unit,100);
                    var b=FactoryEnemyScaling.Roll(104,id+sample,floor,FactoryEnemyScaling.Unit,100);
                    Require(StateJson.ToJson(a)==StateJson.ToJson(b),"Случайность зависит от повторного вызова/порядка");
                    var other=FactoryEnemyScaling.Roll(105,id+sample,floor,FactoryEnemyScaling.Unit,100);
                    if(a.enemyLevel!=other.enemyLevel || a.rolledChampion!=other.rolledChampion) changed++;
                    Require(a.enemyLevel>=minimum[floor/2] && a.enemyLevel<=maximum[floor/2],"Уровень вышел из исходного диапазона: "+id);
                    if(a.rolledChampion>0) {champions++;Require(a.enemyLevel==floor/2,"Чемпионский бросок ошибочно получил обычный разброс");}
                }
            }
            Require(changed>0 && champions>0,"Seed не влияет на броски или чемпионская ветвь не проверена");
            Near(FactoryEnemyScaling.HealthFactor(10),2.1f,"HP");Near(FactoryEnemyScaling.NaturalDamageFactor(10),1.7f,"Естественный урон");
            Near(FactoryEnemyScaling.ProtectionFactor(10),1.5f,"Кожа");Near(FactoryEnemyScaling.MineDamageFactor(10),1,"Мина на L10");
            Near(FactoryEnemyScaling.MineDamageFactor(11),1.1f,"Мина на L11");
            var trap=HazardCatalog.All.Single(d=>d.kind==HazardRules.Trap);
            for(int level=0;level<=11;level++)
                Require(trap.health*FactoryEnemyScaling.HealthFactor(level)<35,"Капкан переживает исходный внутренний урон 35 на текущей глубине");
            Reject(()=>FactoryEnemyScaling.PlacementFloor("factory:0:2,21:bad","factory:0"),"Принят ряд за пределами мегазавода");
        }
        private static void CheckMigration()
        {
            const string level="factory:0";
            ActorState Source(string key)=>new() {id=level+":1,20:"+key,actorType="tarakan",anatomy="tarakan",level=level,health=100,weapon=""};
            var alive=Source("alive");var dead=Source("dead");var removed=Source("removed");
            var layout=new NpcLayout {patch=level+":"+FactoryCreatureAdapter.Patch,level=level,actors=new[]{alive,dead,removed}};
            var trapProfile=HazardCatalog.All.Single(d=>d.kind==HazardRules.Trap);
            var chargeProfile=HazardCatalog.All.Single(d=>d.kind==HazardRules.Charge);
            var trap=HazardRules.Create(level+":2,20:trap",level,trapProfile,Vector2.zero);
            var charge=HazardRules.Create(level+":2,20:charge",level,chargeProfile,Vector2.zero);
            var mine=new MineState {id=level+":3,20:mine",level=level,definition="hmine",side=MineRules.LevelSide,triggered=true,remaining=.25f};
            var batch=new RoomContentBatch {npcs=layout,
                hazards=new HazardLayout {patch=level+":"+FactoryHazardAdapter.Patch,level=level,hazards=new[]{trap,charge}},
                mines=new MineLayout {patch=level+":"+FactoryMineAdapter.Patch,level=level,mines=new[]{mine}}};
            var world=new WorldState {worldId="scaling-check",seed=104};
            var wounded=StateJson.Copy(alive);wounded.health=25;wounded.owner="owner";wounded.inventory.Add(new ItemStack {id="preserved",definition="pin",count=2});
            wounded.resolvedAttacks.Add("old-hit");
            var corpse=StateJson.Copy(dead);corpse.health=0;corpse.down=true;corpse.pose="down";
            world.actors.Add(wounded);world.actors.Add(corpse);
            var savedTrap=StateJson.Copy(trap);savedTrap.health=trapProfile.health*.4f;savedTrap.closed=true;
            world.hazards.Add(savedTrap);world.hazards.Add(StateJson.Copy(charge));world.mines.Add(StateJson.Copy(mine));
            world.contentPatches.Add(layout.patch);world.contentPatches.Add(batch.hazards.patch);world.contentPatches.Add(batch.mines.patch);
            Require(RoomContentInstaller.NeedsInstall(world,batch),"Установленная версия скрывает миграцию старых противников");
            Require(RoomContentInstaller.Apply(world,batch),"Не применена миграция");
            Require(ReferenceEquals(world.Find(wounded.id),wounded) && wounded.owner=="owner" && wounded.inventory.Single().id=="preserved" && wounded.resolvedAttacks.Single()=="old-hit","Миграция заменила живой объект/его данные");
            Near(wounded.health/wounded.factoryEnemy.maxHealth,.25f,"Доля ранения NPC");
            Near(savedTrap.health/savedTrap.factoryEnemy.maxHealth,.4f,"Доля ранения ловушки");
            Require(corpse.health==0 && corpse.down && corpse.pose=="down" && world.Find(removed.id)==null,"Воскрешён умерший/удалённый NPC");
            Require(savedTrap.closed && world.mines[0].triggered && world.mines[0].remaining==.25f,"Сброшено состояние ловушки/мины");
            Near(world.hazards[1].health,chargeProfile.health,"UnitDamager ошибочно получил рост HP");
            string before=StateJson.ToJson(world);
            Require(!RoomContentInstaller.Apply(world,batch) && StateJson.ToJson(world)==before,"Повторная установка повторно усилила противников");
            var restored=StateJson.Copy(world);
            Require(StateJson.ToJson(restored)==before,"Профиль масштаба не пережил сохранение");
            // Fresh installation exercises append publication followed by the prepared instance migration.
            var fresh=new WorldState {worldId="new-scaled-world",seed=104};
            Require(RoomContentInstaller.Apply(fresh,batch) && fresh.actors.Count==3 && fresh.actors.All(a=>a.factoryEnemy!=null),"Новые NPC не получили уровень после append publication");
        }
        private static void CheckDamage()
        {
            var type=ActorTypes.Get("tarakan");float damage=type.naturalAttack.damage,protection=type.hitProtection;
            var scale=FactoryEnemyScaling.Roll(104,"factory:0:0,20:damage",20,FactoryEnemyScaling.Unit,100);
            var attack=new AttackEvent {id="scale-bite",attacker="source",level="hub",weapon="",naturalAttackType="tarakan",naturalAttackTarget="victim",
                factoryEnemy=FactoryEnemyScaling.Capture(scale)};
            float factor=FactoryEnemyScaling.AttackFactor(attack);
            Near(factor,1+.07f*scale.enemyLevel,"Множитель события естественной атаки");
            var source=new ActorState {id=attack.attacker,factoryEnemy=scale};
            FactoryEnemyScaling.ValidateCaptured(attack,source);
            var altered=StateJson.Copy(attack);altered.factoryEnemy.enemyLevel=altered.factoryEnemy.enemyLevel==0?1:0;
            Reject(()=>FactoryEnemyScaling.ValidateCaptured(altered,source),"Принят подменённый множитель атаки");
            var sameLaunch=typeof(DoorDamageRequestRules).GetMethod("SameLaunch",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
            Require(sameLaunch!=null && !(bool)sameLaunch.Invoke(null,new object[]{attack,altered}),"Подменённый профиль принят как канонический полёт");
            var data=type.naturalAttack;attack.endX=data.reach;attack.radius=data.radius;
            var receiver=new DamageReceiver(new PrototypeDamageRule());
            var hurt=new ActorState {id="victim",actorType="pony",level="hub",player=true,connected=true,health=100};
            Require(receiver.Apply(hurt,attack,true),"Получатель не применил усиленную атаку");
            Near(hurt.health,100-damage*factor,"Применённый естественный урон");
            scale.enemyLevel=0; // The captured historical event is an independent profile.
            Near(FactoryEnemyScaling.AttackFactor(StateJson.Copy(attack)),factor,"Историческая атака зависит от изменённого стрелка");
            var victim=new ActorState {actorType="tarakan",factoryEnemy=attack.factoryEnemy};
            var shield=new TypeHitProtectionRule();
            Near(shield.Resolve(victim,attack,100),100-protection*FactoryEnemyScaling.Protection(attack.factoryEnemy),"Кожа экземпляра");
            Require(type.naturalAttack.damage==damage && type.hitProtection==protection,"Изменены общие данные типа");
            var ranged=new AttackEvent {weapon="pistol",factoryEnemy=attack.factoryEnemy};
            Near(FactoryEnemyScaling.AttackFactor(ranged),1,"Огнестрел получил чужой множитель урона вместо weaponSkill");
            var mine=StateJson.Copy(attack);mine.naturalAttackType=null;mine.factoryEnemy.kind=FactoryEnemyScaling.Mine;mine.factoryEnemy.enemyLevel=11;
            Near(FactoryEnemyScaling.AttackFactor(mine),1.1f,"Множитель взрыва мины");
            mine.factoryEnemy.kind=FactoryEnemyScaling.Charge;
            Near(FactoryEnemyScaling.AttackFactor(mine),1,"Фиксированный взрыв UnitDamager ошибочно усилен");
        }
        private static void CheckTurrets()
        {
            Near(FactoryEnemyScaling.TurretHealthFactor(10),3,"HP турели");
            Near(FactoryEnemyScaling.TurretProtectionFactor(10),2,"Кожа/броня турели");
            Near(FactoryEnemyScaling.TurretDamageFactor(10),2.2f,"Урон оружия турели");
            var creature=CreatureCatalog.Load().Creatures.OrderBy(c=>c.actorType,StringComparer.Ordinal).First(c=>NpcCreatureBehaviours.Fixed(ActorTypes.Get(c.actorType).behavior));
            const string level="factory:0";
            var source=creature.spawn.Create(new TrainingPlacement {id="0,20:turret-upgrade",type=creature.actorType,facing=1},level);
            var deadSource=source.Copy();deadSource.id=level+":0,20:dead-turret-upgrade";
            var layout=new NpcLayout {patch=level+":"+FactoryCreatureAdapter.Patch,level=level,actors=new[]{source,deadSource}};
            var old=FactoryEnemyScaling.Roll(104,source.id,20,FactoryEnemyScaling.Unit,source.health);old.version=1;
            var historical=new AttackEvent {weapon=source.weapon,factoryEnemy=FactoryEnemyScaling.Capture(old)};
            var actor=source.Copy();actor.factoryEnemy=old;actor.health=old.maxHealth*.4f;
            var dead=deadSource.Copy();dead.factoryEnemy=FactoryEnemyScaling.Capture(old);dead.factoryEnemy.placement=dead.id;dead.health=0;dead.down=true;
            var world=new WorldState {worldId="turret-scaling-v1-upgrade",seed=104};world.actors.Add(actor);world.actors.Add(dead);world.contentPatches.Add(layout.patch);
            var batch=new RoomContentBatch {npcs=layout};
            Require(RoomContentInstaller.NeedsInstall(world,batch) && RoomContentInstaller.Apply(world,batch),"Профиль турели v1 не мигрирует");
            var current=actor.factoryEnemy;
            Require(current.version==FactoryEnemyScaling.Version && current.kind==FactoryEnemyScaling.Turret && current.enemyLevel==old.enemyLevel && current.rolledChampion==old.rolledChampion && current.seed==old.seed,"Исправление турели перебросило уровень/чемпиона");
            Near(current.maxHealth,source.health*(1+.2f*old.enemyLevel),"Исправленный максимум HP турели");
            Near(actor.health/current.maxHealth,.4f,"Исправление турели вылечило рану");
            Require(dead.health==0 && dead.down,"Исправление уровня воскресило турель");
            var shot=new AttackEvent {weapon=source.weapon,factoryEnemy=FactoryEnemyScaling.Capture(current)};
            Near(FactoryEnemyScaling.AttackFactor(shot),1+.12f*current.enemyLevel,"Снаряд турели не получил исходный рост урона");
            Near(FactoryEnemyScaling.Protection(current),1+.1f*current.enemyLevel,"Защита турели использует общий Unit множитель");
            Near(FactoryEnemyScaling.AttackFactor(historical),1,"Принятый исторический выстрел v1 изменился после миграции");
            Require(!RoomContentInstaller.Apply(world,batch),"Повторный вход повторно усилил турель v2");
            var unscaled=source.Copy();unscaled.factoryEnemy=null;
            var newWorld=new WorldState {worldId="turret-scale-new",seed=104};newWorld.actors.Add(unscaled);newWorld.contentPatches.Add(layout.patch);
            Require(RoomContentInstaller.Apply(newWorld,batch) && unscaled.factoryEnemy.kind==FactoryEnemyScaling.Turret,"Новая турель не выбрала профиль UnitTurret");
        }
        private static void CheckProvenance()
        {
            const string level="factory:0";
            var first=new ActorState {id=level+":0,20:unscaled",actorType="tarakan",anatomy="tarakan",level=level,weapon="",health=100};
            Require(!NpcCreatureBehaviours.Fixed(ActorTypes.Get(first.actorType).behavior),"Provenance fixture требует явный тип обычного существа");
            var second=first.Copy();second.id=level+":1,20:scaled";
            var batch=new RoomContentBatch {npcs=new NpcLayout {patch=level+":"+FactoryCreatureAdapter.Patch,level=level,actors=new[]{first,second}}};
            Action<FactoryEnemyScaleState>[] corruptions={s=>s.placement+="-wrong",s=>s.seed++,s=>s.floor--,s=>s.kind=FactoryEnemyScaling.Trap,
                s=>s.baseHealth+=10,s=>s.maxHealth+=10};
            foreach(var corrupt in corruptions)
            {
                var world=new WorldState {worldId="scaling-provenance",seed=104};world.contentPatches.Add(batch.npcs.patch);
                var beforeFirst=first.Copy();beforeFirst.health=75;world.actors.Add(beforeFirst);
                var invalid=second.Copy();invalid.factoryEnemy=FactoryEnemyScaling.Roll(world.seed,invalid.id,20,FactoryEnemyScaling.Unit,second.health);
                corrupt(invalid.factoryEnemy);world.actors.Add(invalid);
                string before=StateJson.ToJson(world);
                Reject(()=>RoomContentInstaller.Apply(world,batch),"Сохранённый профиль другого источника принят");
                Require(StateJson.ToJson(world)==before && beforeFirst.factoryEnemy==null && beforeFirst.health==75,"Отказ provenance частично изменил мир/здоровье");
                // Validation must run even when there is no pending unscaled entity or version upgrade.
                world.actors.Remove(beforeFirst);before=StateJson.ToJson(world);
                Reject(()=>RoomContentInstaller.NeedsInstall(world,batch),"Профиль ошибочного источника молча пропущен как уже установленный");
                Require(StateJson.ToJson(world)==before,"Проверка необходимости установки изменила профиль");
            }
            var rounded=FactoryEnemyScaling.Roll(104,second.id,20,FactoryEnemyScaling.Unit,second.health);
            rounded.maxHealth*=1.000001f;
            FactoryEnemyScaling.ValidatePlacement(rounded,104,second.id,20,FactoryEnemyScaling.Unit,second.health);
            var saved=new WorldState {worldId="authored-rebalance",seed=104};saved.contentPatches.Add(batch.npcs.patch);
            var wounded=second.Copy();wounded.factoryEnemy=FactoryEnemyScaling.Roll(104,second.id,20,FactoryEnemyScaling.Unit,100);
            wounded.health=wounded.factoryEnemy.maxHealth*.3f;saved.actors.Add(wounded);
            second.health=200; // A later content release rebalances the same source type.
            string beforeRebalance=StateJson.ToJson(saved);
            Require(!RoomContentInstaller.NeedsInstall(saved,batch) && !RoomContentInstaller.Apply(saved,batch) && StateJson.ToJson(saved)==beforeRebalance,
                "Авторский ребаланс переписал максимум/ранение существующего профиля или заблокировал загрузку");
            var newSpawn=new WorldState {worldId="after-authored-rebalance",seed=104};
            Require(RoomContentInstaller.Apply(newSpawn,batch),"Не установлена новая расстановка после ребаланса");
            Require(newSpawn.Find(second.id).factoryEnemy.baseHealth==200,"Новая расстановка не использует текущий авторский HP");
            var badWorld=new WorldState {worldId="wrong-v2-turret-repair",seed=104};badWorld.contentPatches.Add(batch.npcs.patch);
            var bad=second.Copy();bad.factoryEnemy=FactoryEnemyScaling.Roll(104,bad.id,20,FactoryEnemyScaling.Turret,100);bad.factoryEnemy.version=2;
            bad.health=bad.factoryEnemy.maxHealth*.35f;int oldLevel=bad.factoryEnemy.enemyLevel;badWorld.actors.Add(bad);
            Require(RoomContentInstaller.Apply(badWorld,batch) && bad.factoryEnemy.version==3 && bad.factoryEnemy.kind==FactoryEnemyScaling.Unit && bad.factoryEnemy.enemyLevel==oldLevel,
                "Точный ошибочный v2 профиль не исправлен без переброса");
            Near(bad.health/bad.factoryEnemy.maxHealth,.35f,"Исправление ошибочного v2 профиля изменило ранение");
            Require(bad.factoryEnemy.baseHealth==100 && !RoomContentInstaller.Apply(badWorld,batch),"Исправление взяло новый baseline или повторилось");
            var corruptWorld=StateJson.Copy(badWorld);var corruptActor=corruptWorld.actors.Single();
            corruptActor.factoryEnemy.kind=FactoryEnemyScaling.Turret;corruptActor.factoryEnemy.version=2; // maxHP still Unit: not the known old bug.
            Reject(()=>RoomContentInstaller.Apply(corruptWorld,batch),"Несогласованный wrongkind профиль выдан за известную ошибку v2");
        }
        private static void Reject(Action action,string message)
        {try {action();} catch(InvalidOperationException) {return;}throw new InvalidOperationException(message);}
        private static void Near(float actual,float expected,string reason)=>Require(Mathf.Abs(actual-expected)<.0001f,$"{reason}: {actual} != {expected}");
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("Уровни завода: "+reason);}
    }
}
