using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    public readonly struct KnockdownContext
    {
        public readonly float previousHealth,remainingHealth;
        public readonly bool player;
        public KnockdownContext(float previousHealth,float remainingHealth,bool player)
        {this.previousHealth=previousHealth;this.remainingHealth=remainingHealth;this.player=player;}
    }
    public interface IKnockdownRule {bool ShouldEnter(KnockdownContext context);}
    public sealed class ZeroHealthKnockdownRule : IKnockdownRule
    {public bool ShouldEnter(KnockdownContext context)=>context.remainingHealth<=0;}

    public readonly struct PartyMemberStatus
    {
        public readonly string id,level;
        public readonly bool down;
        public readonly float health;
        public PartyMemberStatus(ActorState actor) {id=actor.id;level=actor.level;down=actor.down;health=actor.health;}
    }
    public readonly struct DefeatRecovery
    {
        public readonly string level;
        public readonly Vector2 position;
        public readonly float health;
        public readonly bool removePeriodicInjuries;
        public DefeatRecovery(string level,Vector2 position,float health,bool removePeriodicInjuries)
        {this.level=level;this.position=position;this.health=health;this.removePeriodicInjuries=removePeriodicInjuries;}
    }
    public interface IPartyDefeatRule
    {
        bool IsDefeated(IReadOnlyList<PartyMemberStatus> connectedPlayers);
        DefeatRecovery Recovery(PartyMemberStatus member,int slot);
        string Notice {get;}
    }
    public sealed class HubPartyDefeatRule : IPartyDefeatRule
    {
        public bool IsDefeated(IReadOnlyList<PartyMemberStatus> players)
        {
            if(players.Count==0) return false;
            foreach(var player in players) if(!player.down) return false;
            return true;
        }
        public DefeatRecovery Recovery(PartyMemberStatus member,int slot)
            =>new("hub",CharacterPlacement.HubPosition(slot),1,true);
        public string Notice=>"Вся группа без сознания. Возвращение в хаб с 1 HP.";
    }
    // Session-scoped composition; transport and persistence remain outside the rules.
    public sealed class GameplayRuleSet
    {
        public IDamageRule Damage {get;}
        public IKnockdownRule Knockdown {get;}
        public IPartyDefeatRule PartyDefeat {get;}
        public ILootRule Loot {get;}
        public ILevelPauseRule Pause {get;}
        public IDepositRule Deposit {get;}
        public GameplayRuleSet(IDamageRule damage,IKnockdownRule knockdown,IPartyDefeatRule partyDefeat,ILootRule loot=null,ILevelPauseRule pause=null,IDepositRule deposit=null)
        {
            Damage=damage??throw new ArgumentNullException(nameof(damage));
            Knockdown=knockdown??throw new ArgumentNullException(nameof(knockdown));
            PartyDefeat=partyDefeat??throw new ArgumentNullException(nameof(partyDefeat));
            Loot=loot??new NearbyLootRule();
            Pause=pause??new CooperativePauseRule();Deposit=deposit??new NearbyDepositRule();
        }
        public static GameplayRuleSet CreateDefault()=>new(new PrototypeDamageRule(),new ZeroHealthKnockdownRule(),new HubPartyDefeatRule());
    }
}
