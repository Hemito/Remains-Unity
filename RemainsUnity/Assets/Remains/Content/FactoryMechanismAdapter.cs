using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Remains.Domain;
using Remains.RoomFormat;
using UnityEngine;

namespace Remains.Content
{
    // The mechanisms of the plant templates on the megafactory (user 2026-11-06): moving platforms and their buttons
    // (allact move3), buttons that swap a door group (swap) and areas that open one (open). Groups live inside a room, as
    // in Location.allAct. Saved worlds gain them through their own patch, a later visit changes nothing.
    public sealed class FactoryMechanismAdapter : MonoBehaviour
    {
        public const string Patch="base:factory-generated:mechanisms-v1";
        [SerializeField,InspectorName("Платформа: долгий ход (6 с)")] private MovingPlatformAsset platformLong;
        [SerializeField,InspectorName("Платформа: короткий ход (4 с)")] private MovingPlatformAsset platformShort;
        [SerializeField,InspectorName("Кнопка")] private MechanismButtonAsset button;
        [SerializeField,InspectorName("Кнопка, нуждается в ремонте")] private MechanismButtonAsset repairButton;
        private bool installed;
        public void Configure(MovingPlatformAsset moving,MovingPlatformAsset quick,MechanismButtonAsset plain,MechanismButtonAsset repaired)
        {platformLong=moving;platformShort=quick;button=plain;repairButton=repaired;}
        // A door of this group has something that drives it here, so placing it closed cannot wall the room off for good.
        public static bool Driven(FactoryPlacedObject door,IEnumerable<FactoryPlacedObject> objects)
        {
            if(!door.Source.Params.TryGetValue("allid",out string group)) return false;
            return objects.Any(other=>other.Room==door.Room && Group(other)==group &&
                other.Source.Params.TryGetValue("allact",out string action) &&
                (Button(other) && action=="swap" || other.Source.Type=="remains:obj:area" && action=="open"));
        }
        private static string Group(FactoryPlacedObject placement)=>placement.Source.Params.TryGetValue("allid",out string group)?group:null;
        private static bool Button(FactoryPlacedObject placement)=>placement.Source.Type is "remains:obj:knop1" or "remains:obj:knop3";
        private static string Local(FactoryPlacedObject placement)=>$"{placement.Room.Cell.X},{placement.Room.Cell.Y}:{placement.Source.Id}";
        private static string RoomId(FactoryPlacedObject placement)=>$"factory-cell:{placement.Room.Cell.X},{placement.Room.Cell.Y}";
        public void Install(FactoryLevelRoot root)
        {
            if(installed || root==null || root.Prepared==null || root.transform!=transform)
                throw new InvalidOperationException("Адаптер механизмов: нужен подготовленный корень; повторная установка запрещена.");
            if(platformLong==null || platformShort==null || button==null || repairButton==null)
                throw new InvalidOperationException("Адаптер механизмов: не заданы сериализованные определения.");
            var cell=(float)root.Prepared.Grid.Level.Cell;
            var created=new List<GameObject>();
            var platforms=new Dictionary<(FactoryRoomPlacement,string),string>();
            var issues=new List<string>();
            try
            {
                foreach(var placement in root.Objects.Where(p=>p.Source.Type=="remains:obj:platform2"))
                {
                    string group=Group(placement);
                    if(group==null || !Route(placement,cell,out var delta,out var type)) {issues.Add($"{placement.Id}: нет группы или маршрута");continue;}
                    var node=Node("Платформа · "+placement.Id,placement.Position+new Vector2(0,Lift),created);
                    node.AddComponent<RoomMovingPlatformPlacement>().Configure(Local(placement),RoomId(placement),group,type,delta);
                    if(platforms.ContainsKey((placement.Room,group))) throw new InvalidOperationException("Адаптер механизмов: вторая платформа группы: "+placement.Id);
                    platforms.Add((placement.Room,group),Local(placement));
                    root.HideObjectArtwork(placement);
                }
                foreach(var placement in root.Objects.Where(p=>Button(p) || p.Source.Type=="remains:obj:area"))
                {
                    if(!placement.Source.Params.TryGetValue("allact",out string action)) continue;
                    string group=Group(placement);bool isButton=Button(placement);
                    if(group==null) {issues.Add($"{placement.Id}: команда {action} без группы");continue;}
                    if(action=="move3" && isButton)
                    {
                        if(!platforms.TryGetValue((placement.Room,group),out string platform)) {issues.Add($"{placement.Id}: нет платформы группы {group}");continue;}
                        var node=Node("Кнопка платформы · "+placement.Id,placement.Position+new Vector2(0,Lift),created);
                        node.AddComponent<RoomMechanismButtonPlacement>().Configure(Local(placement),RoomId(placement),group,platform,Type(placement),Repair(placement));
                        root.HideObjectArtwork(placement);
                    }
                    else if(action=="swap" && isButton || action=="open" && !isButton)
                    {
                        var doors=root.Objects.Where(p=>p.Room==placement.Room && p!=placement && p.Source.Type=="remains:obj:platform1" && Group(p)==group).Select(Local).ToArray();
                        if(doors.Length==0) {issues.Add($"{placement.Id}: нет дверей группы {group}");continue;}
                        Vector2 position=placement.Position,area=Vector2.zero;
                        if(!isButton) Area(root,placement,cell,out position,out area);
                        var node=Node((isButton?"Кнопка дверей · ":"Область дверей · ")+placement.Id,position+new Vector2(0,isButton?Lift:0),created);
                        var entry=node.AddComponent<RoomMechanismButtonPlacement>();
                        entry.Configure(Local(placement),RoomId(placement),group,null,isButton?Type(placement):button,isButton?Repair(placement):MechanismRepairStates.Ready);
                        entry.ConfigureDoors(doors,area);
                        if(isButton) root.HideObjectArtwork(placement);
                    }
                    else issues.Add($"{placement.Id}: команда {action} не поддержана");
                }
                if(created.Count>0)
                {
                    if(GetComponent<RoomMechanismContent>()!=null) throw new InvalidOperationException("Корень завода уже содержит механизмы.");
                    gameObject.AddComponent<RoomMechanismContent>().Configure(Patch);
                }
                installed=true;
            }
            catch
            {
                foreach(var item in created) {if(Application.isPlaying) Destroy(item);else DestroyImmediate(item);}
                throw;
            }
            if(issues.Count>0) Debug.LogWarning($"Завод {root.Prepared.Physics.id}: механизмы пропущены, {issues.Count}: {string.Join("; ",issues)}",this);
        }
        // Workshop 18 stands its records this much above the source cell.
        private const float Lift=.02f;
        private GameObject Node(string title,Vector2 position,List<GameObject> created)
        {
            var node=new GameObject(title);node.transform.SetParent(transform,false);node.transform.localPosition=position;
            created.Add(node);return node;
        }
        private MechanismButtonAsset Type(FactoryPlacedObject placement)=>placement.Source.Type=="remains:obj:knop3"?repairButton:button;
        private static string Repair(FactoryPlacedObject placement)=>
            placement.Source.Type=="remains:obj:knop3"?MechanismRepairStates.NeedsRepair:MechanismRepairStates.Ready;
        // <move dx="21" tstay="30" tmove="180"/>: Interact.as, dx turns with a mirrored room, dy grows downwards.
        private bool Route(FactoryPlacedObject placement,float cell,out Vector2 delta,out MovingPlatformAsset type)
        {
            delta=default;type=null;
            string text=placement.Source.Extra?.Items?.FirstOrDefault(item=>item.Text?.Contains("<move")==true)?.Text;
            if(text==null) return false;
            int Read(string key)
            {
                var match=Regex.Match(text,key+@"=""(-?\d+)");
                return match.Success?int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture):0;
            }
            int dx=Read("dx"),dy=Read("dy"),move=Read("tmove");
            if(dx==0 && dy==0 || move<=0) return false;
            delta=new Vector2((placement.Room.Cell.Mirror?-dx:dx)*cell,-dy*cell);
            double seconds=move/30.0;
            foreach(var asset in new[]{platformLong,platformShort})
                if(Math.Abs(asset.CreateDefinition().durationSeconds-seconds)<.001) type=asset;
            return type!=null;
        }
        // Area.as: from the cell upward and rightward, w and h in cells (two by default).
        private static void Area(FactoryLevelRoot root,FactoryPlacedObject placement,float cell,out Vector2 position,out Vector2 size)
        {
            int Size(string key)=>placement.Source.Params.TryGetValue(key,out string value)?int.Parse(value,CultureInfo.InvariantCulture):2;
            int width=Size("w"),height=Size("h");
            var room=placement.Room;
            int x=room.Cell.Mirror?room.Template.Width-placement.Source.X-width:placement.Source.X;
            position=root.Offset(room)+new Vector2((x+width*.5f)*cell,placement.Source.Y*cell);
            size=new Vector2(width*cell,height*cell);
        }
    }
}
