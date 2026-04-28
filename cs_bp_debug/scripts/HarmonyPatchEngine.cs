#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Claude.Editor
{
    /// <summary>
    /// Dynamic Harmony patch engine for C# runtime debugging.
    /// Generates Prefix/Postfix/Finalizer patches to capture method invocations.
    /// </summary>
    public static class HarmonyPatchEngine
    {
        static readonly Harmony _harmony = new Harmony("com.claude.csharp-debug");

        static readonly Dictionary<string, PatchRecord> _patches = new Dictionary<string, PatchRecord>();
        static int _patchIdCounter;

        public class PatchConfig
        {
            public int MaxCaptures = 10;
            public int MaxDepth = 3;
            public int MaxFields = 30;
            public int StackDepth = 8;
            public string[] Vars;
            public string Condition;
            public string[] EvalExpressions;
        }

        class PatchRecord
        {
            public string Id;
            public string TypeName;
            public string MethodName;
            public MethodInfo OriginalMethod;
            public PatchConfig Config;
            public int CaptureCount;
        }

        // Shared state for patch callbacks (Harmony patches are static)
        static readonly Dictionary<MethodBase, PatchRecord> _methodToRecord = new Dictionary<MethodBase, PatchRecord>();

        public static string Patch(string typeName, string methodName, PatchConfig config)
        {
            var type = FindType(typeName);
            if (type == null)
                throw new Exception($"Type not found: {typeName}");

            var method = type.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (method == null)
                throw new Exception($"Method not found: {typeName}.{methodName}");

            string patchId = $"csd_{++_patchIdCounter}";
            var record = new PatchRecord
            {
                Id = patchId,
                TypeName = typeName,
                MethodName = methodName,
                OriginalMethod = method,
                Config = config ?? new PatchConfig(),
                CaptureCount = 0
            };

            var prefixMethod = typeof(HarmonyPatchEngine).GetMethod(nameof(PrefixHandler),
                BindingFlags.Static | BindingFlags.NonPublic);
            var postfixMethod = typeof(HarmonyPatchEngine).GetMethod(nameof(PostfixHandler),
                BindingFlags.Static | BindingFlags.NonPublic);
            var finalizerMethod = typeof(HarmonyPatchEngine).GetMethod(nameof(FinalizerHandler),
                BindingFlags.Static | BindingFlags.NonPublic);

            _methodToRecord[method] = record;

            _harmony.Patch(method,
                prefix: new HarmonyMethod(prefixMethod),
                postfix: new HarmonyMethod(postfixMethod),
                finalizer: new HarmonyMethod(finalizerMethod));

            _patches[patchId] = record;

            Debug.Log($"[CSharpDebug] Patched {typeName}.{methodName} → {patchId}");
            return patchId;
        }

        public static void Unpatch(string patchId)
        {
            if (!_patches.TryGetValue(patchId, out var record))
                return;

            _harmony.Unpatch(record.OriginalMethod,
                typeof(HarmonyPatchEngine).GetMethod(nameof(PrefixHandler),
                    BindingFlags.Static | BindingFlags.NonPublic));
            _harmony.Unpatch(record.OriginalMethod,
                typeof(HarmonyPatchEngine).GetMethod(nameof(PostfixHandler),
                    BindingFlags.Static | BindingFlags.NonPublic));
            _harmony.Unpatch(record.OriginalMethod,
                typeof(HarmonyPatchEngine).GetMethod(nameof(FinalizerHandler),
                    BindingFlags.Static | BindingFlags.NonPublic));

            _methodToRecord.Remove(record.OriginalMethod);
            _patches.Remove(patchId);

            Debug.Log($"[CSharpDebug] Unpatched {patchId} ({record.TypeName}.{record.MethodName})");
        }

        public static void UnpatchAll()
        {
            _harmony.UnpatchAll("com.claude.csharp-debug");
            _methodToRecord.Clear();
            _patches.Clear();
            _patchIdCounter = 0;
            Debug.Log("[CSharpDebug] All patches removed");
        }

        public static int ActivePatchCount => _patches.Count;

        public static List<Dictionary<string, object>> GetPatchInfo()
        {
            var list = new List<Dictionary<string, object>>();
            foreach (var kv in _patches)
            {
                list.Add(new Dictionary<string, object>
                {
                    { "id", kv.Key },
                    { "type", kv.Value.TypeName },
                    { "method", kv.Value.MethodName },
                    { "captures", kv.Value.CaptureCount },
                    { "maxCaptures", kv.Value.Config.MaxCaptures }
                });
            }
            return list;
        }

        // ─── Harmony Callback Handlers ───

        static void PrefixHandler(object __instance, object[] __args, MethodBase __originalMethod)
        {
            try
            {
                if (!_methodToRecord.TryGetValue(__originalMethod, out var record))
                    return;
                if (record.CaptureCount >= record.Config.MaxCaptures)
                {
                    AutoUnpatch(record);
                    return;
                }

                // Evaluate condition if set (Layer 2)
                if (!string.IsNullOrEmpty(record.Config.Condition))
                {
                    if (!CSharpEvaluator.EvaluateCondition(record.Config.Condition, __instance, __args))
                        return;
                }

                record.CaptureCount++;

                var capture = new Dictionary<string, object>
                {
                    { "index", record.CaptureCount },
                    { "type", "prefix" },
                    { "method", $"{record.TypeName}.{record.MethodName}" },
                    { "phase", "enter" },
                    { "timestamp", DateTime.UtcNow.ToString("o") }
                };

                // Capture parameters
                var paramInfos = record.OriginalMethod.GetParameters();
                var paramDict = new Dictionary<string, object>();
                for (int i = 0; i < paramInfos.Length && i < (__args?.Length ?? 0); i++)
                {
                    paramDict[paramInfos[i].Name] = SerializeValue(__args[i], record.Config.MaxDepth, record.Config.MaxFields);
                }
                capture["params"] = paramDict;

                // Capture instance fields
                if (__instance != null)
                {
                    capture["instance"] = SerializeInstance(__instance, record.Config);
                }

                // Layer 2: Evaluate expressions
                if (record.Config.EvalExpressions != null && record.Config.EvalExpressions.Length > 0)
                {
                    var evalResults = new Dictionary<string, object>();
                    foreach (var expr in record.Config.EvalExpressions)
                    {
                        evalResults[expr] = CSharpEvaluator.Evaluate(expr, __instance, __args);
                    }
                    capture["eval"] = evalResults;
                }

                // Capture stack trace
                capture["stack"] = CaptureStack(record.Config.StackDepth);

                CSharpDebugHook.WriteCapture(capture);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CSharpDebug] Prefix error: {e.Message}");
            }
        }

        static void PostfixHandler(object __instance, object __result, MethodBase __originalMethod)
        {
            try
            {
                if (!_methodToRecord.TryGetValue(__originalMethod, out var record))
                    return;
                if (record.CaptureCount < 1)
                    return;

                var capture = new Dictionary<string, object>
                {
                    { "index", record.CaptureCount },
                    { "type", "postfix" },
                    { "method", $"{record.TypeName}.{record.MethodName}" },
                    { "phase", "exit" },
                    { "result", SerializeValue(__result, record.Config.MaxDepth, record.Config.MaxFields) },
                    { "timestamp", DateTime.UtcNow.ToString("o") }
                };

                CSharpDebugHook.WriteCapture(capture);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CSharpDebug] Postfix error: {e.Message}");
            }
        }

        static Exception FinalizerHandler(Exception __exception, MethodBase __originalMethod)
        {
            try
            {
                if (__exception == null)
                    return null;

                if (!_methodToRecord.TryGetValue(__originalMethod, out var record))
                    return __exception;

                var capture = new Dictionary<string, object>
                {
                    { "index", record.CaptureCount },
                    { "type", "finalizer" },
                    { "method", $"{record.TypeName}.{record.MethodName}" },
                    { "phase", "exception" },
                    { "exception", new Dictionary<string, object>
                        {
                            { "type", __exception.GetType().FullName },
                            { "message", __exception.Message },
                            { "stackTrace", __exception.StackTrace }
                        }
                    },
                    { "timestamp", DateTime.UtcNow.ToString("o") }
                };

                CSharpDebugHook.WriteCapture(capture);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CSharpDebug] Finalizer error: {e.Message}");
            }
            return __exception;
        }

        // ─── Helpers ───

        static void AutoUnpatch(PatchRecord record)
        {
            Debug.LogWarning($"[CSharpDebug] Safety valve: {record.Id} reached {record.Config.MaxCaptures} captures, auto-unpatching");
            Unpatch(record.Id);
        }

        static Type FindType(string typeName)
        {
            // Try direct lookup first
            var type = Type.GetType(typeName);
            if (type != null) return type;

            // Search all loaded assemblies
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(typeName);
                if (type != null) return type;
            }
            return null;
        }

        public static object SerializeValue(object value, int maxDepth, int maxFields, int currentDepth = 0)
        {
            if (value == null) return null;
            if (currentDepth >= maxDepth) return value.ToString();

            var type = value.GetType();

            // Primitives and strings
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
                return value;

            // Enums
            if (type.IsEnum)
                return value.ToString();

            // Unity types
            if (value is Vector3 v3) return new { x = v3.x, y = v3.y, z = v3.z };
            if (value is Vector2 v2) return new { x = v2.x, y = v2.y };
            if (value is Quaternion q) return new { x = q.x, y = q.y, z = q.z, w = q.w };
            if (value is Color c) return new { r = c.r, g = c.g, b = c.b, a = c.a };

            // Arrays / Lists
            if (value is System.Collections.IList list)
            {
                var arr = new List<object>();
                int count = Math.Min(list.Count, maxFields);
                for (int i = 0; i < count; i++)
                    arr.Add(SerializeValue(list[i], maxDepth, maxFields, currentDepth + 1));
                if (list.Count > maxFields)
                    arr.Add($"...+{list.Count - maxFields} more");
                return arr;
            }

            // Dictionaries
            if (value is System.Collections.IDictionary dict)
            {
                var d = new Dictionary<string, object>();
                int count = 0;
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    if (count >= maxFields)
                    {
                        d[$"...+{dict.Count - maxFields} more"] = "...";
                        break;
                    }
                    d[entry.Key?.ToString() ?? "null"] = SerializeValue(entry.Value, maxDepth, maxFields, currentDepth + 1);
                    count++;
                }
                return d;
            }

            // UnityEngine.Object — just name + type
            if (value is UnityEngine.Object uObj)
            {
                return new Dictionary<string, object>
                {
                    { "_unityType", type.Name },
                    { "name", uObj.name },
                    { "instanceID", uObj.GetInstanceID() }
                };
            }

            // Generic objects — reflect fields
            var result = new Dictionary<string, object>();
            result["_type"] = type.Name;
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            int fieldCount = 0;
            foreach (var field in fields)
            {
                if (fieldCount >= maxFields) break;
                try
                {
                    result[field.Name] = SerializeValue(field.GetValue(value), maxDepth, maxFields, currentDepth + 1);
                    fieldCount++;
                }
                catch { result[field.Name] = "<error>"; }
            }
            return result;
        }

        static Dictionary<string, object> SerializeInstance(object instance, PatchConfig config)
        {
            var result = new Dictionary<string, object>();
            var type = instance.GetType();
            result["_type"] = type.FullName;

            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            int count = 0;
            foreach (var field in fields)
            {
                if (count >= config.MaxFields) break;

                // Filter by vars if specified
                if (config.Vars != null && config.Vars.Length > 0)
                {
                    bool found = false;
                    foreach (var v in config.Vars)
                    {
                        if (v == field.Name) { found = true; break; }
                    }
                    if (!found) continue;
                }

                try
                {
                    result[field.Name] = SerializeValue(field.GetValue(instance), config.MaxDepth, config.MaxFields, 1);
                    count++;
                }
                catch { result[field.Name] = "<error>"; }
            }
            return result;
        }

        static string[] CaptureStack(int depth)
        {
            var st = new StackTrace(3, true); // skip 3 frames (handler + harmony internals)
            int frameCount = Math.Min(st.FrameCount, depth);
            var frames = new string[frameCount];
            for (int i = 0; i < frameCount; i++)
            {
                var frame = st.GetFrame(i);
                var m = frame.GetMethod();
                string file = frame.GetFileName();
                int line = frame.GetFileLineNumber();
                string location = !string.IsNullOrEmpty(file) ? $" at {file}:{line}" : "";
                frames[i] = $"{m?.DeclaringType?.Name}.{m?.Name}{location}";
            }
            return frames;
        }
    }
}
#endif
