using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Remains.Domain;

namespace Remains.Content
{
    // Prepared once from represented original units; no XML/asset lookup in physics.
    public static class PropImpactCatalog
    {
        private static readonly Dictionary<string,PropImpactBody> bodies=new(StringComparer.Ordinal);
        public static void Register(string actorType,PropImpactBody body)=>bodies[actorType]=body;
        public static bool TryGet(string actorType,out PropImpactBody body)=>bodies.TryGetValue(actorType,out body);
        public static bool ResolveBody(ActorState actor,out PropImpactBody body)
        {
            if(!TryGet(actor.actorType,out body)) return false;
            // UnitSlime.putLoc: a ceiling-hanging slime is fixed until the ceiling is lost.
            if(ActorTypes.Get(actor.actorType).behavior==NpcCreatureBehaviours.Slime && actor.creature?.inverted==true)
                body=new PropImpactBody(body.mass,body.knocked,body.immunity,true,body.immune);
            return true;
        }
        public static void Load()
        {
            bodies.Clear();
            // Unit/UnitPlayer standard difficulty defaults; hardest difficulty's 15-frame immunity is not implemented.
            Register("pony",new PropImpactBody(ActorTypes.Get("pony").bodyMassKg,1,20d/30));
            var exported=ExportedContentCatalog.Load();
            foreach(var creature in CreatureCatalog.Load().Creatures)
            {
                var type=ActorTypes.Get(creature.actorType);
                var xml=XElement.Parse(exported.Require("unit:"+creature.sourceId).rawXml);
                var phis=xml.Element("phis");
                float Number(string key,float originalDefault)=>phis?.Attribute(key) is XAttribute attribute?
                    float.Parse(attribute.Value,CultureInfo.InvariantCulture):originalDefault;
                // Unit.getXmlParam default knocked=1; UnitDamager/UnitTrigger set noBox=true in their constructors.
                // Those types are hazards, not entries of CreatureCatalog (the represented creature constructors don't set noBox).
                Register(creature.actorType,new PropImpactBody(Number("massafix",Number("massa",50)),Number("knocked",1),20d/30,
                    NpcCreatureBehaviours.Fixed(type.behavior)));
            }
            // Explicit Unity training content: immovable targets still receive physical damage/receipts.
            Register("training-dummy",new PropImpactBody(0,0,20d/30,true));
            Register("training-armored-dummy",new PropImpactBody(0,0,20d/30,true));
        }
    }
}
