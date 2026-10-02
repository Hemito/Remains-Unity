using System;
using UnityEngine;

namespace Remains.Content
{
    [Serializable]
    public sealed class TelekinesisSettings
    {
        public float maximumMass,freeMass,range,releaseRange,manaPerSecondPerRootKg;
        public float recoveryAcceleration;
        public void Validate()
        {
            foreach(float value in new[]{maximumMass,range,releaseRange})
                if(!float.IsFinite(value) || value<=0) throw new InvalidOperationException("Пределы телекинеза должны быть конечными положительными числами.");
            if(!float.IsFinite(recoveryAcceleration) || recoveryAcceleration<0 || !float.IsFinite(freeMass) || freeMass<0 || freeMass>maximumMass ||
                !float.IsFinite(manaPerSecondPerRootKg) || manaPerSecondPerRootKg<0 || releaseRange<range)
                throw new InvalidOperationException("Некорректный порог, расход маны или дальность отпускания телекинеза.");
        }
        public static TelekinesisSettings Load()
        {
            var asset=Resources.Load<TextAsset>("Content/telekinesis");
            if(asset==null) throw new InvalidOperationException("Нет параметров телекинеза Content/telekinesis.");
            var result=JsonUtility.FromJson<TelekinesisSettings>(asset.text);
            if(result==null) throw new InvalidOperationException("Не удалось прочитать Content/telekinesis.");
            result.Validate();return result;
        }
    }
}
