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
using Game.Entities;
using System;

namespace Game.Movement
{
    public class RandomMovementGenerator : MovementGeneratorMedium<Creature>
    {
        public RandomMovementGenerator(float spawnDist = 0.0f)
        {
            _timer = new TimeTracker();
            _reference = new();
            _wanderDistance = spawnDist;

            Mode = MovementGeneratorMode.Default;
            Priority = MovementGeneratorPriority.Normal;
            Flags = MovementGeneratorFlags.InitializationPending;
            BaseUnitState = UnitState.Roaming;
        }

        public override void DoInitialize(Creature owner)
        {
            RemoveFlag(MovementGeneratorFlags.InitializationPending | MovementGeneratorFlags.Transitory | MovementGeneratorFlags.Deactivated | MovementGeneratorFlags.Paused);
            AddFlag(MovementGeneratorFlags.Initialized);

            if (owner == null || !owner.IsAlive())
                return;

            _reference = owner.GetPosition();
            owner.StopMoving();

            if (_wanderDistance == 0f)
                _wanderDistance = owner.GetWanderDistance();

            // Retail seems to let a creature walk 2 up to 10 splines before triggering a pause
            _wanderSteps = RandomHelper.URand(2, 10);

            _timer.Reset(0);
            _path = null;
        }

        public override void DoReset(Creature owner)
        {
            RemoveFlag(MovementGeneratorFlags.Transitory | MovementGeneratorFlags.Deactivated);
            DoInitialize(owner);
        }

        public override bool DoUpdate(Creature owner, uint diff)
        {
            if (!owner || !owner.IsAlive())
                return true;

            if (HasFlag(MovementGeneratorFlags.Finalized | MovementGeneratorFlags.Paused))
                return true;

            if (owner.HasUnitState(UnitState.NotMove) || owner.IsMovementPreventedByCasting())
            {
                AddFlag(MovementGeneratorFlags.Interrupted);
                owner.StopMoving();
                _path = null;
                return true;
            }
            else
                RemoveFlag(MovementGeneratorFlags.Interrupted);

            _timer.Update(diff);
            if ((HasFlag(MovementGeneratorFlags.SpeedUpdatePending) && !owner.MoveSpline.Finalized()) || (_timer.Passed() && owner.MoveSpline.Finalized()))
                SetRandomLocation(owner);

            return true;
        }

        public override void DoDeactivate(Creature owner)
        {
            AddFlag(MovementGeneratorFlags.Deactivated);
            owner.ClearUnitState(UnitState.RoamingMove);
        }

        public override void DoFinalize(Creature owner, bool active, bool movementInform)
        {
            AddFlag(MovementGeneratorFlags.Finalized);
            if (active)
            {
                owner.ClearUnitState(UnitState.RoamingMove);
                owner.StopMoving();

                // TODO: Research if this modification is needed, which most likely isnt
                owner.SetWalk(false);
            }
        }

        public override void Pause(uint timer = 0)
        {
            if (timer != 0)
            {
                AddFlag(MovementGeneratorFlags.TimedPaused);
                _timer.Reset(timer);
                RemoveFlag(MovementGeneratorFlags.Paused);
            }
            else
            {
                AddFlag(MovementGeneratorFlags.Paused);
                RemoveFlag(MovementGeneratorFlags.TimedPaused);
            }
        }

        public override void Resume(uint overrideTimer = 0)
        {
            if (overrideTimer != 0)
                _timer.Reset(overrideTimer);

            RemoveFlag(MovementGeneratorFlags.Paused);
        }

        void SetRandomLocation(Creature owner)
        {
            if (owner == null)
                return;

            if (owner.HasUnitState(UnitState.NotMove | UnitState.LostControl) || owner.IsMovementPreventedByCasting())
            {
                AddFlag(MovementGeneratorFlags.Interrupted);
                owner.StopMoving();
                _path = null;
                return;
            }

            Position position = new(_reference);
            float distance = RandomHelper.FRand(0.0f, _wanderDistance);
            float angle = RandomHelper.FRand(0.0f, MathF.PI * 2.0f);
            owner.MovePositionToFirstCollision(position, distance, angle);

            // Check if the destination is in LOS
            if (!owner.IsWithinLOS(position.GetPositionX(), position.GetPositionY(), position.GetPositionZ()))
            {
                // Retry later on
                _timer.Reset(200);
                return;
            }

            if (_path == null)
            {
                _path = new PathGenerator(owner);
                _path.SetPathLengthLimit(30.0f);
            }

            bool result = _path.CalculatePath(position.GetPositionX(), position.GetPositionY(), position.GetPositionZ());
            if (!result || _path.GetPathType().HasFlag(PathType.NoPath) || _path.GetPathType().HasFlag(PathType.Shortcut) || _path.GetPathType().HasFlag(PathType.FarFromPoly))
            {
                _timer.Reset(100);
                return;
            }

            RemoveFlag(MovementGeneratorFlags.Transitory | MovementGeneratorFlags.TimedPaused);

            owner.AddUnitState(UnitState.RoamingMove);

            bool walk = true;
            switch (owner.GetMovementTemplate().GetRandom())
            {
                case CreatureRandomMovementType.CanRun:
                    walk = owner.IsWalking();
                    break;
                case CreatureRandomMovementType.AlwaysRun:
                    walk = false;
                    break;
                default:
                    break;
            }

            MoveSplineInit init = new(owner);
            init.MovebyPath(_path.GetPath());
            init.SetWalk(walk);
            int splineDuration = init.Launch();

            --_wanderSteps;
            if (_wanderSteps != 0) // Creature has yet to do steps before pausing
                _timer.Reset(splineDuration);
            else
            {
                // Creature has made all its steps, time for a little break
                _timer.Reset(splineDuration + RandomHelper.URand(4, 10) * Time.InMilliseconds); // Retails seems to use rounded numbers so we do as well
                _wanderSteps = RandomHelper.URand(2, 10);
            }

            // Call for creature group update
            owner.SignalFormationMovement(position);
        }

        public override void UnitSpeedChanged() { AddFlag(MovementGeneratorFlags.SpeedUpdatePending); }

        public override MovementGeneratorType GetMovementGeneratorType()
        {
            return MovementGeneratorType.Random;
        }

        PathGenerator _path;
        TimeTracker _timer;
        Position _reference;
        float _wanderDistance;
        uint _wanderSteps;
    }
}
