using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using MelonLoader;
using ScheduleOne.Economy;
using ScheduleOne.NPCs;
using ScheduleOne.UI.Compass;
using ScheduleOne.DevUtilities;
using ScheduleOne.Map;
using ScheduleOne.Persistence.Datas;
using ScheduleOne.Quests;
using ScheduleOne.PlayerScripts;

[assembly: MelonInfo(typeof(MVP_Tracker.Core), "MVP Tracker", "1.1.0", "Stehlel", null)]            // UPDATE VERSION NUMBER LOL
[assembly: MelonGame("TVGS", "Schedule I")]

namespace MVP_Tracker
{
    public class Core : MelonMod
    {
        private static readonly HashSet<string> RelevantFirstNames = new HashSet<string>
        {
            "Fiona","Herbert","Jen","Lily","Michael","Pearl","Ray","Tobias","Walter",
            "Jessi","Carl","Dean","George","Geraldine","Alison","Dennis","Hank","Harold",
            "Jack","Jackie","Jeremy","Karen","Chris"
        };

        private List<QuestEntry> questEntries = new List<QuestEntry>();
        private HashSet<String> entries = new HashSet<string>();
        private HashSet<String> onCd = new HashSet<string>();
        private readonly Dictionary<string, NPCPoI> pois = new Dictionary<string, NPCPoI>();
        private readonly Dictionary<string, CompassManager.Element> compassEls = new Dictionary<string, CompassManager.Element>();
        public Quest quest;

        public override void OnInitializeMelon()
        {
            MelonLogger.Msg("MVP Tracker geladen");
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Customer.onCustomerUnlocked -= HandleCustomerUnlocked;
            if (!scene.name.Equals("Main", StringComparison.OrdinalIgnoreCase))
            {
                MelonCoroutines.Stop(CooldownTicker());
                quest.ClearEntries();
                if (quest != null)
                    UnityEngine.Object.Destroy(quest.gameObject);
                quest = null;
                Customer.onCustomerUnlocked -= HandleCustomerUnlocked;

                return;
            }
            else
            {
                entries.Clear();
                onCd.Clear();
                pois.Clear();
                compassEls.Clear();
                questEntries.Clear();
                QuestManaging.guid = null;

                if (Player.Local != null)
                {
                    MelonCoroutines.Start(InitializeNextFrame());
                }
                else
                {
                    Player.onLocalPlayerSpawned += () =>
                    {
                        MelonCoroutines.Start(InitializeNextFrame());
                    };
                }
            }
        }

        private IEnumerator InitializeNextFrame()
        {
            yield return null;

            quest = QuestManaging.CreateQuest("MVP Tracker");
            quest.ClearEntries();
            try
            {
                quest.InitializeQuest("MVP Tracker", "Helps you keep track of the richest customers", [], QuestManaging.guid);
            }
            catch (Exception e)
            {
                MelonLogger.Error(e);
                yield break;
            }
            quest.Begin();

            Customer.onCustomerUnlocked += HandleCustomerUnlocked;
            foreach (var cust in Customer.UnlockedCustomers)
                HandleCustomerUnlocked(cust);

            MelonCoroutines.Start(CooldownTicker());
        }

        private IEnumerator CooldownTicker()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(1f);
                Update();
            }
        }

        private void HandleCustomerUnlocked(Customer cust)
        {
            if (cust?.NPC == null || !RelevantFirstNames.Contains(cust.NPC.FirstName))
                return;
            Create(cust);
        }

        private const int CooldownSeconds = 600;

        internal void Create(Customer cust)
        {
            var id = cust.NPC.ID;
            if (entries.Contains(id)) return;
            createPOICompass(cust);
            entries.Add(id);
            var newEntry = quest.AddEntry(cust.NPC.FirstName);
            questEntries.Add(newEntry);
        }

        internal void Update()
        {
            foreach (var entry in entries)
            {
                var id = entry.ToString();
                var cust = Customer.UnlockedCustomers.Find(c => c.NPC.ID == id);
                if (cust == null) continue;

                bool hasDealer = cust.AssignedDealer != null;
                float elapsed = cust.TimeSinceInstantDealOffered;
                bool isCd = elapsed < CooldownSeconds;
                bool wasCd = onCd.Contains(id);

                if (!pois.ContainsKey(id)) continue;
                var poi = pois[id];
                var compEl = compassEls[id];

                foreach (var qEntry in questEntries)
                {
                    if (! qEntry.Title.Contains(cust.NPC.FirstName)) continue;
                    if (!hasDealer)
                    {
                        if (isCd)
                        {
                            int rem = CooldownSeconds - (int)elapsed;
                            qEntry.SetActive(true);
                            qEntry.SetEntryTitle($"<color=orange>{cust.NPC.FirstName}: {rem / 60}h {rem % 60}m</color>");
                            poi.gameObject.SetActive(false);
                            compEl.Visible = false;
                            if (!wasCd) onCd.Add(id);
                        }
                        else
                        {
                            qEntry.SetActive(true);
                            qEntry.SetEntryTitle($"<color=green>{cust.NPC.FirstName}: Available</color>");
                            poi.transform.position = cust.NPC.transform.position;
                            poi.SetMainText(cust.NPC.FirstName);
                            poi.gameObject.SetActive(true);
                            compEl.Visible = true;
                            if (wasCd) onCd.Remove(id);
                        }
                    }
                    else
                    {
                        qEntry.SetActive(true);
                        qEntry.SetEntryTitle($"<color=red>{cust.NPC.FirstName} Warning: Dealer assigned! {cust.AssignedDealer.name}</color> ");
                        poi.gameObject.SetActive(false);
                        compEl.Visible = false;
                    }
                }
            }
        }

        internal void createPOICompass(Customer cust)
        {
            var poi = UnityEngine.Object.Instantiate(
                NetworkSingleton<NPCManager>.Instance.PotentialCustomerPoIPrefab,
                cust.NPC.transform
            );
            poi.SetNPC(cust.NPC);
            poi.SetMainText(cust.NPC.FirstName);
            pois[cust.NPC.ID] = poi;

            var compEl = CompassManager.Instance.AddElement(
                poi.transform,
                poi.UI.GetComponent<RectTransform>()
            );
            compassEls[cust.NPC.ID] = compEl;
        }
    }
 
    public static class QuestManaging
    {
        public static string guid;
        public static Quest CreateQuest(string title)
        {
            if (string.IsNullOrEmpty(guid))
                guid = Guid.NewGuid().ToString();

            var prefab = QuestManager.Instance.DefaultQuests[0];
            var questGO = UnityEngine.Object.Instantiate(prefab.gameObject, QuestManager.Instance.QuestContainer);
            var questCo = questGO.GetComponent<Quest>();

            return questCo;
        }

        public static QuestEntry AddEntry(this Quest quest, string entryTitle)
        {
            var entryData = new QuestEntryData(entryTitle, EQuestState.Inactive);

            var go = new GameObject(entryTitle);
            go.transform.SetParent(quest.transform, worldPositionStays: false);
            var entry = go.AddComponent<QuestEntry>();;

            quest.Entries.Add(entry);
            entry.SetData(entryData);

            return entry;
        }

        public static void ClearEntries(this Quest quest)
        {
            foreach (var entry in quest.Entries.ToArray())
                UnityEngine.Object.Destroy(entry.gameObject);

            quest.Entries.Clear();
        }
    }
}