using System;
using System.Collections.Generic;
using System.Linq;
using Remains.Content;
using Remains.Domain;
using UnityEngine;

namespace Remains.Editor
{
    // Editor adapter: keeps the existing geometry, registration, palette and wardrobe as the artwork source.
    // Authoring controls apply deltas over it; they never reconstruct anatomy from texture/mesh AABBs.
    public sealed class PonyAnimationArtwork
    {
        public PonyRenderPart[] Parts {get;private set;}
        public PonyRigDrawPlan.Frame Frame {get;private set;}
        public PonyAppearanceKey Appearance {get;private set;}
        public Bounds Bounds=>Frame.Bounds;
        public string Outfit {get;private set;}
        public string SourceClip {get;private set;}
        public int SourceFrame {get;private set;}

        public static PonyAnimationArtwork Prepare(string outfit="",string sourceClip="idle",int sourceFrame=0,CharacterAppearance appearance=null)
        {
            PonyRenderSelection.Prepare();
            var actor=new ActorState {id="pony-animation-editor",actorType="pony",anatomy="pony",player=true,connected=true,
                pose=sourceClip,facing=1,weapon=null,appearance=appearance??new CharacterAppearance {version=CharacterAppearance.CurrentVersion}};
            string expectedVisual=null;
            if(!string.IsNullOrEmpty(outfit))
            {
                if(!CosmeticCatalog.Current.TryGet(outfit,out var definition) || definition.Anatomy!="pony")
                    throw new InvalidOperationException("Редактор анимации: неизвестный комплект пони "+outfit+".");
                expectedVisual=definition.Visual;
                var item=SharedInventory.Create(outfit,1);InventoryStacks.Add(actor,item);
                if(!CosmeticEquipment.TryEquip(actor,item.id,false,out var reason))
                    throw new InvalidOperationException("Редактор анимации: не надет "+outfit+": "+reason);
            }
            var plan=PonyRenderSelection.PrepareEquipment(actor);
            if(expectedVisual!=null && !plan.VisualIds.Contains(expectedVisual,StringComparer.Ordinal))
                throw new InvalidOperationException($"Редактор анимации: комплект {outfit} не предоставил рисунок {expectedVisual}.");
            var frames=plan.Frames(sourceClip);
            if(sourceFrame<0 || sourceFrame>=frames.Length)
                throw new InvalidOperationException($"Редактор анимации: нет исходного кадра {sourceClip}/{sourceFrame}; кадров={frames.Length}.");
            var result=new PonyAnimationArtwork {Parts=frames[sourceFrame],Frame=PonyRigDrawPlan.Prepare(plan).Get(sourceClip,sourceFrame),
                Appearance=PonyAppearance.Snapshot(actor),Outfit=outfit??"",SourceClip=sourceClip,SourceFrame=sourceFrame};
            if(result.Frame.Nodes.Length!=17 || result.Frame.PartNodes.Length!=result.Parts.Length)
                throw new InvalidOperationException($"Редактор анимации: повреждён рисунок {sourceClip}/{sourceFrame}, узлов={result.Frame.Nodes.Length}.");
            return result;
        }

        public Matrix4x4[] Matrices(PonyPoseDocument document,PonyPoseValue[] pose)
        {
            RequireSource(document);
            var deltas=PonyPoseMath.SocketDeltas(document,pose);var matrices=new Matrix4x4[Frame.Nodes.Length];
            for(int i=0;i<matrices.Length;i++)
            {
                var node=Frame.Nodes[i];
                if(!deltas.TryGetValue(node.socket,out var delta))
                    throw new InvalidOperationException("Редактор анимации: поза не задаёт гнездо "+node.socket+".");
                matrices[i]=delta*node.transform.Matrix;
            }
            return matrices;
        }

        public static PonyAnimationArtwork ReplaceNodes(PonyAnimationArtwork source,PonyAnimationArtwork replacement,System.Collections.Generic.IReadOnlyDictionary<string,Matrix4x4> canonical)
        {
            if(source.Outfit!=replacement.Outfit)throw new InvalidOperationException("Нельзя соединить рисунки разных комплектов.");
            var nodes=source.Frame.Nodes.Select(n=>new PonyRigNode {top=n.top,socket=n.socket,transform=canonical.TryGetValue(n.socket,out var value)?PonyRigTransform.FromMatrix(value):n.transform}).ToArray();
            var parts=new List<PonyRenderPart>();var partNodes=new List<int>();var inserted=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<source.Parts.Length;i++)
            {
                int node=source.Frame.PartNodes[i];string socket=nodes[node].socket;
                if(!canonical.ContainsKey(socket)){parts.Add(source.Parts[i]);partNodes.Add(node);continue;}
                if(!inserted.Add(socket))continue;
                int before=parts.Count;
                for(int r=0;r<replacement.Parts.Length;r++)if(replacement.Frame.Nodes[replacement.Frame.PartNodes[r]].socket==socket)
                {parts.Add(replacement.Parts[r]);partNodes.Add(node);}
                if(parts.Count==before)throw new InvalidOperationException("В образце нет рисунка гнезда "+socket);
            }
            var frame=new PonyRigDrawPlan.Frame {Nodes=nodes,PartNodes=partNodes.ToArray()};bool any=false;
            for(int i=0;i<parts.Count;i++)
            {
                var bounds=PonyRigDrawPlan.TransformBounds(parts[i].mesh.bounds,nodes[partNodes[i]].transform.Matrix);
                if(!any){frame.Bounds=bounds;any=true;}else frame.Bounds.Encapsulate(bounds);
            }
            return new PonyAnimationArtwork {Parts=parts.ToArray(),Frame=frame,Appearance=source.Appearance,Outfit=source.Outfit,SourceClip=source.SourceClip,SourceFrame=source.SourceFrame};
        }

