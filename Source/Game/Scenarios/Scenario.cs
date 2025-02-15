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
using Game.Achievements;
using Game.DataStorage;
using Game.Entities;
using Game.Networking;
using Game.Networking.Packets;
using System;
using System.Collections.Generic;

namespace Game.Scenarios
{
    public class Scenario : CriteriaHandler
    {
        public Scenario(ScenarioData scenarioData)
        {
            _data = scenarioData;
            _currentstep = null;

            //ASSERT(_data);

            foreach (var scenarioStep in _data.Steps.Values)
                SetStepState(scenarioStep, ScenarioStepState.NotStarted);

            ScenarioStepRecord firstStep = GetFirstStep();
            if (firstStep != null)
                SetStep(firstStep);
            else
                Log.outError(LogFilter.Scenario, "Scenario.Scenario: Could not launch Scenario (id: {0}), found no valid scenario step", _data.Entry.Id);
        }

        ~Scenario()
        {
            foreach (ObjectGuid guid in _players)
            {
                Player player = Global.ObjAccessor.FindPlayer(guid);
                if (player)
                    SendBootPlayer(player);
            }

            _players.Clear();
        }

        public override void Reset()
        {
            base.Reset();
            SetStep(GetFirstStep());
        }

        public virtual void CompleteStep(ScenarioStepRecord step)
        {
            Quest quest = Global.ObjectMgr.GetQuestTemplate(step.RewardQuestID);
            if (quest != null)
            {
                foreach (ObjectGuid guid in _players)
                {
                    Player player = Global.ObjAccessor.FindPlayer(guid);
                    if (player)
                        player.RewardQuest(quest, LootItemType.Item, 0, null, false);
                }
            }

            if (step.IsBonusObjective())
                return;

            ScenarioStepRecord newStep = null;
            foreach (var scenarioStep in _data.Steps.Values)
            {
                if (scenarioStep.IsBonusObjective())
                    continue;

                if (GetStepState(scenarioStep) == ScenarioStepState.Done)
                    continue;

                if (newStep == null || scenarioStep.OrderIndex < newStep.OrderIndex)
                    newStep = scenarioStep;
            }

            SetStep(newStep);
            if (IsComplete())
                CompleteScenario();
            else
                Log.outError(LogFilter.Scenario, "Scenario.CompleteStep: Scenario (id: {0}, step: {1}) was completed, but could not determine new step, or validate scenario completion.", step.ScenarioID, step.Id);
        }

        public virtual void CompleteScenario()
        {
            SendPacket(new ScenarioCompleted(_data.Entry.Id));
        }

        void SetStep(ScenarioStepRecord step)
        {
            _currentstep = step;
            if (step != null)
                SetStepState(step, ScenarioStepState.InProgress);

            ScenarioState scenarioState = new();
            BuildScenarioState(scenarioState);
            SendPacket(scenarioState);
        }

        public virtual void OnPlayerEnter(Player player)
        {
            _players.Add(player.GetGUID());
            SendScenarioState(player);
        }

        public virtual void OnPlayerExit(Player player)
        {
            _players.Remove(player.GetGUID());
            SendBootPlayer(player);
        }

        bool IsComplete()
        {
            foreach (var scenarioStep in _data.Steps.Values)
            {
                if (scenarioStep.IsBonusObjective())
                    continue;

                if (GetStepState(scenarioStep) != ScenarioStepState.Done)
                    return false;
            }

            return true;
        }

        public ScenarioRecord GetEntry()
        {
            return _data.Entry;
        }

        ScenarioStepState GetStepState(ScenarioStepRecord step)
        {
            if (!_stepStates.ContainsKey(step))
                return ScenarioStepState.Invalid;

            return _stepStates[step];
        }

        public override void SendCriteriaUpdate(Criteria criteria, CriteriaProgress progress, TimeSpan timeElapsed, bool timedCompleted)
        {
            ScenarioProgressUpdate progressUpdate = new();
            progressUpdate.CriteriaProgress.Id = criteria.Id;
            progressUpdate.CriteriaProgress.Quantity = progress.Counter;
            progressUpdate.CriteriaProgress.Player = progress.PlayerGUID;
            progressUpdate.CriteriaProgress.Date = progress.Date;
            if (criteria.Entry.StartTimer != 0)
                progressUpdate.CriteriaProgress.Flags = timedCompleted ? 1 : 0u;

            progressUpdate.CriteriaProgress.TimeFromStart = (uint)timeElapsed.TotalSeconds;
            progressUpdate.CriteriaProgress.TimeFromCreate = 0;

            SendPacket(progressUpdate);
        }

        public override bool CanUpdateCriteriaTree(Criteria criteria, CriteriaTree tree, Player referencePlayer)
        {
            ScenarioStepRecord step = tree.ScenarioStep;
            if (step == null)
                return false;

            if (step.ScenarioID != _data.Entry.Id)
                return false;

            ScenarioStepRecord currentStep = GetStep();
            if (currentStep == null)
                return false;

            if (step.IsBonusObjective())
                return true;

            return currentStep == step;
        }

        public override bool CanCompleteCriteriaTree(CriteriaTree tree)
        {
            ScenarioStepRecord step = tree.ScenarioStep;
            if (step == null)
                return false;

            if (step.ScenarioID != _data.Entry.Id)
                return false;

            if (step.IsBonusObjective())
                return !IsComplete();

            if (step != GetStep())
                return false;

            return true;
        }

