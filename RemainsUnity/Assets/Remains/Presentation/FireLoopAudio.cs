using System;
using Remains.Content;
using UnityEngine;

namespace Remains.Presentation
{
    // The burning loop. The original game has no separate fire sound, so the loop is cut from the sustained middle of its
    // flamethrower stream (flamer_s, a one-shot that fades out) with the tail crossfaded into the head: no silent gap and
    // no click where it repeats. Built once when audio starts, never per frame.
    public static class FireLoopAudio
    {
        public const string Source=AudioClipSource.ExportedPrefix+"sound_weapon:54";
        public const float Start=.10f,End=3.40f,Crossfade=.15f;

        public static AudioClip Create()=>Section(AudioClipSource.Load(Source),Start,End,Crossfade,"fire-loop:flamer_s");

        public static AudioClip Section(AudioClip source,float start,float end,float crossfade,string name)
        {
            if(source==null) throw new ArgumentNullException(nameof(source));
            if(source.loadState!=AudioDataLoadState.Loaded && !source.LoadAudioData())
                throw new InvalidOperationException("Звук для петли не загрузился: "+source.name);
            int channels=source.channels,rate=source.frequency;
            int first=Mathf.RoundToInt(start*rate),last=Mathf.RoundToInt(end*rate),blend=Mathf.RoundToInt(crossfade*rate);
            if(first<0 || last>source.samples || blend<=0 || last-first<=blend*2)
                throw new InvalidOperationException($"Неверный участок петли: clip={source.name}, samples={source.samples}, start={start}, end={end}, crossfade={crossfade}.");
            var all=new float[source.samples*channels];
            if(!source.GetData(all,0)) throw new InvalidOperationException("Не прочитаны данные звука: "+source.name);
            int length=last-first-blend;
            var loop=new float[length*channels];
            for(int i=0;i<length;i++)
            for(int c=0;c<channels;c++)
            {
                float sample=all[(first+i)*channels+c];
                // The head fades in while the section's tail fades out, so the last frame flows into the first.
                if(i<blend)
                {
                    float t=(float)i/blend;
                    sample=sample*t+all[(last-blend+i)*channels+c]*(1-t);
                }
                loop[i*channels+c]=sample;
            }
            var clip=AudioClip.Create(name,length,channels,rate,false);
            if(!clip.SetData(loop,0)) throw new InvalidOperationException("Не записана петля звука: "+name);
            return clip;
        }
    }
}
