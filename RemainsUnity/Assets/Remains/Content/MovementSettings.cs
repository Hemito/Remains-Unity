using System;
using UnityEngine;

namespace Remains.Content
{
    [Serializable]
    public sealed class MovementSettings
    {
        public float walkSpeed,runSpeed,climbSpeed,jumpSpeed,dropSpeed,doubleTapSeconds;
        public void Validate()
        {
            foreach(float value in new[]{walkSpeed,runSpeed,climbSpeed,jumpSpeed,dropSpeed,doubleTapSeconds})
                if(!float.IsFinite(value) || value<=0) throw new InvalidOperationException("Параметры движения должны быть конечными положительными числами.");
            if(runSpeed<walkSpeed) throw new InvalidOperationException("Скорость бега ниже скорости ходьбы.");
        }
        public static MovementSettings Load()
        {
            var asset=Resources.Load<TextAsset>("Content/movement");
            if(asset==null) throw new InvalidOperationException("Нет параметров движения Content/movement.");
            var result=JsonUtility.FromJson<MovementSettings>(asset.text);
            if(result==null) throw new InvalidOperationException("Не удалось прочитать параметры движения Content/movement.");
            result.Validate();return result;
        }
    }
}
