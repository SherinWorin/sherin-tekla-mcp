using SherinTeklaMcp.Core;
using SherinTeklaMcp.Core.Settings;

namespace SherinTeklaMcp.Server
{
    internal static class ServerInfo
    {
        public const string Version = "0.1.0";

        /// <summary>Sent to the AI client at connect time: how to use this server safely.</summary>
        public const string Instructions =
            "Sherin Tekla MCP - controlled tools for Tekla Structures 2025. You never run code in Tekla; you only call these tools.\n\n" +
            "WORKFLOW: 1) tekla_get_connection_info. 2) Inspect before acting: tekla_get_selected_objects, tekla_get_objects, tekla_find_objects. " +
            "3) Calculate with tools, not in your head: tekla_quantity_report, tekla_calculate_distance, tekla_plan_wall, tekla_divide_opening. " +
            "4) Check with tekla_validate_objects. 5) Show results in Tekla with tekla_select_objects / tekla_highlight_objects.\n\n" +
            "MODEL CHANGES (tekla_create_beams, tekla_create_columns, tekla_modify_objects, tekla_delete_objects) are TWO-STEP: " +
            "first call with apply=false returns a plan, a risk level and a confirmToken - nothing is changed. Show the plan to the user in plain words. " +
            "ONLY after the user clearly agrees, call again with the SAME arguments, apply=true and the confirmToken. Never apply without the user's agreement. " +
            "Always fill 'intent' with the user's request in one sentence - it goes into the operation log.\n\n" +
            "If a write is refused (read-only mode, delete disabled, bad token, unknown profile/material), tell the user why - do not work around it. " +
            "If a write ends 'partial' or 'failed', read the objects back before trying anything again.\n\n" +
            "UNITS AND COORDINATES: millimetres and kilograms; all points are GLOBAL model coordinates written 'x,y,z'. " +
            "Never invent model data: if a tool did not return it, you do not know it.";
    }

#if NET48
    /// <summary>Kept in its own method so Tekla types load only after the assembly resolver is registered.</summary>
    internal static class TeklaBackend
    {
        public static ITeklaService Create(ServerSettings settings) => new SherinTeklaMcp.Tekla.TeklaService(settings);
    }
#endif
}
