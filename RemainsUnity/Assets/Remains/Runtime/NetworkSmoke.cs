using System;
using System.IO;
using System.Linq;
using Remains.Domain;
using UnityEngine;

namespace Remains.Runtime
{
    // This driver runs the production transport and actions, without desktop input.
    public sealed class NetworkSmoke : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private float deadline;
        private float nextAction;
        private bool sawDown;
        private int initialHeals;
        private bool recordedHeals;
        public void Initialize(PrototypeRuntime value) { runtime=value; deadline=Time.unscaledTime+45; }
        private void Update()
        {
            if(Time.unscaledTime>deadline) { Finish(false,"Таймаут сетевого сценария"); return; }
            var session=runtime.Session;
            var self=session.LocalActor;
            if(self==null || session.World.actors.Count(a=>a.player && a.connected)<2) return;
            var other=session.World.actors.First(a=>a.player && a.connected && a.id!=self.id);
            if(runtime.Options.Smoke=="victim")
            {
                if(self.down) sawDown=true;
                if(sawDown && !self.down && self.health==35) Finish(true,"Клиент принял попадания, вошёл в нокдаун и получил лечение");
                return;
            }
            if(!recordedHeals) { initialHeals=self.inventory.Where(i=>i.definition=="heal").Sum(i=>i.count); recordedHeals=true; }
            // Put both actors within revival distance using the same pose replication path.
            self.x=other.x-1.2f; self.y=other.y;
            if(other.down) sawDown=true;
            if(sawDown && !other.down)
            {
                int remaining=self.inventory.Where(i=>i.definition=="heal").Sum(i=>i.count);
                if(remaining==initialHeals-1) Finish(true,"Два процесса: стрельба, нокдаун, поднятие, расход одной хилки");
                return;
            }
            if(Time.unscaledTime<nextAction) return;
            nextAction=Time.unscaledTime+0.7f;
            if(other.down) runtime.Revival.Step(true,0.7f);
            else { runtime.Combat.Weapon="pistol"; runtime.Combat.Fire(self,other.Position+new Vector2(0,0.55f)); }
        }
        private void Finish(bool success,string detail)
        {
            enabled=false;
            var report=JsonUtility.ToJson(new Report { success=success, detail=detail, actor=runtime.Session.LocalActorId,
                world=runtime.Session.World?.worldId, revision=runtime.Session.World?.revision??0 },true);
            if(!string.IsNullOrEmpty(runtime.Options.Report)) File.WriteAllText(runtime.Options.Report,report);
            Debug.Log(report);
            Application.Quit(success?0:2);
        }
        [Serializable] private sealed class Report { public bool success; public string detail,actor,world; public long revision; }
    }
}
