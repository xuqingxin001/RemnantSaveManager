﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace RemnantSaveManager
{
    public class RemnantItem : IEquatable<Object>, IComparable
    {
        public enum RemnantItemMode
        {
            Normal,
            Hardcore,
            Survival
        }

        private string itemKey;
        private string itemType;
        private string itemName;
        private string itemAltName;
        private string ItemKey { 
            get { return itemKey; } 
            set 
            {
                try
                {
                    itemKey = value;
                    itemType = "未分类";
                    itemName = itemKey.Substring(itemKey.LastIndexOf('/') + 1);
                    if (itemKey.Contains("/Weapons/"))
                    {
                        itemType = "武器";
                        if (itemName.Contains("Mod_")) itemName = itemName.Replace("/Weapons/", "/Mods/");
                    }
                    if (itemKey.Contains("/Armor/") || itemKey.Contains("TwistedMask"))
                    {
                        itemType = "护甲";
                        if (itemKey.Contains("TwistedMask"))
                        {
                            itemName = "TwistedMask (Head)";
                        }
                        else
                        {
                            string[] parts = itemName.Split('_');
                            itemName = parts[2] + " (" + parts[1] + ")";
                        }
                    }
                    if (itemKey.Contains("/Trinkets/") || itemKey.Contains("BrabusPocketWatch")) itemType = "饰品";
                    if (itemKey.Contains("/Mods/")) itemType = "配件";
                    if (itemKey.Contains("/Traits/")) itemType = "特性";
                    if (itemKey.Contains("/Emotes/")) itemType = "动作";

                    itemName = itemName.Replace("Weapon_", "").Replace("Root_", "").Replace("Wasteland_", "").Replace("Swamp_", "").Replace("Pan_", "").Replace("Atoll_", "").Replace("Mod_", "").Replace("Trinket_", "").Replace("Trait_", "").Replace("Quest_", "").Replace("Emote_", "").Replace("Rural_", "").Replace("Snow_", "");
                    if (!itemType.Equals("Armor"))
                    {
                        itemName = Regex.Replace(itemName, "([a-z])([A-Z])", "$1 $2");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("处理物品名称时出错: " + ex.Message);
                    itemName = value;
                }
            } 
        }

        public string ItemName 
        { 
            get 
            {
                if (itemAltName != null) return itemAltName;
                return itemName; 
            } 
        }
        public string ItemType { get { return itemType; } }
        public RemnantItemMode ItemMode { get; set; }
        public string ItemNotes { get; set; }
        public string ItemAltName { get { return itemAltName; } set { itemAltName = value; } }

        public RemnantItem(string key)
        {
            this.ItemKey = key;
            this.ItemMode = RemnantItemMode.Normal;
            this.ItemNotes = "";
        }

        public RemnantItem(string key, RemnantItemMode mode)
        {
            this.ItemKey = key;
            this.ItemMode = mode;
            this.ItemNotes = "";
        }

        public string GetKey()
        {
            return this.itemKey;
        }

        public override string ToString()
        {
            return itemType + ": " + ItemName;
        }

        public override bool Equals(Object obj)
        {
            //Check for null and compare run-time types.
            if ((obj == null))
            {
                return false;
            }
            else if (!this.GetType().Equals(obj.GetType()))
            {
                if (obj.GetType() == typeof(string))
                {
                    return (this.GetKey().Equals(obj));
                }
                return false;
            }
            else
            {
                RemnantItem rItem = (RemnantItem)obj;
                return (this.GetKey().Equals(rItem.GetKey()) && this.ItemMode == rItem.ItemMode);
            }
        }

        public override int GetHashCode()
        {
            return this.itemKey.GetHashCode();
        }

        public int CompareTo(Object obj)
        {
            //Check for null and compare run-time types.
            if ((obj == null))
            {
                return 1;
            }
            else if (!this.GetType().Equals(obj.GetType()))
            {
                if (obj.GetType() == typeof(string))
                {
                    return (this.GetKey().CompareTo(obj));
                }
                return this.ToString().CompareTo(obj.ToString());
            }
            else
            {
                RemnantItem rItem = (RemnantItem)obj;
                if (this.ItemMode != rItem.ItemMode)
                {
                    return this.ItemMode.CompareTo(rItem.ItemMode);
                }
                return this.itemKey.CompareTo(rItem.GetKey());
            }
        }
    }
}
