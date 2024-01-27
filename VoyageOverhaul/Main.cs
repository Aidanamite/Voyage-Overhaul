using HarmonyLib;
using HMLLibrary;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.UI;
using System.Globalization;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using FMODUnity;
using System.Runtime.Serialization;

namespace VoyageOverhaul {
    public class Main : Mod
    {
        Harmony harmony;
        static public Main instance;
        public bool motorAdditive = false;
        public float motorTorqueMultiplier = 1;
        public float motorSpeedMultiplier = 1;
        public float sailTorqueMultiplier = 1;
        public float sailSpeedMultiplier = 1;
        public float blockTorqueMultiplier = 1;
        public float waterSpeedMultiplier = 1;
        public void Start()
        {
            instance = this;
            (harmony = new Harmony("com.aidanamite.VoyageOverhaul")).PatchAll();
            Log("Mod has been loaded!");
        }

        public void OnModUnload()
        {
            harmony.UnpatchAll(harmony.Id);
            if (Patch_Raft.body)
                Patch_Raft.body.drag = Patch_Raft.lastDrag;
            Log("Mod has been unloaded!");
        }

        void ExtraSettingsAPI_Load() => ExtraSettingsAPI_SettingsClose();
        void ExtraSettingsAPI_SettingsClose()
        {
            motorAdditive = ExtraSettingsAPI_GetCheckboxState("motorAdditive");
            motorTorqueMultiplier = ExtraSettingsAPI_GetInputValue("motorTorqueMultiplier").ParseFloat();
            motorSpeedMultiplier = ExtraSettingsAPI_GetInputValue("motorSpeedMultiplier").ParseFloat();
            sailTorqueMultiplier = ExtraSettingsAPI_GetInputValue("sailTorqueMultiplier").ParseFloat();
            sailSpeedMultiplier = ExtraSettingsAPI_GetInputValue("sailSpeedMultiplier").ParseFloat();
            blockTorqueMultiplier = ExtraSettingsAPI_GetInputValue("blockTorqueMultiplier").ParseFloat();
            waterSpeedMultiplier = ExtraSettingsAPI_GetInputValue("waterSpeedMultiplier").ParseFloat();
        }

        string ExtraSettingsAPI_GetInputValue(string SettingName) => "".ToString();
        bool ExtraSettingsAPI_GetCheckboxState(string SettingName) => new bool();

        public static void Log(object message)
        {
            Debug.Log("[" + instance.name + "]: " + message.ToString());
        }
        public static T CreateObject<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    }

    static class ExtentionMethods
    {
        public static void CopyFieldsOf(this object value, object source)
        {
            var t1 = value.GetType();
            var t2 = source.GetType();
            while (!t1.IsAssignableFrom(t2))
                t1 = t1.BaseType;
            while (t1 != typeof(Object) && t1 != typeof(object))
            {
                foreach (var f in t1.GetFields(~BindingFlags.Default))
                    if (!f.IsStatic)
                        f.SetValue(value, f.GetValue(source));
                t1 = t1.BaseType;
            }
        }

        public static T BasicClone<T>(this T original)
        {
            var n = Main.CreateObject<T>();
            n.CopyFieldsOf(original);
            return n;
        }
        public static void EditFields(this object obj, params (string,object)[] fields)
        {
            var fs = fields.ToList();
            var t = obj.GetType();
            while (t != typeof(object))
            {
                for (int i = fs.Count - 1; i >= 0; i--) {
                    var f = t.GetField(fs[i].Item1, ~BindingFlags.Static);
                    if (f != null)
                        try
                        {
                            f.SetValue(obj,fs[i].Item2);
                            fs.RemoveAt(i);
                        } catch { }
                }
                if (fs.Count == 0)
                    break;
                t = t.BaseType;
            }
        }
        public static float ParseFloat(this string value, float EmptyFallback = 1)
        {
            if (string.IsNullOrWhiteSpace(value))
                return EmptyFallback;
            if (value.Contains(",") && !value.Contains("."))
                value = value.Replace(',', '.');
            var c = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfoByIetfLanguageTag("en-NZ");
            Exception e = null;
            float r = 0;
            try
            {
                r = float.Parse(value);
            }
            catch (Exception e2)
            {
                e = e2;
            }
            CultureInfo.CurrentCulture = c;
            if (e != null)
                throw e;
            return r;
        }
        public static Quaternion LookRotation(this Vector3 vector, Vector3? up = null) => up == null ? Quaternion.LookRotation(vector) : Quaternion.LookRotation(vector,up.Value);
        public static Quaternion Inverse(this Quaternion rot) => Quaternion.Inverse(rot);
    }

    [HarmonyPatch]
    static class Patch_Raft
    {
        public static Rigidbody body = null;

        public static float lastDrag = 1;
        public static float lastTime = -1;

