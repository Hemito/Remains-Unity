using System;
using UnityEngine;

namespace Remains.Editor
{
    public static class PonyLegShapePose
    {
        public sealed class Result
        {
            public Vector2[][] Points;
            public PonyLegShapeModel.Prediction Prediction;
            public float UsedStrength;
            public string LimitedReason;
        }
        public static Result Evaluate(PonyLegShapeModel model,PonyPuppetControls.Input input,float strength=1,string filter="all",string previousVariant=null)
        {
            if(!float.IsFinite(strength) || strength<0 || strength>1)throw new InvalidOperationException("Примеры формы: неверная сила поправки.");
            // Width and seam adhesion belong to the procedural alternative. Learned shape supplies them.
            var baseline=PonyPuppetControls.Solve(new PonyPuppetControls.Input {Body=input.Body,Foot=input.Foot,KneeOffset=input.KneeOffset,Limits=input.Limits}).Points;
            var local=new Vector2[2][];for(int p=0;p<2;p++){local[p]=new Vector2[3];for(int i=0;i<3;i++)local[p][i]=baseline[p][i]-input.Body;}
            var prediction=model.Predict(PonyLegShapeModel.Driver(local),filter,previousVariant);string reason=null;
            for(int step=0;step<=12;step++)
            {
                float used=step==12?0:strength*Mathf.Pow(.5f,step);var points=new Vector2[2][];bool valid=true;
                for(int p=0;p<2;p++)
                {
                    points[p]=new Vector2[3];for(int i=0;i<3;i++)points[p][i]=Vector2.LerpUnclamped(local[p][i],prediction.Points[p][i],used)+input.Body;
                    var matrix=PonyAffinePoints.Solve(PonyPointControls.Parts[p].Rest,points[p]);
                    if(!PonyAffinePoints.AcceptEdit(matrix,out var geometryReason)){reason=geometryReason;valid=false;}
                }
                if(!PonyPuppetLimits.Check(points,input.Limits,out var jointReason)){reason=jointReason;valid=false;}
                if(valid)return new Result {Points=points,Prediction=prediction,UsedStrength=used,LimitedReason=used<strength?reason:null};
            }
            throw new InvalidOperationException("Примеры формы: даже базовая поза не проходит ограничения: "+reason);
        }
    }
}
