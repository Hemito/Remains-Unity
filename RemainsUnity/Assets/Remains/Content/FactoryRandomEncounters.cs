using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Remains.Domain;
using Remains.RoomFormat;

namespace Remains.Content
{
    // Random enemies of every room of a generated factory (RandomEncounterRules): the "up" points of the templates
    // (enl1/enl2/enf1/enc1/lov) get the original counts and kinds per room. The whole plan is decided first and stays
    // the same whatever this build can place, so a creature connected later appears exactly where the plan put it.
    // Deterministic: the layout seed, the factory index and the room cell choose; the session RNG is not used.
    public static class FactoryRandomEncounters
    {
        public const string Version="factory-random-units-v1";
        private const string ObjectPrefix="remains:obj:";
        private const int RoomWidth=48,RoomHeight=25;   // original spaceX × spaceY, the passage strips assume them

        public sealed class Encounter
        {
            public FactoryPlacedObject Point;
            public string Unit;            // original unit ID (randomUnit / createHidden)
            public int Kind;               // RandomEncounterRules kind of the point
            public bool Hidden;            // createHidden on an ordinary point (a buried zombie digs out)
            public int EnemyType;          // the room's tipEnemy
            public string LocalId=>$"{Point.Room.Cell.X},{Point.Room.Cell.Y}:{Point.Source.Id}";
        }
        public sealed class RoomPlan
        {
            public FactoryRoomPlacement Room;
            public int EnemyType,Hidden;
            public double Danger;
            public int[] Counts,Points;
        }

        public static List<Encounter> Plan(FactoryLevelRoot root,List<RoomPlan> rooms=null)
        {
            if(root==null || root.Prepared==null) throw new InvalidOperationException("Случайные враги завода: нужен подготовленный корень.");
            var grid=root.Prepared.Grid;var layout=grid.Layout;var definitions=FactoryCatalogAsset.Load().Definitions;
            var points=new Dictionary<FactoryRoomPlacement,List<FactoryPlacedObject>[]>();
            foreach(var placement in root.Objects)
            {
                string type=placement.Source.Type;
                if(!type.StartsWith(ObjectPrefix,StringComparison.Ordinal)) continue;
                int kind=RandomEncounterRules.Kind(type.Substring(ObjectPrefix.Length));
                if(kind==0) continue;
                if(placement.Source.Params.Count>0 || placement.Source.Items.Count>0 || placement.Source.Links.Count>0 || placement.Source.Extra!=null)
                    throw new InvalidOperationException("Случайные враги завода: точка с параметрами пока не поддержана: "+placement.Id);
                if(!points.TryGetValue(placement.Room,out var byKind)) points.Add(placement.Room,byKind=Enumerable.Range(0,RandomEncounterRules.Kinds).Select(_=>new List<FactoryPlacedObject>()).ToArray());
                byKind[kind].Add(placement);
            }
            var result=new List<Encounter>();
            foreach(var room in grid.Rooms)
            {
                if(room.Template.Width!=RoomWidth || room.Template.Height!=RoomHeight)
                    throw new InvalidOperationException($"Случайные враги завода: комната {room.Template.Template} {room.Template.Width}×{room.Template.Height}, ожидалась {RoomWidth}×{RoomHeight}.");
                var random=Random(layout,room.Cell);
                double danger=room.Cell.Y/2.0;   // the same half-floor locDifLevel as FactoryEnemyScaling
                int enemyType=RandomEncounterRules.EnemyType(0,random.Unit);
                var (counts,hidden)=RandomEncounterRules.Counts(danger,enemyType,random.Unit);
                var plan=new RoomPlan {Room=room,EnemyType=enemyType,Hidden=hidden,Danger=danger,Counts=counts,Points=new int[RandomEncounterRules.Kinds]};
                rooms?.Add(plan);
                if(!points.TryGetValue(room,out var byKind)) continue;
                var blocked=Blocked(grid,room);
                // Points are taken in a fixed order (template object order), so the plan does not depend on enumeration.
                var free=byKind.Select(list=>list.Where(p=>!blocked.Contains(FlashCell(room,p,definitions))).OrderBy(p=>p.Index).ToList()).ToArray();
                for(int kind=1;kind<RandomEncounterRules.Kinds;kind++)
                {
                    plan.Points[kind]=free[kind].Count;
                    for(int n=0;n<counts[kind] && free[kind].Count>0;n++)
                    {
                        int pick=random.Range(free[kind].Count);var point=free[kind][pick];free[kind].RemoveAt(pick);
                        string unit=RandomEncounterRules.Unit(kind,enemyType,danger,(room.Cell.X+room.Cell.Y)%2,random.Unit);
                        if(unit.Length>0) result.Add(new Encounter {Point=point,Unit=unit,Kind=kind,EnemyType=enemyType});
                    }
                }
                for(int n=0;n<hidden && free[RandomEncounterRules.Ordinary].Count>0;n++)
                {
                    var list=free[RandomEncounterRules.Ordinary];
                    int pick=random.Range(list.Count);var point=list[pick];list.RemoveAt(pick);
                    string unit=RandomEncounterRules.Hidden(enemyType);
                    if(unit.Length>0) result.Add(new Encounter {Point=point,Unit=unit,Kind=RandomEncounterRules.Ordinary,Hidden=true,EnemyType=enemyType});
                }
            }
            return result;
        }

