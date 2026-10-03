using System;
using System.Collections;
using System.IO;
using System.Linq;
using Remains.Domain;
using UnityEngine;

namespace Remains.Runtime
{
    public sealed class NetworkAppearanceProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private float deadline;
        private bool finished;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+30;
            if(value.Options.Character=="session-client-1")
            {
                if(string.IsNullOrEmpty(value.Options.SaveDirectory)) throw new InvalidOperationException("Проверке палитры нужен отдельный профиль.");
                value.Session.Saves.SaveCharacter(new ActorState {id="player:"+value.Options.Character,character=value.Options.Character,player=true,hair=6});
            }
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,"Таймаут передачи внешности.");return;}
            var session=runtime.Session;var actors=session.World?.actors.Where(a=>a.player && a.connected).ToArray();
            if(session.LocalActor==null || actors==null || actors.Length!=runtime.Options.ProbePeers) return;
            if(actors.Single(a=>a.character=="session-client-1").hair!=6 || actors.Select(a=>a.hair).Distinct().Count()!=actors.Length)
            {Finish(false,"Импортированный цвет заменён либо новые персонажи получили одинаковые цвета.");return;}
            session.Saves.SaveCharacter(session.LocalActor);
            bool saved=session.Saves.LoadCharacter().hair==session.LocalActor.hair;
            Finish(saved,saved?"Все участники видят различные цвета; импортированный розовый сохранён, локальная запись цвета совпадает.":"Локальное сохранение потеряло цвет.");
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail},true));StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(3);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