        [HarmonyPatch(typeof(Raft), "FixedUpdate")]
        [HarmonyPrefix]
        static bool FixedUpdate(ref Raft __instance, ref Vector3 ___moveDirection, StudioEventEmitter ___eventEmitter_idle, ref Vector3 ___previousPosition, ref Vector3 ___anchorPosition, float ___maxDistanceFromAnchorPoint, Rigidbody ___body)
        {
            if (body == null)
                body = ___body;
            float timeDif = Time.time - lastTime;
            if (timeDif < 0)
                timeDif = 0;
            lastTime = Time.time;
            if (!LoadSceneManager.IsGameSceneLoaded)
                return true;
            if (!Raft_Network.IsHost || GameModeValueManager.GetCurrentGameModeValue().raftSpecificVariables.isRaftAlwaysAnchored || (!Raft_Network.WorldHasBeenRecieved && !GameManager.IsInNewGame) || timeDif == 0)
                return false;
            if (__instance.IsAnchored)
            {
                float num2 = __instance.transform.position.DistanceXZ(___anchorPosition);
                if (num2 >= ___maxDistanceFromAnchorPoint * 3f)
                    ___anchorPosition = __instance.transform.position;
                if (num2 > ___maxDistanceFromAnchorPoint)
                    ___body.AddForce((___anchorPosition - __instance.transform.position).XZOnly().normalized * 2f);
                if (___body.drag == 0 && lastDrag != 0)
                    ___body.drag = lastDrag;
            } else
            {
                if (___body.drag != 0)
                {
                    lastDrag = ___body.drag;
                    ___body.drag = 0;
                }
                var allSails = Sail.AllSails;
                var allMotors = Traverse.Create<RaftVelocityManager>().Field("motors").GetValue<List<MotorWheel>>();
                var pulls = new List<(Vector3, float)>();
                foreach (Sail sail in allSails)
                    if (sail.open)
                        pulls.Add((sail.GetNormalizedDirection().XZOnly().normalized * Traverse.Create(sail).Field("force").GetValue<float>() * Main.instance.sailSpeedMultiplier, 20 * Main.instance.sailTorqueMultiplier));
                var m = pulls.Count;
                foreach (MotorWheel motor in allMotors)
                    if (motor.MotorState)
                    {
                        var push = motor.PushDirection.XZOnly().normalized;
                        if (Main.instance.motorAdditive && pulls.Count > m)
                        {
                            var i = pulls.FindIndex(m, x => x.Item1.normalized == push);
                            if (i >= 0)
                            {
                                var v = pulls[i];
                                v.Item1 += push * motor.TargetSpeed() * Main.instance.motorSpeedMultiplier;
                                v.Item2 += (motor.MotorStrength + motor.ExtraMotorStrength) * Main.instance.motorTorqueMultiplier;
                                pulls[i] = v;
                                continue;
                            }
                        }
                        pulls.Add((push * motor.TargetSpeed() * Main.instance.motorSpeedMultiplier, (motor.MotorStrength + motor.ExtraMotorStrength) * Main.instance.motorTorqueMultiplier));
                    }
                pulls.Add((Vector3.forward * Mathf.Sqrt(__instance.waterDriftSpeed) * Main.instance.waterSpeedMultiplier, (ComponentManager<RaftBounds>.Value.FoundationCount * 0.9f + ComponentManager<RaftBounds>.Value.WalkableBlocksCount * 0.1f) * Main.instance.blockTorqueMultiplier));
                var vel = Vector3.zero;
                if (pulls.Any(x => x.Item2 > 0))
                {
                    var weight = 0f;
                    foreach (var i in pulls)
                        if (i.Item2 > 0)
                        {
                            vel += i.Item1 * i.Item2;
                            weight += i.Item2;
                        }
                    vel /= weight;
                }
                __instance.maxVelocity = vel.magnitude;
                if (___body.velocity != vel)
                    ___body.velocity = Vector3.MoveTowards(___body.velocity, vel, Mathf.Max(Vector3.Distance(___body.velocity, vel),1) * __instance.accelerationSpeed * timeDif);
                List<SteeringWheel> steeringWheels = RaftVelocityManager.steeringWheels;
                float num = 0f;
                foreach (SteeringWheel steeringWheel in steeringWheels)
                    num += steeringWheel.SteeringRotation;
                num = Mathf.Clamp(num, -1.5f, 1.5f);
                if (num != 0f)
                {
                    Vector3 torque = new Vector3(0f, Mathf.Tan(0.017453292f * num), 0f) * __instance.maxVelocity;
                    ___body.AddTorque(torque, ForceMode.Acceleration);
                }
            }
            ___moveDirection = ___body.velocity.XZOnly().normalized;
            ___eventEmitter_idle.SetParameter("velocity", ___body.velocity.sqrMagnitude / __instance.maxVelocity);
            ___previousPosition = ___body.transform.position;
            return false;
        }

