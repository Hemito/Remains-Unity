using System;
using System.IO;
using Remains.Domain;

namespace Remains.Persistence
{
    // One explicit selection for the entire connect/admission/lobby/Welcome lifetime.
    // Launch options and editable menu fields cannot replace it after the request starts.
    public sealed class CharacterJoinSelection
    {
        public string CharacterId {get;}
        public SaveStore Saves {get;}
        public CharacterJoinSelection(string characterId,SaveStore saves)
        {
            if(string.IsNullOrWhiteSpace(characterId)) throw new ArgumentException("Не указан выбранный персонаж.",nameof(characterId));
            CharacterId=characterId;Saves=saves??throw new ArgumentNullException(nameof(saves));
        }
        public static void RequireCanChange(bool connectionActive,bool preparingConnection)
        {
            if(connectionActive || preparingConnection)
                throw new InvalidOperationException("Выбор персонажа уже зафиксирован для подключения. Сначала выйдите из сессии или отмените подключение.");
        }
        public void RequireCurrent(string characterId,SaveStore saves)
        {
            if(characterId!=CharacterId || saves==null ||
                !string.Equals(saves.CharacterDirectoryPath,Saves.CharacterDirectoryPath,StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(saves.WorldDirectoryPath,Saves.WorldDirectoryPath,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Выбор персонажа изменился во время подключения: ожидался character={CharacterId}, profile={Saves.CharacterDirectoryPath}; получен character={characterId}, profile={saves?.CharacterDirectoryPath}. Подключение отменено без записи профиля.");
        }
        // Call before PersistCopy/SaveLocalCharacter: a rejected Welcome must not touch either save.
        public ActorState RequireWelcome(WorldState world,string actorId,string owner)
        {
            var actor=world?.Find(actorId);
            if(actor==null || string.IsNullOrWhiteSpace(actorId) || actor.id!=actorId || actor.character!=CharacterId || !actor.player ||
                string.IsNullOrWhiteSpace(owner) || actor.owner!=owner || !actor.connected)
                throw new InvalidDataException($"Ответ подключения не соответствует выбранному персонажу: world={world?.worldId}, expectedCharacter={CharacterId}, actor={actorId}, actualCharacter={actor?.character}, owner={owner}, actualOwner={actor?.owner}, profile={Saves.CharacterDirectoryPath}. Профиль и мир не записаны.");
            return actor;
        }
    }
}
