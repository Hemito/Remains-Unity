using Remains.Content;
using Remains.Domain;
namespace Remains.Gameplay
{
    // The replicated pose carries the airborne mode across ownership/save boundaries.
    public static class AirbornePose
    {
        public static string Select(ActorState actor,bool launched,bool leftSupport)
        {
            if(launched || !leftSupport && actor.pose=="jump") return "jump";
            string fall=Anatomy.FallPose(actor);
            return string.IsNullOrEmpty(fall)?"jump":fall;
        }
    }
}
