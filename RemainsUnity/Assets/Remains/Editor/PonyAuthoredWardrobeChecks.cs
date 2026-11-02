using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    // Focused integration check for the per-node donor contract, without changing source assets.
    public static class PonyAuthoredWardrobeChecks
    {
        public static void Run()
        {
            PonyAnimationData.Load();int checkedNodes=0,checkedInstances=0;
            foreach(string outfit in new[]{"pip","antirad"})
            {
                string key="Original/Pony/Wearables/"+outfit;
                var json=GameAssets.Load<TextAsset>(key)??throw new InvalidOperationException("Проверка узловой одежды: нет "+key);
                var bank=StateJson.FromJson<WearableSourceBank>(json.text);
                var sources=bank.clips.ToDictionary(c=>c.id,StringComparer.Ordinal);
                var before=sources.ToDictionary(p=>p.Key,p=>StateJson.ToJson(p.Value),StringComparer.Ordinal);
                PonyAuthoredBuildPlans.Extend(bank,"проверка "+outfit);
                foreach(var authored in PonyAuthoredBuildPlans.Bank.clips)
                {
                    var actual=bank.clips.Single(c=>c.id==authored.clip.id);
                    for(int f=0;f<authored.frames.Length;f++)
                    {
                        var plan=authored.frames[f];var frame=actual.frames[f];var expectedHidden=new HashSet<int>();int expectedInstances=0;
                        for(int n=0;n<plan.nodes.Length;n++)
                        {
                            var node=plan.nodes[n];var donor=sources[node.donorClip].frames[node.donorFrame];
                            var map=node.donorPartIndices.Select((p,i)=>(p,i)).ToDictionary(p=>p.p,p=>node.partIndices[p.i]);
                            for(int p=0;p<node.partIndices.Length;p++)
                            {
                                var expected=(int[])donor.baseDepthPaths[node.donorPartIndices[p]].Clone();expected[0]=n+1;
                                Require(expected.SequenceEqual(frame.baseDepthPaths[node.partIndices[p]]),$"порядок базовой части: {outfit}/{authored.clip.id}/{f}/{node.top}/{p}");
                            }
                            foreach(int index in donor.hiddenBasePartIndices)if(map.TryGetValue(index,out int target))expectedHidden.Add(target);
                            foreach(var instance in donor.instances.Where(i=>Top(i.basePath)==node.donorTop))
                            {
                                string path=node.top+instance.basePath.Substring(node.donorTop.Length);
                                var matched=frame.instances.Where(i=>i.basePath==path && i.templateId==instance.templateId).ToArray();
                                Require(matched.Length==1,$"единственный экземпляр: {outfit}/{authored.clip.id}/{f}/{path}/{instance.templateId}");
                                var got=matched[0];var matrix=PonyAuthoredBuildPlans.Move(node.move,instance.matrix);
                                Require(!ReferenceEquals(got,instance),"экземпляр снаряжения не скопирован");
                                for(int k=0;k<6;k++)Require(Math.Abs(matrix[k]-got.matrix[k])<.00001f,"матрица снаряжения: "+path);
                                Require(instance.basePartIndices.Select(i=>map[i]).SequenceEqual(got.basePartIndices),"карта скрытых частей: "+path);
                                var depth=(int[])instance.depthPath.Clone();depth[0]=n+1;
                                Require(depth.SequenceEqual(got.depthPath),"порядок экземпляра: "+path);
                                checkedInstances++;expectedInstances++;
                            }
                            checkedNodes++;
                        }
                        Require(expectedHidden.SetEquals(frame.hiddenBasePartIndices),"скрытые базовые части: "+outfit);
                        Require(expectedInstances==frame.instances.Length,"лишние или потерянные экземпляры: "+outfit);
                    }
                }
                foreach(var pair in sources)Require(before[pair.Key]==StateJson.ToJson(pair.Value),"изменён исходный кадр банка: "+outfit+"/"+pair.Key);
                string once=StateJson.ToJson(bank);PonyAuthoredBuildPlans.Extend(bank,"повторная проверка "+outfit);
                Require(once==StateJson.ToJson(bank),"повторное расширение изменило банк: "+outfit);
            }
            Debug.Log($"Узловые основы своих анимаций: PASS — узлов={checkedNodes}, экземпляров одежды={checkedInstances}, точные карты частей/слоёв, исходные банки не меняются, расширение идемпотентно.");
        }
        private static string Top(string path){int slash=path.IndexOf('/');return slash<0?path:path.Substring(0,slash);}
        private static void Require(bool value,string reason){if(!value)throw new InvalidOperationException("Проверка узловой одежды: "+reason);}
    }
}
