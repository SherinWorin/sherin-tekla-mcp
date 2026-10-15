using System;
using System.ComponentModel;
using ModelContextProtocol.Server;
using SherinTeklaMcp.Core;
using SherinTeklaMcp.Core.Logging;
using SherinTeklaMcp.Core.Models;
using SherinTeklaMcp.Core.Operations;
using SherinTeklaMcp.Core.Settings;

namespace SherinTeklaMcp.Server.Tools
{
    /// <summary>LOW risk: connection, reading objects, searching, selecting and highlighting in the UI.</summary>
    [McpServerToolType]
    public static class ModelReadTools
    {
        [McpServerTool(Name = "tekla_get_connection_info")]
        [Description("[LOW risk, read-only] Check the connection to Tekla Structures 2025: model name and path, which Tekla DLLs are used, and the safety settings (read-only mode, delete allowed). Call this first.")]
        public static ConnectionInfo GetConnectionInfo(ITeklaService tekla, ServerSettings settings, OperationLog log)
        {
            ConnectionInfo info;
            try { info = tekla.GetConnectionInfo(); }
            catch (Exception ex) { info = new ConnectionInfo { Connected = false, Backend = tekla.BackendName, Message = ToolHelpers.Describe(ex) }; }
            info.ServerVersion = ServerInfo.Version;
            info.ReadOnlyMode = settings.ReadOnly;
            info.DeleteAllowed = settings.AllowDelete;
            info.SettingsFile = settings.LoadedFrom;
            info.OperationLogFolder = log.Directory;
            return info;
        }

        [McpServerTool(Name = "tekla_get_selected_objects")]
        [Description("[LOW risk, read-only] Objects currently selected in the Tekla model: GUID, type, name, profile, material, class, finish, numbering prefixes, positions, start/end points (global mm), axis length, Tekla LENGTH and WEIGHT (kg). Set includeUdas=true for user-defined attributes.")]
        public static ObjectQueryResult GetSelectedObjects(
            ITeklaService tekla,
            ServerSettings settings,
            [Description("Also read user-defined attributes (slower).")] bool includeUdas = false,
            [Description("Maximum objects to return (capped by the server setting MaxObjectsPerRead).")] int limit = 200)
        {
            try { return tekla.GetSelectedObjects(includeUdas, ToolInputs.ClampLimit(limit, settings)); }
            catch (Exception ex) { return Error(ex); }
        }

        [McpServerTool(Name = "tekla_get_objects")]
        [Description("[LOW risk, read-only] Read objects by GUID (same fields as tekla_get_selected_objects). Unknown GUIDs are listed in notFound.")]
        public static ObjectQueryResult GetObjects(
            ITeklaService tekla,
            ServerSettings settings,
            [Description(ToolHelpers.GuidList)] string guids,
            [Description("Also read user-defined attributes.")] bool includeUdas = false)
        {
            try
            {
                var ids = ToolInputs.ParseGuids(guids);
                if (ids.Count == 0) throw new ArgumentException("No GUIDs given.");
                if (ids.Count > settings.MaxObjectsPerRead) throw new ArgumentException($"Too many GUIDs ({ids.Count}); the limit is {settings.MaxObjectsPerRead}.");
                return tekla.GetObjects(ids, includeUdas);
            }
            catch (Exception ex) { return Error(ex); }
        }

        [McpServerTool(Name = "tekla_find_objects")]
        [Description("[LOW risk, read-only] Search model PARTS with a structured filter (all given fields must match, case-insensitive). Examples: all IPE200 -> profile='IPE200'; all columns -> orientation='vertical'; all RHS -> profileContains='RHS'. Returns at most 'limit' objects and says if more matched.")]
        public static ObjectQueryResult FindObjects(
            ITeklaService tekla,
            ServerSettings settings,
            [Description(ToolHelpers.Types)] string types = "",
            [Description("Exact profile, e.g. 'HEB200'.")] string profile = "",
            [Description("Part of the profile name, e.g. 'RHS'.")] string profileContains = "",
            [Description("Exact material, e.g. 'S355JR'.")] string material = "",
            [Description("Exact Tekla class, e.g. '3'.")] string teklaClass = "",
            [Description("Part of the name, e.g. 'COLUMN'.")] string nameContains = "",
            [Description(ToolHelpers.Orientation)] string orientation = "",
            [Description("Also read user-defined attributes of the returned objects.")] bool includeUdas = false,
            [Description("Maximum objects to return.")] int limit = 200)
        {
            try
            {
                var filter = ToolInputs.BuildFilter(types, profile, profileContains, material, teklaClass, nameContains, orientation);
                if (filter.IsEmpty) throw new ArgumentException("Give at least one filter field. To list everything use a broad filter such as types='Beam'.");
                return tekla.FindObjects(filter, includeUdas, ToolInputs.ClampLimit(limit, settings), settings.MaxObjectsToScan);
            }
            catch (Exception ex) { return Error(ex); }
        }

        [McpServerTool(Name = "tekla_select_objects")]
        [Description("[LOW risk, UI only] Select objects in the Tekla model view by GUID so the user can see them. Changes nothing in the model.")]
        public static ActionResult SelectObjects(ITeklaService tekla, [Description(ToolHelpers.GuidList)] string guids)
        {
            try
            {
                var ids = ToolInputs.ParseGuids(guids);
                if (ids.Count == 0) throw new ArgumentException("No GUIDs given.");
                var n = tekla.SelectObjects(ids);
                var result = new ActionResult { Count = n, Message = $"Selected {n} of {ids.Count} object(s) in Tekla." };
                if (n < ids.Count) result.Warnings.Add($"{ids.Count - n} GUID(s) were not found.");
                return result;
            }
            catch (Exception ex) { return new ActionResult { Status = "error", Message = ToolHelpers.Describe(ex) }; }
        }

        [McpServerTool(Name = "tekla_highlight_objects")]
        [Description("[LOW risk, UI only] Temporarily colour objects in the Tekla model view (default red). Nothing is saved; a redraw in Tekla clears it.")]
        public static ActionResult HighlightObjects(
            ITeklaService tekla,
            [Description(ToolHelpers.GuidList)] string guids,
            [Description("Red 0..1.")] double red = 1.0,
            [Description("Green 0..1.")] double green = 0.0,
            [Description("Blue 0..1.")] double blue = 0.0)
        {
            try
            {
                var ids = ToolInputs.ParseGuids(guids);
                if (ids.Count == 0) throw new ArgumentException("No GUIDs given.");
                double Clamp(double v) => double.IsNaN(v) ? 0 : Math.Max(0, Math.Min(1, v));
                var n = tekla.HighlightObjects(ids, Clamp(red), Clamp(green), Clamp(blue));
                return new ActionResult { Count = n, Message = $"Highlighted {n} of {ids.Count} object(s)." };
            }
            catch (Exception ex) { return new ActionResult { Status = "error", Message = ToolHelpers.Describe(ex) }; }
        }

        private static ObjectQueryResult Error(Exception ex) => new ObjectQueryResult { Status = "error", Message = ToolHelpers.Describe(ex) };
    }
}
