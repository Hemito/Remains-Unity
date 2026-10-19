using System;
using System.Globalization;
using System.Text;
using Remains.Content;
using Remains.RoomFormat;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Domain
{
    [Serializable]
    public sealed class FactoryEnemyScaleState : ExtensibleState
    {
        public int version,seed,floor,enemyLevel,rolledChampion;
        public string placement,kind;
        public float baseHealth,maxHealth;
    }

    // Original Land.newLoc/setLocDif -> Location.createUnit -> Unit/Mine.setLevel.
    // Instance state only; catalog definitions are shared and must never be scaled.
    public static class FactoryEnemyScaling
    {
        public const int Version=2;
        public const string Unit="unit",Trap="trap",Mine="mine",Charge="charge",Turret="turret";
        public static float HealthFactor(int level)=>1+.11f*Nonnegative(level);
        public static float TurretHealthFactor(int level)=>1+.20f*Nonnegative(level);
        public static float TurretProtectionFactor(int level)=>1+.10f*Nonnegative(level);
        public static float TurretDamageFactor(int level)=>1+.12f*Nonnegative(level);
        public static float NaturalDamageFactor(int level)=>1+.07f*Nonnegative(level);
        public static float ProtectionFactor(int level)=>1+.05f*Nonnegative(level);
        public static float MineDamageFactor(int level)=>1+.1f*Math.Max(0,Nonnegative(level)-10);
        private static int Nonnegative(int value)=>value>=0?value:throw new InvalidOperationException("Отрицательный уровень противника: "+value);

        public static FactoryEnemyScaleState Roll(int seed,string placement,int floor,string kind,float baseHealth)
        {
            if(string.IsNullOrWhiteSpace(placement) || floor<0 || floor>20 ||
                kind!=Unit && kind!=Trap && kind!=Mine && kind!=Charge && kind!=Turret || !float.IsFinite(baseHealth) || baseHealth<0)
                throw new InvalidOperationException($"Некорректное масштабирование: id={placement}, floor={floor}, kind={kind}, health={baseHealth}.");
            ulong hash=14695981039346656037UL;
            foreach(byte value in Encoding.UTF8.GetBytes("factory-enemy-v1|"+seed.ToString(CultureInfo.InvariantCulture)+"|"+placement))
                hash=unchecked((hash^value)*1099511628211UL);
            var random=new LayoutRandom(hash);
            // Location.enemyLevel is int: the half-floor locDif is truncated before level jitter.
            double difficulty=floor/2.0;int level=floor/2;
            int champion=random.Unit()<Math.Min(.05,difficulty/100+.02)?random.Range(4)+1:0;
            if(champion==0) level=(int)Math.Floor(level*(1.1-random.Unit()*.4)+.5); // AS Math.round for nonnegative values.
            return new FactoryEnemyScaleState {version=Version,seed=seed,placement=placement,floor=floor,enemyLevel=level,
                rolledChampion=champion,kind=kind,baseHealth=baseHealth,maxHealth=baseHealth*(kind==Turret?TurretHealthFactor(level):kind==Unit || kind==Trap?HealthFactor(level):1)};
        }
        public static void Validate(FactoryEnemyScaleState state)
        {
            if(state==null) return; // Explicit legacy absence means unscaled, until content migration.
            if(state.version<1 || state.version>Version || state.floor<0 || state.floor>20 || state.enemyLevel<0 || state.enemyLevel>11 ||
                state.rolledChampion<0 || state.rolledChampion>4 || string.IsNullOrWhiteSpace(state.placement) ||
                state.kind!=Unit && state.kind!=Trap && state.kind!=Mine && state.kind!=Charge && state.kind!=Turret ||
                state.version==1 && state.kind==Turret ||
                !float.IsFinite(state.baseHealth) || state.baseHealth<0 || !float.IsFinite(state.maxHealth) || state.maxHealth<0)
                throw new InvalidOperationException($"Повреждено масштабирование противника: id={state.placement}, version={state.version}, floor={state.floor}, level={state.enemyLevel}, kind={state.kind}.");
        }
        public static float PreserveHealth(float current,float oldMaximum,float newMaximum)
        {
            if(!float.IsFinite(current) || current<0 || !float.IsFinite(oldMaximum) || oldMaximum<=0 || !float.IsFinite(newMaximum) || newMaximum<=0)
                throw new InvalidOperationException($"Некорректное здоровье при масштабировании: health={current}, oldMax={oldMaximum}, newMax={newMaximum}.");
            return current<=0?0:newMaximum*Mathf.Clamp01(current/oldMaximum);
        }
        public static void ValidatePlacement(FactoryEnemyScaleState state,int seed,string placement,int floor,string kind,float baseHealth)
        {
            Validate(state);if(state==null) return;
            string expectedKind=state.version==1 && kind==Turret?Unit:kind;
            float factor=expectedKind==Turret?TurretHealthFactor(state.enemyLevel):expectedKind==Unit || expectedKind==Trap?HealthFactor(state.enemyLevel):1;
            float maximum=baseHealth*factor;
            // Tolerance covers persisted float round trips, not a changed authored stat profile.
            bool Near(float a,float b)=>Mathf.Abs(a-b)<=.0001f*Mathf.Max(1,Mathf.Abs(b));
            if(state.seed!=seed || state.placement!=placement || state.floor!=floor || state.kind!=expectedKind ||
                !Near(state.baseHealth,baseHealth) || !Near(state.maxHealth,maximum))
                throw new InvalidOperationException($"Сохранённый профиль уровня не соответствует источнику: id={placement}, storedId={state.placement}, seed={state.seed}/{seed}, floor={state.floor}/{floor}, kind={state.kind}/{expectedKind}, baseHP={state.baseHealth}/{baseHealth}, maxHP={state.maxHealth}/{maximum}, version={state.version}.");
        }
        public static float Protection(FactoryEnemyScaleState state)
        {Validate(state);return state?.kind==Turret?TurretProtectionFactor(state.enemyLevel):state!=null && (state.kind==Unit || state.kind==Trap)?ProtectionFactor(state.enemyLevel):1;}
        public static float AttackFactor(AttackEvent attack)
        {
            var state=attack.factoryEnemy;Validate(state);
            if(state==null) return 1;
            if(state.kind==Mine) return MineDamageFactor(state.enemyLevel);
            if(state.kind==Charge) return 1; // UnitDamager.setLevel does not scale fixed tip3 explosion or health.
            if(state.kind==Turret) return TurretDamageFactor(state.enemyLevel); // UnitTurret overrides Unit.setLevel, including ranged weapon damage.
            if(NpcNaturalAttack.IsNatural(attack) || state.kind==Trap) return NaturalDamageFactor(state.enemyLevel);
            // Unit.setLevel scales currentWeapon.damage only for original tip=0 melee.
            return WeaponCatalog.Get(attack.weapon).meleeMotion!=null?NaturalDamageFactor(state.enemyLevel):1;
        }
        public static FactoryEnemyScaleState Capture(FactoryEnemyScaleState source)=>source==null?null:StateJson.Copy(source);
        public static FactoryEnemyScaleState UpgradeTurret(FactoryEnemyScaleState source)
        {
            Validate(source);
            if(source==null || source.version!=1 || source.kind!=Unit || source.baseHealth<=0)
                throw new InvalidOperationException("Нет профиля v1 для исправления турели: "+source?.placement);
            var result=Capture(source);result.version=Version;result.kind=Turret;
            result.maxHealth=result.baseHealth*TurretHealthFactor(result.enemyLevel);
            return result; // Preserve level/champion/seed: the v1 random stream itself was correct.
        }
        public static bool Same(FactoryEnemyScaleState a,FactoryEnemyScaleState b)=>a==null || b==null?a==null && b==null:
            a.version==b.version && a.seed==b.seed && a.floor==b.floor && a.enemyLevel==b.enemyLevel && a.rolledChampion==b.rolledChampion &&
            a.placement==b.placement && a.kind==b.kind && a.baseHealth==b.baseHealth && a.maxHealth==b.maxHealth;
        // Only at launch/current-source validation. In-flight receipts compare the accepted launch instead.
        public static void ValidateCaptured(AttackEvent attack,ActorState source)
        {
            Validate(attack?.factoryEnemy);Validate(source?.factoryEnemy);
            if(attack==null || source==null || source.id!=attack.attacker || !Same(attack.factoryEnemy,source.factoryEnemy))
                throw new InvalidOperationException($"Профиль уровня атаки не совпадает с источником: attack={attack?.id}, actor={attack?.attacker}, source={source?.id}, level={attack?.factoryEnemy?.enemyLevel}, expected={source?.factoryEnemy?.enemyLevel}.");
        }

        public static int PlacementFloor(string id,string level)
        {
            string prefix=level+":";
            if(id==null || level==null || !id.StartsWith(prefix,StringComparison.Ordinal))
                throw new InvalidOperationException("ID противника не принадлежит заводу: "+id);
            int end=id.IndexOf(':',prefix.Length),comma=id.IndexOf(',',prefix.Length);
            if(end<0 || comma<0 || comma>end || !int.TryParse(id.Substring(prefix.Length,comma-prefix.Length),NumberStyles.None,CultureInfo.InvariantCulture,out int column) ||
                !int.TryParse(id.Substring(comma+1,end-comma-1),NumberStyles.None,CultureInfo.InvariantCulture,out int floor) || column<0 || column>3 || floor<0 || floor>20)
                throw new InvalidOperationException("ID противника не содержит клетку мегазавода 4×21: "+id);
            return floor;
        }
    }
}
