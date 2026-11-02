using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Remains.Content
{
    // Finished authored animation only. Build recipes, donor frames and original-source fingerprints live in Editor.
    [Serializable] public sealed class PonyAuthoredBank {public int schema;public PonyClip[] clips;}

    public static class PonyAuthoredClips
    {
        public const int Schema=3;
        public const string Resource="Original/Pony/pony-authored";
        public static PonyAuthoredBank Bank {get;private set;}
        private static string fingerprint;
        public static string Fingerprint {get {Load();return fingerprint;}}
        public static void Reset(){Bank=null;fingerprint=null;}
        // The compiled runtime bank has its own identity; rebuilding the authoring atlas does not change it.
        internal static PonyClip[] Load()
        {
            if(Bank!=null)return Bank.clips;
            var json=GameAssets.Load<TextAsset>(Resource);
            if(json==null)throw new InvalidOperationException("Нет готового банка своих анимаций пони: "+Resource+".");
            var parsed=JsonUtility.FromJson<PonyAuthoredBank>(json.text);
            if(parsed==null || parsed.schema!=Schema || parsed.clips==null)
                throw new InvalidOperationException($"Некорректный готовый банк своих анимаций пони: {Resource}, schema={parsed?.schema}, требуется={Schema}.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var clip in parsed.clips)
            {
                if(clip==null || string.IsNullOrWhiteSpace(clip.id) || !ids.Add(clip.id) || clip.frames==null || clip.frames.Length==0 ||
                    !float.IsFinite(clip.fps) || clip.fps<=0 || clip.loopStartFrame<0 || clip.loopStartFrame>=clip.frames.Length)
                    throw new InvalidOperationException("Неполная или повторная готовая анимация пони: "+clip?.id+".");
                for(int f=0;f<clip.frames.Length;f++)
                {
                    var parts=clip.frames[f]?.parts;
                    if(parts==null || parts.Length==0)throw new InvalidOperationException($"Готовая анимация пони: пустой кадр {clip.id}/{f}.");
                    var orders=new HashSet<int>();
                    foreach(var part in parts)
                    {
                        if(part==null || string.IsNullOrWhiteSpace(part.path) || string.IsNullOrWhiteSpace(part.shape) || string.IsNullOrWhiteSpace(part.texture) ||
                            !Finite(part.matrix,6) || !Finite(part.bounds,4) || !Finite(part.color,4) || part.bounds[2]<=part.bounds[0] || part.bounds[3]<=part.bounds[1] ||
                            part.hairStyle<0 || part.eyeStyle<0 || part.drawOrder<0 || !orders.Add(part.drawOrder) ||
                            part.slot is not ("none" or "fur" or "hair" or "hairSecondary" or "eye" or "magic"))
                            throw new InvalidOperationException($"Готовая анимация пони: повреждена часть {clip.id}/{f}/{part?.path}.");
                    }
                }
            }
            using var sha=SHA256.Create();
            // Git checkouts may use CRLF; the same JSON bank must keep its identity on every machine.
            string preparedFingerprint=Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(json.text.Replace("\r\n","\n"))));
            Bank=parsed;fingerprint=preparedFingerprint;return parsed.clips;
        }
        private static bool Finite(float[] value,int count)=>value?.Length==count && Array.TrueForAll(value,float.IsFinite);
    }
}
