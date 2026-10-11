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
        private readonly Action<IRoomLighting,RoomLayout,bool> bind;
        private Coroutine preparation;
        private EntryPreparationContext context;
        private GameObject roomObject;
        public IRoomLighting Light {get;private set;}
        public RoomLighting Lighting=>Light as RoomLighting;
        public RoomLayout Room {get;private set;}
        public bool IsPrepared {get;private set;}
        public bool HasRoom=>roomObject!=null;
        public event Action<EntryPreparationContext> Prepared;

        public EntryRoomPreparation(MonoBehaviour runner,CoopSession session,SimulationGeometry geometry,
            Action<IRoomLighting,RoomLayout,bool> bind)
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
                    Light=roomObject.GetComponent<IRoomLighting>();
                    if(Light==null) throw new InvalidOperationException("Подготовка входа: отсутствует свет локации "+value.Level);
                    // Factory fields are prepared explicitly; staged artwork and its global
                    // ambient must stay hidden while the previous level is still active.
                    if(Light is FactoryLevelView) roomObject.SetActive(false);
                }
                Light.Synchronize(Room,value.ViewWorld,geometry.EntryOcclusionRevision(Room));
                if(Light is FactoryLevelView factory) factory.Focus(value.ViewWorld.Find(value.ActorId).Position);
                bind(Light,Room,true);
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
                if(Light.Failure!=null) {session.ReportGameplayPreparationFailure(Light.Failure);yield break;}
                if(Light.IsReady && !Light.IsUpdating &&
                    Light.AppliedOcclusionRevision==geometry.EntryOcclusionRevision(Room) &&
                    (!(Light is FactoryLevelView factory) || factory.AppliedLampRevision==value.ViewWorld.lampRevision))
                {
                    IsPrepared=true;geometry.MarkEntryPrepared(Room);preparation=null;
                    Debug.Log($"Готова локация перед входом: world={value.WorldId}, level={value.Level}, token={value.Token}, revision={Light.AppliedOcclusionRevision}.");
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
                !ReferenceEquals(liveRoom,Room) || !Light.IsReady || Light.IsUpdating ||
                Light.AppliedOcclusionRevision!=geometry.OcclusionRevision(level) ||
                (Light is FactoryLevelView factory && factory.AppliedLampRevision!=world.lampRevision))
                throw new InvalidOperationException($"Готовый вход не совпадает с опубликованной локацией: world={world?.worldId}, level={level}, ready={IsPrepared}.");
            var result=roomObject;roomObject=null;Light=null;Room=null;context=null;IsPrepared=false;
            result.SetActive(true);
            return result;
        }
        private void ReleaseRoom()
        {
            Light?.Release();
            if(roomObject!=null) {roomObject.SetActive(false);UnityEngine.Object.Destroy(roomObject);}
            roomObject=null;Light=null;Room=null;IsPrepared=false;
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
