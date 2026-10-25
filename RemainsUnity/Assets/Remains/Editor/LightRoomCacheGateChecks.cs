using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Remains.Content;
using Remains.Domain;
using Remains.Presentation;
using Remains.RoomFormat;
using UnityEditor;
using UnityEngine;

namespace Remains.Editor
{
    // Explicit GPU diagnostic. Readbacks and reflection never run in ordinary gameplay.
    public static class LightRoomCacheGateChecks
    {
        private const float Threshold=1f/255;
        private static readonly CultureInfo Invariant=CultureInfo.InvariantCulture;
        public static void RebuildReportsBatch()
        {
            try
            {
                string output=Path.GetFullPath("../.reference/light-room-cache");
                var report=JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(output,"gate.json")));
                WriteTables(report,output);Debug.Log("Отчёты gate света пересобраны из сохранённых измерений без GPU-прогона.");EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
        }
        [Serializable] private sealed class Snapshot
        {
            public RectInt rooms;
            public Vector4 worldRect,ambient;
            public int width,height,cascadeLevels,staticPasses;
            public float backgroundEnabled;
            public float minLinear,maxLinear,backgroundDifference;
            [NonSerialized] public Color[] light,background;
        }
        [Serializable] private sealed class RoomDelta
        {
            public int x,y,distance,texels,coreTexels,changed,changedCore;
            public float displayMax,coreDisplayMax,linearMax;
            public Vector2 maxWorld;
            public Rect changedCoreWorldBounds;
        }
        [Serializable] private sealed class Margin
        {
            public int k;
            public string state;
            public Snapshot context;
            public long samples;
            public double mae;
            public float max,linearMax;
            public Vector2 maxWorld;
            public bool visible,identicalRoomRectangle;
        }
        [Serializable] private sealed class Case
        {
            public string name,template,eventKind,eventId,heatmap,eventDescription;
            public int x,y,radiusAll=-1,radiusCore=-1;
            public bool ordinary,canProceed,perturbationObserved;
            public Rect eventBounds;
            public Vector2Int[] attributedRooms;
            public Snapshot baseline,changed;
            public RoomDelta[] receivers;
            public Margin[] margins;
        }
        [Serializable] private sealed class Report
        {
            public int schema=1,seed=104;
            public bool canProceed;
            public float threshold=Threshold,coreBoundaryExclusionMetres=.8f;
            public string colorSpace,metric="Neutral opaque white WorldLit receiver: display=linearToSRGB(clamp01(ambient+fluence)) in a Linear project; visible if max RGB absolute difference > 1/255. MAE averages RGB. Also records unclipped linear maxima.",
                limitations="No actors/projected shadows: RenderTransient(empty,null,null) composes background, but backgroundEnabled=0. Background is measured and compared; this does not validate per-source direct-mask fields. Reference is today's clipped 3x3 window, not an infinite/full-factory solution. Core excludes one grid cell at each room edge; radius counts Chebyshev grid-room distance. Heatmap grayscale=max display delta across both consumer fields x16, clipped to white.",
                reference="Current FactoryLevelView.RefreshLight(customRect), synchronous 8 passes; original seed104 content, doors and lamps. Shifted 3x3 places event at a window edge to expose distance2. Natural k-margin is clipped at factory boundaries rather than shifted.";
            public Case[] cases;
        }