        // Cosmetic facial states may replace the complete eye subtree and change the part count. Keep the
        // authored node placements intact; rebuild the lookup from the source paths of the resulting parts.
        public static PonyAnimationArtwork WithExpression(PonyAnimationArtwork source,PonyFacialExpression expression)
        {
            if(source==null)throw new ArgumentNullException(nameof(source));
            var parts=PonyNestedEyeFrames.Prepare(source.Parts).Parts(expression);
            var nodes=source.Frame.Nodes;var byTop=nodes.Select((n,i)=>(n.top,i)).ToDictionary(n=>n.top,n=>n.i,StringComparer.Ordinal);
            var partNodes=new int[parts.Length];Bounds bounds=default;bool any=false;
            for(int p=0;p<parts.Length;p++)
            {
                string path=parts[p].sourcePath;int slash=path?.IndexOf('/')??-1;string top=slash<0?path:path.Substring(0,slash);
                if(top==null || !byTop.TryGetValue(top,out int node))
                    throw new InvalidOperationException($"Предпросмотр мимики: часть {path} не имеет узла рисунка {source.SourceClip}/{source.SourceFrame}.");
                partNodes[p]=node;
                if(!parts[p].visible)continue;
                var partBounds=PonyRigDrawPlan.TransformBounds(parts[p].mesh.bounds,nodes[node].transform.Matrix);
                if(!any){bounds=partBounds;any=true;}else bounds.Encapsulate(partBounds);
            }
            var frame=new PonyRigDrawPlan.Frame {Nodes=nodes,PartNodes=partNodes,Bounds=any?bounds:source.Frame.Bounds};
            return new PonyAnimationArtwork {Parts=parts,Frame=frame,Appearance=source.Appearance,Outfit=source.Outfit,
                SourceClip=source.SourceClip,SourceFrame=source.SourceFrame};
        }

        public PonyRigFrame CompileFrame(PonyPoseDocument document,PonyPoseValue[] pose)
        {
            var matrices=Matrices(document,pose);var nodes=new PonyRigNode[matrices.Length];
            for(int i=0;i<nodes.Length;i++)
            {
                var original=Frame.Nodes[i];var value=PonyRigTransform.FromMatrix(matrices[i]);
                for(int element=0;element<16;element++)
                    if(Mathf.Abs(value.Matrix[element]-matrices[i][element])>.00005f)
                        throw new InvalidOperationException("Редактор анимации: преобразование теряет наклон у "+original.socket+".");
                nodes[i]=new PonyRigNode {top=original.top,socket=original.socket,transform=value};
            }
            return new PonyRigFrame {nodes=nodes};
        }

        public Bounds PoseBounds(PonyPoseDocument document,PonyPoseValue[] pose)
        {
            var matrices=Matrices(document,pose);Bounds bounds=default;bool any=false;
            for(int i=0;i<Parts.Length;i++)
            {
                var part=Parts[i];if(!part.visible)continue;
                var transformed=PonyRigDrawPlan.TransformBounds(part.mesh.bounds,matrices[Frame.PartNodes[i]]);
                if(!any){bounds=transformed;any=true;}else bounds.Encapsulate(transformed);
            }
            if(!any)throw new InvalidOperationException("Редактор анимации: рисунок позы пуст.");
            return bounds;
        }

        private void RequireSource(PonyPoseDocument document)
        {
            if(document==null || document.sourceClip!=SourceClip || document.sourceFrame!=SourceFrame)
                throw new InvalidOperationException($"Редактор анимации: исходный рисунок позы {document?.sourceClip}/{document?.sourceFrame} не совпадает с подготовленным {SourceClip}/{SourceFrame}.");
        }

        public sealed class Limb
        {
            public string Label {get;}
            public int Root {get;}
            public int Middle {get;}
            public int End {get;}
            public Limb(string label,int root,int middle,int end){Label=label;Root=root;Middle=middle;End=end;}
        }

