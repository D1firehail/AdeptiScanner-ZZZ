using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AdeptiScanner_ZZZ
{
    #region IParsable stuff
    public interface IParsableData
    {
        string GetPlainText();
    }

    public readonly record struct SimpleParsable(string Text) : IParsableData
    {
        public string GetPlainText() => Text;
    }

    public readonly record struct PieceData(string Text, string StatKey) : IParsableData
    {
        public string GetPlainText() => Text;
    }

    public readonly record struct CharacterNameData(string Text, string Key) : IParsableData
    {
        public string GetPlainText() => Text;
    }

    public readonly record struct ArtifactLevelData(string Text, int Key) : IParsableData
    {
        public string GetPlainText() => Text;
    }

    public readonly record struct ArtifactMainStatData(string Text, string StatKey, double StatValue, int Level) : IParsableData
    {
        public string GetPlainText() => Text;
    }

    public readonly record struct ArtifactSubStatData(string Text, string StatKey, double StatValue) : IParsableData
    {
        public string GetPlainText() => Text;
    }

    public readonly record struct ArtifactSetData(string Text, string Key) : IParsableData
    {
        public string GetPlainText() => Text;
    }

    public readonly record struct WeaponNameData(string Text, string Key, int Rarity) : IParsableData
    {
        public string GetPlainText() => Text; // weapons currently only parse the GOOD name and assumes it's close enough to the english name
    }

    public readonly record struct WeaponLevelAndAscensionData(string BaseAtk, int Level, int Ascension) : IParsableData
    {
        public string GetPlainText() => BaseAtk;
    }

    public readonly record struct DiscSetAndSlot(string Text, string Key, int Slot) : IParsableData
    {
        public string GetPlainText() => Text;
    }

    public enum Rarity
    {
        B,
        A,
        S
    }

    public readonly record struct DiscLevelAndRarity(string Text, int Level, Rarity Tier) : IParsableData
    {
        public string GetPlainText() => Text;
    }

    public readonly record struct DiscMainStat(string Text, string Key) : IParsableData
    {
        public string GetPlainText() => Text;
    }

    public readonly record struct DiscSubStat(string Text, string Key, int Upgrades) : IParsableData
    {
        public string GetPlainText() => Text;
    }

    #endregion

    class Database
    {
        private static System.Globalization.CultureInfo culture = new System.Globalization.CultureInfo("en-GB", false);
        public static string appDir = Path.Join(Application.StartupPath, "ScannerFiles");
        public static string appdataPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdeptiScanner_ZZZ");
        public static string programVersion = "2.4.0";
        public static string dataVersion = "X.XX";
        //These get filled on startup by other file
        public static List<PieceData> Pieces = new List<PieceData>();
        public static List<CharacterNameData> Characters = new List<CharacterNameData>();
        public static List<ArtifactLevelData> ArtifactLevels = new List<ArtifactLevelData>();
        public static Database[] rarityData = new Database[3];
        public static List<WeaponNameData> WeaponNames = new List<WeaponNameData>();
        public static Dictionary<WeaponNameData, List<WeaponLevelAndAscensionData>> WeaponLevels = new Dictionary<WeaponNameData, List<WeaponLevelAndAscensionData>>();

        public static Dictionary<int, string> CharacterNames = new Dictionary<int, string>();
        public static Dictionary<string, string> SkillTypes = new Dictionary<string, string>();



        public List<ArtifactMainStatData> MainStats = new List<ArtifactMainStatData>();
        public List<ArtifactSubStatData> Substats = new List<ArtifactSubStatData>();
        public List<ArtifactSetData> Sets = new List<ArtifactSetData>();

        public static List<DiscSetAndSlot> DiscSets = new List<DiscSetAndSlot>();
        public static List<DiscLevelAndRarity> DiscLevels = new List<DiscLevelAndRarity>();
        public static Dictionary<int, List<DiscMainStat>> DiscMainStats = new();

        public List<DiscSubStat> DiscSubStats = new List<DiscSubStat>();

        public Database()
        {

        }


        /// <summary>
        /// Get Levenshtein Distance between two strings, taken from WFInfo and slightly modified
        /// </summary>
        /// <param name="s">One of the words to compare</param>
        /// <param name="t">Second word to compare</param>
        /// <returns>Levenshtein distance between <paramref name="s"/> and <paramref name="t"/>, after some filtering</returns>
        public static int LevenshteinDistance(string s, string t)
        {
            // Levenshtein Distance determines how many character changes it takes to form a known result
            // For more info see: https://en.wikipedia.org/wiki/Levenshtein_distance
            s = s.ToLower();
            t = t.ToLower();
            s = Regex.Replace(s, @"[+,.: ]", "");
            t = Regex.Replace(t, @"[+,.: ]", "");
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            if (n == 0 || m == 0)
                return n + m;

            d[0, 0] = 0;

            int count = 0;
            for (int i = 1; i <= n; i++)
                d[i, 0] = (s[i - 1] == ' ' ? count : ++count);

            count = 0;
            for (int j = 1; j <= m; j++)
                d[0, j] = (t[j - 1] == ' ' ? count : ++count);

            for (int i = 1; i <= n; i++)
                for (int j = 1; j <= m; j++)
                {
                    // deletion of s
                    int opt1 = d[i - 1, j];
                    if (s[i - 1] != ' ')
                        opt1++;

                    // deletion of t
                    int opt2 = d[i, j - 1];
                    if (t[j - 1] != ' ')
                        opt2++;

                    // swapping s to t
                    int opt3 = d[i - 1, j - 1];
                    if (t[j - 1] != s[i - 1])
                        opt3++;
                    d[i, j] = Math.Min(Math.Min(opt1, opt2), opt3);
                }



            return d[n, m];
        }

        /// <summary>
        /// Get closest match according to Levenshtein Distance, taken from WFInfo but slightly modified
        /// </summary>
        /// <param name="rawText">Word to find match for</param>
        /// <param name="validText">List of words to match against</param>
        /// <param name="dist">Levenshtein distance to closest match</param>
        /// <returns>Closest matching word</returns>
        public static string FindClosestMatch<T>(string rawText, List<T> validText, out T? result, out int dist) where T : struct, IParsableData
        {
            string lowest = "ERROR";
            dist = 9999;
            result = null;

            for (int i = 0; i < validText.Count; i++)
            {
                string validWord = validText[i].GetPlainText();
                int val = LevenshteinDistance(validWord, rawText);
                if (val < dist)
                {
                    result = validText[i];
                    dist = val;
                    lowest = validWord;
                }
            }
            return lowest;
        }


        static void ReadMainStats(JArray mainStats, List<DiscMainStat> mainStatList)
        {
            foreach (JObject mainStat in mainStats)
            {
                foreach (KeyValuePair<string, JToken> statNameTup in mainStat["name"].ToObject<JObject>())
                {
                    string statName = statNameTup.Key;
                    string statKey = statNameTup.Value.ToObject<string>();
                    mainStatList.Add(new DiscMainStat(statName, statKey));
                }
            }
        }

        void readSubstats(JArray substats)
        {
            foreach (JObject substat in substats)
            {
                foreach (KeyValuePair<string, JToken> statNameTup in substat["name"].ToObject<JObject>())
                {
                    string statName = statNameTup.Key;
                    string statKey = statNameTup.Value.ToObject<string>();
                    List<int> baserolls = new List<int>();
                    List<int> rolls = new List<int>();

                    double step = substat["step"].ToObject<double>();

                    bool showDecimal = Math.Abs(Math.Round(step) - step) > 0.001; // difference from rounded value is beyond precision, so it's not intended to be an int

                    for(int i = 1; i <= 6; i++)
                    {
                        double value = step * i;

                        string valueString = showDecimal ? value.ToString("N1", culture) : value.ToString("N0", culture);

                        string text = statName + valueString;
                        if (statName.Contains("%"))
                        {
                            text = text.Replace("%", "") + "%";
                        }
                        DiscSubStats.Add(new DiscSubStat(text, statKey, i));
                    }
                }
            }
        }

        /// <summary>
        /// Generate all possible text to look for and assign to filter word lists
        /// </summary>
        public static void GenerateFilters()
        {
            for (int i = 0; i < rarityData.Length; i++)
            {
                rarityData[i] = new Database();
            }


            for (int i = 0; i <= 9; i++)
            {
                DiscLevels.Add(new DiscLevelAndRarity("Lv. " + i.ToString("00") + "/09", i, Rarity.B));
            }

            for (int i = 0; i <= 12; i++)
            {
                DiscLevels.Add(new DiscLevelAndRarity("Lv. " + i.ToString("00") + "/12", i, Rarity.A));
            }

            for (int i = 0; i <= 15; i++)
            {
                DiscLevels.Add(new DiscLevelAndRarity("Lv. " + i.ToString("00") + "/15", i, Rarity.S));
            }

            //Main stat filter
            JObject allJson = new JObject();
            try
            {
                allJson = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(Path.Join(appDir, "ArtifactInfo.json")));
            } 
            catch (Exception e)
            {
                MessageBox.Show("Error trying to access ArtifactInfo file" + Environment.NewLine + Environment.NewLine +
                    "Exact error:" + Environment.NewLine + e.ToString(),

                    "Scanner could not start", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(-1);
            }
            if (allJson.TryGetValue("DataVersion", out JToken ver)) {
                dataVersion = ver.ToObject<string>();
                allJson.Remove("DataVersion");
            }
            foreach (KeyValuePair<string, JToken> entry in allJson)
            {
                JArray entry_arr = entry.Value.ToObject<JArray>();

                if (entry.Key == "Sets")
                {
                    foreach (JObject set in entry_arr)
                    {
                        foreach (KeyValuePair<string, JToken> statNameTup in set["name"].ToObject<JObject>())
                        {
                            string statName = statNameTup.Key;
                            string statKey = statNameTup.Value.ToObject<string>();
                            for (int i = 1; i <= 6; i++)
                            {
                                string text = statName + " [" + i + "]";
                                DiscSets.Add(new DiscSetAndSlot(text, statKey, i));
                            }
                        }
                    }
                }

                if (entry.Key == "DiscSlots")
                {
                    foreach (JObject slot in entry_arr)
                    {
                        int slotNum = slot["slot"].ToObject<int>();
                        List<DiscMainStat> mainStatList = new();
                        foreach (KeyValuePair<string, JToken> statNameTup in slot["data"].ToObject<JObject>())
                        {
                            if(statNameTup.Key == "MainStats")
                            {
                                ReadMainStats(statNameTup.Value.ToObject<JArray>(), mainStatList);
                            }
                        }

                        DiscMainStats[slotNum] = mainStatList;
                    }
                }


                if (entry.Key == "DiscTiers")
                {
                    foreach (JObject rarityTier in entry_arr)
                    {
                        Rarity rarity = rarityTier["rarity"].ToObject<Rarity>();
                        int rarityInt = (int)rarity;
                        JObject tierData = rarityTier["data"].ToObject<JObject>();

                        foreach (KeyValuePair<string, JToken> rarityEntry in tierData)
                        {
                            JArray rarityEntry_arr = rarityEntry.Value.ToObject<JArray>();
                            
                            if (rarityEntry.Key == "Substats")
                            {
                                rarityData[rarityInt].readSubstats(rarityEntry_arr);
                            }
                        }
                    }
                }

            }
        }

        public static void SetCharacterName(string displayName, string GOODName)
        {
            for (int i = 0; i < Characters.Count; i++)
            {
                if (Characters[i].Key == GOODName)
                {
                    Characters.RemoveAt(i);
                    Characters.Insert(i, new CharacterNameData("Equipped: " + displayName, GOODName));
                    break;
                }
            }
        }

        public static bool discInvalid(Disc item)
        {
            return true;
        }

        public static bool weaponInvalid(Weapon item)
        {
            return item.level == null || item.name == null || item.level.Value.Level > 90 
                || (item.name.Value.Rarity < 3 && item.level.Value.Level > 70) 
                || (item.name.Value.Rarity >= 3 && item.refinement == null);
        }
    }

}
