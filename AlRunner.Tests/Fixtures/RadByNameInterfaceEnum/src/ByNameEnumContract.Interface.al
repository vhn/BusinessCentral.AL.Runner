// X in the by-name triple (see RadByNameInterfaceEnumTests). Every test in that class
// edits this file, so BcCompiler.DeltaCompile classifies it as `modified` and strips it
// from the packaged ModuleDefinition (ModuleDefinitionOps.WithoutObjects) — while V
// (ByNameKind.Enum.al) stays untouched and keeps naming this interface in its own
// serialized `ImplementedInterfaces`.
interface "ByName Enum Contract"
{
    procedure Label(): Text;
}
