﻿using PegasusShared;
using System;
using System.IO;
using System.Reflection;

namespace Hearthrock
{
    using Hearthrock.Contracts;
    using Hearthrock.Configuration;
    using Hearthrock.MiniJson;
    using Hearthrock.Engine;

    /// <summary>
    /// This class is the bridge between Hearthstone and Hearthrock.
    /// HearthrockEngine will used to do the Artificial Intelligence
    /// </summary>
    class HearthrockEngine
    {
        /// <summary>
        /// ranked, vs_ai and so on
        /// </summary>
        private RockGameMode GameMode;
        private DateTime delay_start = DateTime.Now;
        private long delay_length = 0;

        private RockConfiguration configuration;
        private RockRobotClient robotClient;

        public HearthrockEngine()
        {
            this.Reload();
        }

        public void SwitchMode(int mode)
        {
            // do a simple map
            GameMode = (RockGameMode) mode;
        }

        public void Reload()
        {
            var configurationString = File.ReadAllText("Hearthstone_Data\\Managed\\hearthrock.json");
            this.configuration = MiniJsonSerializer.Deserialize<RockConfiguration>(configurationString);
            this.robotClient = new RockRobotClient(this.configuration);
        }

        /// <summary>
        /// called every frame
        /// </summary>
        public void Tick()
        {
            InactivePlayerKicker ipk = InactivePlayerKicker.Get();
            if (ipk == null)
            {
                Trace("InactivePlayerKicker NA");
                return;
            }
            FieldInfo fieldinfo = ipk.GetType().GetField("m_activityDetected", BindingFlags.NonPublic | BindingFlags.Instance);
            fieldinfo.SetValue(ipk, true);
        }

        /// <summary>
        /// this function is called when you want you check sth later
        /// make sure you returned after call this
        /// </summary>
        /// <param name="msec"></param>
        private void HoldBack(long msec)
        {
            delay_start = DateTime.Now;
            delay_length = msec;
        }

        /// <summary>
        /// Display trace info to screen.
        /// </summary>
        /// <param name="message">The trace info.</param>
        public void RockInfo(string message)
        {
            this.Trace(message);
            UIStatus.Get().AddInfo(message);
        }

        /// <summary>
        /// Display trace info to screen.
        /// </summary>
        /// <param name="message">The trace info.</param>
        public void Trace(string message)
        {
            this.robotClient.SendTrace(message);
        }



        SceneMgr.Mode scenemgr_mode = SceneMgr.Mode.INVALID;
        private void LogSceneMode(SceneMgr.Mode mode)
        {
            if (scenemgr_mode != mode)
            {
                scenemgr_mode = mode;
                Trace(scenemgr_mode.ToString());
            }
        }