        public override void CompletedCriteriaTree(CriteriaTree tree, Player referencePlayer)
        {
            ScenarioStepRecord step = tree.ScenarioStep;
            if (step == null)
                return;

            if (!step.IsBonusObjective() && step != GetStep())
                return;

            if (GetStepState(step) == ScenarioStepState.Done)
                return;

            SetStepState(step, ScenarioStepState.Done);
            CompleteStep(step);
        }

        public override void SendPacket(ServerPacket data)
        {
            foreach (ObjectGuid guid in _players)
            {
                Player player = Global.ObjAccessor.FindPlayer(guid);
                if (player)
                    player.SendPacket(data);
            }
        }

        void BuildScenarioState(ScenarioState scenarioState)
        {
            scenarioState.ScenarioID = (int)_data.Entry.Id;
            ScenarioStepRecord step = GetStep();
            if (step != null)
                scenarioState.CurrentStep = (int)step.Id;
            scenarioState.CriteriaProgress = GetCriteriasProgress();
            scenarioState.BonusObjectives = GetBonusObjectivesData();
            // Don't know exactly what this is for, but seems to contain list of scenario steps that we're either on or that are completed
            foreach (var state in _stepStates)
            {
                if (state.Key.IsBonusObjective())
                    continue;

                switch (state.Value)
                {
                    case ScenarioStepState.InProgress:
                    case ScenarioStepState.Done:
                        break;
                    case ScenarioStepState.NotStarted:
                    default:
                        continue;
                }

                scenarioState.PickedSteps.Add(state.Key.Id);
            }
            scenarioState.ScenarioComplete = IsComplete();
        }

        ScenarioStepRecord GetFirstStep()
        {
            // Do it like this because we don't know what order they're in inside the container.
            ScenarioStepRecord firstStep = null;
            foreach (var scenarioStep in _data.Steps.Values)
            {
                if (scenarioStep.IsBonusObjective())
                    continue;

                if (firstStep == null || scenarioStep.OrderIndex < firstStep.OrderIndex)
                    firstStep = scenarioStep;
            }

            return firstStep;
        }

        public ScenarioStepRecord GetLastStep()
        {
            // Do it like this because we don't know what order they're in inside the container.
            ScenarioStepRecord lastStep = null;
            foreach (var scenarioStep in _data.Steps.Values)
            {
                if (scenarioStep.IsBonusObjective())
                    continue;

                if (lastStep == null || scenarioStep.OrderIndex > lastStep.OrderIndex)
                    lastStep = scenarioStep;
            }

            return lastStep;
        }
        
        public void SendScenarioState(Player player)
        {
            ScenarioState scenarioState = new();
            BuildScenarioState(scenarioState);
            player.SendPacket(scenarioState);
        }

        List<BonusObjectiveData> GetBonusObjectivesData()
        {
            List<BonusObjectiveData> bonusObjectivesData = new();
            foreach (var scenarioStep in _data.Steps.Values)
            {
                if (!scenarioStep.IsBonusObjective())
                    continue;

                if (Global.CriteriaMgr.GetCriteriaTree(scenarioStep.CriteriaTreeId) != null)
                {
                    BonusObjectiveData bonusObjectiveData;
                    bonusObjectiveData.BonusObjectiveID = (int)scenarioStep.Id;
                    bonusObjectiveData.ObjectiveComplete = GetStepState(scenarioStep) == ScenarioStepState.Done;
                    bonusObjectivesData.Add(bonusObjectiveData);
                }
            }

            return bonusObjectivesData;
        }

        List<CriteriaProgressPkt> GetCriteriasProgress()
        {
            List<CriteriaProgressPkt> criteriasProgress = new();

            if (!_criteriaProgress.Empty())
            {
                foreach (var pair in _criteriaProgress)
                {
                    CriteriaProgressPkt criteriaProgress = new();
                    criteriaProgress.Id = pair.Key;
                    criteriaProgress.Quantity = pair.Value.Counter;
                    criteriaProgress.Date = pair.Value.Date;
                    criteriaProgress.Player = pair.Value.PlayerGUID;
                    criteriasProgress.Add(criteriaProgress);
                }
            }

            return criteriasProgress;
        }

        public override List<Criteria> GetCriteriaByType(CriteriaType type, uint asset)
        {
            return Global.CriteriaMgr.GetScenarioCriteriaByType(type);
        }

        void SendBootPlayer(Player player)
        {
            ScenarioVacate scenarioBoot = new();
            scenarioBoot.ScenarioID = (int)_data.Entry.Id;
            player.SendPacket(scenarioBoot);
        }

        public virtual void Update(uint diff) { }

        public void SetStepState(ScenarioStepRecord step, ScenarioStepState state) { _stepStates[step] = state; }
        public ScenarioStepRecord GetStep()
        {
            return _currentstep;
        }

        public override void SendCriteriaProgressRemoved(uint criteriaId) { }
        public override void AfterCriteriaTreeUpdate(CriteriaTree tree, Player referencePlayer) { }
        public override void SendAllData(Player receiver) { }

        List<ObjectGuid> _players = new();
        ScenarioData _data;
        ScenarioStepRecord _currentstep;
        Dictionary<ScenarioStepRecord, ScenarioStepState> _stepStates = new();
    }

    public enum ScenarioStepState
    {
        Invalid = 0,
        NotStarted = 1,
        InProgress = 2,
        Done = 3
    }

    enum ScenarioType
    {
        Scenario = 0,
        ChallengeMode = 1,
        Solo = 2,
        Dungeon = 10,
    }

}
