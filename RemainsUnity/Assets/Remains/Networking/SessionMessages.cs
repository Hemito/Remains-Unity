using Mirror;

namespace Remains.Networking
{
    // The character profile travels as UTF-8 bytes (JoinCharacter): a Mirror string is limited to 64 KB.
    public struct JoinMessage : NetworkMessage
    { public string character; public string displayName; public byte[] characterUtf8; public string modFingerprint; public bool acceptMismatch; }
    public static class JoinCharacter
    {
        public static byte[] Encode(string json)=>json==null?null:new System.Text.UTF8Encoding(false,true).GetBytes(json);
        public static string Decode(byte[] utf8)=>utf8==null || utf8.Length==0?null:new System.Text.UTF8Encoding(false,true).GetString(utf8);
        // The whole message must fit one reliable transport message; fail with a clear reason before sending.
        public static void RequireFits(JoinMessage message,int maxBytes)
        {
            int size=(message.characterUtf8?.Length??0)+(message.character?.Length??0)*3+(message.displayName?.Length??0)*3+(message.modFingerprint?.Length??0)*3+64;
            if(size>maxBytes)
                throw new System.InvalidOperationException($"Профиль персонажа слишком велик для подключения: около {size} байт при пределе транспорта {maxBytes}. Персонаж={message.character}.");
        }
    }
    public struct ModMismatchMessage : NetworkMessage { public byte[] manifest; }
    public struct WelcomeMessage : NetworkMessage { public string owner; public string actor; public string worldJson; }
    public struct WorldMessage : NetworkMessage { public string json; }
    // An actor's state: whole (json), or, after a previous message of the same stream, as its template plus the
    // changed cold texts (ActorPiecesSender); actor/sequence/baseSequence identify the stream position.
    public struct ActorMessage : NetworkMessage
    {
        public string json;
        public string actor;
        public uint baseSequence,sequence;
        public string template;
        public string[] cold;
    }
    public struct PoseMessage : NetworkMessage
    {
        public string actor;
        public int generation;
        public int lifecycleSequence;
        public uint sequence;
        public double sentAt;
        public float x, y, vx, vy;
        public int facing;
        public string pose;
        public float poseTime;
        public uint poseSequence;
        public bool climbing;
        public bool crouched;
        public float jumpSupportRemaining;
        public string weapon;
        public float weaponAngle,weaponTipX,weaponTipY;
        public float weaponReach,weaponLift;
        public bool weaponFlipY;
        public long weaponSelectionRevision;
        public Remains.Domain.WeaponRecoilPose weaponRecoil;
        public float heldItemPoseX,heldItemPoseY;
        public bool heldItemPoseValid;
        public string attackId;
        public bool attackBusy;
        public float attackRemaining,attackCooldown;
        public string target;
        public Remains.Domain.NpcPerceptionMemory perception;
    }
    public struct AttackMessage : NetworkMessage { public string json; }
    public struct NpcHitMessage : NetworkMessage { public string target; public int generation; public string attackJson; }
    public struct FriendlyHitMessage : NetworkMessage {public string target;public int generation,lifecycle;public string attackJson;}
    public struct ClaimMessage : NetworkMessage { public string transaction; public string container;public string item;public int quantity;public int actorGeneration,actorLifecycle; }
    // Acceptance means a prepared reservation, never an already awarded item.
    public struct ClaimReplyMessage : NetworkMessage {public string transaction;public bool accepted;public string reason;}
    public struct ReviveMessage : NetworkMessage { public string target; public string transaction; public bool selfHeal; public int healerGeneration,healerLifecycle,targetGeneration,targetLifecycle,damageSequence; public string item; }
    public struct HealingReplyMessage : NetworkMessage {public string transaction;public bool success;public string reason;}
    public struct NoticeMessage : NetworkMessage { public string text; }
    public struct BackupAckMessage : NetworkMessage { public string world; public long revision; public string snapshot; }
}
