using Remains.RoomEditor;
using Remains.RoomFormat;

public static class RenderChecks
{
    public static void Run(string root)
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        var definitions=RoomDefinitionCatalog.Read(File.ReadAllText(Path.Combine(root,"definitions.json")),"defs");
        var art=TileManifest.Read(File.ReadAllText(Path.Combine(root,"Tiles/tiles-manifest.json")),"art");
        var lands=RoomLandCatalog.Read(File.ReadAllText(Path.Combine(root,"lands.json")),"lands");
        var noPicture=DecorWithoutPicture.Read(File.ReadAllText(Path.Combine(root,"decor-without-picture.json")),"no-picture");
        var visible=new HashSet<string>(RoomLayers.All.Concat(new[]{"decor","objects"}));
        RoomRenderPlan Plan(RoomFile r,int x=0,int row=0,int w=0,int h=0)=>RoomRenderPlan.Build(r,definitions,art,lands,noPicture,visible,x,row,w==0?r.Width:w,h==0?r.Height:h);
        var room=new RoomFile{Template="remains:plant:render-check",Width=8,Height=8};
        room.Palette("solid").Add('#',"remains:solid:C");
        room.Layers["solid"]=new[]{"........","........","........","...#....","........","........","........","........"};
        var plan=Plan(room);var main=plan.Commands.Where(c=>c.TextureId=="texture:tConcrete").ToArray();
        Check(main.Length==4,"concrete must draw four real alpha-mask quarters");
        var mask=art.Masks["maskBare"];
        Check(main.All(c=>mask.Quarters.Any(q=>TileManifest.MaskFrameId(q.Clip,5)==c.ImageId)),"isolated cell quarter state must select frame5");
        Check(main.Any(c=>c.ScaleX<0)&&main.Any(c=>c.ScaleY<0),"quarter flip metadata lost");
        Check(main.All(c=>!c.EraseAffected),"foreground unexpectedly affected by window erase");
        Check(plan.TexelsPerMeter==50,"bitmap world-space density changed");
        room.Layers["height"]=new[]{"........","........","........","...2....","........","........","........","........"};
        var lowered=Plan(room);var low=lowered.Commands.Where(c=>c.TextureId=="texture:tConcrete").ToArray();
        Check(low.All(c=>Math.Abs(c.ScaleY)==.5),"partial-height alpha masks were not squashed");
        Check(lowered.Commands.Where(c=>c.TextureId=="texture:tFloor").All(c=>Math.Abs(c.ScaleY)==1),"floor strip must move down without squashing");
        for(int row=0;row<8;row++)room.Layers["solid"][row]=new string(Enumerable.Range(0,8).Select(x=>(row+x)%3==0?'#':'.').ToArray());
        var full=Plan(room);var cropped=Plan(room,2,2,4,4);
        string Signature(RoomDrawCommand c)=>$"{c.CellX},{c.CellY}|{c.ImageId}|{c.TextureId}|{c.X:R},{c.Y:R}|{c.ScaleX:R},{c.ScaleY:R}|{c.Order},{c.Group}";
        var wanted=full.Commands.Where(c=>c.Kind=="material"&&c.CellX>=2&&c.CellX<6&&c.CellY>=2&&c.CellY<6).Select(Signature);
        Check(wanted.SequenceEqual(cropped.Commands.Where(c=>c.Kind=="material").Select(Signature)),"viewport halo changed shared neighbour states/order/registration");
        var window=definitions.Items.Values.First(d=>d.Kind==RoomDefinitionKind.Decor&&d.Art!=null&&art.Art.ContainsKey(TileManifest.DecorEraseFrameId(d.Art,1)));
        var decor=new RoomDecor{Id="window",Type=window.Id,X=2,Y=2};decor.Params.Add("fr","1");room.Decor.Add(decor);
        var windows=Plan(room);Check(windows.Erases.Count==1,"actual window erase clip missing");
        Check(windows.Commands.Any(c=>c.Kind=="fill"&&c.EraseAffected),"background must read erase mask");
        Check(windows.Commands.Select(c=>c.Order).SequenceEqual(windows.Commands.Select(c=>c.Order).Order()),"draw ordering changed");
        int total=0,erases=0;
        foreach(var file in Directory.GetFiles(root,"*.room.json",SearchOption.AllDirectories))
        {
            var r=RoomSerializer.Read(File.ReadAllText(file),file);var p=Plan(r);
            Check(!p.Issues.Any(i=>i.StartsWith("Нет маски:")||i.StartsWith("Нет рисунка:")),"public room art missing: "+file+" "+string.Join("; ",p.Issues));
            Check(p.Commands.All(c=>new[]{c.X,c.Y,c.Width,c.Height,c.OriginX,c.OriginY,c.ScaleX,c.ScaleY,c.Alpha}.All(double.IsFinite)),"nonfinite render command: "+file);
            total+=p.Commands.Count;erases+=p.Erases.Count;
        }
        Console.WriteLine($"PASS C# render plans: shared quarter states/flips, partial heights, room-space texture density, viewport halo, window erase, ordering;57 rooms /{total} commands /{erases} erase clips");
    }
}
