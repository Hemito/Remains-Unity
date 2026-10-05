using System.IO;

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
        public SaveLibraries(string applicationDirectory,string initialCharacter)
        {
            RootDirectory=SavePath.Absolute(applicationDirectory);
            ProfilesDirectory=Path.Combine(RootDirectory,"Profiles");
            WorldsDirectory=Path.Combine(RootDirectory,"WorldLibrary");
            Characters=new CharacterLibrary(ProfilesDirectory);
            Migration=new WorldLibraryMigration(ProfilesDirectory,WorldsDirectory,Characters.ProfilePath(initialCharacter));
        }
        public SaveStore ForCharacter(string character)=>new(Characters.ProfilePath(character),WorldsDirectory,Migration);
    }
}