        /// <summary>
        /// The update method of engine.
        /// Each time the engine will check the state and do responding behavior.
        /// Cannot use yield because user may change states at any time.
        /// </summary>
        public double Update()
        {
            try
            {
                SceneMgr.Mode mode = SceneMgr.Get().GetMode();
                LogSceneMode(mode);

                DateTime current_time = DateTime.Now;
                TimeSpan time_since_delay = current_time - delay_start;
                if (time_since_delay.TotalMilliseconds < delay_length)
                {
                    return 1;
                }
                switch (mode)
                {
                    case SceneMgr.Mode.STARTUP:
                    case SceneMgr.Mode.LOGIN:
                        return 1;
                    case SceneMgr.Mode.DRAFT:
                        Trace("disable for bug");
                        return 1;
                    case SceneMgr.Mode.COLLECTIONMANAGER:
                    case SceneMgr.Mode.PACKOPENING:
                    case SceneMgr.Mode.FRIENDLY:
                    case SceneMgr.Mode.CREDITS:
                        Trace("NEXT MODE" + mode);
                        {
                            if (DialogManager.Get().ShowingDialog())
                            {
                                Trace("ShowingDialog");
                                DialogManager.Get().GoBack();
                            }
                        }
                        SceneMgr.Get().SetNextMode(SceneMgr.Mode.HUB);
                        break;
                    case SceneMgr.Mode.INVALID:
                    case SceneMgr.Mode.FATAL_ERROR:
                    case SceneMgr.Mode.RESET:
                    default:
                        break;
                    case SceneMgr.Mode.HUB:
                        Clear();
                        switch (GameMode)
                        {
                            case RockGameMode.PRACTICE_NORMAL:
                            case RockGameMode.PRACTICE_EXPERT:
                                SceneMgr.Get().SetNextMode(SceneMgr.Mode.ADVENTURE);
                                break;
                            case RockGameMode.PLAY_UNRANKED:
                            case RockGameMode.PLAY_RANKED:
                                SceneMgr.Get().SetNextMode(SceneMgr.Mode.TOURNAMENT);
                                Tournament.Get().NotifyOfBoxTransitionStart();
                                break;
                            default:
                                break;
                        }
                        break;
                    case SceneMgr.Mode.ADVENTURE:
                        ClearGameState();
                        ClearUIQuest();
                        switch (GameMode)
                        {
                            case RockGameMode.PRACTICE_NORMAL:
                                OnRockPraticeMode(false);
                                break;
                            case RockGameMode.PRACTICE_EXPERT:
                                OnRockPraticeMode(true);
                                break;
                            case RockGameMode.PLAY_UNRANKED:
                            case RockGameMode.PLAY_RANKED:
                                SceneMgr.Get().SetNextMode(SceneMgr.Mode.HUB);
                                break;
                            default:
                                break;
                        }
                        break;
                    case SceneMgr.Mode.TOURNAMENT:
                        ClearGameState();
                        ClearUIQuest();
                        switch (GameMode)
                        {
                            case RockGameMode.PRACTICE_NORMAL:
                            case RockGameMode.PRACTICE_EXPERT:
                                SceneMgr.Get().SetNextMode(SceneMgr.Mode.HUB);
                                break;
                            case RockGameMode.PLAY_UNRANKED:
                                OnRockTournamentMode(false);
                                break;
                            case RockGameMode.PLAY_RANKED:
                                OnRockTournamentMode(true);
                                break;
                            default:
                                break;
                        }
                        break;
                    case SceneMgr.Mode.GAMEPLAY:
                        SingletonOnGameRequest = false;
                        return OnRockGamePlay();
                }
            }
            catch (Exception e)
            {
                RockInfo(e.ToString());
            }
            finally
            {
            }

            return 1;
        }

        bool SingletonOnGameRequest = false;

        bool TurnReady = false;
        private double OnRockGamePlay()
        {
            GameState state = GameState.Get();
            if (state == null)
            {
                return 1;
            }
            
            if (state.IsBlockingPowerProcessor())
            {
                Trace("BlockingServer");
                return 0.75;
            }
            else if (state.IsMulliganPhase())
            {
                TurnReady = false;
                return OnRockMulligan();
            }
            else if (state.IsMulliganPhasePending())
            {
                // which means some pending about mulligan
                RockInfo("MulliganPhasePending");
                return 0.75;
            }
            else if (state.IsGameOver())
            {
                Clear();
                return OnRockGameOver();
            }
            else if (state.IsFriendlySidePlayerTurn() == true)
            {
                if (TurnReady)
                {
                    return OnRockAI2();
                }
                else
                {
                    return OnRockTurnStart();
                }
            }
            else
            {
                TurnReady = false;
                return 1;
            }
        }

        private RockActionContext rockActionContext;

        private double OnRockAI2()
        {
            if (EndTurnButton.Get().HasNoMorePlays())
            {
                OnRockTurnEnd();
                this.rockActionContext = null;
                return 0.25;
            }

            if (this.rockActionContext == null || this.rockActionContext.IsDone() || this.rockActionContext.IsInvalid(GameState.Get()))
            {
                var scene = RockSnapshotter.SnapshotScene(GameState.Get());
                var rockAction = this.robotClient.GetAction(scene);
                if (rockAction != null)
                {
                    this.rockActionContext = new RockActionContext(rockAction);
                    RockInfo(this.rockActionContext.Interpretion(GameState.Get()));
                }
                else
                {
                    OnRockTurnEnd();
                    return 0.25;
                }
            }

            this.rockActionContext.Apply(GameState.Get(), this);
            return 1;
        }

