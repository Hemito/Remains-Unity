using System;
using System.Collections;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Domain;
using Remains.Networking;
using UnityEngine;

namespace Remains.Runtime
{
    public struct ProtocolProbeFinish : NetworkMessage { }
    public sealed class NetworkProtocolProbe : MonoBehaviour
    {
        private PrototypeRuntime runtime;
        private float deadline;
        private string originalWorld,originalCharacter;
        private bool finished,starting,finishing;
        private bool Host=>runtime.Options.Role=="host";
        private bool Incompatible=>runtime.Options.Character=="session-client-1";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+30;
            if(Incompatible)
            {
                if(string.IsNullOrEmpty(value.Options.SaveDirectory)) throw new InvalidOperationException("Проверка протокола требует отдельного каталога сохранений.");
                value.Session.Admission.OfferedVersion=SessionProtocol.Version+1;
                originalWorld=value.Session.Saves.SaveWorld(new WorldState {seed=789}).Json;
                originalCharacter=JsonUtility.ToJson(new ActorState {id="player:"+value.Options.Character,character=value.Options.Character,health=73});
                File.WriteAllText(Path.Combine(value.Options.SaveDirectory,"character.json"),originalCharacter);
            }
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<ProtocolProbeFinish>(_=>Finish(true,"Совместимые клиенты допущены после согласования; несовместимый клиент не вошёл в мир и список сохранений."));
        }
        private void Update()
        {
            if(finished || finishing) return;
            var session=runtime.Session;
            if(Time.unscaledTime>deadline) {Finish(false,$"Таймаут протокола: approved={session.Admission.ClientApproved}, error={session.Admission.LastError}.");return;}
            if(Incompatible)
            {
                if(string.IsNullOrEmpty(session.Admission.LastError) || NetworkClient.isConnected) return;
                bool valid=session.Admission.LastError.Contains("Несовместимый сетевой протокол") && session.World==null && session.LocalActor==null &&
                    File.ReadAllText(Path.Combine(session.Saves.DirectoryPath,"world.json"))==originalWorld &&
                    File.ReadAllText(Path.Combine(session.Saves.DirectoryPath,"character.json"))==originalCharacter;
                Finish(valid,valid?"Протокол отклонён до чтения персонажа/предложения миров; исходные world.json и character.json не изменены.":"Отказ протокола изменил профиль или допустил мир.");return;
            }
            if(!Host) return;
            if(!starting && session.Admission.RejectedCount==1 && session.Lobby.ClientPlayers==runtime.Options.ProbePeers-1)
            {
                if(session.ServerWorld!=null || session.LocalActor!=null || session.Lobby.Copies.Any(c=>c.holder=="session-client-1"))
                {Finish(false,"Несовместимый клиент попал в предыгровой выбор или мир запущен до допуска.");return;}
                starting=true;session.Lobby.StartSelected();
            }
            if(!starting || session.World==null || session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers-1) return;
            if(session.World.Find("player:session-client-1")!=null) {Finish(false,"Создан персонаж отвергнутого клиента.");return;}
            finishing=true;NetworkServer.SendToAll(new ProtocolProbeFinish());
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
