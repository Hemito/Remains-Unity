using System;
using Remains.Domain;
using Remains.Networking;

namespace Remains.Editor
{
    public static class ReloadAudioChecks
    {
        public static void Run()
        {
            var gate=new ReloadSoundGate();
            Require(gate.Accept("a","first",true),"Первое начало");
            Require(!gate.Accept("a","first",true),"Дубликат начала");
            Require(gate.Accept("a","second",true),"Новая перезарядка");
            Require(!gate.Accept("a","first",false),"Старая отмена не прерывает новую перезарядку");
            Require(!gate.Accept("b","second",false),"Чужая отмена");
            Require(gate.Accept("a","second",false),"Отмена текущей перезарядки");
            Require(!gate.Accept("a","second",false),"Дубликат отмены");
            Require(!gate.Accept("a","second",true),"Повтор начала после отмены");
            var actor=new ActorState {id="player:a",level="hub",generation=2,lifecycleSequence=3,
                weapon="pistol",equippedWeaponItem="pistol:a",weaponSelectionRevision=4};
            var message=new ReloadSoundMessage {actor=actor.id,level="hub",generation=2,lifecycle=3,
                started=true,weapon="pistol",weaponItem="pistol:a",selectionRevision=4};
            Require(SessionReloadAudio.Matches(actor,message),"Текущее воплощение");
            actor.equippedWeaponItem="pistol:b";Require(!SessionReloadAudio.Matches(actor,message),"Другой экземпляр того же оружия");
            actor.equippedWeaponItem="pistol:a";actor.weaponSelectionRevision++;
            Require(!SessionReloadAudio.Matches(actor,message),"Возврат к тому же экземпляру не возобновляет старое начало");
            message.started=false;Require(SessionReloadAudio.Matches(actor,message),"Отмена своего token допустима после смены выбора");
            message.started=true;actor.weaponSelectionRevision--;
            actor.weapon="energy";Require(!SessionReloadAudio.Matches(actor,message),"Другое определение оружия");actor.weapon="pistol";
            actor.generation++;Require(!SessionReloadAudio.Matches(actor,message),"Передача владельца");
            actor.generation--;actor.lifecycleSequence++;Require(!SessionReloadAudio.Matches(actor,message),"Новое состояние жизни");
            actor.lifecycleSequence--;actor.level="factory:0";Require(!SessionReloadAudio.Matches(actor,message),"Другой уровень");
        }
        private static void Require(bool condition,string label)
        {if(!condition) throw new InvalidOperationException("Звук перезарядки: "+label);}
    }
}
