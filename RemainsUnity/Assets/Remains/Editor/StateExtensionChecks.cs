using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class StateExtensionChecks
    {
        public static void Run()
        {
            foreach(float speed in new[]{1e-29f,-1e-38f,float.Epsilon,float.MaxValue})
            {
                var moving=new ActorState {id="tiny-speed",vx=speed};
                Require(StateJson.Copy(moving).vx==speed,"Полный конечный диапазон float сохраняется без потери значения");
            }
            bool tinyRejected=false;
            try {StateJson.FromJson<ActorState>("{\"mod:tiny\":1e-40}");} catch(InvalidDataException) {tinyRejected=true;}
            Require(tinyRejected,"Неизвестный малый decimal не обнуляется молча");
            var world=new WorldState();
            var actor=new ActorState {id="a",character="a",player=true,connected=true,loadoutVersion=1};
            actor.inventory.Add(SharedInventory.Create("p10",8));world.actors.Add(actor);
            var root=JObject.Parse(StateJson.ToJson(world));
            var payload=new JObject { ["timestamp"]="2026-10-01T12:34:56Z",["large"]=JToken.Parse("922337203685477580812345"),
                ["decimal"]=new JValue(1.234567890123456789m),["nested"]=new JArray("кириллица",true,null,new JObject {["$type"]="Untrusted.Class, Assembly"})};
            root["thirdParty:world"]=payload.DeepClone();root["$type"]="World.Mod.Metadata";
            root["actors"][0]["thirdParty:actor"]=payload.DeepClone();
            root["actors"][0]["inventory"][0]["thirdParty:item"]=payload.DeepClone();
            var decoded=StateJson.FromJson<WorldState>(root.ToString());var copy=decoded.Copy();
            Require(JToken.DeepEquals(copy.extensions["thirdParty:world"],payload) && (string)copy.extensions["$type"]=="World.Mod.Metadata","Неизвестные поля мира, вложенные значения и $type сохранены как данные");
            var owner=copy.actors[0].Copy();owner.health=73;InventoryJournal.Consume(owner,"p10",1);
            Require(InventoryJournal.Merge(copy.actors[0],owner,out var inventory,out _),"Согласование расхода");
            Require(inventory[0].count==7 && JToken.DeepEquals(inventory[0].extensions["thirdParty:item"],payload),"Расход не стирает метаданные предмета");
            owner.inventory=inventory;copy.actors[0]=owner;
            string path=Path.Combine(Path.GetTempPath(),"Remains-extensions-"+Guid.NewGuid().ToString("N"));
            try
            {
                var store=new SaveStore(path);store.SaveWorld(copy);store.SaveCharacter(owner);
                var loaded=store.LoadWorld();var character=store.LoadCharacter();
                Require(loaded.Find("a").health==73 && loaded.Find("a").inventory[0].count==7,"Изменения игры сохраняются рядом с неизвестными полями");
                Require(JToken.DeepEquals(loaded.extensions["thirdParty:world"],payload) && JToken.DeepEquals(character.extensions["thirdParty:actor"],payload) &&
                    JToken.DeepEquals(character.inventory[0].extensions["thirdParty:item"],payload),"Мир и локальный персонаж не теряют дополнительные данные");
                var output=JObject.Parse(StateJson.ToJson(loaded));
                Require(output["actors"][0]["Position"]==null && output["actors"][0]["Velocity"]==null && output["extensions"]==null,"Вычисляемые свойства и служебный словарь не попадают в формат");
            }
            finally {if(Directory.Exists(path)) Directory.Delete(path,true);}
            bool rejected=false;
            try {StateJson.FromJson<ActorState>("{\"id\":\"a\",\"id\":\"b\"}");} catch(InvalidDataException) {rejected=true;}
            Require(rejected,"Повторные ключи не теряются молча");
            rejected=false;try {StateJson.FromJson<WorldState>("{\"schema\":1}");} catch(InvalidDataException) {rejected=true;}
            Require(rejected,"Отсутствующий worldId не заменяется случайным новым миром");
            rejected=false;try {StateJson.FromJson<ActorState>("{\"mod:precise\":0.12345678901234567890123456789012345}");} catch(InvalidDataException) {rejected=true;}
            Require(rejected,"Непредставимое число не округляется молча");
            rejected=false;try {StateJson.FromJson<ActorState>("{\"Health\":3}");} catch(InvalidDataException) {rejected=true;}
            Require(rejected,"Похожее имя поля не перезаписывает известное поле без явного контракта");
            actor.extensions=new System.Collections.Generic.Dictionary<string,JToken> {["health"]=5};
            rejected=false;try {StateJson.ToJson(actor);} catch(Exception failure) {rejected=failure.ToString().Contains("конфликтует");}
            Require(rejected,"Расширение не может записать второй ключ известного поля");
            var future=new WorldState {historyVersion=2};future.ancestors.Add(new WorldAncestor {branchId="unknown",sequence=7});
            rejected=false;try {WorldHistory.BeginContinuation(future);} catch(InvalidDataException) {rejected=true;}
            Require(rejected && future.historyVersion==2 && future.ancestors.Count==1,"Неизвестная история не сбрасывается при открытии продолжения");
            Debug.Log("StateExtensionChecks PASS: неизвестные поля, вложенность, точные числа, копии, расход, сохранения и запрещённые дубликаты.");
        }
        private static void Require(bool valid,string reason) {if(!valid) throw new InvalidOperationException("StateExtensionChecks: "+reason);}
    }
}
