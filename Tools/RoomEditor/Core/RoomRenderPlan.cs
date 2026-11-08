using System.Globalization;
using Remains.RoomFormat;

namespace Remains.RoomEditor;

// Presentation commands only. Room parsing, validation, terrain neighbour states and
// decor placement still come from the unchanged game RoomFormat sources.
public sealed class RoomDrawCommand
{
    public string Kind {get;set;}="sprite";
    public string Image {get;set;}
    public string ImageId {get;set;}
    public string Texture {get;set;}
    public string TextureId {get;set;}
    public string Blend {get;set;}="normal";
    public string Layer {get;set;}
    public string FilterFlag {get;set;}
    public int CellX {get;set;}=-1;
    public int CellY {get;set;}=-1;
    public int Order {get;set;}
    public long Group {get;set;}
    // Metres, top-left room origin, y down. Image origin is the manifest pivot.
    public double X {get;set;}
    public double Y {get;set;}
    public double Width {get;set;}
    public double Height {get;set;}
    public double OriginX {get;set;}
    public double OriginY {get;set;}
    public double ScaleX {get;set;}=1;
    public double ScaleY {get;set;}=1;
    public double Alpha {get;set;}=1;
    public bool EraseAffected {get;set;}
}

public sealed class RoomRenderPlan
{
    public List<RoomDrawCommand> Commands {get;set;}=new();
    public List<RoomDrawCommand> Erases {get;set;}=new();
    public List<string> Issues {get;set;}=new();
    public List<string> MaterialFilterFlags {get;set;}=new();
    public string FarBackground {get;set;}
    public double TexelsPerMeter {get;set;}

    public static RoomRenderPlan Build(RoomFile room,RoomDefinitionCatalog definitions,TileManifest art,
        RoomLandCatalog lands,IReadOnlyDictionary<string,string> withoutPicture,ISet<string> visible,
        int column,int row,int width,int height)
        =>new Builder(room,definitions,art,lands,withoutPicture,visible).Build(column,row,width,height);

    sealed class Builder
    {
        readonly RoomFile room;
        readonly RoomDefinitionCatalog definitions;
        readonly TileManifest art;
        readonly RoomLandCatalog lands;
        readonly IReadOnlyDictionary<string,string> withoutPicture;
        readonly ISet<string> visible;
        readonly RoomRenderPlan plan=new();
        readonly Dictionary<(string,string),long> first=new();
        readonly HashSet<string> flags=new(StringComparer.Ordinal);
        double Top=>room.Height*room.Cell;

        public Builder(RoomFile room,RoomDefinitionCatalog definitions,TileManifest art,RoomLandCatalog lands,
            IReadOnlyDictionary<string,string> withoutPicture,ISet<string> visible)
        {this.room=room;this.definitions=definitions;this.art=art;this.lands=lands;this.withoutPicture=withoutPicture;this.visible=visible;plan.TexelsPerMeter=art.CellPixels/room.Cell;}

