using Mirror;

namespace Remains.Networking
{
    public struct PropGrabMessage : NetworkMessage { public string prop; public bool release; }
    public struct PropPoseMessage : NetworkMessage { public string prop; public int generation; public float x,y,vx,vy; public bool held; }
    public struct ThrowRequest : NetworkMessage { public string transaction; public float aimX,aimY; }
    public struct GrenadeMessage : NetworkMessage { public string id,attacker,level; public float x,y,vx,vy,fuse; }
}
