using System;
using System.Collections.Generic;
using Gear;
using Player;
using UnityEngine;
using PreLuna.ThirdPerson.Body;

namespace PreLuna.ThirdPerson.Runtime
{
    internal static class GlueGunPoseProbe
    {
        static readonly eGearComponent[] GearComponents = Enum.GetValues<eGearComponent>();

        internal static object Capture(LocalPlayerAgent local, WorldWeaponView view, bool copyEnabled, bool melee=false,bool resourcePack=false,bool flashlight=false)
        {
            try
            {
                var item = local?.Inventory?.WieldedItem;
                bool matchItemData=resourcePack||flashlight;
                bool supported=flashlight?NativeBodyHolding.IsLongRangeFlashlight(item):resourcePack?NativeBodyHolding.SupportsResourcePack(item)
                    :melee?item?.TryCast<MeleeWeaponFirstPerson>()!=null:item?.TryCast<GlueGun>()!=null;
                if (!supported)
                    return new { frame = Time.frameCount,
                        status = flashlight?"local-not-wielding-long-range-flashlight":resourcePack?"local-not-wielding-resource-pack":melee?"local-not-wielding-melee":"local-not-wielding-glue-gun", copyEnabled };

                var localGear = GearParts(item);
                var bots = new List<object>();
                var botEquipment = new List<object>();
                int matched = 0;
                for (int slot = 0; slot < 4; slot++)
                {
                    var bot = PlayerManager.Current?.GetPlayerAgentInSlot(slot);
                    if (bot == null || bot.Pointer == local.Pointer || (!matchItemData&&bot.Owner?.IsBot!=true) ||
                        !bot.Alive || bot.DimensionIndex != local.DimensionIndex) continue;
                    var botItem = bot.Inventory?.WieldedItem;
                    if(melee||matchItemData)botEquipment.Add(new { slot, item = botItem?.name, dataId = botItem?.ItemDataBlock?.persistentID });
                    bool sameKind=matchItemData?botItem?.ItemDataBlock?.persistentID==item.ItemDataBlock.persistentID
                        :melee?botItem?.TryCast<MeleeWeaponThirdPerson>()!=null:botItem?.TryCast<GlueGun>()!=null;
                    if (!sameKind) continue;
                    var botGear = GearParts(botItem);
                    bool sameGear = item.ItemDataBlock != null && botItem.ItemDataBlock != null &&
                        item.ItemDataBlock.persistentID == botItem.ItemDataBlock.persistentID &&
                        (matchItemData||localGear != null && botGear != null && localGear.AsSpan().SequenceEqual(botGear));
                    if (sameGear) matched++;
                    bots.Add(new { slot, sameGear, gearParts = botGear, pose = Pose(bot, botItem, null) });
                }
                string status = matched == 0 ? (flashlight?"no-matching-player-wielding-long-range-flashlight":resourcePack?"no-matching-player-wielding-resource-pack":melee?"no-matching-bot-wielding-melee":"no-matching-bot-wielding-glue-gun") :
                    view == null ? "matching-bot-no-local-copy-render" : "paired-copy-render";
                var timer=resourcePack?item.TryCast<ResourcePackFirstPerson>()?.m_interactApplyResource:null;
                return new { frame = Time.frameCount, status, copyEnabled, localCopyRendered = view != null, matched,
                    gearPartOrder = Array.ConvertAll(GearComponents, c => c.ToString()), localGear,
                    local = Pose(local, item, view), bots, botEquipment,
                    interaction=timer==null?null:new{active=timer.TimerIsActive,progress=timer.InteractionTimerRel,
                        action=NativeBodyHolding.ReadResourcePackAction(item).ToString()} };
            }
            catch (Exception error)
            {
                return new { frame = Time.frameCount, status = "probe-error", error = error.GetBaseException().Message };
            }
        }

        static uint[] GearParts(ItemEquippable item)
        {
            var gear = item.GearIDRange;
            if (gear == null || gear.GetCompID(eGearComponent.Category) == 0) return null;
            return Array.ConvertAll(GearComponents, c => c == eGearComponent.None ? 0u : gear.GetCompID(c));
        }