        public RoomRenderPlan Build(int column,int row,int width,int height)
        {
            if(column<0||row<0||width<1||height<1||column+width>room.Width||row+height>room.Height)throw new ArgumentException("Неверная область отображения.");
            if(!double.IsFinite(Top)||!double.IsFinite(room.Width*room.Cell)||!double.IsFinite(plan.TexelsPerMeter))
            {plan.TexelsPerMeter=1;plan.Issues.Add("Размер клетки выходит за диапазон предпросмотра.");return plan;}
            // One-cell halo is sufficient for RoomTerrainShapes' exact neighbour rule.
            // No full-room 3D mask array is allocated for a multi-million-cell document.
            int left=Math.Max(0,column-1),top=Math.Max(0,row-1),right=Math.Min(room.Width,column+width+1),bottom=Math.Min(room.Height,row+height+1);
            var region=RoomFileRegion.Crop(room,left,top,right-left,bottom-top);
            var shapes=RoomTerrainShapes.Compute(region);
            if(visible.Contains(RoomLayers.Back))Backdrop();
            for(int x=column;x<column+width;x++)for(int r=row;r<row+height;r++)
            {
                int y=room.Height-1-r,localY=region.Height-1-(r-top);
                int h=room.Layers.TryGetValue(RoomLayers.Height,out var heights)&&heights[r][x]!='.'?heights[r][x]-'0':0;
                foreach(string layer in new[]{RoomLayers.Back,RoomLayers.Solid,RoomLayers.Platform})
                {
                    if(!visible.Contains(layer))continue;
                    var d=Definition(layer,x,r);if(d==null)continue;
                    if(d.Filter!=null)flags.Add(d.Filter);
                    int State(Quarter q)=>layer==RoomLayers.Back?shapes.Back(x-left,localY,q):shapes.Front(x-left,localY,q);
                    long group=First(layer,d.Id)*16+(layer==RoomLayers.Back?0:layer==RoomLayers.Solid?4:8);
                    if(layer==RoomLayers.Back)Layer(d.MainTexture,d.MainMask??"TileMask",layer,d,-290,group,x,y,State,0,false);
                    else
                    {
                        int order=d.Rear?-60:-40;
                        Layer(d.MainTexture,d.MainMask??"TileMask",layer,d,order,group,x,y,State,h,false);
                        if(d.BorderTexture!=null&&d.BorderMask!=null)Layer(d.BorderTexture,d.BorderMask,layer,d,d.Rear?-60:-30,group+1,x,y,State,h,false);
                        if(d.FloorTexture!=null&&d.FloorMask!=null)Layer(d.FloorTexture,d.FloorMask,layer,d,d.Rear?-60:-20,group+2,x,y,State,h,true);
                    }
                }
                foreach(string layer in new[]{RoomLayers.Ladder,RoomLayers.Stairs})
                {
                    if(!visible.Contains(layer))continue;
                    var d=Definition(layer,x,r);if(d?.FrontSprite==null)continue;
                    AddSprite(d.FrontSprite,x*room.Cell,Top-(y+1)*room.Cell,1,1,1,d.Rear?-50:10,0,layer,false,"normal");
                }
            }
            double lights=0;string lon=Option("lon");
            if(lon!=null&&!double.TryParse(lon,NumberStyles.Float,CultureInfo.InvariantCulture,out lights))plan.Issues.Add("Параметр lon не является числом.");
            var layoutIssues=new List<string>();
            List<RoomPlacedSprite> placed;
            try{placed=RoomSpriteLayout.Place(room,false,definitions,art,lights,withoutPicture,layoutIssues,new(),new());}
            catch(Exception e){plan.Issues.Add("Размещение рисунков: "+e.Message);placed=new();}
            plan.Issues.AddRange(layoutIssues);
            foreach(var p in placed)
            {
                string layer=p.Kind==RoomSpriteKind.Decor?"decor":"objects";
                if(!visible.Contains(layer)||!new[]{p.X,p.Y,p.ScaleX,p.ScaleY,p.Alpha}.All(double.IsFinite))continue;
                var d=p.Definition;
                int order=p.Kind==RoomSpriteKind.Object?-45:d.EraseFirst?-285:d.DecorLayer<=0?-280+d.DecorLayer:-55+d.DecorLayer;
                AddSprite(p.ArtId,p.X,Top-p.Y,p.ScaleX,p.ScaleY,p.Alpha,order,p.Index,layer,d.EraseFirst,d.Blend??"normal");
                if(p.EraseArtId!=null)
                {
                    var command=Sprite(p.EraseArtId,p.X,Top-p.Y,p.ScaleX,p.ScaleY,1,0,0,layer,false,"normal");
                    if(command!=null)plan.Erases.Add(command);
                }
            }
            plan.Commands=plan.Commands.OrderBy(c=>c.Order).ThenBy(c=>c.Group).ToList();
            plan.MaterialFilterFlags=flags.OrderBy(f=>f,StringComparer.Ordinal).ToList();
            return plan;
        }

