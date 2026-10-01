namespace Remains.Domain
{
    public static class PropReplication
    {
        public static bool IsNewer(uint incoming,uint current)=>unchecked((int)(incoming-current))>0;
        public static bool Accept(PropState prop,int generation,uint sequence,float x,float y,float vx,float vy)
        {
            if(prop.generation!=generation || !IsNewer(sequence,prop.sequence) ||
                !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(vx) || !float.IsFinite(vy)) return false;
            prop.sequence=sequence;prop.x=x;prop.y=y;prop.vx=vx;prop.vy=vy;return true;
        }
        public static void PreserveNewerPose(PropState local,PropState incoming,string localOwner)
        {
            if(local==null || local.generation!=incoming.generation || local.owner!=incoming.owner || local.level!=incoming.level) return;
            if(local.owner!=localOwner && !IsNewer(local.sequence,incoming.sequence)) return;
            incoming.x=local.x;incoming.y=local.y;incoming.vx=local.vx;incoming.vy=local.vy;incoming.sequence=local.sequence;
        }
    }
}
