using System.Collections.Generic;
using System.Text.RegularExpressions;
using Settings;
using Tools;
using UnityEngine;

namespace Data.Char {
    /**
     * 角色元数据控制器
     */
    public class CharManager : Single<CharManager> {
        // rarity映射字典，0->1_3 1->1_3 2->1_3  3->4 4->6 5->6
        private static Dictionary<int, string> rarityBackGroundMapping = new Dictionary<int, string>
        {
            { 0, "1_3" },
            { 1, "1_3" },
            { 2, "1_3" },
            { 3, "4" },
            { 4, "6" },
            { 5, "7" }
        };

        private static Dictionary<int, string> rarityMapping = new Dictionary<int, string>
        {
            { 0, "1_3" },
            { 1, "1_3" },
            { 2, "1_3" },  
            { 3, "4"},
            { 4, "4" },
            { 5, "6" }
        };

        // 元数据
        private Dictionary<string, CharMeta> metaDictionary = new Dictionary<string, CharMeta>();
        // 阵营
        private Dictionary<string, Sprite> campImage = new Dictionary<string,Sprite>();
        // 卡图
        private Dictionary<string, Sprite> cardGroundImage = new Dictionary<string,Sprite>();
        // 精英图
        private Dictionary<string, Sprite> eliteImage = new Dictionary<string,Sprite>();
        // 职业small图
        private Dictionary<string, Sprite> professionImage = new Dictionary<string,Sprite>();
        // 职业small图
        private Dictionary<string, Sprite> professionSmallImage = new Dictionary<string,Sprite>();
        // 职业small图
        private Dictionary<string, Sprite> starImage = new Dictionary<string,Sprite>();
        public enum CardGroundImageType
        {
            BackGround,
            LowerHub,
            UpperHub,
            RarityLight,
            Star
        }
        public CharManager() {
            foreach (Sprite sprite in Asset.LoadAll<Sprite>("Sprite/Char","Camp")) {
                campImage.Add(sprite.name, sprite);
            }

            foreach (Sprite sprite in Asset.LoadAll<Sprite>("Sprite/Char/CardGround","backGround")) {
                cardGroundImage.Add(sprite.name, sprite);
            }
            foreach (Sprite sprite in Asset.LoadAll<Sprite>("Sprite/Char/CardGround","lowerhub")) {
                cardGroundImage.Add(sprite.name, sprite);
            }
            foreach (Sprite sprite in Asset.LoadAll<Sprite>("Sprite/Char/CardGround","rartylight")) {
                cardGroundImage.Add(sprite.name, sprite);
            }
            foreach (Sprite sprite in Asset.LoadAll<Sprite>("Sprite/Char/CardGround","upperhub")) {
                cardGroundImage.Add(sprite.name, sprite);
            }
            foreach (Sprite sprite in Asset.LoadAll<Sprite>("Sprite/Char/CardGround","start")) {
                cardGroundImage.Add(sprite.name, sprite);
            }

            foreach (Sprite sprite in Asset.LoadAll<Sprite>("Sprite/Char","Elite")) {
                eliteImage.Add(sprite.name, sprite);
            }
            foreach (Sprite sprite in Asset.LoadAll<Sprite>("Sprite/Char","Profession")) {
                professionImage.Add(sprite.name, sprite);
            }
            foreach (Sprite sprite in Asset.LoadAll<Sprite>("Sprite/Char","ProfessionSmall")) {
                professionSmallImage.Add(sprite.name, sprite);
            }
            foreach (Sprite sprite in Asset.LoadAll<Sprite>("Sprite/Char","Star")) {
                starImage.Add(sprite.name, sprite);
            }
        }

        // 获取对象
        public CharMeta GetMeta(string id) => metaDictionary.ContainsKey(id) ? metaDictionary[id] : LoadMeta(id);

        // 加载LoadMeta数据
        private CharMeta LoadMeta(string id) {
            CharMeta meta = Asset.Load<CharMeta>(GameSettings.CHAR_META_PATH, id);
            meta.SetId(id);
            metaDictionary.Add(id, meta);
            return meta;
        }
        
        public Sprite GetCampImage(string str) {
            return campImage[str];
        }
        
        public Sprite GetCardGroundImage(string str) {
            return cardGroundImage[str];
        }
        public Sprite GetCardGroundImage(int rarity, CardGroundImageType str) {
            if (str == CardGroundImageType.BackGround)
            {   
                // 根据rarity的映射关系，获取真正的realRarity
                // 0->1_3 1->1_3 2->1_3  3->4 4->6 5->7
                string realRarity = "";
                if (rarityBackGroundMapping.ContainsKey(rarity))
                    realRarity = rarityBackGroundMapping[rarity];
                else
                    realRarity = rarity.ToString();
                    
                return cardGroundImage["sprite_char_background" + realRarity];
            }
            else if (str == CardGroundImageType.LowerHub)
            {
                string realRarity = "";
                if (rarityMapping.ContainsKey(rarity))
                    realRarity = rarityMapping[rarity];
                else
                    realRarity = rarity.ToString();
                return cardGroundImage["sprite_lowerhub" + realRarity + "shadowoff"];
            }
            else if (str == CardGroundImageType.RarityLight)
            {
                string realRarity = (rarity + 1).ToString();
                return cardGroundImage["sprite_rartylight_" + realRarity];
            }
            else if (str == CardGroundImageType.UpperHub)
            {
                string realRarity = (rarity + 1).ToString();
                return cardGroundImage["sprite_upperhub_" + realRarity];
            }
            else if (str == CardGroundImageType.Star)
            {
                string realRarity = (rarity + 1).ToString();
                return cardGroundImage["sprite_star_" + realRarity];
            }

            return null;
        }

        public Sprite GetCharEliteImage(string str) {
            return eliteImage[str];
        }
        
        public Sprite GetProfessionImage(CharProfession profession) {
            return professionImage[profession.ToString()];
        }
        
        public Sprite GetProfessionSmallImage(CharProfession profession) {
            return professionSmallImage[profession.ToString()];
        }
        
        public Sprite GetStarImage(string str) {
            return starImage[str];
        }
    }
}