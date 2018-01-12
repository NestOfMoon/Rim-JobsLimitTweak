using Harmony;
using HugsLib;
using HugsLib.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
//using UnityEngine;
using Verse;
using HugsLib.Core;
using HugsLib.Utils;

namespace NestOfMoon.JobsLimitTweak.Standalone
{
    public class Mod : ModBase
    {
        public static Mod mod { get; private set; }
        public static ModLogger log { get { return mod.Logger; } }

        internal static int jobLim, tickLim;

        private SettingHandle<int> jobsLimitHandle;
        private SettingHandle<int> ticksLimitHandle;
        protected override bool HarmonyAutoPatch => false;
        private HarmonyInstance harmony;

        public override string ModIdentifier
        {
            get { return "JobsLimitTweak"; }
        }

        public Mod()
        {
            mod = this;
        }

        public override void DefsLoaded()
        {
            jobsLimitHandle = Settings.GetHandle<int>("JobsLimitField", "JobsLimit", "How many jobs pawn can start per TicksLimit. Recomended is 40, recomended maximum is 127, the theoretical maximum is 2147483647", 40);
            ticksLimitHandle = Settings.GetHandle<int>("TicksLimitField", "TicksLimit", "It's just here. Minimum is 10, recomended is 10, recomended maximum is 127, the theoretical maximum is 2147483647", 10);
            
            Patch();
        }

        public override void SettingsChanged() => Patch();

        public void Patch()
        {
            jobLim = jobsLimitHandle.Value;
            tickLim = ticksLimitHandle.Value;
            if(harmony == null)
                harmony = HarmonyInstance.Create("com.github.harmony.rimworld.nestofmoon.jobslimittweak");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    [HarmonyPatch(typeof(Verse.AI.Pawn_JobTracker))]
    [HarmonyPatch("FinalizeTick")]
    public static class Patch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var ils = instructions.ToArray();

            // Ticks.
            // Original IL is at number 22, and instruction is Ldc.i4.s 10
            if(!ils[22].ModifyLdci4(Mod.tickLim))
                Mod.log.Warning("Mod is outdated. If this is the only 'warn' then mod can be used in default way. Failed to transpile Pawn_JobTracker, FinalizeTick, at il 22, TicksLimit");

           // Jobs.
           // Original IL is at number 49, and instruction is Ldc.i4.s 10
           if(!ils[49].ModifyLdci4(Mod.jobLim))
            Mod.log.Warning("Mod is outdated. Failed to transpile Pawn_JobTracker, FinalizeTick, at il 49, JobsLimit");

            return ils;
        }

        // Helper method.
        public static bool ModifyLdci4(this CodeInstruction il, int val)
        {
            if (il.opcode == OpCodes.Ldc_I4_S || il.opcode == OpCodes.Ldc_I4)
            {

                if (val >= 10)
                {
                    if (val != il.operand.ChangeType<int>())
                    {
                        if (val <= sbyte.MaxValue)
                        {
                            if (il.opcode == OpCodes.Ldc_I4)
                                il.opcode = OpCodes.Ldc_I4_S;
                            il.operand = (SByte)val;
                        }
                        else
                        {
                            il.opcode = OpCodes.Ldc_I4;
                            il.operand = (int)val;
                        }
                    }
                }
                return true;
            }
            else return false;
        }

    }
}
