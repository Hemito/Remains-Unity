using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Domain;

namespace Remains.Content
{
    // These categories describe the six existing state collections. They are not
    // serialized type IDs and do not change mod/entity/save definitions.
    public enum ContentEntityKind {Actor,Prop,Container,Door,MovingPlatform,MechanismButton}
    public readonly struct ContentEntityIdentity
    {
        public ContentEntityKind Kind {get;}
        public string Id {get;}
        public ContentEntityIdentity(ContentEntityKind kind,string id) {Kind=kind;Id=id;}
    }

    // One namespace rule for explicit, synchronous content publication. Matching
    // existing categories are allowed; each installer retains its own state and
    // placement compatibility rules. No entities, patches or inventories change.
    public static class ContentEntityIds
    {
        public static void ValidateCandidates(IEnumerable<ContentEntityIdentity> candidates,string context)
            =>Index(candidates,context+"/расстановка");

        public static void Preflight(WorldState world,IEnumerable<ContentEntityIdentity> candidates,string context)
        {
            if(world==null) throw new InvalidOperationException("ID содержимого: отсутствует мир; "+context);
            var saved=Index(ReadWorld(world,context),context+"/мир="+world.worldId);
            var incoming=Index(candidates,context+"/расстановка");
            foreach(var entry in incoming)
                if(saved.TryGetValue(entry.Key,out var kind) && kind!=entry.Value)
                    throw new InvalidOperationException($"ID содержимого занят другим типом: id={entry.Key}, сохранён={Name(kind)}, добавляется={Name(entry.Value)}; {context}, мир={world.worldId}.");
        }
        private static Dictionary<string,ContentEntityKind> Index(IEnumerable<ContentEntityIdentity> entries,string context)
        {
            if(entries==null) throw new InvalidOperationException("ID содержимого: отсутствует список; "+context);
            var result=new Dictionary<string,ContentEntityKind>(StringComparer.Ordinal);
            foreach(var entry in entries)
            {
                if((uint)entry.Kind>(uint)ContentEntityKind.MechanismButton)
                    throw new InvalidOperationException($"ID содержимого: неизвестная категория {(int)entry.Kind}; id={entry.Id}, {context}.");
                string kind=Name(entry.Kind);
                if(string.IsNullOrWhiteSpace(entry.Id))
                    throw new InvalidOperationException($"ID содержимого: пустая запись или ID; тип={kind}, {context}.");
                if(result.TryGetValue(entry.Id,out var previous))
                    throw new InvalidOperationException($"ID содержимого повторяется: id={entry.Id}, типы={Name(previous)}/{kind}; {context}.");
                result.Add(entry.Id,entry.Kind);
            }
            return result;
        }
        private static IEnumerable<ContentEntityIdentity> ReadWorld(WorldState world,string context)
            =>Read(world.actors,ContentEntityKind.Actor,a=>a.id,context)
                .Concat(Read(world.props,ContentEntityKind.Prop,p=>p.id,context))
                .Concat(Read(world.containers,ContentEntityKind.Container,c=>c.id,context))
                .Concat(Read(world.doors,ContentEntityKind.Door,d=>d.id,context))
                .Concat(Read(world.movingPlatforms,ContentEntityKind.MovingPlatform,p=>p.id,context))
                .Concat(Read(world.mechanismButtons,ContentEntityKind.MechanismButton,b=>b.id,context));
        private static IEnumerable<ContentEntityIdentity> Read<T>(IEnumerable<T> entries,ContentEntityKind kind,Func<T,string> id,string context) where T:class
        {
            if(entries==null) throw new InvalidOperationException($"ID содержимого: отсутствует список {Name(kind)}; {context}.");
            foreach(var entry in entries) yield return new ContentEntityIdentity(kind,entry==null?null:id(entry));
        }
        private static string Name(ContentEntityKind kind)=>kind switch
        {
            ContentEntityKind.Actor=>"актёр",ContentEntityKind.Prop=>"физический объект",ContentEntityKind.Container=>"контейнер",
            ContentEntityKind.Door=>"дверь",ContentEntityKind.MovingPlatform=>"платформа",ContentEntityKind.MechanismButton=>"кнопка",
            _=>throw new InvalidOperationException("ID содержимого: неизвестная категория "+(int)kind+".")
        };
    }
}