        public static Limb[] Limbs(PonyPoseDocument document)
        {
            if(document==null)throw new ArgumentNullException(nameof(document));
            var lookup=document.joints.Select((joint,index)=>(joint.id,index)).ToDictionary(pair=>pair.id,pair=>pair.index,StringComparer.Ordinal);
            return new[]
            {
                Leg("Передняя ближняя","frontMiddleNear","frontLowerNear","frontHoofNear"),
                Leg("Передняя дальняя","frontMiddleFar","frontLowerFar","frontHoofFar"),
                Leg("Задняя ближняя","hindUpperNear","hindLowerNear","hindHoofNear"),
                Leg("Задняя дальняя","hindUpperFar","hindLowerFar","hindHoofFar")
            };
            Limb Leg(string label,string root,string middle,string end)
            {
                foreach(string id in new[]{root,middle,end})if(!lookup.ContainsKey(id))
                    throw new InvalidOperationException("Редактор анимации: в цепочке ноги нет сустава "+id+".");
                return new Limb(label,lookup[root],lookup[middle],lookup[end]);
            }
        }

        public static PonyPoseDocument CreateDocument()
        {
            // Source: hierarchy.json at donor 0a04b34314352d52951590c09674f2c28b15008c,
            // also checked against body_stay by GodotRigPilotData. These are graphic registration origins,
            // not claims of a recovered anatomical skeleton. The control hierarchy below is ours.
            const float sourceScale=1.29998779296875f/50f;
            var definitions=new List<PonyPoseJoint>();var origins=new List<Vector2>();
            Add("torso","Туловище",null,.8f,-25.85f);
            Add("neck","Шея","torso",3.5f,-29.35f);
            Add("head","Голова","neck",14.1f,-43.8f);
            Add("mane","Грива","head",3.55f,-44f);
            Add("tail","Хвост","torso",-8.2f,-30.95f);
            Add("frontUpperNear","Плечо ближнее","torso",7.95f,-21.1f);
            Add("frontMiddleNear","Локоть ближний","frontUpperNear",7.65f,-17.3f);
            Add("frontLowerNear","Запястье ближнее","frontMiddleNear",7.2f,-12.3f);
            Add("pipbuckNear","ПипБак ближний","frontLowerNear",7.5f,-12.4f);
            Add("frontUpperFar","Плечо дальнее","torso",9.7f,-22.7f);
            Add("frontMiddleFar","Локоть дальний","frontUpperFar",9.9f,-18.85f);
            Add("frontLowerFar","Запястье дальнее","frontMiddleFar",10.1f,-13.9f);
            Add("pipbuckFar","ПипБак дальний","frontLowerFar",10.75f,-12.75f);
            Add("hindUpperNear","Бедро ближнее","torso",-8.9f,-30.05f);
            Add("hindLowerNear","Скакательный сустав ближний","hindUpperNear",-9.3f,-16.3f);
            Add("hindUpperFar","Бедро дальнее","torso",-4.95f,-30.15f);
            Add("hindLowerFar","Скакательный сустав дальний","hindUpperFar",-5.35f,-17.25f);
            // Hoof tips are new editable controls in actor metres, not donor joint metadata.
            Tip("frontHoofNear","Копыто переднее ближнее","frontLowerNear",new Vector2(.22f,.01f));
            Tip("frontHoofFar","Копыто переднее дальнее","frontLowerFar",new Vector2(.29f,.05f));
            Tip("hindHoofNear","Копыто заднее ближнее","hindLowerNear",new Vector2(-.28f,-.025f));
            Tip("hindHoofFar","Копыто заднее дальнее","hindLowerFar",new Vector2(-.18f,.025f));
            var document=ScriptableObject.CreateInstance<PonyPoseDocument>();document.clipId="custom_pose";
            document.sourceClip="idle";document.sourceFrame=0;document.joints=definitions.ToArray();
            document.keys=Array.Empty<PonyPoseKey>();document.SetKey(0,PonyPoseMath.Rest(document));document.Validate();return document;

            void Add(string id,string label,string parent,float tx,float ty)=>Insert(id,label,parent,new Vector2(tx*sourceScale,-ty*sourceScale),id);
            void Tip(string id,string label,string parent,Vector2 world)=>Insert(id,label,parent,world,null);
            void Insert(string id,string label,string parent,Vector2 world,string socket)
            {
                int index=parent==null?-1:definitions.FindIndex(j=>j.id==parent);
                if(parent!=null && index<0)throw new InvalidOperationException("Редактор анимации: не создан родитель "+parent+".");
                definitions.Add(new PonyPoseJoint {id=id,label=label,socket=socket,parent=index,
                    rest=new PonyPoseValue {position=index<0?world:world-origins[index],angle=0,scale=Vector2.one}});
                origins.Add(world);
            }
        }
    }
}
