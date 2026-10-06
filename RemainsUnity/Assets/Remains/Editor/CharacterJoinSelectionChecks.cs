using System;
using System.IO;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class CharacterJoinSelectionChecks
    {
        public static void Run(bool log=true)=>RunAt(Path.Combine(Path.GetTempPath(),"remains-character-selection-"+Guid.NewGuid().ToString("N")),log);
        public static void RunAt(string root,bool log=true)
        {
            var libraries=new SaveLibraries(root,"pony-one");
            var one=libraries.ForCharacter("pony-one");var two=libraries.ForCharacter("Роза");
            string personal=StateJson.ToJson(new ActorState {id="player:Роза",character="Роза",player=true,health=73});
            File.WriteAllText(Path.Combine(two.CharacterDirectoryPath,"character.json"),personal);
            var selected=new CharacterJoinSelection("Роза",two);
            selected.RequireCurrent("Роза",two);
            var actor=new ActorState {id="player:Роза",character="Роза",player=true,connected=true,owner="connection:4"};
            var world=new WorldState();world.actors.Add(actor);
            Require(ReferenceEquals(selected.RequireWelcome(world,actor.id,actor.owner),actor),"Правильный Welcome не принят");
            Reject(()=>selected.RequireCurrent("pony-one",one),"Изменение выбранного персонажа после Join не отклонено");
            Reject(()=>selected.RequireCurrent("Роза",new SaveStore(Path.Combine(root,"other-profile"),two.WorldDirectoryPath)),"Подмена profile root не отклонена");
            Reject(()=>selected.RequireCurrent("Роза",new SaveStore(two.CharacterDirectoryPath,Path.Combine(root,"other-worlds"))),"Подмена world library не отклонена");
            Reject(()=>selected.RequireWelcome(world,"player:pony-one",actor.owner),"Чужой actor ID не отклонён");
            actor.character="pony-one";Reject(()=>selected.RequireWelcome(world,actor.id,actor.owner),"Чужой character при правильном actor ID не отклонён");actor.character="Роза";
            Reject(()=>selected.RequireWelcome(world,actor.id,"connection:5"),"Чужой owner не отклонён");
            actor.connected=false;Reject(()=>selected.RequireWelcome(world,actor.id,actor.owner),"Отключённое тело не принято за joined actor");actor.connected=true;
            actor.player=false;Reject(()=>selected.RequireWelcome(world,actor.id,actor.owner),"NPC не принят за selected character");actor.player=true;
            Reject(()=>CharacterJoinSelection.RequireCanChange(true,false),"Выбор в admission/lobby/active не запрещён");
            Reject(()=>CharacterJoinSelection.RequireCanChange(false,true),"Выбор во время фоновой подготовки не запрещён");
            CharacterJoinSelection.RequireCanChange(false,false);
            // The real content gate already rejects a mismatching source before any asset lookup.
            var gate=new CharacterContentGate();
            Reject(()=>gate.TryPrepare(two,"pony-one",false,out _),"CharacterContentGate принял файл другого персонажа");
            Require(File.ReadAllText(Path.Combine(two.CharacterDirectoryPath,"character.json"))==personal && !File.Exists(Path.Combine(one.CharacterDirectoryPath,"character.json")),
                "Rejected identity изменил личный файл или записал pony-one");
            actor.id="mod:existing-player-body";selected.RequireWelcome(world,actor.id,actor.owner);
            if(log) Debug.Log("Выбор персонажа: PASS — frozen identity/profile/library, admission/lobby guards, строгий Welcome до записи, чужие ID/owner, неизменные файлы. Runtime wiring проверяется отдельно.");
        }
        private static void Reject(Action action,string reason)
        {bool rejected=false;try {action();} catch(Exception error) when(error is InvalidDataException || error is InvalidOperationException) {rejected=true;}Require(rejected,reason);}
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("Выбор персонажа: "+reason);}
    }
}
