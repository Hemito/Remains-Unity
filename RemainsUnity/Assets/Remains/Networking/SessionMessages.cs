using Mirror;

namespace Remains.Networking
{
    public struct JoinMessage : NetworkMessage
    { public string character; public string displayName; public string characterJson; public string modFingerprint; public bool acceptMismatch; }
    public struct ModMismatchMessage : NetworkMessage { public string description; }
    public struct WelcomeMessage : NetworkMessage { public string owner; public string actor; public string worldJson; }
    public struct WorldMessage : NetworkMessage { public string json; }
    public struct ActorMessage : NetworkMessage { public string json; }
    public struct PoseMessage : NetworkMessage
    {
        public string actor;
        public int generation;
        public int lifecycleSequence;
        public uint sequence;
        public float x, y, vx, vy;
        public int facing;
        public string pose;
        public float poseTime;
        public string weapon;
        public float weaponAngle,weaponTipX,weaponTipY;
        public float weaponReach;
        public string attackId;
        public bool attackBusy;
        public float attackRemaining,attackCooldown;
    }
    public struct AttackMessage : NetworkMessage { public string json; }
    public struct NpcHitMessage : NetworkMessage { public string target; public int generation; public string attackJson; }
    public struct ClaimMessage : NetworkMessage { public string transaction; public string container;public string item; }
    public struct ReviveMessage : NetworkMessage { public string target; public string transaction; public bool selfHeal; public int healerGeneration,healerLifecycle,targetGeneration,targetLifecycle,damageSequence; }
    public struct HealingReplyMessage : NetworkMessage {public string transaction;public bool success;public string reason;}
    public struct NoticeMessage : NetworkMessage { public string text; }
    public struct BackupAckMessage : NetworkMessage { public string world; public long revision; public string snapshot; }
}
