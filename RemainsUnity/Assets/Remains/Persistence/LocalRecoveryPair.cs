using System;
using System.IO;
using Remains.Domain;
using Remains.Serialization;

namespace Remains.Persistence
{
    // A local publication intent, not a cross-peer transaction or branch merge.
    // The stored paths are bindings only: every write uses the configured store.
    internal static class LocalRecoveryPair
    {
        private const int Version=1;
        [Serializable]
        private sealed class Intent : ExtensibleState
        {
            public int version;
            public bool? publishWorld;
            public string characterDirectory,worldDirectory,worldId,actorId,characterId;
            public string worldJson,characterJson,worldSha256,characterSha256;
            public string previousWorldSha256,previousCharacterSha256;
        }
        private sealed class Validated
        {
            public Intent intent;
            public RecoverySnapshot snapshot;
        }
        internal static string PendingPath(SaveStore store)=>Path.Combine(store.CharacterDirectoryPath,"RecoveryPair","pending.json");
        internal static void Save(SaveStore store,RecoverySnapshot snapshot,string characterJson,bool publishWorld,Action<string> writeCharacter)
        {
            if(snapshot==null) throw new ArgumentNullException(nameof(snapshot));
            var intent=new Intent {version=Version,publishWorld=publishWorld,
                characterDirectory=store.CharacterDirectoryPath,worldDirectory=store.WorldDirectoryPath,
                worldId=snapshot.WorldId,worldJson=snapshot.Json,worldSha256=snapshot.Id,
                characterJson=characterJson,characterSha256=RecoverySnapshot.Fingerprint(characterJson)};
            var actor=StateJson.FromJson<ActorState>(characterJson);intent.actorId=actor?.id;intent.characterId=actor?.character;
            // Invalid new data cannot cause even an older pending publication.
            var validated=Validate(store,intent);
            using var characterAccess=SaveAccess.Enter(store.CharacterDirectoryPath);
            RecoverLocked(store,writeCharacter);
            using var worldAccess=SaveAccess.Enter(store.WorldDirectoryPath);
            RequireArchive(store,validated.snapshot,publishWorld);
            intent.previousCharacterSha256=ExistingHash(CharacterPath(store));
            if(publishWorld) intent.previousWorldSha256=ExistingHash(WorldPath(store));
            string pending=PendingPath(store);
            AtomicSaveFile.Write(pending,StateJson.ToJson(intent));FlushFile(pending);
            Publish(store,validated,writeCharacter);
            File.Delete(pending);
        }
        internal static bool Recover(SaveStore store,Action<string> writeCharacter)
        {
            using var characterAccess=SaveAccess.Enter(store.CharacterDirectoryPath);
            return RecoverLocked(store,writeCharacter);
        }
        private static bool RecoverLocked(SaveStore store,Action<string> writeCharacter)
        {
            string pending=PendingPath(store);if(!File.Exists(pending)) return false;
            try
            {
                var intent=StateJson.FromJson<Intent>(File.ReadAllText(pending));
                var validated=Validate(store,intent);
                using var worldAccess=SaveAccess.Enter(store.WorldDirectoryPath);
                RequireUnchanged(CharacterPath(store),intent.previousCharacterSha256,intent.characterJson);
                if(intent.publishWorld.Value) RequireUnchanged(WorldPath(store),intent.previousWorldSha256,intent.worldJson);
                RequireArchive(store,validated.snapshot,intent.publishWorld.Value);
                Publish(store,validated,writeCharacter);
                File.Delete(pending);
                UnityEngine.Debug.LogWarning($"Завершена прерванная запись мира и персонажа: world={intent.worldId}, actor={intent.actorId}, character={intent.characterId}, publishWorld={intent.publishWorld}, intent={pending}.");
                return true;
            }
            catch(Exception error) when(error is IOException || error is InvalidOperationException || error is ArgumentException || error is Newtonsoft.Json.JsonException)
            {
                throw new InvalidDataException($"Не удалось восстановить пару мира и персонажа: intent={pending}. Исходные файлы и намерение сохранены. {error.Message}",error);
            }
        }
        private static Validated Validate(SaveStore store,Intent intent)
        {
            if(intent==null || intent.version!=Version || intent.publishWorld==null)
                throw new InvalidDataException("Неподдерживаемая версия намерения записи пары.");
            if(!SamePath(intent.characterDirectory,store.CharacterDirectoryPath) || !SamePath(intent.worldDirectory,store.WorldDirectoryPath))
                throw new InvalidDataException("Намерение принадлежит другой библиотеке мира или профилю.");
            var snapshot=new RecoverySnapshot(intent.worldJson);
            if(snapshot.WorldId!=intent.worldId || snapshot.Id!=intent.worldSha256)
                throw new InvalidDataException("Не совпадает ID или точный хэш мира в намерении записи пары.");
            if(intent.characterJson==null || RecoverySnapshot.Fingerprint(intent.characterJson)!=intent.characterSha256)
                throw new InvalidDataException("Не совпадает точный хэш персонажа в намерении записи пары.");
            var actor=StateJson.FromJson<ActorState>(intent.characterJson);
            if(actor==null || !actor.player || string.IsNullOrWhiteSpace(actor.id) || string.IsNullOrWhiteSpace(actor.character) ||
                actor.id!=intent.actorId || actor.character!=intent.characterId)
                throw new InvalidDataException("Не совпадает личность персонажа в намерении записи пары.");
            Remains.Content.PonyAppearance.ValidateSettings(actor.appearance,actor.id);
            InventoryStacks.Validate(actor.inventory,actor.inventoryAliases);
            CosmeticEquipment.Validate(actor);
            var world=snapshot.CreateLiveWorld();int identities=0;
            foreach(var saved in world.actors)
                if(saved.id==actor.id) {if(!saved.player || saved.character!=actor.character) throw new InvalidDataException("Мир содержит другую личность для ID персонажа.");identities++;}
            if(identities!=1) throw new InvalidDataException("Мир не содержит ровно одну связанную личность персонажа.");
            if(!HashOrMissing(intent.previousWorldSha256) || !HashOrMissing(intent.previousCharacterSha256))
                throw new InvalidDataException("Некорректный хэш исходного файла в намерении записи пары.");
            return new Validated {intent=intent,snapshot=snapshot};
        }
        private static void Publish(SaveStore store,Validated validated,Action<string> writeCharacter)
        {
            var intent=validated.intent;
            // The intent repairs either inter-file crash window; no ordering of two
            // independent files alone can make their publication atomic.
            writeCharacter(intent.characterJson);FlushFile(CharacterPath(store));
            if(intent.publishWorld.Value)
            {
                store.SaveRecovery(validated.snapshot);
                FlushFile(Archive(store,validated.snapshot));
                FlushFile(Path.Combine(store.WorldDirectoryPath,"Worlds",intent.worldId+".json"));
                FlushFile(WorldPath(store));
            }
        }
        private static void RequireArchive(SaveStore store,RecoverySnapshot snapshot,bool allowMissing=false)
        {
            string path=Archive(store,snapshot);
            if(allowMissing && !File.Exists(path)) return;
            if(!File.Exists(path) || File.ReadAllText(path)!=snapshot.Json)
                throw new InvalidDataException($"Не найдена точная каноническая копия перед записью профиля: snapshot={snapshot.Id}, archive={path}.");
        }
        private static void RequireUnchanged(string path,string previous,string intended)
        {
            string current=File.Exists(path)?File.ReadAllText(path):null;
            if(current==intended || (current==null?previous==null:RecoverySnapshot.Fingerprint(current)==previous)) return;
            throw new InvalidDataException($"Файл изменён после создания намерения; автоматическая замена запрещена: path={path}.");
        }
        private static string ExistingHash(string path)=>File.Exists(path)?RecoverySnapshot.Fingerprint(File.ReadAllText(path)):null;
        private static bool SamePath(string first,string second)=>first!=null && string.Equals(SavePath.Absolute(first),second,
            Environment.OSVersion.Platform==PlatformID.Win32NT?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);
        private static bool HashOrMissing(string value)
        {
            if(value==null) return true;if(value.Length!=64) return false;
            foreach(char digit in value) if(!((digit>='0' && digit<='9') || (digit>='a' && digit<='f'))) return false;
            return true;
        }
        private static void FlushFile(string path)
        {using var file=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.Read);file.Flush(true);}
        private static string CharacterPath(SaveStore store)=>Path.Combine(store.CharacterDirectoryPath,"character.json");
        private static string WorldPath(SaveStore store)=>Path.Combine(store.WorldDirectoryPath,"world.json");
        private static string Archive(SaveStore store,RecoverySnapshot snapshot)=>Path.Combine(store.WorldDirectoryPath,"Worlds",snapshot.WorldId,"Snapshots",snapshot.Id+".json");
    }
}
