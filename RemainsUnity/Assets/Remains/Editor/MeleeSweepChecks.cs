using System;
using System.Collections.Generic;
using Remains.Domain;
using Remains.Gameplay;
using UnityEngine;

namespace Remains.Editor
{
    public static class MeleeSweepChecks
    {
        public static void Run()
        {
            foreach(string weapon in new[]{"spear","crowbar"})
            {
                List<MeleeSegment> reference=null;
                foreach(float dt in new[]{1f/120,1f/60,1f/30,.3f,1f})
                {
                    var origin=WeaponPose.Origin(Vector2.zero);var samples=new List<MeleeSegment>();
                    var sweep=new MeleeSweep(weapon,origin,Vector2.right);
                    for(int frame=0;frame<200 && !sweep.Finished;frame++) sweep.Advance(origin,dt,samples.Add);
                    Require(sweep.Finished && samples.Count==36,"Полная траектория при dt="+dt);
                    var target=WeaponPose.Tip(weapon,origin,Vector2.right,.5f);
                    Require(samples.Exists(s=>HitGeometry.SegmentDistance(target,s.start,s.end)<.01f),"Цель на середине дуги не пропущена: "+weapon);
                    if(reference!=null)
                        for(int i=0;i<samples.Count;i++)
                            Require(Vector2.Distance(samples[i].end,reference[i].end)<.0001f && Mathf.Abs(samples[i].speed-reference[i].speed)<.001f,"Частота кадров не меняет неподвижный взмах");
                    reference=samples;
                }
            }
            var stationary=new List<MeleeSegment>();var moving=new List<MeleeSegment>();
            var start=WeaponPose.Origin(Vector2.zero);
            new MeleeSweep("spear",start,Vector2.right).Advance(start,.3f,stationary.Add);
            new MeleeSweep("spear",start,Vector2.right).Advance(start+Vector2.right*.9f,.3f,moving.Add);
            Require(Mathf.Abs(moving[0].speed-stationary[0].speed-3)<.001f,"Скорость движения входит в скорость кончика копья");
            Debug.Log("MeleeSweepChecks PASS: длинный кадр, дуга, одинаковые сегменты 120/60/30 FPS, скорость движения.");
        }
        private static void Require(bool valid,string reason) {if(!valid)throw new InvalidOperationException("MeleeSweep: "+reason);}
    }
}
