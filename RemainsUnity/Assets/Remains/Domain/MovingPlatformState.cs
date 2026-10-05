using System;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Domain
{
    // Type data is separate from instance placement and the persisted transition.
    [Serializable]
    public sealed class MovingPlatformDefinition : ExtensibleState
    {
        public int schema=1,version=1;
        public string id;
        public float width,height;
        public float graphicOffsetX,graphicOffsetY,graphicWidth,graphicHeight;
        public bool oneWay,shotsPassThrough;
        public string surfaceTexture,movementSound;
        public double durationSeconds;
        public void Validate()
        {
            MechanismStateValidation.Require(schema==1,"definition.schema (поддерживается 1)",id);
            MechanismStateValidation.Require(!string.IsNullOrWhiteSpace(id),"definition.id",id);
            MechanismStateValidation.Require(version>0,"definition.version",id);
            MechanismStateValidation.Require(float.IsFinite(width) && width>0 && float.IsFinite(height) && height>0,"support size",id);
            MechanismStateValidation.Require(float.IsFinite(graphicOffsetX) && float.IsFinite(graphicOffsetY) &&
                float.IsFinite(graphicWidth) && graphicWidth>0 && float.IsFinite(graphicHeight) && graphicHeight>0 &&
                float.IsFinite(graphicOffsetX+graphicWidth) && float.IsFinite(graphicOffsetY+graphicHeight),"graphic bounds",id);
            MechanismStateValidation.Require(double.IsFinite(durationSeconds) && durationSeconds>0,"durationSeconds",id);
            MechanismStateValidation.Require(!string.IsNullOrWhiteSpace(surfaceTexture),"surfaceTexture",id);
            MechanismStateValidation.Require(!string.IsNullOrWhiteSpace(movementSound),"movementSound",id);
        }
    }

    [Serializable]
    public sealed class MovingPlatformSegment : ExtensibleState
    {
        public int schema=1;
        public long sequence;
        public int fromEndpoint,toEndpoint;
        public double startElapsedSeconds,durationSeconds;
        // Computed properties are not fields in StateJson; no wall/network time is persisted.
        public double EndElapsedSeconds=>startElapsedSeconds+durationSeconds;
        public void Validate(string platformId)
        {
            MechanismStateValidation.Require(schema==1,"segment.schema (поддерживается 1)",platformId);
            MechanismStateValidation.Require(sequence>0,"segment.sequence",platformId);
            MechanismStateValidation.Require(fromEndpoint is 0 or 1 && toEndpoint is 0 or 1 && fromEndpoint!=toEndpoint,"segment endpoints",platformId);
            MechanismStateValidation.Require(double.IsFinite(startElapsedSeconds) && startElapsedSeconds>=0,"segment.startElapsedSeconds",platformId);
            MechanismStateValidation.Require(double.IsFinite(durationSeconds) && durationSeconds>0 &&
                double.IsFinite(EndElapsedSeconds) && EndElapsedSeconds>startElapsedSeconds,"segment.durationSeconds/end",platformId);
        }
    }

    [Serializable]
    public sealed class MovingPlatformState : ExtensibleState
    {
        public int schema=1;
        public string id,definition,level,roomId,groupId;
        public int definitionVersion,endpoint;
        public float startX,startY,endX,endY;
        public long revision;
        public bool removed;
        public MovingPlatformSegment segment;
        public Vector2 Start=>new(startX,startY);
        public Vector2 End=>new(endX,endY);
        public void Validate()
        {
            MechanismStateValidation.Placement(schema,id,definition,level,roomId,groupId,revision);
            MechanismStateValidation.Require(definitionVersion>0,"definitionVersion",id);
            MechanismStateValidation.Require(float.IsFinite(startX) && float.IsFinite(startY) && float.IsFinite(endX) && float.IsFinite(endY) &&
                (startX!=endX || startY!=endY),"route endpoints",id);
            MechanismStateValidation.Require(endpoint is 0 or 1,"endpoint",id);
            if(segment!=null)
            {
                segment.Validate(id);
                MechanismStateValidation.Require(segment.fromEndpoint==endpoint,"segment.fromEndpoint != last committed endpoint",id);
            }
            // Removed records may retain their last segment for history. Sampling them is forbidden.
        }
        public void Validate(MovingPlatformDefinition type)
        {
            if(type==null) throw new ArgumentNullException(nameof(type));
            type.Validate();Validate();
            MechanismStateValidation.Require(definition==type.id && definitionVersion==type.version,"definition ID/version mismatch",id);
            MechanismStateValidation.Require(segment==null || segment.durationSeconds==type.durationSeconds,"segment duration != definition version",id);
        }
    }
}
