using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Runtime
{
    // Opt-in live run of numbered factories (user 2026-11-06): the host walks hub → −1 → hub → 1 → hub through the ordinary
    // travel request, so the host creates each number on the first visit and the host's own client shows it. A second run
    // on the same save (factory-number-reload) proves a world that already holds them starts and enters them again.
    [DefaultExecutionOrder(10000)]
    public sealed class FactoryNumberProbe : MonoBehaviour
    {
        private const float HoldSeconds=3,DeadlineSeconds=300;
        private static readonly int[] Route={-1,1};
        [Serializable] private sealed class Visit
        {
            public string level,profile;
            public float seconds;
            public int actors,raiders,shooters,containers,doors;
        }
        [Serializable] private sealed class Report
        {
            public bool success,reload;public string detail;public int factories,errors;
            public Visit[] visits;public string[] log;
        }
        private PrototypeRuntime runtime;
        private readonly List<Visit> visits=new();
        private readonly Dictionary<string,int> log=new(StringComparer.Ordinal);
        private int step,exitCode;
        private bool finished,reload;
        private float deadline,hold,requested;
        private string travelingTo;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;reload=value.Options.Smoke=="factory-number-reload";
            deadline=Time.unscaledTime+DeadlineSeconds;
            Application.logMessageReceived+=Log;
        }
        private void OnDestroy()=>Application.logMessageReceived-=Log;
        private void Log(string message,string stack,LogType type)
        {
            if(type is LogType.Log or LogType.Warning) return;
            string line=type+": "+message.Split('\n')[0];
            if(line.Length>300) line=line.Substring(0,300);
            log[line]=log.TryGetValue(line,out int count)?count+1:1;
        }
        private void Update()
        {
            if(finished) return;
            try {Tick();}
            catch(Exception error) {Debug.LogException(error);Finish(false,"Сбой проверки: "+error.Message);}
        }
        private void Tick()
        {
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут: шаг {step}, цель {travelingTo}.");return;}
            var session=runtime.Session;var actor=session.LocalActor;
            if(actor==null || session.Travel.Pending || !runtime.LevelPresentationReady || runtime.ReleasingUnusedAssets) return;
            if(Time.unscaledTime<hold) return;
            if(actor.level=="hub")
            {
                if(travelingTo!=null) {Finish(false,$"Переход на {travelingTo} не состоялся.");return;}
                if(step>=Route.Length) {Done();return;}
                travelingTo=WorldFactories.Level(Route[step]);requested=Time.unscaledTime;
                session.Travel.RequestFactory(Route[step]);return;
            }
            if(actor.level==travelingTo)
            {
                Record(actor.level);travelingTo=null;step++;hold=Time.unscaledTime+HoldSeconds;return;
            }
            if(WorldFactories.IsFactoryLevel(actor.level) && travelingTo==null) {session.Travel.RequestHub();hold=Time.unscaledTime+.5f;}
        }
        private void Record(string level)
        {
            var world=runtime.Session.ServerWorld;
            var factory=WorldFactories.Find(world,level)??throw new InvalidOperationException("Хост не записал завод "+level);
            if(WorldFactories.Find(runtime.Session.World,level)==null) throw new InvalidOperationException("Клиент не получил завод "+level);
            var actors=world.actors.Where(a=>a.level==level && !a.player).ToArray();
            var visit=new Visit {level=level,profile=factory.profile,seconds=Time.unscaledTime-requested,actors=actors.Length,
                raiders=actors.Count(a=>a.actorType.StartsWith("raider",StringComparison.Ordinal)),
                shooters=actors.Count(a=>a.actorType.StartsWith("raider5",StringComparison.Ordinal) || a.actorType.StartsWith("raider7",StringComparison.Ordinal)),
                containers=world.containers.Count(c=>c.level==level),doors=world.doors.Count(d=>d.level==level)};
            visits.Add(visit);
            Debug.Log($"Номерной завод: {JsonUtility.ToJson(visit)}");
            if(visit.actors==0 || visit.containers==0 || visit.doors==0) throw new InvalidOperationException($"Завод {level} пуст: {JsonUtility.ToJson(visit)}");
        }
        private void Done()
        {
            int errors=log.Values.Sum();
            Finish(errors==0 && visits.Count==Route.Length,$"Посещено {visits.Count}, ошибок в журнале {errors}.");
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;
            finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,reload=reload,detail=detail,
                factories=runtime.Session.ServerWorld?.factories.Count??0,errors=log.Values.Sum(),visits=visits.ToArray(),
                log=log.Select(e=>e.Value+" × "+e.Key).ToArray()},true));
            Debug.Log("Номерные заводы: "+(success?"PASS":"FAIL")+"; "+detail);
            exitCode=success?0:1;Invoke(nameof(Exit),2);
        }
        private void Exit()=>Application.Quit(exitCode);
    }
}
