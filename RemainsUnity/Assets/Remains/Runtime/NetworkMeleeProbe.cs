using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Gameplay;
using UnityEngine;
using Remains.Domain;

namespace Remains.Runtime
{
    public struct MeleeProbePhase : NetworkMessage {public int phase;}
    public struct MeleeProbeFinish : NetworkMessage {public bool success;public string detail;}
    public sealed class NetworkMeleeProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private int phase;
        private float deadline,phaseAt;
        private bool fired,finished;
        private bool configuring,uiPrepared;
        private bool UiScenario=>runtime.Options.Smoke.Contains("-ffui-");
        private bool PauseScenario=>runtime.Options.Smoke.Contains("-pause-");
        private NetworkPauseWindow pauseWindow;
        private int pausedPhase;
        private string pausedSwingId;
        private readonly Dictionary<string,int> sweeps=new();
        private bool Host=>runtime.Options.Role=="host";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+40;value.CommandSource=Command;
            if(PauseScenario) pauseWindow=new NetworkPauseWindow(this,value.Session,value.Options.ProbePeers,SwingSnapshot,
                ()=>!Host || runtime.Combat.IsMeleeActive && runtime.Combat.ActiveMeleeId==pausedSwingId && runtime.Combat.MeleeRemaining>0,
                ()=>finished,reason=>Finish(false,reason));
            value.Session.ClientReady+=()=>
            {
                NetworkClient.RegisterHandler<MeleeProbePhase>(m=>{phase=m.phase;phaseAt=Time.unscaledTime;fired=false;configuring=false;});
                NetworkClient.RegisterHandler<MeleeProbeFinish>(m=>Finish(m.success,m.detail));
            };
            value.Session.AttackReceived+=attack=>
            {if(attack.attacker=="player:session-host" && (attack.weapon=="crowbar" || attack.weapon=="spear")) sweeps[attack.id]=sweeps.GetValueOrDefault(attack.id)+1;};
        }
        private PlayerCommand Command()
        {
            var self=runtime.Session.LocalActor;if(self==null) return default;
            if(phase==0)
            {
                float target=Host?5:runtime.Options.Character=="session-client-1"?6:12;
                float delta=target-self.x;
                return new PlayerCommand {movement=new Vector2(Mathf.Abs(delta)>.03f?Mathf.Sign(delta):0,0)};
            }
            if(!Host || phase>2 || fired) return default;
            fired=true;
            return new PlayerCommand {weapon=phase<=1?"crowbar":"spear",fire=true,hasAim=true,aim=self.Position+new Vector2(5,.8f)};
        }
        private void Update()
        {
            if(finished) return;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут ближнего боя: phase={phase}, attacks={sweeps.Count}");return;}
            if(configuring) return;
            var session=runtime.Session;
            if(!Host || session.LocalActor==null || session.ServerWorld.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
            var players=session.ServerWorld.actors.Where(a=>a.player && a.connected).ToArray();
            var victim=session.ServerWorld.Find("player:session-client-1");
            if(PauseScenario && phase is 1 or 2 && pausedPhase!=phase && runtime.Combat.IsMeleeActive && runtime.Combat.MeleeRemaining<.25f)
            {pausedPhase=phase;configuring=true;StartCoroutine(PauseSwing());return;}
            if(phase==0)
            {
                if(players.Any(a=>Mathf.Abs(a.x-(a.character=="session-host"?5:a.character=="session-client-1"?6:12))>.08f || Mathf.Abs(a.vx)>.1f)) return;
                if(UiScenario && !uiPrepared) {uiPrepared=true;configuring=true;StartCoroutine(PrepareUi());return;}
                NetworkServer.SendToAll(new MeleeProbePhase {phase=1});return;
            }
            if(phase>2 || Time.unscaledTime-phaseAt<1.5f) return;
            if(victim.damageSequence!=phase || victim.health>=100 || sweeps.Count!=phase || sweeps.Values.Any(count=>PauseScenario?count!=36:count<2))
            {Complete(false,$"Ожидался один урон за взмах: phase={phase}, damageSequence={victim.damageSequence}, hp={victim.health}, attacks={sweeps.Count}.");return;}
            if(players.Where(a=>a.id!=victim.id).Any(a=>a.health!=100) || players.Any(a=>a.mana!=100))
            {Complete(false,"Ближний бой повредил наблюдателя или изменил ману.");return;}
            if(phase==1)
            {
                if(UiScenario) {configuring=true;StartCoroutine(SwitchToVictim());}
                else NetworkServer.SendToAll(new MeleeProbePhase {phase=2});
            }
            else Complete(true,"Лом и копьё: обычные команды, несколько сегментов одного взмаха, ровно один урон пострадавшему за взмах, наблюдатели и мана не затронуты.");
        }
        private void Complete(bool success,string detail)
        {phase=3;NetworkServer.SendToAll(new MeleeProbeFinish {success=success,detail=detail});}
        private void Finish(bool success,string detail)
        {
            if(finished)return;finished=true;
            if(PauseScenario && pauseWindow.Verified!=2) success=false;
            bool attacker=runtime.Options.Smoke.Contains("-attacker-");
            int expected=runtime.Options.Character=="session-client-1"?2:0;
            if(runtime.Combat.AttackerDecidedHits!=(UiScenario?expected/2:attacker?expected:0) || runtime.Combat.VictimDecidedHits!=(UiScenario?expected/2:attacker?0:expected)) success=false;
            detail+=$" Решения: attacker={runtime.Combat.AttackerDecidedHits}, victim={runtime.Combat.VictimDecidedHits}, проверенные паузы={pauseWindow?.Verified ?? 0}.";
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail},true));StartCoroutine(Exit(success));
        }
        private IEnumerator PauseSwing()
        {
            pausedSwingId=runtime.Combat.ActiveMeleeId;
            yield return pauseWindow.Run("взмах "+runtime.Combat.Weapon);
            phaseAt=Time.unscaledTime;configuring=false;
        }
        private float[] SwingSnapshot()
        {
            var session=runtime.Session;var attacker=session.World.Find("player:session-host");
            var victim=session.World.Find("player:session-client-1");
            return new[]{attacker.weaponTipX,attacker.weaponTipY,attacker.weaponAngle,attacker.weaponReach,
                session.LocalActor.poseTime,victim.health,(float)victim.damageSequence,
                Host?runtime.Combat.MeleeRemaining:0,Host?runtime.Combat.ShotCooldownRemaining:0};
        }
        private IEnumerator PrepareUi()
        {
            var session=runtime.Session;var hud=runtime.Hud;
            yield return UiPointerProbe.Click(hud.FriendlyFireButtonRect);
            while(session.World.friendlyFire && !finished) yield return null;
            if(finished) yield break;
            if(hud.FriendlyDecisionInteractable) {Complete(false,"Выбор стороны активен при выключенном FF.");yield break;}
            yield return UiPointerProbe.Click(hud.FriendlyDecisionButtonRect);
            yield return new WaitForSecondsRealtime(.5f);
            if(session.World.friendlyFireDecision!=FriendlyFireDecision.Victim) {Complete(false,"Неактивная кнопка изменила настройку.");yield break;}
            phase=-1;fired=false;
            yield return new WaitForSecondsRealtime(1.5f);
            var victim=session.ServerWorld.Find("player:session-client-1");
            if(victim.health!=100 || victim.damageSequence!=0 || sweeps.Count!=1)
            {Complete(false,"Удар при выключенном FF изменил союзника или не был выполнен.");yield break;}
            yield return UiPointerProbe.Click(hud.FriendlyFireButtonRect);
            while(!session.World.friendlyFire && !finished) yield return null;
            if(finished) yield break;
            if(!hud.FriendlyDecisionInteractable) {Complete(false,"Выбор стороны остался заблокирован после включения FF.");yield break;}
            yield return UiPointerProbe.Click(hud.FriendlyDecisionButtonRect);
            while(session.World.friendlyFireDecision!=FriendlyFireDecision.Attacker && !finished) yield return null;
            if(finished) yield break;
            sweeps.Clear();NetworkServer.SendToAll(new MeleeProbePhase {phase=1});
        }
        private IEnumerator SwitchToVictim()
        {
            yield return UiPointerProbe.Click(runtime.Hud.FriendlyDecisionButtonRect);
            while(runtime.Session.World.friendlyFireDecision!=FriendlyFireDecision.Victim && !finished) yield return null;
            if(!finished) NetworkServer.SendToAll(new MeleeProbePhase {phase=2});
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(1);Application.Quit(success?0:2);}
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
