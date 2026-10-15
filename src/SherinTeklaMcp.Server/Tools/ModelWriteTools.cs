using System;
using System.Collections.Generic;
using System.ComponentModel;
using ModelContextProtocol.Server;
using SherinTeklaMcp.Core.Models;
using SherinTeklaMcp.Core.Operations;

namespace SherinTeklaMcp.Server.Tools
{
    /// <summary>
    /// MEDIUM / HIGH risk: the only tools that change the model. All of them run through
    /// <see cref="WriteOperations"/>: preview first, confirm token, apply, commit, read-back check, log.
    /// </summary>
    [McpServerToolType]
    public static class ModelWriteTools
    {
        [McpServerTool(Name = "tekla_create_beams")]
        [Description("[MEDIUM risk, HIGH above the batch threshold - changes the model] Create beams (or any straight members) between two points each. " +
                     "Each item: start, end ('x,y,z' global mm), profile, material, optional class, name, positionPlane (LEFT/MIDDLE/RIGHT), positionDepth (FRONT/MIDDLE/BEHIND), positionRotation (FRONT/TOP/BACK/BELOW). " +
                     "Profile and material are checked against the Tekla catalog. Two steps: apply=false returns the plan and a confirmToken; apply=true with the token and the SAME beams creates them.")]
        public static WriteResult CreateBeams(
            WriteOperations writes,
            [Description("The beams to create.")] IReadOnlyList<PartSpec> beams,
            [Description(ToolHelpers.Intent)] string intent = "",
            [Description(ToolHelpers.Apply)] bool apply = false,
            [Description(ToolHelpers.Token)] string confirmToken = "")
        {
            try { return writes.CreateBeams(beams, apply, confirmToken, intent); }
            catch (Exception ex) { return Failed("create_beams", ex); }
        }

        [McpServerTool(Name = "tekla_create_columns")]
        [Description("[MEDIUM risk, HIGH above the batch threshold - changes the model] Create vertical columns from a base point and a height. " +
                     "Each item: base ('x,y,z' global mm), heightMm, profile, material, optional class, name and position fields. Two steps: preview (apply=false) then apply=true with the confirmToken and the SAME columns.")]
        public static WriteResult CreateColumns(
            WriteOperations writes,
            [Description("The columns to create.")] IReadOnlyList<ColumnSpec> columns,
            [Description(ToolHelpers.Intent)] string intent = "",
            [Description(ToolHelpers.Apply)] bool apply = false,
            [Description(ToolHelpers.Token)] string confirmToken = "")
        {
            try { return writes.CreateColumns(columns, apply, confirmToken, intent); }
            catch (Exception ex) { return Failed("create_columns", ex); }
        }

        [McpServerTool(Name = "tekla_modify_objects")]
        [Description("[MEDIUM risk, HIGH above the batch threshold - changes the model] Change profile, material, class, name, finish and/or string UDAs of existing parts. Empty fields are left unchanged. " +
                     "The preview lists every field with its old and new value. Example: 'change the selected members to S355' -> get the selected GUIDs, then material='S355JR' (use the exact catalog name).")]
        public static WriteResult ModifyObjects(
            WriteOperations writes,
            [Description(ToolHelpers.GuidList)] string guids,
            [Description("New profile, or empty.")] string profile = "",
            [Description("New material, or empty.")] string material = "",
            [Description("New Tekla class (whole number), or empty.")] string teklaClass = "",
            [Description("New name, or empty.")] string name = "",
            [Description("New finish, or empty.")] string finish = "",
            [Description("String UDAs to set: 'NAME=value; NAME2=value2', or empty.")] string udas = "",
            [Description(ToolHelpers.Intent)] string intent = "",
            [Description(ToolHelpers.Apply)] bool apply = false,
            [Description(ToolHelpers.Token)] string confirmToken = "")
        {
            try
            {
                var spec = new ModifySpec
                {
                    Profile = profile,
                    Material = material,
                    Class = teklaClass,
                    Name = name,
                    Finish = finish,
                    Udas = ToolInputs.ParseUdas(udas),
                };
                return writes.ModifyObjects(ToolInputs.ParseGuids(guids), spec, apply, confirmToken, intent);
            }
            catch (Exception ex) { return Failed("modify_objects", ex); }
        }

        [McpServerTool(Name = "tekla_delete_objects")]
        [Description("[HIGH risk - deletes from the model] Delete objects by GUID. Disabled unless AllowDelete is true in settings.json. Two steps: preview (apply=false) lists what will be deleted; apply=true needs the confirmToken and the SAME GUIDs. Never delete without the user's explicit agreement.")]
        public static WriteResult DeleteObjects(
            WriteOperations writes,
            [Description(ToolHelpers.GuidList)] string guids,
            [Description(ToolHelpers.Intent)] string intent = "",
            [Description(ToolHelpers.Apply)] bool apply = false,
            [Description(ToolHelpers.Token)] string confirmToken = "")
        {
            try { return writes.DeleteObjects(ToolInputs.ParseGuids(guids), apply, confirmToken, intent); }
            catch (Exception ex) { return Failed("delete_objects", ex); }
        }

        private static WriteResult Failed(string operation, Exception ex) => new WriteResult
        {
            Operation = operation,
            Status = WriteStatus.Failed,
            Summary = ToolHelpers.Describe(ex),
            NextStep = "Tell the user what failed. If apply=true was used, read the objects back before trying again.",
            Errors = new List<string> { ToolHelpers.Describe(ex) },
        };
    }
}
