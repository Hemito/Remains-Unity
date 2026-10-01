using System;
using System.Collections.Generic;
using System.IO;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;

namespace Remains.Persistence
{
    // Consent belongs to an exact local source, not to every future save or session.
    public sealed class CharacterContentGate
    {
        private string pendingSource,acceptedSource;
        private ActorState preparedCharacter;
        public IReadOnlyList<ContentDifference> Differences {get;private set;}=Array.Empty<ContentDifference>();
        public event Action Changed;
        public void Reset()
        {pendingSource=acceptedSource=null;preparedCharacter=null;Differences=Array.Empty<ContentDifference>();Changed?.Invoke();}

        public bool TryPrepare(SaveStore saves,string characterId,bool accept,out ActorState actor)
        {
            actor=null;
            string source=saves.ReadCharacterSource();
            if(accept && (pendingSource==null || pendingSource!=source))
                throw new InvalidDataException("Файл персонажа изменился после сравнения. Подключитесь заново и проверьте актуальную копию.");
            if(source==null) return true;
            if(source==acceptedSource && preparedCharacter?.character==characterId)
            {actor=preparedCharacter.Copy();return true;}
            var saved=StateJson.FromJson<ActorState>(source);
            if(saved==null || saved.character!=characterId)
                throw new InvalidDataException($"ID сохранённого персонажа не совпадает: ожидается {characterId}, записано {saved?.character}.");
            var current=WeaponCatalog.ManifestCopy();
            bool mismatch=ContentComparison.Compare(saved.weaponContent,current).Count>0;
            if(mismatch && acceptedSource!=source && !accept)
            {
                pendingSource=source;Differences=ContentComparison.Compare(saved.weaponContent,current,true);
                Changed?.Invoke();return false;
            }
            var content=saved.weaponContent;
            if(content!=null && !content.IsSupported)
                throw new InvalidDataException($"Формат контента персонажа не поддерживается: schema={content.schema}, scope={content.scope}. Исходный файл сохранён.");
            actor=saves.PrepareCharacter(source,mismatch && acceptedSource!=source);
            preparedCharacter=actor.Copy();
            acceptedSource=source;pendingSource=null;Differences=Array.Empty<ContentDifference>();Changed?.Invoke();
            return true;
        }
    }
}
