using System;
using System.Collections.Generic;
using UnityEngine;
using Remains.Serialization;
using JsonUtility=Remains.Serialization.StateJson;

namespace Remains.Domain
{
    [Serializable]
    public sealed class ItemStack : ExtensibleState
    {
        public string id;
        public string definition;
        public int count;
        public WeaponItemState weaponState;
        public ItemStack Copy()=>StateJson.Copy(this);
        public ItemStack ReservedCopy(int quantity)
        {
            if(quantity<=0 || quantity>count) throw new ArgumentOutOfRangeException(nameof(quantity));
            if(weaponState!=null && (count!=1 || quantity!=1))
                throw new InvalidOperationException($"Оружие нельзя разделить: item={id}, definition={definition}, count={count}, quantity={quantity}.");
            var result=Copy();result.count=quantity;
            if(quantity<count) result.id=Guid.NewGuid().ToString("N");
            return result;
        }
    }

    [Serializable]
    public sealed class ActorState : ExtensibleState
    {
        public Remains.Content.ContentManifest weaponContent;
        public List<Remains.Content.ContentManifest> weaponContentHistory=new();
        public string id;
        public string character;
        public CharacterAppearance appearance;
        public string owner;
        public string target;
        public NpcPerceptionMemory perception=new();
        public string level = "factory:0";
        public string anatomy = "pony";
        public string actorType = "pony";
        public int generation;
        public int lifecycleSequence;
        public long revision;
        public bool player;
        public bool connected;
        // Version 0 saves conflated loss of connection with gameplay knockdown.
        public int controlStateVersion;
        public bool down;
        public bool climbing;
        public bool crouched;
        public int holdEpoch;
        public ActorHoldState hold;
        [NonSerialized] public float crouchPressSeconds;
        public float jumpSupportRemaining;
        public int damageSequence;
        public List<string> resolvedExplosions = new();
        public List<string> resolvedAttacks = new();
        public float health = 100;
        public float mana = 100;
        public float x;
        public float y;
        public float vx;
        public float vy;
        public int facing = 1;
        public string pose = "idle";
        public float poseTime;
        public uint poseSequence;
        [NonSerialized] public float poseCorrectionRemaining;
        public uint motionSequence;
        public double motionSentAt;
        public int motionClockGeneration=-1;
        public int hair;
        public string weapon="pistol";
        public float weaponAngle;
        public bool weaponFlipY;
        public float weaponTipX,weaponTipY;
        public float weaponReach=1.4f;
        public float weaponLift;
        public WeaponRecoilPose weaponRecoil;
        public bool attackBusy;
        public float attackRemaining;
        public string attackId,attackTarget;
        public NpcWeaponActionState npcWeaponAction;
        public float attackCooldown;
        public List<ItemStack> inventory = new();
        public CosmeticEquipmentState cosmeticEquipment; // Null is the known legacy absence of visual equipment.
        public int loadoutVersion;
        public int weaponInventoryVersion;
        public long weaponSelectionRevision;
        public string equippedWeaponItem;
        public string heldItemId;
        public float heldItemPoseX,heldItemPoseY;
        public bool heldItemPoseValid;
        public List<WeaponShortcut> weaponShortcuts=new();
        public List<WeaponMagazine> magazines = new();
        public List<InventoryUse> inventoryUses = new();
        public Dictionary<string,string> inventoryAliases = new();
        [NonSerialized] public Dictionary<string,int> inventoryReservations = new();
        public List<InjuryState> injuries = new();
        public Vector2 Position { get => new(x, y); set { x = value.x; y = value.y; } }
        // Derived body registration is transient; saves and motion packets retain the movement anchor.
        public Vector2 BodyPosition => ActorBodySupport.Position(this);
        public Vector2 Velocity { get => new(vx, vy); set { vx = value.x; vy = value.y; } }
        public ActorState Copy() => JsonUtility.FromJson<ActorState>(JsonUtility.ToJson(this));
    }

    [Serializable] public sealed class InjuryState : ExtensibleState { public string id; public bool periodicDamage; }

    [Serializable]
    public sealed class WorldState : ExtensibleState
    {
        public Remains.Content.ContentManifest weaponContent;
        public List<Remains.Content.ContentManifest> weaponContentHistory=new();
        [Newtonsoft.Json.JsonProperty(Required=Newtonsoft.Json.Required.Always)] public int schema = 1;
        [Newtonsoft.Json.JsonProperty(Required=Newtonsoft.Json.Required.Always)] public string worldId = Guid.NewGuid().ToString("N");
        public string branchId = Guid.NewGuid().ToString("N");
        public int historyVersion;
        public long checkpointSequence;
        public List<WorldAncestor> ancestors = new();
        public long revision;
        public int seed = 104;
        public bool friendlyFire = true;
        public string friendlyFireDecision=FriendlyFireDecision.Victim;
        public List<ActorState> actors = new();
        public List<string> contentPatches = new();
        public List<WorldRoom> rooms = new();
        public List<WorldFactory> factories = new();
        public int levelTimeVersion;
        public List<LevelSimulationTime> levelTimes = new();
        public int mechanismVersion;
        public List<MovingPlatformState> movingPlatforms = new();
        public List<MechanismButtonState> mechanismButtons = new();
        public List<ContainerState> containers = new();
        public List<ItemStack> container; // Legacy schema: read only by explicit migration.
        public List<string> completedTransactions = new();
        public List<PendingGrant> pendingGrants = new();
        public List<PendingDeposit> pendingDeposits = new();
        public List<PropState> props = new();
        public List<DoorState> doors = new();
        public List<LampState> lamps = new();
        public long lampRevision;
        public List<GrenadeState> grenades = new();
        public List<ProjectileState> projectiles = new();
        public List<ExplosionState> explosions = new();
        public ActorState Find(string id)
        {
            for(int i=0;i<actors.Count;i++) if(actors[i].id==id) return actors[i];
            return null;
        }
        public WorldState Copy() => JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(this));
    }

    [Serializable]
    public class PropState : ExtensibleState
    {
        public string definition="prototype-crate";
        public uint sequence;
        public string id;
        public string owner;
        public int generation;
        public float x,y,vx,vy;
        public float mass = 8;
        public bool movable=true;
        public bool hasPhysicalBody=true;
        public float width=.8f,height=.6f,anchorHeight=.3f;
        public string level="factory:0";
        public bool held;
        public Vector2 Position { get=>new(x,y); set { x=value.x;y=value.y; } }
        public Vector2 Center=>Position+Vector2.up*(height*.5f-anchorHeight);
    }

    [Serializable]
    public sealed class PendingGrant : ExtensibleState
    {
        public string transaction;
        public string recipient;
        public string containerId;
        public ItemStack item;
        public long preparedRevision;
    }

    [Serializable]
    public sealed class AttackEvent : ExtensibleState
    {
        public string friendlyFireDecision=FriendlyFireDecision.Victim;
        public string id;
        public string attacker;
        public string level;
        public string sourceOwner;
        public bool sourcePlayer;
        public string weapon;
        public RangedShotPose rangedPose;
        public float originX;
        public MeleeAttackAnchor meleeAnchor;
        public string naturalAttackType;
        public string naturalAttackTarget;
        public float originY;
        public float endX;
        public float endY;
        public float speed;
        public float radius;
        public Vector2 Origin => new(originX, originY);
        public Vector2 End => new(endX, endY);
    }
}
