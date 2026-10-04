using System;
using UnityEngine;

namespace Remains.Content
{
    public sealed class RoomImportAudit : MonoBehaviour
    {
        [SerializeField,InspectorName("Отчёт исходных объектов и недостающих механик")] private RoomImportReportAsset report;
        public RoomImportReportAsset Report=>report??throw new InvalidOperationException("Нет отчёта импорта: "+name);
        public void Configure(RoomImportReportAsset value)=>report=value;
        public void WarnIncomplete()=>Report.WarnIncomplete(this);
    }
}
