using System;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class ArmoredTrainingTargetChecks
    {
        private sealed class FixedDamage : IDamageRule
        {public float damage;public float Calculate(string weapon,float speed)=>damage;}
        private sealed class InvalidProtection : IHitProtectionRule
        {public float Resolve(ActorState target,AttackEvent attack,float damage)=>float.NaN;}
        public static void Run()
        {
            ActorTypes.Load();
            try
            {
                var asset=AssetDatabase.LoadAssetAtPath<ActorTypeAsset>(ArmoredTrainingTargetAuthoring.AssetPath);
                var definition=asset.CreateDefinition();ActorTypes.Register(definition);
                Require(definition.hitProtection==20 && definition.restoreHealthAfterHit==500 && !definition.combatAi && !definition.bodyInventory,"Неверное определение броневой мишени.");
                var target=new ActorState {id="armored-target",actorType=definition.id,level="hub",health=500};
                var damage=new FixedDamage();var receiver=new DamageReceiver(damage);float total=0;int events=0;
                receiver.Applied+=result=>{total+=result.amount;events++;};
                foreach(var value in new[]{11f,20f,35f})
                {
                    damage.damage=value;var attack=new AttackEvent {id="hit-"+value,attacker="player",level="hub",weapon="pistol"};
                    Require(receiver.Apply(target,attack,true),"Попадание потеряно.");
                    Require(!receiver.Apply(target,attack,true),"Повторно применено попадание, включая полностью поглощённое.");
                }
                Require(target.health==500 && !target.down && events==1 && total==15 && target.damageSequence==1 && target.resolvedAttacks.Count==3,"Урон, восстановление или отображаемый результат броневой мишени неверны.");
                var ordinary=new ActorState {id="ordinary",actorType="training-dummy",level="hub",health=500};
                damage.damage=35;receiver.Apply(ordinary,new AttackEvent {id="ordinary-hit",attacker="player",level="hub",weapon="pistol"},true);
                Require(total==50 && ordinary.health==500,"Обычная мишень ошибочно получила защиту.");
                string before=StateJson.ToJson(target);bool rejected=false;
                try {new DamageReceiver(damage,protection:new InvalidProtection()).Apply(target,new AttackEvent {id="invalid",attacker="player",level="hub",weapon="pistol"},true);}
                catch(InvalidOperationException) {rejected=true;}
                Require(rejected && StateJson.ToJson(target)==before,"Некорректное правило защиты изменило состояние цели.");
                Require(asset.TryGetPrefab(definition,out var prefab),"Нет префаба броневой мишени.");
                var visual=UnityEngine.Object.Instantiate(prefab);
                try {visual.Bind(target);Require(visual.GetComponent<SpriteRenderer>().sprite.texture.width==251,"Потеряна исходная графика мишени.");}
                finally {UnityEngine.Object.DestroyImmediate(visual.gameObject);}
                Debug.Log("Броневая мишень PASS: 11→0, 20→0, 35→15; обычная 35→35, восстановление HP, дедупликация нулевого урона и сохранённый префаб.");
            }
            finally {ActorTypes.Load();}
            ContractChecks.Run();
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
    }
}
