using UnityEngine;

namespace Remains.Presentation
{
    public enum MotionContact { None, Step, Ladder, Landing }

    // Cosmetic path sampling. Never changes simulation state or uses animation frames.
    public sealed class MotionContacts
    {
        private bool initialized,previousGrounded,previousClimbing;
        private Vector2 previous;
        private float traveled,highestAirY;
        private string previousLevel;
        private int previousGeneration;

        public MotionContact Sample(Vector2 position,string level,int generation,bool grounded,bool climbing,
            bool suppressed,float stepDistance,float ladderDistance,float teleportDistance)
        {
            bool reset=!initialized || previousLevel!=level || previousGeneration!=generation ||
                Vector2.Distance(previous,position)>teleportDistance || suppressed;
            var contact=MotionContact.None;
            if(reset)
            {traveled=0;highestAirY=position.y;}
            else if(climbing)
            {
                if(!previousClimbing) traveled=0;
                traveled+=Mathf.Abs(position.y-previous.y);
                if(traveled>=ladderDistance) {traveled%=ladderDistance;contact=MotionContact.Ladder;}
                highestAirY=position.y;
            }
            else if(!grounded)
            {traveled=0;highestAirY=Mathf.Max(highestAirY,position.y);}
            else
            {
                if(!previousGrounded && !previousClimbing && highestAirY-position.y>=.15f)
                    contact=MotionContact.Landing;
                else if(previousGrounded && !previousClimbing)
                {
                    traveled+=Mathf.Abs(position.x-previous.x);
                    if(traveled>=stepDistance) {traveled%=stepDistance;contact=MotionContact.Step;}
                }
                highestAirY=position.y;
            }
            initialized=true;previous=position;previousGrounded=grounded;previousClimbing=climbing;
            previousLevel=level;previousGeneration=generation;
            return contact;
        }
    }
}
