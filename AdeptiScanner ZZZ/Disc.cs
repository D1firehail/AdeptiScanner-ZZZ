using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AdeptiScanner_ZZZ
{
    public class Disc
    {
        public DiscSetAndSlot? slot;
        public DiscLevelAndRarity? level;
        public DiscMainStat? main;
        public List<DiscSubStat> subs = new();

        public Disc()
        {

        }

        public override string ToString()
        {
            string text = "";

            text += "SetAndSlot: ";
            if (slot != null)
                text += slot + Environment.NewLine;
            else
                text += "Null-------" + Environment.NewLine;

            if (level != null)
                text += level + Environment.NewLine;
            else
                text += "Null-------" + Environment.NewLine;

            text += "Main: ";
            if (main != null)
                text += main + Environment.NewLine;
            else
                text += "Null-------" + Environment.NewLine;

            text += "Subs: ";
            text += subs.Count + Environment.NewLine;
            foreach (var sub in subs)
            {
                text += sub + Environment.NewLine;
            }

          
            return text;
        }

        public JObject toZOD(bool includeLocation = true)
        {
            JObject result = new JObject();

            if (slot != null)
            {
                result.Add("setKey", JToken.FromObject(slot.Value.Key));
                result.Add("slotKey", JToken.FromObject(slot.Value.Slot.ToString()));
            }
            if (level != null)
            {
                result.Add("level", JToken.FromObject(level.Value.Level));
                result.Add("rarity", JToken.FromObject(level.Value.Tier.ToString()));
            }
            if (main != null)
            {
                result.Add("mainStatKey", JToken.FromObject(main.Value.Key));
            }
            if (subs != null)
            {
                JArray subsJArr = new JArray();
                foreach (DiscSubStat sub in subs)
                {
                    JObject subJObj = new JObject
                    {
                        { "key", JToken.FromObject(sub.Key) },
                        { "upgrades", JToken.FromObject(sub.Upgrades) }
                    };
                    subsJArr.Add(subJObj);
                }
                result.Add("substats", subsJArr);
            }
            return result;
        }

        //public static Artifact fromGOODArtifact(JObject GOODArtifact)
        //{
        //    Artifact res = new Artifact();
        //    if (GOODArtifact.ContainsKey("rarity"))
        //    {
        //        res.rarity = GOODArtifact["rarity"].ToObject<int>();
        //    }
        //    else
        //    {
        //        return null;
        //    }
        //    Database rarityDb = Database.rarityData[res.rarity - 1];
        //    if (GOODArtifact.ContainsKey("setKey"))
        //    {
        //        string setKey = GOODArtifact["setKey"].ToObject<string>();
        //        for (int i = 0; i < rarityDb.Sets.Count; i++)
        //        {
        //            if (rarityDb.Sets[i].Key == setKey)
        //            {
        //                res.set = rarityDb.Sets[i];
        //                break;
        //            }
        //        }
        //    }
        //    if (GOODArtifact.ContainsKey("slotKey"))
        //    {
        //        string slotKey = GOODArtifact["slotKey"].ToObject<string>();
        //        for (int i = 0; i < Database.Pieces.Count; i++)
        //        {
        //            if (Database.Pieces[i].StatKey == slotKey)
        //            {
        //                res.piece = Database.Pieces[i];
        //                break;
        //            }
        //        }
        //    }
        //    if (GOODArtifact.ContainsKey("mainStatKey") && GOODArtifact.ContainsKey("level"))
        //    {
        //        string mainStatKey = GOODArtifact["mainStatKey"].ToObject<string>();
        //        int levelKey = GOODArtifact["level"].ToObject<int>();

        //        for (int i = 0; i < rarityDb.MainStats.Count; i++)
        //        {
        //            if (rarityDb.MainStats[i].StatKey == mainStatKey && rarityDb.MainStats[i].Level == levelKey)
        //            {
        //                res.main = rarityDb.MainStats[i];
        //                break;
        //            }
        //        }
        //        for (int i = 0; i < Database.ArtifactLevels.Count; i++)
        //        {
        //            if (Database.ArtifactLevels[i].Key == levelKey)
        //            {
        //                res.level = Database.ArtifactLevels[i];
        //                break;
        //            }
        //        }
        //    }
        //    if (GOODArtifact.ContainsKey("location"))
        //    {
        //        string locationKey = GOODArtifact["location"].ToObject<string>();

        //        for (int i = 0; i < Database.Characters.Count; i++)
        //        {
        //            if (Database.Characters[i].Key == locationKey)
        //            {
        //                res.character = Database.Characters[i];
        //                break;
        //            }
        //        }
        //    }
        //    if (GOODArtifact.ContainsKey("lock"))
        //    {
        //        res.locked = GOODArtifact["lock"].ToObject<bool>();
        //    }
        //    if (GOODArtifact.ContainsKey("substats"))
        //    {
        //        JArray substats = GOODArtifact["substats"].ToObject<JArray>();
        //        res.subs = new List<ArtifactSubStatData>();
        //        foreach (JObject sub in substats)
        //        {
        //            if (sub.ContainsKey("key") && sub.ContainsKey("value"))
        //            {
        //                string statKey = sub["key"].ToObject<string>();
        //                double statVal = sub["value"].ToObject<double>();
        //                for (int i = 0; i < rarityDb.Substats.Count; i++)
        //                {
        //                    if (rarityDb.Substats[i].StatKey == statKey && rarityDb.Substats[i].StatValue - statVal < 0.099)
        //                    {
        //                        res.subs.Add(rarityDb.Substats[i]);
        //                        break;
        //                    }
        //                }
        //            }
        //        }
        //    }

        //    if (Database.artifactInvalid(res))
        //    {
        //        ScannerForm.INSTANCE.AppendStatusText("Failed to parse artifact: " + GOODArtifact.ToString(Newtonsoft.Json.Formatting.None) + Environment.NewLine, false);
        //        return null;
        //    }
        //    else
        //    {
        //        return res;
        //    }
        //}

        public static JObject listToZOD(List<Disc> items, int minLevel, int maxLevel, int minRarity, int maxRarity)
        {
            JObject result = new JObject();
            JArray discJArr = new JArray();
            foreach (Disc item in items)
            {
                bool add = false;

                if (item.level.HasValue && item.level.Value.Level >= minLevel && item.level.Value.Level <= maxLevel
                    && (int)item.level.Value.Tier >= minRarity && (int)item.level.Value.Tier <= maxRarity)
                {
                    add = true;
                }

                if (add)
                {
                    discJArr.Add(item.toZOD());
                }
            }
            result.Add("discs", discJArr);
            return result;
        }
    }
}
