using System;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using Remains.Networking;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonyHeadPersistenceChecks
    {
        public static void Run()
        {
            PonyHeadCatalog.Load();WeaponCatalog.Load();
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../.reference/tests/pony-head-persistence-"+Guid.NewGuid().ToString("N")));
            var world=new WorldState();
            for(int i=0;i<PonyHeadCatalog.Styles.Count;i++)
            {
                string head=PonyHeadCatalog.Styles[i].id;
                var actor=new ActorState {id="head-player-"+i,character="Форма головы "+i,player=true,level="hub",
                    appearance=new CharacterAppearance {version=CharacterAppearance.CurrentVersion,headStyleId=head,hairStyle=5,eyeStyle=6,
                        fur=0xCCBB88,hair=0x551188,secondaryHair=0xEE2288,eyes=0x2288FF,magic=0xFFAA00,secondaryHairEnabled=true}};
                PlayerStarterInventory.Give(actor);actor.weaponContent=WeaponCatalog.ManifestCopy();
                var store=new SaveStore(Path.Combine(directory,"profile-"+i));store.SaveCharacter(actor);
                RequireAppearance(actor,store.LoadCharacter(),"личный профиль / "+head);
                var copy=actor.Copy();RequireAppearance(actor,copy,"независимая копия / "+head);
                copy.appearance.headStyleId="head.test.unregistered";
                Require(actor.appearance.headStyleId==head,"копия изменила выбор исходного персонажа / "+head);
                var join=new JoinMessage {character=actor.character,characterUtf8=JoinCharacter.Encode(StateJson.ToJson(actor))};
                RequireAppearance(actor,StateJson.FromJson<ActorState>(JoinCharacter.Decode(join.characterUtf8)),"JSON подключения / "+head);
                var message=new ActorMessage {json=StateJson.ToJson(actor)};
                RequireAppearance(actor,StateJson.FromJson<ActorState>(message.json),"JSON состояния другого игрока / "+head);
                world.actors.Add(actor);
            }
            var worldStore=new SaveStore(Path.Combine(directory,"world"));worldStore.SaveWorld(world);
            var loadedWorld=worldStore.LoadWorld();
            foreach(var actor in world.actors) RequireAppearance(actor,loadedWorld.actors.Single(value=>value.id==actor.id),"мир / "+actor.id);

            const string oldAppearance="{\"version\":1,\"hairStyle\":5,\"eyeStyle\":6,\"fur\":13417352,\"hair\":5575048,\"secondaryHair\":15606408,\"eyes\":2263295,\"magic\":16755200,\"secondaryHairEnabled\":true,\"head.test.mod\":{\"preserved\":true}}";
            var old=StateJson.FromJson<CharacterAppearance>(oldAppearance);
            var expected=StateJson.Copy(old);expected.version=CharacterAppearance.CurrentVersion;
            var oldActor=new ActorState {id="legacy-head",character="Старый профиль",player=true,level="hub",appearance=old};
            PlayerStarterInventory.Give(oldActor);oldActor.weaponContent=WeaponCatalog.ManifestCopy();
            // Recreate an actual v1 source with no head field; keep its original bytes for the load check.
            string legacySource=StateJson.ToJson(oldActor);
            var legacyObject=Newtonsoft.Json.Linq.JObject.Parse(legacySource);
            legacyObject["appearance"].Replace(Newtonsoft.Json.Linq.JObject.Parse(oldAppearance));
            legacySource=legacyObject.ToString(Newtonsoft.Json.Formatting.None);
            var legacyStore=new SaveStore(Path.Combine(directory,"legacy"));
            string path=Path.Combine(legacyStore.CharacterDirectoryPath,"character.json");File.WriteAllText(path,legacySource);
            var upgraded=legacyStore.LoadCharacter();
            Require(upgraded.appearance.version==CharacterAppearance.CurrentVersion && upgraded.appearance.headStyleId==CharacterAppearance.OriginalHeadStyleId,
                "старый профиль не получил исходную голову и текущую версию");
            Require(StateJson.ToJson(upgraded.appearance)==StateJson.ToJson(expected),"миграция изменила старые формы, цвета или дополнительные данные мода");
            Require(File.ReadAllText(path)==legacySource,"чтение миграции заменило исходный старый профиль");
            legacyStore.SaveCharacter(upgraded);RequireAppearance(upgraded,legacyStore.LoadCharacter(),"сохранение после миграции v1");
            var invalid=new ActorState {id="unknown-head",appearance=new CharacterAppearance {version=CharacterAppearance.CurrentVersion,headStyleId="head.test.unregistered"}};
            bool rejected=false;try {PonyAppearance.ValidateSettings(invalid.appearance,invalid.id);} catch(InvalidOperationException) {rejected=true;}
            Require(rejected && invalid.appearance.headStyleId=="head.test.unregistered","неизвестная форма принята или незаметно заменена");
            Debug.Log($"Формы головы: PASS — {PonyHeadCatalog.Styles.Count} стабильных ID в копиях, JSON подключения/другого игрока, профилях и мире; v1 без нового поля сохранён до явной записи, цвета и данные мода сохранены, неизвестный ID отклонён.");
        }
        private static void RequireAppearance(ActorState expected,ActorState actual,string context)
        {
            Require(actual!=null && StateJson.ToJson(actual.appearance)==StateJson.ToJson(expected.appearance),"внешность потеряна / "+context);
        }
        private static void Require(bool condition,string context)
        {if(!condition) throw new InvalidOperationException("Сохранение формы головы: "+context+".");}
    }
}
