namespace Remains.Domain
{
    public static class CharacterPlacement
    {
        public static UnityEngine.Vector2 HubPosition(int slot)
        {
            var room=RoomLayout.Load("hub");
            return new UnityEngine.Vector2(room.spawnX+slot*0.6f,room.spawnY);
        }
        public static void SpawnInHub(ActorState actor,int slot=0)
        {
            actor.level="hub";
            actor.Position=HubPosition(slot);
            actor.vx=actor.vy=0;
            ActorPose.Select(actor,"idle");
        }
    }
}
