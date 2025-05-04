using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
using ScheduleOne.Product;
using ScheduleOne.ItemFramework;
using ScheduleOne.Persistence;

[assembly: MelonInfo(typeof(MVP_Tracker.Core), "MVP Tracker", "2.0.0", "Stehlel", null)]            // UPDATE VERSION NUMBER LOL
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
            MelonLogger.Msg("MVP Tracker loaded");
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var sm = SaveManager.Instance;
            sm.onSaveStart.AddListener(OnBeforeSave);
            sm.onSaveComplete.AddListener(OnAfterSave);
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

        private void OnBeforeSave()
        {
            var sm = SaveManager.Instance;
            sm.Saveables.Remove(quest);
            if (quest == null) return;
            Quest.Quests.Remove(quest);
            Debug.Log("[MVP Tracker] Dynamic quest suppressed from save");
        }

        private void OnAfterSave()
        {
            if (quest == null) return;
            if (!Quest.Quests.Contains(quest))
                Quest.Quests.Add(quest);
            Debug.Log("[MVP Tracker] Dynamic quest restored after save");
        }

        private IEnumerator InitializeNextFrame()
        {
            yield return null;

            quest = QuestManaging.CreateQuest("MVP Tracker");
            quest.ClearEntries();
            try
            {
                quest.InitializeQuest("MVP Tracker", "Helps you keep track of the richest customers", [], QuestManaging.guid);
                var sm = SaveManager.Instance;

                sm.Saveables.Remove(quest);
                if (quest is IBaseSaveable baseSave)
                {
                    sm.BaseSaveables.Remove(baseSave);
                }
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

        private const int CooldownSeconds = 360;

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
                    if (!qEntry.Title.Contains(cust.NPC.FirstName)) continue;
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
                            var chance = calculateChance(cust);

                            var getOptimal = GetOptimalProductAndPrice(cust);

                            var budget = calculateDailyBudget(cust);

                            qEntry.SetActive(true);
                            qEntry.SetEntryTitle($"<color=green>{cust.NPC.FirstName} ({chance}%): Budget: {budget}$, {getOptimal.optimalQuantity}x{getOptimal.product.Name} ({getOptimal.optimalPrice}$ ,{getOptimal.acceptanceProbability}%)</color>");
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

        public (ProductDefinition product,
                float optimalPrice,
                float acceptanceProbability,
                int optimalQuantity)
        GetOptimalProductAndPrice(Customer cust, int priceStep = 5)
        {
            int orderDays = cust.CustomerData
                .GetOrderDays(cust.CurrentAddiction, cust.NPC.RelationData.RelationDelta / 5f)
                .Count;
            orderDays = Mathf.Max(orderDays, 1);
            float dailyBudget = cust.CustomerData
                .GetAdjustedWeeklySpend(cust.NPC.RelationData.RelationDelta / 5f)
                / orderDays;

            EQuality q = cust.CustomerData.Standards.GetCorrespondingQuality();

            ProductDefinition bestProduct = null;
            float bestPrice = 0f, bestRevenue = 0f, bestProb = 0f;

            var allProducts = ProductManager.Instance.AllProducts;
            if (allProducts == null || allProducts.Count == 0)
                return (null, 0f, 0f, 0);

            foreach (var def in allProducts)
            {
                var inst = def.GetDefaultInstance(1);
                if (inst is ProductItemInstance pi)
                {
                    pi.Quality = q;
                    if (def.ValidPackaging != null && def.ValidPackaging.Length > 0)
                        pi.SetPackaging(def.ValidPackaging[1]);
                }
                var items = new List<ItemInstance> { inst };

                float basePrice = def.Price;
                float startPrice = Mathf.Max(basePrice, priceStep);
                float maxPrice = Mathf.Floor(dailyBudget * 3f / priceStep) * priceStep;
                maxPrice = Mathf.Max(startPrice, maxPrice);

                for (float price = startPrice; price <= maxPrice; price += priceStep)
                {
                    float prob = cust.GetOfferSuccessChance(items, price);
                    float revenue = price * prob;
                    if (revenue > bestRevenue)
                    {
                        bestRevenue = revenue;
                        bestPrice = price;
                        bestProduct = def;
                        bestProb = prob;
                    }
                }
            }

            int optimalQuantity = 1;
            float pricePerItem = bestPrice;
            if (bestProduct != null && bestPrice > 0)
            {
                float enjoyment = cust.GetProductEnjoyment(bestProduct, q);
                float priceFactor = Mathf.Lerp(0.66f, 1.5f, enjoyment);
                int qty = Mathf.RoundToInt(dailyBudget * priceFactor / bestProduct.Price);
                qty = Mathf.Clamp(qty, 1, 1000);
                if (qty >= 14)
                    qty = Mathf.RoundToInt(qty / 5f) * 5;
                optimalQuantity = qty;
                pricePerItem = bestPrice / qty;
            }
            bestProb = Mathf.RoundToInt(bestProb * 100f);
            bestPrice = Mathf.RoundToInt(bestPrice);

            return (bestProduct, bestPrice, bestProb, optimalQuantity);
        }

        internal int calculateChance(Customer cust)
        {
            int chancePercent = 0;

            double num1 = Mathf.Clamp01(cust.TimeSinceLastDealCompleted / 1440f) * 0.5;
            float num2 = cust.NPC.RelationData.NormalizedRelationDelta * 0.3f;
            float num3 = cust.CurrentAddiction * 0.2f;

            float chance = (float)(num1 + num2) + num3;

            chancePercent = Mathf.RoundToInt(chance * 100f);

            return chancePercent;
        }

        internal float calculateDailyBudget(Customer cust)
        {
            float dailyBudget = 0;

            float weeklyBudget = cust.CustomerData.GetAdjustedWeeklySpend(cust.NPC.RelationData.RelationDelta / 5f);
            int orderDays = cust.CustomerData.GetOrderDays(cust.CurrentAddiction, cust.NPC.RelationData.RelationDelta / 5f).Count;
            dailyBudget = MathF.Round((weeklyBudget / orderDays));

            return dailyBudget;
        }

        internal ProductDefinition bestProduct(Customer cust)
        {
            EQuality q = cust.CustomerData.Standards.GetCorrespondingQuality();
            return cust.OrderableProducts
                .Select(def => new {
                    def,
                    Appeal = cust.GetProductEnjoyment(def, q)
                           + Mathf.Lerp(1f, -1f, def.Price / def.MarketValue / 2f)
                })
                .OrderByDescending(x => x.Appeal)
                .First()
                .def;
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