        static object Pose(PlayerAgent player, ItemEquippable item, WorldWeaponView copy)
        {
            var body = player.AnimatorBody;
            var rig = player.SyncIK;
            var references = rig?.m_fullbodyBipedIK?.references ?? rig?.m_bipedIK?.references;
            var root = body?.transform;
            var rightHand = body?.isHuman == true ? body.GetBoneTransform(HumanBodyBones.RightHand) : null;
            var leftHand = body?.isHuman == true ? body.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            rightHand ??= references?.rightHand ?? rig?.m_rightArmSolver?.bone3?.transform;
            leftHand ??= references?.leftHand ?? rig?.m_leftArmSolver?.bone3?.transform;
            var anchor = player.PlayerSyncModel?.ItemAlign;
            var itemRoot = copy == null ? item.transform : copy.AimReference;
            var visualSource = copy?.VisualSource ?? item;
            var velocity = player.Locomotion?.Velocity;
            var layers = new List<object>();
            if (body != null)
                for (int i = 0; i < body.layerCount; i++)
                {
                    var state = body.GetCurrentAnimatorStateInfo(i);
                    layers.Add(new { index = i, name = body.GetLayerName(i), weight = body.GetLayerWeight(i),
                        state = state.fullPathHash, shortState = state.shortNameHash, time = state.normalizedTime,
                        transitioning = body.IsInTransition(i) });
                }
            return new
            {
                locomotion = player.Locomotion?.m_currentStateEnum.ToString(),
                horizontalSpeed = velocity is Vector3 v ? (float?)Mathf.Sqrt(v.x*v.x + v.z*v.z) : null,
                item = item.name, dataId = item.ItemDataBlock?.persistentID,
                modelMode = copy != null ? "copy" : item.m_isFirstPerson ? "native-first-person" : "native-third-person",
                movement = item.ThirdPersonFullbodyMovement.ToString(),
                leftGripAnimation = item.LeftHandGripAnim, rightGripAnimation = item.RightHandGripAnim,
                bodyScale = root == null ? null : Vector(root.lossyScale), layers,
                model = TransformPose(root, itemRoot), anchor = TransformPose(root, anchor),
                meleeAnchor = TransformPose(root, player.PlayerSyncModel?.m_alignGearMelee),
                resourcePackAnchor = TransformPose(root, player.PlayerSyncModel?.m_alignResourcePack),
                rightHand = TransformPose(root, rightHand), leftHand = TransformPose(root, leftHand),
                rightGrip = GripPose(root, rightHand, visualSource.RightHandGripTrans, visualSource.transform, itemRoot),
                leftGrip = GripPose(root, leftHand, visualSource.LeftHandGripTrans, visualSource.transform, itemRoot),
                rightIkWeight = rig?.m_rightArmSolver?.IKPositionWeight,
                leftIkWeight = rig?.m_leftArmSolver?.IKPositionWeight,
                aimIkWeight = rig?.m_aimSolver?.IKPositionWeight
            };
        }

        static object TransformPose(Transform root, Transform value)
        {
            if (root == null || value == null) return null;
            return new { name = value.name, parent = value.parent?.name, position = Vector(root.InverseTransformPoint(value.position)),
                rotation = Rotation(Quaternion.Inverse(root.rotation)*value.rotation) };
        }

        static object GripPose(Transform root, Transform hand, Transform grip, Transform source, Transform displayed)
        {
            if (root == null || grip == null || displayed == null || source == null) return null;
            var position = displayed.TransformPoint(source.InverseTransformPoint(grip.position));
            var rotation = displayed.rotation*Quaternion.Inverse(source.rotation)*grip.rotation;
            return new { name = grip.name, position = Vector(root.InverseTransformPoint(position)),
                rotation = Rotation(Quaternion.Inverse(root.rotation)*rotation),
                handDistanceMeters = hand == null ? (float?)null : (position-hand.position).magnitude,
                handAngleDegrees = hand == null ? (float?)null : Quaternion.Angle(rotation, hand.rotation) };
        }

        static float[] Vector(Vector3 value) => new[] {value.x, value.y, value.z};
        static float[] Rotation(Quaternion value) => new[] {value.x, value.y, value.z, value.w};
    }
}
