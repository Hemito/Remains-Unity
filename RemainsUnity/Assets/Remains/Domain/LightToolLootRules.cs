using System;

namespace Remains.Domain
{
    // Additional utility supplies, independent of the original's loot stream and personal perks.
    public static class LightToolLootRules
    {
        public static void Generate(string category,Func<double> random,Action<string,int> emit)
        {
            if(random==null || emit==null)throw new ArgumentNullException();
            bool supplies=category is "instr" or "instr2" or "ammo" or "expl" or "bigexpl" or "wbattle" or "wbig" or "chest";
            if(!supplies)return;
            if(random()<.25)emit(LightTestItems.Stick,1+(int)(random()*3));
            if(random()<.08)emit(LightTestItems.Ball,1);
        }
    }
}
