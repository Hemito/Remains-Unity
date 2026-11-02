using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Remains.Serialization;
using Remains.Content;
using UnityEngine;

namespace Remains.Editor
{
    // Editor-only build recipes. Nothing in this file or build-plans.json is needed by the game Player.
    // Each node names its original donor and maps old/new part indices while compiling the finished body and
    // wardrobe banks. Published runtime clips contain only finished parts, drawings and matrices.
    [Serializable] public sealed class PonyAuthoredPlanBank {public int schema;public string baseSourceHash;public PonyAuthoredClip[] clips;}
    [Serializable] public sealed class PonyAuthoredClip {public string source;public PonyClip clip;public PonyAuthoredFrame[] frames;}
    [Serializable] public sealed class PonyAuthoredFrame {public string donorClip;public int donorFrame;public PonyAuthoredNode[] nodes;}
    // move: actor-space affine (m00,m10,m01,m11,m03,m13), applied on the left of the donor's placement.
    [Serializable] public sealed class PonyAuthoredNode
    {public string top,donorTop,donorClip;public int donorFrame;public int[] donorPartIndices,partIndices;public float[] move;}

    public static class PonyAuthoredBuildPlans
    {
        public const int Schema=2;
        public const string PathAsset="Assets/Remains/Editor/PonyAnimation/Authored/build-plans.json";
        private static PonyAuthoredPlanBank bank;
        public static PonyAuthoredPlanBank Bank {get {Load();return bank;}}
        public static void Reset()=>bank=null;
        private static void Load()
        {
            if(bank!=null)return;
            if(!File.Exists(PathAsset))throw new InvalidOperationException("Нет редакторского плана своих анимаций: "+PathAsset+". Выполните импорт своих анимаций пони.");
            var parsed=JsonUtility.FromJson<PonyAuthoredPlanBank>(File.ReadAllText(PathAsset));
            if(parsed==null || parsed.schema!=Schema || parsed.clips==null)
                throw new InvalidOperationException($"Некорректный редакторский план своих анимаций: {PathAsset}, schema={parsed?.schema}.");
            string baseHash=PonyRenderAsset.SourceHash();
            if(parsed.baseSourceHash!=baseHash)
                throw new InvalidOperationException($"Редакторский план собран для другой исходной анимации: {PathAsset}, base={parsed.baseSourceHash}, current={baseHash}. Выполните импорт своих анимаций пони.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var value in parsed.clips)
            {
                if(value?.clip?.frames==null || value.clip.frames.Length==0 || string.IsNullOrWhiteSpace(value.clip.id) || !ids.Add(value.clip.id) ||
                    value.frames==null || value.frames.Length!=value.clip.frames.Length)
                    throw new InvalidOperationException("Неполная или повторная редакторская анимация пони: "+value?.clip?.id+".");
                for(int f=0;f<value.frames.Length;f++)ValidateFrame(value,f);
            }
            bank=parsed;
        }
        private static void ValidateFrame(PonyAuthoredClip clip,int index)
        {
            string context=$"clip={clip.clip.id}, frame={index}";
            var frame=clip.frames[index];var parts=clip.clip.frames[index]?.parts;
            if(frame?.nodes?.Length!=17 || parts==null || parts.Length==0)
                throw new InvalidOperationException("Своя анимация пони: нужны 17 узлов и непустой рисунок: "+context);
            var tops=new HashSet<string>(StringComparer.Ordinal);var covered=new bool[parts.Length];
            foreach(var node in frame.nodes)
            {
                if(node==null || !TopValid(node.top) || !tops.Add(node.top) || !TopValid(node.donorTop) ||
                    string.IsNullOrWhiteSpace(node.donorClip) || node.donorFrame<0 || !FiniteMatrix(node.move) ||
                    Math.Abs((double)node.move[0]*node.move[3]-(double)node.move[1]*node.move[2])<1e-12 ||
                    node.donorPartIndices==null || node.partIndices==null || node.partIndices.Length==0 || node.donorPartIndices.Length!=node.partIndices.Length)
                    throw new InvalidOperationException($"Своя анимация пони: неполный узел или карта частей: {context}, top={node?.top}.");
                var donorIndices=new HashSet<int>();
                for(int p=0;p<node.partIndices.Length;p++)
                {
                    int destination=node.partIndices[p],source=node.donorPartIndices[p];
                    if(source<0 || !donorIndices.Add(source) || destination<0 || destination>=parts.Length || covered[destination] ||
                        parts[destination]==null || Top(parts[destination].path)!=node.top)
                        throw new InvalidOperationException($"Своя анимация пони: неверная привязка части: {context}, top={node.top}, source={source}, destination={destination}.");
                    covered[destination]=true;
                }
            }
            if(covered.Any(value=>!value))throw new InvalidOperationException("Своя анимация пони: узлы не покрывают все части: "+context);
        }
        private static bool TopValid(string top)=>!string.IsNullOrWhiteSpace(top) && top.IndexOf('/')<0;
        private static string Top(string path){int slash=path?.IndexOf('/')??-1;return slash<0?path:path.Substring(0,slash);}
        private static bool FiniteMatrix(float[] value)=>value?.Length==6 && Array.TrueForAll(value,float.IsFinite);
        // Appends the authored clips to a wearable bank in library order. A donor frame without the instance's
        // node is a broken import, not a substitution, and throws.
        public static void Extend(WearableSourceBank bank,string source)
        {
            Load();
            if(bank?.clips==null) throw new InvalidOperationException("Нет кадров банка снаряжения: "+source+".");
            if(Bank.clips.Length==0)return;
            var original=new Dictionary<string,WearableSourceClip>(StringComparer.Ordinal);
            foreach(var clip in bank.clips)
                if(clip==null || string.IsNullOrWhiteSpace(clip.id) || clip.frames==null || !original.TryAdd(clip.id,clip))
                    throw new InvalidOperationException("Банк снаряжения: пустая или повторная анимация: "+source+"/"+clip?.id);
            int present=Bank.clips.Count(c=>original.ContainsKey(c.clip.id));
            if(present!=0 && present!=Bank.clips.Length)
                throw new InvalidOperationException($"Банк снаряжения содержит только часть своих анимаций: source={source}, есть={present}, требуется={Bank.clips.Length}.");
            if(present==Bank.clips.Length)
            {
                foreach(var authored in Bank.clips)
                {
                    var existing=original[authored.clip.id];
                    if(existing.frames.Length!=authored.frames.Length)
                        throw new InvalidOperationException($"Банк снаряжения: неверное число готовых кадров своей анимации: source={source}, clip={authored.clip.id}.");
                    for(int f=0;f<existing.frames.Length;f++)ValidateExisting(existing.frames[f],authored.clip.frames[f].parts.Length,$"source={source}, clip={authored.clip.id}/{f}");
                }
                return;
            }
            var result=bank.clips.ToList();
            foreach(var authored in Bank.clips)
            {
                var frames=new WearableSourceFrame[authored.frames.Length];
                for(int f=0;f<frames.Length;f++)
                {
                    var plan=authored.frames[f];
                    var depths=new int[authored.clip.frames[f].parts.Length][];
                    var hidden=new HashSet<int>();var instances=new List<WearableSourceInstance>();
                    for(int n=0;n<plan.nodes.Length;n++)
                    {
                        var node=plan.nodes[n];string context=$"source={source}, clip={authored.clip.id}/{f}, top={node.top}, donor={node.donorClip}/{node.donorFrame}/{node.donorTop}";
                        if(!original.TryGetValue(node.donorClip,out var donorClip) || node.donorFrame>=donorClip.frames.Length)
                            throw new InvalidOperationException("Нет кадра-основы узла своей анимации в банке снаряжения: "+context);
                        var donor=donorClip.frames[node.donorFrame];
                        if(donor?.baseDepthPaths==null || donor.hiddenBasePartIndices==null || donor.instances==null)
                            throw new InvalidOperationException("Неполный кадр-основа снаряжения: "+context);
                        var mapping=new Dictionary<int,int>();
                        for(int p=0;p<node.partIndices.Length;p++)
                        {
                            int old=node.donorPartIndices[p],target=node.partIndices[p];
                            if(old>=donor.baseDepthPaths.Length || depths[target]!=null || !mapping.TryAdd(old,target))
                                throw new InvalidOperationException($"Не совпала карта частей снаряжения: {context}, old={old}, target={target}.");
                            depths[target]=Depth(donor.baseDepthPaths[old],n+1,context);
                        }
                        foreach(int old in donor.hiddenBasePartIndices)
                        {
                            if(old<0 || old>=donor.baseDepthPaths.Length)throw new InvalidOperationException("Скрытая часть вне кадра: "+context+", index="+old);
                            if(mapping.TryGetValue(old,out int target))hidden.Add(target);
                        }
                        foreach(var item in donor.instances)
                        {
                            if(item==null || string.IsNullOrWhiteSpace(item.basePath))throw new InvalidOperationException("Пустая привязка снаряжения: "+context);
                            if(Top(item.basePath)!=node.donorTop)continue;
                            if(item.basePartIndices==null || !FiniteMatrix(item.matrix))throw new InvalidOperationException("Неполная деталь снаряжения: "+context+", base="+item.basePath);
                            var instance=StateJson.Copy(item);
                            instance.basePath=node.top+item.basePath.Substring(node.donorTop.Length);
                            instance.basePartIndices=new int[item.basePartIndices.Length];
                            for(int p=0;p<instance.basePartIndices.Length;p++)
                                if(!mapping.TryGetValue(item.basePartIndices[p],out instance.basePartIndices[p]))
                                    throw new InvalidOperationException($"Снаряжение узла скрывает чужую часть: {context}, base={item.basePath}, index={item.basePartIndices[p]}.");
                            instance.depthPath=Depth(item.depthPath,n+1,context);instance.matrix=Move(node.move,item.matrix);instances.Add(instance);
                        }
                    }
                    if(depths.Any(d=>d==null))throw new InvalidOperationException($"Не все части своей анимации получили порядок снаряжения: source={source}, clip={authored.clip.id}/{f}.");
                    frames[f]=new WearableSourceFrame {baseDepthPaths=depths,hiddenBasePartIndices=hidden.OrderBy(i=>i).ToArray(),instances=instances.ToArray()};
                }
                result.Add(new WearableSourceClip {id=authored.clip.id,frames=frames});
            }
            bank.clips=result.ToArray();
        }
        private static int[] Depth(int[] source,int root,string context)
        {
            WearableRenderAsset.ValidatePath(source,context);var value=(int[])source.Clone();value[0]=root;return value;
        }
        private static void ValidateExisting(WearableSourceFrame frame,int count,string context)
        {
            if(frame?.baseDepthPaths?.Length!=count || frame.hiddenBasePartIndices==null || frame.instances==null)
                throw new InvalidOperationException("Неполный готовый кадр своего снаряжения: "+context);
            foreach(var path in frame.baseDepthPaths)WearableRenderAsset.ValidatePath(path,context);
            foreach(int index in frame.hiddenBasePartIndices)if(index<0 || index>=count)throw new InvalidOperationException("Неверная скрытая часть: "+context);
            foreach(var instance in frame.instances)
            {
                if(instance==null || string.IsNullOrWhiteSpace(instance.basePath) || instance.basePartIndices==null || instance.basePartIndices.Any(i=>i<0 || i>=count) || !FiniteMatrix(instance.matrix))
                    throw new InvalidOperationException("Неверная готовая привязка снаряжения: "+context);
                WearableRenderAsset.ValidatePath(instance.depthPath,context);
            }
        }
        // Canvas matrix (a,b,c,d,tx,ty in pixels, y down) moved by an actor-space affine.
        public static float[] Move(float[] move,float[] canvas)
        {
            if(!FiniteMatrix(move) || !FiniteMatrix(canvas))throw new InvalidOperationException("Своя анимация пони: преобразование требует две конечные матрицы из6 чисел.");
            float a=canvas[0],b=-canvas[1],c=-canvas[2],d=canvas[3],x=canvas[4]/1000,y=-canvas[5]/1000;
            float m00=move[0],m10=move[1],m01=move[2],m11=move[3],m03=move[4],m13=move[5];
            float na=m00*a+m01*b,nb=m10*a+m11*b,nc=m00*c+m01*d,nd=m10*c+m11*d,nx=m00*x+m01*y+m03,ny=m10*x+m11*y+m13;
            return new[] {na,-nb,-nc,nd,nx*1000,-ny*1000};
        }
    }
}