        private static LayoutRandom Random(FactoryLayout layout,FactoryCell cell)
        {
            ulong hash=14695981039346656037UL;
            string key=Version+"|"+layout.Seed.ToString(CultureInfo.InvariantCulture)+"|"+layout.Index.ToString(CultureInfo.InvariantCulture)+"|"+
                cell.X.ToString(CultureInfo.InvariantCulture)+","+cell.Y.ToString(CultureInfo.InvariantCulture);
            foreach(byte value in Encoding.UTF8.GetBytes(key)) hash=unchecked((hash^value)*1099511628211UL);
            return new LayoutRandom(hash);
        }

        // A point's cell in the original room coordinates of this placement (x in world orientation, row from the top).
        private static (int x,int row) FlashCell(FactoryRoomPlacement room,FactoryPlacedObject point,RoomDefinitionCatalog definitions)
        {
            var definition=definitions.Require(point.Source.Type,"случайные враги завода");
            return (RoomSpriteLayout.ObjectCellX(room.Template,point.Source,definition,room.Cell.Mirror),room.Template.Height-1-point.Source.Y);
        }

        // Location.setDoor/setNoObj: no random enemy on the strips in front of the room's passages (noHolesPlace of
        // random lands). Seams are in world orientation, as the points' x.
        private static HashSet<(int,int)> Blocked(FactoryGrid grid,FactoryRoomPlacement room)
        {
            var blocked=new HashSet<(int,int)>();
            int cx=room.Cell.X,cy=room.Cell.Y;
            foreach(var passage in grid.Passages)
            {
                var seam=passage.Seam;bool wide=seam.Width>2;
                if(!seam.Down)
                {
                    int q=seam.Slot*4+3;
                    if(seam.X==cx && seam.Y==cy) {Strip(RoomWidth-6,RoomWidth-1,q);Strip(RoomWidth-6,RoomWidth-1,q-1);}
                    else if(seam.X+1==cx && seam.Y==cy) {Strip(0,5,q);Strip(0,5,q-1);}
                }
                else
                {
                    int q=(seam.Slot-6)*9+4;
                    var columns=wide?new[]{q,q+1,q+2,q+3}:new[]{q+1,q+2};
                    if(seam.X==cx && seam.Y==cy) foreach(int x in columns) Column(x,RoomHeight-3,RoomHeight-1);
                    else if(seam.X==cx && seam.Y+1==cy) foreach(int x in columns) Column(x,0,2);
                }
            }
            return blocked;
            void Strip(int x0,int x1,int row) {for(int x=x0;x<=x1;x++) blocked.Add((x,row));}
            void Column(int x,int row0,int row1) {for(int row=row0;row<=row1;row++) blocked.Add((x,row));}
        }
    }
}
