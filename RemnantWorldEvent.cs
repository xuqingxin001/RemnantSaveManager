using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Xml;
using System.Net;
using System.Diagnostics;

namespace RemnantSaveManager
{
    public class RemnantWorldEvent
    {
        private string eventKey;
        private List<RemnantItem> mItems;
        public string Location { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public string MissingItems {
            get {
                return string.Join("\n", mItems);
            }
        }
        public string PossibleItems
        {
            get
            {
                return string.Join("\n", this.getPossibleItems());
            }
        }
        public enum ProcessMode { Campaign, Adventure, Subject2923 };

        public string getKey()
        {
            return eventKey;
        }

        public void setKey(string key)
        {
            this.eventKey = key;
        }

        public List<RemnantItem> getPossibleItems()
        {
            List<RemnantItem> items = new List<RemnantItem>();
            if (GameInfo.EventItem.ContainsKey(this.eventKey))
            {
                items = new List<RemnantItem>(GameInfo.EventItem[this.eventKey]);
            }
            return items;
        }

        public void setMissingItems(RemnantCharacter charData)
        {
            List<RemnantItem> missingItems = new List<RemnantItem>();
            List<RemnantItem> possibleItems = this.getPossibleItems();
            foreach (RemnantItem item in possibleItems)
            {
                if (!charData.Inventory.Contains(item.GetKey()))
                {
                    missingItems.Add(item);
                }
            }
            mItems = missingItems;

            if (possibleItems.Count == 0 && !GameInfo.Events.ContainsKey(this.getKey()) && !this.getKey().Equals("TraitBook") && !this.getKey().Equals("Simulacrum"))
            {
                RemnantItem ri = new RemnantItem("/UnknownPotentialLoot");
                mItems.Add(ri);
            }
        }

        public override string ToString()
        {
            return this.Name;
        }

        //credit to /u/hzla00 for original javascript implementation
        static public void ProcessEvents(RemnantCharacter character, string eventsText, ProcessMode mode)
        {
            Dictionary<string, Dictionary<string, string>> zones = new Dictionary<string, Dictionary<string, string>>();
            Dictionary<string, List<RemnantWorldEvent>> zoneEvents = new Dictionary<string, List<RemnantWorldEvent>>();
            List<RemnantWorldEvent> churchEvents = new List<RemnantWorldEvent>();
            foreach (string z in GameInfo.Zones.Keys)
            {
                zones.Add(z, new Dictionary<string, string>());
                zoneEvents.Add(z, new List<RemnantWorldEvent>());
            }

            string zone = null;
            string currentMainLocation = "Fairview";
            string currentSublocation = null;

            string eventName = null;
            MatchCollection matches = Regex.Matches(eventsText, "(?:/[a-zA-Z0-9_]+){3}/(([a-zA-Z0-9]+_[a-zA-Z0-9]+_[a-zA-Z0-9_]+)|Quest_Church)");
            foreach (Match match in matches)
            {
                string eventType = null;
                string lastEventname = eventName;
                eventName = null;

                string textLine = match.Value;
                try
                {
                    if (currentSublocation != null)
                    {
                        //Some world bosses don't have a preceding dungeon; subsequent items therefore spawn in the overworld
                        if (currentSublocation.Equals("TheRavager'sHaunt") || currentSublocation.Equals("TheTempestCourt")) currentSublocation = null;
                    }
                    zone = getZone(textLine);
                    
                    eventType = getEventType(textLine);
                    
                    if (textLine.Contains("Overworld_Zone") || textLine.Contains("_Overworld_"))
                    {
                        //process overworld zone marker
                        currentMainLocation = textLine.Split('/')[4].Split('_')[1] + " " + textLine.Split('/')[4].Split('_')[2] + " " + textLine.Split('/')[4].Split('_')[3];
                        if (GameInfo.MainLocations.ContainsKey(currentMainLocation))
                        {
                            currentMainLocation = GameInfo.MainLocations[currentMainLocation];
                        }
                        else
                        {
                            currentMainLocation = null;
                        }
                        continue;
                    }
                    else if (textLine.Contains("Quest_Church"))
                    {
                        //process Root Mother event
                        currentMainLocation = "教堂车站";
                        eventName = "RootMother";
                        currentSublocation = "先驱者教堂";
                    }
                    else if (eventType != null)
                    {
                        //process other events, if they're recognized by getEventType
                        eventName = textLine.Split('/')[4].Split('_')[2];
                        if (textLine.Contains("OverworldPOI"))
                        {
                            currentSublocation = null;
                        }
                        else if (!textLine.Contains("Quest_Event"))
                        {
                            if (GameInfo.SubLocations.ContainsKey(eventName))
                            {
                                currentSublocation = GameInfo.SubLocations[eventName];
                            }
                            else
                            {
                                currentSublocation = null;
                            }
                        }
                        if ("Chapel Station".Equals(currentMainLocation))
                        {
                            if (textLine.Contains("Quest_Boss"))
                            {
                                currentMainLocation = "西苑";
                            } else
                            {
                                currentSublocation = null;
                            }
                        }
                    }

                    if (mode == ProcessMode.Adventure) currentMainLocation = null;

                    if (eventName != lastEventname)
                    {
                        RemnantWorldEvent se = new RemnantWorldEvent();
                        // Replacements
                        if (eventName != null)
                        {
                            se.setKey(eventName);
                            if (GameInfo.Events.ContainsKey(eventName)) {
                                se.Name = GameInfo.Events[eventName];
                            } else
                            {
                                se.Name = eventName;
                            }
                            se.Name = Regex.Replace(se.Name, "([a-z])([A-Z])", "$1 $2");
                        }

                        if (zone != null && eventType != null && eventName != null)
                        {
                            if (!zones[zone].ContainsKey(eventType))
                            {
                                zones[zone].Add(eventType, "");
                            }
                            if (!zones[zone][eventType].Contains(eventName))
                            {
                                zones[zone][eventType] += ", " + eventName;
                                List<string> locationList = new List<string>();
                                string zonelabel = zone;
                                if (GameInfo.Zones.ContainsKey(zone))
                                {
                                    zonelabel = GameInfo.Zones[zone];
                                }
                                locationList.Add(zonelabel);
                                if (currentMainLocation != null) locationList.Add(Regex.Replace(currentMainLocation, "([a-z])([A-Z])", "$1 $2"));
                                if (currentSublocation != null) locationList.Add(Regex.Replace(currentSublocation, "([a-z])([A-Z])", "$1 $2"));
                                se.Location = string.Join(": ", locationList);
                                se.Type = eventType;
                                se.setMissingItems(character);
                                if (!"Chapel Station".Equals(currentMainLocation)) {
                                    zoneEvents[zone].Add(se);
                                }
                                else
                                {
                                    churchEvents.Insert(0, se);
                                }

                                // rings drop with the Cryptolith on Rhom
                                if (eventName.Equals("Cryptolith") && zone.Equals("Rhom"))
                                {
                                    RemnantWorldEvent ringdrop = new RemnantWorldEvent();
                                    ringdrop.Location = zone;
                                    ringdrop.setKey("SoulLink");
                                    ringdrop.Name = "灵魂连接";
                                    ringdrop.Type = "世界掉落物";
                                    ringdrop.setMissingItems(character);
                                    zoneEvents[zone].Add(ringdrop);
                                }
                                // beetle always spawns in Strange Pass
                                else if (eventName.Equals("BrainBug"))
                                {
                                    RemnantWorldEvent beetle = new RemnantWorldEvent();
                                    beetle.Location = se.Location;
                                    beetle.setKey("Sketterling");
                                    beetle.Name = "圣甲虫";
                                    beetle.Type = "甲虫战利品";
                                    beetle.setMissingItems(character);
                                    zoneEvents[zone].Add(beetle);
                                }
                                else if (eventName.Equals("BarnSiege") || eventName.Equals("Homestead"))
                                {
                                    RemnantWorldEvent wardPrime = new RemnantWorldEvent();
                                    wardPrime.setKey("WardPrime");
                                    wardPrime.Name = "主实验区";
                                    wardPrime.Location = "地球: 主实验区";
                                    wardPrime.Type = "主线任务";
                                    wardPrime.setMissingItems(character);
                                    zoneEvents[zone].Add(wardPrime);
                                }
                            }
                        }

                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error parsing save event:");
                    Console.WriteLine("\tLine: " + textLine);
                    Console.WriteLine("\tError: " + ex.ToString());
                }
            }

            List<RemnantWorldEvent> orderedEvents = new List<RemnantWorldEvent>();

            bool churchAdded = false;
            bool queenAdded = false;
            bool navunAdded = false;
            RemnantWorldEvent ward13 = new RemnantWorldEvent();
            RemnantWorldEvent hideout = new RemnantWorldEvent();
            RemnantWorldEvent undying = new RemnantWorldEvent();
            RemnantWorldEvent queen = new RemnantWorldEvent();
            RemnantWorldEvent navun = new RemnantWorldEvent();
            RemnantWorldEvent ward17 = new RemnantWorldEvent();
            if (mode == ProcessMode.Campaign)
            {
                ward13.setKey("Ward13");
                ward13.Name = "13号实验区";
                ward13.Location = "地球: 13号实验区";
                ward13.Type = "家";
                ward13.setMissingItems(character);
                if (ward13.MissingItems.Length > 0) orderedEvents.Add(ward13);

                hideout.setKey("FoundersHideout");
                hideout.Name = "创始人的藏身处";
                hideout.Location = "地球: 美景镇";
                hideout.Type = "兴趣点";
                hideout.setMissingItems(character);
                if (hideout.MissingItems.Length > 0) orderedEvents.Add(hideout);

                undying.setKey("UndyingKing");
                undying.Name = "不灭之王";
                undying.Location = "洛姆: 不灭王座";
                undying.Type = "世界首领";
                undying.setMissingItems(character);

                queen.Name = "伊斯卡尔女王";
                queen.setKey("IskalQueen");
                queen.Location = "克尔苏斯: 迷雾沼泽";
                queen.Type = "兴趣点";
                queen.setMissingItems(character);

                navun.Name = "与叛军战斗";
                navun.setKey("SlaveRevolt");
                navun.Location = "耶莎: 不朽者神祠";
                navun.Type = "围攻";
                navun.setMissingItems(character);

                ward17.setKey("Ward17");
                ward17.Name = "梦游者";
                ward17.Location = "地球: 17号实验区";
                ward17.Type = "世界首领";
                ward17.setMissingItems(character);
            }

            for (int i = 0; i < zoneEvents["Earth"].Count; i++)
            {
                //if (mode == ProcessMode.Subject2923) Console.WriteLine(zoneEvents["Earth"][i].eventKey);
                if (mode == ProcessMode.Campaign && !churchAdded && zoneEvents["Earth"][i].Location.Contains("Westcourt"))
                {
                    foreach (RemnantWorldEvent rwe in churchEvents)
                    {
                        orderedEvents.Add(rwe);
                    }
                    churchAdded = true;
                }
                orderedEvents.Add(zoneEvents["Earth"][i]);
            }
            for (int i = 0; i < zoneEvents["Rhom"].Count; i++)
            {
                orderedEvents.Add(zoneEvents["Rhom"][i]);
            }
            if (mode == ProcessMode.Campaign && undying.MissingItems.Length > 0) orderedEvents.Add(undying);
            for (int i = 0; i < zoneEvents["Corsus"].Count; i++)
            {
                if (mode == ProcessMode.Campaign && !queenAdded && zoneEvents["Corsus"][i].Location.Contains("The Mist Fen"))
                {
                    if (queen.MissingItems.Length > 0) orderedEvents.Add(queen);
                    queenAdded = true;
                }
                orderedEvents.Add(zoneEvents["Corsus"][i]);
            }
            for (int i = 0; i < zoneEvents["Yaesha"].Count; i++)
            {
                if (mode == ProcessMode.Campaign && !navunAdded && zoneEvents["Yaesha"][i].Location.Contains("The Scalding Glade"))
                {
                    if (navun.MissingItems.Length > 0) orderedEvents.Add(navun);
                    navunAdded = true;
                }
                orderedEvents.Add(zoneEvents["Yaesha"][i]);
            }
            for (int i = 0; i < zoneEvents["Reisum"].Count; i++)
            {
                /*if (mode == ProcessMode.Campaign && !navunAdded && zoneEvents["Yaesha"][i].Location.Contains("The Scalding Glade"))
                {
                    if (navun.MissingItems.Length > 0) orderedEvents.Add(navun);
                    navunAdded = true;
                }*/
                orderedEvents.Add(zoneEvents["Reisum"][i]);
            }

            if (mode == ProcessMode.Campaign)
            {
                if (ward17.MissingItems.Length > 0) orderedEvents.Add(ward17);
            }

            for (int i = 0; i < orderedEvents.Count; i++)
            {
                if (mode == ProcessMode.Campaign || mode == ProcessMode.Subject2923)
                {
                    character.CampaignEvents.Add(orderedEvents[i]);
                }
                else
                {
                    character.AdventureEvents.Add(orderedEvents[i]);
                }
            }

            if (mode == ProcessMode.Subject2923)
            {
                ward17.setKey("Ward17Root");
                ward17.Name = "哈斯加德";
                ward17.Location = "地球: 17号实验区 (根蔓次元)";
                ward17.Type = "世界首领";
                ward17.setMissingItems(character);
                character.CampaignEvents.Add(ward17);
            }
        }

        static private string getZone(string textLine)
        {
            string zone = null;
            if (textLine.Contains("World_City") || textLine.Contains("Quest_Church") || textLine.Contains("World_Rural"))
            {
                zone = "Earth";
            }
            else if (textLine.Contains("World_Wasteland"))
            {
                zone = "Rhom";
            }
            else if (textLine.Contains("World_Jungle"))
            {
                zone = "Yaesha";
            }
            else if (textLine.Contains("World_Swamp"))
            {
                zone = "Corsus";
            }
            else if (textLine.Contains("World_Snow") || textLine.Contains("Campaign_Clementine"))
            {
                zone = "Reisum";
            }
            return zone;
        }

        static private string getEventType(string textLine)
        {
            string eventType = null;
            if (textLine.Contains("SmallD"))
            {
                eventType = "支线地下城";
            }
            else if (textLine.Contains("Quest_Boss"))
            {
                eventType = "世界首领";
            }
            else if (textLine.Contains("Siege")|| textLine.Contains("Quest_Church"))
            {
                eventType = "围攻";
            }
            else if (textLine.Contains("Mini"))
            {
                eventType = "小首领";
            }
            else if (textLine.Contains("Quest_Event"))
            {
                if (textLine.Contains("Nexus"))
                {
                    eventType = "围攻";
                }
                else if (textLine.Contains("Sketterling"))
                {
                    eventType = "甲虫战利品";
                }
                else
                {
                    eventType = "世界掉落物";
                }
            }
            else if (textLine.Contains("OverworldPOI") || textLine.Contains("OverWorldPOI") || textLine.Contains("OverworlPOI"))
            {
                eventType = "兴趣点";
            }
            return eventType;
        }
    }
}