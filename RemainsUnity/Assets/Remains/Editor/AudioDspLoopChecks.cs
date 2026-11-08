using System;
using System.Collections.Generic;
using System.IO;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Editor
{
    // Shared by native Editor checks and the .NET runner; no audio hardware or real Reset is used.
    public static class AudioDspLoopChecks
    {
        private sealed class Playback : IAudioLoopPlayback
        {
            public bool valid=true,active=true,playing=true,loop=true,readFails,seekFails,playFails;
            public object clip=new object();
            public int samples=123,reads,seeks,plays;
            public bool IsValidActive=>valid && active;
            public bool IsPlaying=>playing;
            public bool Loop=>loop;
            public object Clip=>clip;
            public int SamplePosition
            {
                get {reads++;if(readFails)throw new NotSupportedException("fixture read");return samples;}
                set {seeks++;if(seekFails)throw new NotSupportedException("fixture seek");samples=value;}
            }
            public void Play() {plays++;if(playFails)throw new InvalidOperationException("fixture play");playing=true;}
        }
        public static void Run()
        {
            int checks=0;
            void Require(bool valid,string detail)
            {if(!valid)throw new InvalidOperationException("DSP loop: "+detail);checks++;}
            var initial=new AudioConfiguration {dspBufferSize=512,sampleRate=48000,speakerMode=AudioSpeakerMode.Stereo,numRealVoices=32,numVirtualVoices=128};
            foreach(string outcome in new[]{"true","false","throw"})
            {
                var good=new Playback();var stopped=new Playback {playing=false};var oneShot=new Playback {loop=false};
                var inactive=new Playback {active=false};var destroyed=new Playback();var disabled=new Playback();
                var replaced=new Playback();var unlooped=new Playback();var alreadyPlaying=new Playback();
                var noClip=new Playback {clip=null};var readUnsupported=new Playback {readFails=true};
                var seekUnsupported=new Playback {seekFails=true};var broken=new Playback {playFails=true};var last=new Playback();
                var all=new[]{good,stopped,oneShot,inactive,destroyed,disabled,replaced,unlooped,alreadyPlaying,noClip,readUnsupported,seekUnsupported,broken,last};
                var warnings=new List<string>();int captures=0,resets=0,reads=0;
                var adapter=new UnityAudioConfigurationDevice(()=>{reads++;return initial;},configuration=>
                {
                    Require(captures==1,"reset occurred before capture");resets++;
                    foreach(var source in all) {source.playing=false;source.samples=0;}
                    destroyed.valid=false;disabled.active=false;replaced.clip=new object();unlooped.loop=false;alreadyPlaying.playing=true;
                    if(outcome=="throw")throw new IOException("fixture reset");
                    return outcome=="true";
                },()=>{captures++;return AudioLoopResume.Capture(all,warnings.Add);},warnings.Add);
                Require(adapter.Read().dspBufferSize==512 && reads==1,"read injection was not used");
                bool result=false,threw=false;
                try {result=adapter.Reset(initial);}
                catch(IOException error) {threw=error.Message=="fixture reset";}
                Require(threw==(outcome=="throw") && (threw || result==(outcome=="true")),"reset outcome/exception was masked");
                Require(captures==1 && resets==1 && good.plays==1 && good.playing && good.samples==123,"finally did not resume once at original sample");
                Require(stopped.plays==0 && oneShot.plays==0 && inactive.plays==0 && noClip.plays==0 &&
                    stopped.reads==0 && oneShot.reads==0,"stopped/non-loop/inactive/empty sources were captured");
                Require(destroyed.plays==0 && disabled.plays==0 && replaced.plays==0 && unlooped.plays==0,"invalid or changed source was resumed");
                Require(alreadyPlaying.plays==0 && alreadyPlaying.seeks==0,"playing source was restarted or repositioned");
                Require(readUnsupported.plays==1 && readUnsupported.seeks==0 && seekUnsupported.plays==1,"unsupported sample access prevented playback");
                Require(broken.plays==1 && last.plays==1 && last.samples==123 && warnings.Count==3,"one recovery failure prevented later loops or was hidden");
            }
            // A failed resume callback must not replace either Reset's return value or its original exception.
            foreach(string outcome in new[]{"true","false","throw"})
            {
                int warnings=0;bool threw=false,result=false;
                var adapter=new UnityAudioConfigurationDevice(()=>initial,_=>
                {if(outcome=="throw")throw new IOException("original reset");return outcome=="true";},
                    ()=>()=>throw new InvalidOperationException("resume callback"),_=>warnings++);
                try {result=adapter.Reset(initial);}catch(IOException error) {threw=error.Message=="original reset";}
                Require(warnings==1 && threw==(outcome=="throw") && (threw || result==(outcome=="true")),"resume callback masked reset outcome");
            }
            var idempotent=new Playback();var resume=AudioLoopResume.Capture(new[]{idempotent},_=>{});
            idempotent.playing=false;resume();resume();
            Require(idempotent.plays==1,"repeated recovery duplicated playback");
            foreach(string failure in new[]{"save","false","throw"})
            {
                var source=new Playback();var current=initial;int resets=0,captures=0;
                var adapter=new UnityAudioConfigurationDevice(()=>current,configuration=>
                {
                    resets++;current=configuration;source.playing=false;source.samples=0;
                    if(resets==1 && failure=="throw")throw new IOException("partial reset");
                    return resets!=1 || failure!="false";
                },()=>{captures++;return AudioLoopResume.Capture(new[]{source},_=>{});},_=>{});
                var preferences=new AudioPreferences(null,adapter,_=>
                {if(failure=="save")throw new IOException("fixture save");});
                Require(!preferences.TrySaveBuffer(256,out _),"partial reset/save failure was accepted");
                Require(current.dspBufferSize==512 && resets==2 && captures==2 && source.plays==2 && source.samples==123,
                    "apply/rollback reset did not each recover the original loop");
            }
            int scans=0;var unchanged=new UnityAudioConfigurationDevice(()=>initial,_=>throw new Exception("unexpected reset"),
                ()=>{scans++;return ()=>{};},_=>{});
            Require(new AudioPreferences(null,unchanged,_=>{}).TrySaveBuffer(512,out _) && scans==0,"unchanged buffer scanned/restarted loops");
            Debug.Log($"AUDIO_DSP_LOOP_RESUME_PASS: {checks} checks; fake sources/reset; no hardware listening claim.");
        }
    }
}
