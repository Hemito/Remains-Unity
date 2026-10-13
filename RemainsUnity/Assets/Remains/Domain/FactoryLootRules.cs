using System;

namespace Remains.Domain
{
    public readonly struct FactoryLootRequest
    {
        public readonly string pool,sourceId;
        // Original Item constructor's quantity argument: for weapons this is condition mode.
        public readonly int count;
        public FactoryLootRequest(string pool,string sourceId,int count)
        {this.pool=pool;this.sourceId=sourceId;this.count=count;}
    }

    // Baseline world loot, deliberately independent of the character opening the container.
    // Source: fe/serv/LootGen.as lootCont. Catalog selection and stack adaptation belong to caller.
    public static class FactoryLootRules
    {
        public static void Generate(string category,double locationDifficulty,int biome,double prize,
            Func<double> random,Func<FactoryLootRequest,bool> emit,Action<string> unsupported)
        {
            if(random==null||emit==null||unsupported==null)throw new ArgumentNullException();
            double d=Math.Min(locationDifficulty,20);
            int emitted=0;
            double U()=>random();
            int N(double scale,double offset=0)=>(int)Math.Floor(U()*scale+offset);
            bool Add(double chance,string pool,string id=null,int count=-1)
            {
                if(chance<1&&U()>chance)return false;
                bool success=emit(new FactoryLootRequest(pool,id,count));
                if(success)emitted++;return success;
            }
            void Gem(double chance)=>Add(chance,"item","gem"+N(3,1));
            void Creature(string id)=>unsupported("Появление существа из контейнера пока не поддержано: category="+category+", creature="+id+".");
            bool Infested(double chance)
            {
                if(U()>=chance)return false;
                Creature(U()<.6?"tarakan":"rat");return true;
            }
            switch(category)
            {
                case "":case "empty":return;
                case "ammo":Add(.7,"a");Add(.25,"a");Add(.15,"a");break;
                case "metal":if(!Add(.5,"item","money",(int)(U()*30*(d*.15+1)+5)))Add(1,"a");break;
                case "bomb":for(int i=0;i<3;i++)Add(1,"e","dinamit");break;
                case "expl":{int n=N(4);for(int i=0;i<n;i++)Add(1,"e");Add(.5,"compe");break;}
                case "bigexpl":{int n=N(4,3);for(int i=0;i<n;i++)Add(1,"e");Add(.5,"compe");break;}
                case "wbattle":
                    if(!Add(.04,"uniq"))Add(1,"weapon",U()<Math.Min(d/5,.7)?"4":"3",1);
                    Add(.8,"a");Add(.1,"item","stealth");break;
                case "case":Add(.9,"item","money",(int)(U()*20*(d*.11+1)+5));break;
                case "wbig":
                    if(!Add(.08,"uniq"))Add(1,"weapon",U()<.5?"5":"4",1);
                    Add(.5,"e",null,N(4));Add(.5,"a",null,N(4));break;
                case "robocell":Add(1,"compm");break;
                case "instr":case "instr2":
                    Add(.1,"item","pin",N(5,1));if(!Add(.35,"weapon","2",1))Add(.5,"item","rep");
                    Add(category=="instr"?.85:.75,"compa");Add(category=="instr"?.7:.4,"compw");
                    if(category=="instr")Add(.1,"compe");Add(.5,"compm");Add(category=="instr"?.5:.2,"paint");break;
                case "trash":
                    if(biome==0)Add(.25,"food","radcookie");
                    if(!Infested(.25)){int n=N(2,1);for(int i=0;i<n;i++)Add(1,"stuff");Add(.4,"item","money",(int)(U()*10*(d*.1+1)+5));}break;
                case "fridge":
                    Add(.5,"food","sparklecola");Add(.5,"food","sars");Add(.1,"food","radcola");
                    if(U()<.2)Creature(U()<.4?"tarakan":U()<.5?"rat":"bloat");
                    else {int n=N(2,1);for(int i=0;i<n;i++)Add(1,"food");Add(.3,"compp","herbs",N(6,1));}break;
                case "food":
                    if(biome==0)Add(.25,"food","radcookie");
                    if(!Infested(.25)){Add(.8,"food");Add(.5,"stuff");Add(.2,"compp","herbs",N(6,1));Add(.05,"co");}break;
                case "med":
                    Add(.05,"item","pin",N(2,1));{int n=N(3);for(int i=0;i<n;i++)Add(1,"med");}
                    Add(.25,"him");Add(.03,"med","firstaid");Add(.05,"pot","potHP");Add(.25,"item","gel");break;
                case "med2":
                    Add(.75,"pot","potHP");{int n=N(3,1);for(int i=0;i<n;i++)Add(1,"med");}
                    Add(1,"him");Add(.5,"med","firstaid");Add(.5,"med","doctor");Add(.5,"med","surgeon");Add(.8,"item","gel");break;
                case "table":
                    Add(.1,"item","pin",N(3,1));if(!Add(.08,"book"))Add(.5,"item","money",(int)(U()*50+5+3*d));
                    Add(.1,"food");Add(.13,"weapon","3");Add(.3,"a");Add(.1,"item","dart");Add(.1,"item","app");
                    Add(.04,"scheme");Add(.25,"food");Add(.08,"co");Add(.1,"med","potm1");break;
                case "filecab":
                    Add(.1,"item","pin",N(3,1));Add(.5,"item","money",(int)(U()*20));Add(.1,"item","app");Add(.02,"scheme");Add(.08,"co");break;
                case "cup":
                    Add(.06,"item","pin",N(3,1));Add(.3,"item","money",(int)(U()*20));Add(.25,"compa");Add(.75,"stuff");
                    Add(.2,"compa","kombu_comp");Add(.2,"compa","antirad_comp");Add(.2,"compa","antihim_comp");break;
                case "bloat":Creature("bloat");break;
                case "book":
                    if(!Add(.3,"book"))Add(1,"item","lbook");Gem(.1);Add(.25,"scheme");Add(.3,"co");break;
                case "term":case "info":
                    if(!Add(.25,"item","disc"))Add(1,"item","data");Add(.5,"compm");break;
                case "cryo":
                    {int n=N(3,1);for(int i=0;i<n;i++)Add(1,"item","pcryo");}Add(.5,"item","gel");break;
                case "chest":
                    Add(.1,"item","pin",N(5,1));Add(.2,"weapon","3",2);Add(.2,"item","bit",(int)(U()*50+7*d+2));
                    Gem(.3);Add(.25,"compa");Add(.03,"book");Add(.5,"a");Add(.03,"scheme");break;
                case "safe":
                    // Random-world enemy ambush depends on original room proof metadata, absent here.
                    // Caller must reject unsupported authored ambush semantics before entering this baseline branch.
                    Add(prize/100,"uniq");Add(.1+prize/100,"item","sphera");Add(.2+prize/200,"item","stealth");
                    Gem(.25+prize/100);Add(.2,"med");Add(.25,"item","retr");Add(.1,"item","runa");Add(.1,"item","reboot");
                    Add(.2+prize/100,"book");Add(.1+prize/100,"compp");
                    Add(1,"item","bit",(int)(U()*(prize+10)*8+2+4*d));Add(.1+prize/300,"scheme");
                    Add(.25,"pot","potMP");Add(.1,"pot","potHP");if(!Add(.4,"med","potm2"))Add(.3,"med","potm3");
                    if(emitted==0)Gem(1);break;
                case "specalc":Add(1,"spec","alc7");Gem(1);break;
                case "speclp":Add(1,"spec","lp_item");Gem(1);break;
                default:unsupported("Не поддержана исходная категория лута: "+category+".");break;
            }
        }
    }
}
