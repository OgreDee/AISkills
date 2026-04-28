#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Editor-only JSONL writer for DebugHook capture output.
/// No [LuaCallCSharp] — XLua accesses via LazyReflectionWrap in Editor.
/// </summary>
public static class DebugCaptureWriter
{
    static readonly string FilePath = Path.GetFullPath(
        Path.Combine(Application.dataPath, "..", "Temp", "debug_capture.jsonl"));

    static StreamWriter _writer;
    static readonly StringBuilder _buffer = new StringBuilder(4096);
    static bool _opened;

    public static string GetFilePath()
    {
        return FilePath;
    }

    public static void Open()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _writer = new StreamWriter(FilePath, append: false, new UTF8Encoding(false), 8192);
            _opened = true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DebugCapture] Open failed: " + e.Message);
            _opened = false;
        }
    }

    public static void Append(string text)
    {
        if (!_opened) return;
        _buffer.Append(text);
    }

    public static void Flush()
    {
        if (!_opened || _buffer.Length == 0) return;
        try
        {
            _writer.Write(_buffer.ToString());
            _writer.Flush();
            _buffer.Clear();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DebugCapture] Flush failed: " + e.Message);
            Close();
        }
    }

    public static void Close()
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
        _opened = false;
        _buffer.Clear();
    }

    public static bool IsOpened()
    {
        return _opened;
    }
}
#endif
