using System;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class NpcPerceptionChecks
    {
        public static void Run()
        {
            // Synthetic units and timings test the contract; these are not original-game measurements.
            var settings=new NpcPerceptionSettings {sightDistance=12,closeDistance=1,recognitionSeconds=.3f,
                memorySeconds=4,hearingDistance=8,eyeHeight=.8f,targetHeight=.8f};
            var perception=new NpcPerception(settings);var memory=new NpcPerceptionMemory();
            var npc=new ActorState {id="npc",owner="owner-a",facing=1};
            var a=new ActorState {id="a",player=true,connected=true,owner="owner-a",x=5};
            var b=new ActorState {id="b",player=true,connected=true,owner="owner-b",x=7};
            var subjects=new[]{a,b};bool Clear(Vector2 from,Vector2 to)=>true;
            perception.Step(npc,memory,subjects,Clear,.1f);
            Require(npc.target==null && !memory.hasKnownPosition,"Первый взгляд мгновенно подтвердил цель.");
            perception.Step(npc,memory,subjects,Clear,.21f);
            Require(npc.target==a.id && memory.KnownPosition==a.Position,"Непрерывный контакт не подтвердил ближайшую цель.");

            // Losing sight must retain a snapshot, not follow an invisible actor's new coordinates.
            var last=memory.KnownPosition;a.x=6;
            perception.Step(npc,memory,subjects,(_,_)=>false,.1f);
            Require(npc.target==null && memory.hasKnownPosition && memory.KnownPosition==last,"Поиск читает скрытое положение игрока.");
            perception.Step(npc,memory,subjects,(_,_)=>false,4);
            Require(!memory.hasKnownPosition,"Память контакта не закончилась.");
            a.x=-4;b.level="hub";
            perception.Step(npc,memory,subjects,Clear,1);
            Require(npc.target==null,"Взгляд обнаружил дальнюю цель позади или на другом уровне.");
            a.x=-.5f;perception.Step(npc,memory,subjects,Clear,.31f);
            Require(npc.target==a.id,"Близкий контакт позади не обнаружен.");
            a.down=true;perception.Step(npc,memory,subjects,Clear,.1f);
            Require(npc.target==null,"Лежащий игрок остался боевой целью.");
            Require(perception.Hear(npc,memory,npc.level,new Vector2(3,0)) && npc.target==null && memory.knownX==3,
                "Шум должен дать место поиска без точной цели.");
            Require(!perception.Hear(npc,memory,"hub",Vector2.zero) && !perception.Hear(npc,memory,npc.level,new Vector2(20,0)),
                "Слух пересёк уровень или дальность.");

            a.down=false;a.x=5;b.level=npc.level;b.x=7;
            perception.Step(npc,memory,subjects,Clear,.31f);
            NpcWeaponTestItems.Equip(npc);NpcAttackRules.Begin(npc,a);string attack=npc.attackId;
            b.x=2;perception.Step(npc,memory,subjects,Clear,.31f);
            Require(npc.target==b.id && npc.attackTarget==a.id && npc.attackId==attack && npc.attackBusy,
                "Новая обнаруженная цель переписала уже начатую атаку.");
            var restored=StateJson.FromJson<NpcPerceptionMemory>(StateJson.ToJson(memory));
            var restoredNpc=npc.Copy();restoredNpc.owner="owner-b";restoredNpc.generation++;
            Require(restored.KnownPosition==memory.KnownPosition && restored.remainingSeconds==memory.remainingSeconds &&
                restored.exposureSeconds==memory.exposureSeconds && restored.candidate==memory.candidate,
                "Сериализация потеряла состояние восприятия для передачи владельцу.");
            var restoredPosition=restored.KnownPosition;
            b.x=10;perception.Step(restoredNpc,restored,subjects,(_,_)=>false,.1f);
            Require(restoredNpc.target==null && restored.KnownPosition==restoredPosition,"Новый владелец получил всеведение после потери контакта.");
            var frozen=StateJson.ToJson(restored);perception.Step(restoredNpc,restored,subjects,Clear,0);
            Require(frozen==StateJson.ToJson(restored),"Нулевой шаг изменил память.");
            settings.sightDistance=float.NaN;bool rejected=false;
            try {new NpcPerception(settings);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"Некорректные настройки зрения приняты.");
            Debug.Log("NpcPerceptionChecks PASS: взгляд, перекрытие, реакция, память, шум, цель атаки, сериализация и остановка времени.");
        }
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
