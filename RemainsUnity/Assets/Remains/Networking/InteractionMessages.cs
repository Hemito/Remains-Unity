using Mirror;

namespace Remains.Networking
{
    public struct PropGrabMessage : NetworkMessage { public string prop; public bool release; }
    public struct PropPoseMessage : NetworkMessage { public string prop; public int generation; public uint sequence; public float x,y,vx,vy;public bool impactFriendlyPlayers; }
    public struct ThrowRequest : NetworkMessage
    {
        public string transaction,level,item;
        public int generation,lifecycle;
        public float originX,originY,aimX,aimY,vx,vy;
        public float angle,angularVelocity;
        public bool mirrored;
    }
    public struct GrenadePoseMessage : NetworkMessage {public string id;public int generation;public uint sequence;public float x,y,vx,vy,remaining,angle,angularVelocity;public double simulationRemainder;public bool mirrored,detonate;public string[] enemyHits;}
    public struct GrenadeMessage { public string id,attacker,level; public float x,y,vx,vy,fuse; }
}
