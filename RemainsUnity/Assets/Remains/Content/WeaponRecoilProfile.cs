using System;
using System.IO;
using UnityEngine;

namespace Remains.Content
{
    // Independent Unity calibration; original raw recoil values are provenance, not these units.
    [Serializable]
    public sealed class WeaponRecoilProfile
    {
        [InspectorName("Версия отдачи")] public int version=1;
        [InspectorName("Отдача включена")] public bool enabled;
        [InspectorName("Пик отвода (м)")] public float horizontalPeak;
        [InspectorName("Пик подъёма (градусы)")] public float anglePeakDegrees;
        [InspectorName("Время нарастания (с)")] public float riseSeconds;
        [InspectorName("Возврат положения (с)")] public float positionReturnSeconds;
        [InspectorName("Возврат угла (с)")] public float angleReturnSeconds;
        [InspectorName("Предел отвода (м)")] public float maxHorizontal;
        [InspectorName("Предел угла (градусы)")] public float maxAngleDegrees;
        public double DurationSeconds=>(double)riseSeconds+Math.Max(positionReturnSeconds,angleReturnSeconds);

        public static WeaponRecoilProfile Disabled()=>new();
        public static WeaponRecoilProfile PistolCalibration()=>new()
        {
            enabled=true,horizontalPeak=.08f,anglePeakDegrees=4,riseSeconds=.035f,
            positionReturnSeconds=.22f,angleReturnSeconds=.55f,maxHorizontal=.16f,maxAngleDegrees=12
        };
        public static WeaponRecoilProfile EnergyCalibration()=>new()
        {
            enabled=true,horizontalPeak=.04f,anglePeakDegrees=1.5f,riseSeconds=.025f,
            positionReturnSeconds=.16f,angleReturnSeconds=.30f,maxHorizontal=.08f,maxAngleDegrees=5
        };
        public WeaponRecoilProfile Copy()=>new()
        {
            version=version,enabled=enabled,horizontalPeak=horizontalPeak,anglePeakDegrees=anglePeakDegrees,
            riseSeconds=riseSeconds,positionReturnSeconds=positionReturnSeconds,angleReturnSeconds=angleReturnSeconds,
            maxHorizontal=maxHorizontal,maxAngleDegrees=maxAngleDegrees
        };
        public bool SameValues(WeaponRecoilProfile other)=>other!=null && version==other.version && enabled==other.enabled &&
            horizontalPeak==other.horizontalPeak && anglePeakDegrees==other.anglePeakDegrees && riseSeconds==other.riseSeconds &&
            positionReturnSeconds==other.positionReturnSeconds && angleReturnSeconds==other.angleReturnSeconds &&
            maxHorizontal==other.maxHorizontal && maxAngleDegrees==other.maxAngleDegrees;

        public void Validate(string context)
        {
            bool numbers=float.IsFinite(horizontalPeak) && float.IsFinite(anglePeakDegrees) && float.IsFinite(riseSeconds) &&
                float.IsFinite(positionReturnSeconds) && float.IsFinite(angleReturnSeconds) &&
                float.IsFinite(maxHorizontal) && float.IsFinite(maxAngleDegrees);
            bool valid=version==1 && numbers && (enabled?
                horizontalPeak>=0 && anglePeakDegrees>=0 && (horizontalPeak>0 || anglePeakDegrees>0) &&
                riseSeconds>0 && positionReturnSeconds>0 && angleReturnSeconds>0 &&
                maxHorizontal>=horizontalPeak && maxAngleDegrees>=anglePeakDegrees:
                horizontalPeak==0 && anglePeakDegrees==0 && riseSeconds==0 && positionReturnSeconds==0 &&
                angleReturnSeconds==0 && maxHorizontal==0 && maxAngleDegrees==0);
            if(!valid) throw new InvalidDataException($"Некорректный профиль отдачи: {context}, version={version}, enabled={enabled}, " +
                $"horizontalPeak={horizontalPeak}, anglePeakDegrees={anglePeakDegrees}, riseSeconds={riseSeconds}, " +
                $"positionReturnSeconds={positionReturnSeconds}, angleReturnSeconds={angleReturnSeconds}, maxHorizontal={maxHorizontal}, maxAngleDegrees={maxAngleDegrees}.");
        }
        // Call at catalog load AND owner preflight, before spending ammo or mana.
        public static WeaponRecoilProfile ValidateForWeapon(WeaponDefinition weapon,string context,Action<string> warning=null)
        {
            if(weapon==null) throw new ArgumentNullException(nameof(weapon),$"Нет определения для отдачи: {context}.");
            var profile=weapon.recoil;
            if(profile==null)
            {
                if(weapon.projectileSpeed>0)
                {
                    string reason=$"Оружие без профиля отдачи использует явную legacy-neutral policy: {context}, weapon={weapon.id}. " +
                        "Задайте version=1 и enabled=false либо полный профиль; исходные recoil5/2 не являются настройкой Unity.";
                    if(warning!=null) warning(reason);else Debug.LogWarning(reason);
                }
                return null;
            }
            profile.Validate($"{context}, weapon={weapon.id}");
            if(profile.enabled && (weapon.projectileSpeed<=0 || weapon.meleeMotion!=null || weapon.id=="grenade"))
                throw new InvalidDataException($"Отдача неприменима к этому определению: {context}, weapon={weapon.id}, projectileSpeed={weapon.projectileSpeed}.");
            return profile;
        }
    }
}
