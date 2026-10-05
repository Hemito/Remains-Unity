using System;
using UnityEngine;

namespace Remains.Content
{
    // Identity only. Geometry is owned by the ordinary surface/artwork components.
    public sealed class RoomLadderLandingAuthoring : MonoBehaviour
    {
        [SerializeField,InspectorName("Постоянный ID верхней посадки")] private string stableId;
        public string StableId=>stableId;
        public void Configure(string id)
        {
            if(string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Верхняя посадка: нужен постоянный ID.",nameof(id));
            stableId=id;
        }
    }
}
