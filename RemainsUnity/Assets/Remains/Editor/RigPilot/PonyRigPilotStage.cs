using System;
using Remains.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Remains.Editor
{
    // Labelled diagnostic silhouettes, not level geometry or collision truth.
    public sealed class PonyRigPilotStage:IDisposable
    {
        private readonly Mesh quad;
        private readonly Material material;
        private readonly MaterialPropertyBlock block=new();
        public PonyRigPilotStage()
        {
            quad=new Mesh {name="Схема опоры предпросмотра",vertices=new[]{Vector3.zero,Vector3.right,Vector3.up,new Vector3(1,1,0)},
                uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one},triangles=new[]{0,2,1,2,3,1},colors=new[]{Color.white,Color.white,Color.white,Color.white}};
            quad.RecalculateBounds();material=WorldArtworkMaterial.Create(Texture2D.whiteTexture);
        }
        public void Draw(CommandBuffer command,GodotRigPilotData.Clip clip,int frame,int facing,bool front)
        {
            if(clip.Context=="box")
            {
                if(clip.Frames[frame].CoverInFront!=front)return;
                Rect(-.64f,0,1.28f,.86f,new Color(.22f,.13f,.07f));
                Rect(-.60f,.045f,1.20f,.77f,new Color(.40f,.27f,.14f));
                for(int row=1;row<5;row++)Rect(-.60f,row*.155f,1.20f,.018f,new Color(.26f,.16f,.08f));
                Rect(-.57f,.04f,.07f,.77f,new Color(.54f,.39f,.23f));Rect(.5f,.04f,.07f,.77f,new Color(.54f,.39f,.23f));
                Rect(-.60f,.055f,1.20f,.065f,new Color(.54f,.39f,.23f));Rect(-.60f,.735f,1.20f,.065f,new Color(.54f,.39f,.23f));
            }
            else if(front)return;
            else if(clip.Context=="ladder")
            {
                Rect(.43f,-.18f,.055f,1.95f,new Color(.36f,.39f,.43f));Rect(.85f,-.18f,.055f,1.95f,new Color(.36f,.39f,.43f));
                for(int rung=0;rung<11;rung++)Rect(.43f,rung*.18f-.15f,.47f,.045f,new Color(.50f,.54f,.59f));
            }
            else if(clip.Context=="edge")
            {
                bool anterior=clip.Id.EndsWith("front",StringComparison.Ordinal);float cut=.2f-.25f*frame/10;
                Rect(anterior?-1.35f:-cut,-.10f,1.35f+cut,.10f,new Color(.34f,.35f,.38f));
                Rect(anterior?-1.35f:-cut,-.14f,1.35f+cut,.04f,new Color(.19f,.20f,.23f));
            }
            void Rect(float x,float y,float w,float h,Color color)
            {
                block.Clear();block.SetTexture("_MainTex",Texture2D.whiteTexture);block.SetColor("_Color",color);block.SetFloat("_Emission",0);
                var matrix=Matrix4x4.Scale(new Vector3(facing,1,1))*Matrix4x4.TRS(new Vector3(x,y,0),Quaternion.identity,new Vector3(w,h,1));
                command.DrawMesh(quad,matrix,material,0,0,block);
            }
        }
        public void Dispose(){Object.DestroyImmediate(quad);Object.DestroyImmediate(material);}
    }
}
