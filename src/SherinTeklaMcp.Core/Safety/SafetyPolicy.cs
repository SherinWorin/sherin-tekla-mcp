using System.Text.Json.Serialization;
using SherinTeklaMcp.Core.Settings;

namespace SherinTeklaMcp.Core.Safety
{
    /// <summary>Project risk levels (MASTER INSTRUCTION section 9). Serialized as text: "Low", "Medium", "High".</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RiskLevel
    {
        /// <summary>Read, search, calculate, select/highlight in the UI.</summary>
        Low = 0,
        /// <summary>Create a limited number of objects, modify properties of a small set.</summary>
        Medium = 1,
        /// <summary>Delete, or create/modify more than the high-risk threshold.</summary>
        High = 2,
    }

    public enum WriteKind
    {
        Create,
        Modify,
        Delete,
    }

    /// <summary>
    /// Decides the risk level of a write and whether it may run at all. Enforced in the
    /// write pipeline for every call - hiding tools is not treated as protection.
    /// </summary>
    public static class SafetyPolicy
    {
        public static RiskLevel Classify(WriteKind kind, int objectCount, ServerSettings settings)
        {
            if (kind == WriteKind.Delete) return RiskLevel.High;
            return objectCount > settings.HighRiskObjectCount ? RiskLevel.High : RiskLevel.Medium;
        }

        /// <summary>Returns a refusal reason, or null when the write is allowed to proceed to preview/apply.</summary>
        public static string? CheckAllowed(WriteKind kind, int objectCount, ServerSettings settings)
        {
            if (settings.ReadOnly)
                return "The server is in READ-ONLY mode (settings.json \"ReadOnly\": true). " +
                       "Nothing was changed. To allow model changes, set \"ReadOnly\": false and restart the MCP client.";
            if (kind == WriteKind.Delete && !settings.AllowDelete)
                return "Delete is disabled (settings.json \"AllowDelete\": false). Nothing was deleted.";
            if (objectCount <= 0)
                return "Nothing to do: no objects in the request.";
            if (objectCount > settings.MaxObjectsPerWrite)
                return $"Too many objects in one call ({objectCount}); the limit is {settings.MaxObjectsPerWrite}. Split the work into smaller batches.";
            return null;
        }
    }
}
