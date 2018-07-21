using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using BattleTech;
using BestHTTP;
using BestHTTP.SignalR;
using Harmony;
using Newtonsoft.Json;

namespace BurstBallisticAmmoChecker
{
    public class Core
    {
        public const string ModName = "BurstBallisticAmmoChecker";
        public const string ModId   = "com.joelmeador.BurstBallisticAmmoChecker";

        internal static Settings ModSettings = new Settings();
        internal static string ModDirectory;

        public static void Init(string directory, string settingsJSON)
        {
            ModDirectory = directory;
            try
            {
                ModSettings = JsonConvert.DeserializeObject<Settings>(settingsJSON);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                ModSettings = new Settings();
            }

            var harmony = HarmonyInstance.Create(ModId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            HarmonyInstance.DEBUG = ModSettings.debug;
        }
    }

// trying some shot delay changes
//    [HarmonyPatch(typeof(BallisticEffect), "SetupBullets")]
//    static class BallisticEffect_SetupBullets_ShotDelay_Patch
//    {
////        IL_0017: ldarg.0
////        IL_0018: ldc.r4 0.5
////        IL_001d: stfld float32 BallisticEffect::shotDelay
//        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
//        {
//            var instructionList = instructions.ToList();
//            var index = instructionList.FindIndex(ins =>
//                ins.opcode == OpCodes.Ldc_R4 && ins.operand is float f && Math.Abs(f - 0.5f) < 0.001);
//            instructionList[index].operand = (object) 0.1f;
//            return instructionList;
//        }
//    }

    static class TouchUp
    {
        static void Updater(BurstBallisticEffect effect)
        {
            if (effect.currentState != WeaponEffect.WeaponEffectState.Firing)
                return;
            var tEffect = Traverse.Create(effect);
            var t = tEffect.Field("t").GetValue<float>();
            var nextFloatieField = tEffect.Field("nextFloatie");
            var nextFloatie = nextFloatieField.GetValue<float>();
            var hitIndexField = tEffect.Field("hitIndex");
            var hitIndex = hitIndexField.GetValue<int>();
            var floatieInterval = tEffect.Field("floatieInterval").GetValue<float>();
            var playImpactMethod = tEffect.Method("PlayImpact");
            var onImpactMethod = tEffect.Method("OnImpact", new Type[]{typeof(float)});
            var onCompleteMethod = tEffect.Method("OnComplete");
            var sb = new StringBuilder();
            sb.AppendLine($"t: {t}");
            sb.AppendLine($"hitIndex: {hitIndex}");
            sb.AppendLine($"hitLocations: {effect.hitInfo.hitLocations.Length}");
            sb.AppendLine($"shotsWhenFired: {effect.weapon.ShotsWhenFired}");
            Logger.Debug(sb.ToString());
            if ((double) t >= (double) effect.impactTime && (double) t >= (double)nextFloatie && hitIndex < effect.hitInfo.hitLocations.Length && effect.hitInfo.hitLocations[hitIndex] != 0 && effect.hitInfo.hitLocations[hitIndex] != 65536)
            {
                nextFloatieField.SetValue(t + floatieInterval);
                //effect.nextFloatie = t + effect.floatieInterval;
                playImpactMethod.GetValue();
                //effect.PlayImpact();
            }
            if ((double) t < 1.0)
                return;
            float hitDamage = effect.weapon.DamagePerShotAdjusted(effect.weapon.parent.occupiedDesignMask);
            for (int index = 0; index < effect.hitInfo.hitLocations.Length && index < effect.weapon.ShotsWhenFired; ++index)
            {
                Logger.Debug($"index: {index}\nshotsWhenFired: {effect.weapon.ShotsWhenFired}\nhitlocation length: {effect.hitInfo.hitLocations.Length}");
                if (effect.hitInfo.hitLocations[index] != 0 && effect.hitInfo.hitLocations[index] != 65536)
                {
                    hitIndexField.SetValue(index);

                    onImpactMethod.GetValue(new object[] {hitDamage});
                    //effect.OnImpact(hitDamage);
                }
            }
            onCompleteMethod.GetValue();
            //effect.OnComplete();
        }

    }

