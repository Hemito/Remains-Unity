using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct ContainerProbePhase : NetworkMessage {public int phase;public long clickAt;}
    public struct ContainerProbeAck : NetworkMessage {public bool pending;}
    public struct ContainerProbeFinish : NetworkMessage { }
    public sealed class NetworkContainerProbe : MonoBehaviour
    {
        private sealed class NoHealingLoot : ILootRule
        {
            private readonly NearbyLootRule normal=new();
            public string AccessRejection(LootAccessContext context)=>normal.AccessRejection(context);
            public string ClaimRejection(LootClaimContext context)=>context.definition=="heal"?"Тестовое правило: хилки остаются в контейнере.":null;
        }
        private bool ruleRequested,ruleRejected;
        private bool RuleScenario=>runtime.Options.Smoke.Contains("-rule-");
        private PrototypeRuntime runtime;
        private int phase;
        private float deadline,started;
        private bool rejected,requested,traveled,opened,clicked,acked,finished,sawPending,anyPending;
        private readonly HashSet<int> acknowledgements=new();
        private long clickAt;
        private bool Host=>runtime.Options.Role=="host";
        private bool Partial=>runtime.Options.Smoke.Contains("-partial-");
        private bool Resume=>runtime.Options.Smoke.Contains("-resume-");
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+45;started=Time.unscaledTime;
            if(RuleScenario) value.Session.ConfigureRules(new GameplayRuleSet(new PrototypeDamageRule(),new ZeroHealthKnockdownRule(),new HubPartyDefeatRule(),new NoHealingLoot()));
            value.CommandSource=()=>
            {
                if(phase>=1 && !opened && value.Session.LocalActor?.level=="factory:0" && !value.Session.Travel.Pending)
                {opened=true;return new PlayerCommand {inventory=true};}
                return default;
            };
            value.Session.Notice+=text=>{if(text=="Контейнер находится в другой локации.") rejected=true;};
            value.Session.Notice+=text=>{if(text=="Тестовое правило: хилки остаются в контейнере.") ruleRejected=true;};
            value.Session.Changed+=()=>
            {
                var actor=value.Session.LocalActor;
                if(actor!=null && value.Session.World.pendingGrants.Any(g=>g.recipient==actor.id) && !actor.inventory.Any(i=>i.definition=="caps")) sawPending=true;
            };
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<ContainerProbePhase>(m=>{phase=m.phase;clickAt=m.clickAt;});
                NetworkClient.RegisterHandler<ContainerProbeFinish>(_=>Finish(true,Partial?"Подбор частей стопки: по одной единице у каждого, остаток в контейнере, уникальные ID и отсутствие незавершённых резервов подтверждены.":Resume?"Перезапуск: общая выдача сохранена ровно у одного персонажа.":"UI-подбор: чужая локация отклонена, одновременные клики дали один предмет после резерва без предварительной выдачи."));
            };
            value.Session.ServerReady+=()=>NetworkServer.RegisterHandler<ContainerProbeAck>((connection,m)=>{acknowledgements.Add(connection.connectionId);anyPending|=m.pending;});
        }
        private void Advance(int next)
        {phase=next;clickAt=DateTime.UtcNow.AddSeconds(1.5).Ticks;NetworkServer.SendToAll(new ContainerProbePhase {phase=next,clickAt=clickAt});}
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут контейнера: phase={phase}, rejected={rejected}, pending={sawPending}, acknowledgements={acknowledgements.Count}.");return;}
            var session=runtime.Session;var actor=session.LocalActor;if(actor==null) return;
            if(Resume)
            {
                if(Host && phase==0 && session.World.actors.Count(a=>a.player && a.connected)==runtime.Options.ProbePeers) Advance(3);
            }
            else
            {
                if(phase==0 && !requested)
                {requested=true;session.RequestClaim(ContainerState.PrototypeId,session.World.containers[0].items.First(i=>i.definition=="caps").id);}
                if(Host && phase==0 && rejected && Time.unscaledTime-started>5 && session.World.actors.Count(a=>a.player && a.connected)==runtime.Options.ProbePeers)
                {
                    if(session.World.actors.Any(a=>a.inventory.Any(i=>i.definition=="caps"))) {Finish(false,"Подбор из хаба выдал предмет завода.");return;}
                    Advance(1);
                }
                if(phase==1 && !traveled) {traveled=true;session.Travel.RequestFactory();}
                if(Host && phase==1 && session.World.actors.Count(a=>a.player && a.connected && a.level=="factory:0")==runtime.Options.ProbePeers)
                {
                    if(RuleScenario)
                    {
                        var source=session.World.containers[0];
                        if(!ruleRequested) {ruleRequested=true;session.RequestClaim(source.id,source.items.First(i=>i.definition=="heal").id);}
                        if(!ruleRejected) return;
                        if(source.items.Single(i=>i.definition=="heal").count!=3 || session.World.pendingGrants.Count!=0 || actor.inventory.Where(i=>i.definition=="heal").Sum(i=>i.count)!=5)
                        {Finish(false,"Отказ правила изменил хилки или создал резерв.");return;}
                    }
                    Advance(2);
                }
                if(phase==2 && !clicked) {clicked=true;if(!Host || !runtime.Options.Smoke.Contains("-remote-")) StartCoroutine(Click());}
            }
            if(phase<2) return;
            var copies=session.World.actors.SelectMany(a=>a.inventory).Where(i=>i.definition=="caps").ToArray();
            if(session.World.pendingGrants.Count!=0) return;
            if(Partial)
            {
                var rest=session.World.containers.SelectMany(c=>c.items).Where(i=>i.definition=="caps").ToArray();
                if(copies.Length!=runtime.Options.ProbePeers || copies.Any(i=>i.count!=1) || rest.Length!=1 || rest[0].count!=20-runtime.Options.ProbePeers ||
                    copies.Select(i=>i.id).Append(rest[0].id).Distinct().Count()!=runtime.Options.ProbePeers+1 ||
                    session.World.actors.Where(a=>a.player && a.connected).Any(a=>a.inventory.Count(i=>i.definition=="caps")!=1)) return;
            }
            else if(copies.Length!=1 || copies[0].count!=20 || session.World.containers.Any(c=>c.items.Any(i=>i.definition=="caps"))) return;
            if(!acked) {acked=true;NetworkClient.Send(new ContainerProbeAck {pending=sawPending});}
            if(Host && acknowledgements.Count==runtime.Options.ProbePeers)
            {
                if(runtime.Options.Smoke.Contains("-remote-") && actor.inventory.Any(i=>i.definition=="caps")) {Finish(false,"Удалённая выдача неожиданно досталась хосту.");return;}
                if(!Resume && !anyPending) {Finish(false,"Не наблюдался резерв до выдачи.");return;}
                phase=4;NetworkServer.SendToAll(new ContainerProbeFinish());acknowledgements.Clear();
            }
        }
        private IEnumerator Click()
        {
            while(DateTime.UtcNow.Ticks<clickAt) yield return null;
            if(Partial) yield return UiPointerProbe.Click(runtime.Hud.ClaimQuantityRect);
            yield return UiPointerProbe.Click(runtime.Hud.ContainerSlotRect(0));
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail},true));StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
