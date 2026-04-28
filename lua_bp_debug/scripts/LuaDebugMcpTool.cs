#if UNITY_EDITOR
using System;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Claude.Editor
{
    /// <summary>
    /// MCP tool for executing Lua code at runtime.
    ///
    /// ADAPTATION NOTES for other projects:
    /// - Requires MCP for Unity package (com.mcp4u.mcpforunity)
    /// - Replace LuaManager.Instance / EditorDoString with your project's Lua runtime access
    /// - If using ToLua/SLua/other Lua binding, adapt the luaState access method
    /// </summary>
    [McpForUnityTool("lua_debug",
        Description = "Execute Lua code at runtime via LuaManager.EditorDoString. " +
                      "Use for DebugHook breakpoint commands (WatchLine/WatchMulti/WatchCall/Stop). " +
                      "Requires Unity in Play Mode with LuaManager initialized. " +
                      "Returns execution result or error message.",
        Group = "core")]
    public static class LuaDebugMcpTool
    {
        public static object HandleCommand(JObject @params)
        {
            string code = @params["code"]?.ToString();
            if (string.IsNullOrWhiteSpace(code))
            {
                return new ErrorResponse("Required parameter 'code' is missing or empty.");
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
                var results = luaManager.EditorDoString(code, "ClaudeDebugHook");

                string resultStr = "ok";
                if (results != null && results.Length > 0)
                {
                    resultStr = string.Join(", ",
                        Array.ConvertAll(results, r => r?.ToString() ?? "nil"));
                }

                return new SuccessResponse($"Lua executed. Result: {resultStr}");
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Lua error: {e.Message}");
            }
        }
    }
}
#endif
