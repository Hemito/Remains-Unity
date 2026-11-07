using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Serialization;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    public static class ValidatedStateCopyChecks
    {
        public static void RunBatch()
        {
            try {Run();EditorApplication.Exit(0);}
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }

        public static void Run()
        {
            var actor=new ActorState {
                id="check:copy",appearance=new CharacterAppearance(),hold=new ActorHoldState(),
                weaponContent=new Remains.Content.ContentManifest(),weaponRecoil=new WeaponRecoilPose(),
                cosmeticEquipment=new CosmeticEquipmentState(),
                inventory=new List<ItemStack> {new() {id="weapon",definition="pistol",count=1,
                    weaponState=new WeaponItemState {rounds=3,extensions=new Dictionary<string,JToken> {{"mod",JObject.Parse("{\"count\":7}")}}}}},
                magazines=new List<WeaponMagazine> {new() {weapon="pistol",rounds=9}},
                inventoryUses=new List<InventoryUse> {new() {id="use"}},
                weaponShortcuts=new List<WeaponShortcut> {new()},
                injuries=new List<InjuryState> {new() {id="injury"}},
                extensions=new Dictionary<string,JToken> {
                    {"mod",JObject.Parse("{\"array\":[1,1.234567890123456789,9223372036854775808,null,{\"flag\":true}]}")},
                    {"empty",JValue.CreateNull()}}
            };
            actor.weaponContent.packs.Add(new Remains.Content.ContentPackRecord {id="pack",dependencies=new[]{"base"}});
            actor.weaponContentHistory.Add(new Remains.Content.ContentManifest());
            actor.inventoryAliases.Add("old","weapon");actor.resolvedAttacks.Add("attack");actor.resolvedExplosions.Add("explosion");
            // Canonical graphs come from the reader, including its numeric-token normalization.
            actor=StateJson.FromJson<ActorState>(StateJson.ToJson(actor));
            actor.crouchPressSeconds=4;actor.poseCorrectionRemaining=2;actor.inventoryReservations["weapon"]=1;
            string canonical=StateJson.ToJson(actor);
            var expected=StateJson.FromTrustedJson<ActorState>(canonical);
            var originalPieces=Remains.Persistence.ActorText.Pieces.Of(expected);
            var nulled=StateJson.FromTrustedJson<ActorState>(canonical);
            nulled.weaponContent=null;nulled.weaponContentHistory=null;nulled.inventory=null;nulled.inventoryUses=null;nulled.inventoryAliases=null;
            var nullPieces=Remains.Persistence.ActorText.Pieces.Of(nulled);
            var readNull=Remains.Persistence.ActorText.Read(nullPieces.Template,originalPieces.Cold,_=>false,expected);
            Require(StateJson.ToJson(readNull)==StateJson.ToJson(nulled),"Холодный кеш подменил literal null шаблона.");
            var readRestored=Remains.Persistence.ActorText.Read(originalPieces.Template,originalPieces.Cold,_=>false,readNull);
            Require(StateJson.ToJson(readRestored)==canonical,"Возврат холодного поля из null потерял сохранённый текст.");
            var copy=ValidatedStateCopy.Copy(actor);
            Require(StateJson.ToJson(copy)==StateJson.ToJson(expected),"Копия отличается от JSON-контракта.");
            Require(copy.crouchPressSeconds==0 && copy.poseCorrectionRemaining==0 && copy.inventoryReservations.Count==0,
                "Копия перенесла несериализуемое состояние.");
            AssertDetached(actor,copy,"actor",0);
            // Mutate both directions; cached canonical state and an independently produced live copy stay intact.
            copy.inventory[0].count=99;copy.inventory[0].weaponState.extensions["mod"]["count"]=12;
            copy.weaponContent.packs[0].dependencies[0]="changed";copy.inventoryAliases["old"]="changed";
            copy.extensions["mod"]["array"][4]["flag"]=false;copy.perception.remainingSeconds=40;
            copy.appearance.hairStyle=99;copy.resolvedAttacks.Clear();copy.weaponContentHistory.Clear();
            Require(StateJson.ToJson(actor)==canonical,"Изменение живой копии повредило каноническое состояние.");
            var second=ValidatedStateCopy.Copy(actor);string secondText=StateJson.ToJson(second);
            actor.inventory.Clear();actor.extensions.Clear();actor.weaponContent.packs.Clear();
            Require(StateJson.ToJson(second)==secondText,"Изменение источника повредило отделённую копию.");
            bool rejected=false;
            try {ValidatedStateCopy.Copy<object>(new object());} catch(InvalidOperationException) {rejected=true;}
            Require(rejected,"Неподдерживаемый объект скопирован без явного отказа.");
            CheckImpactContactTick();
            Debug.Log("Копирование проверенного состояния: PASS — JSON-контракт, вложенные объекты, числовые extensions, transient defaults, изоляция.");
        }

        // A box that hit someone keeps the contact in its journal; the next world tick carries it to every client.
        private static void CheckImpactContactTick()
        {
            var host=new WorldState {worldId="check:tick"};
            var box=new PropState {id="check:box",level="hub",definition="box",generation=1};
            host.props.Add(box);
            WorldChanges.Track(host);
            var composer=new Remains.Persistence.WorldTickComposer();composer.Next(host,part=>WorldChanges.IsMarked(host,part));
            var client=StateJson.FromJson<WorldState>(StateJson.ToJson(host));
            box.impacts=new PropImpactJournal();
            box.impacts.contacts.Add(new PropImpactContact {id="contact",target="check:actor",generationBefore=1,generationAfter=2,
                source=new PropImpactSample {id="sample",prop=box.id,generation=1}});
            box.generation=2;WorldChanges.Mark(host,box);host.revision++;
            var tick=composer.Next(host,part=>WorldChanges.IsMarked(host,part));WorldChanges.Clear(host);
            var live=Remains.Persistence.WorldTickLists.ShallowCopy(client);
            Remains.Persistence.WorldTickApplier.Apply(live,Remains.Persistence.WorldTickApplier.Parse(live,tick),ValidatedStateCopy.Copy);
            Require(StateJson.ToJson(live)==StateJson.ToJson(host),"Клиент собрал другой мир после удара ящика.");
            Require(live.props[0].impacts.contacts.Count==1 && !ReferenceEquals(live.props[0].impacts.contacts[0],box.impacts.contacts[0]),
                "Запись об ударе ящика не отделена от источника.");
        }

        private static void AssertDetached(object source,object copy,string path,int depth)
        {
            Require(depth<=128,"Цикл в проверке копии: "+path);
            if(source==null || copy==null) {Require(source==null && copy==null,"Разное null: "+path);return;}
            var type=source.GetType();
            Require(type==copy.GetType(),"Разные типы: "+path);
            if(type.IsValueType || source is string) return;
            Require(!ReferenceEquals(source,copy),"Общая изменяемая ссылка: "+path);
            if(source is JToken token)
            {
                var other=(JToken)copy;Require(JToken.DeepEquals(token,other),"Разные токены: "+path);
                using var a=token.Children().GetEnumerator();using var b=other.Children().GetEnumerator();
                while(a.MoveNext()) {Require(b.MoveNext(),"Разное число токенов: "+path);AssertDetached(a.Current,b.Current,path+"/token",depth+1);}
                Require(!b.MoveNext(),"Лишний токен: "+path);return;
            }
            if(source is IDictionary dictionary)
            {
                var other=(IDictionary)copy;Require(dictionary.Count==other.Count,"Разный размер словаря: "+path);
                foreach(DictionaryEntry pair in dictionary) AssertDetached(pair.Value,other[pair.Key],path+"/"+pair.Key,depth+1);
                return;
            }
            if(source is IList list)
            {
                var other=(IList)copy;Require(list.Count==other.Count,"Разный размер списка: "+path);
                for(int i=0;i<list.Count;i++) AssertDetached(list[i],other[i],path+"/"+i,depth+1);
                return;
            }
            foreach(var field in type.GetFields(BindingFlags.Instance|BindingFlags.Public))
                if(!field.IsNotSerialized) AssertDetached(field.GetValue(source),field.GetValue(copy),path+"/"+field.Name,depth+1);
        }
        private static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
    }
}
