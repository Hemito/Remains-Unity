using Remains.Content;
using Remains.Domain;
using Remains.Serialization;

namespace Remains.Runtime
{
    internal static class RoomContentProbePersistence
    {
        // Verify the saved state through the production installer, including absent/dead objects and taken loot.
        public static bool UnchangedOnReinstall(WorldState saved,string level)
        {
            string before=StateJson.ToJson(saved);
            var room=RoomCatalogAsset.Load().Resolve(WorldRooms.Definition(saved,level));
            return !RoomContentInstaller.Apply(saved,RoomContentBatch.Read(room,level)) && StateJson.ToJson(saved)==before;
        }
    }
}
