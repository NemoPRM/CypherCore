﻿/*
 * Copyright (C) 2012-2020 CypherCore <http://github.com/CypherCore>
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using Framework.Constants;
using Framework.Database;
using Game.AI;
using Game.Conditions;
using Game.DataStorage;
using Game.Groups;
using Game.Mails;
using Game.Maps;
using Game.Misc;
using Game.Networking.Packets;
using Game.Spells;
using System;
using System.Collections.Generic;

namespace Game.Entities
{
    public partial class Player
    {
        public uint GetSharedQuestID() { return m_sharedQuestId; }
        public ObjectGuid GetPlayerSharingQuest() { return m_playerSharingQuest; }
        public void SetQuestSharingInfo(ObjectGuid guid, uint id) { m_playerSharingQuest = guid; m_sharedQuestId = id; }
        public void ClearQuestSharingInfo() { m_playerSharingQuest = ObjectGuid.Empty; m_sharedQuestId = 0; }

        uint GetInGameTime() { return m_ingametime; }
        public void SetInGameTime(uint time) { m_ingametime = time; }

        void AddTimedQuest(uint questId) { m_timedquests.Add(questId); }
        public void RemoveTimedQuest(uint questId) { m_timedquests.Remove(questId); }

        public List<uint> GetRewardedQuests() { return m_RewardedQuests; }
        Dictionary<uint, QuestStatusData> GetQuestStatusMap() { return m_QuestStatus; }

        public int GetQuestMinLevel(Quest quest)
        {
            var questLevels = Global.DB2Mgr.GetContentTuningData(quest.ContentTuningId, m_playerData.CtrOptions.GetValue().ContentTuningConditionMask);
            if (questLevels.HasValue)
            {
                ChrRacesRecord race = CliDB.ChrRacesStorage.LookupByKey(GetRace());
                FactionTemplateRecord raceFaction = CliDB.FactionTemplateStorage.LookupByKey(race.FactionID);
                int questFactionGroup = CliDB.ContentTuningStorage.LookupByKey(quest.ContentTuningId).GetScalingFactionGroup();
                if (questFactionGroup != 0 && raceFaction.FactionGroup != questFactionGroup)
                    return questLevels.Value.MaxLevel;

                return questLevels.Value.MinLevelWithDelta;
            }

            return 0;
        }

        public int GetQuestLevel(Quest quest)
        {
            if (quest == null)
                return 0;

            var questLevels = Global.DB2Mgr.GetContentTuningData(quest.ContentTuningId, m_playerData.CtrOptions.GetValue().ContentTuningConditionMask);
            if (questLevels.HasValue)
            {
                int minLevel = GetQuestMinLevel(quest);
                int maxLevel = questLevels.Value.MaxLevel;
                int level = (int)GetLevel();
                if (level >= minLevel)
                    return Math.Min(level, maxLevel);
                return minLevel;
            }

            return 0;
        }

        public int GetRewardedQuestCount() { return m_RewardedQuests.Count; }

        public void LearnQuestRewardedSpells(Quest quest)
        {
            //wtf why is rewardspell a uint if it can me -1
            int spell_id = Convert.ToInt32(quest.RewardSpell);
            uint src_spell_id = quest.SourceSpellID;

            // skip quests without rewarded spell
            if (spell_id == 0)
                return;

            // if RewSpellCast = -1 we remove aura do to SrcSpell from player.
            if (spell_id == -1 && src_spell_id != 0)
            {
                RemoveAurasDueToSpell(src_spell_id);
                return;
            }

            SpellInfo spellInfo = Global.SpellMgr.GetSpellInfo((uint)spell_id, Difficulty.None);
            if (spellInfo == null)
                return;

            // check learned spells state
            bool found = false;
            foreach (var spellEffectInfo in spellInfo.GetEffects())
            {
                if (spellEffectInfo.IsEffect(SpellEffectName.LearnSpell) && !HasSpell(spellEffectInfo.TriggerSpell))
                {
                    found = true;
                    break;
                }
            }

            // skip quests with not teaching spell or already known spell
            if (!found)
                return;

            SpellEffectInfo effect = spellInfo.GetEffect(0);
            uint learned_0 = effect.TriggerSpell;
            if (!HasSpell(learned_0))
            {
                found = false;
                var skills = Global.SpellMgr.GetSkillLineAbilityMapBounds(learned_0);
                foreach (var skillLine in skills)
                {
                    if (skillLine.AcquireMethod == AbilityLearnType.RewardedFromQuest)
                    {
                        found = true;
                        break;
                    }
                }

                // profession specialization can be re-learned from npc
                if (!found)
                    return;
            }

            CastSpell(this, (uint)spell_id, true);
        }

        public void LearnQuestRewardedSpells()
        {
            // learn spells received from quest completing
            foreach (var questId in m_RewardedQuests)
            {
                Quest quest = Global.ObjectMgr.GetQuestTemplate(questId);
                if (quest == null)
                    continue;

                LearnQuestRewardedSpells(quest);
            }
        }

        public void DailyReset()
        {
            foreach (uint questId in m_activePlayerData.DailyQuestsCompleted)
            {
                uint questBit = Global.DB2Mgr.GetQuestUniqueBitFlag(questId);
                if (questBit != 0)
                    SetQuestCompletedBit(questBit, false);
            }

            DailyQuestsReset dailyQuestsReset = new();
            dailyQuestsReset.Count = m_activePlayerData.DailyQuestsCompleted.Size();
            SendPacket(dailyQuestsReset);

            ClearDynamicUpdateFieldValues(m_values.ModifyValue(m_activePlayerData).ModifyValue(m_activePlayerData.DailyQuestsCompleted));

            m_DFQuests.Clear(); // Dungeon Finder Quests.

            // DB data deleted in caller
            m_DailyQuestChanged = false;
            m_lastDailyQuestTime = 0;

            if (_garrison != null)
                _garrison.ResetFollowerActivationLimit();
        }

        public void ResetWeeklyQuestStatus()
        {
            if (m_weeklyquests.Empty())
                return;

            foreach (uint questId in m_weeklyquests)
            {
                uint questBit = Global.DB2Mgr.GetQuestUniqueBitFlag(questId);
                if (questBit != 0)
                    SetQuestCompletedBit(questBit, false);
            }

            m_weeklyquests.Clear();
            // DB data deleted in caller
            m_WeeklyQuestChanged = false;

        }

        public void ResetSeasonalQuestStatus(ushort event_id)
        {
            var eventList = m_seasonalquests.LookupByKey(event_id);
            if (eventList.Empty())
                return;

            foreach (uint questId in eventList)
            {
                uint questBit = Global.DB2Mgr.GetQuestUniqueBitFlag(questId);
                if (questBit != 0)
                    SetQuestCompletedBit(questBit, false);
            }

            m_seasonalquests.Remove(event_id);
            // DB data deleted in caller
            m_SeasonalQuestChanged = false;
        }

        public void ResetMonthlyQuestStatus()
        {
            if (m_monthlyquests.Empty())
                return;

            foreach (uint questId in m_monthlyquests)
            {
                uint questBit = Global.DB2Mgr.GetQuestUniqueBitFlag(questId);
                if (questBit != 0)
                    SetQuestCompletedBit(questBit, false);
            }

            m_monthlyquests.Clear();
            // DB data deleted in caller
            m_MonthlyQuestChanged = false;
        }

        public bool CanInteractWithQuestGiver(WorldObject questGiver)
        {
            switch (questGiver.GetTypeId())
            {
                case TypeId.Unit:
                    return GetNPCIfCanInteractWith(questGiver.GetGUID(), NPCFlags.QuestGiver, NPCFlags2.None) != null;
                case TypeId.GameObject:
                    return GetGameObjectIfCanInteractWith(questGiver.GetGUID(), GameObjectTypes.QuestGiver) != null;
                case TypeId.Player:
                    return IsAlive() && questGiver.ToPlayer().IsAlive();
                case TypeId.Item:
                    return IsAlive();
                default:
                    break;
            }
            return false;
        }

        public bool IsQuestRewarded(uint quest_id)
        {
            return m_RewardedQuests.Contains(quest_id);
        }

        public void PrepareQuestMenu(ObjectGuid guid)
        {
            QuestRelationResult questRelations;
            QuestRelationResult questInvolvedRelations;

            // pets also can have quests
            Creature creature = ObjectAccessor.GetCreatureOrPetOrVehicle(this, guid);
            if (creature != null)
            {
                questRelations = Global.ObjectMgr.GetCreatureQuestRelations(creature.GetEntry());
                questInvolvedRelations = Global.ObjectMgr.GetCreatureQuestInvolvedRelations(creature.GetEntry());
            }
            else
            {
                //we should obtain map from GetMap() in 99% of cases. Special case
                //only for quests which cast teleport spells on player
                Map _map = IsInWorld ? GetMap() : Global.MapMgr.FindMap(GetMapId(), GetInstanceId());
                Cypher.Assert(_map != null);
                GameObject gameObject = _map.GetGameObject(guid);
                if (gameObject != null)
                {
                    questRelations = Global.ObjectMgr.GetGOQuestRelations(gameObject.GetEntry());
                    questInvolvedRelations = Global.ObjectMgr.GetGOQuestInvolvedRelations(gameObject.GetEntry());
                }
                else
                    return;
            }

            QuestMenu qm = PlayerTalkClass.GetQuestMenu();
            qm.ClearMenu();

            foreach (var questId in questInvolvedRelations)
            {
                QuestStatus status = GetQuestStatus(questId);
                if (status == QuestStatus.Complete)
                    qm.AddMenuItem(questId, 4);
                else if (status == QuestStatus.Incomplete)
                    qm.AddMenuItem(questId, 4);
            }

            foreach (var questId in questRelations)
            {
                Quest quest = Global.ObjectMgr.GetQuestTemplate(questId);
                if (quest == null)
                    continue;

                if (!CanTakeQuest(quest, false))
                    continue;

                if (quest.IsAutoComplete() && (!quest.IsRepeatable() || quest.IsDaily() || quest.IsWeekly() || quest.IsMonthly()))
                    qm.AddMenuItem(questId, 0);
                else if (quest.IsAutoComplete())
                    qm.AddMenuItem(questId, 4);
                else if (GetQuestStatus(questId) == QuestStatus.None)
                    qm.AddMenuItem(questId, 2);
            }
        }

        public void SendPreparedQuest(WorldObject source)
        {
            QuestMenu questMenu = PlayerTalkClass.GetQuestMenu();
            if (questMenu.IsEmpty())
                return;

            // single element case
            if (questMenu.GetMenuItemCount() == 1)
            {
                QuestMenuItem qmi0 = questMenu.GetItem(0);
                uint questId = qmi0.QuestId;

                // Auto open
                Quest quest = Global.ObjectMgr.GetQuestTemplate(questId);
                if (quest != null)
                {
                    if (qmi0.QuestIcon == 4)
                        PlayerTalkClass.SendQuestGiverRequestItems(quest, source.GetGUID(), CanRewardQuest(quest, false), true);

                    // Send completable on repeatable and autoCompletable quest if player don't have quest
                    // @todo verify if check for !quest.IsDaily() is really correct (possibly not)
                    else if (!source.HasQuest(questId) && !source.HasInvolvedQuest(questId))
                        PlayerTalkClass.SendCloseGossip();
                    else
                    {
                        if (quest.IsAutoAccept() && CanAddQuest(quest, true) && CanTakeQuest(quest, true))
                            AddQuestAndCheckCompletion(quest, source);

                        if (quest.IsAutoComplete() && quest.IsRepeatable() && !quest.IsDailyOrWeekly())
                            PlayerTalkClass.SendQuestGiverRequestItems(quest, source.GetGUID(), CanCompleteRepeatableQuest(quest), true);
                        else
                            PlayerTalkClass.SendQuestGiverQuestDetails(quest, source.GetGUID(), true, false);

                    }

                    return;
                }
            }

            PlayerTalkClass.SendQuestGiverQuestListMessage(source);
        }

        public bool IsActiveQuest(uint quest_id)
        {
            return m_QuestStatus.ContainsKey(quest_id);
        }

        public Quest GetNextQuest(ObjectGuid guid, Quest quest)
        {
            QuestRelationResult quests;
            uint nextQuestID = quest.NextQuestInChain;

            switch (guid.GetHigh())
            {
                case HighGuid.Player:
                    Cypher.Assert(quest.HasFlag(QuestFlags.AutoComplete));
                    return Global.ObjectMgr.GetQuestTemplate(nextQuestID);
                case HighGuid.Creature:
                case HighGuid.Pet:
                case HighGuid.Vehicle:
                {
                    Creature creature = ObjectAccessor.GetCreatureOrPetOrVehicle(this, guid);
                    if (creature != null)
                        quests = Global.ObjectMgr.GetCreatureQuestRelations(creature.GetEntry());
                    else
                        return null;
                    break;
                }
                case HighGuid.GameObject:
                {
                    //we should obtain map from GetMap() in 99% of cases. Special case
                    //only for quests which cast teleport spells on player
                    Map _map = IsInWorld ? GetMap() : Global.MapMgr.FindMap(GetMapId(), GetInstanceId());
                    Cypher.Assert(_map != null);
                    GameObject gameObject = _map.GetGameObject(guid);
                    if (gameObject != null)
                        quests = Global.ObjectMgr.GetGOQuestRelations(gameObject.GetEntry());
                    else
                        return null;
                    break;
                }
                default:
                    return null;
            }

            if (nextQuestID != 0)
                if (quests.HasQuest(nextQuestID))
                    return Global.ObjectMgr.GetQuestTemplate(nextQuestID);

            return null;
        }

        public bool CanSeeStartQuest(Quest quest)
        {
            if (!Global.DisableMgr.IsDisabledFor(DisableType.Quest, quest.Id, this) && SatisfyQuestClass(quest, false) && SatisfyQuestRace(quest, false) &&
                SatisfyQuestSkill(quest, false) && SatisfyQuestExclusiveGroup(quest, false) && SatisfyQuestReputation(quest, false) &&
                SatisfyQuestDependentQuests(quest, false) && SatisfyQuestDay(quest, false) && SatisfyQuestWeek(quest, false) &&
                SatisfyQuestMonth(quest, false) && SatisfyQuestSeasonal(quest, false))
            {
                return GetLevel() + WorldConfig.GetIntValue(WorldCfg.QuestHighLevelHideDiff) >= GetQuestMinLevel(quest);
            }

            return false;
        }

        public bool CanTakeQuest(Quest quest, bool msg)
        {
            return !Global.DisableMgr.IsDisabledFor(DisableType.Quest, quest.Id, this)
                && SatisfyQuestStatus(quest, msg) && SatisfyQuestExclusiveGroup(quest, msg)
                && SatisfyQuestClass(quest, msg) && SatisfyQuestRace(quest, msg) && SatisfyQuestLevel(quest, msg)
                && SatisfyQuestSkill(quest, msg) && SatisfyQuestReputation(quest, msg)
                && SatisfyQuestDependentQuests(quest, msg) && SatisfyQuestTimed(quest, msg)
                && SatisfyQuestDay(quest, msg)
                && SatisfyQuestWeek(quest, msg) && SatisfyQuestMonth(quest, msg)
                && SatisfyQuestSeasonal(quest, msg) && SatisfyQuestConditions(quest, msg);
        }

        public bool CanAddQuest(Quest quest, bool msg)
        {
            if (!SatisfyQuestLog(msg))
                return false;

            uint srcitem = quest.SourceItemId;
            if (srcitem > 0)
            {
                uint count = quest.SourceItemIdCount;
                List<ItemPosCount> dest = new();
                InventoryResult msg2 = CanStoreNewItem(ItemConst.NullBag, ItemConst.NullSlot, dest, srcitem, count);

                // player already have max number (in most case 1) source item, no additional item needed and quest can be added.
                if (msg2 == InventoryResult.ItemMaxCount)
                    return true;

                if (msg2 != InventoryResult.Ok)
                {
                    SendEquipError(msg2, null, null, srcitem);
                    return false;
                }
            }
            return true;
        }

        public bool CanCompleteQuest(uint questId, uint ignoredQuestObjectiveId = 0)
        {
            if (questId != 0)
            {
                Quest qInfo = Global.ObjectMgr.GetQuestTemplate(questId);
                if (qInfo == null)
                    return false;

                if (!qInfo.IsRepeatable() && GetQuestRewardStatus(questId))
                    return false;                                   // not allow re-complete quest

                // auto complete quest
                if (qInfo.IsAutoComplete() && CanTakeQuest(qInfo, false))
                    return true;

                var q_status = m_QuestStatus.LookupByKey(questId);
                if (q_status == null)
                    return false;

                if (q_status.Status == QuestStatus.Incomplete)
                {
                    foreach (QuestObjective obj in qInfo.Objectives)
                    {
                        if (ignoredQuestObjectiveId != 0 && obj.Id == ignoredQuestObjectiveId)
                            continue;

                        if (!obj.Flags.HasAnyFlag(QuestObjectiveFlags.Optional) && !obj.Flags.HasAnyFlag(QuestObjectiveFlags.PartOfProgressBar))
                        {
                            if (!IsQuestObjectiveComplete(q_status.Slot, qInfo, obj))
                                return false;
                        }
                    }

                    if (qInfo.HasSpecialFlag(QuestSpecialFlags.ExplorationOrEvent) && !q_status.Explored)
                        return false;

                    if (qInfo.LimitTime != 0 && q_status.Timer == 0)
                        return false;

                    return true;
                }
            }
            return false;
        }

        public bool CanCompleteRepeatableQuest(Quest quest)
        {
            // Solve problem that player don't have the quest and try complete it.
            // if repeatable she must be able to complete event if player don't have it.
            // Seem that all repeatable quest are DELIVER Flag so, no need to add more.
            if (!CanTakeQuest(quest, false))
                return false;

            if (quest.HasQuestObjectiveType(QuestObjectiveType.Item))
                foreach (QuestObjective obj in quest.Objectives)
                    if (obj.Type == QuestObjectiveType.Item && !HasItemCount((uint)obj.ObjectID, (uint)obj.Amount))
                        return false;

            if (!CanRewardQuest(quest, false))
                return false;

            return true;
        }

        public bool CanRewardQuest(Quest quest, bool msg)
        {
            // quest is disabled
            if (Global.DisableMgr.IsDisabledFor(DisableType.Quest, quest.Id, this))
                return false;

            // not auto complete quest and not completed quest (only cheating case, then ignore without message)
            if (!quest.IsDFQuest() && !quest.IsAutoComplete() && GetQuestStatus(quest.Id) != QuestStatus.Complete)
                return false;

            // daily quest can't be rewarded (25 daily quest already completed)
            if (!SatisfyQuestDay(quest, msg) || !SatisfyQuestWeek(quest, msg) || !SatisfyQuestMonth(quest, msg) || !SatisfyQuestSeasonal(quest, msg))
                return false;

            // player no longer satisfies the quest's requirements (skill level etc.)
            if (!SatisfyQuestLevel(quest, msg) || !SatisfyQuestSkill(quest, msg) || !SatisfyQuestReputation(quest, msg))
                return false;

            // rewarded and not repeatable quest (only cheating case, then ignore without message)
            if (GetQuestRewardStatus(quest.Id))
                return false;

            // prevent receive reward with quest items in bank
            if (quest.HasQuestObjectiveType(QuestObjectiveType.Item))
            {
                foreach (QuestObjective obj in quest.Objectives)
                {
                    if (obj.Type != QuestObjectiveType.Item)
                        continue;

                    if (GetItemCount((uint)obj.ObjectID) < obj.Amount)
                    {
                        if (msg)
                            SendEquipError(InventoryResult.ItemNotFound, null, null, (uint)obj.ObjectID);
                        return false;
                    }
                }
            }

            foreach (QuestObjective obj in quest.Objectives)
            {
                switch (obj.Type)
                {
                    case QuestObjectiveType.Currency:
                        if (!HasCurrency((uint)obj.ObjectID, (uint)obj.Amount))
                            return false;
                        break;
                    case QuestObjectiveType.Money:
                        if (!HasEnoughMoney(obj.Amount))
                            return false;
                        break;
                }
            }

            return true;
        }

        public bool CanRewardQuest(Quest quest, LootItemType rewardType, uint rewardId, bool msg)
        {
            List<ItemPosCount> dest = new();
            if (quest.GetRewChoiceItemsCount() > 0)
            {
                switch (rewardType)
                {
                    case LootItemType.Item:
                        for (uint i = 0; i < SharedConst.QuestRewardChoicesCount; ++i)
                        {
                            if (quest.RewardChoiceItemId[i] != 0 && quest.RewardChoiceItemType[i] == LootItemType.Item && quest.RewardChoiceItemId[i] == rewardId)
                            {
                                InventoryResult res = CanStoreNewItem(ItemConst.NullBag, ItemConst.NullSlot, dest, quest.RewardChoiceItemId[i], quest.RewardChoiceItemCount[i]);
                                if (res != InventoryResult.Ok)
                                {
                                    if (msg)
                                        SendQuestFailed(quest.Id, res);

                                    return false;
                                }
                            }
                        }
                        break;
                    case LootItemType.Currency:
                        break;
                }
            }

            if (quest.GetRewItemsCount() > 0)
            {
                for (uint i = 0; i < quest.GetRewItemsCount(); ++i)
                {
                    if (quest.RewardItemId[i] != 0)
                    {
                        InventoryResult res = CanStoreNewItem(ItemConst.NullBag, ItemConst.NullSlot, dest, quest.RewardItemId[i], quest.RewardItemCount[i]);
                        if (res != InventoryResult.Ok)
                        {
                            if (msg)
                                SendQuestFailed(quest.Id, res);

                            return false;
                        }
                    }
                }
            }

            // QuestPackageItem.db2
            if (quest.PackageID != 0)
            {
                bool hasFilteredQuestPackageReward = false;
                var questPackageItems = Global.DB2Mgr.GetQuestPackageItems(quest.PackageID);
                if (questPackageItems != null)
                {
                    foreach (var questPackageItem in questPackageItems)
                    {
                        if (questPackageItem.ItemID != rewardId)
                            continue;

                        if (CanSelectQuestPackageItem(questPackageItem))
                        {
                            hasFilteredQuestPackageReward = true;
                            InventoryResult res = CanStoreNewItem(ItemConst.NullBag, ItemConst.NullSlot, dest, questPackageItem.ItemID, questPackageItem.ItemQuantity);
                            if (res != InventoryResult.Ok)
                            {
                                SendEquipError(res, null, null, questPackageItem.ItemID);
                                return false;
                            }
                        }
                    }
                }

                if (!hasFilteredQuestPackageReward)
                {
                    List<QuestPackageItemRecord> questPackageItems1 = Global.DB2Mgr.GetQuestPackageItemsFallback(quest.PackageID);
                    if (questPackageItems1 != null)
                    {
                        foreach (QuestPackageItemRecord questPackageItem in questPackageItems1)
                        {
                            if (questPackageItem.ItemID != rewardId)
                                continue;

                            InventoryResult res = CanStoreNewItem(ItemConst.NullBag, ItemConst.NullSlot, dest, questPackageItem.ItemID, questPackageItem.ItemQuantity);
                            if (res != InventoryResult.Ok)
                            {
                                SendEquipError(res, null, null, questPackageItem.ItemID);
                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        }

        public void AddQuestAndCheckCompletion(Quest quest, WorldObject questGiver)
        {
            AddQuest(quest, questGiver);

            foreach (QuestObjective obj in quest.Objectives)
                if (obj.Type == QuestObjectiveType.CriteriaTree)
                    if (m_questObjectiveCriteriaMgr.HasCompletedObjective(obj))
                        KillCreditCriteriaTreeObjective(obj);

            if (CanCompleteQuest(quest.Id))
                CompleteQuest(quest.Id);

            if (!questGiver)
                return;

            switch (questGiver.GetTypeId())
            {
                case TypeId.Unit:
                    PlayerTalkClass.ClearMenus();
                    questGiver.ToCreature().GetAI().QuestAccept(this, quest);
                    break;
                case TypeId.Item:
                case TypeId.Container:
                case TypeId.AzeriteItem:
                case TypeId.AzeriteEmpoweredItem:
                {
                    Item item = (Item)questGiver;
                    Global.ScriptMgr.OnQuestAccept(this, item, quest);

                    // There are two cases where the source item is not destroyed when the quest is accepted:
                    // - It is required to finish the quest, and is an unique item
                    // - It is the same item present in the source item field (item that would be given on quest accept)
                    bool destroyItem = true;
                    foreach (QuestObjective obj in quest.Objectives)
                    {
                        if (obj.Type == QuestObjectiveType.Item && obj.ObjectID == item.GetEntry() && item.GetTemplate().GetMaxCount() > 0)
                        {
                            destroyItem = false;
                            break;
                        }
                    }

                    if (quest.SourceItemId == item.GetEntry())
                        destroyItem = false;

                    if (destroyItem)
                        DestroyItem(item.GetBagSlot(), item.GetSlot(), true);

                    break;
                }
                case TypeId.GameObject:
                    PlayerTalkClass.ClearMenus();
                    questGiver.ToGameObject().GetAI().QuestAccept(this, quest);
                    break;
                default:
                    break;
            }
        }

        public void AddQuest(Quest quest, WorldObject questGiver)
        {
            ushort logSlot = FindQuestSlot(0);
            if (logSlot >= SharedConst.MaxQuestLogSize) // Player does not have any free slot in the quest log
                return;

            uint questId = quest.Id;

            // if not exist then created with set uState == NEW and rewarded=false
            if (!m_QuestStatus.ContainsKey(questId))
                m_QuestStatus[questId] = new QuestStatusData();

            QuestStatusData questStatusData = m_QuestStatus.LookupByKey(questId);
            QuestStatus oldStatus = questStatusData.Status;

            // check for repeatable quests status reset
            SetQuestSlot(logSlot, questId);
            questStatusData.Slot = logSlot;
            questStatusData.Status = QuestStatus.Incomplete;
            questStatusData.Explored = false;

            foreach (QuestObjective obj in quest.Objectives)
            {
                m_questObjectiveStatus.Add((obj.Type, obj.ObjectID), new QuestObjectiveStatusData() { QuestStatusPair = (questId, questStatusData), Objective = obj });
                switch (obj.Type)
                {
                    case QuestObjectiveType.MinReputation:
                    case QuestObjectiveType.MaxReputation:
                        FactionRecord factionEntry = CliDB.FactionStorage.LookupByKey(obj.ObjectID);
                        if (factionEntry != null)
                            GetReputationMgr().SetVisible(factionEntry);
                        break;
                    case QuestObjectiveType.CriteriaTree:
                        m_questObjectiveCriteriaMgr.ResetCriteriaTree((uint)obj.ObjectID);
                        break;
                    default:
                        break;
                }
            }

            GiveQuestSourceItem(quest);
            AdjustQuestObjectiveProgress(quest);

            long endTime = 0;
            uint limittime = quest.LimitTime;
            if (limittime != 0)
            {
                // shared timed quest
                if (questGiver != null && questGiver.IsTypeId(TypeId.Player))
                    limittime = questGiver.ToPlayer().m_QuestStatus[questId].Timer / Time.InMilliseconds;

                AddTimedQuest(questId);
                questStatusData.Timer = limittime * Time.InMilliseconds;
                endTime = GameTime.GetGameTime() + limittime;
            }
            else
                questStatusData.Timer = 0;

            if (quest.HasFlag(QuestFlags.Pvp))
            {
                pvpInfo.IsHostile = true;
                UpdatePvPState();
            }

            if (quest.SourceSpellID > 0)
            {
                SpellInfo spellInfo = Global.SpellMgr.GetSpellInfo(quest.SourceSpellID, GetMap().GetDifficultyID());
                Unit caster = this;
                if (questGiver != null && questGiver.IsTypeMask(TypeMask.Unit) && !quest.HasFlag(QuestFlags.PlayerCastOnAccept) && !spellInfo.HasTargetType(Targets.UnitCaster) && !spellInfo.HasTargetType(Targets.DestCasterSummon))
                {
                    Unit unit = questGiver.ToUnit();
                    if (unit != null)
                        caster = unit;
                }

                caster.CastSpell(this, spellInfo.Id, new CastSpellExtraArgs(TriggerCastFlags.FullMask).SetCastDifficulty(spellInfo.Difficulty));
            }

            SetQuestSlotEndTime(logSlot, endTime);
            SetQuestSlotAcceptTime(logSlot, GameTime.GetGameTime());

            m_QuestStatusSave[questId] = QuestSaveType.Default;

            StartCriteriaTimer(CriteriaStartEvent.AcceptQuest, questId);

            SendQuestUpdate(questId);

            Global.ScriptMgr.OnQuestStatusChange(this, questId);
            Global.ScriptMgr.OnQuestStatusChange(this, quest, oldStatus, questStatusData.Status);
        }

        public void CompleteQuest(uint quest_id)
        {
            if (quest_id != 0)
            {
                SetQuestStatus(quest_id, QuestStatus.Complete);

                QuestStatusData questStatus = m_QuestStatus.LookupByKey(quest_id);
                if (questStatus != null)
                    SetQuestSlotState(questStatus.Slot, QuestSlotStateMask.Complete);

                Quest qInfo = Global.ObjectMgr.GetQuestTemplate(quest_id);
                if (qInfo != null)
                    if (qInfo.HasFlag(QuestFlags.Tracking))
                        RewardQuest(qInfo, LootItemType.Item, 0, this, false);
            }
        }

        public void IncompleteQuest(uint quest_id)
        {
            if (quest_id != 0)
            {
                SetQuestStatus(quest_id, QuestStatus.Incomplete);

                ushort log_slot = FindQuestSlot(quest_id);
                if (log_slot < SharedConst.MaxQuestLogSize)
                    RemoveQuestSlotState(log_slot, QuestSlotStateMask.Complete);
            }
        }

        public uint GetQuestMoneyReward(Quest quest)
        {
            return (uint)(quest.MoneyValue(this) * WorldConfig.GetFloatValue(WorldCfg.RateMoneyQuest));
        }

        public uint GetQuestXPReward(Quest quest)
        {
            bool rewarded = IsQuestRewarded(quest.Id) && !quest.IsDFQuest();

            // Not give XP in case already completed once repeatable quest
            if (rewarded)
                return 0;

            uint XP = (uint)(quest.XPValue(this) * WorldConfig.GetFloatValue(WorldCfg.RateXpQuest));

            // handle SPELL_AURA_MOD_XP_QUEST_PCT auras
            var ModXPPctAuras = GetAuraEffectsByType(AuraType.ModXpQuestPct);
            foreach (var eff in ModXPPctAuras)
                MathFunctions.AddPct(ref XP, eff.GetAmount());

            return XP;
        }

        public bool CanSelectQuestPackageItem(QuestPackageItemRecord questPackageItem)
        {
            ItemTemplate rewardProto = Global.ObjectMgr.GetItemTemplate(questPackageItem.ItemID);
            if (rewardProto == null)
                return false;

            if ((rewardProto.HasFlag(ItemFlags2.FactionAlliance) && GetTeam() != Team.Alliance) ||
                (rewardProto.HasFlag(ItemFlags2.FactionHorde) && GetTeam() != Team.Horde))
                return false;

            switch (questPackageItem.DisplayType)
            {
                case QuestPackageFilter.LootSpecialization:
                    return rewardProto.IsUsableByLootSpecialization(this, true);
                case QuestPackageFilter.Class:
                    return rewardProto.ItemSpecClassMask == 0 || (rewardProto.ItemSpecClassMask & GetClassMask()) != 0;
                case QuestPackageFilter.Everyone:
                    return true;
                default:
                    break;
            }

            return false;
        }

        public void RewardQuestPackage(uint questPackageId, uint onlyItemId = 0)
        {
            bool hasFilteredQuestPackageReward = false;
            var questPackageItems = Global.DB2Mgr.GetQuestPackageItems(questPackageId);
            if (questPackageItems != null)
            {
                foreach (QuestPackageItemRecord questPackageItem in questPackageItems)
                {
                    if (onlyItemId != 0 && questPackageItem.ItemID != onlyItemId)
                        continue;

                    if (CanSelectQuestPackageItem(questPackageItem))
                    {
                        hasFilteredQuestPackageReward = true;
                        List<ItemPosCount> dest = new();
                        if (CanStoreNewItem(ItemConst.NullBag, ItemConst.NullSlot, dest, questPackageItem.ItemID, questPackageItem.ItemQuantity) == InventoryResult.Ok)
                        {
                            Item item = StoreNewItem(dest, questPackageItem.ItemID, true, ItemEnchantmentManager.GenerateItemRandomBonusListId(questPackageItem.ItemID));
                            SendNewItem(item, questPackageItem.ItemQuantity, true, false);
                        }
                    }
                }
            }

            if (!hasFilteredQuestPackageReward)
            {
                var questPackageItemsFallback = Global.DB2Mgr.GetQuestPackageItemsFallback(questPackageId);
                if (questPackageItemsFallback != null)
                {
                    foreach (QuestPackageItemRecord questPackageItem in questPackageItemsFallback)
                    {
                        if (onlyItemId != 0 && questPackageItem.ItemID != onlyItemId)
                            continue;

                        List<ItemPosCount> dest = new();
                        if (CanStoreNewItem(ItemConst.NullBag, ItemConst.NullSlot, dest, questPackageItem.ItemID, questPackageItem.ItemQuantity) == InventoryResult.Ok)
                        {
                            Item item = StoreNewItem(dest, questPackageItem.ItemID, true, ItemEnchantmentManager.GenerateItemRandomBonusListId(questPackageItem.ItemID));
                            SendNewItem(item, questPackageItem.ItemQuantity, true, false);
                        }
                    }
                }
            }
        }

        public void RewardQuest(Quest quest, LootItemType rewardType, uint rewardId, WorldObject questGiver, bool announce = true)
        {
            //this THING should be here to protect code from quest, which cast on player far teleport as a reward
            //should work fine, cause far teleport will be executed in Update()
            SetCanDelayTeleport(true);

            uint questId = quest.Id;
            QuestStatus oldStatus = GetQuestStatus(questId);

            foreach (QuestObjective obj in quest.Objectives)
            {
                switch (obj.Type)
                {
                    case QuestObjectiveType.Item:
                        DestroyItemCount((uint)obj.ObjectID, (uint)obj.Amount, true);
                        break;
                    case QuestObjectiveType.Currency:
                        ModifyCurrency((CurrencyTypes)obj.ObjectID, -obj.Amount, false, true);
                        break;
                }
            }

            if (!quest.FlagsEx.HasAnyFlag(QuestFlagsEx.KeepAdditionalItems))
            {
                for (byte i = 0; i < SharedConst.QuestItemDropCount; ++i)
                {
                    if (quest.ItemDrop[i] != 0)
                    {
                        uint count = quest.ItemDropQuantity[i];
                        DestroyItemCount(quest.ItemDrop[i], count != 0 ? count : 9999, true);
                    }
                }
            }

            RemoveTimedQuest(questId);

            if (quest.GetRewItemsCount() > 0)
            {
                for (uint i = 0; i < quest.GetRewItemsCount(); ++i)
                {
                    uint itemId = quest.RewardItemId[i];
                    if (itemId != 0)
                    {
                        List<ItemPosCount> dest = new();
                        if (CanStoreNewItem(ItemConst.NullBag, ItemConst.NullSlot, dest, itemId, quest.RewardItemCount[i]) == InventoryResult.Ok)
                        {
                            Item item = StoreNewItem(dest, itemId, true, ItemEnchantmentManager.GenerateItemRandomBonusListId(itemId));
                            SendNewItem(item, quest.RewardItemCount[i], true, false);
                        }
                        else if (quest.IsDFQuest())
                            SendItemRetrievalMail(itemId, quest.RewardItemCount[i], ItemContext.QuestReward);
                    }
                }
            }

            switch (rewardType)
            {
                case LootItemType.Item:
                    ItemTemplate rewardProto = Global.ObjectMgr.GetItemTemplate(rewardId);
                    if (rewardProto != null && quest.GetRewChoiceItemsCount() != 0)
                    {
                        for (uint i = 0; i < SharedConst.QuestRewardChoicesCount; ++i)
                        {
                            if (quest.RewardChoiceItemId[i] != 0 && quest.RewardChoiceItemType[i] == LootItemType.Item && quest.RewardChoiceItemId[i] == rewardId)
                            {
                                List<ItemPosCount> dest = new();
                                if (CanStoreNewItem(ItemConst.NullBag, ItemConst.NullSlot, dest, rewardId, quest.RewardChoiceItemCount[i]) == InventoryResult.Ok)
                                {
                                    Item item = StoreNewItem(dest, rewardId, true, ItemEnchantmentManager.GenerateItemRandomBonusListId(rewardId));
                                    SendNewItem(item, quest.RewardChoiceItemCount[i], true, false);
                                }
                            }
                        }
                    }


                    // QuestPackageItem.db2
                    if (rewardProto != null && quest.PackageID != 0)
                        RewardQuestPackage(quest.PackageID, rewardId);
                    break;
                case LootItemType.Currency:
                    if (CliDB.CurrencyTypesStorage.HasRecord(rewardId) && quest.GetRewChoiceItemsCount() != 0)
                    {
                        for (uint i = 0; i < SharedConst.QuestRewardChoicesCount; ++i)
                            if (quest.RewardChoiceItemId[i] != 0 && quest.RewardChoiceItemType[i] == LootItemType.Currency && quest.RewardChoiceItemId[i] == rewardId)
                                ModifyCurrency((CurrencyTypes)quest.RewardChoiceItemId[i], (int)quest.RewardChoiceItemCount[i]);
                    }
                    break;
            }

            for (byte i = 0; i < SharedConst.QuestRewardCurrencyCount; ++i)
            {
                if (quest.RewardCurrencyId[i] != 0)
                    ModifyCurrency((CurrencyTypes)quest.RewardCurrencyId[i], (int)quest.RewardCurrencyCount[i]);
            }

            uint skill = quest.RewardSkillId;
            if (skill != 0)
                UpdateSkillPro(skill, 1000, quest.RewardSkillPoints);

            ushort log_slot = FindQuestSlot(questId);
            if (log_slot < SharedConst.MaxQuestLogSize)
                SetQuestSlot(log_slot, 0);

            uint XP = GetQuestXPReward(quest);

            int moneyRew = 0;
            if (GetLevel() < WorldConfig.GetIntValue(WorldCfg.MaxPlayerLevel))
                GiveXP(XP, null);
            else
                moneyRew = (int)(quest.GetRewMoneyMaxLevel() * WorldConfig.GetFloatValue(WorldCfg.RateDropMoney));

            moneyRew += (int)GetQuestMoneyReward(quest);

            if (moneyRew != 0)
            {
                ModifyMoney(moneyRew);

                if (moneyRew > 0)
                    UpdateCriteria(CriteriaType.MoneyEarnedFromQuesting, (uint)moneyRew);
            }

            // honor reward
            uint honor = quest.CalculateHonorGain(GetLevel());
            if (honor != 0)
                RewardHonor(null, 0, (int)honor);

            // title reward
            if (quest.RewardTitleId != 0)
            {
                CharTitlesRecord titleEntry = CliDB.CharTitlesStorage.LookupByKey(quest.RewardTitleId);
                if (titleEntry != null)
                    SetTitle(titleEntry);
            }

            // Send reward mail
            uint mail_template_id = quest.RewardMailTemplateId;
            if (mail_template_id != 0)
            {
                SQLTransaction trans = new();
                // @todo Poor design of mail system
                uint questMailSender = quest.RewardMailSenderEntry;
                if (questMailSender != 0)
                    new MailDraft(mail_template_id).SendMailTo(trans, this, new MailSender(questMailSender), MailCheckMask.HasBody, quest.RewardMailDelay);
                else
                    new MailDraft(mail_template_id).SendMailTo(trans, this, new MailSender(questGiver), MailCheckMask.HasBody, quest.RewardMailDelay);
                DB.Characters.CommitTransaction(trans);
            }

            if (quest.IsDaily() || quest.IsDFQuest())
            {
                SetDailyQuestStatus(questId);
                if (quest.IsDaily())
                {
                    UpdateCriteria(CriteriaType.CompleteDailyQuest, questId);
                    UpdateCriteria(CriteriaType.CompleteAnyDailyQuestPerDay, questId);
                }
            }
            else if (quest.IsWeekly())
                SetWeeklyQuestStatus(questId);
            else if (quest.IsMonthly())
                SetMonthlyQuestStatus(questId);
            else if (quest.IsSeasonal())
                SetSeasonalQuestStatus(questId);

            RemoveActiveQuest(questId, false);
            if (quest.CanIncreaseRewardedQuestCounters())
                SetRewardedQuest(questId);

            SendQuestReward(quest, questGiver?.ToCreature(), XP, !announce);

            RewardReputation(quest);

            // cast spells after mark quest complete (some spells have quest completed state requirements in spell_area data)
            if (quest.RewardSpell > 0)
            {
                SpellInfo spellInfo = Global.SpellMgr.GetSpellInfo(quest.RewardSpell, GetMap().GetDifficultyID());
                Unit caster = this;
                if (questGiver != null && questGiver.IsTypeMask(TypeMask.Unit) && !quest.HasFlag(QuestFlags.PlayerCastOnComplete) && !spellInfo.HasTargetType(Targets.UnitCaster))
                {
                    Unit unit = questGiver.ToUnit();
                    if (unit != null)
                        caster = unit;
                }

                caster.CastSpell(this, spellInfo.Id, new CastSpellExtraArgs(TriggerCastFlags.FullMask).SetCastDifficulty(spellInfo.Difficulty));
            }
            else
            {
                foreach (QuestRewardDisplaySpell displaySpell in quest.RewardDisplaySpell)
                {
                    var playerCondition = CliDB.PlayerConditionStorage.LookupByKey(displaySpell.PlayerConditionId);
                    if (playerCondition != null)
                        if (!ConditionManager.IsPlayerMeetingCondition(this, playerCondition))
                            continue;

                    SpellInfo spellInfo = Global.SpellMgr.GetSpellInfo(displaySpell.SpellId, GetMap().GetDifficultyID());
                    Unit caster = this;
                    if (questGiver && questGiver.IsTypeMask(TypeMask.Unit) && !quest.HasFlag(QuestFlags.PlayerCastOnComplete) && !spellInfo.HasTargetType(Targets.UnitCaster))
                    {
                        Unit unit = questGiver.ToUnit();
                        if (unit)
                            caster = unit;
                    }

                    caster.CastSpell(this, spellInfo.Id, new CastSpellExtraArgs(TriggerCastFlags.FullMask).SetCastDifficulty(spellInfo.Difficulty));
                }
            }

            if (quest.QuestSortID > 0)
                UpdateCriteria(CriteriaType.CompleteQuestsInZone, quest.Id);

            UpdateCriteria(CriteriaType.CompleteQuestsCount);
            UpdateCriteria(CriteriaType.CompleteQuest, quest.Id);
            UpdateCriteria(CriteriaType.CompleteAnyReplayQuest, 1);

            // make full db save
            SaveToDB(false);

            uint questBit = Global.DB2Mgr.GetQuestUniqueBitFlag(questId);
            if (questBit != 0)
                SetQuestCompletedBit(questBit, true);

            if (quest.HasFlag(QuestFlags.Pvp))
            {
                pvpInfo.IsHostile = pvpInfo.IsInHostileArea || HasPvPForcingQuest();
                UpdatePvPState();
            }

            SendQuestUpdate(questId);
            SendQuestGiverStatusMultiple();

            //lets remove flag for delayed teleports
            SetCanDelayTeleport(false);

            Global.ScriptMgr.OnQuestStatusChange(this, questId);
            Global.ScriptMgr.OnQuestStatusChange(this, quest, oldStatus, QuestStatus.Rewarded);
        }

        public void SetRewardedQuest(uint quest_id)
        {
            m_RewardedQuests.Add(quest_id);
            m_RewardedQuestsSave[quest_id] = QuestSaveType.Default;
        }

        public void FailQuest(uint questId)
        {
            Quest quest = Global.ObjectMgr.GetQuestTemplate(questId);
            if (quest != null)
            {
                QuestStatus qStatus = GetQuestStatus(questId);

                // we can only fail incomplete quest or...
                if (qStatus != QuestStatus.Incomplete)
                {
                    // completed timed quest with no requirements
                    if (qStatus != QuestStatus.Complete || quest.LimitTime == 0 || !quest.Objectives.Empty())
                        return;
                }

                SetQuestStatus(questId, QuestStatus.Failed);

                ushort log_slot = FindQuestSlot(questId);

                if (log_slot < SharedConst.MaxQuestLogSize)
                    SetQuestSlotState(log_slot, QuestSlotStateMask.Fail);

                if (quest.LimitTime != 0)
                {
                    QuestStatusData q_status = m_QuestStatus[questId];

                    RemoveTimedQuest(questId);
                    q_status.Timer = 0;

                    SendQuestTimerFailed(questId);
                }
                else
                    SendQuestFailed(questId);

                // Destroy quest items on quest failure.
                foreach (QuestObjective obj in quest.Objectives)
                {
                    if (obj.Type == QuestObjectiveType.Item)
                    {
                        ItemTemplate itemTemplate = Global.ObjectMgr.GetItemTemplate((uint)obj.ObjectID);
                        if (itemTemplate != null)
                            if (itemTemplate.GetBonding() == ItemBondingType.Quest)
                                DestroyItemCount((uint)obj.ObjectID, (uint)obj.Amount, true, true);
                    }
                }

                // Destroy items received during the quest.
                for (byte i = 0; i < SharedConst.QuestItemDropCount; ++i)
                {
                    ItemTemplate itemTemplate = Global.ObjectMgr.GetItemTemplate(quest.ItemDrop[i]);
                    if (itemTemplate != null)
                        if (quest.ItemDropQuantity[i] != 0 && itemTemplate.GetBonding() == ItemBondingType.Quest)
                            DestroyItemCount(quest.ItemDrop[i], quest.ItemDropQuantity[i], true, true);
                }
            }
        }

        public void AbandonQuest(uint questId)
        {
            Quest quest = Global.ObjectMgr.GetQuestTemplate(questId);
            if (quest != null)
            {
                // Destroy quest items on quest abandon.
                foreach (QuestObjective obj in quest.Objectives)
                {
                    if (obj.Type == QuestObjectiveType.Item)
                    {
                        ItemTemplate itemTemplate = Global.ObjectMgr.GetItemTemplate((uint)obj.ObjectID);
                        if (itemTemplate != null)
                            if (itemTemplate.GetBonding() == ItemBondingType.Quest)
                                DestroyItemCount((uint)obj.ObjectID, (uint)obj.Amount, true, true);
                    }
                }

                // Destroy items received during the quest.
                for (byte i = 0; i < SharedConst.QuestItemDropCount; ++i)
                {
                    ItemTemplate itemTemplate = Global.ObjectMgr.GetItemTemplate(quest.ItemDrop[i]);
                    if (itemTemplate != null)
                        if (quest.ItemDropQuantity[i] != 0 && itemTemplate.GetBonding() == ItemBondingType.Quest)
                            DestroyItemCount(quest.ItemDrop[i], quest.ItemDropQuantity[i], true, true);
                }
            }
        }

        public bool SatisfyQuestSkill(Quest qInfo, bool msg)
        {
            uint skill = qInfo.RequiredSkillId;

            // skip 0 case RequiredSkill
            if (skill == 0)
                return true;

            // check skill value
            if (GetSkillValue((SkillType)skill) < qInfo.RequiredSkillPoints)
            {
                if (msg)
                {
                    SendCanTakeQuestResponse(QuestFailedReasons.None);
                    Log.outDebug(LogFilter.Server, "SatisfyQuestSkill: Sent QuestFailedReason.None (questId: {0}) because player does not have required skill value.", qInfo.Id);
                }

                return false;
            }

            return true;
        }

        bool SatisfyQuestLevel(Quest qInfo, bool msg)
        {
            return SatisfyQuestMinLevel(qInfo, msg) && SatisfyQuestMaxLevel(qInfo, msg);
        }

        public bool SatisfyQuestMinLevel(Quest qInfo, bool msg)
        {
            if (GetLevel() < GetQuestMinLevel(qInfo))
            {
                if (msg)
                {
                    SendCanTakeQuestResponse(QuestFailedReasons.FailedLowLevel);
                    Log.outDebug(LogFilter.Server, "SatisfyQuestMinLevel: Sent QuestFailedReasons.FailedLowLevel (questId: {0}) because player does not have required (min) level.", qInfo.Id);
                }
                return false;
            }
            return true;
        }

        public bool SatisfyQuestMaxLevel(Quest qInfo, bool msg)
        {
            if (qInfo.MaxLevel > 0 && GetLevel() > qInfo.MaxLevel)
            {
                if (msg)
                {
                    SendCanTakeQuestResponse(QuestFailedReasons.None); // There doesn't seem to be a specific response for too high player level
                    Log.outDebug(LogFilter.Server, "SatisfyQuestMaxLevel: Sent QuestFailedReasons.None (questId: {0}) because player does not have required (max) level.", qInfo.Id);
                }
                return false;
            }
            return true;
        }

        public bool SatisfyQuestLog(bool msg)
        {
            // exist free slot
            if (FindQuestSlot(0) < SharedConst.MaxQuestLogSize)
                return true;

            if (msg)
                SendPacket(new QuestLogFull());

            return false;
        }

        public bool SatisfyQuestDependentQuests(Quest qInfo, bool msg)
        {
            return SatisfyQuestPreviousQuest(qInfo, msg) && SatisfyQuestDependentPreviousQuests(qInfo, msg) &&
                SatisfyQuestBreadcrumbQuest(qInfo, msg) && SatisfyQuestDependentBreadcrumbQuests(qInfo, msg);
        }

        public bool SatisfyQuestPreviousQuest(Quest qInfo, bool msg)
        {
            // No previous quest (might be first quest in a series)
            if (qInfo.PrevQuestId == 0)
                return true;

            uint prevId = (uint)Math.Abs(qInfo.PrevQuestId);
            // If positive previous quest rewarded, return true
            if (qInfo.PrevQuestId > 0 && m_RewardedQuests.Contains(prevId))
                return true;

            // If negative previous quest active, return true
            if (qInfo.PrevQuestId < 0 && GetQuestStatus(prevId) == QuestStatus.Incomplete)
                return true;

            // Has positive prev. quest in non-rewarded state
            // and negative prev. quest in non-active state
            if (msg)
            {
                SendCanTakeQuestResponse(QuestFailedReasons.None);
                Log.outDebug(LogFilter.Misc, $"Player.SatisfyQuestPreviousQuest: Sent QUEST_ERR_NONE (QuestID: {qInfo.Id}) because player '{GetName()}' ({GetGUID()}) doesn't have required quest {prevId}.");
            }

            return false;
        }

        bool SatisfyQuestDependentPreviousQuests(Quest qInfo, bool msg)
        {
            // No previous quest (might be first quest in a series)
            if (qInfo.DependentPreviousQuests.Empty())
                return true;

            foreach (uint prevId in qInfo.DependentPreviousQuests)
            {
                // checked in startup
                Quest questInfo = Global.ObjectMgr.GetQuestTemplate(prevId);

                // If any of the previous quests completed, return true
                if (IsQuestRewarded(prevId))
                {
                    // skip one-from-all exclusive group
                    if (questInfo.ExclusiveGroup >= 0)
                        return true;

                    // each-from-all exclusive group (< 0)
                    // can be start if only all quests in prev quest exclusive group completed and rewarded
                    var bounds = Global.ObjectMgr.GetExclusiveQuestGroupBounds(questInfo.ExclusiveGroup);
                    foreach (var exclusiveQuestId in bounds)
                    {
                        // skip checked quest id, only state of other quests in group is interesting
                        if (exclusiveQuestId == prevId)
                            continue;

                        // alternative quest from group also must be completed and rewarded (reported)
                        if (!IsQuestRewarded(exclusiveQuestId))
                        {
                            if (msg)
                            {
                                SendCanTakeQuestResponse(QuestFailedReasons.None);
                                Log.outDebug(LogFilter.Misc, $"Player.SatisfyQuestDependentPreviousQuests: Sent QUEST_ERR_NONE (QuestID: {qInfo.Id}) because player '{GetName()}' ({GetGUID()}) doesn't have the required quest (1).");
                            }

                            return false;
                        }
                    }

                    return true;
                }
            }

            // Has only prev. quests in non-rewarded state
            if (msg)
            {
                SendCanTakeQuestResponse(QuestFailedReasons.None);
                Log.outDebug(LogFilter.Misc, $"Player.SatisfyQuestDependentPreviousQuests: Sent QUEST_ERR_NONE (QuestID: {qInfo.Id}) because player '{GetName()}' ({GetGUID()}) doesn't have required quest (2).");
            }

            return false;
        }

        bool SatisfyQuestBreadcrumbQuest(Quest qInfo, bool msg)
        {
            uint breadcrumbTargetQuestId = (uint)Math.Abs(qInfo.BreadcrumbForQuestId);

            //If this is not a breadcrumb quest.
            if (breadcrumbTargetQuestId == 0)
                return true;

            // If the target quest is not available
            if (!CanTakeQuest(Global.ObjectMgr.GetQuestTemplate(breadcrumbTargetQuestId), false))
            {
                if (msg)
                {
                    SendCanTakeQuestResponse(QuestFailedReasons.None);
                    Log.outDebug(LogFilter.Misc, $"Player.SatisfyQuestBreadcrumbQuest: Sent INVALIDREASON_DONT_HAVE_REQ (QuestID: {qInfo.Id}) because target quest (QuestID: {breadcrumbTargetQuestId}) is not available to player '{GetName()}' ({GetGUID()}).");
                }

                return false;
            }

            return true;
        }

        bool SatisfyQuestDependentBreadcrumbQuests(Quest qInfo, bool msg)
        {
            foreach (uint breadcrumbQuestId in qInfo.DependentBreadcrumbQuests)
            {
                QuestStatus status = GetQuestStatus(breadcrumbQuestId);
                // If any of the breadcrumb quests are in the quest log, return false.
                if (status == QuestStatus.Incomplete || status == QuestStatus.Complete || status == QuestStatus.Failed)
                {
                    if (msg)
                    {
                        SendCanTakeQuestResponse(QuestFailedReasons.None);
                        Log.outDebug(LogFilter.Misc, $"Player.SatisfyQuestDependentBreadcrumbQuests: Sent INVALIDREASON_DONT_HAVE_REQ (QuestID: {qInfo.Id}) because player '{GetName()}' ({GetGUID()}) has a breadcrumb quest towards this quest in the quest log.");
                    }

                    return false;
                }
            }
            return true;
        }

        public bool SatisfyQuestClass(Quest qInfo, bool msg)
        {
            uint reqClass = qInfo.AllowableClasses;

            if (reqClass == 0)
                return true;

            if ((reqClass & GetClassMask()) == 0)
            {
                if (msg)
                {
                    SendCanTakeQuestResponse(QuestFailedReasons.None);
                    Log.outDebug(LogFilter.Server, "SatisfyQuestClass: Sent QuestFailedReason.None (questId: {0}) because player does not have required class.", qInfo.Id);
                }

                return false;
            }

            return true;
        }

        public bool SatisfyQuestRace(Quest qInfo, bool msg)
        {
            long reqraces = qInfo.AllowableRaces;
            if (reqraces == -1)
                return true;

            if ((reqraces & (long)SharedConst.GetMaskForRace(GetRace())) == 0)
            {
                if (msg)
                {
                    SendCanTakeQuestResponse(QuestFailedReasons.FailedWrongRace);
                    Log.outDebug(LogFilter.Server, "SatisfyQuestRace: Sent QuestFailedReasons.FailedWrongRace (questId: {0}) because player does not have required race.", qInfo.Id);

                }
                return false;
            }
            return true;
        }

        public bool SatisfyQuestReputation(Quest qInfo, bool msg)
        {
            uint fIdMin = qInfo.RequiredMinRepFaction;      //Min required rep
            if (fIdMin != 0 && GetReputationMgr().GetReputation(fIdMin) < qInfo.RequiredMinRepValue)
            {
                if (msg)
                {
                    SendCanTakeQuestResponse(QuestFailedReasons.None);
                    Log.outDebug(LogFilter.Server, "SatisfyQuestReputation: Sent QuestFailedReason.None (questId: {0}) because player does not have required reputation (min).", qInfo.Id);
                }
                return false;
            }

            uint fIdMax = qInfo.RequiredMaxRepFaction;      //Max required rep
            if (fIdMax != 0 && GetReputationMgr().GetReputation(fIdMax) >= qInfo.RequiredMaxRepValue)
            {
                if (msg)
                {
                    SendCanTakeQuestResponse(QuestFailedReasons.None);
                    Log.outDebug(LogFilter.Server, "SatisfyQuestReputation: Sent QuestFailedReason.None (questId: {0}) because player does not have required reputation (max).", qInfo.Id);
                }
                return false;
            }

            return true;
        }

        public bool SatisfyQuestStatus(Quest qInfo, bool msg)
        {
            if (GetQuestStatus(qInfo.Id) == QuestStatus.Rewarded)
            {
                if (msg)
                {
                    SendCanTakeQuestResponse(QuestFailedReasons.AlreadyDone);
                    Log.outDebug(LogFilter.Misc, "Player.SatisfyQuestStatus: Sent QUEST_STATUS_REWARDED (QuestID: {0}) because player '{1}' ({2}) quest status is already REWARDED.",
                        qInfo.Id, GetName(), GetGUID().ToString());
                }
                return false;
            }

            if (GetQuestStatus(qInfo.Id) != QuestStatus.None)
            {
                if (msg)
                {
                    SendCanTakeQuestResponse(QuestFailedReasons.AlreadyOn1);
                    Log.outDebug(LogFilter.Server, "SatisfyQuestStatus: Sent QuestFailedReasons.AlreadyOn1 (questId: {0}) because player quest status is not NONE.", qInfo.Id);
                }
                return false;
            }
            return true;
        }

        public bool SatisfyQuestConditions(Quest qInfo, bool msg)
        {
            if (!Global.ConditionMgr.IsObjectMeetingNotGroupedConditions(ConditionSourceType.QuestAvailable, qInfo.Id, this))
            {
                if (msg)
                {
                    SendCanTakeQuestResponse(QuestFailedReasons.None);
                    Log.outDebug(LogFilter.Server, "SatisfyQuestConditions: Sent QuestFailedReason.None (questId: {0}) because player does not meet conditions.", qInfo.Id);
                }
                Log.outDebug(LogFilter.Condition, "SatisfyQuestConditions: conditions not met for quest {0}", qInfo.Id);
                return false;
            }
            return true;
        }

        public bool SatisfyQuestTimed(Quest qInfo, bool msg)
        {
            if (!m_timedquests.Empty() && qInfo.LimitTime != 0)
            {
                if (msg)
                {
                    SendCanTakeQuestResponse(QuestFailedReasons.OnlyOneTimed);
                    Log.outDebug(LogFilter.Server, "SatisfyQuestTimed: Sent QuestFailedReasons.OnlyOneTimed (questId: {0}) because player is already on a timed quest.", qInfo.Id);
                }
                return false;
            }
            return true;
        }

        public bool SatisfyQuestExclusiveGroup(Quest qInfo, bool msg)
        {
            // non positive exclusive group, if > 0 then can be start if any other quest in exclusive group already started/completed
            if (qInfo.ExclusiveGroup <= 0)
                return true;

            var range = Global.ObjectMgr.GetExclusiveQuestGroupBounds(qInfo.ExclusiveGroup);
            // always must be found if qInfo.ExclusiveGroup != 0

            foreach (var exclude_Id in range)
            {
                // skip checked quest id, only state of other quests in group is interesting
                if (exclude_Id == qInfo.Id)
                    continue;

                // not allow have daily quest if daily quest from exclusive group already recently completed
                Quest Nquest = Global.ObjectMgr.GetQuestTemplate(exclude_Id);
                if (!SatisfyQuestDay(Nquest, false) || !SatisfyQuestWeek(Nquest, false) || !SatisfyQuestSeasonal(Nquest, false))
                {
                    if (msg)
                    {
                        SendCanTakeQuestResponse(QuestFailedReasons.None);
                        Log.outDebug(LogFilter.Server, "SatisfyQuestExclusiveGroup: Sent QuestFailedReason.None (questId: {0}) because player already did daily quests in exclusive group.", qInfo.Id);
                    }

                    return false;
                }

                // alternative quest already started or completed - but don't check rewarded states if both are repeatable
                if (GetQuestStatus(exclude_Id) != QuestStatus.None || (!(qInfo.IsRepeatable() && Nquest.IsRepeatable()) && GetQuestRewardStatus(exclude_Id)))
                {
                    if (msg)
                    {
                        SendCanTakeQuestResponse(QuestFailedReasons.None);
                        Log.outDebug(LogFilter.Server, "SatisfyQuestExclusiveGroup: Sent QuestFailedReason.None (questId: {0}) because player already did quest in exclusive group.", qInfo.Id);
                    }
                    return false;
                }
            }
            return true;
        }

        public bool SatisfyQuestDay(Quest qInfo, bool msg)
        {
            if (!qInfo.IsDaily() && !qInfo.IsDFQuest())
                return true;

            if (qInfo.IsDFQuest())
            {
                if (m_DFQuests.Contains(qInfo.Id))
                    return false;

                return true;
            }

            return m_activePlayerData.DailyQuestsCompleted.FindIndex(qInfo.Id) == -1;
        }

        public bool SatisfyQuestWeek(Quest qInfo, bool msg)
        {
            if (!qInfo.IsWeekly() || m_weeklyquests.Empty())
                return true;

            // if not found in cooldown list
            return !m_weeklyquests.Contains(qInfo.Id);
        }

        public bool SatisfyQuestSeasonal(Quest qInfo, bool msg)
        {
            if (!qInfo.IsSeasonal() || m_seasonalquests.Empty())
                return true;

            var list = m_seasonalquests.LookupByKey(qInfo.GetEventIdForQuest());
            if (list == null || list.Empty())
                return true;

            // if not found in cooldown list
            return !list.Contains(qInfo.Id);
        }

        public bool SatisfyQuestExpansion(Quest qInfo, bool msg)
        {
            if ((int)GetSession().GetExpansion() < qInfo.Expansion)
            {
                if (msg)
                    SendCanTakeQuestResponse(QuestFailedReasons.FailedExpansion);

                Log.outDebug(LogFilter.Misc, $"Player.SatisfyQuestExpansion: Sent QUEST_ERR_FAILED_EXPANSION (QuestID: {qInfo.Id}) because player '{GetName()}' ({GetGUID()}) does not have required expansion.");
                return false;
            }
            return true;
        }

        public bool SatisfyQuestMonth(Quest qInfo, bool msg)
        {
            if (!qInfo.IsMonthly() || m_monthlyquests.Empty())
                return true;

            // if not found in cooldown list
            return !m_monthlyquests.Contains(qInfo.Id);
        }

        public bool GiveQuestSourceItem(Quest quest)
        {
            uint srcitem = quest.SourceItemId;
            if (srcitem > 0)
            {
                // Don't give source item if it is the same item used to start the quest
                ItemTemplate itemTemplate = Global.ObjectMgr.GetItemTemplate(srcitem);
                if (quest.Id == itemTemplate.GetStartQuest())
                    return true;

                uint count = quest.SourceItemIdCount;
                if (count <= 0)
                    count = 1;

                List<ItemPosCount> dest = new();
                InventoryResult msg = CanStoreNewItem(ItemConst.NullBag, ItemConst.NullSlot, dest, srcitem, count);
                if (msg == InventoryResult.Ok)
                {
                    Item item = StoreNewItem(dest, srcitem, true);
                    SendNewItem(item, count, true, false);
                    return true;
                }
                // player already have max amount required item, just report success
                if (msg == InventoryResult.ItemMaxCount)
                    return true;

                SendEquipError(msg, null, null, srcitem);
                return false;
            }

            return true;
        }

        public bool TakeQuestSourceItem(uint questId, bool msg)
        {
            Quest quest = Global.ObjectMgr.GetQuestTemplate(questId);
            if (quest != null)
            {
                uint srcItemId = quest.SourceItemId;
                ItemTemplate item = Global.ObjectMgr.GetItemTemplate(srcItemId);

                if (srcItemId > 0)
                {
                    uint count = quest.SourceItemIdCount;
                    if (count <= 0)
                        count = 1;

                    // There are two cases where the source item is not destroyed:
                    // - Item cannot be unequipped (example: non-empty bags)
                    // - The source item is the item that started the quest, so the player is supposed to keep it (otherwise it was already destroyed in AddQuestAndCheckCompletion())
                    InventoryResult res = CanUnequipItems(srcItemId, count);
                    if (res != InventoryResult.Ok)
                    {
                        if (msg)
                            SendEquipError(res, null, null, srcItemId);
                        return false;
                    }

                    if (item.GetStartQuest() != questId)
                        DestroyItemCount(srcItemId, count, true, true);
                }
            }

            return true;
        }

        public bool GetQuestRewardStatus(uint quest_id)
        {
            Quest qInfo = Global.ObjectMgr.GetQuestTemplate(quest_id);
            if (qInfo != null)
            {
                if (qInfo.IsSeasonal() && !qInfo.IsRepeatable())
                    return !SatisfyQuestSeasonal(qInfo, false);

                // for repeatable quests: rewarded field is set after first reward only to prevent getting XP more than once
                if (!qInfo.IsRepeatable())
                    return IsQuestRewarded(quest_id);

                return false;
            }
            return false;
        }

        public QuestStatus GetQuestStatus(uint questId)
        {
            if (questId != 0)
            {
                var questStatusData = m_QuestStatus.LookupByKey(questId);
                if (questStatusData != null)
                    return questStatusData.Status;

                if (GetQuestRewardStatus(questId))
                    return QuestStatus.Rewarded;
            }
            return QuestStatus.None;
        }

        public bool CanShareQuest(uint quest_id)
        {
            Quest qInfo = Global.ObjectMgr.GetQuestTemplate(quest_id);
            if (qInfo != null && qInfo.HasFlag(QuestFlags.Sharable))
            {
                var questStatusData = m_QuestStatus.LookupByKey(quest_id);
                return questStatusData != null;
            }
            return false;
        }

        public void SetQuestStatus(uint questId, QuestStatus status, bool update = true)
        {
            Quest quest = Global.ObjectMgr.GetQuestTemplate(questId);
            if (quest != null)
            {
                if (!m_QuestStatus.ContainsKey(questId))
                    m_QuestStatus[questId] = new QuestStatusData();

                QuestStatus oldStatus = m_QuestStatus[questId].Status;
                m_QuestStatus[questId].Status = status;
                if (!quest.IsAutoComplete())
                    m_QuestStatusSave[questId] = QuestSaveType.Default;

                Global.ScriptMgr.OnQuestStatusChange(this, questId);
                Global.ScriptMgr.OnQuestStatusChange(this, quest, oldStatus, status);
            }

            if (update)
                SendQuestUpdate(questId);
        }

        public void RemoveActiveQuest(uint questId, bool update = true)
        {
            var questStatus = m_QuestStatus.LookupByKey(questId);
            if (questStatus != null)
            {
                foreach (var objective in m_questObjectiveStatus.KeyValueList)
                {
                    if (objective.Value.QuestStatusPair.Status == questStatus)
                        m_questObjectiveStatus.Remove(objective);
                }
                m_QuestStatus.Remove(questId);
                m_QuestStatusSave[questId] = QuestSaveType.Delete;
            }

            if (update)
                SendQuestUpdate(questId);
        }

        public void RemoveRewardedQuest(uint questId, bool update = true)
        {
            if (m_RewardedQuests.Contains(questId))
            {
                m_RewardedQuests.Remove(questId);
                m_RewardedQuestsSave[questId] = QuestSaveType.ForceDelete;
            }

            uint questBit = Global.DB2Mgr.GetQuestUniqueBitFlag(questId);
            if (questBit != 0)
                SetQuestCompletedBit(questBit, false);

            // Remove seasonal quest also
            Quest qInfo = Global.ObjectMgr.GetQuestTemplate(questId);
            if (qInfo.IsSeasonal())
            {
                ushort eventId = qInfo.GetEventIdForQuest();
                if (m_seasonalquests.ContainsKey(eventId))
                {
                    m_seasonalquests.Remove(eventId, questId);
                    m_SeasonalQuestChanged = true;
                }
            }

            if (update)
                SendQuestUpdate(questId);
        }

        void SendQuestUpdate(uint questId)
        {
            var saBounds = Global.SpellMgr.GetSpellAreaForQuestMapBounds(questId);
            if (!saBounds.Empty())
            {
                List<uint> aurasToRemove = new();
                List<uint> aurasToCast = new();
                GetZoneAndAreaId(out uint zone, out uint area);

                foreach (var spell in saBounds)
                {
                    if (spell.flags.HasAnyFlag(SpellAreaFlag.AutoRemove) && !spell.IsFitToRequirements(this, zone, area))
                        aurasToRemove.Add(spell.spellId);
                    else if (spell.flags.HasAnyFlag(SpellAreaFlag.AutoCast) && !spell.flags.HasAnyFlag(SpellAreaFlag.IgnoreAutocastOnQuestStatusChange))
                        aurasToCast.Add(spell.spellId);
                }

                // Auras matching the requirements will be inside the aurasToCast container.
                // Auras not matching the requirements may prevent using auras matching the requirements.
                // aurasToCast will erase conflicting auras in aurasToRemove container to handle spells used by multiple quests.

                for (var c = 0; c < aurasToRemove.Count;)
                {
                    bool auraRemoved = false;

                    foreach (var i in aurasToCast)
                    {
                        if (aurasToRemove[c] == i)
                        {
                            aurasToRemove.Remove(aurasToRemove[c]);
                            auraRemoved = true;
                            break;
                        }
                    }

                    if (!auraRemoved)
                        ++c;
                }

                foreach (var spellId in aurasToCast)
                    if (!HasAura(spellId))
                        CastSpell(this, spellId, true);

                foreach (var spellId in aurasToRemove)
                    RemoveAurasDueToSpell(spellId);
            }

            UpdateVisibleGameobjectsOrSpellClicks();
            PhasingHandler.OnConditionChange(this);
        }

        public QuestGiverStatus GetQuestDialogStatus(WorldObject questgiver)
        {
            QuestRelationResult questRelations;
            QuestRelationResult questInvolvedRelations;

            switch (questgiver.GetTypeId())
            {
                case TypeId.GameObject:
                {
                    GameObjectAI ai = questgiver.ToGameObject().GetAI();
                    if (ai != null)
                    {
                        var questStatus = ai.GetDialogStatus(this);
                        if (questStatus.HasValue)
                            return questStatus.Value;
                    }

                    questRelations = Global.ObjectMgr.GetGOQuestRelations(questgiver.GetEntry());
                    questInvolvedRelations = Global.ObjectMgr.GetGOQuestInvolvedRelations(questgiver.GetEntry());
                    break;
                }
                case TypeId.Unit:
                {
                    CreatureAI ai = questgiver.ToCreature().GetAI();
                    if (ai != null)
                    {
                        QuestGiverStatus? questStatus = ai.GetDialogStatus(this);
                        if (questStatus.HasValue)
                            return questStatus.Value;
                    }

                    questRelations = Global.ObjectMgr.GetCreatureQuestRelations(questgiver.GetEntry());
                    questInvolvedRelations = Global.ObjectMgr.GetCreatureQuestInvolvedRelations(questgiver.GetEntry());
                    break;
                }
                default:
                    // it's impossible, but check
                    Log.outError(LogFilter.Player, "GetQuestDialogStatus called for unexpected type {0}", questgiver.GetTypeId());
                    return QuestGiverStatus.None;
            }

            QuestGiverStatus result = QuestGiverStatus.None;

            foreach (var questId in questInvolvedRelations)
            {
                Quest quest = Global.ObjectMgr.GetQuestTemplate(questId);
                if (quest == null)
                    continue;

                switch (GetQuestStatus(questId))
                {
                    case QuestStatus.Complete:
                        if (quest.GetQuestTag() == QuestTagType.CovenantCalling)
                            result |= quest.HasFlag(QuestFlags.HideRewardPoi) ? QuestGiverStatus.CovenantCallingRewardCompleteNoPOI : QuestGiverStatus.CovenantCallingRewardCompletePOI;
                        else if (quest.HasFlagEx(QuestFlagsEx.LegendaryQuest))
                            result |= quest.HasFlag(QuestFlags.HideRewardPoi) ? QuestGiverStatus.LegendaryRewardCompleteNoPOI : QuestGiverStatus.LegendaryRewardCompletePOI;
                        else
                            result |= quest.HasFlag(QuestFlags.HideRewardPoi) ? QuestGiverStatus.RewardCompleteNoPOI : QuestGiverStatus.RewardCompletePOI;
                        break;
                    case QuestStatus.Incomplete:
                        if (quest.GetQuestTag() == QuestTagType.CovenantCalling)
                            result |= QuestGiverStatus.CovenantCallingReward;
                        else
                            result |= QuestGiverStatus.Reward;
                        break;
                    default:
                        break;
                }

                if (quest.IsAutoComplete() && CanTakeQuest(quest, false) && quest.IsRepeatable() && !quest.IsDailyOrWeekly() && !quest.IsMonthly())
                {
                    if (GetLevel() <= (GetQuestLevel(quest) + WorldConfig.GetIntValue(WorldCfg.QuestLowLevelHideDiff)))
                        result |= QuestGiverStatus.RepeatableTurnin;
                    else
                        result |= QuestGiverStatus.TrivialRepeatableTurnin;
                }
            }

            foreach (var questId in questRelations)
            {
                Quest quest = Global.ObjectMgr.GetQuestTemplate(questId);
                if (quest == null)
                    continue;

                if (!Global.ConditionMgr.IsObjectMeetingNotGroupedConditions(ConditionSourceType.QuestAvailable, quest.Id, this))
                    continue;

                if (GetQuestStatus(questId) == QuestStatus.None)
                {
                    if (CanSeeStartQuest(quest))
                    {
                        if (SatisfyQuestLevel(quest, false))
                        {
                            if (GetLevel() <= (GetQuestLevel(quest) + WorldConfig.GetIntValue(WorldCfg.QuestLowLevelHideDiff)))
                            {
                                if (quest.GetQuestTag() == QuestTagType.CovenantCalling)
                                    result |= QuestGiverStatus.CovenantCallingQuest;
                                else if (quest.HasFlagEx(QuestFlagsEx.LegendaryQuest))
                                    result |= QuestGiverStatus.LegendaryQuest;
                                else if (quest.IsDaily())
                                    result |= QuestGiverStatus.DailyQuest;
                                else
                                    result |= QuestGiverStatus.Quest;
                            }
                            else if (quest.IsDaily())
                                result |= QuestGiverStatus.TrivialDailyQuest;
                            else
                                result |= QuestGiverStatus.Trivial;
                        }
                        else
                            result |= QuestGiverStatus.Future;
                    }
                }
            }

            return result;
        }

        public ushort GetReqKillOrCastCurrentCount(uint quest_id, int entry)
        {
            Quest qInfo = Global.ObjectMgr.GetQuestTemplate(quest_id);
            if (qInfo == null)
                return 0;

            ushort slot = FindQuestSlot(quest_id);
            if (slot >= SharedConst.MaxQuestLogSize)
                return 0;

            foreach (QuestObjective obj in qInfo.Objectives)
                if (obj.ObjectID == entry)
                    return (ushort)GetQuestSlotObjectiveData(slot, obj);

            return 0;
        }

        public void AdjustQuestObjectiveProgress(Quest quest)
        {
            // adjust progress of quest objectives that rely on external counters, like items
            if (quest.HasQuestObjectiveType(QuestObjectiveType.Item))
            {
                foreach (QuestObjective obj in quest.Objectives)
                {
                    if (obj.Type == QuestObjectiveType.Item)
                    {
                        uint reqItemCount = (uint)obj.Amount;
                        uint curItemCount = GetItemCount((uint)obj.ObjectID, true);
                        SetQuestObjectiveData(obj, (int)Math.Min(curItemCount, reqItemCount));
                    }
                    else if (obj.Type == QuestObjectiveType.HaveCurrency)
                    {
                        uint reqCurrencyCount = (uint)obj.Amount;
                        uint curCurrencyCount = GetCurrency((uint)obj.ObjectID);
                        SetQuestObjectiveData(obj, (int)Math.Min(reqCurrencyCount, curCurrencyCount));
                    }
                }
            }
        }

        public ushort FindQuestSlot(uint quest_id)
        {
            for (ushort i = 0; i < SharedConst.MaxQuestLogSize; ++i)
                if (GetQuestSlotQuestId(i) == quest_id)
                    return i;

            return SharedConst.MaxQuestLogSize;
        }

        public uint GetQuestSlotQuestId(ushort slot)
        {
            return m_playerData.QuestLog[slot].QuestID;
        }

        public uint GetQuestSlotState(ushort slot, byte counter)
        {
            return m_playerData.QuestLog[slot].StateFlags;
        }

        public ushort GetQuestSlotCounter(ushort slot, byte counter)
        {
            if (counter < SharedConst.MaxQuestCounts)
                return m_playerData.QuestLog[slot].ObjectiveProgress[counter];

            return 0;
        }

        public uint GetQuestSlotEndTime(ushort slot)
        {
            return m_playerData.QuestLog[slot].EndTime;
        }

        public uint GetQuestSlotAcceptTime(ushort slot)
        {
            return m_playerData.QuestLog[slot].AcceptTime;
        }

        bool GetQuestSlotObjectiveFlag(ushort slot, sbyte objectiveIndex)
        {
            if (objectiveIndex < SharedConst.MaxQuestCounts)
                return ((m_playerData.QuestLog[slot].ObjectiveFlags) & (1 << objectiveIndex)) != 0;
            return false;
        }

        public int GetQuestSlotObjectiveData(ushort slot, QuestObjective objective)
        {
            if (objective.StorageIndex < 0)
            {
                Log.outError(LogFilter.Player, $"Player.GetQuestObjectiveData: Called for quest {objective.QuestID} with invalid StorageIndex {objective.StorageIndex} (objective data is not tracked)");
                return 0;
            }

            if (objective.StorageIndex >= SharedConst.MaxQuestCounts)
            {
                Log.outError(LogFilter.Player, $"Player.GetQuestObjectiveData: Player '{GetName()}' ({GetGUID()}) quest {objective.QuestID} out of range StorageIndex {objective.StorageIndex}");
                return 0;
            }

            if (!objective.IsStoringFlag())
                return GetQuestSlotCounter(slot, (byte)objective.StorageIndex);

            return GetQuestSlotObjectiveFlag(slot, objective.StorageIndex) ? 1 : 0;
        }

        public void SetQuestSlot(ushort slot, uint quest_id)
        {
            var questLogField = m_values.ModifyValue(m_playerData).ModifyValue(m_playerData.QuestLog, slot);
            SetUpdateFieldValue(questLogField.ModifyValue(questLogField.QuestID), quest_id);
            SetUpdateFieldValue(questLogField.ModifyValue(questLogField.StateFlags), 0u);
            SetUpdateFieldValue(questLogField.ModifyValue(questLogField.EndTime), 0u);
            SetUpdateFieldValue(questLogField.ModifyValue(questLogField.AcceptTime), 0u);
            SetUpdateFieldValue(questLogField.ModifyValue(questLogField.ObjectiveFlags), 0u);

            for (int i = 0; i < SharedConst.MaxQuestCounts; ++i)
                SetUpdateFieldValue(ref questLogField.ModifyValue(questLogField.ObjectiveProgress, i), (ushort)0);

        }

        public void SetQuestSlotCounter(ushort slot, byte counter, ushort count)
        {
            if (counter >= SharedConst.MaxQuestCounts)
                return;

            QuestLog questLog = m_values.ModifyValue(m_playerData).ModifyValue(m_playerData.QuestLog, slot);
            SetUpdateFieldValue(ref questLog.ModifyValue(questLog.ObjectiveProgress, counter), count);
        }

        public void SetQuestSlotState(ushort slot, QuestSlotStateMask state)
        {
            QuestLog questLogField = m_values.ModifyValue(m_playerData).ModifyValue(m_playerData.QuestLog, slot);
            SetUpdateFieldFlagValue(questLogField.ModifyValue(questLogField.StateFlags), (uint)state);
        }

        public void RemoveQuestSlotState(ushort slot, QuestSlotStateMask state)
        {
            QuestLog questLogField = m_values.ModifyValue(m_playerData).ModifyValue(m_playerData.QuestLog, slot);
            RemoveUpdateFieldFlagValue(questLogField.ModifyValue(questLogField.StateFlags), (uint)state);
        }

        public void SetQuestSlotEndTime(ushort slot, long endTime)
        {
            QuestLog questLog = m_values.ModifyValue(m_playerData).ModifyValue(m_playerData.QuestLog, slot);
            SetUpdateFieldValue(questLog.ModifyValue(questLog.EndTime), (uint)endTime);
        }

        public void SetQuestSlotAcceptTime(ushort slot, long acceptTime)
        {
            QuestLog questLog = m_values.ModifyValue(m_playerData).ModifyValue(m_playerData.QuestLog, slot);
            SetUpdateFieldValue(questLog.ModifyValue(questLog.AcceptTime), (uint)acceptTime);
        }

        void SetQuestSlotObjectiveFlag(ushort slot, sbyte objectiveIndex)
        {
            QuestLog questLog = m_values.ModifyValue(m_playerData).ModifyValue(m_playerData.QuestLog, slot);
            SetUpdateFieldFlagValue(questLog.ModifyValue(questLog.ObjectiveFlags), 1u << objectiveIndex);
        }

        void RemoveQuestSlotObjectiveFlag(ushort slot, sbyte objectiveIndex)
        {
            QuestLog questLog = m_values.ModifyValue(m_playerData).ModifyValue(m_playerData.QuestLog, slot);
            RemoveUpdateFieldFlagValue(questLog.ModifyValue(questLog.ObjectiveFlags), 1u << objectiveIndex);
        }

        void SetQuestCompletedBit(uint questBit, bool completed)
        {
            if (questBit == 0)
                return;

            uint fieldOffset = (questBit - 1) >> 6;
            if (fieldOffset >= PlayerConst.QuestsCompletedBitsSize)
                return;

            ulong flag = 1ul << (((int)questBit - 1) & 63);
            if (completed)
                SetUpdateFieldFlagValue(ref m_values.ModifyValue(m_activePlayerData).ModifyValue(m_activePlayerData.QuestCompleted, (int)fieldOffset), flag);
            else
                RemoveUpdateFieldFlagValue(ref m_values.ModifyValue(m_activePlayerData).ModifyValue(m_activePlayerData.QuestCompleted, (int)fieldOffset), flag);
        }

        public void AreaExploredOrEventHappens(uint questId)
        {
            if (questId != 0)
            {
                QuestStatusData status = m_QuestStatus.LookupByKey(questId);
                if (status != null)
                {
                    // Dont complete failed quest
                    if (!status.Explored && status.Status != QuestStatus.Failed)
                    {
                        status.Explored = true;
                        m_QuestStatusSave[questId] = QuestSaveType.Default;

                        SendQuestComplete(questId);
                    }
                }
                if (CanCompleteQuest(questId))
                    CompleteQuest(questId);
            }
        }

        public void GroupEventHappens(uint questId, WorldObject pEventObject)
        {
            var group = GetGroup();
            if (group)
            {
                for (GroupReference refe = group.GetFirstMember(); refe != null; refe = refe.Next())
                {
                    Player player = refe.GetSource();

                    // for any leave or dead (with not released body) group member at appropriate distance
                    if (player && player.IsAtGroupRewardDistance(pEventObject) && !player.GetCorpse())
                        player.AreaExploredOrEventHappens(questId);
                }
            }
            else
                AreaExploredOrEventHappens(questId);
        }

        public void ItemAddedQuestCheck(uint entry, uint count)
        {
            UpdateQuestObjectiveProgress(QuestObjectiveType.Item, (int)entry, count);
        }

        public void ItemRemovedQuestCheck(uint entry, uint count)
        {
            foreach (var objectiveStatusData in m_questObjectiveStatus.LookupByKey((QuestObjectiveType.Item, (int)entry)))
            {
                uint questId = objectiveStatusData.QuestStatusPair.QuestID;
                Quest quest = Global.ObjectMgr.GetQuestTemplate(questId);
                ushort logSlot = objectiveStatusData.QuestStatusPair.Status.Slot;
                QuestObjective objective = objectiveStatusData.Objective;

                if (!IsQuestObjectiveCompletable(logSlot, quest, objective))
                    continue;

                int curItemCount = GetQuestSlotObjectiveData(logSlot, objective);
                if (curItemCount >= objective.Amount) // we may have more than what the status shows
                    curItemCount = (int)GetItemCount(entry, false);

                int newItemCount = (int)((count > curItemCount) ? 0 : curItemCount - count);

                if (newItemCount < objective.Amount)
                {
                    SetQuestObjectiveData(objective, newItemCount);
                    IncompleteQuest(questId);
                }
            }

            UpdateVisibleGameobjectsOrSpellClicks();
        }

        public void KilledMonster(CreatureTemplate cInfo, ObjectGuid guid)
        {
            Cypher.Assert(cInfo != null);

            if (cInfo.Entry != 0)
                KilledMonsterCredit(cInfo.Entry, guid);

            for (byte i = 0; i < 2; ++i)
                if (cInfo.KillCredit[i] != 0)
                    KilledMonsterCredit(cInfo.KillCredit[i]);
        }

        public void KilledMonsterCredit(uint entry, ObjectGuid guid = default)
        {
            ushort addKillCount = 1;
            uint real_entry = entry;
            Creature killed = null;
            if (!guid.IsEmpty())
            {
                killed = GetMap().GetCreature(guid);
                if (killed != null && killed.GetEntry() != 0)
                    real_entry = killed.GetEntry();
            }

            StartCriteriaTimer(CriteriaStartEvent.KillNPC, real_entry);   // MUST BE CALLED FIRST
            UpdateCriteria(CriteriaType.KillCreature, real_entry, addKillCount, 0, killed);

            UpdateQuestObjectiveProgress(QuestObjectiveType.Monster, (int)entry, 1, guid);
        }

        public void KilledPlayerCredit(ObjectGuid victimGuid)
        {
            UpdateQuestObjectiveProgress(QuestObjectiveType.PlayerKills, 0, 1, victimGuid);
        }

        public void KillCreditGO(uint entry, ObjectGuid guid = default)
        {
            UpdateQuestObjectiveProgress(QuestObjectiveType.GameObject, (int)entry, 1, guid);
        }

        public void KillCreditCriteriaTreeObjective(QuestObjective questObjective)
        {
            UpdateQuestObjectiveProgress(QuestObjectiveType.CriteriaTree, questObjective.ObjectID, 1);
        }

        public void TalkedToCreature(uint entry, ObjectGuid guid)
        {
            UpdateQuestObjectiveProgress(QuestObjectiveType.TalkTo, (int)entry, 1, guid);
        }

        public void MoneyChanged(ulong value)
        {
            UpdateQuestObjectiveProgress(QuestObjectiveType.Money, 0, (long)value - (long)GetMoney());
        }

        public void ReputationChanged(FactionRecord FactionRecord, int change)
        {
            UpdateQuestObjectiveProgress(QuestObjectiveType.MinReputation, (int)FactionRecord.Id, change);
            UpdateQuestObjectiveProgress(QuestObjectiveType.MaxReputation, (int)FactionRecord.Id, change);
            UpdateQuestObjectiveProgress(QuestObjectiveType.IncreaseReputation, (int)FactionRecord.Id, change);
        }

        void CurrencyChanged(uint currencyId, int change)
        {
            UpdateQuestObjectiveProgress(QuestObjectiveType.Currency, (int)currencyId, change);
            UpdateQuestObjectiveProgress(QuestObjectiveType.HaveCurrency, (int)currencyId, change);
            UpdateQuestObjectiveProgress(QuestObjectiveType.ObtainCurrency, (int)currencyId, change);
        }

        public void UpdateQuestObjectiveProgress(QuestObjectiveType objectiveType, int objectId, long addCount, ObjectGuid victimGuid = default)
        {
            bool anyObjectiveChangedCompletionState = false;

            foreach (var objectiveStatusData in m_questObjectiveStatus.LookupByKey((objectiveType, objectId)))
            {
                uint questId = objectiveStatusData.QuestStatusPair.QuestID;
                Quest quest = Global.ObjectMgr.GetQuestTemplate(questId);

                if (!QuestObjective.CanAlwaysBeProgressedInRaid(objectiveType))
                    if (GetGroup() && GetGroup().IsRaidGroup() && quest.IsAllowedInRaid(GetMap().GetDifficultyID()))
                        continue;

                ushort logSlot = objectiveStatusData.QuestStatusPair.Status.Slot;
                QuestObjective objective = objectiveStatusData.Objective;
                if (!IsQuestObjectiveCompletable(logSlot, quest, objective))
                    continue;

                bool objectiveWasComplete = IsQuestObjectiveComplete(logSlot, quest, objective);
                if (!objectiveWasComplete || addCount < 0)
                {
                    bool objectiveIsNowComplete = false;
                    if (objective.IsStoringValue())
                    {
                        if (objectiveType == QuestObjectiveType.PlayerKills && objective.Flags.HasAnyFlag(QuestObjectiveFlags.KillPlayersSameFaction))
                        {
                            Player victim = Global.ObjAccessor.GetPlayer(GetMap(), victimGuid);
                            if (victim?.GetTeam() != GetTeam())
                                continue;
                        }

                        int currentProgress = GetQuestSlotObjectiveData(logSlot, objective);
                        if (addCount > 0 ? (currentProgress < objective.Amount) : (currentProgress > 0))
                        {
                            int newProgress = (int)Math.Clamp(currentProgress + addCount, 0, objective.Amount);
                            SetQuestObjectiveData(objective, newProgress);
                            if (addCount > 0 && !objective.Flags.HasAnyFlag(QuestObjectiveFlags.HideCreditMsg))
                            {
                                if (objectiveType != QuestObjectiveType.PlayerKills)
                                    SendQuestUpdateAddCredit(quest, victimGuid, objective, (uint)newProgress);
                                else
                                    SendQuestUpdateAddPlayer(quest, (uint)newProgress);
                            }

                            objectiveIsNowComplete = IsQuestObjectiveComplete(logSlot, quest, objective);
                        }
                    }
                    else if (objective.IsStoringFlag())
                    {
                        SetQuestObjectiveData(objective, addCount > 0 ? 1 : 0);

                        if (addCount > 0 && !objective.Flags.HasAnyFlag(QuestObjectiveFlags.HideCreditMsg))
                            SendQuestUpdateAddCreditSimple(objective);

                        objectiveIsNowComplete = IsQuestObjectiveComplete(logSlot, quest, objective);
                    }
                    else
                    {
                        switch (objectiveType)
                        {
                            case QuestObjectiveType.Currency:
                                objectiveIsNowComplete = GetCurrency((uint)objectId) + addCount >= objective.Amount;
                                break;
                            case QuestObjectiveType.LearnSpell:
                                objectiveIsNowComplete = addCount != 0;
                                break;
                            case QuestObjectiveType.MinReputation:
                                objectiveIsNowComplete = GetReputationMgr().GetReputation((uint)objectId) + addCount >= objective.Amount;
                                break;
                            case QuestObjectiveType.MaxReputation:
                                objectiveIsNowComplete = GetReputationMgr().GetReputation((uint)objectId) + addCount <= objective.Amount;
                                break;
                            case QuestObjectiveType.Money:
                                objectiveIsNowComplete = (long)GetMoney() + addCount >= objective.Amount;
                                break;
                            case QuestObjectiveType.ProgressBar:
                                objectiveIsNowComplete = IsQuestObjectiveProgressBarComplete(logSlot, quest);
                                break;
                            default:
                                Cypher.Assert(false, "Unhandled quest objective type {objectiveType}");
                                break;
                        }
                    }

                    if (objective.Flags.HasAnyFlag(QuestObjectiveFlags.PartOfProgressBar))
                    {
                        if (IsQuestObjectiveProgressBarComplete(logSlot, quest))
                        {
                            var progressBarObjective = quest.Objectives.Find(otherObjective => otherObjective.Type == QuestObjectiveType.ProgressBar && !otherObjective.Flags.HasAnyFlag(QuestObjectiveFlags.PartOfProgressBar));
                            if (progressBarObjective != null)
                                SendQuestUpdateAddCreditSimple(progressBarObjective);

                            objectiveIsNowComplete = true;
                        }
                    }

                    if (objectiveWasComplete != objectiveIsNowComplete)
                        anyObjectiveChangedCompletionState = true;

                    if (objectiveIsNowComplete && CanCompleteQuest(questId, objective.Id))
                        CompleteQuest(questId);
                    else if (objectiveStatusData.QuestStatusPair.Status.Status == QuestStatus.Complete)
                        IncompleteQuest(questId);
                }
            }

            if (anyObjectiveChangedCompletionState)
                UpdateVisibleGameobjectsOrSpellClicks();
        }

        public bool HasQuestForItem(uint itemid)
        {
            // Search incomplete objective first
            foreach (var objectiveItr in m_questObjectiveStatus.LookupByKey((QuestObjectiveType.Item, (int)itemid)))
            {
                Quest qInfo = Global.ObjectMgr.GetQuestTemplate(objectiveItr.QuestStatusPair.QuestID);
                QuestObjective objective = objectiveItr.Objective;
                if (!IsQuestObjectiveCompletable(objectiveItr.QuestStatusPair.Status.Slot, qInfo, objective))
                    continue;

                // hide quest if player is in raid-group and quest is no raid quest
                if (GetGroup() && GetGroup().IsRaidGroup() && !qInfo.IsAllowedInRaid(GetMap().GetDifficultyID()))
                    if (!InBattleground()) //there are two ways.. we can make every bg-quest a raidquest, or add this code here.. i don't know if this can be exploited by other quests, but i think all other quests depend on a specific area.. but keep this in mind, if something strange happens later
                        continue;

                if (!IsQuestObjectiveComplete(objectiveItr.QuestStatusPair.Status.Slot, qInfo, objective))
                    return true;
            }

            // This part - for ItemDrop
            foreach (var questStatus in m_QuestStatus)
            {
                if (questStatus.Value.Status != QuestStatus.Incomplete)
                    continue;

                Quest qInfo = Global.ObjectMgr.GetQuestTemplate(questStatus.Key);
                // hide quest if player is in raid-group and quest is no raid quest
                if (GetGroup() && GetGroup().IsRaidGroup() && !qInfo.IsAllowedInRaid(GetMap().GetDifficultyID()))
                    if (!InBattleground())
                        continue;

                for (byte j = 0; j < SharedConst.QuestItemDropCount; ++j)
                {
                    // examined item is a source item
                    if (qInfo.ItemDrop[j] != itemid)
                        continue;

                    ItemTemplate pProto = Global.ObjectMgr.GetItemTemplate(itemid);

                    // allows custom amount drop when not 0
                    uint maxAllowedCount = qInfo.ItemDropQuantity[j] != 0 ? qInfo.ItemDropQuantity[j] : pProto.GetMaxStackSize();

                    // 'unique' item
                    if (pProto.GetMaxCount() != 0 && pProto.GetMaxCount() < maxAllowedCount)
                        maxAllowedCount = pProto.GetMaxCount();

                    if (GetItemCount(itemid, true) < maxAllowedCount)
                        return true;
                }
            }

            return false;
        }

        public int GetQuestObjectiveData(QuestObjective objective)
        {
            ushort slot = FindQuestSlot(objective.QuestID);
            if (slot >= SharedConst.MaxQuestLogSize)
                return 0;

            return GetQuestSlotObjectiveData(slot, objective);
        }

        public void SetQuestObjectiveData(QuestObjective objective, int data)
        {
            if (objective.StorageIndex < 0)
            {
                Log.outError(LogFilter.Player, $"Player.SetQuestObjectiveData: called for quest {objective.QuestID} with invalid StorageIndex {objective.StorageIndex} (objective data is not tracked)");
                return;
            }

            var status = m_QuestStatus.LookupByKey(objective.QuestID);
            if (status == null)
            {
                Log.outError(LogFilter.Player, $"Player.SetQuestObjectiveData: player '{GetName()}' ({GetGUID()}) doesn't have quest status data (QuestID: {objective.QuestID})");
                return;
            }
            if (objective.StorageIndex >= SharedConst.MaxQuestCounts)
            {
                Log.outError(LogFilter.Player, $"Player.SetQuestObjectiveData: player '{GetName()}' ({GetGUID()}) quest {objective.QuestID} out of range StorageIndex {objective.StorageIndex}");
                return;
            }

            if (status.Slot >= SharedConst.MaxQuestLogSize)
                return;

            // No change
            int oldData = GetQuestSlotObjectiveData(status.Slot, objective);
            if (oldData == data)
                return;

            Quest quest = Global.ObjectMgr.GetQuestTemplate(objective.QuestID);
            if (quest != null)
                Global.ScriptMgr.OnQuestObjectiveChange(this, quest, objective, oldData, data);

            // Add to save
            m_QuestStatusSave[objective.QuestID] = QuestSaveType.Default;

            // Update quest fields
            if (!objective.IsStoringFlag())
                SetQuestSlotCounter(status.Slot, (byte)objective.StorageIndex, (ushort)data);
            else if (data != 0)
                SetQuestSlotObjectiveFlag(status.Slot, objective.StorageIndex);
            else
                RemoveQuestSlotObjectiveFlag(status.Slot, objective.StorageIndex);
        }

        public bool IsQuestObjectiveCompletable(ushort slot, Quest quest, QuestObjective objective)
        {
            Cypher.Assert(objective.QuestID == quest.Id);

            if (objective.Flags.HasAnyFlag(QuestObjectiveFlags.PartOfProgressBar))
            {
                // delegate check to actual progress bar objective
                var progressBarObjective = quest.Objectives.Find(otherObjective => otherObjective.Type == QuestObjectiveType.ProgressBar && !otherObjective.Flags.HasAnyFlag(QuestObjectiveFlags.PartOfProgressBar));
                if (progressBarObjective == null)
                    return false;

                return IsQuestObjectiveCompletable(slot, quest, progressBarObjective) && !IsQuestObjectiveComplete(slot, quest, progressBarObjective);
            }

            int objectiveIndex = quest.Objectives.IndexOf(objective);
            if (objectiveIndex == 0)
                return true;

            // check sequenced objectives
            int previousIndex = objectiveIndex - 1;
            bool objectiveSequenceSatisfied = true;
            bool previousSequencedObjectiveComplete = false;
            int previousSequencedObjectiveIndex = -1;
            do
            {
                QuestObjective previousObjective = quest.Objectives[previousIndex];
                if (previousObjective.Flags.HasAnyFlag(QuestObjectiveFlags.Sequenced))
                {
                    previousSequencedObjectiveIndex = previousIndex;
                    previousSequencedObjectiveComplete = IsQuestObjectiveComplete(slot, quest, previousObjective);
                    break;
                }

                if (objectiveSequenceSatisfied)
                    objectiveSequenceSatisfied = IsQuestObjectiveComplete(slot, quest, previousObjective) || previousObjective.Flags.HasAnyFlag(QuestObjectiveFlags.Optional | QuestObjectiveFlags.PartOfProgressBar);

                --previousIndex;
            } while (previousIndex >= 0);

            if (objective.Flags.HasAnyFlag(QuestObjectiveFlags.Sequenced))
            {
                if (previousSequencedObjectiveIndex == -1)
                    return objectiveSequenceSatisfied;
                if (!previousSequencedObjectiveComplete || !objectiveSequenceSatisfied)
                    return false;
            }
            else if (!previousSequencedObjectiveComplete && previousSequencedObjectiveIndex != -1)
            {
                if (!IsQuestObjectiveCompletable(slot, quest, quest.Objectives[previousSequencedObjectiveIndex]))
                    return false;
            }

            return true;
        }

        public bool IsQuestObjectiveComplete(ushort slot, Quest quest, QuestObjective objective)
        {
            switch (objective.Type)
            {
                case QuestObjectiveType.Monster:
                case QuestObjectiveType.Item:
                case QuestObjectiveType.GameObject:
                case QuestObjectiveType.TalkTo:
                case QuestObjectiveType.PlayerKills:
                case QuestObjectiveType.WinPvpPetBattles:
                case QuestObjectiveType.HaveCurrency:
                case QuestObjectiveType.ObtainCurrency:
                case QuestObjectiveType.IncreaseReputation:
                    if (GetQuestSlotObjectiveData(slot, objective) < objective.Amount)
                        return false;
                    break;
                case QuestObjectiveType.MinReputation:
                    if (GetReputationMgr().GetReputation((uint)objective.ObjectID) < objective.Amount)
                        return false;
                    break;
                case QuestObjectiveType.MaxReputation:
                    if (GetReputationMgr().GetReputation((uint)objective.ObjectID) > objective.Amount)
                        return false;
                    break;
                case QuestObjectiveType.Money:
                    if (!HasEnoughMoney(objective.Amount))
                        return false;
                    break;
                case QuestObjectiveType.AreaTrigger:
                case QuestObjectiveType.WinPetBattleAgainstNpc:
                case QuestObjectiveType.DefeatBattlePet:
                case QuestObjectiveType.CriteriaTree:
                case QuestObjectiveType.AreaTriggerEnter:
                case QuestObjectiveType.AreaTriggerExit:
                    if (GetQuestSlotObjectiveData(slot, objective) == 0)
                        return false;
                    break;
                case QuestObjectiveType.LearnSpell:
                    if (!HasSpell((uint)objective.ObjectID))
                        return false;
                    break;
                case QuestObjectiveType.Currency:
                    if (!HasCurrency((uint)objective.ObjectID, (uint)objective.Amount))
                        return false;
                    break;
                case QuestObjectiveType.ProgressBar:
                    if (!IsQuestObjectiveProgressBarComplete(slot, quest))
                        return false;
                    break;
                default:
                    Log.outError(LogFilter.Player, "Player.CanCompleteQuest: Player '{0}' ({1}) tried to complete a quest (ID: {2}) with an unknown objective type {3}",
                        GetName(), GetGUID().ToString(), objective.QuestID, objective.Type);
                    return false;
            }

            return true;
        }

        public bool IsQuestObjectiveProgressBarComplete(ushort slot, Quest quest)
        {
            float progress = 0.0f;
            foreach (QuestObjective obj in quest.Objectives)
            {
                if (obj.Flags.HasAnyFlag(QuestObjectiveFlags.PartOfProgressBar))
                {
                    progress += GetQuestSlotObjectiveData(slot, obj) * obj.ProgressBarWeight;
                    if (progress >= 100.0f)
                        return true;
                }
            }

            return false;
        }

        public void SendQuestComplete(uint questId)
        {
            if (questId != 0)
            {
                QuestUpdateComplete data = new();
                data.QuestID = questId;
                SendPacket(data);
            }
        }

        public void SendQuestReward(Quest quest, Creature questGiver, uint xp, bool hideChatMessage)
        {
            uint questId = quest.Id;
            Global.GameEventMgr.HandleQuestComplete(questId);

            uint moneyReward;

            if (GetLevel() < WorldConfig.GetIntValue(WorldCfg.MaxPlayerLevel))
            {
                moneyReward = GetQuestMoneyReward(quest);
            }
            else // At max level, increase gold reward
            {
                xp = 0;
                moneyReward = (uint)(GetQuestMoneyReward(quest) + (int)(quest.GetRewMoneyMaxLevel() * WorldConfig.GetFloatValue(WorldCfg.RateDropMoney)));
            }

            QuestGiverQuestComplete packet = new();

            packet.QuestID = questId;
            packet.MoneyReward = moneyReward;
            packet.XPReward = xp;
            packet.SkillLineIDReward = quest.RewardSkillId;
            packet.NumSkillUpsReward = quest.RewardSkillPoints;

            if (questGiver)
            {
                if (questGiver.IsGossip())
                    packet.LaunchGossip = true;
                else if (questGiver.IsQuestGiver())
                    packet.LaunchQuest = true;
                else if (quest.NextQuestInChain != 0 && !quest.HasFlag(QuestFlags.AutoComplete))
                    packet.UseQuestReward = true;
            }

            packet.HideChatMessage = hideChatMessage;

            SendPacket(packet);
        }

        public void SendQuestFailed(uint questId, InventoryResult reason = InventoryResult.Ok)
        {
            if (questId != 0)
            {
                QuestGiverQuestFailed questGiverQuestFailed = new();
                questGiverQuestFailed.QuestID = questId;
                questGiverQuestFailed.Reason = reason; // failed reason (valid reasons: 4, 16, 50, 17, other values show default message)
                SendPacket(questGiverQuestFailed);
            }
        }

        public void SendQuestTimerFailed(uint questId)
        {
            if (questId != 0)
            {
                QuestUpdateFailedTimer questUpdateFailedTimer = new();
                questUpdateFailedTimer.QuestID = questId;
                SendPacket(questUpdateFailedTimer);
            }
        }

        public void SendCanTakeQuestResponse(QuestFailedReasons reason, bool sendErrorMessage = true, string reasonText = "")
        {
            QuestGiverInvalidQuest questGiverInvalidQuest = new();

            questGiverInvalidQuest.Reason = reason;
            questGiverInvalidQuest.SendErrorMessage = sendErrorMessage;
            questGiverInvalidQuest.ReasonText = reasonText;

            SendPacket(questGiverInvalidQuest);
        }

        public void SendQuestConfirmAccept(Quest quest, Player receiver)
        {
            if (!receiver)
                return;

            QuestConfirmAcceptResponse packet = new();

            packet.QuestTitle = quest.LogTitle;

            Locale loc_idx = receiver.GetSession().GetSessionDbLocaleIndex();
            if (loc_idx != Locale.enUS)
            {
                QuestTemplateLocale questLocale = Global.ObjectMgr.GetQuestLocale(quest.Id);
                if (questLocale != null)
                    ObjectManager.GetLocaleString(questLocale.LogTitle, loc_idx, ref packet.QuestTitle);
            }

            packet.QuestID = quest.Id;
            packet.InitiatedBy = GetGUID();

            receiver.SendPacket(packet);
        }

        public void SendPushToPartyResponse(Player player, QuestPushReason reason, Quest quest = null)
        {
            if (player != null)
            {
                QuestPushResultResponse response = new();
                response.SenderGUID = player.GetGUID();
                response.Result = reason;
                if (quest != null)
                {
                    response.QuestTitle = quest.LogTitle;
                    Locale localeConstant = GetSession().GetSessionDbLocaleIndex();
                    if (localeConstant != Locale.enUS)
                    {
                        QuestTemplateLocale questTemplateLocale = Global.ObjectMgr.GetQuestLocale(quest.Id);
                        if (questTemplateLocale != null)
                            ObjectManager.GetLocaleString(questTemplateLocale.LogTitle, localeConstant, ref response.QuestTitle);
                    }
                }

                SendPacket(response);
            }
        }

        void SendQuestUpdateAddCredit(Quest quest, ObjectGuid guid, QuestObjective obj, uint count)
        {
            QuestUpdateAddCredit packet = new();
            packet.VictimGUID = guid;
            packet.QuestID = quest.Id;
            packet.ObjectID = obj.ObjectID;
            packet.Count = (ushort)count;
            packet.Required = (ushort)obj.Amount;
            packet.ObjectiveType = (byte)obj.Type;
            SendPacket(packet);
        }

        public void SendQuestUpdateAddCreditSimple(QuestObjective obj)
        {
            QuestUpdateAddCreditSimple packet = new();
            packet.QuestID = obj.QuestID;
            packet.ObjectID = obj.ObjectID;
            packet.ObjectiveType = obj.Type;
            SendPacket(packet);
        }

        public void SendQuestUpdateAddPlayer(Quest quest, uint newCount)
        {
            QuestUpdateAddPvPCredit packet = new();
            packet.QuestID = quest.Id;
            packet.Count = (ushort)newCount;
            SendPacket(packet);
        }

        public void SendQuestGiverStatusMultiple()
        {
            QuestGiverStatusMultiple response = new();

            foreach (var itr in m_clientGUIDs)
            {
                if (itr.IsAnyTypeCreature())
                {
                    // need also pet quests case support
                    Creature questgiver = ObjectAccessor.GetCreatureOrPetOrVehicle(this, itr);
                    if (!questgiver || questgiver.IsHostileTo(this))
                        continue;

                    if (!questgiver.HasNpcFlag(NPCFlags.QuestGiver))
                        continue;

                    response.QuestGiver.Add(new QuestGiverInfo(questgiver.GetGUID(), GetQuestDialogStatus(questgiver)));
                }
                else if (itr.IsGameObject())
                {
                    GameObject questgiver = GetMap().GetGameObject(itr);
                    if (!questgiver || questgiver.GetGoType() != GameObjectTypes.QuestGiver)
                        continue;

                    response.QuestGiver.Add(new QuestGiverInfo(questgiver.GetGUID(), GetQuestDialogStatus(questgiver)));
                }
            }

            SendPacket(response);
        }

        public bool HasPvPForcingQuest()
        {
            for (byte i = 0; i < SharedConst.MaxQuestLogSize; ++i)
            {
                uint questId = GetQuestSlotQuestId(i);
                if (questId == 0)
                    continue;

                Quest quest = Global.ObjectMgr.GetQuestTemplate(questId);
                if (quest == null)
                    continue;

                if (quest.HasFlag(QuestFlags.Pvp))
                    return true;
            }

            return false;
        }

        public bool HasQuestForGO(int GOId)
        {
            foreach (var objectiveStatusData in m_questObjectiveStatus.LookupByKey((QuestObjectiveType.GameObject, GOId)))
            {
                Quest qInfo = Global.ObjectMgr.GetQuestTemplate(objectiveStatusData.QuestStatusPair.QuestID);
                QuestObjective objective = objectiveStatusData.Objective;
                if (!IsQuestObjectiveCompletable(objectiveStatusData.QuestStatusPair.Status.Slot, qInfo, objective))
                    continue;

                // hide quest if player is in raid-group and quest is no raid quest
                if (GetGroup() && GetGroup().IsRaidGroup() && !qInfo.IsAllowedInRaid(GetMap().GetDifficultyID()))
                    if (!InBattleground()) //there are two ways.. we can make every bg-quest a raidquest, or add this code here.. i don't know if this can be exploited by other quests, but i think all other quests depend on a specific area.. but keep this in mind, if something strange happens later
                        continue;

                if (!IsQuestObjectiveComplete(objectiveStatusData.QuestStatusPair.Status.Slot, qInfo, objective))
                    return true;
            }

            return false;
        }

        public void UpdateVisibleGameobjectsOrSpellClicks()
        {
            if (m_clientGUIDs.Empty())
                return;

            UpdateData udata = new(GetMapId());
            UpdateObject packet;
            foreach (var guid in m_clientGUIDs)
            {
                if (guid.IsGameObject())
                {
                    GameObject obj = ObjectAccessor.GetGameObject(this, guid);
                    if (obj != null)
                    {
                        ObjectFieldData objMask = new();
                        GameObjectFieldData goMask = new();

                        if (m_questObjectiveStatus.ContainsKey((QuestObjectiveType.GameObject, (int)obj.GetEntry())))
                            objMask.MarkChanged(obj.m_objectData.DynamicFlags);

                        switch (obj.GetGoType())
                        {
                            case GameObjectTypes.QuestGiver:
                            case GameObjectTypes.Chest:
                            case GameObjectTypes.Goober:
                            case GameObjectTypes.Generic:
                                if (Global.ObjectMgr.IsGameObjectForQuests(obj.GetEntry()))
                                    objMask.MarkChanged(obj.m_objectData.DynamicFlags);
                                break;
                            default:
                                break;
                        }

                        if (objMask.GetUpdateMask().IsAnySet() || goMask.GetUpdateMask().IsAnySet())
                            obj.BuildValuesUpdateForPlayerWithMask(udata, objMask.GetUpdateMask(), goMask.GetUpdateMask(), this);
                    }
                }
                else if (guid.IsCreatureOrVehicle())
                {
                    Creature obj = ObjectAccessor.GetCreatureOrPetOrVehicle(this, guid);
                    if (obj == null)
                        continue;

                    // check if this unit requires quest specific flags
                    if (!obj.HasNpcFlag(NPCFlags.SpellClick))
                        continue;

                    var clickBounds = Global.ObjectMgr.GetSpellClickInfoMapBounds(obj.GetEntry());
                    foreach (var spellClickInfo in clickBounds)
                    {
                        List<Condition> conds = Global.ConditionMgr.GetConditionsForSpellClickEvent(obj.GetEntry(), spellClickInfo.spellId);
                        if (conds != null)
                        {
                            ObjectFieldData objMask = new();
                            UnitData unitMask = new();
                            unitMask.MarkChanged(m_unitData.NpcFlags, 0); // NpcFlags[0] has UNIT_NPC_FLAG_SPELLCLICK
                            obj.BuildValuesUpdateForPlayerWithMask(udata, objMask.GetUpdateMask(), unitMask.GetUpdateMask(), this);
                            break;
                        }
                    }
                }
            }
            udata.BuildPacket(out packet);
            SendPacket(packet);
        }

        void SetDailyQuestStatus(uint quest_id)
        {
            Quest qQuest = Global.ObjectMgr.GetQuestTemplate(quest_id);
            if (qQuest != null)
            {
                if (!qQuest.IsDFQuest())
                {
                    AddDynamicUpdateFieldValue(m_values.ModifyValue(m_activePlayerData).ModifyValue(m_activePlayerData.DailyQuestsCompleted), quest_id);
                    m_lastDailyQuestTime = GameTime.GetGameTime();              // last daily quest time
                    m_DailyQuestChanged = true;

                }
                else
                {
                    m_DFQuests.Add(quest_id);
                    m_lastDailyQuestTime = GameTime.GetGameTime();
                    m_DailyQuestChanged = true;
                }
            }
        }

        public bool IsDailyQuestDone(uint quest_id)
        {
            return m_activePlayerData.DailyQuestsCompleted.FindIndex(quest_id) >= 0;
        }

        void SetWeeklyQuestStatus(uint quest_id)
        {
            m_weeklyquests.Add(quest_id);
            m_WeeklyQuestChanged = true;
        }

        void SetSeasonalQuestStatus(uint quest_id)
        {
            Quest quest = Global.ObjectMgr.GetQuestTemplate(quest_id);
            if (quest == null)
                return;

            m_seasonalquests.Add(quest.GetEventIdForQuest(), quest_id);
            m_SeasonalQuestChanged = true;
        }

        void SetMonthlyQuestStatus(uint quest_id)
        {
            m_monthlyquests.Add(quest_id);
            m_MonthlyQuestChanged = true;
        }

        void PushQuests()
        {
            foreach (Quest quest in Global.ObjectMgr.GetQuestTemplatesAutoPush())
            {
                if (quest.GetQuestTag() != 0 && quest.GetQuestTag() != QuestTagType.Tag)
                    continue;

                if (!quest.IsUnavailable() && CanTakeQuest(quest, false))
                    AddQuestAndCheckCompletion(quest, null);
            }
        }
    }
}
