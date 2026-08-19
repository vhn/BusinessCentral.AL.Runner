// X in the by-name triple (see RadByNameInterfaceCodeunitTests). Every test in that class
// edits this file, so BcCompiler.DeltaCompile classifies it as `modified` and strips it
// from the packaged ModuleDefinition (ModuleDefinitionOps.WithoutObjects) — while V
// (ByNameImpl.Codeunit.al) stays untouched and keeps naming this interface in its own
// serialized `ImplementedInterfaces`.
//
// No namespace, deliberately: an id-less object loses its namespace in a delta, and this
// fixture is not the place to re-test that (see RadIdlessObjectTests instead).
interface "ByName Contract"
{
    procedure Describe(): Text;
}
