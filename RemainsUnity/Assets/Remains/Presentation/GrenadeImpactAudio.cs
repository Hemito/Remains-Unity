using System;
using System.Collections.Generic;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    public struct GrenadeImpactSound : NetworkMessage
    {public string token,grenade,level;public int generation;public float x,y,gain;}

    // Only the simulation owner reports a contact; presentation never changes physics.
    public sealed class GrenadeImpactAudio : MonoBehaviour
    {
        [Serializable] private sealed class Settings {public string clip;public float minimumSpeed,fullGainSpeed;}
        private CoopSession session;
        private GrenadeController controller;
        private GameplayAudio playback;
        private AudioClip clip;
        private Settings settings;
        private readonly HashSet<string> seen=new();
        private readonly Queue<string> recent=new();
        public int Played {get;private set;}
        public float MinimumSpeed=>settings.minimumSpeed;
        public event Action<GrenadeImpactSound> Observed;
        public void Initialize(CoopSession session,GrenadeController controller,GameplayAudio playback)
        {
            this.session=session;this.controller=controller;this.playback=playback;
            var data=Resources.Load<TextAsset>("Presentation/grenade-audio");
            if(data==null) throw new InvalidOperationException("Нет настроек Presentation/grenade-audio.");
            settings=JsonUtility.FromJson<Settings>(data.text);
            if(settings==null || !float.IsFinite(settings.minimumSpeed) || settings.minimumSpeed<=0 ||
                !float.IsFinite(settings.fullGainSpeed) || settings.fullGainSpeed<settings.minimumSpeed)
                throw new InvalidOperationException("Некорректные пороги звука удара гранаты.");
            clip=Resources.Load<AudioClip>(settings.clip);
            if(clip==null || clip.samples<=0) throw new InvalidOperationException("Нет звука удара гранаты: "+settings.clip);
            controller.Impact+=Contact;session.ServerReady+=RegisterServer;session.ClientReady+=RegisterClient;
        }
        private void RegisterServer()=>NetworkServer.RegisterHandler<GrenadeImpactSound>((connection,message)=>
        {
            var actor=session.ConnectedActor(connection);
            var grenade=session.ServerWorld?.grenades.Find(value=>value.id==message.grenade);
            if(actor==null || grenade==null || actor.owner!=grenade.owner || actor.level!=grenade.level ||
                message.level!=grenade.level || message.generation!=grenade.generation || session.Pause.IsServerPaused(grenade.level) ||
                string.IsNullOrEmpty(message.token) || !float.IsFinite(message.x) || !float.IsFinite(message.y) ||
                !float.IsFinite(message.gain) || message.gain<=0 || message.gain>1) return;
            NetworkServer.SendToAll(message);
        });
        private void RegisterClient()
        {
            seen.Clear();recent.Clear();
            NetworkClient.RegisterHandler<GrenadeImpactSound>(Receive);
        }
        private void Receive(GrenadeImpactSound message)
        {
                if(!seen.Add(message.token)) return;
                recent.Enqueue(message.token);if(recent.Count>512) seen.Remove(recent.Dequeue());
                if(playback.PlayContact(clip,message.level,new Vector2(message.x,message.y),message.gain)) Played++;
                Observed?.Invoke(message);
        }
        private void Contact(GrenadeState state,float speed)
        {
            if(speed<settings.minimumSpeed || !NetworkClient.isConnected) return;
            var message=new GrenadeImpactSound {token=Guid.NewGuid().ToString("N"),grenade=state.id,
                level=state.level,generation=state.generation,x=state.x,y=state.y,gain=Mathf.Clamp01(speed/settings.fullGainSpeed)};
            Receive(message);
            NetworkClient.Send(message);
        }
        private void OnDestroy()
        {
            if(controller!=null) controller.Impact-=Contact;
            if(session!=null) {session.ServerReady-=RegisterServer;session.ClientReady-=RegisterClient;}
        }
    }
}
