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

        //public JObject toGOODArtifact(bool includeLocation = true)
        //{
        //    JObject result = new JObject();

        //    if (set != null)
        //        result.Add("setKey", JToken.FromObject(set.Value.Key));
        //    result.Add("rarity", JToken.FromObject(rarity));
        //    if (main != null)
        //        result.Add("level", JToken.FromObject(main.Value.Level));
        //    if (piece != null)
        //        result.Add("slotKey", JToken.FromObject(piece.Value.StatKey));
        //    if (main != null)
        //        result.Add("mainStatKey", JToken.FromObject(main.Value.StatKey));
        //    if (subs != null)
        //    {
        //        JArray subsJArr = new JArray();
        //        foreach (ArtifactSubStatData sub in subs)
        //        {
        //            JObject subJObj = new JObject();
        //            subJObj.Add("key", JToken.FromObject(sub.StatKey));
        //            subJObj.Add("value", JToken.FromObject(sub.StatValue));
        //            subsJArr.Add(subJObj);
        //        }
        //        result.Add("substats", subsJArr);
        //    }
        //    if (includeLocation)
        //    {
        //        if (character != null)
        //            result.Add("location", JToken.FromObject(character.Value.Key));
        //        else
        //            result.Add("location", JToken.FromObject(""));
        //    }

        //    result.Add("lock", JToken.FromObject(locked));

        //    /*if (level != null && main != null && level.Item2 != main.Item4)
        //    {
        //        Console.WriteLine("Read level: " + level.Item2 + ", mainstat level: " + main.Item4);
        //        Console.WriteLine(this.ToString());
                
        //    }*/
        //    return result;
        //}

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

        //public static JObject listToGOODArtifacts(List<Artifact> items, int minLevel, int maxLevel, int minRarity, int maxRarity, bool exportAllEquipped, bool exportEquipStatus)
        //{
        //    JObject result = new JObject();
        //    JArray artifactJArr = new JArray();
        //    foreach (Artifact item in items)
        //    {
        //        bool add = false;
        //        if (exportAllEquipped && item.character != null)
        //        {
        //            add = true;
        //        }

        //        if (item.level.HasValue && item.level.Value.Key >= minLevel && item.level.Value.Key <= maxLevel
        //            && item.rarity >= minRarity && item.rarity <= maxRarity)
        //        {
        //            add = true;
        //        }

        //        if (add)
        //        {
        //            artifactJArr.Add(item.toGOODArtifact(exportEquipStatus));
        //        }
        //    }
        //    result.Add("artifacts", artifactJArr);
        //    return result;
        //}
    }
}
