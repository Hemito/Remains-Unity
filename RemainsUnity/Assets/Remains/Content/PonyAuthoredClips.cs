using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Content
{
    // Clips authored in the pose animator, kept apart from the original bank: pony.json and every hash bound to it
    // stay untouched. Each frame names the original frame it was built from (its donor) and, per node, the affine
    // that carries the donor's node to the authored one. The pony parts are stored resolved; any wearable bank,
    // a mod's included, gets its frames by moving the donor frame's instances with the same per-node affine.
    [Serializable] public sealed class PonyAuthoredBank {public int schema;public string baseSourceHash;public PonyAuthoredClip[] clips;}
    [Serializable] public sealed class PonyAuthoredClip {public string source;public PonyClip clip;public PonyAuthoredFrame[] frames;}
    [Serializable] public sealed class PonyAuthoredFrame {public string donorClip;public int donorFrame;public PonyAuthoredNode[] nodes;}
    // move: actor-space affine (m00,m10,m01,m11,m03,m13), applied on the left of the donor's placement.
    [Serializable] public sealed class PonyAuthoredNode {public string top;public float[] move;}

    public static class PonyAuthoredClips
    {
        public const int Schema=1;
        public const string Resource="Original/Pony/pony-authored";
        public static PonyAuthoredBank Bank {get;private set;}
        internal static void Reset()=>Bank=null;
        // Called by PonyAnimationData.Load before it publishes the library.
        internal static PonyClip[] Load(string baseHash)
        {
            if(Bank!=null) return Bank.clips.Select(c=>c.clip).ToArray();
            var json=GameAssets.Load<TextAsset>(Resource);
            if(json==null) throw new InvalidOperationException("Нет банка своих анимаций пони: "+Resource+".");
            var bank=JsonUtility.FromJson<PonyAuthoredBank>(json.text);
            if(bank==null || bank.schema!=Schema || bank.clips==null)
                throw new InvalidOperationException($"Некорректный банк своих анимаций пони: {Resource}, schema={bank?.schema}.");
            if(bank.baseSourceHash!=baseHash)
                throw new InvalidOperationException($"Банк своих анимаций собран для другой исходной анимации пони: {Resource}, base={bank.baseSourceHash}, current={baseHash}. Выполните Remains → Импортировать свои анимации пони.");
            foreach(var value in bank.clips)
                if(value?.clip?.frames==null || value.frames==null || value.frames.Length!=value.clip.frames.Length ||
                    value.frames.Any(f=>f==null || string.IsNullOrWhiteSpace(f.donorClip) || f.nodes==null || f.nodes.Any(n=>n?.move==null || n.move.Length!=6 || string.IsNullOrWhiteSpace(n.top))))
                    throw new InvalidOperationException("Неполная своя анимация пони: "+value?.clip?.id+".");
            Bank=bank;return bank.clips.Select(c=>c.clip).ToArray();
        }
        // Appends the authored clips to a wearable bank in library order. A donor frame without the instance's
        // node is a broken import, not a substitution, and throws.
        public static void Extend(WearableSourceBank bank,string source)
        {
            PonyAnimationData.Load();
            if(bank?.clips==null) throw new InvalidOperationException("Нет кадров банка снаряжения: "+source+".");
            if(Bank.clips.Length==0 || bank.clips.Any(c=>c.id==Bank.clips[0].clip.id)) return;
            var result=bank.clips.ToList();
            foreach(var authored in Bank.clips)
            {
                var frames=new WearableSourceFrame[authored.frames.Length];
                for(int f=0;f<frames.Length;f++)
                {
                    var plan=authored.frames[f];
                    var donor=bank.clips.SingleOrDefault(c=>c.id==plan.donorClip)?.frames?.ElementAtOrDefault(plan.donorFrame)??
                        throw new InvalidOperationException($"Нет кадра-основы своей анимации в банке снаряжения: source={source}, clip={authored.clip.id}, donor={plan.donorClip}/{plan.donorFrame}.");
                    var frame=StateJson.Copy(donor);
                    foreach(var instance in frame.instances)
                    {
                        int slash=instance.basePath?.IndexOf('/')??-1;string top=slash<0?instance.basePath:instance.basePath.Substring(0,slash);
                        var node=plan.nodes.SingleOrDefault(n=>n.top==top)??
                            throw new InvalidOperationException($"Деталь снаряжения без узла своей анимации: source={source}, clip={authored.clip.id}/{f}, base={instance.basePath}.");
                        instance.matrix=Move(node.move,instance.matrix);
                    }
                    frames[f]=frame;
                }
                result.Add(new WearableSourceClip {id=authored.clip.id,frames=frames});
            }
            bank.clips=result.ToArray();
        }
        // Canvas matrix (a,b,c,d,tx,ty in pixels, y down) moved by an actor-space affine.
        public static float[] Move(float[] move,float[] canvas)
        {
            float a=canvas[0],b=-canvas[1],c=-canvas[2],d=canvas[3],x=canvas[4]/1000,y=-canvas[5]/1000;
            float m00=move[0],m10=move[1],m01=move[2],m11=move[3],m03=move[4],m13=move[5];
            float na=m00*a+m01*b,nb=m10*a+m11*b,nc=m00*c+m01*d,nd=m10*c+m11*d,nx=m00*x+m01*y+m03,ny=m10*x+m11*y+m13;
            return new[] {na,-nb,-nc,nd,nx*1000,-ny*1000};
        }
    }
}
