using System;
using System.Linq;
using Remains.Content;
using Remains.Serialization;

namespace Remains.Editor
{
    // Earlier content regressions retain their original fixture; the new module has separate full-state checks.
    internal static class FactoryContentCheckFixtures
    {
        public static NpcLayout BeforeOffice(NpcLayout source)
        {
            if(source.patch!=source.level+":"+FactoryOfficeNpcAuthoring.ConnectedPatch) return source;
            var result=StateJson.Copy(source);string prefix=source.level+":original:plant:53:";
            var expected=FactoryOfficeNpcAuthoring.Spawns.Select(s=>source.level+":"+s.Id).OrderBy(id=>id).ToArray();
            if(!result.actors.Where(a=>a.id.StartsWith(prefix,StringComparison.Ordinal)).Select(a=>a.id).OrderBy(id=>id).SequenceEqual(expected) ||
                result.expansion?.previousPatch!=source.level+":"+FactoryCrawlerAuthoring.Patch || result.expansion.earlierVersions?.Length!=2)
                throw new InvalidOperationException("Прежняя проверка NPC: неполное офисное дополнение v4 не скрывается fixture.");
            result.actors=result.actors.Where(a=>!a.id.StartsWith(prefix,StringComparison.Ordinal)).ToArray();
            result.loot=result.loot.Where(b=>!b.actor.StartsWith(prefix,StringComparison.Ordinal)).ToArray();
            result.patch=source.level+":"+FactoryCrawlerAuthoring.Patch;
            var steps=result.expansion.earlierVersions;
            result.expansion=new ContentExpansion {previousPatch=steps[0].previousPatch,
                additions=steps[0].additions.Where(id=>!id.StartsWith(prefix,StringComparison.Ordinal)).ToArray(),
                earlierVersions=steps.Skip(1).Select(step=>new ContentExpansionStep {previousPatch=step.previousPatch,
                    additions=step.additions.Where(id=>!id.StartsWith(prefix,StringComparison.Ordinal)).ToArray()}).ToArray()};
            if(result.actors.Length!=8 || result.loot.Length!=8 || result.expansion.additions.Length!=2 || result.expansion.earlierVersions.Single().additions.Length!=7)
                throw new InvalidOperationException("Прежняя проверка NPC: офисное дополнение потеряло исходную fixture v3 из восьми тел.");
            return result;
        }
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
