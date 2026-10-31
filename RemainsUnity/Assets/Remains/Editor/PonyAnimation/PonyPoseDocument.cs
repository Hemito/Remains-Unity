using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Remains.Editor
{
    [Serializable]
    public struct PonyPoseValue
    {
        public Vector2 position;
        public float angle;
        public Vector2 scale;
        public static PonyPoseValue Identity=>new() {scale=Vector2.one};
        public Matrix4x4 Matrix=>Matrix4x4.TRS(position,Quaternion.Euler(0,0,angle),new Vector3(scale.x,scale.y,1));
    }

    [Serializable]
    public sealed class PonyPoseJoint
    {
        public string id,label,socket;
        public int parent=-1;
        public PonyPoseValue rest;
    }

    [Serializable]
    public sealed class PonyPoseKey
    {
        public float time;
        public PonyPoseValue[] values;
    }

    // Editor source. Socket deltas are baked into a separate PonyRigClip by the artwork bridge.
    public sealed class PonyPoseDocument : ScriptableObject
    {
        public int schema=1;
        public string clipId="custom",sourceClip="idle";
        public int sourceFrame;
        public float fps=30,duration=1;
        public bool loop;
        public PonyPoseJoint[] joints=Array.Empty<PonyPoseJoint>();
        public PonyPoseKey[] keys=Array.Empty<PonyPoseKey>();
        private const float TimeTolerance=.00001f;

        public void Validate()
        {
            ValidateSkeleton();
            if(keys==null || keys.Length==0)throw Error("нет ключевых кадров");
            float previous=-1;
            for(int k=0;k<keys.Length;k++)
            {
                var key=keys[k];
                if(key==null || !Finite(key.time) || key.time<0 || key.time>duration || key.time<=previous)
                    throw Error($"неверное время ключа {k}: {key?.time}");
                if(k>0 && key.time-previous<TimeTolerance)throw Error($"слишком близкие ключи {k-1} и {k}");
                ValidateValues(key.values,"ключ "+k);PonyPoseMath.Worlds(this,key.values);previous=key.time;
            }
        }

        public void ValidateSkeleton()
        {
            if(schema!=1 || string.IsNullOrWhiteSpace(clipId) || string.IsNullOrWhiteSpace(sourceClip) || sourceFrame<0)
                throw Error("неверная версия, ID клипа или исходный кадр");
            if(!Finite(fps) || fps<1 || fps>120 || !Finite(duration) || duration<=0 || duration>600)
                throw Error("частота должна быть 1–120 кадров/с, длительность — больше 0 и не больше 600 с");
            if(joints==null || joints.Length<17 || joints.Length>128)throw Error("нужны 17 привязок рисунка и не более 128 суставов");
            var ids=new HashSet<string>(StringComparer.Ordinal);var sockets=new HashSet<string>(StringComparer.Ordinal);int roots=0;
            for(int i=0;i<joints.Length;i++)
            {
                var joint=joints[i];
                if(joint==null || string.IsNullOrWhiteSpace(joint.id) || !ids.Add(joint.id))throw Error("пустой/повторный ID сустава "+i);
                if(string.IsNullOrWhiteSpace(joint.label))throw Error("нет названия сустава "+joint.id);
                if(joint.parent<-1 || joint.parent>=joints.Length || joint.parent==i)throw Error("неверный родитель сустава "+joint.id);
                if(joint.parent<0)roots++;
                if(!string.IsNullOrEmpty(joint.socket) && !sockets.Add(joint.socket))throw Error("повторная привязка гнезда "+joint.socket);
                ValidateValue(joint.rest,"исходный сустав "+joint.id);
            }
            if(roots!=1)throw Error("требуется один корень, найдено "+roots);
            if(sockets.Count!=17)throw Error("требуются 17 разных привязок гнёзд, найдено "+sockets.Count);
            var states=new byte[joints.Length];for(int i=0;i<joints.Length;i++)Visit(i);
            void Visit(int i)
            {
                if(states[i]==2)return;
                if(states[i]==1)throw Error("цикл родителей у "+joints[i].id);
                states[i]=1;if(joints[i].parent>=0)Visit(joints[i].parent);states[i]=2;
            }
        }

        public void ValidateValues(PonyPoseValue[] values,string context)
        {
            if(values==null || joints==null || values.Length!=joints.Length)throw Error("число суставов не совпадает: "+context);
            for(int i=0;i<values.Length;i++)
            {
                if(joints[i]==null)throw Error("нет сустава "+i+": "+context);
                ValidateValue(values[i],context+"/"+joints[i].id);
            }
        }

        public PonyPoseValue[] Sample(float time)
        {
            RequireTime(time);
            if(keys==null || keys.Length==0)throw Error("нет ключей для воспроизведения");
            if(time<=keys[0].time)return PonyPoseMath.ClonePose(keys[0].values);
            int last=keys.Length-1;if(time>=keys[last].time)return PonyPoseMath.ClonePose(keys[last].values);
            int low=0,high=last;
            while(high-low>1){int mid=(low+high)/2;if(keys[mid].time<=time)low=mid;else high=mid;}
            var a=keys[low];var b=keys[high];float t=(time-a.time)/(b.time-a.time);
            var result=new PonyPoseValue[joints.Length];
            for(int i=0;i<result.Length;i++)result[i]=new PonyPoseValue {
                position=Vector2.LerpUnclamped(a.values[i].position,b.values[i].position,t),
                angle=a.values[i].angle+Mathf.DeltaAngle(a.values[i].angle,b.values[i].angle)*t,
                scale=Vector2.LerpUnclamped(a.values[i].scale,b.values[i].scale,t)};
            return result;
        }

        // Authoring keys are snapped to the document's frame grid; an exact duration endpoint is retained.
        public float SnapTime(float time)
        {
            RequireTime(time);
            if(Mathf.Abs(time-duration)<TimeTolerance)return duration;
            if(keys!=null)foreach(var key in keys)if(key!=null && Mathf.Abs(key.time-time)<TimeTolerance)return key.time;
            return Mathf.Min(duration,Mathf.Round(time*fps)/fps);
        }

        public void SetKey(float time,PonyPoseValue[] values)
        {
            ValidateSkeleton();ValidateValues(values,"новый ключ");PonyPoseMath.Worlds(this,values);time=SnapTime(time);
            var list=new List<PonyPoseKey>(keys??Array.Empty<PonyPoseKey>());
            int same=list.FindIndex(k=>k!=null && Mathf.Abs(k.time-time)<TimeTolerance);
            var key=new PonyPoseKey {time=time,values=PonyPoseMath.ClonePose(values)};
            if(same>=0)list[same]=key;else list.Add(key);
            list.Sort((a,b)=>a.time.CompareTo(b.time));keys=list.ToArray();Validate();
        }

        public void RemoveKey(int index)
        {
            Validate();
            if(index<0 || index>=keys.Length)throw Error("неверный индекс удаляемого ключа "+index);
            if(keys.Length==1)throw Error("нельзя удалить единственный ключ");
            var list=new List<PonyPoseKey>(keys);list.RemoveAt(index);keys=list.ToArray();
        }

        public string ToJson(){Validate();return JsonUtility.ToJson(this,true);}
        public static PonyPoseDocument FromJson(string json)
        {
            if(string.IsNullOrWhiteSpace(json))throw Error("пустой JSON");
            var parsed=JObject.Parse(json);
            foreach(string field in new[]{"schema","clipId","sourceClip","sourceFrame","fps","duration","loop","joints","keys"})
                if(parsed[field]==null || parsed[field].Type==JTokenType.Null)throw Error("в JSON отсутствует обязательное поле "+field);
            var document=CreateInstance<PonyPoseDocument>();
            try{JsonUtility.FromJsonOverwrite(json,document);document.Validate();return document;}
            catch{DestroyImmediate(document);throw;}
        }

        private void RequireTime(float time)
        {
            if(!Finite(time) || !Finite(duration) || duration<=0 || time<0 || time>duration)
                throw Error($"время {time} вне диапазона 0…{duration}");
        }
        internal static void ValidateValue(PonyPoseValue value,string context)
        {
            if(!Finite(value.position.x) || !Finite(value.position.y) || !Finite(value.angle) ||
                !Finite(value.scale.x) || !Finite(value.scale.y) || value.scale.x<.001f || value.scale.y<.001f)
                throw Error("неконечное/вырожденное преобразование: "+context);
        }
        internal static bool Finite(float value)=>!float.IsNaN(value) && !float.IsInfinity(value);
        internal static InvalidOperationException Error(string reason)=>new("Анимация пони: "+reason+".");
    }
}
