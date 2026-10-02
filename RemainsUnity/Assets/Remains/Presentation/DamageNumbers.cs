using System.Collections.Generic;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    public sealed class DamageNumbers : MonoBehaviour
    {
        private sealed class Entry {public FloatingDamageNumber number;public string level;public float elapsed;}
        private readonly List<Entry> active=new();
        private CoopSession session;
        private DamageFeedback feedback;
        private FloatingDamageNumber prefab;
        public int Displayed {get;private set;}
        public event System.Action<DamageResult> Shown;
        public void Initialize(CoopSession session,DamageFeedback feedback)
        {
            this.session=session;this.feedback=feedback;
            prefab=Resources.Load<FloatingDamageNumber>("Presentation/DamageNumber");
            if(prefab==null) throw new System.InvalidOperationException("Нет префаба Presentation/DamageNumber.");
            prefab.Validate();
            feedback.Received+=Show;
        }
        private void Show(DamageResult result)
        {
            if(session.LocalActor?.level!=result.level) return;
            if(active.Count>=64) {Debug.LogWarning($"Лимит чисел урона: actor={result.actor}, attack={result.attack}, level={result.level}.",this);return;}
            var number=Instantiate(prefab,transform,false);number.name="Урон "+result.actor;
            number.Initialize(result);
            var occupied=new List<Rect>(active.Count);
            foreach(var entry in active)
                if(entry.level==result.level && entry.elapsed<FloatingDamageNumber.Duration) occupied.Add(entry.number.Bounds);
            number.PlaceAbove(occupied);Displayed++;active.Add(new Entry {number=number,level=result.level});
            Shown?.Invoke(result);
        }
        private void Update()
        {
            for(int i=active.Count-1;i>=0;i--)
            {
                var entry=active[i];
                if(session.LocalActor?.level!=entry.level || entry.elapsed>=FloatingDamageNumber.Duration)
                {Destroy(entry.number.gameObject);active.RemoveAt(i);continue;}
                if(!session.Pause.IsPaused(entry.level)) entry.elapsed+=Time.deltaTime;
                entry.number.Draw(entry.elapsed);
            }
        }
        private void OnDestroy() {if(feedback!=null) feedback.Received-=Show;}
    }
}
