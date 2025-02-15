/*
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
using Game.AI;
using Game.Entities;
using Game.Scripting;
using Game.Spells;
using System;

namespace Scripts.World
{
    [Script]
    class trigger_periodic : NullCreatureAI
    {
        public trigger_periodic(Creature creature) : base(creature)
        {
            var interval = me.GetBaseAttackTime(WeaponAttackType.BaseAttack);
            var spell = me.m_spells[0] != 0 ? Global.SpellMgr.GetSpellInfo(me.m_spells[0], me.GetMap().GetDifficultyID()) : null;

            _scheduler.Schedule(TimeSpan.FromMilliseconds(interval), task =>
            {
                if (spell != null)
                    me.CastSpell(me, spell.Id, new CastSpellExtraArgs(TriggerCastFlags.FullMask).SetCastDifficulty(spell.Difficulty));
                task.Repeat(TimeSpan.FromMilliseconds(interval));
            });
        }

        public override void UpdateAI(uint diff)
        {
            _scheduler.Update();
        }
    }
}