using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            HarmonyInstance.DEBUG = ModSettings.debug;
            var harmony = HarmonyInstance.Create(ModId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    static class TouchUp
    {
        static void LogEffect(BurstBallisticEffect effect)
        {
            LogEffect(effect, -1);
        }

        static void LogEffect(BurstBallisticEffect effect, int index)
        {
            var tEffect = Traverse.Create(effect);
            var hitIndexField = tEffect.Field("hitIndex");
            var hitIndex = hitIndexField.GetValue<int>();
            var sb = new StringBuilder();
            sb.AppendLine($"hitIndex: {hitIndex}");
            sb.AppendLine($"hitLocations: {effect.hitInfo.hitLocations.Length}");
            sb.AppendLine($"shotsWhenFired: {effect.weapon.ShotsWhenFired}");
            if (index >= 0)
            {
                sb.AppendLine($"index: {index}");
            }
            Logger.Debug(sb.ToString());
        }

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
            if ((double) t >= (double) effect.impactTime && (double) t >= (double)nextFloatie && hitIndex < effect.hitInfo.hitLocations.Length && effect.hitInfo.hitLocations[hitIndex] != 0 && effect.hitInfo.hitLocations[hitIndex] != 65536)
            {
                nextFloatieField.SetValue(t + floatieInterval);
                playImpactMethod.GetValue();
            }
            if ((double) t < 1.0)
                return;
            float hitDamage = effect.weapon.DamagePerShotAdjusted(effect.weapon.parent.occupiedDesignMask);
            for (int index = 0; index < effect.hitInfo.hitLocations.Length && index < effect.weapon.ShotsWhenFired; ++index)
            {
                if (effect.hitInfo.hitLocations[index] != 0 && effect.hitInfo.hitLocations[index] != 65536)
                {
                    hitIndexField.SetValue(index);
                    onImpactMethod.GetValue(new object[] {hitDamage});
                }
            }
            onCompleteMethod.GetValue();
        }

    }

    [HarmonyPatch(typeof(BurstBallisticEffect), "Update")]
    static class BurstBallistic_IL_Patcheroo
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var instructionList = instructions.ToList();
            // † this is dead for now. It works but doesn't play nicely †
            // we're gonna nuke the method mostly, and replace it with our own stuff
            //var replacerMethod = AccessTools.Method(typeof(TouchUp), "Updater", new[] {typeof(BurstBallisticEffect)});
            //var callout = new CodeInstruction(OpCodes.Callvirt, replacerMethod);
            //instructionList.RemoveRange(3, instructionList.Count - 4);
            //instructionList.Insert(3, callout);
            // † † †


            // Patch check in for first check into hitindex. want to change:
            //   if ((double) this.t >= (double) this.impactTime && (double) this.t >= (double) this.nextFloatie && (this.hitInfo.hitLocations[this.hitIndex] != 0 && this.hitInfo.hitLocations[this.hitIndex] != 65536))
            // To:
            //   if ((double) this.t >= (double) this.impactTime && (double) this.t >= (double) this.nextFloatie && this.hitIndex < this.hitInfo.hitLocations.Length && this.hitInfo.hitLocations[this.hitIndex] != 0 && this.hitInfo.hitLocations[this.hitIndex] != 65536)
            var instructionsToInsert = new List<CodeInstruction>();

            var logMethod = AccessTools.Method(typeof(TouchUp), "LogEffect", new[] {typeof(BurstBallisticEffect)});
            var logout = new CodeInstruction(OpCodes.Call, logMethod);
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldarg_0));
            instructionsToInsert.Add(logout);

            var hitIndex = AccessTools.Field(typeof(BurstBallisticEffect), "hitIndex");
            var hitInfo = AccessTools.Field(typeof(BurstBallisticEffect), "hitInfo");
            var hitLocations = AccessTools.Field(typeof(WeaponHitInfo), "hitLocations");
            var jumpLabelIndex = instructionList.FindIndex(instruction => instruction.opcode == OpCodes.Ldc_R4) - 2;
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldarg_0));                                          // this
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldfld, hitIndex));                                  // this.hitIndex
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldarg_0));                                          // this
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldflda, hitInfo));                                  // this.hitInfo
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldfld, hitLocations));                              // this.hitInfo.hitLocations
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldlen));                                            // this.hitInfo.hitLocations.Length
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Conv_I4));                                          // convert to int32
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Bge_S, instructionList[jumpLabelIndex].labels[0])); // hitIndex >= length -> goto point after conditional code 

            var nextFloatieField = AccessTools.Field(typeof(BurstBallisticEffect), "nextFloatie");
            var nextFloatieIndex = instructionList.FindIndex(instruction => 
                instruction.opcode == OpCodes.Ldfld && instruction.operand == nextFloatieField
            );
            instructionList.InsertRange(nextFloatieIndex + 2, instructionsToInsert);

            // then patch the for loop check in. I want to change:
            //   for (int index = 0; index < this.weapon.ShotsWhenFired; ++index)
            // To:
            //   for (int index = 0; index < this.hitInfo.hitLocations.Length && index < this.weapon.ShotsWhenFired; ++index)
            var damagePerShotAdjustedMethod = AccessTools.Method(
                typeof(Weapon), 
                "DamagePerShotAdjusted",
                new Type[] {typeof(DesignMaskDef)}
            );
            var damagePerShotAdjustedIndex = instructionList.FindIndex(instruction =>
                instruction.opcode == OpCodes.Callvirt && instruction.operand == damagePerShotAdjustedMethod
            );
            var retIndex = instructionList.FindIndex(instruction => instruction.opcode == OpCodes.Ret);
            var labelToJumpTo = new Label();
            instructionList[retIndex - 2].labels.Add(labelToJumpTo);
            

            instructionsToInsert.Clear();

            logMethod = AccessTools.Method(typeof(TouchUp), "LogEffect", new[] {typeof(BurstBallisticEffect), typeof(int)});
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldarg_0));
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldloc_1));
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Call, logMethod));

            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldloc_1));              // loop index variable
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldarg_0));              // "this"
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldflda, hitInfo));      // "this.hitInfo
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldfld, hitLocations));  // this.hitInfo.hitLocations
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldlen));                // this.hitInfo.hitLocations.Length
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Conv_I4));              // convert to int32
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Bge_S, labelToJumpTo)); // i >= length -> goto label for completing method
            instructionsToInsert.Add(new CodeInstruction(OpCodes.Ldloc_1));              // replicate the single code we nuked in the nop below

            var shotWhenFiredMethod = AccessTools.Method(typeof(Weapon), "get_ShotsWhenFired", new Type[] {});
            var shotsWhenFiredIndex = instructionList.FindIndex(instruction =>
                instruction.opcode == OpCodes.Callvirt && instruction.operand == shotWhenFiredMethod
            );
            var labelPreservationIndex = shotsWhenFiredIndex - 3;
            Trace.Assert(instructionList[labelPreservationIndex].opcode == OpCodes.Ldloc_1);
            instructionList[labelPreservationIndex].opcode = OpCodes.Nop;
            instructionList.InsertRange(shotsWhenFiredIndex - 2, instructionsToInsert);
            return instructionList;
        }
    }
}