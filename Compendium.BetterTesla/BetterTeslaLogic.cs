﻿using Compendium.Extensions;
using Compendium.Features;
using Compendium.Helpers.Events;

using helpers;
using helpers.Configuration.Ini;
using helpers.Patching;
using PlayerRoles;
using PluginAPI.Enums;
using PluginAPI.Events;

using System;
using System.Collections.Generic;

using UnityEngine;

namespace Compendium.BetterTesla
{
    public static class BetterTeslaLogic
    {
        private static bool _isEnabled;
        private static readonly Dictionary<TeslaGate, TeslaDamageStatus> _damage = new Dictionary<TeslaGate, TeslaDamageStatus>();

        public static bool IsEnabled
        {
            get
            {
                return _isEnabled;
            }
            set
            {
                if (value == _isEnabled)
                    return;

                if (!value)
                {
                    _isEnabled = false;

                    PatchManager.Unpatch(BetterTeslaPatch.patch);

                    ServerEventType.RoundRestart.RemoveHandler<Action>(OnRoundRestart);
                    ServerEventType.PlayerShotWeapon.RemoveHandler<Action<PlayerShotWeaponEvent>>(OnShot);
                    ServerEventType.GrenadeExploded.RemoveHandler<Action<GrenadeExplodedEvent>>(OnGrenadeExplode);

                    Log.Info($"Better Tesla disabled.");
                }
                else
                {
                    _isEnabled = true;

                    PatchManager.Patch(BetterTeslaPatch.patch);

                    ServerEventType.RoundRestart.AddHandler<Action>(OnRoundRestart);
                    ServerEventType.PlayerShotWeapon.AddHandler<Action<PlayerShotWeaponEvent>>(OnShot);
                    ServerEventType.GrenadeExploded.AddHandler<Action<GrenadeExplodedEvent>>(OnGrenadeExplode);

                    Log.Info($"Better Tesla enabled.");
                }
            }
        }

        public static bool RoundDisabled { get; set; }
        public static List<RoleTypeId> RoundDisabledRoles { get; set; } = new List<RoleTypeId>();

        [IniConfig("Allow Damage", null, "Whether or not to allow players to damage Tesla gates, which results in them not working for a bit.")]
        public static bool AllowTeslaDamage { get; set; } = true;

        [IniConfig("Damaged Hint", null, "Whether or not to show a hint when a Tesla gets damaged.")]
        public static bool DamagedTeslaHint { get; set; } = true;

        [IniConfig("Damaged Radius", null, "The radius of shown hint.")]
        public static float DamagedTeslaRadius { get; set; } = 30f;

        [IniConfig("Damaged Blackout", null, "Whether or not to blackout the room when a Tesla gets damaged.")]
        public static bool DamagedBlackout { get; set; } = true;

        [IniConfig("Damaged Blackout Duration", null, "The duration of the blackout.")]
        public static float DamagedBlackoutDuration { get; set; } = 5f;

        [IniConfig("Allow Grenade Damage", null, "Whether or not to allow grenades to damage Tesla gates.")]
        public static bool AllowTeslaGrenadeDamage { get; set; } = true;

        [IniConfig("Grenade Radius", null, "The radius in which a Tesla can be hit from a grenade.")]
        public static float GrenadeRadius { get; set; } = 30f;

        [IniConfig("Grenade Damage", null, "Base grenade damage.")]
        public static float GrenadeDamage { get; set; } = 200f;

        [IniConfig("Grenade Full Damage Distance", null, "The maximum distance for a full damage hit by a grenade.")]
        public static float FullGrenadeDamageDistance { get; set; } = 10f;

        [IniConfig("Grenade Damage Falloff", null, "Grenade damage falloff over distance.")]
        public static float GrenadeDamageFalloff { get; set; } = 10f;

        [IniConfig("Grenade Time Multiplier", null, "The time multiplier if a Tesla gets knocked out by a grenade.")]
        public static int GrenadeTimeMultiplier { get; set; } = -1;

        [IniConfig("Min Tesla Timeout", null, "The minimal Tesla timeout, in seconds.")]
        public static float MinTeslaTimeout { get; set; } = 5f;

        [IniConfig("Max Tesla Timeout", null, "The maximum Tesla timeout, in seconds.")]
        public static float MaxTeslaTimeout { get; set; } = 8f;

        [IniConfig("Tesla Health", null, "The health of a Tesla gate.")]
        public static float TeslaHealth { get; set; } = 200f;

        [IniConfig("Ignored Roles", null, "A list of roles that will be ignored by the Tesla gate.")]
        public static List<RoleTypeId> IgnoredRoles { get; set; } = new List<RoleTypeId>()
        { 
            RoleTypeId.CustomRole,
            RoleTypeId.FacilityGuard,
            RoleTypeId.Filmmaker,
            RoleTypeId.NtfCaptain,
            RoleTypeId.NtfPrivate,
            RoleTypeId.NtfSergeant,
            RoleTypeId.NtfSpecialist,
            RoleTypeId.Overwatch,
            RoleTypeId.Scientist,
            RoleTypeId.Tutorial
        };

        public static IReadOnlyDictionary<TeslaGate, TeslaDamageStatus> Damage => _damage;

        public static int TeslaMask { get; } = LayerMask.GetMask("Default", "Viewmodel", "Hitbox", "InvisibleCollider", "Ragdoll", "Water", "UI", "IgnoreRaycast");

        public static TeslaDamageStatus GetStatus(TeslaGate gate)
        {
            if (_damage.TryGetValue(gate, out var status))
                return status;
            else
                return status = (_damage[gate] = new TeslaDamageStatus(gate));
        }

        private static void OnShot(PlayerShotWeaponEvent ev)
        {
            if (!AllowTeslaDamage)
                return;

            var ray = new Ray(ev.Player.Camera.position, ev.Player.Camera.forward);
            var cast = Physics.Raycast(ray, out var hit, ev.Firearm.BaseStats.MaxDistance(), TeslaMask, QueryTriggerInteraction.Ignore);

            if (cast && hit.collider != null)
            {
                if (hit.collider.gameObject.TryGet<TeslaGate>(out var tesla))
                {
                    var damage = ev.Firearm.BaseStats.DamageAtDistance(ev.Firearm, hit.distance);

                    if (damage > 0f)
                    {
                        GetStatus(tesla)?.ProcessDamage(damage);
                    }
                }
            }
        }

        private static void OnGrenadeExplode(GrenadeExplodedEvent ev)
        {
            if (TeslaGateController.Singleton is null)
                return;

            foreach (var tesla in TeslaGateController.Singleton.TeslaGates)
            {
                var damage = DamageAtDistance(GrenadeDamage, tesla.DistanceSquared(ev.Position));

                if (damage <= 0f)
                    continue;

                GetStatus(tesla)?.ProcessDamage(damage, true);
                return;
            }
        }

        private static float DamageAtDistance(float baseDamage, float distance)
        {
            if (distance >= GrenadeRadius)
                return 0f;

            if (distance > FullGrenadeDamageDistance)
            {
                var num2 = 100f - GrenadeDamageFalloff * (distance - FullGrenadeDamageDistance);

                baseDamage *= num2 / 100f;
            }

            return baseDamage;
        }

        private static void OnRoundRestart()
        {
            RoundDisabled = false;
            RoundDisabledRoles.Clear();

            _damage.Clear();

            Log.Debug($"Cleared round-temporary variables.");
        }
    }
}