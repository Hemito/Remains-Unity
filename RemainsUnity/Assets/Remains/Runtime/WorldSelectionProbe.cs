using System;
using System.Collections;
using System.IO;
using System.Linq;
using Remains.Domain;
using UnityEngine;

namespace Remains.Runtime
{
    // Fixture writes only to the explicit disposable test profile. Actions use real UI events.
    public sealed class WorldSelectionProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private float deadline;
        private bool finished;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+25;
            if(string.IsNullOrEmpty(value.Options.SaveDirectory))
                throw new InvalidOperationException("Проверка выбора мира требует отдельного --save-dir.");
        }
        private IEnumerator Start()
        {
            var store=runtime.Session.Saves;
            var root=new WorldState();WorldHistory.BeginContinuation(root);store.SaveWorld(root);
            var left=root.Copy();WorldHistory.BeginContinuation(left);left.revision=900;
            var leftSnapshot=store.SaveWorld(left);
            var right=root.Copy();WorldHistory.BeginContinuation(right);right.revision=2;
            var rightSnapshot=store.SaveWorld(right);
            store.SelectContinuation(leftSnapshot);
            yield return UiPointerProbe.Click(runtime.Hud.WorldPickerButtonRect);
            yield return UiPointerProbe.Click(runtime.Hud.WorldChoiceRect(rightSnapshot.Id));
            if(store.SelectedSnapshot().Id!=rightSnapshot.Id) {Finish(false,"UI не выбрал указанную ветвь.");yield break;}
            yield return UiPointerProbe.Click(runtime.Hud.HostButtonRect);
            while(runtime.Session.LocalActor==null && !finished) yield return null;
            if(finished) yield break;
            var world=runtime.Session.ServerWorld;
            bool correct=world.worldId==right.worldId && world.branchId!=right.branchId &&
                world.ancestors.Any(a=>a.branchId==right.branchId && a.sequence==right.checkpointSequence) &&
                !world.ancestors.Any(a=>a.branchId==left.branchId) &&
                runtime.Session.Saves.Continuations().Any(s=>s.Id==leftSnapshot.Id);
            Finish(correct,"Виртуальные UI-клики: выбрана другая ветвь с меньшей ревизией; хост продолжил её, альтернативная ветвь сохранена.");
        }
        private void Update() {if(!finished && Time.unscaledTime>deadline) Finish(false,"Таймаут UI-проверки выбора мира.");}
        private void Finish(bool success,string detail)
        {
            finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail},true));
            Application.Quit(success?0:1);
        }
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
