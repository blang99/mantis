using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace Mantis.Plugin.ScriptBuilder;

/// <summary>
/// Deterministic post-build pass that guarantees a generated script resolves to
/// geometry with NO "failed to collect data" warnings.
///
/// For every REQUIRED component input that the LLM left unconnected (no source,
/// no persistent default), it injects a sensible default value as persistent
/// data so the graph still computes:
///   Point  → world origin (0,0,0)
///   Plane  → world XY
///   Vector → Z axis (0,0,1)
///   Number → 1
///   Integer→ 1
///   Boolean→ true
///
/// Inputs that need a Curve / Surface / Brep / Mesh / Geometry have no safe
/// scalar default and are left untouched — those must be wired by the model
/// (enforced via the system prompt). This is the safety net behind the prompt:
/// even if the model picks "Circle CNR" and forgets the Center point, the empty
/// Center is filled with the origin and a valid circle is produced.
/// </summary>
public static class CanvasRepair
{
    /// <summary>
    /// Inspect every placed component and fill empty required inputs with
    /// sensible defaults. Returns the number of inputs that were repaired.
    /// Call this AFTER all components are placed and wired, but BEFORE the
    /// final <c>NewSolution</c> so the injected data flows through.
    /// </summary>
    public static int SatisfyRequiredInputs(IEnumerable<IGH_DocumentObject> placed)
    {
        int fixes = 0;

        foreach (var obj in placed)
        {
            if (obj is not GH_Component comp) continue;

            foreach (var input in comp.Params.Input)
            {
                if (!NeedsDefault(input)) continue;
                if (InjectDefault(input)) fixes++;
            }
        }

        return fixes;
    }

    /// <summary>
    /// An input needs a default only when it is required (not optional) and has
    /// no upstream source feeding it. Inputs that are optional never raise the
    /// "failed to collect data" warning, so we leave them alone.
    /// </summary>
    private static bool NeedsDefault(IGH_Param input)
    {
        if (input.SourceCount > 0) return false; // already wired upstream
        if (input.Optional) return false;        // optional → no warning
        return true;
    }

    /// <summary>
    /// Set persistent data on a known scalar/geometry-primitive param type.
    /// Skips params that already carry persistent data (e.g. a component-defined
    /// default). Returns true if a default was injected.
    /// </summary>
    private static bool InjectDefault(IGH_Param input)
    {
        var path = new GH_Path(0);

        switch (input)
        {
            case Param_Point p:
                if (p.PersistentData.DataCount > 0) return false;
                p.PersistentData.Clear();
                p.PersistentData.Append(new GH_Point(Point3d.Origin), path);
                p.ExpireSolution(false);
                return true;

            case Param_Plane pl:
                if (pl.PersistentData.DataCount > 0) return false;
                pl.PersistentData.Clear();
                pl.PersistentData.Append(new GH_Plane(Plane.WorldXY), path);
                pl.ExpireSolution(false);
                return true;

            case Param_Vector v:
                if (v.PersistentData.DataCount > 0) return false;
                v.PersistentData.Clear();
                v.PersistentData.Append(new GH_Vector(Vector3d.ZAxis), path);
                v.ExpireSolution(false);
                return true;

            case Param_Number n:
                if (n.PersistentData.DataCount > 0) return false;
                n.PersistentData.Clear();
                n.PersistentData.Append(new GH_Number(1.0), path);
                n.ExpireSolution(false);
                return true;

            case Param_Integer i:
                if (i.PersistentData.DataCount > 0) return false;
                i.PersistentData.Clear();
                i.PersistentData.Append(new GH_Integer(1), path);
                i.ExpireSolution(false);
                return true;

            case Param_Boolean b:
                if (b.PersistentData.DataCount > 0) return false;
                b.PersistentData.Clear();
                b.PersistentData.Append(new GH_Boolean(true), path);
                b.ExpireSolution(false);
                return true;

            default:
                // Curve / Surface / Brep / Mesh / Geometry have no safe default.
                return false;
        }
    }
}
