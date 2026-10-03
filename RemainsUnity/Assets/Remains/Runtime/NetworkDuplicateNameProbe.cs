using System;
using System.Collections;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Presentation;
using UnityEngine;

namespace Remains.Runtime
{
    public struct DuplicateNameProbeFinish : NetworkMessage { }
    public sealed class NetworkDuplicateNameProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private float deadline,retryAt;
        private bool rejected,retried,finished,finishing;
        private string hostOwner;
        private bool Host=>runtime.Options.Role=="host";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+40;
            if(!Host) value.Session.CharacterId="session-host";
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<DuplicateNameProbeFinish>(_=>
            {
                bool valid=Host || (rejected && retried && runtime.Session.LocalActor?.character==runtime.Options.Character);
                Finish(valid,valid?"Отказ с именем виден после отключения; повторный вход с другим именем работает, исходный игрок сохранён.":"Повторный вход не создал выбранного персонажа.");
            });
        }
        private void Update()
        {
            if(finished || finishing) return;
            var session=runtime.Session;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут проверки имени: rejected={rejected}, retried={retried}, error={session.Admission.LastError}.");return;}
            if(Host)
            {
                if(session.LocalActor!=null && hostOwner==null) hostOwner=session.LocalActor.owner;
                if(session.Admission.RejectedCount!=1) return;
                if(session.Lobby.Waiting) {session.Lobby.StartSelected();return;}
                var players=session.ServerWorld?.actors.Where(actor=>actor.player).ToArray();
                if(players==null || players.Count(actor=>actor.connected)!=2) return;
                bool valid=players.Length==2 && players.Count(actor=>actor.character=="session-host")==1 &&
                    session.LocalActor?.character=="session-host" && session.LocalActor.owner==hostOwner;
                if(!valid) {Finish(false,"Отказ изменил исходного игрока или оставил лишнее тело.");return;}
                finishing=true;NetworkServer.SendToAll(new DuplicateNameProbeFinish());return;
            }
            if(!rejected)
            {
                if(string.IsNullOrEmpty(session.Admission.LastError) || NetworkClient.active) return;
                var menu=runtime.Hud.GetComponentInChildren<MainMenuView>(true);
                string reason=session.Admission.LastError;
                if(session.World!=null || session.LocalActor!=null || menu==null || !menu.Notice.gameObject.activeInHierarchy ||
                    menu.Notice.text!=reason || !reason.Contains("«session-host»") || !reason.Contains("Выберите другое имя"))
                {Finish(false,"Причина отказа не видна в главном меню либо клиент вошёл в мир.");return;}
                rejected=true;retryAt=Time.unscaledTime+2;return;
            }
            if(!retried && Time.unscaledTime>=retryAt)
            {
                retried=true;
                var menu=runtime.Hud.GetComponentInChildren<MainMenuView>(true);
                menu.Character.text=runtime.Options.Character;
                menu.Address.text=runtime.Options.Address;
                menu.Join.onClick.Invoke();
            }
        }
        private void Finish(bool success,string detail)
        {
            if(finished) return;finished=true;
            File.WriteAllText(runtime.Options.Report,JsonUtility.ToJson(new Report {success=success,detail=detail},true));StartCoroutine(Exit(success));
        }
        private IEnumerator Exit(bool success) {yield return new WaitForSecondsRealtime(2);Application.Quit(success?0:1);}
        [Serializable] private sealed class Report {public bool success;public string detail;}
    }
}
