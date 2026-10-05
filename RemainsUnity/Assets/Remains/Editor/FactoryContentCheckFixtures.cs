using System;
using System.Linq;
using Remains.Content;
using Remains.Serialization;

namespace Remains.Editor
{
    // Earlier content regressions retain their original fixture; the new module has separate full-state checks.
    internal static class FactoryContentCheckFixtures
    {
        public static PhysicalLayout BeforeOffice(PhysicalLayout source)
        {
            if(source.patch!="factory:0:"+FactoryOfficeConnectionAuthoring.PhysicalPatch) return source;
            var result=StateJson.Copy(source);const string prefix="factory:0:original:plant:53:";
            result.props=result.props.Where(p=>!p.id.StartsWith(prefix,StringComparison.Ordinal)).ToArray();
            result.containers=result.containers.Where(p=>!p.id.StartsWith(prefix,StringComparison.Ordinal)).ToArray();
            result.loot=result.loot.Where(p=>!p.container.StartsWith(prefix,StringComparison.Ordinal)).ToArray();
            result.patch="factory:0:"+FactoryToolStandAuthoring.ConnectedPatch;
            result.expansion=new PhysicalExpansion {previousPatch="factory:0:base:factory-connected-46-18:physical-v2",
                additions=new[]{"factory:0:"+FactoryToolStandAuthoring.SourceId},earlierVersions=new[]{new ContentExpansionStep {
                    previousPatch="factory:0:base:factory-connected-46-18:physical-v1",additions=new[]{"factory:0:"+FactoryToolStandAuthoring.SourceId,
                        "factory:0:original:plant:18:ZoIAvJZMFks7XYbh","factory:0:original:plant:18:Ainu8fDKejKYdTIz"}}}};
            return result;
        }
    }
}
