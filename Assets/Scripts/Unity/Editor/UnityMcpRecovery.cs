using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace MagicBrawl.App.EditorTools
{
    /// <summary>Reconnect to an already running MCP server without restarting Unity.</summary>
    [InitializeOnLoad]
    public static class UnityMcpRecovery
    {
        private const string RequestPath = "Library/MCPForUnity/reconnect.request";
        private static bool _connecting;

        static UnityMcpRecovery()
        {
            if (File.Exists(RequestPath)) EditorApplication.update += CheckRequest;
        }

        private static void CheckRequest()
        {
            if (_connecting || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= CheckRequest;
            if (!File.Exists(RequestPath)) return;
            File.Delete(RequestPath);
            Reconnect();
        }

        [MenuItem("Tools/MCP/Reconnect Existing Server")]
        public static async void Reconnect()
        {
            if (_connecting) return;
            _connecting = true;
            try
            {
                // Keep this optional editor helper independent of the plugin assembly reference.
                Type locator = Type.GetType(
                    "MCPForUnity.Editor.Services.MCPServiceLocator, MCPForUnity.Editor", true);
                object bridge = locator.GetProperty("Bridge", BindingFlags.Public | BindingFlags.Static)
                    .GetValue(null);
                if ((bool)bridge.GetType().GetProperty("IsRunning").GetValue(bridge))
                {
                    Debug.Log("[MCP Recovery] Editor bridge is already connected.");
                    return;
                }

                var connect = (Task<bool>)bridge.GetType().GetMethod("StartAsync").Invoke(bridge, null);
                if (await connect) Debug.Log("[MCP Recovery] Editor bridge connected.");
                else Debug.LogError("[MCP Recovery] Connection failed. Start the HTTP server first.");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
            finally
            {
                _connecting = false;
            }
        }
    }
}
