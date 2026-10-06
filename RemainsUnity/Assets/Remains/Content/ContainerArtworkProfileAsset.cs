using System;
using UnityEngine;

namespace Remains.Content
{
    public enum ContainerArtworkMode {Static,ClosedOpened}
    [CreateAssetMenu(menuName="Remains/Графика контейнера",fileName="ContainerArtworkProfile")]
    public sealed class ContainerArtworkProfileAsset : ScriptableObject
    {
        [SerializeField,InspectorName("Версия профиля")] private int version=1;
        [SerializeField,InspectorName("Исходный тип объекта")] private string definition;
        [SerializeField,InspectorName("Правило рисунка")] private ContainerArtworkMode mode;
        [SerializeField,InspectorName("Исходный / закрытый кадр")] private Sprite closed;
        [SerializeField,InspectorName("После первого просмотра")] private Sprite opened;
        public int Version=>version;
        public string Definition=>definition;
        public ContainerArtworkMode Mode=>mode;
        public Sprite Closed=>closed;
        public Sprite Opened=>opened;
        public Sprite Select(bool wasOpened)=>mode==ContainerArtworkMode.ClosedOpened && wasOpened?opened:closed;
        public void Configure(string id,ContainerArtworkMode policy,Sprite first,Sprite second=null)
        {definition=id;mode=policy;closed=first;opened=second;version=1;Validate(id);}
        public void Validate(string id)
        {
            if(version!=1 || string.IsNullOrWhiteSpace(definition) || definition!=id || closed==null ||
                mode is not (ContainerArtworkMode.Static or ContainerArtworkMode.ClosedOpened) ||
                mode==ContainerArtworkMode.Static && opened!=null || mode==ContainerArtworkMode.ClosedOpened && (opened==null || opened==closed))
                throw new InvalidOperationException($"Некорректный профиль графики контейнера: asset={name}, type={id}/{definition}, version={version}, mode={mode}.");
        }
        private void OnValidate()
        {
            try {Validate(definition);}
            catch(InvalidOperationException error) {Debug.LogWarning(error.Message,this);}
        }
    }
}