        [HarmonyPatch(typeof(ObjectSpawner_RaftDirection), "Update")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> Update(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var ind = code.FindLastIndex(code.FindIndex(x => x.opcode == OpCodes.Call && (x.operand as MethodInfo).Name == "get_magnitude"), x => x.opcode == OpCodes.Ldarg_0);
            code.RemoveRange(ind, code.FindIndex(ind, x => x.opcode == OpCodes.Ret) - ind + 1);
            ind = code.FindLastIndex(code.FindIndex(x => x.opcode == OpCodes.Call && (x.operand as MethodInfo).Name == "get_deltaTime"), x => x.opcode.OperandType == OperandType.InlineBrTarget || x.opcode.OperandType == OperandType.ShortInlineBrTarget) + 1;
            code.RemoveRange(ind, code.FindIndex(ind, x => x.opcode == OpCodes.Ldfld && (x.operand as FieldInfo).Name == "spawnDelay") - ind - 1);
            code.InsertRange(ind, new[] {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(SpawnDirectionExtra),nameof(SpawnDirectionExtra.DistanceTraveled)))
            });
            ind = code.FindIndex(ind, x => x.opcode.OperandType == OperandType.InlineBrTarget || x.opcode.OperandType == OperandType.ShortInlineBrTarget) + 2;
            code.RemoveRange(ind, code.FindIndex(ind, x => x.opcode == OpCodes.Stfld && (x.operand as FieldInfo).Name == "spawnTimer") - ind + 1);
            code.Insert(ind, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SpawnDirectionExtra), nameof(SpawnDirectionExtra.UpdatePosition))));
            return code;
        }

        [HarmonyPatch(typeof(Raft), "Moving", MethodType.Getter)]
        [HarmonyPrefix]
        static bool Moving(ref bool __result)
        {
            __result = true;
            return false;
        }

        [HarmonyPatch(typeof(RaftVelocityManager), "MotorWheelWeightStrength", MethodType.Getter)]
        [HarmonyPrefix]
        static bool MotorWheelWeightStrength(ref MotorWheel.WeightStrength __result, Raft ___raft)
        {
            if (___raft == null || ___raft.IsAnchored)
                __result = MotorWheel.WeightStrength.NotStrongEnough;
            else if (body.velocity.sqrMagnitude < 1)
                __result = MotorWheel.WeightStrength.Weak;
            else
                __result = MotorWheel.WeightStrength.StrongEnough;
            return false;
        }

        [HarmonyPatch(typeof(MotorWheel), "RotateWheel")]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> RotateWheel(IEnumerable<CodeInstruction> instructions, ILGenerator iL)
        {
            var code = instructions.ToList();
            var ind = code.FindIndex(x => x.opcode == OpCodes.Stloc_0) + 1;
            code.RemoveRange(ind, code.FindLastIndex(code.FindIndex(x => x.opcode == OpCodes.Call && (x.operand as MethodInfo).Name == "Lerp"), x => x.opcode == OpCodes.Stloc_2) - ind + 1);
            var local1 = iL.DeclareLocal(typeof(float));
            var local2 = iL.DeclareLocal(typeof(float));
            code.InsertRange(ind, new[] {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldloca_S, local1),
                new CodeInstruction(OpCodes.Ldloca_S, local2),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MotorWheelExtra), nameof(MotorWheelExtra.GetRotationSpeed))),
                new CodeInstruction(OpCodes.Ldloc_S, local1),
                new CodeInstruction(OpCodes.Stloc_1),
                new CodeInstruction(OpCodes.Ldloc_S, local2),
                new CodeInstruction(OpCodes.Stloc_2)
            });
            return code;
        }
    }

    static class SpawnDirectionExtra
    {
        static Dictionary<ObjectSpawner_RaftDirection, Vector3> lastSpawnPos = new Dictionary<ObjectSpawner_RaftDirection, Vector3>();
        public static float DistanceTraveled(ObjectSpawner_RaftDirection spawner)
        {
            var p = Patch_Raft.body?.position ?? Vector3.zero;
            lastSpawnPos.TryGetValue(spawner, out var p2);
            return p.DistanceXZ(p2) * 0.5f;
        }
        public static void UpdatePosition(ObjectSpawner_RaftDirection spawner) => lastSpawnPos[spawner] = Patch_Raft.body?.position ?? Vector3.zero;
    }

    static class MotorWheelExtra
    {
        public static void GetRotationSpeed(MotorWheel motor, out float normalizedEvent, out float targetRotationSpeed)
        {
            var relativeVel = motor.PushDirection == Vector3.zero ? Vector3.zero : (motor.PushDirection.LookRotation().Inverse() * Patch_Raft.body.velocity);
            var normal = relativeVel.z / motor.TargetSpeed();
            if (!float.IsFinite(normal))
                normal = 0;
            normalizedEvent = normal;
            normal = Mathf.Lerp(normal, 1, ComponentManager<Raft>.Value.IsAnchored ? 0.05f : 0.5f);
            targetRotationSpeed = normal * motor.WheelRotationSpeed() * (motor.rotatesForward ? -1 : 1);
        }

        static FieldInfo wheelRotationSpeed = typeof(MotorWheel).GetField("wheelRotationSpeed", ~BindingFlags.Default);
        public static float TargetSpeed(this MotorWheel motor) => motor.WheelRotationSpeed() * Mathf.PI * 4.5f / 360;
        public static float WheelRotationSpeed(this MotorWheel motor) => (float)wheelRotationSpeed.GetValue(motor);
    }
}