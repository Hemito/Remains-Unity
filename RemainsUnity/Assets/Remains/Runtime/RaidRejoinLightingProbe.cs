using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Remains.Domain;
using UnityEngine;

namespace Remains.Runtime
{
    // Native regression: break a lamp after a returning player's preview is sent
    // and before the client can finish preparing its light and send Ready.
    [DefaultExecutionOrder(10000)]
    public sealed class RaidRejoinLightingProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private double deadline;
        private bool host,finished,changed,admitted,admissionMatches;
        private long preparedRevision;
        private string preparedToken,changedLamp;
        private int preparations;
        private HashSet<string> originalBroken;

        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;
            host=value.Options.Role=="host";
            deadline=Time.realtimeSinceStartupAsDouble+125;
            if(host) return;
            value.Session.EntryPreparation.Requested+=context=>
            {
                preparations++;
                var broken=context.ViewWorld.lamps.Where(lamp=>lamp.level==context.Level && lamp.broken);
                if(originalBroken==null) originalBroken=new HashSet<string>(broken.Select(lamp=>lamp.id),StringComparer.Ordinal);
                else changedLamp=broken.FirstOrDefault(lamp=>!originalBroken.Contains(lamp.id))?.id;
            };
            value.EntryPresentationPrepared+=context=>
            {
                preparedRevision=context.ViewWorld.lampRevision;
                preparedToken=context.Token;
            };
            value.Session.EntryPreparation.ClientAdmitted+=(context,world)=>
            {
                admitted=true;
                admissionMatches=preparedToken==context.Token && preparedRevision==world.lampRevision;
                if(!admissionMatches)
                    Debug.LogWarning("Проверка повторного входа: допущен клиент с устаревшим светом.");
            };
            value.Session.GameplayPreparationFailed+=error=>Finish(false,error.Message);
        }

        private void LateUpdate()
        {
            if(finished) return;
            if(Time.realtimeSinceStartupAsDouble>=deadline)
            {Finish(false,$"Таймаут повторного входа: подготовок={preparations}, допущен={admitted}.");return;}
            var session=runtime.Session;
            if(host)
            {
                if(changed || session.ServerWorld==null) return;
                var world=session.ServerWorld;
                var returning=world.actors.SingleOrDefault(ActorSessionRules.IsDisconnectedBody);
                if(returning==null || !session.EntryPreparation.TryGetServerPending(returning.character,out var pending)) return;
                var lamp=world.lamps.Where(item=>item.level==pending.Level && !item.broken)
                    .OrderBy(item=>(item.Bounds.center-returning.Position).sqrMagnitude).First();
                changed=LampOperations.Break(world,lamp,Guid.NewGuid().ToString("N"));
                session.CommitWorldAtFrameEnd();
                Debug.Log($"Проверка повторного входа: разбита {lamp.id}, подготовка={pending.Token}, лампы={world.lampRevision}.");
                return;
            }
            if(!admitted || session.LocalActor==null || !runtime.LevelPresentationReady || runtime.IsLevelPreparing) return;
            var live=session.World;
            if(!admissionMatches || preparations<2 || runtime.ActiveFactoryLighting?.AppliedLampRevision!=live.lampRevision ||
                changedLamp==null || !live.lamps.Any(lamp=>lamp.id==changedLamp && lamp.broken))
            {Finish(false,"Повторный вход не применил обновлённый свет.");return;}
            NetworkFrameCapture.Save(runtime.WorldCamera,Path.ChangeExtension(runtime.Options.Report,"png"));
            Finish(true,"Повторный вход применил новый свет до первого кадра.");
        }

        private void Finish(bool success,string detail)
        {
            if(finished) return;
            finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail,
                level=runtime.Session.LocalActor?.level,lamp=changedLamp,preparations=preparations,lampRevision=preparedRevision},true));
            StartCoroutine(Exit(success));
        }

        private static IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(2);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report
        {public bool success;public string detail,level,lamp;public int preparations;public long lampRevision;}
    }
}
