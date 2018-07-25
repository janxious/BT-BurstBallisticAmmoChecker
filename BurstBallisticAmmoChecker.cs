using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BattleTech;
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

    [HarmonyPatch(typeof(BurstBallisticEffect), "Update")]
    static class BurstBallistic_IL_Patcheroo
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var instructionList = instructions.ToList();

            // Patch check in for first check into hitindex. want to change:
            //   if ((double) this.t >= (double) this.impactTime && (double) this.t >= (double) this.nextFloatie && (this.hitInfo.hitLocations[this.hitIndex] != 0 && this.hitInfo.hitLocations[this.hitIndex] != 65536))
            // To:
            //   if ((double) this.t >= (double) this.impactTime && (double) this.t >= (double) this.nextFloatie && this.hitIndex < this.hitInfo.hitLocations.Length && this.hitInfo.hitLocations[this.hitIndex] != 0 && this.hitInfo.hitLocations[this.hitIndex] != 65536)
            var instructionsToInsert = new List<CodeInstruction>();

            var logMethod = AccessTools.Method(typeof(Logger), "LogEffect", new[] {typeof(BurstBallisticEffect)});
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

            logMethod = AccessTools.Method(typeof(Logger), "LogEffect", new[] {typeof(BurstBallisticEffect), typeof(int)});
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