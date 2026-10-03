using Mirror;

namespace Remains.Networking
{
    public struct PropGrabMessage : NetworkMessage { public string prop; public bool release; }
    public struct PropPoseMessage : NetworkMessage { public string prop; public int generation; public uint sequence; public float x,y,vx,vy; }
    public struct ThrowRequest : NetworkMessage
    {
        public string transaction,level;
        public int generation,lifecycle;
        public float originX,originY,aimX,aimY;
    }
    public struct GrenadePoseMessage : NetworkMessage {public string id;public int generation;public uint sequence;public float x,y,vx,vy,remaining;public double simulationRemainder;public bool detonate;public string[] enemyHits;}
    public struct GrenadeMessage { public string id,attacker,level; public float x,y,vx,vy,fuse; }
}