        string Option(string key)
        {
            if(room.Options.TryGetValue(key,out var own))return own;
            // Custom template names can be edited before they are assigned to a land.
            if(room.Template?.Split(':').Length!=3)return null;
            return lands.Option(room,key,plan.Issues);
        }
        RoomDefinition Definition(string layer,int x,int row)
        {
            if(!room.Layers.TryGetValue(layer,out var rows)||rows[row][x]=='.')return null;
            if(!room.Palettes.TryGetValue(layer,out var palette)||!palette.TryGetValue(rows[row][x],out var id)||!definitions.Items.TryGetValue(id,out var d))return null;
            return d;
        }
        long First(string layer,string id)
        {
            if(first.TryGetValue((layer,id),out long found))return found;
            long index=long.MaxValue;
            if(room.Layers.TryGetValue(layer,out var rows)&&room.Palettes.TryGetValue(layer,out var palette))
                foreach(var key in palette.Where(p=>p.Value==id).Select(p=>p.Key))
                    for(int r=room.Height-1;r>=0;r--)
                    {
                        int x=rows[r].IndexOf(key);if(x<0)continue;
                        index=Math.Min(index,(long)x*room.Height+room.Height-1-r);
                        if(x==0)break;
                    }
            first[(layer,id)]=index;return index;
        }
        string Url(string id)
        {
            if(id!=null&&art.Art.TryGetValue(id,out var image))return "data/Tiles/"+image.File;
            plan.Issues.Add("Нет рисунка: "+id);return null;
        }
        RoomDrawCommand Sprite(string id,double x,double y,double sx,double sy,double alpha,int order,long group,string layer,bool erase,string blend)
        {
            if(id==null||!art.Art.TryGetValue(id,out var a)){plan.Issues.Add("Нет рисунка: "+id);return null;}
            return new(){Image=Url(id),ImageId=id,X=x,Y=y,Width=a.Width/a.Zoom/RoomSpriteLayout.SourcePixelsPerMeter,Height=a.Height/a.Zoom/RoomSpriteLayout.SourcePixelsPerMeter,
                OriginX=a.OriginX/RoomSpriteLayout.SourcePixelsPerMeter,OriginY=a.OriginY/RoomSpriteLayout.SourcePixelsPerMeter,
                ScaleX=sx,ScaleY=sy,Alpha=alpha,Order=order,Group=group,Layer=layer,EraseAffected=erase,Blend=blend};
        }
        void AddSprite(string id,double x,double y,double sx,double sy,double alpha,int order,long group,string layer,bool erase,string blend)
        {var command=Sprite(id,x,y,sx,sy,alpha,order,group,layer,erase,blend);if(command!=null)plan.Commands.Add(command);}
        void Layer(string textureId,string maskName,string layer,RoomDefinition d,int order,long group,int x,int y,Func<Quarter,int> state,int height,bool floor)
        {
            if(textureId==null)return;
            string texture=Url(textureId);if(texture==null)return;
            if(!art.Masks.TryGetValue(maskName,out var mask)){plan.Issues.Add("Нет маски: "+maskName);return;}
            double scale=floor?1:(4-height)/4.0,drop=floor?room.Cell*height/4:0;
            void Mask(string id,double rx,double ry,double sx,double sy)
            {
                var command=Sprite(id,rx,Top-ry,sx,sy,1,order,group,layer,layer==RoomLayers.Back,"normal");
                if(command==null)return;command.CellX=x;command.CellY=y;command.Kind="material";command.Texture=texture;command.TextureId=textureId;command.FilterFlag=d.Filter;plan.Commands.Add(command);
            }
            if(mask.Kind=="whole")Mask(TileManifest.MaskFrameId(mask.Clip,1),x*room.Cell+room.Cell/2,y*room.Cell+room.Cell/2*scale-drop,1,scale);
            else foreach(var q in mask.Quarters)
            {
                var quarter=Enum.Parse<Quarter>(q.Quarter,true);int s=state(quarter);if(s<0)continue;
                bool right=quarter==Quarter.TopRight||quarter==Quarter.BottomRight,top=quarter==Quarter.TopLeft||quarter==Quarter.TopRight;
                Mask(TileManifest.MaskFrameId(q.Clip,s+1),(x+(right?1:0))*room.Cell,y*room.Cell+(top?room.Cell*scale:0)-drop,q.FlipX?-1:1,(q.FlipY?-1:1)*scale);
            }
        }
        void Backdrop()
        {
            string far=Option("fon");if(far!=null&&art.Art.TryGetValue(TileManifest.TextureId(far),out var background))plan.FarBackground="data/Tiles/"+background.File;
            string name=Option("backwall");if(name==null||name=="sky")return;
            string texture=TileManifest.TextureId(name.Length==0?"tBackWall":name);
            if(!art.Art.ContainsKey(texture)){plan.Issues.Add("Фон не выгружен: "+name);texture=TileManifest.TextureId("tBackWall");}
            string url=Url(texture);if(url==null)return;
            int form=room.Options.TryGetValue("backform",out var value)&&int.TryParse(value,out var n)?n:0;
            double w=room.Width*room.Cell,h=Top;
            double Px(double pixels)=>pixels/art.CellPixels*room.Cell;
            void Rect(double x,double bottom,double width,double height)
            {if(width>0&&height>0)plan.Commands.Add(new(){Kind="fill",Texture=url,TextureId=texture,X=x,Y=Top-bottom-height,Width=width,Height=height,Order=-300,Layer=RoomLayers.Back,EraseAffected=true});}
            switch(form)
            {
                case 0:Rect(0,0,w,h);break;
                case 1:Rect(0,0,Px(11*art.CellPixels-10),h);Rect(Px(37*art.CellPixels+10),0,w-Px(37*art.CellPixels+10),h);break;
                case 2:Rect(0,0,w,h-Px(16*art.CellPixels+10));break;
                case 3:Rect(0,0,w,h-Px(24*art.CellPixels+10));break;
                default:plan.Issues.Add("Неизвестная форма фона: "+form);break;
            }
        }
    }
}
