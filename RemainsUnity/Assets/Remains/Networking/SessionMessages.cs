using Mirror;

namespace Remains.Networking
{
    public struct JoinMessage : NetworkMessage
    { public string character; public string displayName; public string characterJson; public string modFingerprint; public bool acceptMismatch; }
    public struct ModMismatchMessage : NetworkMessage { public byte[] manifest; }
    public struct WelcomeMessage : NetworkMessage { public string owner; public string actor; public string worldJson; }
    public struct WorldMessage : NetworkMessage { public string json; }
    public struct ActorMessage : NetworkMessage { public string json; }
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
