// #if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Data.Item;
using Newtonsoft.Json;

namespace EditorTools {
    public class ItemMetaImporter : EditorWindow {
        private string jsonPath = "Assets/Resources/GameData/excel/item_table.json";
        private string outputPath = "Assets/Resources/Meta/Item";

        [MenuItem("Tools/导入道具元数据")]
        public static void ShowWindow() {
            GetWindow<ItemMetaImporter>("道具元数据导入");
        }

        private void OnGUI() {
            GUILayout.Label("道具元数据批量导入工具", EditorStyles.boldLabel);
            GUILayout.Space(10);

            jsonPath = EditorGUILayout.TextField("JSON 路径", jsonPath);
            outputPath = EditorGUILayout.TextField("输出路径", outputPath);

            GUILayout.Space(10);

            if (GUILayout.Button("开始导入", GUILayout.Height(30))) {
                Import();
            }
        }

        private void Import() {
            if (!File.Exists(jsonPath)) {
                Debug.LogError($"JSON 文件不存在: {jsonPath}");
                return;
            }

            // 确保输出目录存在
            if (!Directory.Exists(outputPath)) {
                Directory.CreateDirectory(outputPath);
            }

            string jsonContent = File.ReadAllText(jsonPath);
            var wrapper = JsonConvert.DeserializeObject<ItemTableWrapper>(jsonContent);

            if (wrapper == null || wrapper.items == null) {
                Debug.LogError($"JSON 解析失败或数据结构不匹配");
                return;
            }

            int successCount = 0;
            int failCount = 0;

            foreach (var kvp in wrapper.items) {
                try {
                    string itemId = kvp.Key;
                    ItemJsonData data = kvp.Value;

                    // 跳过无效ID（如包含字母的ID）
                    if (!int.TryParse(itemId, out int numericId)) {
                        Debug.LogWarning($"跳过非数字ID: {itemId}");
                        failCount++;
                        continue;
                    }

                    // 创建 ItemMeta
                    ItemMeta meta = ScriptableObject.CreateInstance<ItemMeta>();

                    // 设置数值
                    SetMetaValues(meta, numericId, data);

                    // 保存为 Asset
                    string assetPath = $"{outputPath}/{numericId}.asset";
                    AssetDatabase.CreateAsset(meta, assetPath);
                    successCount++;
                } catch (Exception e) {
                    Debug.LogError($"导入道具 {kvp.Key} 失败: {e.Message}");
                    failCount++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"导入完成！成功: {successCount}, 失败: {failCount}");
        }

        private void SetMetaValues(ItemMeta meta, int id, ItemJsonData data) {
            // 使用 Unity SerializedObject 设置值，更可靠
            var serializedObject = new SerializedObject(meta);
            var iterator = serializedObject.GetIterator();

            while (iterator.NextVisible(true)) {
                switch (iterator.propertyPath) {
                    case "<id>k__BackingField":
                        iterator.intValue = id;
                        break;
                    case "<name>k__BackingField":
                        iterator.stringValue = data.name;
                        break;
                    case "<icon>k__BackingField":
                        iterator.objectReferenceValue = GetSpriteByIconId(data.iconId);
                        break;
                    case "<rarity>k__BackingField":
                        iterator.intValue = data.rarity;
                        break;
                    case "<type>k__BackingField":
                        iterator.enumValueIndex = (int)GetItemTypeByData(data.itemType);
                        break;
                    case "<useInfo>k__BackingField":
                        iterator.stringValue = data.usage;
                        break;
                    case "<description>k__BackingField":
                        iterator.stringValue = data.description;
                        break;
                    case "<waysObtain>k__BackingField":
                        iterator.stringValue = data.obtainApproach;
                        break;
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private Sprite GetSpriteByIconId(string iconId){
            if (string.IsNullOrEmpty(iconId)) return null;
            // Sprite 目录：Assets/Resources/Sprite/Item/
            // Sprite 路径格式：Sprite/Item/{iconId}
            string spritePath = $"Sprite/Item/{iconId}";
            Sprite sprite = Resources.Load<Sprite>(spritePath);
            if (sprite == null) {
                Debug.LogWarning($"未找到 Sprite: {spritePath}");
                return null;
            }
            return sprite;
        }

        private ItemType GetItemTypeByData(string itemType)
        {
            if (itemType == "MATERIAL")
            {
                return ItemType.YANG_CHENG;
            }
            return ItemType.JI_CHU;
        }

        [Serializable]
        private class ItemTableWrapper {
            [JsonProperty("items")]
            public Dictionary<string, ItemJsonData> items;
        }

        [Serializable]
        private class ItemJsonData {
            [JsonProperty("itemId")]
            public string itemId;
            [JsonProperty("name")]
            public string name;
            [JsonProperty("description")]
            public string description;
            [JsonProperty("rarity")]
            public int rarity;
            [JsonProperty("iconId")]
            public string iconId;
            [JsonProperty("usage")]
            public string usage;
            [JsonProperty("obtainApproach")]
            public string obtainApproach;
            [JsonProperty("itemType")]
            public string itemType;
        }
    }
}
// #endif