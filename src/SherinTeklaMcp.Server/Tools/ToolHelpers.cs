using System;
using System.Collections.Generic;

namespace SherinTeklaMcp.Server.Tools
{
    /// <summary>Simple result for tools that do one small thing.</summary>
    public sealed class ActionResult
    {
        public string Status { get; set; } = "success";
        public string Message { get; set; } = "";
        public int Count { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    internal static class ToolHelpers
    {
        public const string GuidList = "Object GUIDs separated by commas, spaces or new lines.";
        public const string Source = "Which objects: 'selected' (current Tekla selection, default), 'guids' (use the guids argument) or 'filter' (use the filter arguments).";
        public const string Types = "Open API types separated by commas, e.g. 'Beam,PolyBeam,ContourPlate'. Empty = all parts.";
        public const string Orientation = "'vertical' (columns), 'nonvertical' (beams, braces) or empty.";
        public const string Intent = "The user's request in one sentence (written to the operation log).";
        public const string Apply = "false (default) = preview only, nothing changes. true = apply - only after the user agreed to the preview.";
        public const string Token = "The confirmToken returned by the preview. Required when apply=true.";

        /// <summary>Error text for the AI: the message plus the inner cause when it adds something.</summary>
        public static string Describe(Exception ex) =>
            ex.InnerException != null && ex.InnerException.Message != ex.Message ? ex.Message + " (" + ex.InnerException.Message + ")" : ex.Message;
    }
}
