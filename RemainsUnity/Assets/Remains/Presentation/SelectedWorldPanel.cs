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
        private WorldSummary cachedSummary;
        private string cachedName;
        private bool cachedWarnings,hasResult;
        public bool Busy {get;private set;}
        public void Initialize(Text text,Button button,Func<bool> available=null) {description=text;host=button;canStart=available;}
        public void SetPending(string text)
        {
            // Invalidate an older read before a mutating operation begins.
            // Its completion must not enable Host against an unfinished selection.
            generation++;Busy=true;hasResult=false;host.interactable=false;description.text=text;
        }
        public void RefreshLanguage()
        {
            if(!hasResult)return;
            Render(cachedSummary,cachedName,cachedWarnings);
        }
        private void Render(WorldSummary summary,string name,bool warnings)
        {
            host.GetComponentInChildren<Text>().text=summary==null?Remains.Content.GameLocalization.UiText("source.SelectedWorldPanel.7e33e928010bd887", "Начать в новом мире", "SelectedWorldPanel"):Remains.Content.GameLocalization.UiText("source.SelectedWorldPanel.70674b38b7c69c95", "Продолжить выбранный мир", "SelectedWorldPanel");
            description.text=summary==null?Remains.Content.GameLocalization.UiText("source.SelectedWorldPanel.8d3550878b807556", "Сохранённый мир не выбран.\n«Миры и сохранения»: выбор, имя и удаление.", "SelectedWorldPanel"):MainMenuPanel.DescribeWorld(name,summary.branchId??"",summary.revision,summary.snapshot);
            if(warnings)description.text+=Remains.Content.GameLocalization.UiText("source.SelectedWorldPanel.6af915edfe833488", "\nВ библиотеке есть предупреждения; подробности — в списке миров и журнале.", "SelectedWorldPanel");
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
                cachedSummary=result.Item1;cachedName=result.Item2;cachedWarnings=result.Item3;hasResult=true;
                Render(cachedSummary,cachedName,cachedWarnings);
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
