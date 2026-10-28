using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Remains.Editor
{
    // Explicit presentation recipes from source UnitPlayer's scrub/overhang/lurk drivers.
    // These preview recipes do not implement terrain detection, collision or gameplay state.
    public static class PonyRigPilotSets
    {
        public static readonly Dictionary<string,string> Labels=new(StringComparer.Ordinal)
        {
            {"body_stay","Стойка"},{"body_trot","Ходьба"},{"body_sit","Лёжа"},{"body_sit_down","Переход в приседание"},{"body_sit_up","Подняться из приседания"},
            {"ladder_hold","Лестница: держаться"},{"ladder_up","Лестница: вверх"},{"ladder_down","Лестница: вниз"},
            {"edge_stand_front","Край: свисание спереди, стоя"},{"edge_stand_back","Край: свисание сзади, стоя"},
            {"edge_crouch_front","Край: свисание спереди, лёжа"},{"edge_crouch_back","Край: свисание сзади, лёжа"},
            {"hide1","Укрытие за ящиком: вариант 1"},{"hide2","Укрытие за ящиком: вариант 2"},{"hide3","Укрытие за ящиком: вариант 3"}
        };
        public static void Expand(Dictionary<string,GodotRigPilotData.Clip> clips)
        {
            var ladder=Take(clips,"body_laz",13);
            Add("ladder_hold",ladder.Frames.Take(1),ladder,"ladder",false);
            Add("ladder_up",ladder.Frames.Skip(1),ladder,"ladder",true);
            Add("ladder_down",ladder.Frames.Skip(1).Reverse(),ladder,"ladder",true);
            var edge=Take(clips,"body_shield",44);
            // body_shield is the donor's scrub section over body_stay source frames 27..70, not a shield animation.
            int at=0;
            foreach(string id in new[]{"edge_stand_front","edge_stand_back","edge_crouch_front","edge_crouch_back"})
            {Add(id,edge.Frames.Skip(at).Take(11),edge,"edge",false);at+=11;}
            for(int type=1;type<=3;type++)
            {
                var enter=Take(clips,"body_lurk"+type,type==1?10:20);
                var leave=Take(clips,"body_lurk"+type+"_un",type==3?12:11);
                var frames=new List<GodotRigPilotData.Frame>();
                for(int f=0;f<enter.Frames.Length;f++)frames.Add(Cover(enter.Frames[f],f>=10));
                // Source stops on label "un" until the player leaves cover. Half-second hold makes that state visible.
                for(int f=0;f<15;f++)frames.Add(Cover(enter.Frames[enter.Frames.Length-1],true));
                for(int f=0;f<leave.Frames.Length;f++)frames.Add(Cover(leave.Frames[f],f<5));
                Add("hide"+type,frames,enter,"box",true);
            }
            foreach(var clip in clips.Values)if(!Labels.ContainsKey(clip.Id))throw new InvalidDataException("Пилот: нет русского названия "+clip.Id);
            void Add(string id,IEnumerable<GodotRigPilotData.Frame> frames,GodotRigPilotData.Clip source,string context,bool loop)
                =>clips.Add(id,new GodotRigPilotData.Clip {Id=id,Frames=frames.ToArray(),Order=source.Order,Context=context,Loop=loop});
        }
        private static GodotRigPilotData.Clip Take(Dictionary<string,GodotRigPilotData.Clip> clips,string key,int expected)
        {
            if(!clips.Remove(key,out var clip) || clip.Frames.Length!=expected)throw new InvalidDataException("Пилот: изменился диапазон исходного клипа "+key);
            return clip;
        }
        private static GodotRigPilotData.Frame Cover(GodotRigPilotData.Frame frame,bool inFront)=>new()
        {Delta=frame.Delta,Libraries=frame.Libraries,Brightness=frame.Brightness,CoverInFront=inFront};
    }
}
