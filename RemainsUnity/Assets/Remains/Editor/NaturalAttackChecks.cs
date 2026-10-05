using System;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class NaturalAttackChecks
    {
        private sealed class ForbiddenWeapon : IDamageRule {public float Calculate(string weapon,float speed)=>throw new InvalidOperationException("Естественная атака попала в оружейное правило.");}
        private sealed class OverrideDamage : INaturalAttackDamageRule {public float Calculate(string type)=>7;}
        public static void Run()
        {
            var source=new ActorState {id="natural-source",actorType="radroach",anatomy="radroach",level="check",health=8,facing=1,owner="one",weapon=null};
            var player=new ActorState {id="natural-player",actorType="pony",anatomy="pony",level="check",player=true,connected=true,owner="one",x=.8f,health=100};
            var other=player.Copy();other.id="natural-other";other.owner="two";other.x=2;
            var world=new WorldState {worldId="natural-check"};world.actors.AddRange(new[]{source,player,other});
            var clear=new RoomLayout {platforms=Array.Empty<RoomSurface>()};
            Require(!NpcAttackRules.TryBegin(source,source) && !source.attackBusy && source.attackId==null,"Естественная атака начала кусать себя.");
            var invalidTarget=player.Copy();invalidTarget.id="";bool badTarget=false;
            try {NpcAttackRules.TryBegin(source,invalidTarget);} catch(InvalidOperationException) {badTarget=true;}
            Require(badTarget && !source.attackBusy && source.attackId==null,"Цель без ID принята или успела изменить действие.");
            Require(NpcAttackRules.TryBegin(source,player) && source.npcWeaponAction.kind==NpcNaturalAttack.Kind && source.weapon==null,"Не начата безоружная атака.");
            string id=source.attackId;float remaining=source.attackRemaining;source.target=other.id;
            var ownership=new OwnershipService();ownership.Reassign(world,new[]{"one","two"});Require(source.owner=="one" && source.attackId==id,"Владелец сменился в середине естественной атаки.");
            source=StateJson.Copy(source);Require(source.npcWeaponAction.naturalAttackType=="radroach" && source.attackRemaining==remaining,"Save/restore потерял действие.");
            Require(NpcAttackRules.Complete(source,player,clear)==null && source.attackBusy,"Подготовка завершилась раньше времени.");
            source.attackRemaining=0;var attack=NpcAttackRules.Complete(source,player,clear);Require(attack!=null && attack.naturalAttackType=="radroach" && attack.weapon=="","Создано фиктивное оружие/потеряно действие.");
            Require(attack.naturalAttackTarget==player.id && StateJson.Copy(attack).naturalAttackTarget==player.id,"Событие или сериализация потеряли адресата укуса.");
            foreach(string invalid in new[]{"",source.id})
            {
                var malformed=StateJson.Copy(attack);malformed.naturalAttackTarget=invalid;bool refused=false;
                try {NpcNaturalAttack.ValidateEvent(malformed);} catch(InvalidOperationException) {refused=true;}
                Require(refused,"Событие укуса допускает отсутствующего или собственного адресата.");
            }
            var neighbor=new ActorState {id="natural-neighbor",actorType="radroach",anatomy="radroach",level="check",health=8,x=.3f};
            world.actors.Add(neighbor);
            var contacts=NpcNaturalAttack.Contacts(world,attack,clear);Require(contacts.Count==1 && contacts[0].id==player.id,"Укус перенаправился на соседнее живое тело.");
            neighbor.down=true;neighbor.health=0;
            contacts=NpcNaturalAttack.Contacts(world,attack,clear);
            Require(contacts.Count==1 && contacts[0].id==player.id,"Труп соседа перехватил адресный укус.");
            Require(!AttackScope.CanAffect(attack,neighbor,true) && !new DamageReceiver(new ForbiddenWeapon()).Apply(neighbor,attack,true),"Адресный укус принял посторонний адресат.");
            neighbor.down=false;neighbor.health=8;
            Require(!new DamageReceiver(new ForbiddenWeapon()).Apply(neighbor,attack,true) && neighbor.health==8 && neighbor.resolvedAttacks.Count==0,"Прямой вызов урона повредил живого соседа.");
            var receiver=new DamageReceiver(new ForbiddenWeapon());Require(receiver.Apply(player,attack,true) && player.health==96 && !receiver.Apply(player,attack,true),"Урон не применён ровно один раз через отдельный контракт.");
            attack.id="override-natural";receiver=new DamageReceiver(new ForbiddenWeapon(),null,null,new OverrideDamage());Require(receiver.Apply(player,attack,true) && player.health==89,"Не заменяется модовое правило естественного урона.");
            var wall=new RoomLayout {platforms=new[]{new RoomSurface {x=.45f,y=0,width=.1f,height=1}}};
            player.x=1.25f; // The victim's hull is behind the wall, not overlapping it.
            Require(NpcNaturalAttack.Contacts(world,attack,wall).Count==0,"Укус проходит через стену.");
            source.attackCooldown=0;source.x=0;source.target=player.id;player.x=3;Require(!NpcAttackRules.TryBegin(source,player),"Контактная атака начинается издалека.");
            player.x=.8f;Require(NpcAttackRules.TryBegin(source,player),"Не начата повторная атака.");world.actors[0]=source;
            player.connected=false;ownership.Reassign(world,new[]{"two"});Require(!source.attackBusy && source.attackId==null && source.owner=="two","Потеря владельца не отменила незавершённую атаку.");
            attack.naturalAttackTarget=other.id;attack.naturalAttackType="missing-natural-type";bool rejected=false;try {receiver.Apply(other,attack,true);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"Неизвестный natural ID скрыт оружейным fallback.");
            Debug.Log("Естественные атаки: PASS — адресная капсула/terrain/contact, сосед и его труп не перехватывают укус, без предмета оружия, отдельный модовый урон, durable дедуп, finish-before-handoff и disconnect-cancel.");
        }
        private static void Require(bool valid,string reason)
        {if(!valid) throw new InvalidOperationException("Естественная атака: "+reason);}
    }
}
