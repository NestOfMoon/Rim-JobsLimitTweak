using HarmonyLib;
using HugsLib;
using HugsLib.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Verse;
using HugsLib.Utils;

namespace NestOfMoon.JobsLimitTweak
{
    public class JobsLimitTweakMod : ModBase
    {
        #region props, setup, hugs-stuff
        public static JobsLimitTweakMod mod { get; private set; }
        internal static ModLogger log => mod.Logger;

        internal static int jobLim, tickLim;

        private SettingHandle<int> jobsLimitHandle;
        private SettingHandle<int> ticksLimitHandle;
        private SettingHandle<bool> proModeHandle;

        protected override bool HarmonyAutoPatch => false;
        private Harmony harmony;

        public override string ModIdentifier => "JobsLimitTweak";

        public JobsLimitTweakMod()
        {
            mod = this;
        }
        #endregion


        #region Entry Points
        public override void DefsLoaded()
        {
            jobsLimitHandle = Settings.GetHandle<int>("JobsLimitField", "JobsLimit","How many jobs a Pawn can start per TicksLimit. Recomended is 40, minimum is 5, recomended maximum is 127, the theoretical maximum is 2147483647", 40);
            ticksLimitHandle = Settings.GetHandle<int>("TicksLimitField", "TicksLimit", "It's just here. This is how many ticks will pass before resetting JobsLimit count. Minimum is 5, recomended is 10, recomended maximum is 127, the theoretical maximum is 2147483647", 10);
            proModeHandle = Settings.GetHandle<bool>("ProModeField", "ProMode", "Removing logical limitters on all fields. Use with caution.", false);

            Patch();
        }

        public override void SettingsChanged() => Patch();
        #endregion

        public void Patch()
        {
            jobLim =  jobsLimitHandle.Value;
            tickLim = ticksLimitHandle.Value;

            // If not in proMode. 
            if (!proModeHandle.Value)
            {
                // System.Math is being used to limit the lower bound.
                jobLim = Math.Max(jobLim, 5);
                tickLim = Math.Max(tickLim, 5);

                // And limit upper bound for ticks.
                jobLim = Math.Min(jobLim, 100);
                tickLim = Math.Min(tickLim, 100);
            }

            if (harmony == null)
                harmony = new Harmony("com.github.harmony.rimworld.nestofmoon.jobslimittweak");

            
            MethodInfo original = typeof(Verse.AI.Pawn_JobTracker).GetMethod("FinalizeTick", BindingFlags.NonPublic | BindingFlags.Instance);
            HarmonyMethod transpiler = new HarmonyMethod(typeof(JobsLimitTweakMod).GetMethod(nameof(Transpiler), BindingFlags.NonPublic | BindingFlags.Static));

            harmony.Patch(original, transpiler: transpiler);
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            JobsLimitTweakMod.log.Message($"IL modification of Pawn_JobTracker.FinalizeTick(): Ticks limit is set to {JobsLimitTweakMod.tickLim} and Jobs limit is set to {JobsLimitTweakMod.jobLim}.");

            var ils = instructions.ToArray();

            // Ticks.
            // Original IL is at index 22, and instruction is 'Ldc.i4.s 10'
            if (ModifyLdci4(ils[22], JobsLimitTweakMod.tickLim) != 0)
                JobsLimitTweakMod.log.Warning("ILModification failed. Mod is outdated or conflicting with other mod." +
                    "If this is the only 'warning' then mod still can be used." +
                    "Details: Failed to transpile Pawn_JobTracker, FinalizeTick, at il 22, TicksLimit");

            // Jobs.
            // Original IL is at index 49, and instruction is 'Ldc.i4.s 10'
            if (ModifyLdci4(ils[49], JobsLimitTweakMod.jobLim) != 0)
                JobsLimitTweakMod.log.Warning("ILModification failed. Mod is outdated or conflicting with other mod." +
                    "Details: Failed to transpile Pawn_JobTracker, FinalizeTick, at il 49, JobsLimit");

            return ils;
        }

        internal static int ModifyLdci4(CodeInstruction il, int val)
        {
            // Report null reference.
            if (il == null) return 2;

            // Check if opcodes types does match to SByte or Int32.
            if (il.opcode != OpCodes.Ldc_I4_S && il.opcode != OpCodes.Ldc_I4)
                // Report IL type mismatch.
                return 1;

            // Check if val is different, otherwise do nothing.
            if (val != il.operand.ChangeType<int>())
            {
                // If val is in range of SignByte.
                if (val <= sbyte.MaxValue)
                {
                    il.opcode = OpCodes.Ldc_I4_S;
                    il.operand = (SByte)val;
                }
                // Otherwise write it as Int32
                else
                {
                    il.opcode = OpCodes.Ldc_I4;
                    il.operand = (Int32)val;
                }
            }

            // Report no errors.
            return 0;
        }
    }
}
