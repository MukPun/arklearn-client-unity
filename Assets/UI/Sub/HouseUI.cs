using System.Collections.Generic;
using Data.Item;
using Data.Player;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sub {
    public class HouseUI : UIBase {

        public Toggle ALL;
        public Toggle JI_CHU;
        public Toggle YANG_CHENG;
        public ItemIconComponent prefab;

        private List<ItemIconComponent> list = new List<ItemIconComponent>();
        private PlayerData data;
        
        public override void Init() {
            Color selectedColor = new Color(0.298f, 0.298f, 0.298f); // #4C4C4C
            Color normalColor = Color.white;

            // Text allText = ALL.transform.GetComponentInChildren<Text>();
            // Image allBg = ALL.transform.Find("Background")?.GetComponent<Image>();
            // if (allBg != null) allBg.color = ALL.isOn ? selectedColor : normalColor;
            // allText.color = ALL.isOn ? Color.white : Color.black;
            // ALL.onValueChanged.AddListener(value => {
            //     if (value) ShowItem(ItemType.JI_CHU,ItemType.YANG_CHENG);
            //     allText.DOColor(value ? Color.white : Color.black, 0.2f);
            //     if (allBg != null) allBg.DOColor(value ? selectedColor : normalColor, 0.2f);
            // });

            // Text ji_chuText = JI_CHU.transform.GetComponentInChildren<Text>();
            // Image ji_chuBg = JI_CHU.transform.Find("Background")?.GetComponent<Image>();
            // if (ji_chuBg != null) ji_chuBg.color = JI_CHU.isOn ? selectedColor : normalColor;
            // ji_chuText.color = JI_CHU.isOn ? Color.white : Color.black;
            // JI_CHU.onValueChanged.AddListener(value => {
            //     if (value) ShowItem(ItemType.JI_CHU);
            //     ji_chuText.DOColor(value ? Color.white : Color.black, 0.2f);
            //     if (ji_chuBg != null) ji_chuBg.DOColor(value ? selectedColor : normalColor, 0.2f);
            // });

            // Text yang_chengText = YANG_CHENG.transform.GetComponentInChildren<Text>();
            // Image yang_chengBg = YANG_CHENG.transform.Find("Background")?.GetComponent<Image>();
            // if (yang_chengBg != null) yang_chengBg.color = YANG_CHENG.isOn ? selectedColor : normalColor;
            // yang_chengText.color = YANG_CHENG.isOn ? Color.white : Color.black;
            // YANG_CHENG.onValueChanged.AddListener(value => {
            //     if (value) ShowItem(ItemType.YANG_CHENG);
            //     yang_chengText.DOColor(value ? Color.white : Color.black, 0.2f);
            //     if (yang_chengBg != null) yang_chengBg.DOColor(value ? selectedColor : normalColor, 0.2f);
            // });


            InitToggle(ALL, selectedColor, normalColor);
            InitToggle(JI_CHU, selectedColor, normalColor);
            InitToggle(YANG_CHENG, selectedColor, normalColor);
        }

        private void InitToggle(Toggle toggle, Color selectedColor, Color normalColor){
            Text toggle_Text = toggle.transform.GetComponentInChildren<Text>();
            Image toggle_Bg = toggle.transform.Find("Background")?.GetComponent<Image>();
            if (toggle_Bg != null) toggle_Bg.color = toggle.isOn ? selectedColor : normalColor;
            toggle_Text.color = toggle.isOn ? Color.white : Color.black;
            toggle.onValueChanged.AddListener(value => {
                ShowItem(GetCurrentItemTypes());
                toggle_Text.DOColor(value ? Color.white : Color.black, 0.2f);
                if (toggle_Bg != null) toggle_Bg.DOColor(value ? selectedColor : normalColor, 0.2f);
            });
        }

        private ItemType[] GetCurrentItemTypes() {
            if (ALL.isOn) {
                return new ItemType[] {ItemType.JI_CHU, ItemType.YANG_CHENG};
            }
            if (JI_CHU.isOn && YANG_CHENG.isOn) {
                return new ItemType[] {ItemType.JI_CHU, ItemType.YANG_CHENG};
            }
            if (JI_CHU.isOn) {
                return new ItemType[] {ItemType.JI_CHU};
            }
            if (YANG_CHENG.isOn) {
                return new ItemType[] {ItemType.YANG_CHENG};
            }
            return System.Array.Empty<ItemType>();
        }

        // 筛选物品
        private void ShowItem(params ItemType[] type) {
            List<ItemType> types = new List<ItemType>(type);
            foreach (ItemIconComponent itemIconComponent in list) {
                itemIconComponent.gameObject.SetActive(types.Contains(itemIconComponent.GetMeta().GetItemType()));
            }
        }

        public override void UpdateView() {
            data = PlayerManager.Inst().Get();
            List<ItemStack> dataList = data.GetItems();
            for (int i = 0; i < dataList.Count || i < list.Count; i++) {
                if (i < dataList.Count) {
                    ItemStack itemStack = dataList[i];
                    Debug.Log($"[UpdateView] i={i}, itemStack={itemStack.GetId()}, dataList.Count={dataList.Count}");
                    if (list.Count <= i) {
                        list.Add(Instantiate(prefab, prefab.transform.parent));
                    }
                    ItemIconComponent iic = list[i];
                    Debug.Log($"[UpdateView] itemStack={itemStack} itemStack.GetItemMeta()={itemStack.GetItemMeta()}");
                    iic.SetMeta(itemStack.GetItemMeta());
                    iic.SetAmount(itemStack.GetAmount());
                    iic.gameObject.name = itemStack.GetId().ToString();
                    iic.RemoveAllListeners();
                    iic.AddListener(() => ItemInfoUI.Show(itemStack));
                }
                list[i].gameObject.SetActive(i < dataList.Count);
            }
            ShowItem(GetCurrentItemTypes());
        }

        public override void Show() {
            base.Show();
            canvasGroup.alpha = 0;
            canvasGroup.DOFade(1,0.3f);
        }

        public override void Hide(bool destroy = false) {
            canvasGroup.DOFade(0,0.2f).OnComplete(() => {
                base.Hide(destroy);
            });
        }
    }
}