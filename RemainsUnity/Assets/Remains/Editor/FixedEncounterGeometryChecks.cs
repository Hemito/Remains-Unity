using System;
using Remains.Content;
using Remains.Domain;
using Remains.Serialization;
using UnityEngine;

namespace Remains.Editor
{
    // Count/stage/source checks belong to each authorer. This is one actual terrain/body rule.
    public static class FixedEncounterGeometryChecks
    {
        public static void Validate(RoomLayout geometry,PhysicalLayout physical,DoorState[] doors,ActorState[] actors,string context)
        {
            if(geometry==null || physical?.props==null || physical.containers==null || doors==null || actors==null)
                throw new InvalidOperationException("Проверка встречи "+context+": отсутствуют terrain/physical/doors/actors.");
            var room=StateJson.Copy(geometry);room.platforms=DoorGeometry.Compose(room.platforms,doors,physical.level);
            foreach(var actor in actors)
            {
                if(actor==null || actor.level!=physical.level) throw new InvalidOperationException("Проверка встречи "+context+": отсутствует NPC/неверный уровень.");
                var hull=Anatomy.Movement(actor);var body=new Rect(actor.x-hull.width*.5f,actor.y,hull.width,hull.height);
                if(!ActorTerrainMotion.IsClear(room,hull,actor.Position) || !ActorTerrainMotion.Supported(room,hull,actor.Position))
                    throw new InvalidOperationException($"Встреча {context}: нет свободного тела/настоящей опоры: actor={actor.id}, anatomy={actor.anatomy}, position={actor.Position}, bounds={body}.");
                foreach(var prop in physical.props) CheckProp(prop,actor,body,context);
                foreach(var prop in physical.containers) CheckProp(prop,actor,body,context);
            }
        }
        private static void CheckProp(PropState prop,ActorState actor,Rect body,string context)
        {
            if(prop==null) throw new InvalidOperationException("Встреча "+context+": отсутствует физический объект.");PropBodyRules.Validate(prop);
            if(prop.hasPhysicalBody && body.Overlaps(new Rect(prop.x-prop.width*.5f,prop.y-prop.anchorHeight,prop.width,prop.height)))
                throw new InvalidOperationException($"Встреча {context}: фактическое тело NPC пересекает авторский груз: actor={actor.id}, prop={prop.id}, body={body}, propBounds=({prop.x},{prop.y},{prop.width},{prop.height},anchor={prop.anchorHeight}). Исходный anchor не смещён.");
        }
    }
}
