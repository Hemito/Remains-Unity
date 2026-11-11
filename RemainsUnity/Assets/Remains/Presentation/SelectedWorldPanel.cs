using System;
using System.IO;
using System.Threading.Tasks;
using Remains.Persistence;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    public sealed class SelectedWorldPanel : MonoBehaviour
    {
        private Text description;
        private Button host;
        private int generation;
        private Func<bool> canStart;
        public bool Busy {get;private set;}
        public void Initialize(Text text,Button button,Func<bool> available=null) {description=text;host=button;canStart=available;}
        public void SetPending(string text)
        {
            // Invalidate an older read before a mutating operation begins.
            // Its completion must not enable Host against an unfinished selection.
            generation++;Busy=true;host.interactable=false;description.text=text;
        }
        public async void Refresh(SaveStore store)
        {
            int request=++generation;Busy=true;host.interactable=false;description.text=Remains.Content.GameLocalization.UiText("source.SelectedWorldPanel.9fe00ab38889731b", "Чтение выбранного мира…", "RemainsUnity/Assets/Remains/Presentation/SelectedWorldPanel.cs");
            try
            {
                var result=await Task.Run(()=>
                {
                    store.EnsureWorldLibrary();
                    string path=Path.Combine(store.WorldDirectoryPath,"world.json");
                    var summary=File.Exists(path)?WorldFileSummary.Read(path):null;
                    return (summary,summary==null?null:new WorldLibrary(store).Name(summary.worldId),store.LibraryWarnings.Count>0);
                });
                if(this==null || request!=generation) return;
                host.interactable=canStart?.Invoke()??true;
                host.GetComponentInChildren<Text>().text=result.Item1==null?Remains.Content.GameLocalization.UiText("source.SelectedWorldPanel.7e33e928010bd887", "Начать в новом мире", "RemainsUnity/Assets/Remains/Presentation/SelectedWorldPanel.cs"):Remains.Content.GameLocalization.UiText("source.SelectedWorldPanel.70674b38b7c69c95", "Продолжить выбранный мир", "RemainsUnity/Assets/Remains/Presentation/SelectedWorldPanel.cs");
                description.text=result.Item1==null?Remains.Content.GameLocalization.UiText("source.SelectedWorldPanel.8d3550878b807556", "Сохранённый мир не выбран.\n«Миры и сохранения»: выбор, имя и удаление.", "RemainsUnity/Assets/Remains/Presentation/SelectedWorldPanel.cs"):
                    MainMenuPanel.DescribeWorld(result.Item2,result.Item1.branchId??"",result.Item1.revision,result.Item1.snapshot);
                if(result.Item3) description.text+=Remains.Content.GameLocalization.UiText("source.SelectedWorldPanel.6af915edfe833488", "\nВ библиотеке есть предупреждения; подробности — в списке миров и журнале.", "RemainsUnity/Assets/Remains/Presentation/SelectedWorldPanel.cs");
            }
            catch(Exception error)
            {
                if(this==null || request!=generation) return;
                host.interactable=false;
                description.text=Remains.Content.GameLocalization.UiText("source.SelectedWorldPanel.3d4c20bd32c33055", "Выбранное сохранение повреждено или имеет неподдерживаемый формат.\nОткройте «Миры и сохранения» и выберите другую копию.", "RemainsUnity/Assets/Remains/Presentation/SelectedWorldPanel.cs");
                Debug.LogWarning($"Не удалось прочитать выбранный мир {store.WorldDirectoryPath}: {error}");
            }
            finally {if(this!=null && request==generation) Busy=false;}
        }
    }
}
