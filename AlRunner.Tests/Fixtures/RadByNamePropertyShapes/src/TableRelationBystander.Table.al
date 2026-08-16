// TableRelation, V — the bystander of a shape RadByNameCleanShapesTests deliberately does NOT
// test. This triple exists to record WHY, so the gap is named rather than silently absent.
//
// Measured against this pipeline: a dangling `TableRelation` raises no diagnostic at all —
// neither one naming a table that does not exist, nor one naming a missing FIELD of a table
// that does. A cold compile therefore cannot tell a surviving by-name reference from a
// destroyed one, and this suite's oracle is "the delta says what a cold compile of the same
// tree says". Both sides would be silent either way, so the test would assert nothing while
// looking like coverage.
//
// Proving this shape needs an oracle the cold compile cannot give: e.g. reading the merged
// ModuleDefinition back and asserting the relation still names the target.
table 72126 "BN TableRelation Table"
{
    DataClassification = CustomerContent;

    fields
    {
        field(1; "Entry No."; Integer) { DataClassification = CustomerContent; }
        field(2; "Target Code"; Code[20])
        {
            DataClassification = CustomerContent;
            TableRelation = "BN TableRelation Target"."Code";
        }
    }

    keys
    {
        key(PK; "Entry No.") { Clustered = true; }
    }
}