        private float OnRockTurnStart()
        {
            TurnReady = true;

            return 5;
        }
        private float OnRockTurnEnd()
        {
            RockInfo("Job's Done!");
            TurnReady = false;
            InputManager.Get().DoEndTurnButton();
            return 3;
        }

        public void Clear()
        {
            SingletonOnGameRequest = false;
            ClearGameState();
            ClearUIQuest();
        }


        private void ClearGameState()
        {
            MulliganState = 0;
            TurnReady = false;
        }

        private void ClearUIQuest()
        {
            try
            {
                WelcomeQuests wq = WelcomeQuests.Get();
                if (wq != null) wq.m_clickCatcher.TriggerRelease();
            }
            catch (Exception e)
            {
                RockInfo(e.ToString());
            }
        }

        private double OnRockGameOver()
        {
            if (EndGameScreen.Get() != null)
            {
                RockInfo("Game Over");
                // EndGameScreen.Get().ContinueEvents();
                try
                {
                    EndGameScreen.Get().m_hitbox.TriggerRelease();
                }
                catch { }
            }

            return 5;
        }
        

        int MulliganState = 0;
        private double OnRockMulligan()
        {
            if (GameState.Get().IsMulliganManagerActive() == false || MulliganManager.Get() == null || MulliganManager.Get().GetMulliganButton() == null || MulliganManager.Get().GetMulliganButton().IsEnabled() == false)
            {
                return 0.5;
            }

            FieldInfo filedinfo = MulliganManager.Get().GetType().GetField("m_waitingForUserInput", BindingFlags.NonPublic | BindingFlags.Instance);
            bool iswaiting = (bool)filedinfo.GetValue(MulliganManager.Get());
            if (!iswaiting)
            {
                return 0.5;
            }
            
            if (MulliganState <= 0)
            {
                RockInfo("Mulligan");
                Card[] cards = GameState.Get().GetFriendlySidePlayer().GetHandZone().GetCards().ToArray();
                foreach (Card current in cards)
                {
                    if (current.GetEntity().GetCardId() == "GAME_005") continue;
                    if (current.GetEntity().GetCost() > 4)
                    {
                        HearthstoneClickCard(current);
                    }
                }
                MulliganState = 1;
                return 1;
            }
            if (MulliganState <= 1)
            {
                MulliganManager.Get().GetMulliganButton().TriggerRelease();
                MulliganState = 2;
                return 5;
            }
            return 0.1;
        }

        private void OnRockTournamentMode(bool ranked)
        {
            if (SingletonOnGameRequest) return;
            SingletonOnGameRequest = true;

            if (SceneMgr.Get().IsInGame() || Network.Get().IsFindingGame())
            {
                HoldBack(1000);
                return;
            }
            if (DeckPickerTrayDisplay.Get() == null)
            {
                HoldBack(1000);
                SingletonOnGameRequest = false;
                return;
            }
            long deck = DeckPickerTrayDisplay.Get().GetSelectedDeckID();
            if (deck == 0)
            {
                HoldBack(1000);
                SingletonOnGameRequest = false;
                return;
            }
            /*
            DeckPickerTrayDisplay.Get().GetSelectedDeckID();

            HoldBack(5000);
            MissionID mission = HearthRockEngine.RandomAIMissionID(expert);

            Notify("PraticeMode: Deck " + deck + "Mission " + mission);
            GameMgr.Get().StartGame(GameMode.PRACTICE, mission, deck);
            GameMgr.Get().UpdatePresence();
             * */

            bool is_ranked = Options.Get().GetBool(Option.IN_RANKED_PLAY_MODE);
            if (is_ranked != ranked)
            {
                Options.Get().SetBool(Option.IN_RANKED_PLAY_MODE, ranked);
                SingletonOnGameRequest = false;
                return;
            }

            long selectedDeckID = DeckPickerTrayDisplay.Get().GetSelectedDeckID();


            //Network.TrackWhat what;
            PegasusShared.GameType type;
            if (ranked)
            {
                //what = Network.TrackWhat.TRACK_PLAY_TOURNAMENT_WITH_CUSTOM_DECK;
                type = PegasusShared.GameType.GT_RANKED;
            }
            else
            {
                //what = Network.TrackWhat.TRACK_PLAY_CASUAL_WITH_CUSTOM_DECK;
                type = PegasusShared.GameType.GT_CASUAL;
            }
            //Network.TrackClient(Network.TrackLevel.LEVEL_INFO, what);


            GameMgr.Get().FindGame(type, FormatType.FT_STANDARD, 2, selectedDeckID, 0L);

            Enum[] args = new Enum[] { PresenceStatus.PLAY_QUEUE };
            PresenceMgr.Get().SetStatus(args);
        }

