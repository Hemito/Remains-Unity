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

        public sealed class Pieces
        {
            public readonly string Template;public readonly string[] Cold;
            public Pieces(string template,string[] cold) {Template=template;Cold=cold;}
            public static Pieces Of(ActorState actor) {string template=Split(actor,out var cold);return new Pieces(template,cold);}
            // From an actor's exact text (a replica that has only the text): split again and required to join back.
            public static Pieces OfText(string text)
            {
                var pieces=Of(StateJson.FromTrustedJson<ActorState>(text));
                if(Join(pieces.Template,pieces.Cold)!=text) throw new System.IO.InvalidDataException("Текст персонажа не раскладывается на части без потерь.");
                return pieces;
            }
        }

        private static readonly System.Reflection.FieldInfo[] ColdFields=System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(Cold,
            name=>typeof(ActorState).GetField(name)));
        private static readonly System.Type[] ColdTypes=System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(ColdFields,field=>field.FieldType));
        // Receiver-side canonical graph: the strict hot parse is also the result, rather than a discarded
        // validation followed by another full actor parse. Unchanged cold graphs remain private to the replica.
        public static ActorState Read(string template,IReadOnlyList<string> cold,System.Func<int,bool> changed,ActorState previous)
        {
            var nulls=new string[Cold.Count];for(int i=0;i<nulls.Length;i++) nulls[i]="null";
            var actor=StateJson.FromJson<ActorState>(Join(template,nulls));
            for(int i=0;i<Cold.Count;i++)
            {
                // A literal null in the hot template overrides a retained cold text (wire null also means
                // "unchanged"). A later return to a hole may need to parse that retained text again.
                if(template.IndexOf("\u0002"+i+"\u0002",System.StringComparison.Ordinal)<0) continue;
                var field=ColdFields[i];
                object prior=previous==null?null:field.GetValue(previous);
                object value=cold[i]==null?null:prior!=null && !changed(i)?prior:StateJson.FromJson(cold[i],ColdTypes[i]);
                field.SetValue(actor,value);
            }
            return actor;
        }
        // The strict check of an actor text by pieces: the template (cold fields as null) when it changed, and each cold
        // text that changed, as its field type: together the checks the whole text's strict parse applies.
        public static void Verify(string template,bool checkTemplate,IReadOnlyList<string> cold,System.Func<int,bool> changed)
        {
            if(checkTemplate)
            {
                var nulls=new string[Cold.Count];for(int i=0;i<nulls.Length;i++) nulls[i]="null";
                StateJson.FromJson<ActorState>(Join(template,nulls));
            }
            for(int i=0;i<Cold.Count;i++) if(cold[i]!=null && changed(i)) StateJson.FromJson(cold[i],ColdTypes[i]);
        }
    }
}
