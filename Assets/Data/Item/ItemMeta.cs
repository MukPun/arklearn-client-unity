using System;
using UnityEngine;

namespace Data.Item {
    // 物品元数据
    [Serializable]
    [CreateAssetMenu(fileName = "ItemID", menuName = "ArkNight/ItemMeta")]
    public class ItemMeta : ScriptableObject {
        // ID
        [Header("数字ID")]
        [field: SerializeField] public int id { get; private set; }
        // 名字
        [Header("名字")]
        [field: SerializeField] public string name { get; private set; }
        // 图标
        [Header("图标")]
        [field: SerializeField] public Sprite icon { get; private set; }
        // 稀有度
        [Header("稀有度")]
        [field: SerializeField] public int rarity { get; private set; }
        // 类型
        [Header("类型")]
        [field: SerializeField] public ItemType type { get; private set; }
        // 用途
        [Header("用途")]
        [field: SerializeField] public string useInfo { get; private set; }
        // 描述
        [Header("描述")]
        [field: SerializeField] public string description { get; private set; }
        // 获得方式
        [Header("获得方式")]
        [field: SerializeField] public string waysObtain { get; private set; }

        public int GetID() => id;

        public string GetName() => name;

        public Sprite GetIcon() => icon;

        public int GetRarity() => rarity;

        public ItemType GetItemType() => type;

        public string GetUseInfo() => useInfo;

        public string GetDescription() => description;

        public string GetWaysObtain() => waysObtain;
    }
}