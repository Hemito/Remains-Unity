using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using System.Linq;

namespace Remains.Runtime
{
    internal static class RoomContentProbePersistence
    {
        public static bool HasCompleteAuthoredContent(WorldState saved,string level)
        {
            var room=RoomCatalogAsset.Load().Resolve(WorldRooms.Definition(saved,level));var batch=RoomContentBatch.Read(room,level);
            bool Same(System.Collections.Generic.IEnumerable<string> actual,System.Collections.Generic.IEnumerable<string> expected)
                =>actual.OrderBy(id=>id,System.StringComparer.Ordinal).SequenceEqual(expected.OrderBy(id=>id,System.StringComparer.Ordinal));
            return Same(saved.props.Where(p=>p.level==level).Select(p=>p.id),batch.physical.props.Select(p=>p.id)) &&
                Same(saved.containers.Where(p=>p.level==level).Select(p=>p.id),batch.physical.containers.Select(p=>p.id)) &&
                Same(saved.doors.Where(p=>p.level==level).Select(p=>p.id),batch.doors.doors.Select(p=>p.id)) &&
                Same(saved.actors.Where(p=>p.level==level && !p.player).Select(p=>p.id),batch.npcs.actors.Select(p=>p.id));
        }
        // Verify the saved state through the production installer, including absent/dead objects and taken loot.
        public static bool UnchangedOnReinstall(WorldState saved,string level)
        {
            string before=StateJson.ToJson(saved);
            var room=RoomCatalogAsset.Load().Resolve(WorldRooms.Definition(saved,level));
            return !RoomContentInstaller.Apply(saved,RoomContentBatch.Read(room,level)) && StateJson.ToJson(saved)==before;
        }
    }
}
