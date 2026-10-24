using System;
using UnityEngine;

namespace Remains.Content
{
    [Serializable]
    public sealed class MovementSettings
    {
        public float walkSpeed,runSpeed,climbSpeed,jumpSpeed,dropSpeed,doubleTapSeconds;
        public float groundAcceleration,groundBraking,airAcceleration,airBraking,climbDownMultiplier;
        public float actorGravity,jumpSupportSeconds;
        public float crouchSpeed,crouchSolidSeconds,crouchPlatformSeconds;
        public float sprintPreparationSeconds;
        public float stepHeight;
        public void Validate()
        {
            foreach(float value in new[]{walkSpeed,runSpeed,climbSpeed,jumpSpeed,dropSpeed,doubleTapSeconds,
                groundAcceleration,groundBraking,airAcceleration,airBraking,climbDownMultiplier,actorGravity,jumpSupportSeconds,crouchSpeed,crouchSolidSeconds,crouchPlatformSeconds})
                if(!float.IsFinite(value) || value<=0) throw new InvalidOperationException("Параметры движения должны быть конечными положительными числами.");
            if(runSpeed<walkSpeed) throw new InvalidOperationException("Скорость бега ниже скорости ходьбы.");
            if(!float.IsFinite(sprintPreparationSeconds) || sprintPreparationSeconds<0)
                throw new InvalidOperationException("Время подготовки бега должно быть конечным неотрицательным числом.");
            if(!float.IsFinite(stepHeight) || stepHeight<0)
                throw new InvalidOperationException("Высота перешагивания должна быть конечной и неотрицательной.");
        }
        public static MovementSettings Load()
        {
            var asset=GameAssets.Load<TextAsset>("Content/movement");
            if(asset==null) throw new InvalidOperationException("Нет параметров движения Content/movement.");
            var result=JsonUtility.FromJson<MovementSettings>(asset.text);
            if(result==null) throw new InvalidOperationException("Не удалось прочитать параметры движения Content/movement.");
            result.Validate();return result;
        }
    }
}
