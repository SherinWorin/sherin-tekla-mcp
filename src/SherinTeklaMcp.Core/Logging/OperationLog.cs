using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using SherinTeklaMcp.Core.Models;

namespace SherinTeklaMcp.Core.Logging
{
    /// <summary>One line of the operation log (MASTER INSTRUCTION section 9).</summary>
    public sealed class OperationRecord
    {
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string OperationId { get; set; } = "";
        public string Tool { get; set; } = "";
        /// <summary>The user's request in the AI's words (the 'intent' argument).</summary>
        public string Intent { get; set; } = "";
        public string Risk { get; set; } = "";
        public string Status { get; set; } = "";
        public string ModelPath { get; set; } = "";
        public string Arguments { get; set; } = "";
        public List<string> ObjectsAffected { get; set; } = new List<string>();
        /// <summary>Previous and new values, per object and field.</summary>
        public List<FieldChange> Changes { get; set; } = new List<FieldChange>();
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Validation { get; set; } = new List<string>();
    }

    /// <summary>
    /// Append-only JSON-lines log, one file per day: <c>operations-YYYYMMDD.jsonl</c>.
    /// Every preview, apply and refusal of a model-changing tool is recorded. Logging never
    /// breaks a tool call: a failed write is reported on stderr instead.
    /// </summary>
    public sealed class OperationLog
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = false };
        private readonly object _gate = new object();

        public string Directory { get; }

        public OperationLog(string directory)
        {
            Directory = directory;
        }

        public string CurrentFile(DateTime utc) =>
            Path.Combine(Directory, "operations-" + utc.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".jsonl");

        public void Append(OperationRecord record)
        {
            try
            {
                var line = JsonSerializer.Serialize(record, Options);
                lock (_gate)
                {
                    System.IO.Directory.CreateDirectory(Directory);
                    File.AppendAllText(CurrentFile(record.TimestampUtc), line + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[log] could not write operation log: " + ex.Message);
            }
        }
    }
}
