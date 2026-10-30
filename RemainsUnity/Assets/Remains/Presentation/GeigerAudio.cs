using System;
using Remains.Content;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    // The Geiger counter of the local player (UnitPlayer.actions: sound "geiger" with the chance min(0.5, drad/10) a
    // frame while drad > 0.1): one of the original's three clicks, at the player.
    public sealed class GeigerAudio : MonoBehaviour
    {
        private static readonly string[] Clips={"exported:sound:55","exported:sound:54","exported:sound:53"};   // geiger1–3
        private CoopSession session;
        private Func<float> dose;
        private GameplayAudio playback;
        private AudioClip[] clips;
        public int Played {get;private set;}
        public void Initialize(CoopSession session,Func<float> dose,GameplayAudio playback)
        {
            if(this.session!=null) throw new InvalidOperationException("Счётчик Гейгера уже подготовлен.");
            this.session=session??throw new ArgumentNullException(nameof(session));
            this.dose=dose??throw new ArgumentNullException(nameof(dose));this.playback=playback??throw new ArgumentNullException(nameof(playback));
            clips=Array.ConvertAll(Clips,key=>AudioClipSource.Load(key) is {samples:>0} clip?clip:throw new InvalidOperationException("Нет звука счётчика Гейгера "+key));
        }
        // A click per original frame with the chance min(0.5, drad / 10): over dt, 1 − (1 − p)^(dt × 30).
        public static float Chance(float drad,float dt)=>drad<=.1f || dt<=0?0:1-Mathf.Pow(1-Mathf.Min(.5f,drad/10),dt*30);
        private void Update()
        {
            var actor=session?.LocalActor;
            if(actor==null) return;
            float chance=Chance(dose(),Time.deltaTime);
            if(chance<=0 || UnityEngine.Random.value>=chance) return;
            if(playback.PlayContact(clips[UnityEngine.Random.Range(0,clips.Length)],actor.level,actor.BodyPosition,1)) Played++;
        }
    }
}
