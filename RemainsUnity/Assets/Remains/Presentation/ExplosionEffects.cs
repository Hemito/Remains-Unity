using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class ExplosionEffects : MonoBehaviour
    {
        private CoopSession session;
        private ExplosionArtwork artwork;
        private readonly HashSet<string> seen=new();
        private readonly Queue<string> recent=new();
        public int Shown {get;private set;}
        public event Action<ExplosionVisual> Created;
        public void Initialize(CoopSession session)
        {this.session=session;artwork=ExplosionArtwork.Load();session.AttackReceived+=Attack;}
        private void Attack(AttackEvent attack)
        {
            if(attack.weapon!="grenade" || attack.level!=session.LocalActor?.level || !seen.Add(attack.id)) return;
            recent.Enqueue(attack.id);if(recent.Count>512) seen.Remove(recent.Dequeue());
            var effect=new GameObject("Взрыв "+attack.id).AddComponent<ExplosionVisual>();effect.transform.SetParent(transform,false);
            effect.Initialize(artwork,session,attack.level,attack.Origin);Shown++;Created?.Invoke(effect);
        }
        private void OnDestroy() {if(session!=null) session.AttackReceived-=Attack;}
    }
}
