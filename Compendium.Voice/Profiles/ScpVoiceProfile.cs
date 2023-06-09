﻿using Compendium.Input;

using PlayerRoles;

using UnityEngine;

namespace Compendium.Voice.Profiles
{
    public class ScpVoiceProfile : VoiceProfileBase
    {
        private static bool m_ProximityHandler;

        public override string Name => "SCP Chat";

        public bool AllowSelfHearing { get; set; }

        public ProximityVoiceFlags ProximityFlag { get; set; }

        public ScpVoiceProfile(ReferenceHub owner) : base(owner)
        {
            if (!m_ProximityHandler)
                m_ProximityHandler = InputHandler.TryAddHandler("voice_proximity", KeyCode.RightAlt, ProximityKey);
        }

        public bool IsProximityAvailable()
            => Owner.GetTeam() is Team.SCPs && VoiceConfigs.ProximityScps.Contains(Owner.GetRoleId());

        public void SwitchProximity()
        {
            if (!IsProximityAvailable())
            {
                ProximityFlag = ProximityVoiceFlags.Inactive;
                return;
            }

            if (ProximityFlag is ProximityVoiceFlags.Inactive)
                ProximityFlag = ProximityVoiceFlags.Single;
            else if (ProximityFlag is ProximityVoiceFlags.Single)
                ProximityFlag = ProximityVoiceFlags.Combined;
            else
                ProximityFlag = ProximityVoiceFlags.Inactive;
        }

        private static void ProximityKey(ReferenceHub hub)
        {
            if (VoiceController.TryGetProfile(hub, out var vcProfile) 
                && vcProfile is ScpVoiceProfile scpProfile)
            {
                scpProfile.SwitchProximity();
            }
        }
    }
}