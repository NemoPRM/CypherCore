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
using Game.AI;
using Game.DataStorage;
using Game.Entities;
using Game.Scripting;
using Game.Spells;
using System;
using System.Collections.Generic;

namespace Scripts.Spells.Paladin
{
    struct SpellIds
    {
        public const uint AvengersShield = 31935;
        public const uint AvengingWrath = 31884;
        public const uint BeaconOfLight = 53563;
        public const uint BeaconOfLightHeal = 53652;
        public const uint BlessingOfLowerCityDruid = 37878;
        public const uint BlessingOfLowerCityPaladin = 37879;
        public const uint BlessingOfLowerCityPriest = 37880;
        public const uint BlessingOfLowerCityShaman = 37881;
        public const uint BlindingLightEffect = 105421;
        public const uint ConcentractionAura = 19746;
        public const uint ConsecratedGroundPassive = 204054;
        public const uint ConsecratedGroundSlow = 204242;
        public const uint Consecration = 26573;
        public const uint ConsecrationDamage = 81297;
        public const uint ConsecrationProtectionAura = 188370;
        public const uint DivinePurposeProc = 90174;
        public const uint DivineSteedHuman = 221883;
        public const uint DivineSteedDwarf = 276111;
        public const uint DivineSteedDraenei = 221887;
        public const uint DivineSteedDarkIronDwarf = 276112;
        public const uint DivineSteedBloodelf = 221886;
        public const uint DivineSteedTauren = 221885;
        public const uint DivineSteedZandalariTroll = 294133;
        public const uint DivineStormDamage = 224239;
        public const uint EnduringLight = 40471;
        public const uint EnduringJudgement = 40472;
        public const uint EyeForAnEyeTriggered = 205202;
        public const uint FinalStand = 204077;
        public const uint FinalStandEffect = 204079;
        public const uint Forbearance = 25771;
        public const uint GuardianOfAcientKings = 86659;
        public const uint HammerOfJustice = 853;
        public const uint HammerOfTheRighteousAoe = 88263;
        public const uint HandOfSacrifice = 6940;
        public const uint HolyMending = 64891;
        public const uint HolyPowerArmor = 28790;
        public const uint HolyPowerAttackPower = 28791;
        public const uint HolyPowerSpellPower = 28793;
        public const uint HolyPowerMp5 = 28795;
        public const uint HolyPrismAreaBeamVisual = 121551;
        public const uint HolyPrismTargetAlly = 114871;
        public const uint HolyPrismTargetEnemy = 114852;
        public const uint HolyPrismTargetBeamVisual = 114862;
        public const uint HolyShockR1 = 20473;
        public const uint HolyShockR1Damage = 25912;
        public const uint HolyShockR1Healing = 25914;
        public const uint ImmuneShieldMarker = 61988;
        public const uint ItemHealingTrance = 37706;
        public const uint JudementGainHolyPower = 220637;
        public const uint JudgementProtRetR3 = 315867;
        public const uint RighteousDefenseTaunt = 31790;
        public const uint RighteousVerdictAura = 267611;
        public const uint SealOfRighteousness = 25742;
        public const uint TemplarVerdictDamage = 224266;
        public const uint ZealAura = 269571;
    }

    struct SpellVisualKits
    {
        public const uint DivineStorm = 73892;
    }