    [HarmonyPatch(typeof(BurstBallisticEffect), "Update")]
    static class BurstBallistic_IL_Patcheroo
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var instructionList = instructions.ToList();
            var replacerMethod = AccessTools.Method(typeof(TouchUp), "Updater", new[] {typeof(BurstBallisticEffect)});
            var callout = new CodeInstruction(OpCodes.Callvirt, replacerMethod);
            instructionList.RemoveRange(3, instructionList.Count - 4);
            instructionList.Insert(3, callout);
            // we're gonna nuke the method mostly, and replace it with our own stuff
            // Patch check in for first check into hitindex. want to change:
            //   if ((double) this.t >= (double) this.impactTime && (double) this.t >= (double) this.nextFloatie && (this.hitInfo.hitLocations[this.hitIndex] != 0 && this.hitInfo.hitLocations[this.hitIndex] != 65536))
            // To:
            //   if ((double) this.t >= (double) this.impactTime && (double) this.t >= (double) this.nextFloatie && (this.hitIndex < this.hitInfo.hitLocations.Length && this.hitInfo.hitLocations[this.hitIndex] != 0 && this.hitInfo.hitLocations[this.hitIndex] != 65536))
//            var nextFloatieField = AccessTools.Field(typeof(BurstBallisticEffect), "nextFloatie");
//            var nextFloatieIndex = instructionList.FindIndex(instruction => 
//                instruction.opcode == OpCodes.Ldfld && instruction.operand == nextFloatieField
//            );
//            var instructionsToInsert = new List<CodeInstruction>();
//            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldarg_0));
//            var hitIndex = AccessTools.Field(typeof(BurstBallisticEffect), "hitIndex");
//            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldfld, hitIndex));
//            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldarg_0));
//            var hitInfo = AccessTools.Field(typeof(BurstBallisticEffect), "hitInfo");
//            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldflda, hitInfo));
//            var hitLocations = AccessTools.Field(typeof(WeaponHitInfo), "hitLocations");
//            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldfld, hitLocations));
//            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldlen));
//            instructionsToInsert.Add(new CodeInstruction(OpCodes.Conv_I4));
//            instructionsToInsert.Add(new CodeInstruction(OpCodes.Blt_Un, instructionList[nextFloatieIndex + 1].operand));
//            instructionList.InsertRange(nextFloatieIndex + 2, instructionsToInsert);

            // then patch the for loop check in. I want to change:
            //   for (int index = 0; index < this.weapon.ShotsWhenFired; ++index)
            // To:
            //   for (int index = 0; index < this.hitInfo.hitLocations.Length && index < this.weapon.ShotsWhenFired; ++index)

            //FileLog.Log($"length before: {instructionList.Count}");
//            var damagePerShotAdjustedMethod = AccessTools.Method(typeof(Weapon), 
//                "DamagePerShotAdjusted",
//                new Type[] {typeof(DesignMaskDef)});
//            //FileLog.Log($"found dpsam? {damagePerShotAdjustedMethod != null}");
//            var damagePerShotAdjustedIndex = instructionList.FindIndex(instruction =>
//                instruction.opcode == OpCodes.Callvirt && instruction.operand == damagePerShotAdjustedMethod
//            );
//            //FileLog.Log($"index: {damagePerShotAdjustedIndex}");
//            var jumpLabel = instructionList[damagePerShotAdjustedIndex + 5].labels[0];
//            //FileLog.Log($"label: {jumpLabel}");
//            var shotWhenFiredMethod = AccessTools.Method(typeof(Weapon), "get_ShotsWhenFired", new Type[] {});
//            //FileLog.Log($"found swfm? {shotWhenFiredMethod != null}");
//            var shotsWhenFiredIndex = instructionList.FindIndex(instruction =>
//                instruction.opcode == OpCodes.Callvirt && instruction.operand == shotWhenFiredMethod
//            );
            //FileLog.Log($"index: {shotsWhenFiredIndex}");
//            instructionsToInsert.Clear();
//            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldloc_1));
//            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldarg_0));
//            //var hitInfo = AccessTools.Field(typeof(BurstBallisticEffect), "hitInfo");
//            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldflda, hitInfo));
//            //var hitLocations = AccessTools.Field(typeof(WeaponHitInfo), "hitLocations");
//            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldfld, hitLocations));
//            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldlen));
//            instructionsToInsert.Add(new CodeInstruction(OpCodes.Conv_I4));
//            instructionsToInsert.Add(new CodeInstruction(OpCodes.Blt, jumpLabel));
            //FileLog.Log("okay?");
            //instructionList.InsertRange(shotsWhenFiredIndex + 2, instructionsToInsert);
            //FileLog.Log("still okay?");
            //FileLog.Log($"length after: {instructionList.Count}");

            StringBuilder sb = new StringBuilder();
//            Logger.ListTheStack(sb, instructionList);
//            Logger.LogStringBuilder(sb);
            FileLog.Log("flush force");
            return instructionList;
        }

//        private static bool logged = false;
//        static void Postfix()
//        {
//            if (logged) return;
//            logged = true; 
//            Logger.Debug("we did it!");
//        }
    }
}