﻿// <copyright file="PudgeComboOrbwalker.cs" company="Ensage">
//    Copyright (c) 2017 Ensage.
// </copyright>

namespace Vaper.OrbwalkingModes.Combo
{
    using System;
    using System.Diagnostics;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;

    using Ensage.SDK.Extensions;
    using Ensage.SDK.Prediction;

    using log4net;

    using PlaySharp.Toolkit.Logging;

    using Vaper.Heroes;

    internal class PudgeOrbwalker : ComboOrbwalkingMode
    {
        private static readonly ILog Log = AssemblyLogs.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly Pudge hero;

        public PudgeOrbwalker(Pudge hero)
            : base(hero)
        {
            this.hero = hero;
        }

        public static async Task<bool> KeepTrying(Func<bool> operation, int timeout, int delay = 50, CancellationToken token = default(CancellationToken))
        {
            var success = false;

            var watch = new Stopwatch();
            watch.Start();
            while (watch.ElapsedMilliseconds <= timeout)
            {
                success = operation();
                if (success)
                {
                    break;
                }

                await Task.Delay(delay, token);
            }

            watch.Stop();
            return success;
        }

        public override async Task ExecuteAsync(CancellationToken token)
        {
            if (!await this.ShouldExecute(token))
            {
                return;
            }

            var rot = this.hero.Rot;
            if (this.Owner.IsChanneling())
            {
                if ((rot != null) && !rot.Enabled && (this.CurrentTarget != null) && rot.CanBeCasted && rot.CanHit(this.CurrentTarget))
                {
                    rot.Enabled = true;
                    await Task.Delay(rot.GetCastDelay(), token);
                }

                await Task.Delay(125, token);
                return;
            }

            var hook = this.hero.Hook;
            var blink = this.hero.Blink;
            var atos = this.hero.Atos;

            this.MaxTargetRange = Math.Max(this.MaxTargetRange, hook.CastRange * 1.1f);
            if (blink != null)
            {
                this.MaxTargetRange = Math.Max(this.MaxTargetRange, blink.CastRange * 1.3f);
            }

            if (atos != null)
            {
                this.MaxTargetRange = Math.Max(this.MaxTargetRange, atos.CastRange * 1.1f);
            }

            if ((this.CurrentTarget == null) || !this.CurrentTarget.IsVisible)
            {
                this.hero.Context.Orbwalker.Active.OrbwalkTo(null);
                return;
            }

            if (this.CurrentTarget.IsIllusion)
            {
                this.OrbwalkToTarget();
                return;
            }

            if (!rot.Enabled && rot.CanBeCasted && rot.CanHit(this.CurrentTarget))
            {
                rot.Enabled = true;
                await Task.Delay(rot.GetCastDelay(), token);
            }

            var forceStaff = this.hero.ForceStaff;
            var forceStaffReady = (forceStaff != null) && forceStaff.CanBeCasted;

            var ult = this.hero.Dismember;
            if (ult.CanBeCasted && ult.CanHit(this.CurrentTarget))
            {
                if (this.CurrentTarget.IsLinkensProtected() && forceStaffReady)
                {
                    forceStaff.UseAbility(this.CurrentTarget);
                    await Task.Delay(forceStaff.GetCastDelay(this.CurrentTarget), token);
                }

                ult.UseAbility(this.CurrentTarget);
                await Task.Delay(ult.GetCastDelay(this.CurrentTarget) + 500, token);
                return;
            }

            var urn = this.hero.Urn;
            if (hook.CanBeCasted && hook.CanHit(this.CurrentTarget))
            {
                if ((atos != null) && atos.CanBeCasted && atos.CanHit(this.CurrentTarget) && !this.CurrentTarget.IsStunned() && !this.CurrentTarget.IsRooted())
                {
                    var input = hook.GetPredictionInput(this.CurrentTarget);
                    var output = hook.GetPredictionOutput(input);
                    if ((output.HitChance != HitChance.OutOfRange) && (output.HitChance != HitChance.Collision))
                    {
                        atos.UseAbility(this.CurrentTarget);
                        await Task.Delay(atos.GetCastDelay(this.CurrentTarget) + atos.GetHitTime(this.CurrentTarget), token);
                    }
                }

                if (hook.UseAbility(this.CurrentTarget, this.hero.MinimumHookChance))
                {
                    var castDelay = hook.GetCastDelay(this.CurrentTarget) + 100;
                    await Task.Delay(castDelay, token);

                    if ((ult.Ability.Level > 0) && (ult.Ability.ManaCost <= this.Owner.Mana) && (ult.Ability.Cooldown <= 0.0f))
                    {
                        // wait till hook hits/comes back or we hit the target
                        var hittime = (int)((hook.GetCastDelay(this.CurrentTarget) * 1.1f) + ((hook.Range / hook.Speed) * 1000.0f));
                        var hasHit = await KeepTrying(
                                         () =>
                                             {
                                                 this.Owner.Stop();
                                                 return this.hero.HookModifierDetected;
                                             },
                                         hittime,
                                         50,
                                         token);

                        // we hit the target
                        if (hasHit)
                        {
                            if (this.CurrentTarget.IsLinkensProtected() && forceStaffReady)
                            {
                                var canBreakLinkens = await KeepTrying(
                                                          () =>
                                                              {
                                                                  this.Owner.Stop();
                                                                  return this.Owner.Distance2D(this.CurrentTarget) < forceStaff.CastRange;
                                                              },
                                                          hittime,
                                                          50,
                                                          token);

                                if (canBreakLinkens)
                                {
                                    forceStaff.UseAbility(this.CurrentTarget);
                                    await Task.Delay(forceStaff.GetCastDelay(this.CurrentTarget), token);
                                    forceStaffReady = false;
                                }
                            }

                            if ((urn != null) && urn.CanBeCasted && urn.CanHit(this.CurrentTarget) && !this.CurrentTarget.HasModifier(urn.TargetModifierName))
                            {
                                urn.UseAbility(this.CurrentTarget);
                                await Task.Delay(urn.GetCastDelay(this.CurrentTarget), token);
                            }

                            var canUltHit = await KeepTrying(
                                                () =>
                                                    {
                                                        this.Owner.Stop();
                                                        return this.Owner.Distance2D(this.CurrentTarget) < (ult.CastRange + 150);
                                                    },
                                                hittime,
                                                50,
                                                token);
                            if (rot.CanBeCasted && !rot.Enabled)
                            {
                                rot.Enabled = true;
                                await Task.Delay(rot.GetCastDelay(), token);
                            }

                            this.hero.HookModifierDetected = false;

                            Log.Debug($"can ult {canUltHit}");
                            if (canUltHit)
                            {
                                ult.UseAbility(this.CurrentTarget);
                                await Task.Delay(ult.GetCastDelay(this.CurrentTarget) + 500, token);
                                return;
                            }
                        }
                    }
                }
            }

            if (forceStaffReady && !this.CurrentTarget.IsLinkensProtected())
            {
                if (!this.CurrentTarget.IsRotating() && (this.Owner.Distance2D(this.CurrentTarget.InFront(forceStaff.PushLength)) < rot.Radius))
                {
                    forceStaff.UseAbility(this.CurrentTarget);
                    var travelTime = (int)((forceStaff.PushLength / forceStaff.PushSpeed) * 1000f);
                    await Task.Delay(forceStaff.GetCastDelay(this.CurrentTarget) + travelTime, token);

                    if (rot.CanBeCasted && !rot.Enabled)
                    {
                        rot.Enabled = true;
                        await Task.Delay(rot.GetCastDelay(), token);
                    }
                }
                else if (ult.CanBeCasted && !this.Owner.IsRotating() && (this.CurrentTarget.Distance2D(this.Owner.InFront(forceStaff.PushLength)) <= ult.CastRange))
                {
                    forceStaff.UseAbility(this.Owner);
                    var travelTime = (int)((forceStaff.PushLength / forceStaff.PushSpeed) * 1000f);
                    await Task.Delay(forceStaff.GetCastDelay() + travelTime, token);

                    if (ult.CanHit(this.CurrentTarget))
                    {
                        if ((urn != null) && urn.CanBeCasted && urn.CanHit(this.CurrentTarget) && !this.CurrentTarget.HasModifier(urn.TargetModifierName))
                        {
                            urn.UseAbility(this.CurrentTarget);
                            await Task.Delay(urn.GetCastDelay(this.CurrentTarget), token);
                        }

                        ult.UseAbility(this.CurrentTarget);
                        await Task.Delay(ult.GetCastDelay(this.CurrentTarget) + 500, token);
                        return;
                    }
                }
            }

            this.OrbwalkToTarget();
        }
    }
}