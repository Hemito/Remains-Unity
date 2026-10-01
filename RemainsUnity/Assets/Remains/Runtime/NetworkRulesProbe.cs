using System;
using System.Collections;
using Remains.Presentation;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Runtime
{
    public struct RulesProbeFinish : NetworkMessage { }
    // Test-only rule composition; live actor state is changed by ordinary grenade commands.
    public sealed class NetworkRulesProbe : MonoBehaviour
    {
        private sealed class OneDamage : IDamageRule {public float Calculate(string weapon,float speed)=>1;}
        private sealed class EarlyDown : IKnockdownRule {public bool ShouldEnter(KnockdownContext context)=>context.remainingHealth<100;}
        private sealed class HighHealthRecovery : IPartyDefeatRule
        {
            private readonly HubPartyDefeatRule baseline=new();
            public string Notice=>"Тест правила: восстановление с 142 HP.";
            public bool IsDefeated(IReadOnlyList<PartyMemberStatus> players)=>baseline.IsDefeated(players);
            public DefeatRecovery Recovery(PartyMemberStatus member,int slot)=>new("hub",CharacterPlacement.HubPosition(slot),142,false);
        }
        private PrototypeRuntime runtime;
        private bool thrown,finished,modConsent;
        private float deadline,readyAt=-1;
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+40;
            if(!value.Options.Smoke.Contains("-external-"))
                value.Session.ConfigureRules(new GameplayRuleSet(new OneDamage(),new EarlyDown(),new HighHealthRecovery()));
            value.CommandSource=Command;
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<RulesProbeFinish>(_=>Finish(Valid()));
        }
        private IEnumerator Start()
        {
            if(!runtime.Options.Smoke.Contains("-mismatch-") || runtime.Options.Role=="host") yield break;
            var panel=runtime.Hud.SessionMods;
            while(panel.ConfirmRect==null && !finished) yield return null;
            if(finished) yield break;
            var difference=panel.Differences.SingleOrDefault(d=>d.id=="test.external");
            if(runtime.Session.LocalActor!=null || difference==null || difference.saved==difference.current ||
                !difference.saved.Contains("версия 1") || !difference.current.Contains("версия 1"))
            {Finish(false);yield break;}
            yield return UiPointerProbe.Click(panel.CancelRect);
            while(NetworkClient.active && !finished) yield return null;
            if(finished) yield break;
            if(runtime.Session.LocalActor!=null || panel.ConfirmRect!=null) {Finish(false);yield break;}
            runtime.Session.StartClient();
            while(panel.ConfirmRect==null && !finished) yield return null;
            if(finished) yield break;
            modConsent=true;yield return UiPointerProbe.Click(panel.ConfirmRect);
        }
        private PlayerCommand Command()
        {
            var self=runtime.Session.LocalActor;
            if(self==null || runtime.Session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return default;
            if(readyAt<0) readyAt=Time.unscaledTime;
            if(thrown || Time.unscaledTime-readyAt<1) return default;
            thrown=true;
            return new PlayerCommand {grenade=true,hasAim=true,aim=self.Position+Vector2.down*3};
        }
        private bool Valid()=>runtime.Session.LocalActor is {down:false,level:"hub"} actor && actor.health>100 && actor.health<=142 && actor.damageSequence>0;
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false);return;}
            if(runtime.Options.Role!="host" || runtime.Session.World==null || !Valid()) return;
            var actors=runtime.Session.World.actors.Where(a=>a.player && a.connected).ToArray();
            if(actors.Length==runtime.Options.ProbePeers && actors.All(a=>!a.down && a.health>100 && a.damageSequence>0))
                NetworkServer.SendToAll(new RulesProbeFinish());
        }
        private void Finish(bool success)
        {
            if(finished) return;finished=true;
            if(runtime.Options.Smoke.Contains("-mismatch-") && runtime.Options.Role!="host" && !modConsent) success=false;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,
                detail=$"Замена правил: grenade={thrown}, health={runtime.Session.LocalActor?.health}, down={runtime.Session.LocalActor?.down}, damage={runtime.Session.LocalActor?.damageSequence}."},true));
            Invoke(nameof(Exit),1);exitCode=success?0:1;
        }
        private int exitCode;
        private void Exit()=>Application.Quit(exitCode);
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
