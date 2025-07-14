/* by NestOfMoon. */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace JobsLimitsTweak;

public sealed class JobsLimitSettings : ModSettings
{
  public int jobLim;
  public int tickLim;
  public bool unsafeEnabled;
  public bool disableCheckEntirely;
  public int customILIndexA;
  public int customILIndexB;
  public bool lowerErrorsToWarnings;
  public override void ExposeData()
  {
    Scribe_Values.Look(ref jobLim, "JobsLimitFieldV6", 100);
    Scribe_Values.Look(ref tickLim, "TicksLimitField", 10);
    Scribe_Values.Look(ref unsafeEnabled, "ProModeField");
    Scribe_Values.Look(ref disableCheckEntirely, "DisableCheckEntirely");
    Scribe_Values.Look(ref customILIndexA, "CustomILIndexA", 66);
    Scribe_Values.Look(ref customILIndexB, "CustomILIndexB", 43);
    Scribe_Values.Look(ref lowerErrorsToWarnings, "LowerErrorsToWarnings");
    base.ExposeData();
  }
}

public class JobsLimitMod : Mod
{
  public const int Version = 6;
  public const string ModName="JobsLimitsTweak";
  public const string HarmonyId = "com.github.harmony.rimworld.nestofmoon.jobslimittweak";
  public int safeMin = 10, safeMax = 300;
  static JobsLimitSettings _settings;
  static Harmony _harmony;
  static ErrorCode errorCode;
  static string errorText;

  enum ErrorCode
  {
    None,
    MODLDCI4_NULLREF,
    MODLDCI4_MISMATCH
  }

  public override void DoSettingsWindowContents(Rect inRect)
  {
    var isUnsafe = _settings.unsafeEnabled;
    var ui = new Listing_Standard();

    ui.TextEntry("Restart game to apply changes.");

    void NumberField(string title, ref int value)
    {
      var s = ui;
      s.Label($"<color=#4f1>{value}</color> {title}");
      s.IntAdjuster(ref value, 1, isUnsafe?0:safeMin);
      s.IntAdjuster(ref value, 10, isUnsafe?0:safeMin);
      if (!isUnsafe)
        value = Mathf.Clamp(value, safeMin, safeMax);
      s.GapLine();
      // s.EndSection(s);
      ui.Gap();
    }

    ui.Begin(inRect);
    // ui.Label("Please keep value not to high. 40 and 10 should be alright.");
    // NumberField("Ticks per check", ref _settings.tickLim, ref _buffer1);
    NumberField("Max Completed Jobs in check", ref _settings.jobLim);
    ui.CheckboxLabeled("Unsafe Mode", ref _settings.unsafeEnabled, "Mod will not limit values you can input. Use it only if you know what you are doing.");
    ui.Label("Everything bellow requires Unsafe Mode");
    ui.CheckboxLabeled("Disable Check in FinalizeTick(). DANGEROUS.", ref _settings.disableCheckEntirely, "Use it only if you know what you are doing.");
    NumberField("Custom IL Index for FinalizeTick(). . (Rimworld1.6 default is 66, set to -1 to disable)", ref _settings.customILIndexA);
    NumberField("Custom IL Index for StartJob(...). (Rimworld1.6 default is 38, set to -1 to disable)", ref _settings.customILIndexB);
    ui.End();
    base.DoSettingsWindowContents(inRect);
  }

  void Patch()
  {
    // If only safe.
    if (!_settings.unsafeEnabled)
    {
      // _settings.tickLim = Mathf.Clamp(_settings.tickLim, safeMin, safeMax);
      _settings.jobLim = Mathf.Clamp(_settings.jobLim, safeMin, safeMax);
    }

    _harmony ??= new Harmony(HarmonyId);

    MethodInfo originalFinalizeTick =
      typeof(Verse.AI.Pawn_JobTracker).GetMethod("FinalizeTick", BindingFlags.NonPublic | BindingFlags.Instance);
    MethodInfo originalStartJob =
      typeof(Verse.AI.Pawn_JobTracker).GetMethod("StartJob", BindingFlags.Public | BindingFlags.Instance);

    void TranspileFunc(MethodInfo target, string transpilerName)
    {
      HarmonyMethod transpiler =
        new HarmonyMethod(typeof(JobsLimitMod).GetMethod(transpilerName,
          BindingFlags.NonPublic | BindingFlags.Static));
      _harmony.Unpatch(target, HarmonyPatchType.All, HarmonyId);
      _harmony.Patch(target, transpiler: transpiler);
    }

    if (_settings.unsafeEnabled && _settings.disableCheckEntirely)
    {
      HarmonyMethod mutter =
        new HarmonyMethod(typeof(JobsLimitMod).GetMethod(nameof(Mutter),
          BindingFlags.NonPublic | BindingFlags.Static));
      // Not sure if Unpatch() is needed.
      _harmony.Unpatch(originalFinalizeTick, HarmonyPatchType.All, HarmonyId);
      _harmony.Patch(originalFinalizeTick, prefix:mutter);
      log("Disabling Pawn_JobTracker.FinalizeTick()");
    }
    else
    {
      TranspileFunc(originalFinalizeTick, nameof(TranspilerA));
    }

    TranspileFunc(originalStartJob, nameof(TranspilerB));
  }


