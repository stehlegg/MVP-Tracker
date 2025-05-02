using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MelonLoader;
using FishNet;                           // for InstanceFinder
using FishNet.Managing;
using ScheduleOne.Economy;
using ScheduleOne.NPCs;
using ScheduleOne.UI.Compass;
using HarmonyLib;
using ScheduleOne.Quests;
using ScheduleOne.DevUtilities;
using ScheduleOne.Map;
using ScheduleOne.Persistence.Datas;
using System.Xml.Linq;

[assembly: MelonInfo(typeof(MVP_Tracker.Core), "MVP Tracker", "1.0.0", "Stehlel", null)]
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

        private HashSet<String> entries = new HashSet<string>();
        private HashSet<String> onCd = new HashSet<string>();
        private readonly Dictionary<string, NPCPoI> pois = new Dictionary<string, NPCPoI>();
        private readonly Dictionary<string, CompassManager.Element> compassEls = new Dictionary<string, CompassManager.Element>();
        private Dictionary<String, String> checklist = new Dictionary<string, string>();

        public override void OnInitializeMelon()
        {
            MelonLogger.Msg("MVP Tracker geladen");
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!scene.name.Equals("Main", StringComparison.OrdinalIgnoreCase))
            {
                MelonCoroutines.Stop(CooldownTicker());
                return;
            }

            entries = new HashSet<string>();
            onCd = new HashSet<string>();
            Dictionary<string, NPCPoI> pois = new Dictionary<string, NPCPoI>();
            Dictionary<string, CompassManager.Element> compassEls = new Dictionary<string, CompassManager.Element>();
            Dictionary<String, String> checklist = new Dictionary<string, string>();

            //SceneManager.sceneLoaded -= OnSceneLoaded;
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

                if (!hasDealer)
                {
                    if (isCd)
                    {
                        int rem = CooldownSeconds - (int)elapsed;
                        AddItem(cust.NPC.FirstName, $"<color=orange>{cust.NPC.FirstName}: {rem / 60}h {rem % 60}m</color>");
                        poi.gameObject.SetActive(false);
                        compEl.Visible = false;
                        if (!wasCd) onCd.Add(id);
                    }
                    else
                    {
                        AddItem(cust.NPC.FirstName, $"<color=green>{cust.NPC.FirstName}: Available</color>");
                        poi.transform.position = cust.NPC.transform.position;
                        poi.SetMainText(cust.NPC.FirstName);
                        poi.gameObject.SetActive(true);
                        compEl.Visible = true;
                        if (wasCd) onCd.Remove(id);
                    }
                }
                else
                {
                    // treat warning like cooldown: hide markers
                    AddItem(cust.NPC.FirstName, $"<color=red>{cust.NPC.FirstName} Warning: Dealer assigned! {cust.AssignedDealer.name}</color> ");
                    poi.gameObject.SetActive(false);
                    compEl.Visible = false;
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

        public override void OnGUI()
        {
            GUILayout.BeginArea(new Rect(30, 30, 250, 150), GUI.skin.box );
            GUILayout.Label(" MVP Tracker");

            foreach (var val in new List<string>(checklist.Values))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(val);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }

        public void AddItem(String id, String text)
        {
            if(checklist.ContainsKey(id))
                RemoveItem(id);
            checklist[id] = text;
        }

        public void RemoveItem(string id)
        {
            checklist.Remove(id);
        }
    }
}