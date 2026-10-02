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
            var actor=new ActorState {level="hub",generation=2,lifecycleSequence=3};
            var message=new ReloadSoundMessage {level="hub",generation=2,lifecycle=3};
            Require(SessionReloadAudio.Matches(actor,message),"Текущее воплощение");
            actor.generation++;Require(!SessionReloadAudio.Matches(actor,message),"Передача владельца");
            actor.generation--;actor.lifecycleSequence++;Require(!SessionReloadAudio.Matches(actor,message),"Новое состояние жизни");
            actor.lifecycleSequence--;actor.level="factory:0";Require(!SessionReloadAudio.Matches(actor,message),"Другой уровень");
        }
        private static void Require(bool condition,string label)
        {if(!condition) throw new InvalidOperationException("Звук перезарядки: "+label);}
    }
}
