#if UNITY_EDITOR
using System;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Claude.Editor
{
    /// <summary>
    /// MCP tool for Lua hot-reload.
    /// Executes HotReload.Execute(modPath) at runtime to patch module in-place.
    ///
    /// ADAPTATION NOTES:
    /// - Requires MCP for Unity package (com.mcp4u.mcpforunity)
    /// - Replace LuaManager.Instance / EditorDoString with your project's Lua runtime access
    /// </summary>
    [McpForUnityTool("lua_hot_reload",
        Description = "Hot-reload a Lua module by path. Patches new functions into cached module table " +
                      "so existing instances pick up changes without restarting. " +
                      "Requires Unity in Play Mode with LuaManager initialized. " +
                      "Parameter: modPath (string) - require path like 'UI/Hero/UI_Hero_HeroList'.",
        Group = "core")]
    public static class LuaHotReloadMcpTool
    {
        public static object HandleCommand(JObject @params)
        {
            string modPath = @params["modPath"]?.ToString();
            if (string.IsNullOrWhiteSpace(modPath))
            {
                return new ErrorResponse("Required parameter 'modPath' is missing or empty.");
            }

            if (!Application.isPlaying)
            {
                return new ErrorResponse("Unity is not in Play Mode. Start the game first.");
            }

            var luaManager = LuaManager.Instance;
            if (luaManager == null || !luaManager.HaveLuaState())
            {
                return new ErrorResponse("LuaManager not initialized. Game may still be loading.");
            }

            try
            {
                // Ensure HotReload module is loaded
                string luaCode = string.Format(
                    @"local HR = require('Utils/HotReload')
                      local ok, msg = HR.Execute('{0}')
                      if ok then return 'OK:' .. msg else return 'FAIL:' .. msg end",
                    modPath.Replace("'", "\\'"));

                var results = luaManager.EditorDoString(luaCode, "LuaHotReload");

                string resultStr = "no result";
                if (results != null && results.Length > 0)
                {
                    resultStr = results[0]?.ToString() ?? "nil";
                }

                if (resultStr.StartsWith("OK:"))
                {
                    return new SuccessResponse($"Hot-reload success: {resultStr.Substring(3)}");
                }
                else if (resultStr.StartsWith("FAIL:"))
                {
                    return new ErrorResponse($"Hot-reload failed: {resultStr.Substring(5)}");
                }
                else
                {
                    return new SuccessResponse($"Lua executed. Result: {resultStr}");
                }
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Lua error: {e.Message}");
            }
        }
    }
}
#endif
