#if UNITY_EDITOR
using System;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Claude.Editor
{
    /// <summary>
    /// MCP tool for C# runtime debugging via Harmony patches.
    ///
    /// Actions:
    ///   watchMethod   — Layer 1 basic watch (params/fields/return/stack)
    ///   watchMethodEx — Layer 1+2 enhanced watch (conditions + expressions)
    ///   eval          — Layer 2 standalone C# expression evaluation
    ///   stop          — Remove all patches, close output
    ///   status        — Query active patches and capture count
    ///
    /// ADAPTATION NOTES:
    /// - Requires MCP for Unity package (com.coplaydev.unity-mcp)
    /// - Requires 0Harmony.dll in Assets/Plugins/Editor/
    /// </summary>
    [McpForUnityTool("csharp_debug",
        Description = "C# runtime debugging via Harmony patches. " +
                      "Actions: watchMethod (basic watch), watchMethodEx (with condition/eval), " +
                      "eval (C# expression), stop (remove all), status (query state). " +
                      "Requires Unity in Play Mode. Output: Temp/cs_debug_capture.jsonl.",
        Group = "core")]
    public static class CSharpDebugMcpTool
    {
        public static object HandleCommand(JObject @params)
        {
            string action = @params["action"]?.ToString();
            if (string.IsNullOrWhiteSpace(action))
            {
                return new ErrorResponse("Required parameter 'action' is missing. " +
                    "Valid actions: watchMethod, watchMethodEx, eval, stop, status");
            }

            // status doesn't require Play Mode
            if (action == "status")
            {
                return HandleStatus();
            }

            if (!Application.isPlaying)
            {
                return new ErrorResponse("Unity is not in Play Mode. Start the game first.");
            }

            try
            {
                switch (action)
                {
                    case "watchMethod":
                        return HandleWatchMethod(@params);
                    case "watchMethodEx":
                        return HandleWatchMethodEx(@params);
                    case "eval":
                        return HandleEval(@params);
                    case "stop":
                        return HandleStop();
                    default:
                        return new ErrorResponse($"Unknown action: {action}. " +
                            "Valid actions: watchMethod, watchMethodEx, eval, stop, status");
                }
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error: {e.Message}");
            }
        }

        static object HandleWatchMethod(JObject p)
        {
            string typeName = p["type"]?.ToString();
            string methodName = p["method"]?.ToString();

            if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(methodName))
                return new ErrorResponse("'type' and 'method' are required for watchMethod.");

            var opts = new WatchOptions();
            if (p["maxCaptures"] != null) opts.MaxCaptures = (int)p["maxCaptures"];
            if (p["maxDepth"] != null) opts.MaxDepth = (int)p["maxDepth"];
            if (p["maxFields"] != null) opts.MaxFields = (int)p["maxFields"];
            if (p["stackDepth"] != null) opts.StackDepth = (int)p["stackDepth"];
            if (p["vars"] is JArray varsArr)
            {
                opts.Vars = new string[varsArr.Count];
                for (int i = 0; i < varsArr.Count; i++)
                    opts.Vars[i] = varsArr[i].ToString();
            }

            string patchId = CSharpDebugHook.WatchMethod(typeName, methodName, opts);

            return new SuccessResponse(
                $"Watching {typeName}.{methodName} (patch={patchId}, max={opts.MaxCaptures}). " +
                $"Output: {CSharpDebugHook.GetOutputPath()}");
        }

        static object HandleWatchMethodEx(JObject p)
        {
            string typeName = p["type"]?.ToString();
            string methodName = p["method"]?.ToString();

            if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(methodName))
                return new ErrorResponse("'type' and 'method' are required for watchMethodEx.");

            var opts = new WatchExOptions();
            if (p["maxCaptures"] != null) opts.MaxCaptures = (int)p["maxCaptures"];
            if (p["maxDepth"] != null) opts.MaxDepth = (int)p["maxDepth"];
            if (p["maxFields"] != null) opts.MaxFields = (int)p["maxFields"];
            if (p["stackDepth"] != null) opts.StackDepth = (int)p["stackDepth"];
            if (p["vars"] is JArray varsArr)
            {
                opts.Vars = new string[varsArr.Count];
                for (int i = 0; i < varsArr.Count; i++)
                    opts.Vars[i] = varsArr[i].ToString();
            }
            opts.Condition = p["condition"]?.ToString();
            if (p["eval"] is JArray evalArr)
            {
                opts.EvalExpressions = new string[evalArr.Count];
                for (int i = 0; i < evalArr.Count; i++)
                    opts.EvalExpressions[i] = evalArr[i].ToString();
            }

            string patchId = CSharpDebugHook.WatchMethodEx(typeName, methodName, opts);

            string condInfo = !string.IsNullOrEmpty(opts.Condition) ? $", condition='{opts.Condition}'" : "";
            string evalInfo = opts.EvalExpressions != null ? $", eval={opts.EvalExpressions.Length} exprs" : "";

            return new SuccessResponse(
                $"Watching {typeName}.{methodName} (patch={patchId}, max={opts.MaxCaptures}{condInfo}{evalInfo}). " +
                $"Layer2={CSharpEvaluator.IsAvailable}. Output: {CSharpDebugHook.GetOutputPath()}");
        }

        static object HandleEval(JObject p)
        {
            string expression = p["expression"]?.ToString();
            if (string.IsNullOrWhiteSpace(expression))
                return new ErrorResponse("'expression' is required for eval.");

            var result = CSharpDebugHook.Eval(expression);
            return new SuccessResponse($"Eval result: {result}",
                new { expression, result = result?.ToString() });
        }

        static object HandleStop()
        {
            CSharpDebugHook.Stop();
            return new SuccessResponse("All C# debug patches removed and output closed.");
        }

        static object HandleStatus()
        {
            var status = CSharpDebugHook.GetStatus();
            return new SuccessResponse("C# debug status", status);
        }
    }
}
#endif
