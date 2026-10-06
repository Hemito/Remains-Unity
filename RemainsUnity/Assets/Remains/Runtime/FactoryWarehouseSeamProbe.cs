using System;
using System.Collections;
using System.IO;
using System.Linq;
using Mirror;
using Remains.Content;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    public struct WarehouseSeamFinish : NetworkMessage {public bool success;public string detail,snapshot;}
    // The shared seam probe changes only initial player placement. All later movement is normal input.
    [DefaultExecutionOrder(10000)]
    public sealed class FactoryWarehouseSeamProbe : MonoBehaviour
    {
        [Serializable] private sealed class Report
        {
            public bool success,traversalVerified,savedReloadVerified,reinstallVerified;
            public string detail,world,snapshot,actor,owner,level;
            public int generation,props,containers,doors,npcs;
            public Vector2 position;
        }
        private PrototypeRuntime runtime;private FactoryOfficeSeamProbe seam;
        private bool traveled,sent,finished;private float deadline;
        private bool Host=>runtime.Options.Role=="host";
        public void Initialize(PrototypeRuntime value)
        {
            runtime=value;deadline=Time.unscaledTime+65;
            if(string.IsNullOrEmpty(value.Options.SaveDirectory) || value.Options.ProbePeers is <1 or >2)
                throw new InvalidOperationException("Стык склада требует отдельное сохранение и 1–2 участников.");
            if(Host)
            {
                if(value.Session.Saves.SelectedSnapshot()!=null) throw new InvalidOperationException("Проверка стыка не заменяет существующий мир.");
                value.Session.Saves.SaveWorld(NewGameWorld.Create());
            }
            seam=new FactoryOfficeSeamProbe(value,true);runtime.CommandSource=seam.Command;
            value.Session.ClientReady+=()=>NetworkClient.RegisterHandler<WarehouseSeamFinish>(Finish);
        }
        private void Update()
        {
            if(finished) return;
            try
            {
                if(Time.unscaledTime>deadline) throw new InvalidOperationException("Таймаут прохода офис→склад.");
                var session=runtime.Session;var actor=session.LocalActor;
                if(actor==null || session.World.actors.Count(a=>a.player && a.connected)!=runtime.Options.ProbePeers) return;
                if(!traveled) {traveled=true;session.Travel.RequestFactory();}
                seam.Tick();if(seam.Failure!=null) throw new InvalidOperationException(seam.Failure);
                if(!Host || !seam.ContainerReady || sent) return;
                sent=true;StartCoroutine(PersistAndFinish());
            }
            catch(Exception error) {Finish(new WarehouseSeamFinish {detail=error.Message});}
        }
        private IEnumerator PersistAndFinish()
        {
            yield return null;
            var session=runtime.Session;WarehouseSeamFinish result;
            try
            {
                session.CommitWorld();var saved=session.Saves.LoadWorld(false);
                bool valid=RoomContentProbePersistence.UnchangedOnReinstall(saved,"factory:0") &&
                    saved.props.Count(p=>p.level=="factory:0")==29 && saved.containers.Count(p=>p.level=="factory:0")==52 &&
                    saved.doors.Count(p=>p.level=="factory:0")==17 && saved.actors.Count(a=>!a.player && a.level=="factory:0")==22;
                result=new WarehouseSeamFinish {success=valid,snapshot=RecoverySnapshot.Fingerprint(StateJson.ToJson(saved)),detail=valid?
                    "Стык офис→склад пройден в обе стороны обычным движением; актёр/владелец/уровень/опора сохранены, обычный мир записан без повторной генерации содержимого.":
                    "Сохранённый склад потерял объекты или изменился при повторной установке."};
            }
            catch(Exception error) {result=new WarehouseSeamFinish {detail=error.Message};}
            NetworkServer.SendToAll(result);
        }
        private void LateUpdate() {if(!finished) seam?.Capture();}
        private void Finish(WarehouseSeamFinish result)
        {
            if(finished) return;finished=true;var world=runtime.Session.World;var actor=runtime.Session.LocalActor;
            bool success=result.success && seam.Verified;
            File.WriteAllText(runtime.Options.Report,StateJson.ToJson(new Report {success=success,detail=result.detail,world=world?.worldId,
                snapshot=result.snapshot,traversalVerified=seam.Verified,savedReloadVerified=result.success,reinstallVerified=result.success,
                actor=actor?.id,owner=actor?.owner,level=actor?.level,generation=actor?.generation??0,position=actor?.Position??Vector2.zero,
                props=world?.props.Count(p=>p.level=="factory:0")??0,containers=world?.containers.Count(p=>p.level=="factory:0")??0,
                doors=world?.doors.Count(p=>p.level=="factory:0")??0,npcs=world?.actors.Count(a=>!a.player && a.level=="factory:0")??0},true));
            Debug.Log("Склад40 live: "+success+", "+result.detail);StartCoroutine(Quit(success));
        }
        private IEnumerator Quit(bool success) {yield return new WaitForSecondsRealtime(1.2f);Application.Quit(success?0:1);}
    }
}
