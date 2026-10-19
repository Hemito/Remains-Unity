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
            CheckFloors();CheckMigration();CheckDamage();
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
        private static void Reject(Action action,string message)
        {try {action();} catch(InvalidOperationException) {return;}throw new InvalidOperationException(message);}
        private static void Near(float actual,float expected,string reason)=>Require(Mathf.Abs(actual-expected)<.0001f,$"{reason}: {actual} != {expected}");
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("Уровни завода: "+reason);}
    }
}
