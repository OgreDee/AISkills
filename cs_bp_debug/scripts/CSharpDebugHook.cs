#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace Claude.Editor
{
    /// <summary>
    /// Main API for C# runtime debugging via Harmony patches.
    /// Manages breakpoint lifecycle and JSONL output.
    ///
    /// Layer 1: HarmonyLib Prefix/Postfix — captures params, fields, return values, call stacks
    /// Layer 2: Mono.CSharp.Evaluator — runtime C# REPL for complex conditions and expressions
    /// </summary>
    public static class CSharpDebugHook
    {
        static readonly string OutputPath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "Temp", "cs_debug_capture.jsonl"));

        static StreamWriter _writer;
        static bool _active;
        static int _totalCaptures;

        static readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings
        {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            MaxDepth = 8,
            Formatting = Formatting.None
        };

        // ─── Public API ───

        /// <summary>
        /// Layer 1: Basic method watch. Patches target method to capture params/fields/return/stack.
        /// </summary>
        public static string WatchMethod(string typeName, string methodName, WatchOptions opts = null)
        {
            EnsureActive();

            var config = new HarmonyPatchEngine.PatchConfig();
            if (opts != null)
            {
                config.MaxCaptures = opts.MaxCaptures;
                config.MaxDepth = opts.MaxDepth;
                config.MaxFields = opts.MaxFields;
                config.StackDepth = opts.StackDepth;
                config.Vars = opts.Vars;
            }

            return HarmonyPatchEngine.Patch(typeName, methodName, config);
        }

        /// <summary>
        /// Layer 1+2: Enhanced method watch with REPL condition and expression evaluation.
        /// </summary>
        public static string WatchMethodEx(string typeName, string methodName, WatchExOptions opts = null)
        {
            EnsureActive();

            // Initialize Layer 2 if needed
            if (opts != null && (!string.IsNullOrEmpty(opts.Condition) ||
                (opts.EvalExpressions != null && opts.EvalExpressions.Length > 0)))
            {
                CSharpEvaluator.Initialize();
            }

            var config = new HarmonyPatchEngine.PatchConfig();
            if (opts != null)
            {
                config.MaxCaptures = opts.MaxCaptures;
                config.MaxDepth = opts.MaxDepth;
                config.MaxFields = opts.MaxFields;
                config.StackDepth = opts.StackDepth;
                config.Vars = opts.Vars;
                config.Condition = opts.Condition;
                config.EvalExpressions = opts.EvalExpressions;
            }

            return HarmonyPatchEngine.Patch(typeName, methodName, config);
        }

        /// <summary>
        /// Layer 2: Standalone expression evaluation via Mono.CSharp REPL.
        /// </summary>
        public static object Eval(string expression)
        {
            CSharpEvaluator.Initialize();
            return CSharpEvaluator.EvaluateStandalone(expression);
        }

        /// <summary>
        /// Stop all watches and close output file.
        /// </summary>
        public static void Stop()
        {
            HarmonyPatchEngine.UnpatchAll();
            CloseWriter();
            _active = false;
            _totalCaptures = 0;
            Debug.Log("[CSharpDebug] Stopped. All patches removed, output closed.");
        }

        /// <summary>
        /// Query active state and patch info.
        /// </summary>
        public static Dictionary<string, object> GetStatus()
        {
            return new Dictionary<string, object>
            {
                { "active", _active },
                { "patchCount", HarmonyPatchEngine.ActivePatchCount },
                { "totalCaptures", _totalCaptures },
                { "outputPath", OutputPath },
                { "layer2Available", CSharpEvaluator.IsAvailable },
                { "patches", HarmonyPatchEngine.GetPatchInfo() }
            };
        }

        public static bool IsActive() => _active;

        public static string GetOutputPath() => OutputPath;

        // ─── JSONL Output ───

        public static void WriteCapture(Dictionary<string, object> capture)
        {
            _totalCaptures++;

            try
            {
                string json = JsonConvert.SerializeObject(capture, _jsonSettings);

                // Write to file
                if (_writer != null)
                {
                    _writer.WriteLine(json);
                    _writer.Flush();
                }

                // Also log to console (truncated)
                string preview = json.Length > 500 ? json.Substring(0, 500) + "..." : json;
                Debug.Log($"[CSharpDebug] Capture #{_totalCaptures}: {preview}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CSharpDebug] Write error: {e.Message}");
            }
        }

        // ─── Internal ───

        static void EnsureActive()
        {
            if (_active) return;

            try
            {
                var dir = Path.GetDirectoryName(OutputPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                _writer = new StreamWriter(OutputPath, append: false, new UTF8Encoding(false), 8192);
                _active = true;
                _totalCaptures = 0;
                Debug.Log($"[CSharpDebug] Active. Output: {OutputPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[CSharpDebug] Failed to open output: {e.Message}");
            }
        }

        static void CloseWriter()
        {
            try
            {
                if (_writer != null)
                {
                    _writer.Flush();
                    _writer.Close();
                    _writer = null;
                }
            }
            catch { }
        }
    }

    // ─── Option Classes ───

    public class WatchOptions
    {
        public int MaxCaptures = 10;
        public int MaxDepth = 3;
        public int MaxFields = 30;
        public int StackDepth = 8;
        public string[] Vars;
    }

    public class WatchExOptions : WatchOptions
    {
        /// <summary>
        /// C# boolean expression for conditional breakpoint.
        /// Available variables: __instance, __args
        /// Example: "__instance.hp <= 0"
        /// </summary>
        public string Condition;

        /// <summary>
        /// C# expressions to evaluate and capture at each hit.
        /// Example: ["__instance.GetComponent<Transform>().position", "__args[0].ToString()"]
        /// </summary>
        public string[] EvalExpressions;
    }
}
#endif
