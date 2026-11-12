using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Puente de comandos para construir la escena desde fuera del Editor.
///
/// El Editor de Unity ya tiene el proyecto abierto: este runner se engancha al
/// ciclo de vida del Editor ([InitializeOnLoad]) y vigila una carpeta de comandos
/// (<c>.scratch/cmd</c>). Cada archivo de texto que aparece alli contiene el nombre
/// de un metodo publico y estatico de <see cref="WinterSceneBuilder"/>, que se
/// ejecuta y cuyo resultado se escribe en <c>.scratch/results</c>.
///
/// Asi el trabajo se hace SIEMPRE dentro del Editor real (la misma superficie de
/// API que usaria un bridge MCP), pero controlado desde la linea de comandos.
/// </summary>
[InitializeOnLoad]
public static class SceneBuildCommandRunner
{
    static readonly string ScratchDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".scratch"));
    static readonly string CmdDir = Path.Combine(ScratchDir, "cmd");
    static readonly string ResultDir = Path.Combine(ScratchDir, "results");

    static double _nextPoll;
    static bool _busy;

    static SceneBuildCommandRunner()
    {
        Directory.CreateDirectory(CmdDir);
        Directory.CreateDirectory(ResultDir);
        EditorApplication.delayCall += Boot;
    }

    static void Boot()
    {
        WriteResult("_boot", new Dictionary<string, object>
        {
            ["ok"] = true,
            ["unity"] = Application.unityVersion,
            ["project"] = Application.dataPath,
            ["activeScene"] = EditorSceneManager.GetActiveScene().path,
            ["playMode"] = EditorApplication.isPlaying,
            ["time"] = DateTime.Now.ToString("O")
        });
    }

    [InitializeOnLoadMethod]
    static void Hook()
    {
        EditorApplication.update -= Poll;
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (_busy || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.timeSinceStartup < _nextPoll) return;
        _nextPoll = EditorApplication.timeSinceStartup + 0.7;

        if (!Directory.Exists(CmdDir)) return;

        string[] files;
        try { files = Directory.GetFiles(CmdDir, "*.cmd"); }
        catch { return; }
        if (files.Length == 0) return;

        Array.Sort(files, StringComparer.Ordinal);
        foreach (var file in files)
        {
            string cmd = "";
            try { cmd = File.ReadAllText(file).Trim(); } catch { }
            try { File.Delete(file); } catch { }

            if (string.IsNullOrEmpty(cmd)) continue;
            Execute(cmd);
            return;
        }
    }

    static void Execute(string command)
    {
        _busy = true;
        var logs = new List<string>();
        Application.LogCallback handler = (msg, stack, type) => logs.Add(type + ": " + msg);
        Application.logMessageReceived += handler;

        var sw = Stopwatch.StartNew();
        var payload = new Dictionary<string, object> { ["cmd"] = command };
        bool ok = false;
        string error = null;

        try
        {
            if (command == "SaveOpenScene")
            {
                EditorSceneManager.SaveOpenScenes();
                ok = true;
            }
            else
            {
                var type = typeof(WinterSceneBuilder);
                var method = type.GetMethod(command, BindingFlags.Public | BindingFlags.Static);
                if (method == null) throw new MissingMethodException("WinterSceneBuilder." + command + " no existe");
                method.Invoke(null, null);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                ok = true;
            }
        }
        catch (TargetInvocationException tie)
        {
            error = tie.InnerException != null ? tie.InnerException.ToString() : tie.ToString();
        }
        catch (Exception ex)
        {
            error = ex.ToString();
        }
        finally
        {
            sw.Stop();
            Application.logMessageReceived -= handler;
            _busy = false;
        }

        payload["ok"] = ok;
        payload["error"] = error;
        payload["ms"] = sw.ElapsedMilliseconds;
        payload["logs"] = logs;
        payload["time"] = DateTime.Now.ToString("O");
        WriteResult(command, payload);

        if (ok) Debug.Log("[RUNNER] Comando completado: " + command + " (" + sw.ElapsedMilliseconds + " ms)");
        else Debug.LogError("[RUNNER] Fallo en " + command + ": " + error);
    }

    static void WriteResult(string name, Dictionary<string, object> payload)
    {
        try
        {
            Directory.CreateDirectory(ResultDir);
            var parts = new List<string>();
            foreach (var kv in payload)
            {
                string value;
                if (kv.Value is List<string> list)
                    value = "[" + string.Join(",", list.ConvertAll(JsonStr)) + "]";
                else if (kv.Value is string s) value = JsonStr(s);
                else if (kv.Value is bool b) value = b ? "true" : "false";
                else value = Convert.ToString(kv.Value);
                parts.Add(JsonStr(kv.Key) + ":" + value);
            }
            File.WriteAllText(Path.Combine(ResultDir, name + ".json"), "{" + string.Join(",", parts) + "}");
        }
        catch (Exception e)
        {
            Debug.LogError("[RUNNER] No se pudo escribir el resultado: " + e.Message);
        }
    }

    static string JsonStr(string s)
    {
        if (s == null) return "null";
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", " ") + "\"";
    }
}
