﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Smurf.GlobalOffensive.Objects;
using Smurf.GlobalOffensive.SDK;
using Smurf.GlobalOffensive.Utils;

namespace Smurf.GlobalOffensive.Feauters
{
    public class TriggerBot
    {
        #region Fields

        public bool AimOntarget;
        private long _triggerLastTarget;
        private long _triggerLastShot;
        private bool _triggerEnabled;
        private bool _triggerAllies;
        private bool _triggerEnemies;
        private bool _triggerSpawnProtection;
        private bool _triggerDash;
        private bool _triggerZoomed;
        private bool _inCrossTrigger;
        private bool _boneTrigger;
        private bool _hitboxTrigger;
        public int TriggerDelayFirstRandomize;
        public int TriggerDelayShotsRandomize;
        private int _triggerDelayFirstShotMax;
        private int _triggerDelayFirstShotMin;
        private int _triggerDelayShotsMax;
        private int _triggerDelayShotsMin;
        public Vector3 ViewAngles;
        private WinAPI.VirtualKeyShort _triggerKey;
        private IEnumerable<Player> _validTargets;

        #endregion

        #region Methods

        public void Update()
        {
            if (!MiscUtils.ShouldUpdate())
                return;

            ReadSettings();

            if (!_triggerEnabled)
                return;

            if (Core.KeyUtils.KeyIsDown(_triggerKey))
            {
                ViewAngles = Core.Memory.Read<Vector3>((IntPtr)(Core.ClientState + Offsets.ClientState.ViewAngles));

                if (_triggerZoomed)
                    if (Core.LocalPlayerWeapon.ZoomLevel == 0)
                        return;

                if (_triggerDash)
                    if (Core.LocalPlayer.Velocity > 0)
                        return;

                RandomizeDelay();

                if (_inCrossTrigger)
                    InCrossTriggerBot();
                else if (_boneTrigger)
                    BoneTriggerBot();
                else if (_hitboxTrigger)
                    HitBoxTriggerBot();
            }
            else
                AimOntarget = false;
        }

        private void HitBoxTriggerBot()
        {
            GetValidTargets();
            foreach (Player target in _validTargets)
            {
                if (target.Health > 0 & !target.IsDormant)
                {
                    Vector3 bBone = target.GetBonePos(target, 8) + new Vector3(0, 0, 3);
                    Vector3 bottomHitboxHead = new Vector3(bBone.X - 2.54f, bBone.Y - 4.145f, bBone.Z - 7f);
                    Vector3 topHitboxHead = new Vector3(bBone.X + 2.54f, bBone.Y + 4.145f, bBone.Z + 3f);

                    Vector3 hBone = target.GetBonePos(target, 3);
                    Vector3 bottomHitboxBody = new Vector3(hBone.X - 7f, hBone.Y - 5.5f, hBone.Z - 25f);
                    Vector3 topHitboxBody = new Vector3(hBone.X + 7f, hBone.Y + 5.5f, hBone.Z + 15f);

                    Vector3 viewDirection = TraceRay.AngleToDirection(ViewAngles);
                    TraceRay viewRay = new TraceRay(Core.LocalPlayer.Position + Core.LocalPlayer.VecView, viewDirection);
                    float distance = 0;

                    if (viewRay.Trace(bottomHitboxHead, topHitboxHead, ref distance) | viewRay.Trace(bottomHitboxBody, topHitboxBody, ref distance))
                    {
                        if (!CheckDelay())
                            return;

                        _triggerLastShot = DateTime.Now.Ticks;

                        Engine.ForceAttack(0, 12, 10);
                    }
                }

            }

        }

        public float GetNextEnemyToCrosshair(int bone, ref IntPtr pPointer)
        {
            float fov = 0;
            Vector3 pAngles = ViewAngles;

            int[] playerArray = new int[33];
            float[] angleArray = new float[33];


            for (int i = 1; i <= 32; i++)
            {
                Player player = new Player(Core.Objects.GetEntityPtr(i));

                Vector3 pAngle = player.GetBonePos(player, bone);
                pAngle = Core.LocalPlayer.Position.CalcAngle(pAngle);
                pAngle = pAngle.ClampAngle();
                float iDiff = MathUtils.Get3DDistance(pAngle, pAngles);

                playerArray[i] = (int)player.BaseAddress;
                angleArray[i] = iDiff;
            }

            int closestPlayer = 0;
            float closestAngle = 360;

            for (int i = 1; i <= 32; i++)
            {
                Player player = new Player((IntPtr)playerArray[i]);
                float angle = angleArray[i];

                int curPlayerTeam = (int)player.Team;
                bool dormant = player.IsDormant;
                int health = player.Health;

                Vector3 pOriginVec = player.Position;
                pOriginVec.Z += 64;

                if (!(curPlayerTeam != (int) Core.LocalPlayer.Team & (!dormant) & health > 0 & angle < closestAngle))
                    continue;

                closestPlayer = (int)player.BaseAddress;
                closestAngle = angle;
                fov = angle;
            }
            pPointer = (IntPtr)closestPlayer;
            return fov;
        }

        private void InCrossTriggerBot()
        {
            BaseEntity target = Core.LocalPlayer.Target;
            if (target != null && ((_triggerAllies && target.Team == Core.LocalPlayer.Team) || (_triggerEnemies && target.Team != Core.LocalPlayer.Team)))
            {
                if (!AimOntarget)
                {
                    AimOntarget = true;
                    _triggerLastTarget = DateTime.Now.Ticks;
                }
                else
                {
                    if (!CheckDelay())
                        return;

                    _triggerLastShot = DateTime.Now.Ticks;

                    if (!_triggerSpawnProtection)
                        if (target.GunGameImmune)
                            return;

                    Engine.ForceAttack(0, 12, 10);
                }
            }
        }

