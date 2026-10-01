using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class GameplayRuleChecks
    {
        private sealed class FixedDamage : IDamageRule {public float amount=7;public float Calculate(string weapon,float speed)=>amount;}
        private sealed class EarlyDown : IKnockdownRule {public bool ShouldEnter(KnockdownContext context)=>context.remainingHealth<95;}
        private sealed class CustomDefeat : IPartyDefeatRule
        {
            public bool invalid;
            public string Notice=>"Тестовое восстановление";
            public bool IsDefeated(IReadOnlyList<PartyMemberStatus> players)=>true;
            public DefeatRecovery Recovery(PartyMemberStatus member,int slot)=>new("factory:0",new Vector2(slot,2),invalid && slot==1?float.NaN:27,false);
        }
        public static void Run()
        {
            var amount=new FixedDamage();var receiver=new DamageReceiver(amount,new EarlyDown());
            var actor=new ActorState {id="victim",player=true};
            var attack=new AttackEvent {id="hit",weapon="pistol",level=actor.level};
            Require(receiver.Apply(actor,attack,true) && actor.health==93 && actor.down,"Заменяемое правило нокдауна вызвано до нуля HP");
            Require(!receiver.Apply(actor,attack,true) && actor.health==93,"Дедупликация принадлежит обработчику, не моду");
            actor.down=false;attack.id="invalid";amount.amount=float.NaN;
            string before=StateJson.ToJson(actor);
            Reject(()=>receiver.Apply(actor,attack,true));Require(StateJson.ToJson(actor)==before,"Некорректный урон не меняет состояние");
            amount.amount=7;Require(receiver.Apply(actor,attack,true),"Ошибка не поглощает ID атаки");
            var world=new WorldState();actor.connected=true;world.actors.Add(actor);
            var second=new ActorState {id="second",player=true,connected=true,down=true};world.actors.Add(second);
            var offline=new ActorState {id="offline",player=true,down=true,health=18};world.actors.Add(offline);
            actor.injuries.Add(new InjuryState {id="test",periodicDamage=true});
            var policy=new CustomDefeat {invalid=true};before=StateJson.ToJson(world);
            Reject(()=>PartyRules.ResolveDefeat(world,policy));Require(StateJson.ToJson(world)==before,"Ошибка второго результата не применяет первый");
            policy.invalid=false;
            Require(PartyRules.ResolveDefeat(world,policy) && actor.health==27 && actor.level=="factory:0" && actor.injuries.Count==1 &&
                second.x==1 && offline.health==18 && offline.down,"Правило меняет место/HP/травмы, отключённое тело не затронуто");
            Debug.Log("GameplayRuleChecks PASS: замена урона/нокдауна/поражения, дедупликация и атомарная проверка результатов.");
        }
        private static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException("GameplayRuleChecks: "+reason);}
        private static void Reject(Action action) {try {action();} catch(InvalidOperationException) {return;}throw new Exception("Ожидалась ошибка правила.");}
    }
}
