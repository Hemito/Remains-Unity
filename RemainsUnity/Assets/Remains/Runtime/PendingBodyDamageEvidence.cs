using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Remains.Domain;
using Remains.Gameplay;
using Remains.Networking;
using Remains.Persistence;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Runtime
{
    // Test evidence only. Every actor JSON is a detached capture at a named boundary;
    // neither the preview nor a debug message can apply damage or complete admission.
    [Serializable] public sealed class PendingBodyIdentity
    {
        public string token,world,actor,character,level,fingerprint;
        public int connection;public double beganAt;
        public static PendingBodyIdentity Capture(EntryPreparationIdentity value)=>new() {
            token=value.Token,world=value.World,actor=value.Actor,character=value.Character,
            level=value.Level,fingerprint=value.Fingerprint,connection=value.Connection,beganAt=value.BeganAt};
        public bool Matches(EntryPreparationContext value)=>value!=null && token==value.Token && world==value.WorldId &&
            actor==value.ActorId && character==value.CharacterId && level==value.Level && fingerprint==value.GeometryFingerprint;
        public bool Matches(PendingBodyIdentity value)=>value!=null && token==value.token && world==value.world && actor==value.actor &&
            character==value.character && level==value.level && fingerprint==value.fingerprint && connection==value.connection;
    }
    [Serializable] public sealed class PendingBodySource
    {
        public string world,snapshot,archive,sha256,targetJson,allyJson,profileBeforeJson,profileSha256;
        public long revision;
    }
    [Serializable] public sealed class PendingBodyPreparation
    {
        public string token,world,actor,character,level,fingerprint,profileJson,profileSha256,room,lightRoom;
        public double seconds;public int frame;
        public bool worldAbsent,localActorAbsent,pending,roomPrepared,lightReady;
    }
    [Serializable] public sealed class PendingBodyReady
    {
        public string world,actor,target,pistolItem,actorJson,targetJson,firstContact,sourceDoor;
        public double seconds;public int frame,rounds;
        public long selectionRevision;public Vector2 muzzle,aim;public bool clearRay;
    }
    [Serializable] public sealed class PendingBodyDamage
    {
        public string attack,actor,level,owner,beforeJson,afterJson;
        public double seconds,beforeSeconds;public int frame,beforeFrame,generation,lifecycle;public float amount;
        public bool pending,disconnected;
    }
    [Serializable] public sealed class PendingBodyContact
    {
        public string projectile,weapon,level,target,targetType,surfaceTexture,sourceDoorId;
        public Vector2 position;public double seconds;public int frame;
    }
    [Serializable] public sealed class PendingBodyShot
    {
        public string beforeSourceJson,beforeTargetJson,launchJson,projectileJson;
        public double commandSeconds,launchSeconds;public int commandFrame,launchFrame,ordinaryFireCommands;
        public PendingBodyContact contact;public PendingBodyDamage localDamage;
    }
    [Serializable] public sealed class PendingBodyCanonical
    {
        public PendingBodyIdentity identity;public string canonicalBeforeConnectJson,expectedConnectedJson;
        public double seconds;public int frame;
        public bool publicationBeforeAdmission;
    }
    [Serializable] public sealed class PendingBodyHostProof
    {
        public PendingBodyIdentity identity;public PendingBodyShot shot;public PendingBodyCanonical canonical;
        public bool requestedConnectionVerified;public int requestedSenderConnection;
    }
    [Serializable] public sealed class PendingBodyAdmitted
    {
        public string token,welcomedJson;public long revision,pauseRevision;public double seconds;public int frame;
        public bool preparedBeforeAdmission,canonicalDamagePreserved;
    }
    [Serializable] public sealed class PendingBodyArchive
    {
        public string world,snapshot,path,sha256,actorJson;public long revision;
    }
    [Serializable] public sealed class PendingBodyReceipt
    {
        public string world,snapshot,receiptPlayerJson,expectedProfileJson,persistedProfileJson;
        public long revision;public double preparedSeconds,persistedSeconds;
        public PendingBodyArchive archive;
    }
    [Serializable] public sealed class PendingBodyFrame
    {
        public string label,png,sha256,scene,actor,target,level,clip;
        public int phase,width,height,frame,drawnFrame;public double seconds;
        public bool ready;public int cullingMask;public Vector3 minimum,maximum,viewportMinimum,viewportMaximum;
    }
    [Serializable] public sealed class PendingBodyIncomingProof
    {
        public PendingBodyPreparation requested,prepared;public PendingBodyAdmitted admitted;
        public PendingBodyReceipt receipt;public PendingBodyFrame frame;
    }
    [Serializable] public sealed class PendingBodyDamageReport
    {
        public int schema=1,participants,frameRate,pendingFrames;
        public bool success,existingSaveOnly=true,stateOverrides=false,syntheticDamage=false,delayedReady=false,originalRuntimeVerified=false;
        public string mode,detail,world,actor,target,context;
        public PendingBodySource source;public PendingBodyReady allyReady;
        public PendingBodyHostProof hostProof;public PendingBodyIncomingProof incoming;
        public PendingBodyArchive canonicalArchive;public int acknowledgedConnection;public string acknowledgedSnapshot;public long acknowledgedRevision;
        public bool readyConsumedObserved,sourceArchiveUnchanged;
        public PendingBodyFrame[] frames;
    }
    public static class PendingBodyDamageEvidence
    {
        public static void Require(bool condition,string reason)
        {if(!condition) throw new InvalidOperationException("Урон ожидающему телу: "+reason);}
        public static string Json(ActorState actor)
        {Require(actor!=null,"Отсутствует тело для frozen capture.");return StateJson.ToJson(actor);}
        public static void Same(string expected,string actual,string context)
        {
            var left=StateJson.FromJson<JObject>(expected);var right=StateJson.FromJson<JObject>(actual);
            Require(JToken.DeepEquals(left,right),"Различается полный typed actor: "+context+"; expectedHP="+left["health"]+", actualHP="+right["health"]+
                ", expectedGeneration="+left["generation"]+", actualGeneration="+right["generation"]+", expectedLife="+left["lifecycleSequence"]+", actualLife="+right["lifecycleSequence"]);
        }
        public static string ExpectedProfile(ActorState actor)
        {var copy=actor.Copy();ContentProvenance.Record(ref copy.weaponContent,ref copy.weaponContentHistory);return StateJson.ToJson(copy,true);}
        public static string ExpectedConnected(WorldState canonical,ActorState actor,string profileJson,int connection)
        {
            var detached=canonical.Copy();var body=detached.Find(actor.id);
            body=PlayerWorldEntry.Reconcile(detached,body,StateJson.FromJson<ActorState>(profileJson),out _);
            ActorSessionRules.Connect(body,"connection:"+connection);
            return Json(body);
        }
        public static void RequireDamage(PendingBodyShot shot,string target)
        {
            Require(shot?.localDamage!=null && shot.contact!=null && shot.launchJson!=null && shot.projectileJson!=null,
                "Нет настоящего launch/contact/owner damage.");
            var launch=StateJson.FromJson<AttackEvent>(shot.launchJson);var damage=shot.localDamage;
            var before=StateJson.FromJson<ActorState>(damage.beforeJson);var after=StateJson.FromJson<ActorState>(damage.afterJson);
            Require(shot.ordinaryFireCommands==1 && launch.id==damage.attack && launch.id==shot.contact.projectile &&
                launch.weapon=="pistol" && shot.contact.weapon=="pistol" && shot.contact.target==target && damage.actor==target &&
                damage.amount>0 && float.IsFinite(damage.amount) && damage.pending && damage.disconnected && !before.connected && !after.connected &&
                before.owner==after.owner && before.generation==after.generation && before.lifecycleSequence==after.lifecycleSequence &&
                !before.resolvedAttacks.Contains(launch.id) && after.resolvedAttacks.Count(id=>id==launch.id)==1 &&
                after.damageSequence==before.damageSequence+1 && after.health==before.health-damage.amount && after.health>0 && !after.down &&
                before.resolvedAttacks.SequenceEqual(after.resolvedAttacks.Where(id=>id!=launch.id)),
                "Обычный одиночный выстрел не создал ровно один положительный offline receipt: attack="+launch.id+", target="+target+", beforeHP="+before.health+", afterHP="+after.health+", amount="+damage.amount);
            var beforeFields=StateJson.FromJson<JObject>(damage.beforeJson);var afterFields=StateJson.FromJson<JObject>(damage.afterJson);
            foreach(string field in new[]{"health","damageSequence","resolvedAttacks","revision"}) {beforeFields.Remove(field);afterFields.Remove(field);}
            Require(JToken.DeepEquals(beforeFields,afterFields),"Apply изменил поля вне положительного damage/receipt/revision, включая unknown fields: "+damage.attack);
        }
        public static void RequireCanonical(PendingBodyHostProof proof,string target)
        {
            RequireDamage(proof?.shot,target);var canonical=proof.canonical;
            Require(canonical!=null && proof.identity.Matches(canonical.identity) && canonical.publicationBeforeAdmission,
                "Нет canonical publication до admission.");
            var local=StateJson.FromJson<ActorState>(proof.shot.localDamage.afterJson);
            var body=StateJson.FromJson<ActorState>(canonical.canonicalBeforeConnectJson);
            Require(!body.connected && body.id==target && body.owner==local.owner && body.generation==local.generation &&
                body.lifecycleSequence==local.lifecycleSequence && body.health==local.health && body.down==local.down &&
                body.damageSequence==local.damageSequence && body.resolvedAttacks.SequenceEqual(local.resolvedAttacks) &&
                body.resolvedExplosions.SequenceEqual(local.resolvedExplosions),
                "publication-before-admission: canonical не принял offline HP/receipt до Connect; attack="+proof.shot.localDamage.attack+
                ", canonicalHP="+body.health+", localHP="+local.health+", canonicalSeq="+body.damageSequence+", localSeq="+local.damageSequence);
            var localFields=StateJson.FromJson<JObject>(proof.shot.localDamage.afterJson);var canonicalFields=StateJson.FromJson<JObject>(canonical.canonicalBeforeConnectJson);
            // The existing owner may publish normal gravity/pose clocks while Ready
            // is preparing. No personal state, receipt or unknown field is excluded.
            foreach(string field in new[]{"revision","x","y","vx","vy","jumpSupportRemaining","poseTime","motionSequence","motionSentAt","motionClockGeneration"})
            {localFields.Remove(field);canonicalFields.Remove(field);}
            Require(JToken.DeepEquals(localFields,canonicalFields),"Canonical publication потеряла личные/unknown поля offline body: "+proof.shot.localDamage.attack);
        }
        public static PendingBodyArchive Archive(SaveStore store,RecoverySnapshot snapshot,string actor)
        {
            string path=Path.Combine(store.WorldDirectoryPath,"Worlds",snapshot.WorldId,"Snapshots",snapshot.Id+".json");
            Require(File.Exists(path) && File.ReadAllText(path)==snapshot.Json,"Не совпадает immutable archive текущей FIFO операции: "+path);
            return new PendingBodyArchive {world=snapshot.WorldId,snapshot=snapshot.Id,revision=snapshot.Revision,path=path,
                sha256=RecoverySnapshot.Fingerprint(File.ReadAllText(path)),actorJson=Json(snapshot.CreateLiveWorld().Find(actor))};
        }
        // Independent ray query composes the actual production collision consumers.
        // Free cargo is not a projectile obstacle in the current projectile contract.
        public static string FirstActor(PrototypeRuntime runtime,ActorState source,ActorState target,Vector2 origin,Vector2 end,out string door)
        {
            var room=runtime.Geometry.Get(source.level);RoomSurface surface;
            float limit=ShotObstacles.FirstContact(room,origin,end,out surface)??float.PositiveInfinity;
            door=surface?.sourceDoorId;
            var ray=new AttackEvent {id="diagnostic-ray-only",attacker=source.id,weapon="pistol",level=source.level,sourcePlayer=true,
                sourceOwner=source.owner,originX=origin.x,originY=origin.y,endX=end.x,endY=end.y};
            var contacts=AttackContacts.Capture(runtime.Session.World.actors,ray,runtime.Session.World.friendlyFire,true,limit);
            return contacts.Count>0?contacts[0].id:null;
        }
    }
}
