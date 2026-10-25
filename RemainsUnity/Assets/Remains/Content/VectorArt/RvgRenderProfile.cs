using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Remains.Content.VectorArt
{
    // The game's rendering rules over the faithful RVG, the same three the catalog renders and today's imported art were
    // made with (Tools/ContentExport/RenderSource.cs, catalog `renders`; user decisions 2026-10-10):
    //  - hairlines: a stroke narrower than HairlineTwips (one screen pixel at the 1080p reference) is widened to it;
    //  - glow: GLOW filters are dropped inside weapon appearance and item image sprites (our lighting replaces them);
    //  - parts: named moving parts leave their base sprite's first frame (they are drawn and animated separately).
    // RVG itself stays faithful to the SWF; the profile is applied to a parsed document before rasterizing.
    public sealed class RvgRenderProfile
    {
        public readonly struct Part
        {
            public readonly int Sprite,Depth;
            public Part(int sprite,int depth) {Sprite=sprite;Depth=depth;}
        }
        public readonly struct Result
        {
            public readonly int WidenedStrokes,GlowFiltersRemoved,PartPlacementsRemoved;
            public Result(int widened,int glows,int parts) {WidenedStrokes=widened;GlowFiltersRemoved=glows;PartPlacementsRemoved=parts;}
        }

        public readonly int HairlineTwips;
        public IReadOnlyCollection<int> GlowFreeSprites=>glowFree;
        private readonly HashSet<int> glowFree;
        public readonly IReadOnlyList<Part> Parts;

        public RvgRenderProfile(int hairlineTwips,IEnumerable<int> glowFreeSprites,IEnumerable<Part> parts)
        {
            if(hairlineTwips<0) throw new ArgumentOutOfRangeException(nameof(hairlineTwips));
            HairlineTwips=hairlineTwips;
            glowFree=new HashSet<int>(glowFreeSprites??throw new ArgumentNullException(nameof(glowFreeSprites)));
            Parts=(parts??throw new ArgumentNullException(nameof(parts))).Distinct().ToList();
        }

        // Changes the document in place. Glow counts filters per frame of the flattened display lists, so it is not the
        // count of SWF PlaceObject filters; strokes count line styles, as the XML patch does.
        public Result Apply(RvgDocument document)
        {
            if(document==null) throw new ArgumentNullException(nameof(document));
            int widened=0,glows=0,parts=0;
            foreach(var shape in document.shapes)
                foreach(var line in shape.lines)
                    if(line.width<HairlineTwips) {line.width=HairlineTwips;widened++;}
            foreach(var sprite in document.sprites)
            {
                if(!glowFree.Contains(sprite.id)) continue;
                foreach(var frame in sprite.frames) foreach(var placed in frame) glows+=placed.filters.RemoveAll(f=>f.type=="glow");
            }
            foreach(var group in Parts.GroupBy(p=>p.Sprite))
            {
                if(!document.HasSprite(group.Key)) continue;   // the part's base sprite belongs to another archive
                var sprite=document.Sprite(group.Key);
                foreach(var part in group)
                {
                    var first=sprite.frames[0].FirstOrDefault(o=>o.depth==part.Depth)
                        ??throw new InvalidDataException($"RVG {document.source?.archive}: часть на глубине {part.Depth} не найдена в первом кадре спрайта {sprite.id}.");
                    // The SWF loses the placement itself: the instance stays absent while later frames only move it.
                    foreach(var frame in sprite.frames)
                    {
                        int at=frame.FindIndex(o=>o.depth==part.Depth);
                        if(at<0 || frame[at].character!=first.character) break;
                        frame.RemoveAt(at);
                    }
                    parts++;
                }
            }
            return new Result(widened,glows,parts);
        }
    }
}
