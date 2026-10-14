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
        private FloatingDamageNumber prepared;
        public int Displayed {get;private set;}
        public event System.Action<DamageResult> Shown;
        public event System.Action<DamageResult,double,double,double> PreparationMeasured;
        public void Initialize(CoopSession session,DamageFeedback feedback)
        {
            this.session=session;this.feedback=feedback;
            prefab=Resources.Load<FloatingDamageNumber>("Presentation/DamageNumber");
            if(prefab==null) throw new System.InvalidOperationException("Нет префаба Presentation/DamageNumber.");
            prefab.Validate();
            // Подготовка первого текста выполняется при загрузке, без фиктивного урона/событий.
            prepared=Instantiate(prefab,transform,false);
            prepared.gameObject.SetActive(false);
            prepared.PrepareText();
            feedback.Received+=Show;
        }
        private void Show(DamageResult result)
        {
            if(session.LocalActor?.level!=result.level) return;
            if(active.Count>=64) {Debug.LogWarning($"Лимит чисел урона: actor={result.actor}, attack={result.attack}, level={result.level}.",this);return;}
            bool measure=PreparationMeasured!=null;
            long start=measure?System.Diagnostics.Stopwatch.GetTimestamp():0;
            var number=prepared!=null?prepared:Instantiate(prefab,transform,false);prepared=null;
            number.gameObject.SetActive(true);number.name="Урон "+result.actor;
            long created=measure?System.Diagnostics.Stopwatch.GetTimestamp():0;
            number.Initialize(result);
            long initialized=measure?System.Diagnostics.Stopwatch.GetTimestamp():0;
            var occupied=new List<Rect>(active.Count);
            foreach(var entry in active)
                if(entry.level==result.level && entry.elapsed<FloatingDamageNumber.Duration) occupied.Add(entry.number.Bounds);
            number.PlaceAbove(occupied);Displayed++;active.Add(new Entry {number=number,level=result.level});
            if(measure)
            {
                double frequency=System.Diagnostics.Stopwatch.Frequency;
                PreparationMeasured?.Invoke(result,(created-start)/frequency,(initialized-created)/frequency,
                    (System.Diagnostics.Stopwatch.GetTimestamp()-initialized)/frequency);
            }
            Shown?.Invoke(result);
        }
        private void Update()
        {
            using var frameSection=Remains.Diagnostics.FrameHitchLog.Section("DamageNumbers.Update");
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
