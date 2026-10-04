using System;
using UnityEngine;

namespace Remains.Presentation
{
    // Presentation only. The expanded quad lets the halo extend beyond the sprite mesh.
    public sealed class HeldPropGlow : MonoBehaviour
    {
        private Mesh mesh;
        private Material material;
        private MeshRenderer halo;
        public bool Visible=>halo!=null && halo.enabled;
        public void Initialize(SpriteRenderer artwork,Color color)
        {
            var sprite=artwork.sprite;
            if(sprite==null || sprite.packed) throw new InvalidOperationException("Свечение предмета требует отдельного непакованного спрайта: "+artwork.name);
            var shader=Resources.Load<Shader>("Presentation/HeldPropGlow");
            if(shader==null) throw new InvalidOperationException("Нет шейдера Presentation/HeldPropGlow.");
            var bounds=sprite.bounds;
            const float border=.08f;
            float x0=bounds.min.x-border,x1=bounds.max.x+border;
            float y0=bounds.min.y-border,y1=bounds.max.y+border;
            mesh=new Mesh {name="Ореол предмета"};
            mesh.vertices=new[]{new Vector3(x0,y0),new Vector3(x1,y0),new Vector3(x1,y1),new Vector3(x0,y1)};
            mesh.uv=new[]{new Vector2(-border/bounds.size.x,-border/bounds.size.y),new Vector2(1+border/bounds.size.x,-border/bounds.size.y),new Vector2(1+border/bounds.size.x,1+border/bounds.size.y),new Vector2(-border/bounds.size.x,1+border/bounds.size.y)};
            mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateBounds();
            gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            halo=gameObject.AddComponent<MeshRenderer>();
            material=new Material(shader) {name="Свечение "+artwork.name,mainTexture=sprite.texture};
            var rect=sprite.textureRect;
            material.SetVector("_SpriteRect",new Vector4(rect.x/sprite.texture.width,rect.y/sprite.texture.height,rect.width/sprite.texture.width,rect.height/sprite.texture.height));
            material.SetVector("_Radius",new Vector4(border/bounds.size.x,border/bounds.size.y,0,0));
            material.SetColor("_Color",color);
            halo.sharedMaterial=material;halo.sortingLayerID=artwork.sortingLayerID;halo.sortingOrder=artwork.sortingOrder+1;
            halo.enabled=false;
        }
        public void Draw(bool held) {if(halo.enabled!=held) halo.enabled=held;}
        private void OnDestroy()
        {
            if(Application.isPlaying) {Destroy(mesh);Destroy(material);}
            else {DestroyImmediate(mesh);DestroyImmediate(material);}
        }
    }
}
