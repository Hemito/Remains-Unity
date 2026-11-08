using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Presentation
{
    public interface IAudioLoopPlayback
    {
        bool IsValidActive {get;}
        bool IsPlaying {get;}
        bool Loop {get;}
        object Clip {get;}
        int SamplePosition {get;set;}
        void Play();
    }
    // Short-lived snapshots around a deliberate DSP reset. No subscriptions, Update or continuous source scans.
    public static class AudioLoopResume
    {
        private readonly struct PlayingLoop
        {
            public readonly IAudioLoopPlayback source;
            public readonly object clip;
            public readonly int? position;
            public PlayingLoop(IAudioLoopPlayback source,object clip,int? position)
            {this.source=source;this.clip=clip;this.position=position;}
        }
        private sealed class UnitySource : IAudioLoopPlayback
        {
            private readonly AudioSource source;
            public UnitySource(AudioSource source)=>this.source=source;
            public bool IsValidActive=>source!=null && source.isActiveAndEnabled;
            public bool IsPlaying=>source.isPlaying;
            public bool Loop=>source.loop;
            public object Clip=>source.clip==null?null:source.clip;
            public int SamplePosition {get=>source.timeSamples;set=>source.timeSamples=value;}
            public void Play()=>source.Play();
        }
        public static Action Capture()
        {
            var sources=UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            return Capture(Array.ConvertAll(sources,source=>(IAudioLoopPlayback)new UnitySource(source)));
        }
        public static Action Capture(IEnumerable<IAudioLoopPlayback> sources,Action<string> warning=null)
        {
            warning??=message=>Debug.LogWarning(message);
            var playing=new List<PlayingLoop>();
            foreach(var source in sources)
            {
                try
                {
                    if(source==null || !source.IsValidActive || !source.Loop || !source.IsPlaying)continue;
                    object clip=source.Clip;if(clip==null)continue;
                    int? position=null;
                    try {int sample=source.SamplePosition;if(sample>=0)position=sample;}
                    catch(Exception error) {warning("Не удалось запомнить позицию циклического звука. "+error.Message);}
                    playing.Add(new PlayingLoop(source,clip,position));
                }
                catch(Exception error) {warning("Не удалось запомнить циклический звук. "+error.Message);}
            }
            return ()=>
            {
                foreach(var saved in playing)
                {
                    try
                    {
                        var source=saved.source;
                        if(!source.IsValidActive || !source.Loop || !Equals(source.Clip,saved.clip) || source.IsPlaying)continue;
                        if(saved.position.HasValue)
                        {
                            try {source.SamplePosition=saved.position.Value;}
                            catch(Exception error) {warning("Не удалось восстановить позицию циклического звука. "+error.Message);}
                        }
                        source.Play();
                    }
                    catch(Exception error) {warning("Не удалось возобновить циклический звук. "+error.Message);}
                }
            };
        }
    }
}
