using System;
using System.Collections;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using UnityEngine;

namespace Remains.Presentation
{
    // Owns only staged presentation resources. No actor is admitted, simulated or saved here.
    public sealed class EntryRoomPreparation : IDisposable
    {
        private readonly MonoBehaviour runner;
        private readonly CoopSession session;
        private readonly SimulationGeometry geometry;
        private readonly Action<RoomLighting,RoomLayout,bool> bind;
        private Coroutine preparation;
        private EntryPreparationContext context;
        private GameObject roomObject;
        public RoomLighting Lighting {get;private set;}
        public RoomLayout Room {get;private set;}
        public bool IsPrepared {get;private set;}
        public bool HasRoom=>roomObject!=null;
        public event Action<EntryPreparationContext> Prepared;

        public EntryRoomPreparation(MonoBehaviour runner,CoopSession session,SimulationGeometry geometry,
            Action<RoomLighting,RoomLayout,bool> bind)
        {
            this.runner=runner??throw new ArgumentNullException(nameof(runner));
            this.session=session??throw new ArgumentNullException(nameof(session));
            this.geometry=geometry??throw new ArgumentNullException(nameof(geometry));
            this.bind=bind??throw new ArgumentNullException(nameof(bind));
            session.EntryPreparation.Requested+=Begin;
            session.EntryPreparation.Cancelled+=Cancel;
        }
        private void Begin(EntryPreparationContext value)
        {
            try
            {
                if(preparation!=null) {runner.StopCoroutine(preparation);preparation=null;}
                Remains.Content.FactoryContentSources.Prepare(value.ViewWorld,value.Level);
                var room=geometry.PrepareEntry(value.ViewWorld,value.Level);
                if(roomObject!=null && !ReferenceEquals(room,Room)) ReleaseRoom();
                context=value;Room=room;IsPrepared=false;
                var clock=value.ViewWorld.levelTimes.Find(item=>item.level==value.Level)??
                    throw new InvalidOperationException("Подготовка входа: нет часов локации "+value.Level);
                PlatformSimulation.RestoreSupport(value.ViewWorld,Room,value.Level,clock.elapsedSeconds);
                if(roomObject==null)
                {
                    roomObject=AuthoredRoomVisual.Create(value.Level,Room,value.ViewWorld);
                    Lighting=roomObject.GetComponent<RoomLighting>();
                    if(Lighting==null) throw new InvalidOperationException("Подготовка входа: отсутствует свет локации "+value.Level);
                }
                Lighting.Synchronize(Room,geometry.EntryOcclusionRevision(Room));
                bind(Lighting,Room,true);
                Debug.Log($"Начата подготовка входа: world={value.WorldId}, level={value.Level}, actor={value.ActorId}, token={value.Token}.");
                preparation=runner.StartCoroutine(WaitForPreparation(value));
            }
            catch(Exception error) {session.ReportGameplayPreparationFailure(error);}
        }
        private IEnumerator WaitForPreparation(EntryPreparationContext value)
        {
            // Complete Requested delivery before a cached room can report Prepared.
            yield return null;
            while(ReferenceEquals(context,value) && session.EntryPreparation.ClientPending)
            {
                if(Lighting.Failure!=null) {session.ReportGameplayPreparationFailure(Lighting.Failure);yield break;}
                if(Lighting.IsReady && !Lighting.IsUpdating &&
                    Lighting.AppliedOcclusionRevision==geometry.EntryOcclusionRevision(Room))
                {
                    IsPrepared=true;geometry.MarkEntryPrepared(Room);preparation=null;
                    Debug.Log($"Готова локация перед входом: world={value.WorldId}, level={value.Level}, token={value.Token}, revision={Lighting.AppliedOcclusionRevision}.");
                    Prepared?.Invoke(value);
                    session.EntryPreparation.MarkPrepared(value.Token);
                    yield break;
                }
                yield return null;
            }
            preparation=null;
        }
        public GameObject Take(WorldState world,string level,RoomLayout liveRoom)
        {
            if(!HasRoom) return null;
            if(!IsPrepared || context==null || world?.worldId!=context.WorldId || level!=context.Level ||
                !ReferenceEquals(liveRoom,Room) || !Lighting.IsReady || Lighting.IsUpdating ||
                Lighting.AppliedOcclusionRevision!=geometry.OcclusionRevision(level))
                throw new InvalidOperationException($"Готовый вход не совпадает с опубликованной локацией: world={world?.worldId}, level={level}, ready={IsPrepared}.");
            var result=roomObject;roomObject=null;Lighting=null;Room=null;context=null;IsPrepared=false;
            return result;
        }
        private void ReleaseRoom()
        {
            Lighting?.Release();
            if(roomObject!=null) {roomObject.SetActive(false);UnityEngine.Object.Destroy(roomObject);}
            roomObject=null;Lighting=null;Room=null;IsPrepared=false;
        }
        private void ClearPreparation()
        {
            if(preparation!=null) {runner.StopCoroutine(preparation);preparation=null;}
            context=null;geometry.ClearEntryPreparation();ReleaseRoom();
        }
        private void Cancel()
        {
            ClearPreparation();bind(null,null,false);
        }
        public void Dispose()
        {
            session.EntryPreparation.Requested-=Begin;session.EntryPreparation.Cancelled-=Cancel;
            // Unity can destroy HUD children before the runtime owner. Resource teardown
            // must not publish presentation changes to that dying interface.
            ClearPreparation();Prepared=null;
        }
    }
}
