#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Claude.Editor
{
    /// <summary>
    /// Layer 2: Mono.CSharp REPL evaluator for runtime C# expression evaluation.
    /// Gracefully degrades to no-op if Mono.CSharp is unavailable.
    /// </summary>
    public static class CSharpEvaluator
    {
        static bool _initialized;
        static bool _available;
        static object _evaluator;
        static MethodInfo _evaluateMethod;
        static MethodInfo _runMethod;

        // Context variables injected before each evaluation
        static object _currentInstance;
        static object[] _currentArgs;

        public static bool IsAvailable => _available;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                // Try to find Mono.CSharp assembly
                Type evaluatorType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    evaluatorType = asm.GetType("Mono.CSharp.Evaluator");
                    if (evaluatorType != null) break;
                }

                if (evaluatorType == null)
                {
                    // Try loading from known paths
                    try
                    {
                        var monoCsharpAsm = Assembly.Load("Mono.CSharp");
                        evaluatorType = monoCsharpAsm?.GetType("Mono.CSharp.Evaluator");
                    }
                    catch { /* ignore */ }
                }

                if (evaluatorType == null)
                {
                    Debug.Log("[CSharpDebug] Mono.CSharp not found, Layer 2 (REPL) disabled. Using Layer 1 only.");
                    _available = false;
                    return;
                }

                // Create CompilerSettings and ReportPrinter
                var settingsType = evaluatorType.Assembly.GetType("Mono.CSharp.CompilerSettings");
                var printerType = evaluatorType.Assembly.GetType("Mono.CSharp.ConsoleReportPrinter")
                    ?? evaluatorType.Assembly.GetType("Mono.CSharp.StreamReportPrinter");

                if (settingsType == null || printerType == null)
                {
                    Debug.Log("[CSharpDebug] Mono.CSharp types incomplete, Layer 2 disabled.");
                    _available = false;
                    return;
                }

                var settings = Activator.CreateInstance(settingsType);
                object printer;

                // Try ConsoleReportPrinter(System.IO.TextWriter)
                var printerCtor = printerType.GetConstructor(new[] { typeof(System.IO.TextWriter) });
                if (printerCtor != null)
                    printer = printerCtor.Invoke(new object[] { System.IO.TextWriter.Null });
                else
                    printer = Activator.CreateInstance(printerType);

                // Evaluator(CompilerSettings, ReportPrinter)
                var reportPrinterBase = evaluatorType.Assembly.GetType("Mono.CSharp.ReportPrinter");
                var evalCtor = evaluatorType.GetConstructor(new[] { settingsType, reportPrinterBase ?? printerType });
                if (evalCtor == null)
                {
                    Debug.Log("[CSharpDebug] Cannot find Evaluator constructor, Layer 2 disabled.");
                    _available = false;
                    return;
                }

                _evaluator = evalCtor.Invoke(new[] { settings, printer });

                // Find Evaluate method: object Evaluate(string input)
                _evaluateMethod = evaluatorType.GetMethod("Evaluate", new[] { typeof(string) });

                // Find Run method: bool Run(string statement)
                _runMethod = evaluatorType.GetMethod("Run", new[] { typeof(string) });

                if (_evaluateMethod == null && _runMethod == null)
                {
                    Debug.Log("[CSharpDebug] Cannot find Evaluate/Run methods, Layer 2 disabled.");
                    _available = false;
                    return;
                }

                // Pre-import common namespaces
                RunStatement("using System;");
                RunStatement("using System.Linq;");
                RunStatement("using System.Collections.Generic;");
                RunStatement("using UnityEngine;");

                _available = true;
                Debug.Log("[CSharpDebug] Layer 2 (Mono.CSharp REPL) initialized successfully.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CSharpDebug] Layer 2 init failed: {e.Message}. Using Layer 1 only.");
                _available = false;
            }
        }

        /// <summary>
        /// Evaluate a C# expression and return the result.
        /// Injects __instance and __args as accessible variables.
        /// </summary>
        public static object Evaluate(string expression, object instance = null, object[] args = null)
        {
            if (!_available)
            {
                Initialize();
                if (!_available)
                    return $"<eval-unavailable: Mono.CSharp not loaded>";
            }

            try
            {
                _currentInstance = instance;
                _currentArgs = args;

                // Inject context variables
                if (instance != null)
                    RunStatement($"var __instance = Claude.Editor.CSharpEvaluator.GetCurrentInstance();");
                if (args != null)
                    RunStatement($"var __args = Claude.Editor.CSharpEvaluator.GetCurrentArgs();");

                if (_evaluateMethod != null)
                {
                    var result = _evaluateMethod.Invoke(_evaluator, new object[] { expression + ";" });
                    return result ?? "<null>";
                }

                return "<eval-error: no Evaluate method>";
            }
            catch (Exception e)
            {
                return $"<eval-error: {e.InnerException?.Message ?? e.Message}>";
            }
        }

        /// <summary>
        /// Evaluate a boolean condition expression.
        /// Returns true if condition evaluates to truthy, false otherwise.
        /// </summary>
        public static bool EvaluateCondition(string condition, object instance = null, object[] args = null)
        {
            if (!_available)
            {
                // Fallback: try simple structured comparison
                return EvaluateSimpleCondition(condition, instance, args);
            }

            try
            {
                var result = Evaluate(condition, instance, args);
                if (result is bool b) return b;
                if (result is string s) return s != "False" && s != "false" && s != "<null>" && !s.StartsWith("<eval-");
                return result != null;
            }
            catch
            {
                return true; // On error, don't filter (capture anyway)
            }
        }

        /// <summary>
        /// Simple standalone evaluation without context (for MCP "eval" action).
        /// </summary>
        public static object EvaluateStandalone(string expression)
        {
            return Evaluate(expression, null, null);
        }

        // Accessor methods for injecting context into the REPL
        public static object GetCurrentInstance() => _currentInstance;
        public static object[] GetCurrentArgs() => _currentArgs;

        // ─── Private helpers ───

        static void RunStatement(string statement)
        {
            try
            {
                if (_runMethod != null)
                    _runMethod.Invoke(_evaluator, new object[] { statement });
            }
            catch { /* ignore init failures */ }
        }

        /// <summary>
        /// Fallback condition evaluation when Mono.CSharp is unavailable.
        /// Supports simple field comparisons: "fieldName op value"
        /// </summary>
        static bool EvaluateSimpleCondition(string condition, object instance, object[] args)
        {
            if (instance == null || string.IsNullOrWhiteSpace(condition))
                return true;

            try
            {
                // Parse "fieldName op value" format
                var parts = condition.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return true;

                string fieldName = parts[0];
                string op = parts.Length >= 2 ? parts[1] : "!=";
                string valueStr = parts.Length >= 3 ? parts[2] : "null";

                // Get field value
                var type = instance.GetType();
                var field = type.GetField(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null) return true;

                object fieldValue = field.GetValue(instance);

                // Handle null checks
                if (op == "==" && valueStr == "null") return fieldValue == null;
                if (op == "!=" && valueStr == "null") return fieldValue != null;

                if (fieldValue == null) return false;

                // Numeric comparisons
                if (double.TryParse(fieldValue.ToString(), out double fv) &&
                    double.TryParse(valueStr, out double cv))
                {
                    switch (op)
                    {
                        case "==": return Math.Abs(fv - cv) < 0.0001;
                        case "!=": return Math.Abs(fv - cv) >= 0.0001;
                        case ">":  return fv > cv;
                        case "<":  return fv < cv;
                        case ">=": return fv >= cv;
                        case "<=": return fv <= cv;
                    }
                }

                // String comparison
                if (op == "==") return fieldValue.ToString() == valueStr;
                if (op == "!=") return fieldValue.ToString() != valueStr;
            }
            catch { /* on parse error, don't filter */ }

            return true;
        }
    }
}
#endif
