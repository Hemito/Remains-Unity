using System.Collections.Generic;
using Remains.Domain;
using Remains.Serialization;

namespace Remains.Persistence
{
    // An actor's text as a hot template (position, health, combat...) plus its cold fields (inventory and content
    // history, ~50 KB of a long-played character), so frequent actor updates send, check and store only what changed.
    // Join(Split(actor)) is exactly StateJson.ToJson(actor): saves and snapshot IDs keep the same text.
    public static class ActorText
    {
        public static readonly IReadOnlyList<string> Cold=new[]{"weaponContent","weaponContentHistory","inventory","inventoryUses","inventoryAliases"};
        // Cold field texts by Cold index; null: the field is null and written in place in the template.
        public static string Split(ActorState actor,out string[] cold)=>StateJson.ToJsonWithHoles(actor,Cold,out cold);
        public static string Join(string template,IReadOnlyList<string> cold)=>StateJson.FillHoles(template,cold);
    }
}
