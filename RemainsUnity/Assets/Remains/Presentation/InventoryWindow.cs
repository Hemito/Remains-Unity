using System;
using UnityEngine;
using UnityEngine.UI;

namespace Remains.Presentation
{
    // The shell retains its native aspect; each page occupies the screen opening.
    public sealed class InventoryWindow
    {
        private readonly GameObject personal,shared;
        private readonly Button personalTab,sharedTab;
        public RectTransform PersonalTab=>personalTab.GetComponent<RectTransform>();
        public RectTransform SharedTab=>sharedTab.GetComponent<RectTransform>();
        public InventoryWindow(InventoryWindowView view,Action close,Action pageSound)
        {
            view.Validate();personal=view.Personal;shared=view.Shared;personalTab=view.PersonalTab;sharedTab=view.SharedTab;
            personalTab.onClick.AddListener(()=>{Select(false);pageSound();});
            sharedTab.onClick.AddListener(()=>{Select(true);pageSound();});
            view.Close.onClick.AddListener(()=>close());Select(false);
        }
        private void Select(bool container)
        {
            personal.SetActive(!container);shared.SetActive(container);
            OriginalButtonStyle.SetSelected(personalTab,!container);
            OriginalButtonStyle.SetSelected(sharedTab,container);
        }
    }
}

