using BepInEx;
using RoR2;
using System;
using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using MonoMod.Cil;
using BepInEx.Configuration;

namespace RPGMod
{
    public class CommandHelper
    {
        public static void RegisterCommands(RoR2.Console self)
        {
            var types = typeof(CommandHelper).Assembly.GetTypes();
            var catalog = self.GetFieldValue<IDictionary>("concommandCatalog");

            foreach (var methodInfo in types.SelectMany(x => x.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)))
            {
                var customAttributes = methodInfo.GetCustomAttributes(false);
                foreach (var attribute in customAttributes.OfType<ConCommandAttribute>())
                {
                    var conCommand = Reflection.GetNestedType<RoR2.Console>("ConCommand").Instantiate();

                    conCommand.SetFieldValue("flags", attribute.flags);
                    conCommand.SetFieldValue("helpText", attribute.helpText);
                    conCommand.SetFieldValue("action", (RoR2.Console.ConCommandDelegate)Delegate.CreateDelegate(typeof(RoR2.Console.ConCommandDelegate), methodInfo));

                    catalog[attribute.commandName.ToLower()] = conCommand;
                }
            }
        }
    }
    // Quest Message that gets sent to all clients
    public class QuestMessage : MessageBase
    {
        public bool Initialised;
        public string Description;
        public string Target;
        public string TargetName;

        public override void Deserialize(NetworkReader reader)
        {
            Initialised = reader.ReadBoolean();
            Description = reader.ReadString();
            Target = reader.ReadString();
            TargetName = reader.ReadString();
        }

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(Initialised);
            writer.Write(Description);
            writer.Write(Target);
            writer.Write(TargetName);
        }
    }

    // All server side data
    public struct ServerQuestData
    {
        public PickupIndex Drop;
        public int Objective;
        public int Progress;
    }

    [BepInPlugin("com.bamboo98.rpgmod", "RPGMod", "1.3.0")]

    public class RPGMod : BaseUnityPlugin
    {

        // Misc params
        public System.Random random = new System.Random();
        // public SpawnCard chest2 = Resources.Load<SpawnCard>("SpawnCards/InteractableSpawnCard/iscchest2");
        public GameObject targetBody;
        public bool isLoaded = false;
        public bool isDebug = false;
        public bool questFirst = true;
        public bool isSuicide = false;
        public String[] bannedDirectorSpawns;
        public String[] bannedRewards;
        public float percentSpawns = 1.0f;

        // Networking params
        public short msgQuestDrop = 1337;
        public bool isClientRegistered = false;
        public QuestMessage questMessage = new QuestMessage();
        public ServerQuestData serverQuestData;

        // Chance params
        public float chanceNormal;
        public float chanceElite;
        public float chanceNormalMax;
        public float chanceEliteMax;
        public float chanceBoss;
        public float bossChestChanceLegendary;
        public float bossChestChanceUncommon;
        public float chanceQuestingCommon;
        public float chanceQuestingUnCommon;
        public float chanceQuestingLegendary;
        public float dropsPlayerScaling;
        public float eliteChanceTier1;
        public float eliteChanceTier2;
        public float eliteChanceTier3;
        public float eliteChanceTierLunar;
        public float normalChanceTier1;
        public float normalChanceTier2;
        public float normalChanceTier3;
        public float normalChanceTierEquip;
        public float gameStartScaling;

        // UI params
        public Notification Notification { get; set; }
        public int screenPosX;
        public int screenPosY;
        //public int titleFontSize;
        //public int descriptionFontSize;
        public int sizeX;
        public int sizeY;
        public bool resetUI = false;
        public bool Persistent = true;
        public CharacterBody CachedCharacterBody;

        // Questing params
        public int questObjectiveFactor;
        public int questObjectiveLimit;
        public bool itemDroppingFromPlayers;
        public bool questInChat;
        public List<string> questList = new List<string>() { "<b>Kill</b>", "<b>Eliminate</b>"};
        public static List<string> questLanguage = new List<string>() { "Kill", "Eliminate", "Reward", "QUEST", "Progress","Enemy multiple" };
        public static List<string> spawnsList = new List<string>() {};
        public int questIndex;
        public bool stageChange = false;

        // Feature params
        public bool isChests;
        public bool isBossChests;
        public bool isEnemyDrops;
        public bool isQuesting;
        public bool isQuestResetting;

        //EX drops params
        private List<PickupIndex> availableTier1DropList;
        private List<PickupIndex> availableTier2DropList;
        private List<PickupIndex> availableTier3DropList;
        private List<PickupIndex> availableEquipmentDropList;
        private List<PickupIndex> availableLunarDropList;
        private bool isDropInit=false;
        private bool luckAffectingDrop = false;
        private bool randomBossRewards = false;
        private float dropWhenLevelUp = -1f;
        private bool hasPocketMoney = true;
        public float dropCoolingTime=0;
        public float lastDropTime = -9999;
        public float lastLevelUpTime = -9999;


        private static ConfigWrapper<int> MultiplierConfig { get; set; }

        public int Multiplier
        {
            get => enabled ? MultiplierConfig.Value  : 1;
            protected set => MultiplierConfig.Value = value;
        }
        private static ConfigWrapper<uint> StageExtraMoney { get; set; }
        private static ConfigWrapper<float> StageWeightedMoney { get; set; }
        // Refreshes the config values from the config
        public void RefreshConfigValues(bool initLoad)
        {
            if (!initLoad)
            {
                Config.Reload();
            }

            // Chances
            luckAffectingDrop = Convert.ToBoolean(Config.Wrap("Chances", "luckAffectingDrop", "Luck will affect drop(if you have any CLOVERs,make this true will get more rewards)(bool)", "false").Value);
            randomBossRewards = Convert.ToBoolean(Config.Wrap("Chances", "randomBossRewards", "BOSS rewards will random spawn(dont affect some bound rewards like BEETLEGLAND)(bool)", "true").Value);
            dropCoolingTime = ConfigToFloat(Config.Wrap("Chances", "dropCoolingTime", "Time between two drops,set 0 to disable cooling time(second,float)", "1.5").Value);
            chanceNormal = ConfigToFloat(Config.Wrap("Chances", "chanceNormal", "Base chance for a normal enemy to drop an item (float)", "1").Value);
            chanceElite = ConfigToFloat(Config.Wrap("Chances", "chanceElite", "Base chance for an elite enemy to drop an item (float)", "3").Value);
            chanceNormalMax = ConfigToFloat(Config.Wrap("Chances", "chanceNormal", "Max chance for a normal enemy to drop an item (float)", "1").Value);
            chanceEliteMax = ConfigToFloat(Config.Wrap("Chances", "chanceElite", "Max chance for an elite enemy to drop an item (float)", "3").Value);

            // chanceBoss = ConfigToFloat(Config.Wrap("Chances", "chanceBoss", "Base chance for a boss enemy to drop an item (float)", "35.0").Value);

            bossChestChanceLegendary = ConfigToFloat(Config.Wrap("Chances", "bossChestChanceLegendary", "Chance for a legendary to drop from a boss chest (float)", "0.25").Value);
            bossChestChanceUncommon = ConfigToFloat(Config.Wrap("Chances", "bossChestChanceUncommon", "Chance for a uncommon to drop from a boss chest (float)", "0.75").Value);

            chanceQuestingCommon = ConfigToFloat(Config.Wrap("Chances", "chanceQuestingCommon", "Chance for quest drop to be common (float)", "0").Value);
            chanceQuestingUnCommon = ConfigToFloat(Config.Wrap("Chances", "chanceQuestingUnCommon", "Chance for quest drop to be uncommon (float)", "0.95").Value);
            chanceQuestingLegendary = ConfigToFloat(Config.Wrap("Chances", "chanceQuestingLegendary", "Chance for quest drop to be legendary (float)", "0.05").Value);
            dropsPlayerScaling = ConfigToFloat(Config.Wrap("Chances", "dropsPlayerScaling", "Scaling per player (drop chance percentage increase per player) (float)", "0.15").Value);

            eliteChanceTier1 = ConfigToFloat(Config.Wrap("Chances", "eliteChanceTier1", "Chance for elite to drop a tier 1 item (float)", "0.35").Value);
            eliteChanceTier2 = ConfigToFloat(Config.Wrap("Chances", "eliteChanceTier2", "Chance for elite to drop a tier 2 item (float)", "0.55").Value);
            eliteChanceTier3 = ConfigToFloat(Config.Wrap("Chances", "eliteChanceTier3", "Chance for elite to drop a tier 3 item (float)", "0.05").Value);
            eliteChanceTierLunar = ConfigToFloat(Config.Wrap("Chances", "eliteChanceTierLunar", "Chance for elite to drop a lunar item (float)", "0.05").Value);

            normalChanceTier1 = ConfigToFloat(Config.Wrap("Chances", "normalChanceTier1", "Chance for normal enemy to drop a tier 1 item (float)", "0.59").Value);
            normalChanceTier2 = ConfigToFloat(Config.Wrap("Chances", "normalChanceTier2", "Chance for normal enemy to drop a tier 2 item (float)", "0.34").Value);
            normalChanceTier3 = ConfigToFloat(Config.Wrap("Chances", "normalChanceTier3", "Chance for normal enemy to drop a tier 3 item (float)", "0.01").Value);
            normalChanceTierEquip = ConfigToFloat(Config.Wrap("Chances", "normalChanceTierEquip", "Chance for normal enemy to drop equipment (float)", "0.06").Value);

            gameStartScaling = ConfigToFloat(Config.Wrap("Chances", "gameStartScaling", "Scaling of chances for the start of the game, that goes away during later stages (float)", "1.5").Value);

            // UI params
            screenPosX = Config.Wrap("UI", "Screen Pos X", "UI location on the x axis (percentage of screen width) (int)", 89).Value;
            screenPosY = Config.Wrap("UI", "Screen Pos Y", "UI location on the y axis (percentage of screen height) (int)", 50).Value;
            //titleFontSize = Config.Wrap("UI", "Title Font Size", "UI title font size (int)", 18).Value;
            //descriptionFontSize = Config.Wrap("UI", "Description Font Size", "UI description font size (int)", 14).Value;
            sizeX = Config.Wrap("UI", "Size X", "Size of UI on the x axis (pixels)", 300).Value;
            sizeY = Config.Wrap("UI", "Size Y", "Size of UI on the x axis (pixels) (int)", 80).Value;

            // Questing params
            questLanguage= Config.Wrap<String>("Questing", "questLanguage", "Translate the words below into your language(A comma seperated list)", "Kill,Eliminate,Reward,QUEST,Progress,Enemy multiple").Value.Split(',').ToList();
            questObjectiveFactor = Config.Wrap("Questing", "Quest Objective Minimum", "The factor for quest objective values (int)", 12).Value;
            questObjectiveLimit = Config.Wrap("Questing", "Quest Objective Limit", "The factor for the max quest objective value (int)", 30).Value;
            itemDroppingFromPlayers = Convert.ToBoolean(Config.Wrap("Questing", "itemDroppingFromPlayers", "Items drop from player instead of popping up in inventory (bool)", "false").Value);
            questInChat = Convert.ToBoolean(Config.Wrap("Questing", "questInChat", "Quests show up in chat (useful when playing with unmodded players) (bool)", "true").Value);

            // Director params
            MultiplierConfig = Config.Wrap(
                "Director",
                "Multiplier",
                "Sets the Monster and BOSS multiplier .Does not affect the amount of rewards",
                5);
            percentSpawns = ConfigToFloat(Config.Wrap("Director", "percentSpawns", "Percentage amount of world spawns", "0.75").Value);
            bannedDirectorSpawns = Config.Wrap("Director", "bannedDirectorSpawns", "A comma seperated list of banned Interactables for director(use cmd 'rpg_show_spawns' to get all interactables in this scene)", "ShrineHealing,Drone1Broken,Drone2Broken").Value.ToUpper().Split(',');

            bannedRewards = Config.Wrap("Director", "bannedRewards", "A comma seperated list of banned spawns for drops,this list will not affect chests' drop and shops(I suggest that you better ban SHOCKNEARBY and CLOVER,because getting two items will make the game too simple)(you can find itemID in Risk of Rain 2_Data\\Language\\yourLanguage\\****.json,open the file and search 'CLOVER',and you will know what's itemID)", "CLOVER,SHOCKNEARBY").Value.ToUpper().Split(',');


            isChests = Convert.ToBoolean(Config.Wrap("Director", "Interactables", "Use banned director spawns (bool)", "true").Value);

            StageExtraMoney = Config.Wrap<uint>(
                "Director",
                "StageExtraMoney",
                "The flat amount of extra money the player should receive at beginning of each stage (uint)",
                0);

            StageWeightedMoney = Config.Wrap(
                "Director",
                "StageWeightedMoney",
                "The number of small chest worth of money you get at start of each stage (float)",
                1.0f);


            // Feature params
            // isBossChests = Convert.ToBoolean(Config.Wrap("Features", "Boss Chests", "Boss loot chests (recommended to turn off when enabling interactables) (bool)", "false").Value);
            isQuesting = Convert.ToBoolean(Config.Wrap("Features", "Questing", "Questing system (bool)", "true").Value);
            isEnemyDrops = Convert.ToBoolean(Config.Wrap("Features", "Enemy Drops", "Enemies drop items (bool)", "true").Value);
            isQuestResetting = Convert.ToBoolean(Config.Wrap("Features", "Quest Resetting", "Determines whether quests reset over stage advancement (bool)", "false").Value);
            dropWhenLevelUp = ConfigToFloat(Config.Wrap("Features", "dropWhenLevelUp", "Get a base drop when level up(set -1 to disable,other to limit dropping frequency)(second,bool)", "30").Value);
            hasPocketMoney = Convert.ToBoolean(Config.Wrap("Features", "hasPocketMoney", "Get more money at the beginning of each scene(bool)", "true").Value);

            if (questLanguage.Count != 6)
            {
                questLanguage = "Kill,Eliminate,Reward,QUEST,Progress,Enemy multiple".Split(',').ToList();
            }
            questList = new List<string>() { "<b>" + questLanguage[0] + "</b>", "<b>" + questLanguage[1] + "</b>"};

            if(bannedDirectorSpawns.Count() == 1 && bannedDirectorSpawns[0].IsNullOrWhiteSpace())
            {
                isChests = false;
            }


            isDropInit = false;



            // force UI refresh and send message
            resetUI = true;
            Debug.Log("<color=#13d3dd>RPGMod: </color> Config loaded");
        }

        // Handles questing
        public void CheckQuest()
        {
            if (!questMessage.Initialised)
            {
                GetNewQuest();
            }
            else
            {
                DisplayQuesting();
            }
        }

        // Sets quest parameters
        public void GetNewQuest()
        {
            if (!NetworkServer.active) {
                return;
            }

            int monstersAlive = TeamComponent.GetTeamMembers(TeamIndex.Monster).Count;

            if (monstersAlive > 0)
            {
                CharacterBody targetBody = TeamComponent.GetTeamMembers(TeamIndex.Monster)[random.Next(0, monstersAlive)].GetComponent<CharacterBody>();

                if (targetBody.isBoss || SurvivorCatalog.FindSurvivorDefFromBody(targetBody.master.bodyPrefab) != null)
                {
                    return;
                }

                questMessage.Target = targetBody.GetUserName();
                questMessage.TargetName = targetBody.name;
                int upperObjectiveLimit = (int)Math.Round(questObjectiveFactor * Run.instance.compensatedDifficultyCoefficient);

                if (upperObjectiveLimit >= questObjectiveLimit)
                {
                    upperObjectiveLimit = questObjectiveLimit;
                }

                if (!stageChange || questFirst || isQuestResetting)
                {
                    serverQuestData.Objective = random.Next(questObjectiveFactor, upperObjectiveLimit);
                    serverQuestData.Progress = 0;
                    serverQuestData.Drop = GetQuestDrop();
                }
                questMessage.Initialised = true;
                questIndex = random.Next(0, questList.Count);
                questMessage.Description = GetDescription();
                if (questInChat)
                {
                    Chat.SimpleChatMessage message = new Chat.SimpleChatMessage();
                    message.baseToken = string.Format("{0}: {1} {2} {3} ，{4}: <color=#{5}>{6}</color>",
                        questLanguage[3],
                        questLanguage[1],
                        serverQuestData.Objective,
                        questMessage.Target,
                        questLanguage[2],
                        ColorUtility.ToHtmlStringRGBA(serverQuestData.Drop.GetPickupColor()),
                        Language.GetString(ItemCatalog.GetItemDef(serverQuestData.Drop.itemIndex).nameToken));
                    Chat.SendBroadcastChat(message);
                }
                questFirst = false;
                stageChange = false;
                SendQuest();
            }
        }

        // Check if quest fulfilled
        public void CheckQuestStatus()
        {
            if (!NetworkServer.active) {
                return;
            }
            if (serverQuestData.Progress >= serverQuestData.Objective)
            {
                if (questMessage.Initialised) {
                    foreach (var player in PlayerCharacterMasterController.instances)
                    {
                        if (player.master.alive)
                        {
                            var transform = player.master.GetBody().coreTransform;
                            if (itemDroppingFromPlayers)
                            {
                                PickupDropletController.CreatePickupDroplet(serverQuestData.Drop, transform.position, transform.forward * 10f);
                            }
                            else
                            {
                                player.master.inventory.GiveItem(serverQuestData.Drop.itemIndex);
                            }
                        }
                    }
                }
                questMessage.Initialised = false;
            }
        }

        // Handles the display of the UI
        public void DisplayQuesting()
        {
            LocalUser localUser = LocalUserManager.GetFirstLocalUser();

            if (CachedCharacterBody == null && localUser != null)
            {
                CachedCharacterBody = localUser.cachedBody;
            }

            if (Notification == null && CachedCharacterBody != null || resetUI)
            {
                if (resetUI)
                {
                    Destroy(Notification);
                }

                if (isDebug)
                {
                    Debug.Log(CachedCharacterBody);
                    Debug.Log(sizeX);
                    Debug.Log(sizeY);
                    Debug.Log(Screen.width * screenPosX / 100f);
                    Debug.Log(Screen.height * screenPosY / 100f);
                    Debug.Log(questMessage.Description);
                    //Debug.Log(titleFontSize);
                    //Debug.Log(descriptionFontSize);
                }

                Notification = CachedCharacterBody.gameObject.AddComponent<Notification>();
                Notification.transform.SetParent(CachedCharacterBody.transform);
                Notification.SetPosition(new Vector3((float)(Screen.width * screenPosX / 100f), (float)(Screen.height * screenPosY / 100f), 0));
                Notification.GetTitle = () => questLanguage[3];
                Notification.GetDescription = () => questMessage.Description;
                Notification.GenericNotification.fadeTime = 1f;
                Notification.GenericNotification.duration = 86400f;
                Notification.SetSize(sizeX, sizeY);
                resetUI = false;
            }

            if (questMessage.Initialised)
            {
                Notification.SetIcon(BodyCatalog.FindBodyPrefab(questMessage.TargetName).GetComponent<CharacterBody>().portraitIcon);
            }

            if (CachedCharacterBody == null && Notification != null)
            {
                Destroy(Notification);
            }

            if (Notification != null && Notification.RootObject != null)
            {
                if (Persistent || (localUser != null && localUser.inputPlayer != null && localUser.inputPlayer.GetButton("info")))
                {
                    Notification.RootObject.SetActive(true);
                    return;
                }

                Notification.RootObject.SetActive(false);
            }
        }
        private void InitDropList()
        {
            if (!isDropInit)
            {
                availableTier1DropList = new List<PickupIndex>(Run.instance.availableTier1DropList);
                availableTier2DropList = new List<PickupIndex>(Run.instance.availableTier2DropList);
                availableTier3DropList = new List<PickupIndex>(Run.instance.availableTier3DropList);
                availableEquipmentDropList = new List<PickupIndex>(Run.instance.availableEquipmentDropList);
                availableLunarDropList = new List<PickupIndex>(Run.instance.availableLunarDropList);

                availableTier1DropList = availableTier1DropList.Where(val => !bannedRewards.Any(val.itemIndex.ToString().ToUpper().Equals)).ToList();
                availableTier2DropList = availableTier2DropList.Where(val => !bannedRewards.Any(val.itemIndex.ToString().ToUpper().Equals)).ToList();
                availableTier3DropList = availableTier3DropList.Where(val => !bannedRewards.Any(val.itemIndex.ToString().ToUpper().Equals)).ToList();
                availableEquipmentDropList = availableEquipmentDropList.Where(val => !bannedRewards.Any(val.equipmentIndex.ToString().ToUpper().Equals)).ToList();
                availableLunarDropList = availableLunarDropList.Where(val => !bannedRewards.Any(val.itemIndex.ToString().ToUpper().Equals)).ToList();
                isDropInit = true;
            }
        }

        // Gets the drop for the quest
        public PickupIndex GetQuestDrop()
        {
            InitDropList();
            WeightedSelection<List<PickupIndex>> weightedSelection = new WeightedSelection<List<PickupIndex>>(8);

            weightedSelection.AddChoice(availableTier1DropList, chanceQuestingCommon);
            weightedSelection.AddChoice(availableTier2DropList, chanceQuestingUnCommon);
            weightedSelection.AddChoice(availableTier3DropList, chanceQuestingLegendary);

            List<PickupIndex> list = weightedSelection.Evaluate(Run.instance.spawnRng.nextNormalizedFloat);
            PickupIndex item = list[Run.instance.spawnRng.RangeInt(0, list.Count)];
            
            return item;
        }

        // Set Client Handlers
        public void InitClientHanders()
        {
            Debug.Log("[RPGMod] Client Handlers Added");
            NetworkClient client = NetworkManager.singleton.client;

            client.RegisterHandler(msgQuestDrop, OnQuestRecieved);
            isClientRegistered = true;
        }

        // Send data message
        public void SendQuest()
        {
            if (!NetworkServer.active) {
                return;
            }
            NetworkServer.SendToAll(msgQuestDrop, questMessage);
        }

        // Handler function for quest drop message
        public void OnQuestRecieved(NetworkMessage netMsg) {
            QuestMessage message = netMsg.ReadMessage<QuestMessage>();
            questMessage = message;
        }

        // Builds the string for the quest description
        public string GetDescription()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format("{0} {1} {2}", questList[questIndex], serverQuestData.Objective, questMessage.Target));
            sb.AppendLine(string.Format("<b>{0}:</b> {1}/{2}", questLanguage[4], serverQuestData.Progress, serverQuestData.Objective));
            sb.AppendLine(string.Format("<b>{0}:</b> <color=#{1}>{2}</color>", questLanguage[2], ColorUtility.ToHtmlStringRGBA(serverQuestData.Drop.GetPickupColor()), Language.GetString(ItemCatalog.GetItemDef(serverQuestData.Drop.itemIndex).nameToken)));
            return sb.ToString();
        }

        // Converts string config to a float
        public float ConfigToFloat(string configline)
        {
            if (float.TryParse(configline, NumberStyles.Any, CultureInfo.InvariantCulture, out float x))
            {
                return x;
            }
            return 0f;
        }

        // Drops Boss Chest
        public void DropBoss()
        {
            DirectorSpawnRequest r=new DirectorSpawnRequest(Resources.Load<SpawnCard>("SpawnCards/InteractableSpawnCard/iscChest1"), new DirectorPlacementRule
            {
                placementMode = DirectorPlacementRule.PlacementMode.Random
            }, Run.instance.runRNG);
            GameObject gameObject3=r.spawnCard.DoSpawn(LocalUserManager.GetFirstLocalUser().cachedBody.transform.position, Quaternion.identity, r);
            if (gameObject3)
            {
                ChestBehavior component5 = gameObject3.GetComponent<ChestBehavior>();
                



                Chat.AddMessage("spawn chest success");
            }
        }

        //The Awake() method is run at the very start when the game is initialized.
        public void Awake()
        {

            Debug.Log("<color=#13d3dd>MoreHardRPGMod: </color> Loaded Successfully!");
            Debug.Log("Based on ghasttear1's RPGMod,wildbook's Multitudes and JackPendarvesRead's PocketMoney");

            // Refresh values initially
            RefreshConfigValues(true);



            On.RoR2.Run.Start += (orig, self) =>
            {
                isLoaded = true;
                questFirst = true;

                orig(self);


            };

            if (isQuesting)
            {
                On.RoR2.Run.OnClientGameOver += (orig, self, runReport) =>
                {
                    resetUI = true;
                    orig(self, runReport);
                };

                On.RoR2.Run.OnDisable += (orig, self) =>
                {
                    isLoaded = false;
                    serverQuestData = new ServerQuestData();
                    questMessage = new QuestMessage();

                    isClientRegistered = false;

                    CachedCharacterBody = null;

                    if (Notification != null)
                    {
                        Destroy(Notification);
                    }

                    orig(self);
                };

                On.RoR2.Run.OnServerSceneChanged += (orig, self, sceneName) =>
                {
                    questMessage.Initialised = false;
                    stageChange = true;
                    resetUI = true;
                    orig(self, sceneName);
                };
            }

            //if (isBossChests)
            //{
            //    // Edit chest behavior
            //    On.RoR2.ChestBehavior.ItemDrop += (orig, self) =>
            //    {
            //        self.tier2Chance = bossChestChanceUncommon;
            //        self.tier3Chance = bossChestChanceLegendary;
            //        orig(self);
            //    };
            //}

            On.RoR2.SceneDirector.PopulateScene += (orig, self) =>
            {
                int credit = self.GetFieldValue<int>("interactableCredit");
                self.SetFieldValue("interactableCredit", (int)(credit * percentSpawns / MultiplierConfig.Value));
                orig(self);
            };

            On.RoR2.HealthComponent.Suicide += (orig, self, killerOverride, inflictorOverride) =>
            {
                // Debug.Log(self.gameObject.GetComponent<CharacterBody>().master.name);
                if (self.gameObject.GetComponent<CharacterBody>().isBoss || self.gameObject.GetComponent<CharacterBody>().master.name == "EngiTurretMaster(Clone)")
                {
                    isSuicide = true;
                }
                orig(self, killerOverride, inflictorOverride);
            };



            // Death drop hanlder
            On.RoR2.GlobalEventManager.OnCharacterDeath += (orig, self, damageReport) =>
            {
                if (!isSuicide) {
                    float chance;
                    CharacterBody enemyBody = damageReport.victimBody;
                    GameObject attackerMaster = damageReport.damageInfo.attacker.GetComponent<CharacterBody>().masterObject;
                    CharacterMaster attackerController = attackerMaster.GetComponent<CharacterMaster>();

                    if (isQuesting && questMessage.Initialised)
                    {
                        if (enemyBody.GetUserName() == questMessage.Target)
                        {
                            serverQuestData.Progress += 1;
                            CheckQuestStatus();
                            questMessage.Description = GetDescription();
                            SendQuest();
                        }
                    }

                    if (isEnemyDrops)
                    {
                        bool isElite = enemyBody.isElite || enemyBody.isChampion;
                        bool isBoss = enemyBody.isBoss;

                        if (isBoss)
                        {
                            chance = chanceBoss;
                        }
                        else
                        {
                            if (isElite)
                            {
                                chance = chanceElite;
                            }
                            else
                            {
                                chance = chanceNormal;
                            }
                        }

                        chance *= (1f - dropsPlayerScaling + (dropsPlayerScaling * (Run.instance.participatingPlayerCount/ MultiplierConfig.Value)));
                        if (gameStartScaling > Run.instance.difficultyCoefficient )
                        {
                            chance *= (gameStartScaling - (Run.instance.difficultyCoefficient - 1));
                        }
                        if (isElite)
                        {
                            chance = Mathf.Min(chance, chanceEliteMax);
                        }
                        else
                        {
                            chance = Mathf.Min(chance, chanceNormalMax);
                        }

                        // rng check
                        bool didDrop = Util.CheckRoll(chance, luckAffectingDrop ? (attackerController ? attackerController.luck : 0f) : 0f, null);

                        // Gets Item and drops in world
                        if (didDrop && Run.instance.time-lastDropTime>=dropCoolingTime)
                        {
                            InitDropList();
                            lastDropTime = Run.instance.time;
                            if (!isBoss)
                            {
                                // Create a weighted selection for rng
                                WeightedSelection<List<PickupIndex>> weightedSelection = new WeightedSelection<List<PickupIndex>>(8);
                                // Check if enemy is boss, elite or normal
                                if (isElite)
                                {
                                    weightedSelection.AddChoice(availableTier1DropList, eliteChanceTier1);
                                    weightedSelection.AddChoice(availableTier2DropList, eliteChanceTier2);
                                    weightedSelection.AddChoice(availableTier3DropList, eliteChanceTier3);
                                    weightedSelection.AddChoice(availableLunarDropList, eliteChanceTierLunar);
                                }
                                else
                                {
                                    weightedSelection.AddChoice(availableTier1DropList, normalChanceTier1);
                                    weightedSelection.AddChoice(availableTier2DropList, normalChanceTier2);
                                    weightedSelection.AddChoice(availableTier3DropList, normalChanceTier3);
                                    weightedSelection.AddChoice(availableEquipmentDropList, normalChanceTierEquip);
                                }
                                // Get a Tier
                                List<PickupIndex> list = weightedSelection.Evaluate(Run.instance.spawnRng.nextNormalizedFloat);
                                // Pick random from tier
                                PickupIndex item = list[Run.instance.spawnRng.RangeInt(0, list.Count)];
                                
                                

                                // Spawn item
                                PickupDropletController.CreatePickupDroplet(item, enemyBody.transform.position, Vector3.up * 20f);
                            }
                            else
                            {
                                //if (isBossChests)
                                //{
                                //    DropBoss(chest2, damageReport.victim.transform);
                                //}
                            }
                        }
                    }
                }
                else
                {
                    isSuicide = false;
                }
                orig(self, damageReport);
            };

            if (isChests)
            {
                // Handles banned scene spawns
                On.RoR2.ClassicStageInfo.Awake += (orig, self) =>
                {
                    // Gets card catergories using reflection
                    DirectorCardCategorySelection cardSelection = self.GetFieldValue<DirectorCardCategorySelection>("interactableCategories");
                    spawnsList.Clear();
                    for (int i = 0; i < cardSelection.categories.Length; i++)
                    {
                        // Makes copy of category to make changes
                        var cardsCopy = cardSelection.categories[i];
                        foreach (DirectorCard customer in cardsCopy.cards)
                        {
                            spawnsList.Add(customer.spawnCard.prefab.name);
                        }
                        cardsCopy.cards = cardSelection.categories[i].cards.Where(val => !bannedDirectorSpawns.Any(val.spawnCard.prefab.name.ToUpper().Contains)).ToArray();

                        // Sets category to new edited version
                        cardSelection.categories[i] = cardsCopy;
                    }
                    // Sets new card categories
                    self.SetFieldValue("interactableCategories", cardSelection);

                    // Runs original function
                    orig(self);
                };

            }

            On.RoR2.BossGroup.DropRewards += BossGroup_DropRewards;


            On.RoR2.Console.Awake += (orig, self) =>
            {
                CommandHelper.RegisterCommands(self);
                orig(self);
            };

            IL.RoR2.Run.FixedUpdate += il =>
            {
                var c = new ILCursor(il);
                c.GotoNext(x => x.MatchCallvirt<Run>("set_livingPlayerCount"));
                c.EmitDelegate<Func<int, int>>(x => x * Multiplier);

                c.GotoNext(x => x.MatchCallvirt<Run>("set_participatingPlayerCount"));
                c.EmitDelegate<Func<int, int>>(x => x * Multiplier);
            };

            Run.onRunStartGlobal += run => { SendMultiplierChat(); };
            // 传送器的充能时间
            On.RoR2.TeleporterInteraction.GetPlayerCountInRadius += (orig, self) => orig(self) * Multiplier;
            if (dropWhenLevelUp!=-1)
            {
                On.RoR2.GlobalEventManager.OnTeamLevelUp += (orig, self) =>
                {
                    orig(self);

                    if (dropWhenLevelUp == -1 || Run.instance.time - lastLevelUpTime < dropWhenLevelUp)
                        return;
                    lastLevelUpTime = dropWhenLevelUp;
                    PickupIndex item = availableTier1DropList[Run.instance.spawnRng.RangeInt(0, availableTier1DropList.Count)];

                    int connectedPlayers = PlayerCharacterMasterController.instances.Count;
                    for (int i = 0; i < connectedPlayers; i++)
                    {
                        var character = PlayerCharacterMasterController.instances[i].master;
                        if (character.alive)
                        {
                            // Spawn item
                            PickupDropletController.CreatePickupDroplet(item, character.GetBodyObject().transform.position, character.GetBodyObject().transform.forward * 5f + Vector3.up * 20f);
                        }
                    }


                };
            }

            //开局额外金钱
            if(hasPocketMoney)
                On.RoR2.Run.BeginStage += Run_BeginStage;
        }

        private void Run_BeginStage(On.RoR2.Run.orig_BeginStage orig, Run self)
        {
            orig(self);
            var difficultyScaledCost = (uint)Mathf.Round(Run.instance.GetDifficultyScaledCost(25) * StageWeightedMoney.Value);
            var pocketMoney = StageExtraMoney.Value + difficultyScaledCost;
            foreach (var cm in PlayerCharacterMasterController.instances)
            {
                cm.master.GiveMoney(pocketMoney);
            }
        }
        private ItemIndex GetBossDropList()
        {
            if (!randomBossRewards)
            {
                return Run.instance.bossRewardRng.NextElementUniform<PickupIndex>(availableTier2DropList).itemIndex;
            }

            InitDropList();
            WeightedSelection<List<PickupIndex>> weightedSelection = new WeightedSelection<List<PickupIndex>>(8);

            weightedSelection.AddChoice(availableTier2DropList, bossChestChanceLegendary);
            weightedSelection.AddChoice(availableTier3DropList, bossChestChanceUncommon);

            List<PickupIndex> list = weightedSelection.Evaluate(Run.instance.spawnRng.nextNormalizedFloat);

            return Run.instance.bossRewardRng.NextElementUniform<PickupIndex>(list).itemIndex;
        }

        private void BossGroup_DropRewards(On.RoR2.BossGroup.orig_DropRewards orig, BossGroup self)
        {
            int participatingPlayerCount = (int)(Run.instance.participatingPlayerCount / MultiplierConfig.Value);
            if (participatingPlayerCount != 0 && self.dropPosition)
            {
                ItemIndex itemIndex= GetBossDropList();
                int num = participatingPlayerCount * (1 + (TeleporterInteraction.instance ? TeleporterInteraction.instance.shrineBonusStacks : 0));
                float angle = 360f / (float)num;
                Vector3 vector = Quaternion.AngleAxis((float)UnityEngine.Random.Range(0, 360), Vector3.up) * (Vector3.up * 40f + Vector3.forward * 5f);
                Quaternion rotation = Quaternion.AngleAxis(angle, Vector3.up);
                int i = 0;
                List<PickupIndex> bossDrops= self.GetFieldValue<List<PickupIndex>>("bossDrops");
                while (i < num)
                {
                    PickupIndex pickupIndex = new PickupIndex(itemIndex);
                    if (bossDrops.Count > 0 && Run.instance.bossRewardRng.nextNormalizedFloat <= self.bossDropChance)
                    {
                        pickupIndex = Run.instance.bossRewardRng.NextElementUniform<PickupIndex>(bossDrops);
                    }
                    PickupDropletController.CreatePickupDroplet(pickupIndex, self.dropPosition.position, vector);
                    i++;
                    if (randomBossRewards && i < num)
                    {
                        itemIndex = GetBossDropList();
                    }
                    vector = rotation * vector;
                }
            }


        }

        public void Update()
        {
            if (isLoaded)
            {
                // Checks for quest
                if (isQuesting)
                {
                    CheckQuest();
                }

                // Registers Client Handlers
                if (!isClientRegistered)
                {
                    InitClientHanders();
                }

                if (Input.GetKeyDown(KeyCode.F6))
                {
                    RefreshConfigValues(false);
                }

                if (Input.GetKeyDown(KeyCode.F3) && isDebug)
                {
                    DropBoss();
                }

            }
        }
        // Random example command to set multiplier with
        [ConCommand(commandName = "rpg_set_multiplier", flags = ConVarFlags.ExecuteOnServer, helpText = "Lets you pretend to have more friends than you actually do.")]
        private static void CCSetMultiplier(ConCommandArgs args)
        {
            args.CheckArgumentCount(1);

            if (!int.TryParse(args[0], out var multiplier))
            {
                Debug.Log("Invalid argument.");
            }
            else
            {
                if (multiplier > 0)
                {
                    MultiplierConfig.Value = multiplier;
                    Debug.Log($"Multiplier set to {MultiplierConfig.Value}. Good luck!");
                    SendMultiplierChat();
                }
                else
                {
                    Debug.Log("Invalid argument.");
                }
            }
        }

        private static void SendMultiplierChat()
        {
            // If we're not host, we're not setting it for the current lobby
            // That also means no one cares what our Multitudes is set to
            if (!NetworkServer.active)
                return;

            Chat.SendBroadcastChat(
                new Chat.SimpleChatMessage
                {
                    baseToken = "<color=#add8e6>" + questLanguage[5] + ": </color> <color=#ff0000>{0}</color>",
                    paramTokens = new[]
                    {
                        MultiplierConfig.Value.ToString()
                    }
                });
        }

        // Random example command to set multiplier with
        [ConCommand(commandName = "rpg_get_multiplier", flags = ConVarFlags.None, helpText = "Lets you know what Multitudes' multiplier is set to.")]
        private static void CCGetMultiplier(ConCommandArgs args)
        {
            Debug.Log(args.Count != 0
                ? "Invalid arguments. Did you mean mod_wb_set_multiplier?"
                : $"Your multiplier is currently {MultiplierConfig.Value}. Good luck!");
        }

        [ConCommand(commandName = "rpg_show_spawns", flags = ConVarFlags.None, helpText = "Show all can be appeared spawns in this scene")]
        private static void CCShowSpawns(ConCommandArgs args)
        {
            if (spawnsList.Count == 0)
            {
                Debug.Log("please start a scene first");
                return;
            }
            foreach (string name in spawnsList)
            {
                Debug.Log(name);
            }
        }
    
    }
}