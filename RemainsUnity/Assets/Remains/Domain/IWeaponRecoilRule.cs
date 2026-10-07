using System;
using Remains.Content;

namespace Remains.Domain
{
    public struct WeaponRecoilContribution {public float horizontal,angle;}
    public interface IWeaponRecoilRule
    {
        // Unsigned contribution. The owner resolves the captured side separately.
        WeaponRecoilContribution Sample(WeaponRecoilProfile profile,double ageSeconds);
    }
    public sealed class SmoothWeaponRecoilRule : IWeaponRecoilRule
    {
        public WeaponRecoilContribution Sample(WeaponRecoilProfile profile,double ageSeconds)
        {
            if(profile==null) throw new ArgumentNullException(nameof(profile),"Нет профиля для кривой отдачи.");
            if(!profile.enabled) return default;
            return new WeaponRecoilContribution
            {
                horizontal=Envelope(profile.horizontalPeak,profile.riseSeconds,profile.positionReturnSeconds,ageSeconds),
                angle=Envelope(profile.anglePeakDegrees,profile.riseSeconds,profile.angleReturnSeconds,ageSeconds)
            };
        }
        private static float Envelope(float peak,double rise,double returnSeconds,double age)
        {
            if(age<=0 || age>=rise+returnSeconds) return 0;
            double fraction=age<=rise?age/rise:1-(age-rise)/returnSeconds;
            // Independent continuous easing, with zero derivative at both ends and the peak.
            double smooth=fraction*fraction*(3-2*fraction);
            return (float)(peak*smooth);
        }
    }
    public static class WeaponRecoilSampling
    {
        // Profile is validated on load/preflight and frozen for each owner's active binding.
        public static WeaponRecoilContribution ValidateSample(IWeaponRecoilRule rule,WeaponRecoilProfile profile,double ageSeconds,string context)
        {
            if(rule==null || profile==null || !double.IsFinite(ageSeconds) || ageSeconds<0)
                throw new InvalidOperationException($"Некорректные входные данные кривой отдачи: {context}, rule={rule?.GetType().FullName}, age={ageSeconds}.");
            WeaponRecoilContribution sample;
            try {sample=rule.Sample(profile,ageSeconds);}
            catch(Exception failure)
            {throw new InvalidOperationException($"Ошибка кривой отдачи: {context}, rule={rule.GetType().FullName}, age={ageSeconds}.",failure);}
            bool neutralRequired=!profile.enabled || ageSeconds==0 || ageSeconds>=profile.DurationSeconds;
            if(!float.IsFinite(sample.horizontal) || !float.IsFinite(sample.angle) || sample.horizontal<0 || sample.angle<0 ||
                !float.IsFinite(profile.maxHorizontal) || !float.IsFinite(profile.maxAngleDegrees) ||
                sample.horizontal>profile.maxHorizontal || sample.angle>profile.maxAngleDegrees ||
                neutralRequired && (sample.horizontal!=0 || sample.angle!=0))
                throw new InvalidOperationException($"Некорректный результат кривой отдачи: {context}, rule={rule.GetType().FullName}, age={ageSeconds}, " +
                    $"horizontal={sample.horizontal}, angle={sample.angle}, maxHorizontal={profile.maxHorizontal}, maxAngleDegrees={profile.maxAngleDegrees}, neutralRequired={neutralRequired}.");
            return sample;
        }
    }
}
