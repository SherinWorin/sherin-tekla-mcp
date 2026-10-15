using System.Collections.Generic;
using SherinTeklaMcp.Core.Models;

namespace SherinTeklaMcp.Core
{
    /// <summary>
    /// The ONLY way the server reaches Tekla. Two implementations:
    /// <c>TeklaService</c> (SherinTeklaMcp.Tekla, net48, live Tekla 2025) and
    /// <see cref="Mock.MockTeklaService"/> (in-memory, for tests and demos).
    ///
    /// Low-level by design: no safety logic here. Validation, risk levels, confirmation and
    /// logging live in <see cref="Operations.WriteOperations"/> so they are identical for both
    /// backends and can be unit-tested without Tekla.
    /// All points are GLOBAL coordinates in mm.
    /// </summary>
    public interface ITeklaService
    {
        string BackendName { get; }

        ConnectionInfo GetConnectionInfo();

        /// <summary>Objects currently selected in the Tekla model view.</summary>
        ObjectQueryResult GetSelectedObjects(bool includeUdas, int limit);

        /// <summary>Objects by GUID. Unknown GUIDs are listed in <see cref="ObjectQueryResult.NotFound"/>.</summary>
        ObjectQueryResult GetObjects(IReadOnlyList<string> guids, bool includeUdas);

        /// <summary>Scans model parts and returns those matching the filter (up to <paramref name="limit"/>).</summary>
        ObjectQueryResult FindObjects(ObjectFilter filter, bool includeUdas, int limit, int maxScan);

        /// <summary>Selects the given objects in the Tekla UI. Returns how many were selected.</summary>
        int SelectObjects(IReadOnlyList<string> guids);

        /// <summary>Temporarily colours objects in the model view (UI only, nothing saved).</summary>
        int HighlightObjects(IReadOnlyList<string> guids, double red, double green, double blue);

        CatalogCheck CheckCatalog(string profile, string material);

        /// <summary>Inserts ONE beam (no commit). Returns the read-back object, or throws.</summary>
        TeklaObjectInfo InsertBeam(PartSpec spec);

        /// <summary>Applies changes to ONE part (no commit). Returns the field changes made.</summary>
        List<FieldChange> ModifyObject(string guid, ModifySpec spec);

        /// <summary>Deletes ONE object (no commit). Returns false if it was not found.</summary>
        bool DeleteObject(string guid);

        /// <summary>Commits pending changes to the model.</summary>
        void CommitChanges();
    }
}