        private void BoneTriggerBot()
        {
            GetValidTargets();
            foreach (Player validTarget in _validTargets)
            {
                Vector3 myView = Core.LocalPlayer.Position + Core.LocalPlayer.VecView;

                for (int i = 0; i < 81; i++)
                {
                    Vector3 aimView = validTarget.GetBonePos(validTarget, i);
                    Vector3 dst = myView.CalcAngle(aimView);
                    dst = dst.NormalizeAngle();
                    var fov = MathUtils.Fov(ViewAngles, dst, Vector3.Distance(Core.LocalPlayer.Position, validTarget.Position));

                    if (!(fov <= 5))
                        continue;

                    if (!AimOntarget)
                    {
                        AimOntarget = true;
                        _triggerLastTarget = DateTime.Now.Ticks;
                    }
                    else
                    {
                        if (!CheckDelay())
                            return;

                        _triggerLastShot = DateTime.Now.Ticks;

                        Engine.ForceAttack(0, 12, 10);
                    }
                }
            }
        }

        private bool CheckDelay()
        {
            if (!(new TimeSpan(DateTime.Now.Ticks - _triggerLastTarget).TotalMilliseconds >= TriggerDelayFirstRandomize))
                return false;
            if (!(new TimeSpan(DateTime.Now.Ticks - _triggerLastShot).TotalMilliseconds >= TriggerDelayShotsRandomize))
                return false;

            return true;
        }

        private void GetValidTargets()
        {
            _validTargets = Core.Objects.Players.Where(p => p.IsAlive && !p.IsDormant && p.Id != Core.LocalPlayer.Id /*&& p.SeenBy(Core.LocalPlayer)*/);
            if (_triggerEnemies)
                _validTargets = _validTargets.Where(p => p.Team != Core.LocalPlayer.Team);
            if (_triggerAllies)
                _validTargets = _validTargets.Where(p => p.Team == Core.LocalPlayer.Team);
            if (!_triggerSpawnProtection)
                _validTargets = _validTargets.Where(p => !p.GunGameImmune);
        }

        private void RandomizeDelay()
        {
            TriggerDelayFirstRandomize = new Random().Next(_triggerDelayFirstShotMin, _triggerDelayFirstShotMax) + 1;
            TriggerDelayShotsRandomize = new Random().Next(_triggerDelayShotsMin, _triggerDelayShotsMax) + 1;
        }

        private void ReadSettings()
        {
            try
            {
                /*
                _triggerEnabled = Core.Settings.GetBool(Core.LocalPlayerWeapon.WeaponName, "Trigger Enabled");
                _triggerKey = (WinAPI.VirtualKeyShort)Convert.ToInt32(Core.Settings.GetString(Core.LocalPlayerWeapon.WeaponName, "Trigger Key"), 16);
                _triggerEnemies = Core.Settings.GetBool(Core.LocalPlayerWeapon.WeaponName, "Trigger Enemies");
                _triggerAllies = Core.Settings.GetBool(Core.LocalPlayerWeapon.WeaponName, "Trigger Allies");
                _triggerSpawnProtection = Core.Settings.GetBool(Core.LocalPlayerWeapon.WeaponName, "Trigger Spawn Protected");
                _triggerDelayFirstShotMax = Core.Settings.GetInt(Core.LocalPlayerWeapon.WeaponName, "Trigger Delay FirstShot Max");
                _triggerDelayFirstShotMin = Core.Settings.GetInt(Core.LocalPlayerWeapon.WeaponName, "Trigger Delay FirstShot Min");
                _triggerDelayShotsMax = Core.Settings.GetInt(Core.LocalPlayerWeapon.WeaponName, "Trigger Delay Shots Max");
                _triggerDelayShotsMin = Core.Settings.GetInt(Core.LocalPlayerWeapon.WeaponName, "Trigger Delay Shots Min");
                _triggerDash = Core.Settings.GetBool(Core.LocalPlayerWeapon.WeaponName, "Trigger Dash");
                _triggerZoomed = Core.Settings.GetBool(Core.LocalPlayerWeapon.WeaponName, "Trigger When Zoomed");
                _inCrossTrigger = Core.Settings.GetBool("Misc", "InCross Trigger Bot");
                _boneTrigger = Core.Settings.GetBool("Misc", "Bone Trigger Bot");
                _hitboxTrigger = Core.Settings.GetBool("Misc", "Hitbox Trigger Bot");
                */
                _triggerEnabled = SimpleExternalUI.trigger;
                _triggerKey = SimpleExternalUI.triggerkey;
                _triggerEnemies = SimpleExternalUI.triggerenemies;
                _triggerAllies = SimpleExternalUI.triggerallies;
                _triggerSpawnProtection = SimpleExternalUI.triggerspawnprotection;
                _triggerDelayFirstShotMax = SimpleExternalUI.triggerfirstshotmax;
                _triggerDelayFirstShotMin = SimpleExternalUI.triggerfirstshotmin;
                _triggerDelayShotsMax = SimpleExternalUI.triggershotsmax;
                _triggerDelayShotsMin = SimpleExternalUI.triggershotsmin;
                _triggerDash = SimpleExternalUI.triggerdash;
                _triggerZoomed = SimpleExternalUI.triggerzoomed;
                _inCrossTrigger = SimpleExternalUI.triggerincross;
                _boneTrigger = SimpleExternalUI.triggerbone;
                _hitboxTrigger = SimpleExternalUI.triggerhitbox;
            }
            catch (Exception e)
            {
#if DEBUG
                Console.WriteLine(e.Message);
#endif
            }
        }
        #endregion
    }

}
