using System;
using System.IO;
using Remains.Domain;
using UnityEngine;

namespace Remains.Persistence
{
    // One application-owned world library, with independently selected personal profiles.
    public sealed class SaveLibraries
    {
        public string RootDirectory {get;}
        public string ProfilesDirectory {get;}
        public string WorldsDirectory {get;}
        public CharacterLibrary Characters {get;}
        public WorldLibraryMigration Migration {get;}
        private string CharacterSelectionPath=>Path.Combine(RootDirectory,"Settings","last-character.txt");
        public SaveLibraries(string applicationDirectory,string initialCharacter)
        {
            RootDirectory=SavePath.Absolute(applicationDirectory);
            ProfilesDirectory=Path.Combine(RootDirectory,"Profiles");
            WorldsDirectory=Path.Combine(RootDirectory,"WorldLibrary");
            Characters=new CharacterLibrary(ProfilesDirectory);
            Migration=new WorldLibraryMigration(ProfilesDirectory,WorldsDirectory,Characters.ProfilePath(initialCharacter));
        }
        public SaveStore ForCharacter(string character)=>new(Characters.ProfilePath(character),WorldsDirectory,Migration);
        public string RememberedCharacter()
        {
            try
            {
                if(!File.Exists(CharacterSelectionPath)) return null;
                string name=File.ReadAllText(CharacterSelectionPath);
                return CharacterName.IsProfileName(name) && File.Exists(Path.Combine(Characters.ProfilePath(name),"character.json"))?name:null;
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException)
            {Debug.LogWarning("Не удалось прочитать последний выбор персонажа: "+error.Message);return null;}
        }
        public void RememberCharacter(string name)
        {
            if(!CharacterName.IsProfileName(name)) throw new ArgumentException("Некорректное имя выбранного персонажа.",nameof(name));
            try
            {
                if(!File.Exists(CharacterSelectionPath) || File.ReadAllText(CharacterSelectionPath)!=name)
                    AtomicSaveFile.Write(CharacterSelectionPath,name);
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException)
            {Debug.LogWarning("Не удалось запомнить выбранного персонажа: "+error.Message);}
        }
    }
}
