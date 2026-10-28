using System;
using UnityEngine;

namespace Remains.Content
{
    // Alternate eye state is prepared on appearance/equipment events. Mesh/material creation stays in Editor authoring.
    public sealed class PonyNestedEyeFrame
    {
        public PonyRenderPart[] Open {get;}
        public PonyRenderPart[] Closed {get;}
        public bool CanBlink {get;}
        internal PonyNestedEyeFrame(PonyRenderPart[] open,PonyRenderPart[] closed,bool canBlink)
        {Open=open;Closed=closed;CanBlink=canBlink;}
    }
    public static class PonyNestedEyeFrames
    {
        private static PonyNestedEyeAsset asset;
        public static bool IsEyePart(PonyRenderPart part)=>part?.sourcePath?.IndexOf("/eye/",StringComparison.Ordinal)>=0;
        public static void Reset()=>asset=null;
        public static PonyNestedEyeFrame Prepare(PonyRenderPart[] open)
        {
            if(open==null || open.Length==0)throw new ArgumentException("Моргание пони: нет подготовленных частей кадра.",nameof(open));
            PonyRenderPart anchor=null;int first=-1,removed=0;string container=null;
            for(int p=0;p<open.Length;p++)
            {
                var part=open[p];if(!IsEyePart(part))continue;
                int at=part.sourcePath.IndexOf("/eye/",StringComparison.Ordinal);string prefix=part.sourcePath.Substring(0,at+5);
                if(container!=null && container!=prefix)throw new InvalidOperationException("Моргание пони: неоднозначные видимые поддеревья глаз: "+container+" / "+prefix);
                container=prefix;if(first<0)first=p;removed++;
                if(part.sourceShape!="216")continue;
                if(anchor!=null)throw new InvalidOperationException("Моргание пони: повтор опорного символа216: "+part.sourcePath);
                anchor=part;
            }
            // A fully opaque helmet may explicitly remove the complete eye subtree.
            if(first<0)return new PonyNestedEyeFrame(open,open,false);
            if(anchor==null)throw new InvalidOperationException("Моргание пони: видимый глаз не имеет опорного символа216: "+container);
            if(asset==null)
            {
                var loaded=GameAssets.Load<PonyNestedEyeAsset>(PonyNestedEyeAsset.Resource)??throw new InvalidOperationException("Нет сохранённого символа моргания "+PonyNestedEyeAsset.Resource);
                loaded.Validate();asset=loaded;
            }
            var points=new Vector3[4];var matrix=anchor.symbolPose.Matrix;
            for(int p=0;p<4;p++)points[p]=matrix.MultiplyPoint3x4(asset.Points[p]);
            var replacement=new PonyRenderPart {mesh=asset.Mesh,material=asset.Material,slot="none",visible=true,
                hairStyle=0,eyeStyle=0,drawOrder=anchor.drawOrder,sourceIndex=anchor.sourceIndex,
                sourcePath=container+"1",sourceShape="218",points=points,baseColor=Color.white,orderPath=anchor.orderPath,
                symbolId=anchor.symbolId,symbolPose=anchor.symbolPose};
            var closed=new PonyRenderPart[open.Length-removed+1];int target=0;
            for(int p=0;p<open.Length;p++)
            {
                if(p==first)closed[target++]=replacement;
                if(!IsEyePart(open[p]))closed[target++]=open[p];
            }
            return new PonyNestedEyeFrame(open,closed,true);
        }
    }
}
