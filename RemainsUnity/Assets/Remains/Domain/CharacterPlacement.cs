namespace Remains.Domain
{
    public static class CharacterPlacement
    {
        public static void SpawnInHub(ActorState actor,int slot=0)
        {
            var room=RoomLayout.Load("hub");
            actor.level="hub";
            actor.x=room.spawnX+slot*0.6f;actor.y=room.spawnY;
            actor.vx=actor.vy=0;
            ActorPose.Select(actor,"idle");
        }
    }
}