        public static void RunBatch()
        {
            GameObject visual=null;
            try
            {
                Anatomy.Load();WeaponCatalog.Load();
                var catalog=AssetDatabase.LoadAssetAtPath<FactoryCatalogAsset>(FactoryCatalogAuthoring.AssetPath);
                var world=new WorldState {seed=104};FactoryPreparation.Create(world,catalog,0);
                FactoryContentSources.Prepare(world,"factory:0");
                var source=RoomAuthoring.Load("factory:0",world);
                DoorContentInstaller.Apply(world,RoomDoorContent.Read(source,"factory:0"));
                var layout=source.CreateLayout(false);visual=AuthoredRoomVisual.Create("factory:0",layout,world);
                var view=visual.GetComponent<FactoryLevelView>();view.Synchronize(layout,world,1);
                var factory=visual.GetComponent<FactoryLevelRoot>();
                var report=new Report {colorSpace=QualitySettings.activeColorSpace.ToString()};
                string output=Path.GetFullPath("../.reference/light-room-cache");Directory.CreateDirectory(output);
                var rows=new List<Case>();
                foreach(var spec in new[]{("open-hall",1,1,false), ("closed-office",1,9,false), ("door-lit-neighbour",2,11,true), ("factory-edge",0,0,false)})
                    rows.Add(Measure(spec.Item1,spec.Item2,spec.Item3,spec.Item4,view,factory,world,output));
                report.cases=rows.ToArray();report.canProceed=rows.All(row=>row.canProceed);
                File.WriteAllText(Path.Combine(output,"gate.json"),JsonUtility.ToJson(report,true));
                WriteTables(report,output);
                Debug.Log($"Радиус света seed104: измерение завершено, canProceed={report.canProceed}; отчёт {output}. Неудачный gate требует остановить реализацию кэша, а не менять модель света.");
                EditorApplication.Exit(0);
            }
            catch(Exception error) {Debug.LogException(error);EditorApplication.Exit(1);}
            finally
            {
                if(visual!=null) UnityEngine.Object.DestroyImmediate(visual);
                FactoryContentSources.Clear();RoomRadianceField.ClearSpareTextures();
            }
        }
        private static Case Measure(string name,int x,int y,bool doorEvent,FactoryLevelView view,FactoryLevelRoot factory,WorldState world,string output)
        {
            var grid=factory.Prepared.Grid;var placed=grid.Rooms.Single(r=>r.Cell.X==x && r.Cell.Y==y);
            var row=new Case {name=name,x=x,y=y,ordinary=name!="factory-edge",template=placed.Template.Template,eventKind=doorEvent?"door":"lamp"};
            string prefix=$"factory:0:{x},{y}:";
            var states=world.doors.Where(d=>d.level=="factory:0").ToArray();
            DoorState door=doorEvent?states.Where(d=>d.id.StartsWith(prefix,StringComparison.Ordinal)).OrderBy(d=>d.id,StringComparer.Ordinal).First():null;
            var authoredLamps=Private<Dictionary<string,(RoomSpriteAuthoring picture,int frame,Sprite sprite,RoomDefinition definition)>>(view,"lamps");
            string lamp=doorEvent?null:view.LampIds.Where(id=>id.StartsWith(prefix,StringComparison.Ordinal) &&
                view.ShadowSettings.LampFrameShare(authoredLamps[id].definition,authoredLamps[id].frame)>0)
                .OrderBy(id=>id,StringComparer.Ordinal).First();
            row.eventId=doorEvent?door.id:lamp;
            row.eventDescription=doorEvent?"Authored internal office door, not asserted to coincide with a factory grid seam.":"One authored lamp switched off without broken-art substitution.";
            if(door!=null) row.eventBounds=door.Bounds;
            else
            {
                int index=placed.Template.Decor.FindIndex(d=>prefix+d.Id==lamp),roomIndex=grid.Rooms.IndexOf(placed);
                var picture=factory.Rooms.GetChild(roomIndex).GetComponentsInChildren<RoomSpriteAuthoring>(true)
                    .Single(p=>p.Kind==RoomSpriteKind.Decor && p.Index==index);
                var bounds=picture.GetComponent<SpriteRenderer>().bounds;
                row.eventBounds=Rect.MinMaxRect(bounds.min.x,bounds.min.y,bounds.max.x,bounds.max.y);
            }
            row.attributedRooms=door==null?new[]{new Vector2Int(x,y)}:Attribution(door.Bounds,grid);
            bool initialOpen=door?.open??false;long doorRevision=view.AppliedDoorRevision,lampRevision=view.AppliedLampRevision;
            // Reference keeps the current window shape but positions the event on one edge where possible.
            var reference=new RectInt(Mathf.Clamp(x,0,grid.Layout.Width-3),Mathf.Clamp(y,0,grid.Layout.Depth-3),3,3);
            Action<bool> toggle=changed=>
            {
                if(door!=null) {door.open=changed?!initialOpen:initialOpen;view.SyncDoors(states,++doorRevision);}
                else view.SyncLamps(changed?new[]{lamp}:Array.Empty<string>(),++lampRevision);
            };
            try
            {
                toggle(false);view.RefreshLight(reference);row.baseline=Capture(view,reference);
                toggle(true);view.RefreshLight(reference);row.changed=Capture(view,reference);
                row.receivers=Delta(row.baseline,row.changed,grid,row.attributedRooms);
                row.perturbationObserved=row.receivers.Any(r=>r.linearMax>0);
                row.radiusAll=row.receivers.Where(r=>r.changed>0).Select(r=>r.distance).DefaultIfEmpty(-1).Max();
                row.radiusCore=row.receivers.Where(r=>r.changedCore>0).Select(r=>r.distance).DefaultIfEmpty(-1).Max();
                row.heatmap=name+"-delta-x16.png";Heatmap(row.baseline,row.changed,Path.Combine(output,row.heatmap));
                var margins=new List<Margin>();
                MeasureMargin(1);
                if(row.radiusCore>1 || margins.Any(m=>m.visible)) MeasureMargin(2);
                row.margins=margins.ToArray();row.canProceed=row.perturbationObserved && (!row.ordinary || row.radiusCore<=1) && !margins.Any(m=>m.k==1 && m.visible);
                Debug.Log($"Радиус света {name} room={x},{y}, event={row.eventId}: radiusAll={row.radiusAll}, radiusCore={row.radiusCore}, k1max={margins.Where(m=>m.k==1).Max(m=>m.max):G9}, proceed={row.canProceed}.");
                return row;
                void MeasureMargin(int k)
                {
                    var context=NaturalContext(x,y,k,grid.Layout.Width,grid.Layout.Depth);
                    toggle(false);view.RefreshLight(context);var before=Capture(view,context);
                    margins.Add(CompareOwn(k,"baseline",row.baseline,before,grid,placed));
                    toggle(true);view.RefreshLight(context);var after=Capture(view,context);
                    margins.Add(CompareOwn(k,"changed",row.changed,after,grid,placed));
                }
            }
            finally {toggle(false);}
        }
        private static RectInt NaturalContext(int x,int y,int k,int width,int height)
        {int left=Math.Max(0,x-k),top=Math.Max(0,y-k);return new RectInt(left,top,Math.Min(width,x+k+1)-left,Math.Min(height,y+k+1)-top);}
        private static Vector2Int[] Attribution(Rect door,FactoryGrid grid)
        {
            var result=new List<Vector2Int>();
            foreach(var room in grid.Rooms)
            {
                var bounds=Core(room,grid,0);var overlap=Intersection(bounds,door);
                if(overlap.width>1e-5f && overlap.height>1e-5f) result.Add(new Vector2Int(room.Cell.X,room.Cell.Y));
            }
            if(result.Count==0) throw new InvalidOperationException("Дверь вне комнат: "+door);
            return result.ToArray();
        }
        private static Snapshot Capture(FactoryLevelView view,RectInt rooms)
        {
            var field=Private<RoomRadianceField>(view,"field");
            field.RenderTransient(Array.Empty<RoomRadianceField.SpriteEmission>(),null,null);
            var light=Private<RenderTexture>(field,"fluence");var background=Private<RenderTexture>(field,"backgroundFluence");
            var result=new Snapshot {rooms=rooms,worldRect=Private<Vector4>(field,"rect"),ambient=Shader.GetGlobalVector("_RemainsAmbient"),
                width=light.width,height=light.height,cascadeLevels=Private<int>(field,"levels"),staticPasses=field.StaticRenderUpdates,
                backgroundEnabled=Shader.GetGlobalFloat("_RemainsBackgroundRadianceEnabled"),light=Read(light),background=Read(background),minLinear=float.PositiveInfinity};
            for(int i=0;i<result.light.Length;i++) for(int c=0;c<3;c++)
            {float value=result.light[i][c];result.minLinear=Mathf.Min(result.minLinear,value);result.maxLinear=Mathf.Max(result.maxLinear,value);result.backgroundDifference=Mathf.Max(result.backgroundDifference,Mathf.Abs(value-result.background[i][c]));}
            return result;
        }
        private static Color[] Read(RenderTexture target)
        {
            var previous=RenderTexture.active;var image=new Texture2D(target.width,target.height,TextureFormat.RGBAFloat,false,true);
            try {RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply(false);return image.GetPixels();}
            finally {RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
        }
        private static T Private<T>(object value,string name)=>(T)value.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(value);
        private static Vector2 World(Snapshot s,int x,int y)=>new(s.worldRect.x+(x+.5f)*s.worldRect.z/s.width,s.worldRect.y+(y+.5f)*s.worldRect.w/s.height);
        private static float Display(float light,float ambient)
        {float value=Mathf.Clamp01(light+ambient);return QualitySettings.activeColorSpace==ColorSpace.Linear?Mathf.LinearToGammaSpace(value):value;}
        private static float Difference(Snapshot a,int ai,Snapshot b,int bi,out float raw)
        {
            float maximum=0;raw=0;
            for(int field=0;field<2;field++)for(int c=0;c<3;c++)
            {
                float av=(field==0?a.light:a.background)[ai][c],bv=(field==0?b.light:b.background)[bi][c];
                raw=Mathf.Max(raw,Mathf.Abs(av-bv));maximum=Mathf.Max(maximum,Mathf.Abs(Display(av,a.ambient[c])-Display(bv,b.ambient[c])));
            }
            return maximum;
        }
        private static RoomDelta[] Delta(Snapshot before,Snapshot after,FactoryGrid grid,Vector2Int[] sources)
        {
            if(before.width!=after.width || before.height!=after.height || before.worldRect!=after.worldRect) throw new InvalidOperationException("Сравниваются разные окна.");
            var rows=new Dictionary<FactoryRoomPlacement,RoomDelta>();var low=new Dictionary<FactoryRoomPlacement,Vector2>();var high=new Dictionary<FactoryRoomPlacement,Vector2>();
            float cell=(float)grid.Level.Cell;
            for(int y=0;y<before.height;y++)for(int x=0;x<before.width;x++)
            {
                var world=World(before,x,y);int column=Mathf.Clamp(Mathf.FloorToInt(world.x/cell),0,grid.Level.Width-1),topRow=Mathf.Clamp(grid.Level.Height-1-Mathf.FloorToInt(world.y/cell),0,grid.Level.Height-1);
                var owner=grid.Owner(column,topRow);
                if(!rows.TryGetValue(owner,out var row))
                {row=new RoomDelta {x=owner.Cell.X,y=owner.Cell.Y,distance=sources.Min(s=>Math.Max(Math.Abs(s.x-owner.Cell.X),Math.Abs(s.y-owner.Cell.Y)))};rows.Add(owner,row);}
                row.texels++;int index=y*before.width+x;float delta=Difference(before,index,after,index,out float raw);bool core=Core(owner,grid,cell).Contains(world);
                row.linearMax=Mathf.Max(row.linearMax,raw);
                if(delta>row.displayMax) {row.displayMax=delta;row.maxWorld=world;}
                if(delta>Threshold) row.changed++;
                if(!core) continue;row.coreTexels++;row.coreDisplayMax=Mathf.Max(row.coreDisplayMax,delta);
                if(delta<=Threshold) continue;row.changedCore++;
                if(!low.ContainsKey(owner)) {low.Add(owner,world);high.Add(owner,world);} else {low[owner]=Vector2.Min(low[owner],world);high[owner]=Vector2.Max(high[owner],world);}
            }
            foreach(var pair in low) rows[pair.Key].changedCoreWorldBounds=Rect.MinMaxRect(pair.Value.x,pair.Value.y,high[pair.Key].x,high[pair.Key].y);
            return rows.Values.OrderBy(r=>r.y).ThenBy(r=>r.x).ToArray();
        }
        private static Rect Core(FactoryRoomPlacement room,FactoryGrid grid,float exclusion)
        {
            float cell=(float)grid.Level.Cell;
            return new Rect(room.Column*cell+exclusion,(grid.Level.Height-room.Row-room.Template.Height)*cell+exclusion,
                room.Template.Width*cell-2*exclusion,room.Template.Height*cell-2*exclusion);
        }
        private static Rect Intersection(Rect a,Rect b)=>Rect.MinMaxRect(Mathf.Max(a.xMin,b.xMin),Mathf.Max(a.yMin,b.yMin),Mathf.Min(a.xMax,b.xMax),Mathf.Min(a.yMax,b.yMax));
        private static Margin CompareOwn(int k,string state,Snapshot reference,Snapshot context,FactoryGrid grid,FactoryRoomPlacement room)
        {
            var result=new Margin {k=k,state=state,context=context,identicalRoomRectangle=reference.rooms==context.rooms};
            var core=Core(room,grid,(float)grid.Level.Cell);double sum=0;
            for(int y=0;y<reference.height;y++)for(int x=0;x<reference.width;x++)
            {
                var world=World(reference,x,y);if(!core.Contains(world)) continue;
                float fx=(world.x-context.worldRect.x)*context.width/context.worldRect.z-.5f,fy=(world.y-context.worldRect.y)*context.height/context.worldRect.w-.5f;
                int sx=Mathf.RoundToInt(fx),sy=Mathf.RoundToInt(fy);
                if(sx<0 || sy<0 || sx>=context.width || sy>=context.height || Mathf.Abs(fx-sx)>.02f || Mathf.Abs(fy-sy)>.02f)
                    throw new InvalidOperationException($"Контекст k={k} не совмещён с texel центром {world}: sample={fx},{fy}.");
                int ai=y*reference.width+x,bi=sy*context.width+sx;
                float max=Difference(reference,ai,context,bi,out float raw);result.linearMax=Mathf.Max(result.linearMax,raw);
                if(max>result.max) {result.max=max;result.maxWorld=world;}
                for(int field=0;field<2;field++)for(int c=0;c<3;c++)
                {float a=(field==0?reference.light:reference.background)[ai][c],b=(field==0?context.light:context.background)[bi][c];sum+=Math.Abs(Display(a,reference.ambient[c])-Display(b,context.ambient[c]));result.samples++;}
            }
            if(result.samples==0) throw new InvalidOperationException("Нет собственных texel для сравнения контекста.");
            result.mae=sum/result.samples;result.visible=result.max>Threshold;return result;
        }
        private static void Heatmap(Snapshot a,Snapshot b,string path)
        {
            var pixels=new Color32[a.light.Length];
            for(int i=0;i<pixels.Length;i++) {float d=Difference(a,i,b,i,out _);byte v=(byte)Mathf.RoundToInt(Mathf.Clamp01(d*16)*255);pixels[i]=new Color32(v,v,v,255);}
            var image=new Texture2D(a.width,a.height,TextureFormat.RGBA32,false,true);
            try {image.SetPixels32(pixels);image.Apply(false);File.WriteAllBytes(path,image.EncodeToPNG());}
            finally {UnityEngine.Object.DestroyImmediate(image);}
        }
        private static void WriteTables(Report report,string output)
        {
            var csv=new StringBuilder("case,event,roomX,roomY,distance,texels,coreTexels,changed,changedCore,maxDisplay,coreMaxDisplay,maxLinear,maxWorldX,maxWorldY\n");
            var md=new StringBuilder("# Gate: зависимость света по комнатам\n\nПорог: max RGB display delta >1/255, белый непрозрачный WorldLit, clamp затем sRGB в Linear-проекте. Ядро исключает одну клетку по краю комнаты.\n\n| Случай | Комната | Радиус все/ядро | k1 MAE max | k1 max | Gate |\n|---|---|---:|---:|---:|---|\n");
            foreach(var row in report.cases)
            {
                var k1=row.margins.Where(m=>m.k==1).ToArray();
                md.AppendLine($"| {row.name} | {row.x},{row.y} ({row.template}) | {row.radiusAll}/{row.radiusCore} | {k1.Max(m=>m.mae).ToString("G9",Invariant)} | {k1.Max(m=>m.max).ToString("G9",Invariant)} | {row.canProceed} |");
                foreach(var room in row.receivers)
                    csv.AppendLine(string.Join(",",new[]{row.name,row.eventId,room.x.ToString(),room.y.ToString(),room.distance.ToString(),room.texels.ToString(),room.coreTexels.ToString(),room.changed.ToString(),room.changedCore.ToString(),room.displayMax.ToString("G9",Invariant),room.coreDisplayMax.ToString("G9",Invariant),room.linearMax.ToString("G9",Invariant),room.maxWorld.x.ToString("G9",Invariant),room.maxWorld.y.ToString("G9",Invariant)}.Select(value=>"\""+value.Replace("\"","\"\"")+"\"")));
            }
            foreach(var row in report.cases)
                md.AppendLine($"\n{row.name}: событие `{row.eventId}`, поле {row.baseline.width}×{row.baseline.height}, каскадов {row.baseline.cascadeLevels}, backgroundEnabled={row.baseline.backgroundEnabled}; heatmap `{row.heatmap}` (дельта ×16).\n");
            md.AppendLine($"\n**canProceed={report.canProceed}**. Если false, реализация кэша не разрешена этим gate; требуется другой радиус/механизм.\n\n"+report.limitations);
            File.WriteAllText(Path.Combine(output,"gate.md"),md.ToString());File.WriteAllText(Path.Combine(output,"room-deltas.csv"),csv.ToString());
        }
    }
}
