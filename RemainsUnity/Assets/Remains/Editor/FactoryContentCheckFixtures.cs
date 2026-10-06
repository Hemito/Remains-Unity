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
            source=FactoryOfficeFurnitureChecks.BeforeFurniture(source);
            string level=source.level+":";
            if(source.patch!=level+FactoryOfficeConnectionAuthoring.PhysicalPatch && source.patch!=level+FactoryOfficeLootAuthoring.ConnectedPatch) return source;
            var result=StateJson.Copy(source);string prefix=level+"original:plant:53:";
            if(source.patch==level+FactoryOfficeLootAuthoring.ConnectedPatch)
            {
                var ids=FactoryOfficeLootAuthoring.Containers.Select(c=>level+c.Id).ToArray();
                if(result.props.Length!=9 || result.containers.Length!=31 || result.loot.Length!=28 ||
                    !result.containers.Where(c=>ids.Contains(c.id)).Select(c=>c.id).OrderBy(id=>id).SequenceEqual(ids.OrderBy(id=>id)) ||
                    result.expansion?.previousPatch!=level+FactoryOfficeConnectionAuthoring.PhysicalPatch ||
                    !result.expansion.additions.OrderBy(id=>id).SequenceEqual(ids.OrderBy(id=>id)) || result.expansion.earlierVersions.Length!=3)
                    throw new InvalidOperationException("Прежняя физическая проверка: неполное дополнение четырёх контейнеров v5 не скрывается fixture.");
                result.containers=result.containers.Where(c=>!ids.Contains(c.id)).ToArray();result.loot=result.loot.Where(b=>!ids.Contains(b.container)).ToArray();
                var earlier=result.expansion.earlierVersions;
                result.patch=level+FactoryOfficeConnectionAuthoring.PhysicalPatch;
                result.expansion=new PhysicalExpansion {previousPatch=earlier[0].previousPatch,additions=earlier[0].additions.Where(id=>!ids.Contains(id)).ToArray(),
                    earlierVersions=earlier.Skip(1).Select(step=>new ContentExpansionStep {previousPatch=step.previousPatch,additions=step.additions.Where(id=>!ids.Contains(id)).ToArray()}).ToArray()};
            }
            var office=result.props.Cast<Remains.Domain.PropState>().Concat(result.containers).Where(p=>p.id.StartsWith(prefix,StringComparison.Ordinal)).Select(p=>p.id).ToArray();
            if(office.Length!=15 || result.props.Length!=9 || result.containers.Length!=27 || result.loot.Length!=24 ||
                result.expansion?.previousPatch!=level+FactoryToolStandAuthoring.ConnectedPatch || result.expansion.earlierVersions.Length!=2 ||
                !result.expansion.additions.OrderBy(id=>id).SequenceEqual(office.OrderBy(id=>id)))
                throw new InvalidOperationException("Прежняя физическая проверка: неполное офисное дополнение v4 не скрывается fixture.");
            result.props=result.props.Where(p=>!p.id.StartsWith(prefix,StringComparison.Ordinal)).ToArray();
            result.containers=result.containers.Where(p=>!p.id.StartsWith(prefix,StringComparison.Ordinal)).ToArray();
            result.loot=result.loot.Where(p=>!p.container.StartsWith(prefix,StringComparison.Ordinal)).ToArray();
            var steps=result.expansion.earlierVersions;
            result.patch=level+FactoryToolStandAuthoring.ConnectedPatch;
            result.expansion=new PhysicalExpansion {previousPatch=steps[0].previousPatch,additions=steps[0].additions.Where(id=>!id.StartsWith(prefix,StringComparison.Ordinal)).ToArray(),
                earlierVersions=steps.Skip(1).Select(step=>new ContentExpansionStep {previousPatch=step.previousPatch,additions=step.additions.Where(id=>!id.StartsWith(prefix,StringComparison.Ordinal)).ToArray()}).ToArray()};
            if(result.props.Length!=6 || result.containers.Length!=15 || result.loot.Length!=13 ||
                result.expansion.previousPatch!=level+"base:factory-connected-46-18:physical-v2" ||
                !result.expansion.additions.SequenceEqual(new[]{level+FactoryToolStandAuthoring.SourceId}) || result.expansion.earlierVersions.Length!=1 ||
                result.expansion.earlierVersions[0].previousPatch!=level+"base:factory-connected-46-18:physical-v1" || !result.expansion.earlierVersions[0].additions.OrderBy(id=>id).SequenceEqual(new[]{level+FactoryToolStandAuthoring.SourceId,
                    level+"original:plant:18:ZoIAvJZMFks7XYbh",level+"original:plant:18:Ainu8fDKejKYdTIz"}.OrderBy(id=>id)))
                throw new InvalidOperationException("Прежняя физическая проверка: потеряна исходная fixture v3 и её v1/v2 chain.");
            return result;
        }
    }
}
