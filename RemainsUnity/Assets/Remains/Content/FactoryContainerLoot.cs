using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Remains.Domain;

namespace Remains.Content
{
    // Explicit generation during world preparation. No actor-specific modifiers are sampled.
    public static class FactoryContainerLoot
    {
        public const string Version="source-baseline-v1";
        public static ContainerState Generate(FactoryLootCatalog catalog,WorldState world,FactoryPlacedObject placement,
            FactoryContainerDefinition definition,int factoryIndex,Dictionary<string,int> limits,Remains.Presentation.InventoryArtwork artwork)
        {
            var xml=catalog.DefinitionXml(definition.id);var random=new Stream(world.worldId+"|"+world.seed+"|"+placement.Id+"|"+Version);
            double U()=>random.Next();
            var attributes=placement.Source.Params;
            string category=attributes.TryGetValue("cont",out var specified)?specified:(string)xml.Attribute("cont");
            double difficulty=catalog.baseDifficulty+factoryIndex+placement.Room.Cell.Y*catalog.depthDifficulty;
            var state=new ContainerState {id=placement.Id};
            // The caller supplies physical placement fields; generation owns only inventory/security.
            state.lootCategory=category;state.lootGenerationVersion=Version;
            var usage=new JObject();state.extensions=new Dictionary<string,JToken> { ["factoryLootLimitUsage"]=usage,
                ["sourceLocationDifficulty"]=difficulty,["sourceObjectType"]=definition.id,
                ["sourcePlacementParameters"]=JObject.FromObject(attributes) };
            int lockTip=(int)Number(xml,"locktip",1);
            if(attributes.TryGetValue("locktip",out var tip))lockTip=int.Parse(tip,CultureInfo.InvariantCulture);
            double low=Number(xml,"low",0);
            int GenerateLock(XElement rule)
            {
                double baseLock=Number(rule,"lock",0);int value=0;
                if(rule.Attribute("lock")!=null)
                {
                    if(rule.Attribute("lockch")==null||U()<Number(rule,"lockch",1))
                        value=(int)Math.Floor(baseLock+(lockTip is 1 or 2?(.3+U())*difficulty*.7:U()*difficulty/4));
                    if(U()<baseLock-Math.Floor(baseLock))value++;
                }
                return value;
            }
            int GenerateMine(XElement rule)
            {
                if(rule.Attribute("mine")==null)return 0;
                double mine=Number(rule,"mine",0);
                int value=rule.Attribute("minech")!=null?
                    U()<Number(rule,"minech",1)?(int)Math.Floor(U()*(mine+U()*difficulty/4+1)):0:
                    (int)Math.Floor(mine+U()*difficulty/4);
                if(value>=2&&U()<.25)value--;
                return value;
            }
            state.lockLevel=GenerateLock(xml);state.mineLevel=GenerateMine(xml);
            var parameters=new XElement("placement",attributes.Select(pair=>new XAttribute(pair.Key,pair.Value)));
            if(parameters.Attribute("lock")!=null)
            {
                double explicitLock=Number(parameters,"lock",0);
                if(explicitLock!=Math.Floor(explicitLock))throw new NotSupportedException("Не поддержана дробная явная сложность замка: "+placement.Id);
                state.lockLevel=(int)explicitLock;low=0;
                if(Number(parameters,"lock",0)==0){state.lockLevel=0;state.mineLevel=0;}
            }
            if(parameters.Attribute("mine")!=null)state.mineLevel=(int)Number(parameters,"mine",0);
            if(state.lockLevel<100)
            {
                if(lockTip is 1 or 2)
                {
                    if(low>0&&U()<low)state.lockLevel=(int)Math.Ceiling(state.lockLevel*.5);
                    state.lockLevel=Math.Min(24,state.lockLevel);
                    if(state.lockLevel>0&&low<=0&&U()<.2)state.lockLevel+=2+(int)(U()*2);
                }
                else state.lockLevel=Math.Min(7,state.lockLevel);
            }
            state.mineLevel=Math.Min(7,state.mineLevel);
            int tier=(int)Number(parameters,"locklevel",0);
            if((lockTip is 1 or 2)&&tier==0&&state.lockLevel>2)tier=Math.Min(5,(int)Math.Floor(U()*(state.lockLevel-2)/3.2+.5));
            state.extensions["sourceLockTier"]=tier;
            if(category=="empty")return state; // Source returns before processing authored items.
            string Select(FactoryLootRequest request)
            {
                string id=request.sourceId;
                if(request.pool=="weapon")
                {
                    int worth=int.TryParse(id,out int value)?value:-100;
                    if(worth>0)id=catalog.Select("weapon",Math.Max(1,1+difficulty/4+U()*2-1),worth,U);
                    if(string.IsNullOrEmpty(id))id=catalog.Select("weapon",Math.Max(1,1+difficulty/4+U()*2-1),-100,U);
                    if(string.IsNullOrEmpty(id))id=catalog.Select("weapon",-100,-100,U);
                }
                if(string.IsNullOrEmpty(id))
                {
                    double level=request.pool is "e" or "uniq"?1+difficulty/4:difficulty/2;
                    id=catalog.Select(request.pool,Math.Max(1,level+U()*2-1),-100,U)??catalog.Select(request.pool,-100,-100,U);
                }
                return id;
            }
            bool Emit(FactoryLootRequest request,int important=0)
            {
                string id=Select(request);
                if(id==null)
                {
                    UnityEngine.Debug.LogWarning($"Исходный выбор лута не дал предмета: world={world.worldId}, category={category}, pool={request.pool}, difficulty={difficulty}, stage={catalog.gameStage}, container={placement.Id}. Попытка пропущена без подстановки.");
                    return false;
                }
                int grade=0;int split=id.IndexOf('^');if(split>=0){grade=int.Parse(id.Substring(split+1),CultureInfo.InvariantCulture);id=id.Substring(0,split);}
                var source=catalog.ItemXml(id);bool weapon=source.Name.LocalName=="weapon";
                string key=(weapon?"weapon:":"item:")+id;
                int count=weapon?1:request.count<0?(int)Number(source,"kol",1):request.count;
                if(count<=0)return true;
                string limit=(string)source.Attribute("limit");
                if(important==0&&!string.IsNullOrEmpty(limit))
                {
                    int used=limits.TryGetValue(limit,out int previous)?previous:0;
                    double cap=(factoryIndex+3)*Number(source,"mlim",1);
                    if(used>=cap||source.Attribute("maxlim")!=null&&used>=Number(source,"maxlim",0))return false;
                    limits[limit]=used+1;usage[limit]=(int?)usage[limit]+1??1;
                }
                string runtime=weapon&&grade==0?id switch {"p10mm"=>"pistol","lasp"=>"energy","spear"=>"spear","grenade"=>"grenade",_=>null}:null;
                if(!weapon&&id=="grenade")runtime="grenade";
                bool workingAmmo=!weapon&&(string)source.Attribute("base")==id&&WeaponCatalog.All.Any(value=>value.ammo==id);
                ItemStack item;
                if(runtime!=null)
                {
                    item=SharedInventory.Create(runtime,runtime=="grenade"?count:1);
                    if(runtime!="grenade")item.weaponState=new WeaponItemState {rounds=0};
                }
                else item=workingAmmo?SharedInventory.Create(id,count):catalog.source.CreateFormalItem(key,grade,count);
                // An original weapon connected from the export is working: its magazine starts empty, as above.
                if(runtime==null && weapon && ExportedWeaponItems.Working(item.definition)) item.weaponState=new WeaponItemState {rounds=0};
                if(!artwork.TryGet(item.definition,out _))
                    throw new NotSupportedException($"Не подготовлена графика предмета: source={key}, container={placement.Id}.");
                // World-unique: a character carries its items into other worlds of the same seed, where the same
                // placement would otherwise generate the same IDs and reject the factory install (2026-10-14).
                item.id="loot:"+Hash(world.worldId+"|"+placement.Id+"|"+Version+"|"+state.items.Count+"|"+item.definition);
                if((!workingAmmo&&runtime!="grenade")||important>0)item.extensions=new Dictionary<string,JToken> {["sourceDefinition"]=key,["sourceGrade"]=grade};
                if(important>0)item.extensions["sourceImportant"]=important;
                if(weapon)item.extensions["sourceConditionFraction"]=request.count==0?.05+.15*U():request.count==1?.6+.25*U():1;
                state.items.Add(item);return true;
            }
            foreach(var authored in placement.Source.Items)
            {
                if(!authored.TryGetValue("id",out string id))throw new NotSupportedException("Не задан исходный ID вложенного предмета: "+placement.Id);
                int amount=authored.TryGetValue("kol",out string text)?int.Parse(text,CultureInfo.InvariantCulture):1;
                Emit(new FactoryLootRequest("",id,amount),authored.ContainsKey("imp")?2:1);
            }
            // Generated factory rooms have no original authored safe-ambush proof metadata.
            if(category=="safe"&&U()<.05)throw new NotSupportedException("Исходное появление четырёх кровомух из сейфа пока не поддержано.");
            double prize=xml.Attribute("prize")!=null?state.lockLevel+(lockTip is 1 or 2?tier*2:state.lockLevel*2)+state.mineLevel*2:50;
            FactoryLootRules.Generate(category,difficulty,1,prize,U,request=>Emit(request),reason=>throw new NotSupportedException(reason));
            return state;
        }
        private static double Number(XElement xml,string key,double absent)
        =>xml.Attribute(key) is XAttribute value?double.Parse(value.Value,CultureInfo.InvariantCulture):absent;
        private static string Hash(string value)
        {using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();}
        private sealed class Stream
        {
            private ulong state;
            public Stream(string key){using var sha=SHA256.Create();var bytes=sha.ComputeHash(Encoding.UTF8.GetBytes(key));state=BitConverter.ToUInt64(bytes,0)|1;}
            public double Next(){state^=state<<13;state^=state>>7;state^=state<<17;return (state>>11)*(1.0/9007199254740992.0);}
        }
    }
}
