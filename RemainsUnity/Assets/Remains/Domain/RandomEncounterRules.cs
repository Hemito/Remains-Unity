using System;

namespace Remains.Domain
{
    // Random enemies of a room as the original Land.setLocDif / Location.setRandomUnits / randomUnit / createHidden
    // (1.04 dlc-ui, identical to 1.0.2 here). Pure rules: the caller gives a deterministic random source, the room's
    // danger level (locDifLevel) and its grid position; reinforcements by alarm (kolEnSpawn) are not part of the
    // seamless world (user decision 2026-10-28).
    public static class RandomEncounterRules
    {
        // Point kinds of the original (tipn of the "up" objects): 1 small crawling, 2 ordinary, 3 small flying,
        // 4 small ceiling, 5 trap.
        public const int Crawling=1,Ordinary=2,Flying=3,Ceiling=4,Trap=5,Kinds=6;
        public static int Kind(string objectId)=>objectId switch
        {
            "enl1"=>Crawling,"enl2"=>Ordinary,"enf1"=>Flying,"enc1"=>Ceiling,"lov"=>Trap,_=>0
        };

        // Enemy family of a location (tipEnemy): 0 monsters, 1 raiders, 2 robots, ... Only biome 0 (the plant) is ported.
        public static int EnemyType(int biome,Func<double> random)
        {
            if(biome!=0) throw new NotSupportedException("Случайные враги: перенесён только биом 0 (завод), запрошен "+biome+".");
            int type=-1;
            if(random()<.25) type=1;
            if(type<0) type=(int)Math.Floor(random()*3);
            return type;
        }

        // Counts per kind (index 1..5) and hidden enemies, Land.setLocDif for biome 0.
        public static (int[] counts,int hidden) Counts(double danger,int enemyType,Func<double> random)
        {
            var counts=new int[Kinds];
            void Set(int kind,int min,int max,int extra)
            {
                counts[kind]=min+(int)Math.Floor(random()*(max-min+1));
                if(extra>0 && random()<.2) counts[kind]+=extra;
            }
            int hidden;
            if(danger<4)
            {
                Set(1,3,5,2);Set(2,2,4,0);Set(3,3,4,2);Set(4,1,2,0);Set(5,1,4,2);
                if(enemyType==6) Set(2,1,3,0);
                hidden=0;
            }
            else if(danger<10)
            {
                Set(1,3,6,2);
                if(random()<.15) Set(2,1,1,0);
                else if(enemyType==6) Set(2,2,3,0);
                else Set(2,2,5,0);
                Set(3,3,5,2);Set(4,2,3,1);Set(5,2,4,2);
                hidden=(int)Math.Floor(random()*3);
            }
            else
            {
                Set(1,4,6,2);
                if(random()<.15) Set(2,1,2,0);
                else if(enemyType==6) Set(2,3,4,0);
                else Set(2,3,6,0);
                Set(3,4,7,2);Set(4,2,4,1);Set(5,3,6,2);
                hidden=(int)Math.Floor(random()*4);
            }
            if(enemyType==5 || enemyType==10) Set(2,1,3,1);
            return (counts,hidden);
        }

        // Location.randomUnit for biome 0 (not in water). parity = (landX + landY) % 2 of the room. "" = nothing.
        public static string Unit(int kind,int enemyType,double danger,int parity,Func<double> random)
        {
            switch(kind)
            {
                case Ordinary:
                    if(enemyType==0) return "zombie";
                    if(enemyType==2)
                    {
                        if(danger>=5 && random()<.1) return "landturret";
                        if(danger>=6 && random()<Math.Min(danger/40,.3)) return "gutsy";
                        if(danger>=2 && random()<Math.Min(danger/10,.5)) return "protect";
                        return "robot";
                    }
                    return enemyType switch {3=>"slaver",4=>"merc",5=>"alicorn",6=>"zebra",_=>"raider"};
                case Crawling:
                    if(enemyType==2 && danger>=4 || enemyType==7 || enemyType==9 || enemyType==10) return parity==0?"roller":"msp";
                    if(enemyType==0 || enemyType==5 || enemyType==6)
                    {
                        if(danger>=2 && random()<Math.Min(danger/30,.5)) return "scorp";
                        return parity==0?"slime":"ant";
                    }
                    if(danger>=2 && random()<Math.Min(danger/30,.5)) return "molerat";
                    return random()<.6?"tarakan":"rat";
                case Ceiling:
                    return enemyType==0 || enemyType==5 || enemyType==6?"bloodwing":"turret";
                case Flying:
                    if(enemyType==0 || enemyType==5) return "bloat";
                    if((enemyType==1 || enemyType==3 || enemyType==4 || enemyType==6) && danger>=3) return "vortex";
                    if(enemyType==2 && danger>=3) return "spritebot";
                    if(enemyType==11 || enemyType==7 || enemyType==9 || enemyType==10 || enemyType==2 && danger>10 && random()<Math.Min(danger/40,.5)) return "dron";
                    return "";
                case Trap:
                    if(enemyType==0) return parity==0?"slmine":"trap";
                    if((enemyType==1 || enemyType==3 || enemyType==4 || enemyType==6) && parity==0)
                    {
                        if(danger>=10 && random()<.5) return "trridge";
                        return random()<.5?"trplate":"trcans";
                    }
                    return "mine";
                default: throw new ArgumentOutOfRangeException(nameof(kind),"Случайные враги: неизвестный вид точки "+kind);
            }
        }

        // Location.randomCid("mine") for biome 0: the mine a "mine" unit becomes — an ordinary mine with chance
        // min(danger/20, 0.75), otherwise the weaker hmine (the one explicit template mines are).
        public static string MineVariant(double danger,Func<double> random)=>random()<Math.Min(danger/20,.75)?"mine":"hmine";

        // Location.randomCid("raider"): the raider variant 1–2 below danger 2, 1–5 below 5, 1–9 from 5 on.
        public static int RaiderVariant(double danger,Func<double> random)=>
            (int)Math.Floor(random()*(danger>=5?9:danger>=2?5:2)+1);

        // Location.createHidden on an ordinary point: a buried zombie (dig 2), a robot cell or an alarm (not ported:
        // its reinforcements are not part of the seamless world), a trap point for zebras. "" = nothing.
        public static string Hidden(int enemyType)=>enemyType switch
        {
            0=>"zombie",2=>"robocell",6=>"lov",_=>""
        };
    }
}