        private void OnRockPraticeMode(bool expert)
        {
            if (SingletonOnGameRequest) return;
            SingletonOnGameRequest = true;

            if (SceneMgr.Get().IsInGame())
            {
                HoldBack(1000);
                return;
            }
            if (DeckPickerTrayDisplay.Get() == null)
            {
                HoldBack(1000);
                Trace("DeckPickerTrayDisplay.Get() NULL");
                SingletonOnGameRequest = false;
                AdventureDbId adventureId = Options.Get().GetEnum<AdventureDbId>(Option.SELECTED_ADVENTURE, AdventureDbId.PRACTICE);
                AdventureModeDbId modeId = Options.Get().GetEnum<AdventureModeDbId>(Option.SELECTED_ADVENTURE_MODE, AdventureModeDbId.NORMAL);
                if (expert)
                {
                    modeId = Options.Get().GetEnum<AdventureModeDbId>(Option.SELECTED_ADVENTURE_MODE, AdventureModeDbId.EXPERT);
                }
                Trace("AdventureConfig.Get().GetSelectedMode " + AdventureConfig.Get().GetSelectedMode());

                if (AdventureConfig.Get().CanPlayMode(adventureId, modeId))
                {
                    AdventureConfig.Get().SetSelectedAdventureMode(adventureId, modeId);
                    AdventureConfig.Get().ChangeSubSceneToSelectedAdventure();
                }
                else
                {
                    Trace("AdventureConfig.Get().CanPlayMode FALSE");
                }

                return;
            }
            long deck = DeckPickerTrayDisplay.Get().GetSelectedDeckID();
            if (deck == 0)
            {
                HoldBack(1000);
                Trace("DeckPickerTrayDisplay.Get() 0");
                SingletonOnGameRequest = false;
                return;
            }


            AdventureSubScenes currentSubScene = AdventureConfig.Get().GetCurrentSubScene();
            if (currentSubScene == AdventureSubScenes.Practice)
            {
                PracticePickerTrayDisplay.Get().Show();
                HoldBack(3000);
            }
     //       if (currentSubScene == AdventureSubScenes.MissionDeckPicker)
     //       {
     //           GameMgr.Get().FindGame(GameType.GT_VS_AI, formatType, (int)adventureConfig.GetMission(), selectedDeckID3, 0L);
     //       }


            HoldBack(5000);
            ScenarioDbId mission = HearthrockUtils.RandomPracticeMission();


            RockInfo("Mulligan");

            GameMgr.Get().FindGame(PegasusShared.GameType.GT_VS_AI, FormatType.FT_STANDARD, (int)mission, deck, 0L);
        }


        public static void HearthstoneClickCard(Card card)
        {
            InputManager input = InputManager.Get();
            MethodInfo method = input.GetType().GetMethod("HandleClickOnCard", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Invoke(input, new object[] { card.gameObject , true});
        }

    }
}