    // 37877 - Blessing of Faith
    [Script]
    class spell_pal_blessing_of_faith : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.BlessingOfLowerCityDruid, SpellIds.BlessingOfLowerCityPaladin, SpellIds.BlessingOfLowerCityPriest, SpellIds.BlessingOfLowerCityShaman);
        }

        void HandleDummy(uint effIndex)
        {
            Unit unitTarget = GetHitUnit();
            if (unitTarget)
            {
                uint spell_id;
                switch (unitTarget.GetClass())
                {
                    case Class.Druid:
                        spell_id = SpellIds.BlessingOfLowerCityDruid;
                        break;
                    case Class.Paladin:
                        spell_id = SpellIds.BlessingOfLowerCityPaladin;
                        break;
                    case Class.Priest:
                        spell_id = SpellIds.BlessingOfLowerCityPriest;
                        break;
                    case Class.Shaman:
                        spell_id = SpellIds.BlessingOfLowerCityShaman;
                        break;
                    default:
                        return; // ignore for non-healing classes
                }
                Unit caster = GetCaster();
                caster.CastSpell(caster, spell_id, true);
            }
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new EffectHandler(HandleDummy, 0, SpellEffectName.Dummy));
        }
    }

    // 1022 - Blessing of Protection
    [Script] // 204018 - Blessing of Spellwarding
    class spell_pal_blessing_of_protection : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.Forbearance) //, SpellIds._PALADIN_IMMUNE_SHIELD_MARKER) // uncomment when we have serverside only spells
                && spellInfo.ExcludeTargetAuraSpell == SpellIds.ImmuneShieldMarker;
        }

        SpellCastResult CheckForbearance()
        {
            Unit target = GetExplTargetUnit();
            if (!target || target.HasAura(SpellIds.Forbearance))
                return SpellCastResult.TargetAurastate;

            return SpellCastResult.SpellCastOk;
        }

        void TriggerForbearance()
        {
            Unit target = GetHitUnit();
            if (target)
            {
                GetCaster().CastSpell(target, SpellIds.Forbearance, true);
                GetCaster().CastSpell(target, SpellIds.ImmuneShieldMarker, true);
            }
        }

        public override void Register()
        {
            OnCheckCast.Add(new CheckCastHandler(CheckForbearance));
            AfterHit.Add(new HitHandler(TriggerForbearance));
        }
    }

    [Script] // 115750 - Blinding Light
    class spell_pal_blinding_light : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.BlindingLightEffect);
        }

        void HandleDummy(uint effIndex)
        {
            Unit target = GetHitUnit();
            if (target)
                GetCaster().CastSpell(target, SpellIds.BlindingLightEffect, true);
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new EffectHandler(HandleDummy, 0, SpellEffectName.ApplyAura));
        }
    }

    [Script] // 26573 - Consecration
    class spell_pal_consecration : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.ConsecrationDamage, SpellIds.ConsecrationProtectionAura, SpellIds.ConsecratedGroundPassive, SpellIds.ConsecratedGroundSlow);
        }

        void HandleEffectPeriodic(AuraEffect aurEff)
        {
            AreaTrigger at = GetTarget().GetAreaTrigger(SpellIds.Consecration);
            if (at != null)
                GetTarget().CastSpell(at.GetPosition(), SpellIds.ConsecrationDamage, new CastSpellExtraArgs());
        }

        public override void Register()
        {
            OnEffectPeriodic.Add(new EffectPeriodicHandler(HandleEffectPeriodic, 0, AuraType.PeriodicDummy));
        }
    }

    // 26573 - Consecration
    [Script] //  9228 - AreaTriggerId
    class areatrigger_pal_consecration : AreaTriggerAI
    {
        public areatrigger_pal_consecration(AreaTrigger areatrigger) : base(areatrigger) { }

        public override void OnUnitEnter(Unit unit)
        {
            Unit caster = at.GetCaster();
            if (caster != null)
            {
                // 243597 is also being cast as protection, but CreateObject is not sent, either serverside areatrigger for this aura or unused - also no visual is seen
                if (unit == caster && caster.IsPlayer() && caster.ToPlayer().GetPrimarySpecialization() == (uint)TalentSpecialization.PaladinProtection)
                    caster.CastSpell(caster, SpellIds.ConsecrationProtectionAura);

                if (caster.IsValidAttackTarget(unit))
                    if (caster.HasAura(SpellIds.ConsecratedGroundPassive))
                        caster.CastSpell(unit, SpellIds.ConsecratedGroundSlow);
            }
        }

        public override void OnUnitExit(Unit unit)
        {
            if (at.GetCasterGuid() == unit.GetGUID())
                unit.RemoveAurasDueToSpell(SpellIds.ConsecrationProtectionAura, at.GetCasterGuid());

            unit.RemoveAurasDueToSpell(SpellIds.ConsecratedGroundSlow, at.GetCasterGuid());
        }
    }

    [Script] // 196926 - Crusader Might
    class spell_pal_crusader_might : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.HolyShockR1);
        }

        void HandleEffectProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            GetTarget().GetSpellHistory().ModifyCooldown(SpellIds.HolyShockR1, TimeSpan.FromSeconds(aurEff.GetAmount()));
        }

        public override void Register()
        {
            OnEffectProc.Add(new EffectProcHandler(HandleEffectProc, 0, AuraType.Dummy));
        }
    }

    [Script] // 642 - Divine Shield
    class spell_pal_divine_shield : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.FinalStand, SpellIds.FinalStandEffect, SpellIds.Forbearance) //, SpellIds._PALADIN_IMMUNE_SHIELD_MARKER // uncomment when we have serverside only spells
                    && spellInfo.ExcludeCasterAuraSpell == SpellIds.ImmuneShieldMarker;
        }

        SpellCastResult CheckForbearance()
        {
            if (GetCaster().HasAura(SpellIds.Forbearance))
                return SpellCastResult.TargetAurastate;

            return SpellCastResult.SpellCastOk;
        }

        void HandleFinalStand()
        {
            if (GetCaster().HasAura(SpellIds.FinalStand))
                GetCaster().CastSpell((Unit)null, SpellIds.FinalStandEffect, true);
        }

        void TriggerForbearance()
        {
            Unit caster = GetCaster();
            caster.CastSpell(caster, SpellIds.Forbearance, true);
            caster.CastSpell(caster, SpellIds.ImmuneShieldMarker, true);
        }

        public override void Register()
        {
            OnCheckCast.Add(new CheckCastHandler(CheckForbearance));
            AfterCast.Add(new CastHandler(HandleFinalStand));
            AfterCast.Add(new CastHandler(TriggerForbearance));
        }
    }

    [Script] // 190784 - Divine Steed
    class spell_pal_divine_steed : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.DivineSteedHuman, SpellIds.DivineSteedDwarf, SpellIds.DivineSteedDraenei, SpellIds.DivineSteedDarkIronDwarf, SpellIds.DivineSteedBloodelf, SpellIds.DivineSteedTauren, SpellIds.DivineSteedZandalariTroll);
        }

        void HandleOnCast()
        {
            Unit caster = GetCaster();

            uint spellId = SpellIds.DivineSteedHuman;
            switch (caster.GetRace())
            {
                case Race.Human:
                    spellId = SpellIds.DivineSteedHuman;
                    break;
                case Race.Dwarf:
                    spellId = SpellIds.DivineSteedDwarf;
                    break;
                case Race.Draenei:
                case Race.LightforgedDraenei:
                    spellId = SpellIds.DivineSteedDraenei;
                    break;
                case Race.DarkIronDwarf:
                    spellId = SpellIds.DivineSteedDarkIronDwarf;
                    break;
                case Race.BloodElf:
                    spellId = SpellIds.DivineSteedBloodelf;
                    break;
                case Race.Tauren:
                    spellId = SpellIds.DivineSteedTauren;
                    break;
                case Race.ZandalariTroll:
                    spellId = SpellIds.DivineSteedZandalariTroll;
                    break;
                default:
                    break;
            }

            caster.CastSpell(caster, spellId, true);
        }

        public override void Register()
        {
            OnCast.Add(new CastHandler(HandleOnCast));
        }
    }

    [Script] // 224239 - Divine Storm
    class spell_pal_divine_storm : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return CliDB.SpellVisualKitStorage.HasRecord(SpellVisualKits.DivineStorm);
        }

        void HandleOnCast()
        {
            GetCaster().SendPlaySpellVisualKit(SpellVisualKits.DivineStorm, 0, 0);
        }

        public override void Register()
        {
            OnCast.Add(new CastHandler(HandleOnCast));
        }
    }

    [Script] // 205191 - Eye for an Eye
    class spell_pal_eye_for_an_eye : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.EyeForAnEyeTriggered);
        }

        void HandleEffectProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            GetTarget().CastSpell(eventInfo.GetActor(), SpellIds.EyeForAnEyeTriggered, true);
        }

        public override void Register()
        {
            OnEffectProc.Add(new EffectProcHandler(HandleEffectProc, 0, AuraType.Dummy));
        }
    }
    
    [Script] // 234299 - Fist of Justice
    class spell_pal_fist_of_justice : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.HammerOfJustice);
        }

        bool CheckEffectProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            Spell procSpell = eventInfo.GetProcSpell();
            if (procSpell != null)
                return procSpell.HasPowerTypeCost(PowerType.HolyPower);

            return false;
        }

        void HandleEffectProc(AuraEffect aurEff, ProcEventInfo procInfo)
        {
            int value = aurEff.GetAmount() / 10;

            GetTarget().GetSpellHistory().ModifyCooldown(SpellIds.HammerOfJustice, TimeSpan.FromSeconds(-value));
        }

        public override void Register()
        {
            DoCheckEffectProc.Add(new CheckEffectProcHandler(CheckEffectProc, 0, AuraType.Dummy));
            OnEffectProc.Add(new EffectProcHandler(HandleEffectProc, 0, AuraType.Dummy));
        }
    }

    [Script] // -85043 - Grand Crusader
    class spell_pal_grand_crusader : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.AvengersShield);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            return GetTarget().IsTypeId(TypeId.Player);
        }

        void HandleEffectProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            GetTarget().GetSpellHistory().ResetCooldown(SpellIds.AvengersShield, true);
        }

        public override void Register()
        {
            DoCheckProc.Add(new CheckProcHandler(CheckProc));
            OnEffectProc.Add(new EffectProcHandler(HandleEffectProc, 0, AuraType.ProcTriggerSpell));
        }
    }

    [Script] // 54968 - Glyph of Holy Light
    class spell_pal_glyph_of_holy_light : SpellScript
    {
        void FilterTargets(List<WorldObject> targets)
        {
            uint maxTargets = GetSpellInfo().MaxAffectedTargets;

            if (targets.Count > maxTargets)
            {
                targets.Sort(new HealthPctOrderPred());
                targets.Resize(maxTargets);
            }
        }

        public override void Register()
        {
            OnObjectAreaTargetSelect.Add(new ObjectAreaTargetSelectHandler(FilterTargets, 0, Targets.UnitDestAreaAlly));
        }
    }

    [Script] // 53595 - Hammer of the Righteous
    class spell_pal_hammer_of_the_righteous : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.ConsecrationProtectionAura, SpellIds.HammerOfTheRighteousAoe);
        }

        void HandleAoEHit(uint effIndex)
        {
            if (GetCaster().HasAura(SpellIds.ConsecrationProtectionAura))
                GetCaster().CastSpell(GetHitUnit(), SpellIds.HammerOfTheRighteousAoe);
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new EffectHandler(HandleAoEHit, 0, SpellEffectName.SchoolDamage));
        }
    }

    [Script] // 6940 - Hand of Sacrifice
    class spell_pal_hand_of_sacrifice : AuraScript
    {
        int remainingAmount;

        public override bool Load()
        {
            Unit caster = GetCaster();
            if (caster)
            {
                remainingAmount = (int)caster.GetMaxHealth();
                return true;
            }
            return false;
        }

        void Split(AuraEffect aurEff, DamageInfo dmgInfo, uint splitAmount)
        {
            remainingAmount -= (int)splitAmount;

            if (remainingAmount <= 0)
            {
                GetTarget().RemoveAura(SpellIds.HandOfSacrifice);
            }
        }

        public override void Register()
        {
            OnEffectSplit.Add(new EffectSplitHandler(Split, 0));
        }
    }

    [Script] // 327193 - Moment of Glory
    class spell_pal_moment_of_glory : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.AvengersShield);
        }

        void HandleOnHit()
        {
            GetCaster().GetSpellHistory().ResetCooldown(SpellIds.AvengersShield);
        }

        public override void Register()
        {
            OnHit.Add(new HitHandler(HandleOnHit));
        }
    }

    [Script] // 20271/275779 - Judgement Ret/Prot
    class spell_pal_judgement : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.JudgementProtRetR3, SpellIds.JudementGainHolyPower);
        }

        void HandleOnHit()
        {
            Unit caster = GetCaster();
            if (caster.HasSpell(SpellIds.JudgementProtRetR3))
                caster.CastSpell(caster, SpellIds.JudementGainHolyPower, new CastSpellExtraArgs(TriggerCastFlags.FullMask));
        }

        public override void Register()
        {
            OnHit.Add(new HitHandler(HandleOnHit));
        }
    }

    [Script] // 114165 - Holy Prism
    class spell_pal_holy_prism : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.HolyPrismTargetAlly, SpellIds.HolyPrismTargetEnemy, SpellIds.HolyPrismTargetBeamVisual);
        }

        void HandleDummy(uint effIndex)
        {
            if (GetCaster().IsFriendlyTo(GetHitUnit()))
                GetCaster().CastSpell(GetHitUnit(), SpellIds.HolyPrismTargetAlly, true);
            else
                GetCaster().CastSpell(GetHitUnit(), SpellIds.HolyPrismTargetEnemy, true);

            GetCaster().CastSpell(GetHitUnit(), SpellIds.HolyPrismTargetBeamVisual, true);
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new EffectHandler(HandleDummy, 0, SpellEffectName.Dummy));
        }
    }

    // 114852 - Holy Prism (Damage)
    [Script] // 114871 - Holy Prism (Heal)
    class spell_pal_holy_prism_selector : SpellScript
    {
        List<WorldObject> _sharedTargets = new();
        ObjectGuid _targetGUID;

        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.HolyPrismTargetAlly, SpellIds.HolyPrismTargetBeamVisual);
        }

        void SaveTargetGuid(uint effIndex)
        {
            _targetGUID = GetHitUnit().GetGUID();
        }

        void FilterTargets(List<WorldObject> targets)
        {
            byte maxTargets = 5;

            if (targets.Count > maxTargets)
            {
                if (GetSpellInfo().Id == SpellIds.HolyPrismTargetAlly)
                {
                    targets.Sort(new HealthPctOrderPred());
                    targets.Resize(maxTargets);
                }
                else
                    targets.RandomResize(maxTargets);
            }

            _sharedTargets = targets;
        }

        void ShareTargets(List<WorldObject> targets)
        {
            targets = _sharedTargets;
        }

        void HandleScript(uint effIndex)
        {
            Unit initialTarget = Global.ObjAccessor.GetUnit(GetCaster(), _targetGUID);
            if (initialTarget != null)
                initialTarget.CastSpell(GetHitUnit(), SpellIds.HolyPrismTargetBeamVisual, true);
        }

        public override void Register()
        {
            if (m_scriptSpellId == SpellIds.HolyPrismTargetEnemy)
                OnObjectAreaTargetSelect.Add(new ObjectAreaTargetSelectHandler(FilterTargets, 1, Targets.UnitDestAreaAlly));
            else if (m_scriptSpellId == SpellIds.HolyPrismTargetAlly)
                OnObjectAreaTargetSelect.Add(new ObjectAreaTargetSelectHandler(FilterTargets, 1, Targets.UnitDestAreaEnemy));

            OnObjectAreaTargetSelect.Add(new ObjectAreaTargetSelectHandler(ShareTargets, 2, Targets.UnitDestAreaEntry));

            OnEffectHitTarget.Add(new EffectHandler(SaveTargetGuid, 0, SpellEffectName.Any));
            OnEffectHitTarget.Add(new EffectHandler(HandleScript, 2, SpellEffectName.ScriptEffect));
        }
    }
    
    [Script] // 20473 - Holy Shock
    class spell_pal_holy_shock : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            SpellInfo firstRankSpellInfo = Global.SpellMgr.GetSpellInfo(SpellIds.HolyShockR1, Difficulty.None);
            if (firstRankSpellInfo == null)
                return false;

            // can't use other spell than holy shock due to spell_ranks dependency
            if (!spellInfo.IsRankOf(firstRankSpellInfo))
                return false;

            byte rank = spellInfo.GetRank();
            if (Global.SpellMgr.GetSpellWithRank(SpellIds.HolyShockR1Damage, rank, true) == 0 || Global.SpellMgr.GetSpellWithRank(SpellIds.HolyShockR1Healing, rank, true) == 0)
                return false;

            return true;
        }

        void HandleDummy(uint effIndex)
        {
            Unit caster = GetCaster();
            Unit unitTarget = GetHitUnit();
            if (unitTarget)
            {
                byte rank = GetSpellInfo().GetRank();
                if (caster.IsFriendlyTo(unitTarget))
                    caster.CastSpell(unitTarget, Global.SpellMgr.GetSpellWithRank(SpellIds.HolyShockR1Healing, rank), true);
                else
                    caster.CastSpell(unitTarget, Global.SpellMgr.GetSpellWithRank(SpellIds.HolyShockR1Damage, rank), true);
            }
        }

        SpellCastResult CheckCast()
        {
            Unit caster = GetCaster();
            Unit target = GetExplTargetUnit();
            if (target)
            {
                if (!caster.IsFriendlyTo(target))
                {
                    if (!caster.IsValidAttackTarget(target))
                        return SpellCastResult.BadTargets;

                    if (!caster.IsInFront(target))
                        return SpellCastResult.NotInfront;
                }
            }
            else
                return SpellCastResult.BadTargets;
            return SpellCastResult.SpellCastOk;
        }

        public override void Register()
        {
            OnCheckCast.Add(new CheckCastHandler(CheckCast));
            OnEffectHitTarget.Add(new EffectHandler(HandleDummy, 0, SpellEffectName.Dummy));
        }
    }

    [Script] // 37705 - Healing Discount
    class spell_pal_item_healing_discount : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.ItemHealingTrance);
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();
            GetTarget().CastSpell(GetTarget(), SpellIds.ItemHealingTrance, new CastSpellExtraArgs(aurEff));
        }

        public override void Register()
        {
            OnEffectProc.Add(new EffectProcHandler(HandleProc, 0, AuraType.ProcTriggerSpell));
        }
    }

    [Script] // 40470 - Paladin Tier 6 Trinket
    class spell_pal_item_t6_trinket : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.EnduringLight, SpellIds.EnduringJudgement);
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();
            SpellInfo spellInfo = eventInfo.GetSpellInfo();
            if (spellInfo == null)
                return;

            uint spellId;
            int chance;

            // Holy Light & Flash of Light
            if (spellInfo.SpellFamilyFlags[0].HasAnyFlag(0xC0000000))
            {
                spellId = SpellIds.EnduringLight;
                chance = 15;
            }
            // Judgements
            else if (spellInfo.SpellFamilyFlags[0].HasAnyFlag(0x00800000u))
            {
                spellId = SpellIds.EnduringJudgement;
                chance = 50;
            }
            else
                return;

            if (RandomHelper.randChance(chance))
                eventInfo.GetActor().CastSpell(eventInfo.GetProcTarget(), spellId, new CastSpellExtraArgs(aurEff));
        }

        public override void Register()
        {
            OnEffectProc.Add(new EffectProcHandler(HandleProc, 0, AuraType.Dummy));
        }
    }

    [Script] // 633 - Lay on Hands
    class spell_pal_lay_on_hands : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.Forbearance)//, SpellIds.ImmuneShieldMarker);
                && spellInfo.ExcludeTargetAuraSpell == SpellIds.ImmuneShieldMarker;
        }

        SpellCastResult CheckForbearance()
        {
            Unit target = GetExplTargetUnit();
            if (!target || target.HasAura(SpellIds.Forbearance))
                return SpellCastResult.TargetAurastate;

            return SpellCastResult.SpellCastOk;
        }

        void TriggerForbearance()
        {
            Unit target = GetHitUnit();
            if (target)
            {
                GetCaster().CastSpell(target, SpellIds.Forbearance, true);
                GetCaster().CastSpell(target, SpellIds.ImmuneShieldMarker, true);
            }
        }

        public override void Register()
        {
            OnCheckCast.Add(new CheckCastHandler(CheckForbearance));
            AfterHit.Add(new HitHandler(TriggerForbearance));
        }
    }

    [Script] // 53651 - Beacon of Light
    class spell_pal_light_s_beacon : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.BeaconOfLight, SpellIds.BeaconOfLightHeal);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            if (!eventInfo.GetActionTarget())
                return false;
            if (eventInfo.GetActionTarget().HasAura(SpellIds.BeaconOfLight, eventInfo.GetActor().GetGUID()))
                return false;
            return true;
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();

            HealInfo healInfo = eventInfo.GetHealInfo();
            if (healInfo == null || healInfo.GetHeal() == 0)
                return;

            uint heal = MathFunctions.CalculatePct(healInfo.GetHeal(), aurEff.GetAmount());

            var auras = GetCaster().GetSingleCastAuras();
            foreach (var eff in auras)
            {
                if (eff.GetId() == SpellIds.BeaconOfLight)
                {
                    List<AuraApplication> applications = eff.GetApplicationList();
                    if (!applications.Empty())
                    {
                        CastSpellExtraArgs args = new(aurEff);
                        args.AddSpellMod(SpellValueMod.BasePoint0, (int)heal);
                        eventInfo.GetActor().CastSpell(applications[0].GetTarget(), SpellIds.BeaconOfLightHeal, args);
                    }
                    return;
                }
            }
        }

        public override void Register()
        {
            DoCheckProc.Add(new CheckProcHandler(CheckProc));
            OnEffectProc.Add(new EffectProcHandler(HandleProc, 0, AuraType.Dummy));
        }
    }

    [Script] // 204074 - Righteous Protector
    class spell_pal_righteous_protector : AuraScript
    {
        SpellPowerCost _baseHolyPowerCost;

        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.AvengingWrath, SpellIds.GuardianOfAcientKings);
        }

        bool CheckEffectProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            SpellInfo procSpell = eventInfo.GetSpellInfo();
            if (procSpell != null)
                _baseHolyPowerCost = procSpell.CalcPowerCost(PowerType.HolyPower, false, eventInfo.GetActor(), eventInfo.GetSchoolMask());
            else
                _baseHolyPowerCost = null;

            return _baseHolyPowerCost != null;
        }

        void HandleEffectProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            int value = aurEff.GetAmount() * 100 * _baseHolyPowerCost.Amount;

            GetTarget().GetSpellHistory().ModifyCooldown(SpellIds.AvengingWrath, TimeSpan.FromSeconds(-value));
            GetTarget().GetSpellHistory().ModifyCooldown(SpellIds.GuardianOfAcientKings, TimeSpan.FromSeconds(-value));
        }

        public override void Register()
        {
            DoCheckEffectProc.Add(new CheckEffectProcHandler(CheckEffectProc, 0, AuraType.Dummy));
            OnEffectProc.Add(new EffectProcHandler(HandleEffectProc, 0, AuraType.Dummy));
        }
    }

    [Script] // 267610 - Righteous Verdict
    class spell_pal_righteous_verdict : AuraScript
    {
        public override bool Validate(SpellInfo spellEntry)
        {
            return ValidateSpellInfo(SpellIds.RighteousVerdictAura);
        }

        void HandleEffectProc(AuraEffect aurEff, ProcEventInfo procInfo)
        {
            procInfo.GetActor().CastSpell(procInfo.GetActor(), SpellIds.RighteousVerdictAura, true);
        }

        public override void Register()
        {
            OnEffectProc.Add(new EffectProcHandler(HandleEffectProc, 0, AuraType.Dummy));
        }
    }

    [Script] // 85804 - Selfless Healer
    class spell_pal_selfless_healer : AuraScript
    {
        bool CheckEffectProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            Spell procSpell = eventInfo.GetProcSpell();
            if (procSpell != null)
                return procSpell.HasPowerTypeCost(PowerType.HolyPower);

            return false;
        }

        public override void Register()
        {
            DoCheckEffectProc.Add(new CheckEffectProcHandler(CheckEffectProc, 0, AuraType.ProcTriggerSpell));
        }
    }

    [Script] // 85256 - Templar's Verdict
    class spell_pal_templar_s_verdict : SpellScript
    {
        public override bool Validate(SpellInfo spellEntry)
        {
            return ValidateSpellInfo(SpellIds.TemplarVerdictDamage);
        }

        void HandleHitTarget(uint effIndex)
        {
            GetCaster().CastSpell(GetHitUnit(), SpellIds.TemplarVerdictDamage, true);
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new EffectHandler(HandleHitTarget, 0, SpellEffectName.Dummy));
        }
    }

    [Script] // 28789 - Holy Power
    class spell_pal_t3_6p_bonus : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.HolyPowerArmor, SpellIds.HolyPowerAttackPower, SpellIds.HolyPowerSpellPower, SpellIds.HolyPowerMp5);
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();

            uint spellId;
            Unit caster = eventInfo.GetActor();
            Unit target = eventInfo.GetProcTarget();

            switch (target.GetClass())
            {
                case Class.Paladin:
                case Class.Priest:
                case Class.Shaman:
                case Class.Druid:
                    spellId = SpellIds.HolyPowerMp5;
                    break;
                case Class.Mage:
                case Class.Warlock:
                    spellId = SpellIds.HolyPowerSpellPower;
                    break;
                case Class.Hunter:
                case Class.Rogue:
                    spellId = SpellIds.HolyPowerAttackPower;
                    break;
                case Class.Warrior:
                    spellId = SpellIds.HolyPowerArmor;
                    break;
                default:
                    return;
            }

            caster.CastSpell(target, spellId, new CastSpellExtraArgs(aurEff));
        }

        public override void Register()
        {
            OnEffectProc.Add(new EffectProcHandler(HandleProc, 0, AuraType.Dummy));
        }
    }

    [Script] // 64890 Item - Paladin T8 Holy 2P Bonus
    class spell_pal_t8_2p_bonus : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.HolyMending);
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();

            HealInfo healInfo = eventInfo.GetHealInfo();
            if (healInfo == null || healInfo.GetHeal() == 0)
                return;

            Unit caster = eventInfo.GetActor();
            Unit target = eventInfo.GetProcTarget();

            SpellInfo spellInfo = Global.SpellMgr.GetSpellInfo(SpellIds.HolyMending, GetCastDifficulty());
            int amount = (int)MathFunctions.CalculatePct(healInfo.GetHeal(), aurEff.GetAmount());
            amount /= (int)spellInfo.GetMaxTicks();

            CastSpellExtraArgs args = new(aurEff);
            args.AddSpellMod(SpellValueMod.BasePoint0, amount);
            caster.CastSpell(target, SpellIds.HolyMending, args);
        }

        public override void Register()
        {
            OnEffectProc.Add(new EffectProcHandler(HandleProc, 0, AuraType.Dummy));
        }
    }

    [Script] // 269569 - Zeal
    class spell_pal_zeal : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.ZealAura);
        }

        void HandleEffectProc(AuraEffect aurEff, ProcEventInfo procInfo)
        {
            Unit target = GetTarget();
            target.CastSpell(target, SpellIds.ZealAura, new CastSpellExtraArgs(TriggerCastFlags.FullMask).AddSpellMod(SpellValueMod.AuraStack, aurEff.GetAmount()));

            PreventDefaultAction();
        }

        public override void Register()
        {
            OnEffectProc.Add(new EffectProcHandler(HandleEffectProc, 0, AuraType.ProcTriggerSpell));
        }
    }
}
