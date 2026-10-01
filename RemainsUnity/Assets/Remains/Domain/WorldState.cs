using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remains.Domain
{
    [Serializable]
    public sealed class ItemStack
    {
        public string id;
        public string definition;
        public int count;
    }

    [Serializable]
    public sealed class ActorState
    {
        public string id;
        public string character;
        public string owner;
        public string target;
        public string level = "factory:0";
        public string anatomy = "pony";
        public int generation;
        public int lifecycleSequence;
        public long revision;
        public bool player;
        public bool connected;
        public bool down;
        public float health = 100;
        public float mana = 100;
        public float x;
        public float y;
        public float vx;
        public float vy;
        public int facing = 1;
        public string pose = "idle";
        public float poseTime;
        public int hair;
        public string weapon="pistol";
        public float weaponAngle;
        public float weaponTipX,weaponTipY;
        public float weaponReach=1.4f;
        public bool attackBusy;
        public float attackRemaining;
        public List<ItemStack> inventory = new();
        public int loadoutVersion;
        public List<WeaponMagazine> magazines = new();
        public List<InventoryUse> inventoryUses = new();
        public List<InjuryState> injuries = new();
        public Vector2 Position { get => new(x, y); set { x = value.x; y = value.y; } }
        public Vector2 Velocity { get => new(vx, vy); set { vx = value.x; vy = value.y; } }
        public ActorState Copy() => JsonUtility.FromJson<ActorState>(JsonUtility.ToJson(this));
    }

    [Serializable] public sealed class InjuryState { public string id; public bool periodicDamage; }

    [Serializable]
    public sealed class WorldState
    {
        public int schema = 1;
        public string worldId = Guid.NewGuid().ToString("N");
        public string branchId = Guid.NewGuid().ToString("N");
        public long revision;
        public int seed = 104;
        public bool friendlyFire = true;
        public List<ActorState> actors = new();
        public List<ItemStack> container = new();
        public List<string> completedTransactions = new();
        public List<PendingGrant> pendingGrants = new();
        public List<PropState> props = new();
        public ActorState Find(string id) => actors.Find(actor => actor.id == id);
        public WorldState Copy() => JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(this));
    }

    [Serializable]
    public sealed class PropState
    {
        public string id;
        public string owner;
        public int generation;
        public float x,y,vx,vy;
        public float mass = 8;
        public string level="factory:0";
        public bool held;
        public Vector2 Position { get=>new(x,y); set { x=value.x;y=value.y; } }
    }

    [Serializable]
    public sealed class PendingGrant
    {
        public string transaction;
        public string recipient;
        public ItemStack item;
        public long preparedRevision;
    }

    [Serializable]
    public sealed class AttackEvent
    {
        public string id;
        public string attacker;
        public string level;
        public string sourceOwner;
        public bool sourcePlayer;
        public string weapon;
        public float originX;
        public float originY;
        public float endX;
        public float endY;
        public float speed;
        public float radius;
        public Vector2 Origin => new(originX, originY);
        public Vector2 End => new(endX, endY);
    }
}
