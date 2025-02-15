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

using Framework.Dynamic;
using Game.Entities;
using System.Numerics;

namespace Game.Movement
{
    public class MoveSplineInitArgs
    {
        public MoveSplineInitArgs(int path_capacity = 16)
        {
            path_Idx_offset = 0;
            velocity = 0.0f;
            parabolic_amplitude = 0.0f;
            time_perc = 0.0f;
            splineId = 0;
            initialOrientation = 0.0f;
            HasVelocity = false;
            TransformForTransport = true;
            path = new Vector3[path_capacity];
        }

        public Vector3[] path;
        public FacingInfo facing = new();
        public MoveSplineFlag flags = new();
        public int path_Idx_offset;
        public float velocity;
        public float parabolic_amplitude;
        public float vertical_acceleration;
        public float time_perc;
        public uint splineId;
        public float initialOrientation;
        public Optional<SpellEffectExtraData> spellEffectExtra;
        public Optional<AnimTierTransition> animTier;
        public bool walk;
        public bool HasVelocity;
        public bool TransformForTransport;

        // Returns true to show that the arguments were configured correctly and MoveSpline initialization will succeed.
        public bool Validate(Unit unit)
        {
            bool CHECK(bool exp)
            {
                if (!exp)
                {
                    Log.outError(LogFilter.Misc, "MoveSplineInitArgs::Validate: expression '{0}' failed for {1} Entry: {2}", exp.ToString(), unit.GetGUID().ToString(), unit.GetEntry());
                    return false;
                }
                return true;
            }

            if (!CHECK(path.Length > 1))
                return false;
            if (!CHECK(velocity >= 0.01f))
                return false;
            if (!CHECK(time_perc >= 0.0f && time_perc <= 1.0f))
                return false;
            if (!CHECK(_checkPathLengths()))
                return false;
            return true;
        }

        bool _checkPathLengths()
        {
            if (path.Length > 2 || facing.type == Framework.Constants.MonsterMoveType.Normal)
                for (uint i = 0; i < path.Length - 1; ++i)
                    if ((path[i + 1] - path[i]).Length() < 0.1f)
                        return false;
            return true;
        }
    }

    public class SpellEffectExtraData
    {
        public ObjectGuid Target;
        public uint SpellVisualId;
        public uint ProgressCurveId;
        public uint ParabolicCurveId;
    }

    public class AnimTierTransition
    {
        public uint TierTransitionId;
        public byte AnimTier;
    }
}
