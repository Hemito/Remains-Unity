using System;
using System.Collections.Generic;
using Remains.Networking;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Presentation
{

    public sealed class AreaMusic : MonoBehaviour
    {
        private CoopSession session;
        private MusicTrackDefinition[] tracks;
        private float fadeSeconds;
        private CombatMusicState combat;
        private string areaTrack;
        private readonly Dictionary<string,AudioClip> clips=new();
        private readonly HashSet<string> missing=new();
        private readonly AudioSource[] sources=new AudioSource[2];
        private readonly float[] weights=new float[2];
        private int active;
        private bool initialized;
        public string CurrentTrack {get;private set;}
        public string BackgroundTrack=>areaTrack;
        public int Changes {get;private set;}
        public float Volume {get;set;}
        public void Initialize(CoopSession session)
        {
            this.session=session;
            var asset=GameAssets.Load<MusicCatalogAsset>("Content/area-music");
            if(asset==null) throw new InvalidOperationException("Нет каталога Content/area-music.");
            tracks=asset.CreateTracks();fadeSeconds=asset.FadeSeconds;Volume=asset.Volume;
            foreach(var track in tracks) clips.Add(track.id,track.clip);
            var combatTracks=asset.CreateCombatTracks();combat=new CombatMusicState(combatTracks);
            foreach(var track in combatTracks) clips.Add(track.id,track.clip);
            for(int i=0;i<2;i++)
            {
                var source=new GameObject("Музыка "+i).AddComponent<AudioSource>();source.transform.SetParent(transform,false);
                source.playOnAwake=false;source.loop=true;source.spatialBlend=0;source.volume=0;source.mute=Application.isBatchMode;sources[i]=source;
            }
            initialized=true;session.Changed+=Refresh;session.AttackReceived+=OnAttack;Refresh();
        }
        private void Refresh()
        {
            string level=session.LocalActor?.level;
            combat.SetContext(session.World?.worldId,level);
            if(level!=null && !session.Pause.IsPaused(level))
                foreach(var actor in session.World.actors) combat.ObservePreparation(session.World,actor);
            string trackId=null;int specificity=-1;
            foreach(var track in tracks)
            {
                bool menu=string.IsNullOrEmpty(track.levelPrefix);
                if(level==null?menu:!menu && level.StartsWith(track.levelPrefix,StringComparison.Ordinal))
                {
                    int length=track.levelPrefix?.Length??0;
                    if(length>specificity) {specificity=length;trackId=track.id;}
                }
            }
            if(trackId==null && missing.Add(level??"<меню>")) Debug.LogWarning($"Нет музыкальной привязки для локации {level??"<меню>"}; музыка выключена.",this);
            areaTrack=trackId;SelectTrack(combat.Track??areaTrack);
        }
        private void OnAttack(AttackEvent attack)
        {
            var local=session.LocalActor;
            if(local==null || attack.level!=local.level || session.Pause.IsPaused(local.level)) return;
            combat.ObserveAttack(session.World.Find(attack.attacker));SelectTrack(combat.Track??areaTrack);
        }
        private void SelectTrack(string trackId)
        {
            if(trackId==CurrentTrack) return;
            CurrentTrack=trackId;Changes++;
            active=1-active;sources[active].Stop();weights[active]=0;sources[active].volume=0;
            sources[active].clip=trackId==null?null:clips[trackId];
            if(trackId!=null) sources[active].Play();
        }
        private void Update()
        {
            if(!initialized) return;
            if(combat.Step(Time.unscaledDeltaTime,session.Pause.IsPaused(session.LocalActor?.level))) SelectTrack(areaTrack);
            for(int i=0;i<2;i++)
            {
                float target=i==active && CurrentTrack!=null?1:0;
                weights[i]=Mathf.MoveTowards(weights[i],target,Time.unscaledDeltaTime/fadeSeconds);
                sources[i].volume=weights[i]*Mathf.Clamp01(Volume);
                if(target==0 && weights[i]==0 && sources[i].isPlaying) sources[i].Stop();
            }
        }
        private void OnDestroy() {if(session!=null) {session.Changed-=Refresh;session.AttackReceived-=OnAttack;}}
    }
}