  static IEnumerable<CodeInstruction> TranspilerA(IEnumerable<CodeInstruction> instructions)
  {
    var ils = instructions.ToArray();
    // var tickLim = _settings.tickLim;
    var jobLim = _settings.jobLim;

    // Ticks.
    // Note: Since Rimworld1.6 I failed to recognize the update and where the value is.
    //     Seemingly, 10 ticks were changed into hardcoded 1 tick.
    // Original IL is at index 22, and instruction is 'Ldc.i4.s 10'
    //if (!TryModifyLdci4(ils[22], tickLim))
    //{
    //  error(
    //    $@"ERROR:{errorCode}, 'TicksLimit'. Mod is outdated or conflicting with other mods. If this is the only 'error' then mod still might be used. Details: Failed to transpile Pawn_JobTracker, FinalizeTick, at il 22, TicksLimit.");
    //}

    // Jobs.
    // Original IL is at index 66, and instruction is 'Ldc.i4.s 10', IL_009f
    var idx1 = 66;
    // Decide if custom il index is used.
    if (_settings.unsafeEnabled && _settings.customILIndexA >= 0)
    {
      if (_settings.customILIndexA >= ils.Length)
      {
        error($@"ERROR: Custom IL Index for FinalizeTick {_settings.customILIndexA} is out of range. (Max is {ils.Length-1})");
        idx1 = ils.Length - 1;
      }
      else
      {
        idx1 = _settings.customILIndexA;
      }
    }
    log($"Setting ({jobLim}) jobs limit. Using IL index {idx1} in Pawn_JobTracker.FinalizeTick.");

    if (!TryModifyLdci4(ils[idx1], jobLim))
    {
      error(
        $@"ERROR:{errorCode}[{errorText}]. Mod is outdated or conflicting with other mods. Details: Failed to transpile Pawn_JobTracker.FinalizeTick(), at IL index {idx1}.");
    }

    return ils;
  }


  static IEnumerable<CodeInstruction> TranspilerB(IEnumerable<CodeInstruction> instructions)
  {
    var ils = instructions.ToArray();
    // var tickLim = _settings.tickLim;
    var jobLim = _settings.jobLim;

    // Jobs.
    // Original IL is at index 43, and instruction is 'Ldc.i4.s 10'
    var idx2 = 43;
    // Decide if custom il index is used.
    if (_settings.unsafeEnabled && _settings.customILIndexB >= 0)
    {
      if (_settings.customILIndexB >= ils.Length)
      {
        error($@"ERROR: Custom IL Index for StartJob {_settings.customILIndexB} is out of range. (Max is {ils.Length-1})");
        idx2 = ils.Length - 1;
      }
      else
      {
        idx2 = _settings.customILIndexB;
      }
    }
    log($"Setting ({jobLim}) jobs limit. Using IL index {idx2} in Pawn_JobTracker.StartJob.");

    if (!TryModifyLdci4(ils[idx2], jobLim))
    {
      error(
        $@"ERROR:{errorCode}[{errorText}]. Mod is outdated or conflicting with other mods. Details: Failed to transpile Pawn_JobTracker.StartJob(...) at IL index {idx2}.");
    }

    return ils;
  }



  static bool TryModifyLdci4(CodeInstruction il, int val)
  {
    if (il == null)
    {
      errorCode = ErrorCode.MODLDCI4_NULLREF;
      errorText = String.Empty;
      return false;
    }

    // Check if opcodes types does match to SByte or Int32.
    if (il.opcode != OpCodes.Ldc_I4_S && il.opcode != OpCodes.Ldc_I4)
      // Report IL type mismatch.
    {
      errorCode = ErrorCode.MODLDCI4_MISMATCH;
      errorText = $@"underlying opcode:{il.ToString()}";
      return false;
    }

    // Return early if value not different.
    if (val == il.operand.ChangeType<int>())
    {
      errorCode = ErrorCode.None;
      errorText = String.Empty;
      return true;
    }

    // If val is in range of SignByte.
    if (val <= sbyte.MaxValue)
    {
      il.opcode = OpCodes.Ldc_I4_S;
      il.operand = (SByte) val;
    }
    // Otherwise write it as Int32
    else
    {
      il.opcode = OpCodes.Ldc_I4;
      il.operand = (Int32) val;
    }

    errorCode = ErrorCode.None;
    errorText = String.Empty;
    return true;
  }



  // Things I don't care about.
  public override string SettingsCategory()=>"JobsLimitsTweak";//.Translate();
  public JobsLimitMod(ModContentPack content) : base(content)
  {_settings = GetSettings<JobsLimitSettings>();Patch();}
  public override void WriteSettings()
  {Patch();base.WriteSettings();}
  static void log(string msg) => Log.Message($"<color=#999>{ModName}_v{Version}: {msg}</color>");
  static void warning(string msg) => Log.Warning($"{ModName}_v{Version}: {msg}");
  static void error(string msg)
  {
    if(_settings.lowerErrorsToWarnings)
      Log.Warning($"{ModName}_v{Version}: {msg}");
    else
      Log.Error($"{ModName}_v{Version}: {msg}");
  }
  // This is used to prevent a 'FinalizeTick' method from being called, if 'DisableCheckEntirely' option is set.
  static bool Mutter() => false;
}